using System.Runtime.InteropServices;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the conveniences that materialise a payload.
/// </summary>
/// <remarks>
/// <para>
/// Both members return the bytes the copy route wrote. What these tests pin is not the bytes — a copy would
/// return those too — but their provenance: the result is a view of the buffer the destination stream owns,
/// so materialising a payload costs one array of its size rather than two. <c>ToArray</c> hands back a
/// different array, and <see cref="Assert.Same(object?, object?)"/> is what notices, which is why the
/// assertion is identity rather than content.
/// </para>
/// <para>
/// The two members that write a payload to a file are tested in <c>PdfContentReaderExtensionsTests.Save.cs</c>,
/// the second part of this class: they share the payload and the stub, and differ only in what can be
/// observed — bytes in a file rather than a view of an array.
/// </para>
/// </remarks>
public sealed partial class PdfContentReaderExtensionsTests
{
    /// <summary>Four hundred kilobytes, the size at which a caller reaches for these members at all.</summary>
    private const int PayloadLength = 400_000;

    /// <summary>The payload the stub writes, patterned so that a shifted window is visible.</summary>
    private static readonly byte[] Payload = CreatePayload();

    /// <summary>The stub answers from the record it is handed, so the source is never opened.</summary>
    private static readonly IPdfContentSource Source = new PdfByteArraySource(Payload, "fixture.pdf");

    /// <summary>An image's payload comes back as the buffer the copy route filled, not a second array.</summary>
    [Fact]
    public async Task Materialises_an_image_as_a_view_of_the_buffer()
    {
        var reader = new RecordingContentReader();
        var image = new PdfImageInfo { Id = "img-1", ByteLength = PayloadLength };

        var payload = await reader.ReadImageDataAsync(
            Source,
            image,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertIsTheBufferTheCopyFilled(reader.LastBuffer, payload);
    }

    /// <summary>An embedded file's payload comes back on the same terms as an image's.</summary>
    [Fact]
    public async Task Materialises_an_embedded_file_as_a_view_of_the_buffer()
    {
        var reader = new RecordingContentReader();
        var file = new PdfEmbeddedFileInfo { Id = "file-1", Name = "data.bin", ByteLength = PayloadLength };

        var payload = await reader.ReadEmbeddedFileDataAsync(
            Source,
            file,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertIsTheBufferTheCopyFilled(reader.LastBuffer, payload);
    }

    /// <summary>
    /// A payload whose length was not known — a damaged or unsized stream, which asks for a stream that grows
    /// rather than one sized to the payload — is still exactly the bytes written, not the capacity beyond them.
    /// </summary>
    [Fact]
    public async Task Materialises_a_payload_whose_length_was_not_known()
    {
        var reader = new RecordingContentReader();
        var image = new PdfImageInfo { Id = "img-1" };

        var payload = await reader.ReadImageDataAsync(
            Source,
            image,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertGrewToHoldThePayload(reader.LastBuffer, payload);
    }

    /// <summary>
    /// A stated length that cannot be a size — a record whose length was never filled in, or was filled in
    /// wrongly — asks for a stream that grows rather than one sized to a number that would be refused. The
    /// payload still arrives, and it is still exactly the bytes written rather than the capacity beyond them.
    /// </summary>
    /// <remarks>
    /// <see cref="PdfImageInfo.ByteLength"/> and <see cref="PdfEmbeddedFileInfo.ByteLength"/> are stated by the
    /// document, so a damaged record reaches this member as a negative length. Sizing the destination from it
    /// would throw out of <see cref="MemoryStream"/>'s constructor before the payload was ever copied; what
    /// the member does instead is fall back to a stream that grows.
    /// </remarks>
    [Fact]
    public async Task Materialises_a_payload_whose_stated_length_is_impossible()
    {
        var reader = new RecordingContentReader();

        var imagePayload = await reader.ReadImageDataAsync(
            Source,
            new PdfImageInfo { Id = "img-1", ByteLength = -1 },
            cancellationToken: TestContext.Current.CancellationToken);

        AssertGrewToHoldThePayload(reader.LastBuffer, imagePayload);

        var filePayload = await reader.ReadEmbeddedFileDataAsync(
            Source,
            new PdfEmbeddedFileInfo { Id = "file-1", Name = "data.bin", ByteLength = -1 },
            cancellationToken: TestContext.Current.CancellationToken);

        AssertGrewToHoldThePayload(reader.LastBuffer, filePayload);
    }

    /// <summary>A materialising read handed nothing says so, rather than failing further down.</summary>
    [Fact]
    public async Task Refuses_a_missing_argument()
    {
        var reader = new RecordingContentReader();
        var image = new PdfImageInfo { Id = "img-1", ByteLength = PayloadLength };
        var file = new PdfEmbeddedFileInfo { Id = "file-1", Name = "data.bin", ByteLength = PayloadLength };

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reader.ReadImageDataAsync(null!, image, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reader.ReadImageDataAsync(Source, null!, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reader.ReadEmbeddedFileDataAsync(null!, file, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reader.ReadEmbeddedFileDataAsync(Source, null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>The single-page convenience answers the page it was asked for, out of a longer document.</summary>
    [Fact]
    public async Task Reads_the_detail_of_one_page()
    {
        var detail = await Reader().ReadPageDetailAsync(
            new PdfByteArraySource(PdfContentFixtures.Images(), "images.pdf"),
            2,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(detail);
        Assert.Equal(2, detail.PageNumber);
        Assert.Equal(4, detail.Images.Count);
    }

    /// <summary>A page past the last one is answered with nothing, rather than with the last page there is.</summary>
    [Fact]
    public async Task Reads_no_detail_past_the_last_page()
    {
        var detail = await Reader().ReadPageDetailAsync(
            new PdfByteArraySource(PdfContentFixtures.Images(), "images.pdf"),
            99,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(detail);
    }

    /// <summary>A reader over the real engine, for the members the stub above cannot stand in for.</summary>
    /// <returns>A reader that reads the fixture documents in memory.</returns>
    private static PdfContentReaderService Reader() => new(new PdfPageContentReader());

    /// <summary>
    /// Asserts the payload is a view of the buffer the copy wrote into: that array, from its first byte, and
    /// exactly the bytes written — so neither a copy of the payload nor a view of the stream's capacity passes.
    /// </summary>
    /// <param name="buffer">The buffer the destination stream owned, as the stub recorded it.</param>
    /// <param name="payload">What the convenience returned.</param>
    private static void AssertIsTheBufferTheCopyFilled(byte[]? buffer, ReadOnlyMemory<byte> payload)
    {
        Assert.NotNull(buffer);
        Assert.True(MemoryMarshal.TryGetArray(payload, out var segment), "the payload is not array-backed");
        Assert.Same(buffer, segment.Array);
        Assert.Equal(0, segment.Offset);
        Assert.Equal(PayloadLength, segment.Count);
        Assert.Equal(PayloadLength, payload.Length);
        Assert.Equal(PayloadLength, segment.Array!.Length);
        Assert.Equal(Payload, payload.ToArray());
    }

    /// <summary>
    /// Asserts a payload came back as a view of a buffer that had to grow to hold it — the same array, from its
    /// first byte, exactly the bytes written — so neither a copy nor a view of the grown capacity passes.
    /// </summary>
    /// <param name="buffer">The buffer the destination stream owned, as the stub recorded it.</param>
    /// <param name="payload">What the convenience returned.</param>
    private static void AssertGrewToHoldThePayload(byte[]? buffer, ReadOnlyMemory<byte> payload)
    {
        Assert.NotNull(buffer);
        Assert.True(MemoryMarshal.TryGetArray(payload, out var segment), "the payload is not array-backed");
        Assert.Same(buffer, segment.Array);
        Assert.Equal(0, segment.Offset);
        Assert.True(segment.Array!.Length > PayloadLength, "the stream should have grown past the payload");
        Assert.Equal(PayloadLength, segment.Count);
        Assert.Equal(Payload, payload.ToArray());
    }

    /// <summary>Builds the payload every test writes and compares against.</summary>
    /// <returns>Four hundred thousand bytes, cycling through a prime so no window repeats.</returns>
    private static byte[] CreatePayload()
    {
        var payload = new byte[PayloadLength];

        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index % 251);
        }

        return payload;
    }

    /// <summary>
    /// A reader that answers the two copy routes and nothing else: it writes the payload into the stream it is
    /// handed, and keeps that stream's buffer, so a test can tell a view from a copy.
    /// </summary>
    private sealed class RecordingContentReader : IPdfContentReader
    {
        /// <summary>The bounded buffer the real copy route writes through.</summary>
        private const int ChunkSize = 64 * 1024;

        /// <summary>
        /// Gets the buffer of the destination the last copy wrote into, or <see langword="null"/> when that
        /// destination was a file rather than a memory stream.
        /// </summary>
        public byte[]? LastBuffer { get; private set; }

        /// <inheritdoc/>
        public Task CopyImageDataAsync(
            IPdfContentSource source,
            PdfImageInfo image,
            Stream destination,
            string? password = null,
            CancellationToken cancellationToken = default) => Write(destination);

        /// <inheritdoc/>
        public Task CopyEmbeddedFileDataAsync(
            IPdfContentSource source,
            PdfEmbeddedFileInfo file,
            Stream destination,
            string? password = null,
            CancellationToken cancellationToken = default) => Write(destination);

        /// <inheritdoc/>
        public Task CopyImageMaskDataAsync(
            IPdfContentSource source,
            PdfImageMask mask,
            Stream destination,
            string? password = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc/>
        public Task<PdfReadResult<PdfImageInfo>> ReadImagesAsync(
            IPdfContentSource source,
            PageRange range,
            string? password = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc/>
        public Task<PdfReadResult<PdfTextBlock>> ReadTextAsync(
            IPdfContentSource source,
            PageRange range,
            string? password = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc/>
        public Task<PdfReadResult<PdfAnnotationInfo>> ReadAnnotationsAsync(
            IPdfContentSource source,
            PageRange range,
            string? password = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc/>
        public Task<PdfReadResult<PdfFormFieldInfo>> ReadFormFieldsAsync(
            IPdfContentSource source,
            string? password = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc/>
        public Task<PdfXmpResult> ReadMetadataAsync(
            IPdfContentSource source,
            string? password = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc/>
        public Task<PdfReadResult<PdfEmbeddedFileInfo>> ReadEmbeddedFilesAsync(
            IPdfContentSource source,
            string? password = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc/>
        public Task<PdfReadResult<PdfOutlineItem>> ReadOutlineAsync(
            IPdfContentSource source,
            string? password = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc/>
        public IAsyncEnumerable<PdfPageDetail> ReadPageDetailsAsync(
            IPdfContentSource source,
            PageRange range,
            string? password = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>Writes the payload into the destination and remembers the buffer behind it.</summary>
        /// <param name="destination">The stream the convenience created and handed over.</param>
        /// <returns>A completed task, so the convenience awaits it exactly as it awaits a real copy.</returns>
        private Task Write(Stream destination)
        {
            // Written a bounded buffer at a time, the way the copy route writes, so a destination that was not
            // sized to the payload grows by doubling instead of landing exactly on it.
            for (var offset = 0; offset < Payload.Length; offset += ChunkSize)
            {
                destination.Write(Payload, offset, Math.Min(ChunkSize, Payload.Length - offset));
            }

            LastBuffer = destination is MemoryStream memory ? memory.GetBuffer() : null;

            return Task.CompletedTask;
        }
    }
}
