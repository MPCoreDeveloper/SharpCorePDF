using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Sources;

/// <summary>
/// A PDF document that is already in memory.
/// </summary>
/// <remarks>
/// Every read returns a fresh, independent stream over the same bytes, which is what the
/// <see cref="IPdfContentSource"/> contract promises.
/// </remarks>
public sealed class PdfByteArraySource : IPdfContentSource
{
    private readonly byte[] bytes;

    /// <summary>Initializes a new instance of the <see cref="PdfByteArraySource"/> class.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <param name="displayName">The name used in messages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bytes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="displayName"/> is null, empty or whitespace.</exception>
    public PdfByteArraySource(byte[] bytes, string displayName)
    {
        this.bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
    }

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public long? Length => bytes.LongLength;

    /// <summary>Gets the bytes of the document.</summary>
    public ReadOnlyMemory<byte> Bytes => bytes;

    /// <inheritdoc />
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<Stream>(new MemoryStream(bytes, writable: false));
    }

    /// <summary>Formats the source as its display name followed by its size.</summary>
    /// <returns>The formatted source.</returns>
    public override string ToString() => $"{DisplayName} ({bytes.Length} bytes)";
}

/// <summary>
/// A PDF document that is read from a stream.
/// </summary>
/// <remarks>
/// <para>
/// The content of the stream is captured the first time it is opened and kept in memory, because the
/// contract of <see cref="IPdfContentSource"/> is that every read is independent: a stream that is
/// already positioned, or that cannot be read twice, could not honour that. This keeps the source
/// usable for an inspection followed by a merge.
/// </para>
/// <para>
/// The capture is a pooled buffer and turns into one exactly sized array, so a stream-backed source costs
/// the bytes of the document and not a second copy of them while the stream is being read.
/// </para>
/// </remarks>
public sealed class PdfStreamSource : IPdfContentSource
{
    /// <summary>How much is read from the stream at a time while it is captured.</summary>
    private const int ReadChunkSize = 64 * 1024;

    /// <summary>The largest buffer a capture reserves up front, so a huge stream is not reserved twice.</summary>
    private const int MaximumInitialCaptureCapacity = 1024 * 1024;

    private readonly Stream content;
    private readonly Lock gate = new();

    /// <summary>Where this source first met the stream, when the stream is able to go back there.</summary>
    private readonly long? startPosition;

    private Task<byte[]?>? capture;

    /// <summary>How much a cancelled copy took out of a stream that cannot be put back.</summary>
    private long consumed;

    /// <summary>Initializes a new instance of the <see cref="PdfStreamSource"/> class.</summary>
    /// <param name="content">The stream that holds the document; it is read once and never disposed.</param>
    /// <param name="displayName">The name used in messages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="displayName"/> is null, empty or whitespace.</exception>
    public PdfStreamSource(Stream content, string displayName)
    {
        this.content = content ?? throw new ArgumentNullException(nameof(content));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
        startPosition = PositionOf(content);
    }

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public long? Length
    {
        get
        {
            var current = capture;

            if (current is { IsCompletedSuccessfully: true, Result: { } bytes })
            {
                return bytes.LongLength;
            }

            return content.CanSeek ? content.Length : null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The capture is shared by every caller: the stream is read once, no matter how often the source is
    /// opened, and concurrent opens wait for the same read instead of fighting over the position of the
    /// stream.
    /// </para>
    /// <para>
    /// Cancelling the caller that starts the copy ends the copy itself, rather than releasing that
    /// caller's wait while the read runs on in the background, and a copy that ended that way is not
    /// kept: a caller who did not cancel starts a new one. A stream that can seek is put back where this
    /// source first met it, so that the new copy reads the whole document. A stream that cannot seek has
    /// already given up the bytes a cancelled copy took from it, so a later open fails with
    /// <see cref="InvalidOperationException"/> instead of quietly reading a truncated document.
    /// </para>
    /// </remarks>
    public async ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pending = Capture(cancellationToken);
            var bytes = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (bytes is null)
            {
                // The copy was ended by the caller that started it and this caller did not cancel, so it
                // starts a new one rather than inheriting a cancellation that was never its own.
                continue;
            }

            return new MemoryStream(bytes, writable: false);
        }
    }

    /// <summary>Formats the source as its display name followed by its size when it is known.</summary>
    /// <returns>The formatted source.</returns>
    public override string ToString() =>
        Length is { } length ? $"{DisplayName} ({length} bytes)" : DisplayName;

    /// <summary>Gets the capture that is already running, and starts one when there is none to join.</summary>
    /// <param name="cancellationToken">The token of the caller that starts the capture.</param>
    /// <returns>The capture, which yields nothing when its caller cancelled it.</returns>
    private Task<byte[]?> Capture(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (capture is null || Abandoned(capture))
            {
                if (capture is not null)
                {
                    Restart();
                }

                capture = Copy(cancellationToken);
            }

            return capture;
        }
    }

    /// <summary>Gets whether a capture ended because its caller cancelled it.</summary>
    /// <param name="pending">The capture to look at.</param>
    /// <returns><see langword="true"/> when the capture holds no bytes and never will.</returns>
    private static bool Abandoned(Task<byte[]?> pending) =>
        pending.IsCompletedSuccessfully && pending.Result is null;

    /// <summary>Puts the stream back where this source first met it, so a cancelled copy can be retried.</summary>
    /// <exception cref="InvalidOperationException">
    /// The stream cannot seek and a cancelled copy had already taken bytes out of it.
    /// </exception>
    private void Restart()
    {
        if (startPosition is { } position)
        {
            content.Seek(position, SeekOrigin.Begin);
            consumed = 0;
            return;
        }

        if (consumed == 0)
        {
            // The cancelled copy did not get as far as reading a byte, so there is nothing to undo.
            return;
        }

        throw new InvalidOperationException(
            $"The stream of {DisplayName} cannot be read again: a cancelled copy took {consumed} byte(s) "
            + "out of it, and a stream that cannot seek cannot be put back.");
    }

    /// <summary>Reads the stream into memory, once.</summary>
    /// <param name="cancellationToken">The token the read observes.</param>
    /// <returns>The bytes of the document, or nothing when the caller cancelled the read.</returns>
    /// <remarks>
    /// The token of the caller who starts the copy is the one the read observes, so cancelling ends the
    /// copy itself instead of releasing that caller's wait while the copy runs on in the background. The
    /// copy is not pushed onto the thread pool either: reading a stream is I/O, so it is asynchronous of
    /// its own accord, and a source that blocks would only be moved onto a pool thread to look
    /// asynchronous. Nothing is returned when the read was cancelled, which is how the source learns that
    /// this copy must not be kept; the task is deliberately not asked to carry the cancellation, so the
    /// behaviour does not depend on how a stream reports its own.
    /// </remarks>
    private async Task<byte[]?> Copy(CancellationToken cancellationToken)
    {
        using var buffer = new PooledByteBuffer(InitialCaptureCapacity());

        try
        {
            while (true)
            {
                var read = await content
                    .ReadAsync(buffer.GetMemory(ReadChunkSize), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                buffer.Advance(read);
                consumed += read;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return buffer.ToExactArray();
    }

    /// <summary>Gets the capacity a capture reserves, when the stream can say how much it still holds.</summary>
    /// <returns>The number of bytes to reserve up front, or zero when it is not known.</returns>
    private int InitialCaptureCapacity()
    {
        if (startPosition is not { } position)
        {
            return 0;
        }

        try
        {
            return (int)Math.Clamp(content.Length - position, 0, MaximumInitialCaptureCapacity);
        }
        catch (NotSupportedException)
        {
            // A stream may claim to be seekable and still refuse to report its length; the buffer grows
            // by itself in that case.
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>Gets the position of a stream, when it can seek and is willing to say where it is.</summary>
    /// <param name="content">The stream to ask.</param>
    /// <returns>The position, or <see langword="null"/> when it is not known.</returns>
    private static long? PositionOf(Stream content)
    {
        if (!content.CanSeek)
        {
            return null;
        }

        try
        {
            return content.Position;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
