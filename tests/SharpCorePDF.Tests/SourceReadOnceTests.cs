using SharpCorePDF.Application.Ports;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of how many times a use case reads its source, which is the whole point of
/// [ADR-004](../../../docs/adr/0004-buffer-model.md): a merge and an edit read the document once, and only a
/// reader that cannot hand its bytes back makes them read it twice.
/// </summary>
/// <remarks>
/// The source used here is readable exactly once — a stream that is not seekable and that answers every read
/// after the end with nothing — so a second read cannot be papered over by a rewind. The merge and the edit
/// therefore fail on revert of the change the record describes, which is what makes this the test that pins
/// it rather than an allocation figure that could move on its own.
/// </remarks>
public sealed class SourceReadOnceTests
{
    private static readonly DateTimeOffset Moment = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The engine reads a merged source out of the bytes the inspection produced.</summary>
    [Fact]
    public async Task A_merge_reads_its_source_once()
    {
        var source = new CountingSource(PdfContentFixtures.ObjectGraph(), "once.pdf");
        var plan = MergePlan.Create().AddSource(PdfSource.Create(source));
        var merger = new PdfMergerService(new PdfDocumentReader(), new PdfMergeEngine());

        using var destination = new MemoryStream();

        var result = await merger.MergeAsync(plan, destination, Token);

        Assert.Equal(1, source.Passes);
        Assert.Equal(1, result.SourceCount);
        Assert.True(destination.Length > 0);
    }

    /// <summary>The engine reads an edited source out of the bytes the inspection produced.</summary>
    [Fact]
    public async Task An_edit_reads_its_source_once()
    {
        var source = new CountingSource(PdfContentFixtures.ObjectGraph(), "once.pdf");
        var plan = PdfEditPlan
            .Create(PdfEditOutputOptions.Default with { ModificationDate = Moment, DocumentId = "read-once" })
            .RotatePage(1, PageRotation.Clockwise90);
        var editor = new PdfDocumentEditorService(new PdfDocumentReader(), new PdfEditEngine());

        var bytes = await editor.ApplyToArrayAsync(source, plan, cancellationToken: Token);

        Assert.Equal(1, source.Passes);
        Assert.NotEmpty(bytes);
    }

    /// <summary>
    /// A reader that only reads structure still works, and the source is read twice — which is the cost
    /// ADR-004 removes for the reader this library ships, kept so a third-party reader is not broken.
    /// </summary>
    [Fact]
    public async Task A_reader_without_the_bytes_port_reads_its_source_twice()
    {
        var source = new ReReadableSource(PdfContentFixtures.ObjectGraph(), "twice.pdf");
        var plan = MergePlan.Create().AddSource(PdfSource.Create(source));
        var merger = new PdfMergerService(new StructureOnlyReader(new PdfDocumentReader()), new PdfMergeEngine());

        using var destination = new MemoryStream();

        await merger.MergeAsync(plan, destination, Token);

        Assert.Equal(2, source.Passes);
        Assert.True(destination.Length > 0);
    }

    /// <summary>A source whose stream can be drained once, and which counts how often that happened.</summary>
    private sealed class CountingSource(byte[] content, string displayName) : IPdfContentSource
    {
        /// <summary>Gets the number of times the content was read to its end.</summary>
        public int Passes { get; private set; }

        /// <inheritdoc />
        public string DisplayName => displayName;

        /// <inheritdoc />
        public long? Length => content.Length;

        /// <inheritdoc />
        public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new CountingStream(content, () => Passes++));
    }

    /// <summary>
    /// A source whose stream can be read and rewound, the way a file can, and which counts every pass
    /// through its content.
    /// </summary>
    private sealed class ReReadableSource(byte[] content, string displayName) : IPdfContentSource
    {
        /// <summary>Gets the number of times the content was read to its end.</summary>
        public int Passes { get; private set; }

        /// <inheritdoc />
        public string DisplayName => displayName;

        /// <inheritdoc />
        public long? Length => content.Length;

        /// <inheritdoc />
        public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new ReReadableStream(content, () => Passes++));
    }

    /// <summary>
    /// A seekable stream over bytes that reports the first read past its end. It is deliberately not a
    /// <see cref="MemoryStream"/>: a memory stream that owns its whole array is the fast path a reader takes
    /// without reading anything, and then there would be nothing to count.
    /// </summary>
    private sealed class ReReadableStream(byte[] content, Action onDrained) : Stream
    {
        private int position;
        private bool sawData;

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => true;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => content.Length;

        /// <inheritdoc />
        public override long Position
        {
            get => position;
            set => position = checked((int)value);
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        /// <inheritdoc />
        public override int Read(Span<byte> buffer)
        {
            if (position >= content.Length)
            {
                if (sawData)
                {
                    sawData = false;
                    onDrained();
                }

                return 0;
            }

            var taken = Math.Min(buffer.Length, content.Length - position);

            content.AsSpan(position, taken).CopyTo(buffer);
            position += taken;
            sawData = true;

            return taken;
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            position = checked((int)(origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => position + offset,
                _ => content.Length + offset,
            }));

            return position;
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A non-seekable stream over bytes, which reports every pass through them.</summary>
    private sealed class CountingStream(byte[] content, Action onDrained) : Stream
    {
        private int position;
        private bool sawData;

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        /// <inheritdoc />
        public override int Read(Span<byte> buffer)
        {
            if (position >= content.Length)
            {
                if (sawData)
                {
                    sawData = false;
                    onDrained();
                }

                return 0;
            }

            var taken = Math.Min(buffer.Length, content.Length - position);

            content.AsSpan(position, taken).CopyTo(buffer);
            position += taken;
            sawData = true;

            return taken;
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A reader exposed as the narrower port, the way a third-party implementation would be.</summary>
    private sealed class StructureOnlyReader(IPdfDocumentReader inner) : IPdfDocumentReader
    {
        /// <inheritdoc />
        public Task<PdfDocumentInfo> ReadAsync(
            string sourceName,
            Stream content,
            string? password = null,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(sourceName, content, password, cancellationToken);
    }
}
