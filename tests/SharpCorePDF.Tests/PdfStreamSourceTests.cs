using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the promise a content source makes to the engine: every read is independent, a stream is read
/// once, and a cancellation ends the read instead of leaving behind a source that is quietly broken.
/// </summary>
public sealed class PdfStreamSourceTests
{
    /// <summary>A document that is small enough to compare byte for byte and real enough to read.</summary>
    private static readonly byte[] Document =
        "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    /// <summary>Every read is independent, and the stream itself is read exactly once.</summary>
    [Fact]
    public async Task Opens_independent_streams_over_a_stream_that_is_read_once()
    {
        using var content = new GatedStream(Document);
        content.Open();
        var source = new PdfStreamSource(content, "document.pdf");

        var first = await source.OpenReadAsync(TestContext.Current.CancellationToken);
        var served = content.Reads;
        var second = await source.OpenReadAsync(TestContext.Current.CancellationToken);

        Assert.NotSame(first, second);
        Assert.Equal(Document, await DrainAsync(first));
        Assert.Equal(Document, await DrainAsync(second));
        Assert.Equal(served, content.Reads);
        Assert.Equal(Document.LongLength, source.Length);
        Assert.Equal("document.pdf", source.DisplayName);
        Assert.Equal($"document.pdf ({Document.Length} bytes)", source.ToString());
    }

    /// <summary>Cancelling the copy ends the read, and the source is left usable for the next caller.</summary>
    [Fact]
    public async Task Cancelling_the_copy_ends_the_read_and_a_later_caller_starts_a_new_one()
    {
        using var content = new GatedStream(Document);
        using var cancellation = new CancellationTokenSource();
        var source = new PdfStreamSource(content, "document.pdf");

        var cancelled = source.OpenReadAsync(cancellation.Token);
        await content.Entered;
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);

        // The copy that was cancelled is not kept, so the next caller reads the document again and gets
        // all of it rather than the part the cancelled copy had already taken.
        content.Open();
        Assert.Equal(Document, await DrainAsync(await source.OpenReadAsync(TestContext.Current.CancellationToken)));
    }

    /// <summary>A caller who did not cancel is not punished for the cancellation of the one who did.</summary>
    [Fact]
    public async Task Cancelling_a_waiter_leaves_the_shared_copy_running()
    {
        using var content = new GatedStream(Document);
        using var cancellation = new CancellationTokenSource();
        var source = new PdfStreamSource(content, "document.pdf");

        var first = source.OpenReadAsync(TestContext.Current.CancellationToken);
        await content.Entered;

        var waiting = source.OpenReadAsync(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);

        content.Open();
        Assert.Equal(Document, await DrainAsync(await first));

        // The copy was shared rather than restarted: the stream was still read only once.
        var served = content.Reads;
        Assert.Equal(Document, await DrainAsync(await source.OpenReadAsync(TestContext.Current.CancellationToken)));
        Assert.Equal(served, content.Reads);
    }

    /// <summary>
    /// A stream that cannot seek and that a cancelled copy already took bytes from cannot be read again,
    /// and says so instead of handing back the remainder of a document.
    /// </summary>
    [Fact]
    public async Task Refuses_to_read_a_consumed_stream_that_cannot_seek_again()
    {
        using var content = new GatedStream(Document, seekable: false);
        using var cancellation = new CancellationTokenSource();
        var source = new PdfStreamSource(content, "document.pdf");

        var cancelled = source.OpenReadAsync(cancellation.Token);
        await content.Entered;
        content.Open(reads: 1);
        await content.Copied;
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await source.OpenReadAsync(TestContext.Current.CancellationToken));

        Assert.Contains("document.pdf", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A stream that cannot seek is still captured whole when nothing goes wrong.</summary>
    [Fact]
    public async Task Captures_a_stream_that_cannot_seek_whole()
    {
        using var content = new GatedStream(Document, seekable: false);
        content.Open();
        var source = new PdfStreamSource(content, "document.pdf");

        Assert.Null(source.Length);

        var opened = await source.OpenReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Document, await DrainAsync(opened));
        Assert.Equal(Document.LongLength, source.Length);
    }

    /// <summary>Reads a stream to its end, the way the reader does.</summary>
    /// <param name="stream">The stream to read.</param>
    /// <returns>The bytes of the stream.</returns>
    private static async Task<byte[]> DrainAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, TestContext.Current.CancellationToken);
        return buffer.ToArray();
    }

    /// <summary>
    /// A stream that hands out its content one chunk at a time, and only when a test opens the gate, so
    /// that a capture can be watched while it is in flight.
    /// </summary>
    private sealed class GatedStream : Stream
    {
        private readonly byte[] content;
        private readonly int chunk;
        private readonly SemaphoreSlim gate = new(0, int.MaxValue);
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource copied = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int position;
        private int reads;

        /// <summary>Initializes a new instance of the <see cref="GatedStream"/> class.</summary>
        /// <param name="content">The bytes the stream hands out.</param>
        /// <param name="seekable">Whether the stream can be repositioned.</param>
        public GatedStream(byte[] content, bool seekable = true)
        {
            this.content = content;
            CanSeek = seekable;

            // Three chunks, so that a test can stop a capture between them.
            chunk = Math.Max(1, content.Length / 3);
        }

        /// <summary>Gets a task that completes when the stream has been asked for bytes.</summary>
        public Task Entered => entered.Task;

        /// <summary>Gets a task that completes when a read has handed bytes over.</summary>
        public Task Copied => copied.Task;

        /// <summary>Gets how often the stream has handed bytes over.</summary>
        public int Reads => Volatile.Read(ref reads);

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek { get; }

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => content.Length;

        /// <inheritdoc />
        public override long Position
        {
            get => position;
            set => position = (int)value;
        }

        /// <summary>Lets the reads that are waiting for the gate through.</summary>
        /// <param name="reads">The number of reads to let through.</param>
        public void Open(int reads = 4)
        {
            for (var index = 0; index < reads; index++)
            {
                gate.Release();
            }
        }

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var taken = Math.Min(Math.Min(buffer.Length, chunk), content.Length - position);

            if (taken == 0)
            {
                // The end of the document is reached without another wait, so a capture finishes.
                return 0;
            }

            entered.TrySetResult();
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            content.AsSpan(position, taken).CopyTo(buffer.Span);
            position += taken;
            Interlocked.Increment(ref reads);
            copied.TrySetResult();

            return taken;
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            position = origin == SeekOrigin.Begin ? (int)offset : position + (int)offset;
            return position;
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                gate.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
