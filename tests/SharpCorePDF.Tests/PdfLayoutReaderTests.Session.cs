using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of a layout session: one open, several questions, without re-reading the document.
/// </summary>
public sealed partial class PdfLayoutReaderTests
{
    /// <summary>
    /// A session answers letters and words from the same open, and a second question does not change what
    /// the first one returned.
    /// </summary>
    [Fact]
    public async Task Session_answers_letters_and_words_from_one_open()
    {
        var source = Source(PdfContentFixtures.Text(), "text.pdf");

        await using var session = await Reader().OpenAsync(source, cancellationToken: Token);

        var letters = await session.ReadLettersAsync(PageRange.All, Token);
        var words = await session.ReadWordsAsync(PageRange.All, cancellationToken: Token);
        var again = await session.ReadWordsAsync(PageRange.All, cancellationToken: Token);

        Assert.Equal("text.pdf", session.SourceName);
        Assert.Equal("Hello WorldSecond line", string.Concat(letters.Items.Select(letter => letter.Value)));
        Assert.Equal(["Hello", "World", "Second", "line"], words.Items.Select(word => word.Text));
        Assert.Equal(words.Items.Select(word => word.Text), again.Items.Select(word => word.Text));
        Assert.Empty(letters.Warnings);
        Assert.Empty(words.Warnings);
    }

    /// <summary>
    /// Null word options on a session take the same defaults a one-shot read would, so callers can omit them.
    /// </summary>
    [Fact]
    public async Task Session_null_word_options_use_the_defaults()
    {
        var source = Source(PdfContentFixtures.Text(), "text.pdf");

        await using var session = await Reader().OpenAsync(source, cancellationToken: Token);

        var withNull = await session.ReadWordsAsync(PageRange.All, options: null, cancellationToken: Token);
        var withDefault = await session.ReadWordsAsync(
            PageRange.All,
            new PdfWordExtractionOptions(),
            cancellationToken: Token);

        Assert.Equal(
            withDefault.Items.Select(word => (word.Text, word.PageNumber, word.BoundingBox)),
            withNull.Items.Select(word => (word.Text, word.PageNumber, word.BoundingBox)));
    }

    /// <summary>
    /// A cancelled session read stops before grouping, so a caller that abandons a long range does not pay
    /// for the rest of the document.
    /// </summary>
    [Fact]
    public async Task Session_honours_cancellation_before_a_page_walk()
    {
        var source = Source(PdfContentFixtures.TextPages(), "pages.pdf");

        await using var session = await Reader().OpenAsync(source, cancellationToken: Token);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.ReadWordsAsync(PageRange.All, cancellationToken: cancelled.Token));
    }

    /// <summary>
    /// A disposed session refuses every question, so a caller that has finished with a document cannot
    /// accidentally keep reading it. Disposing twice is harmless.
    /// </summary>
    [Fact]
    public async Task Session_refuses_a_read_after_it_is_disposed()
    {
        var source = Source(PdfContentFixtures.Text(), "text.pdf");
        var session = await Reader().OpenAsync(source, cancellationToken: Token);

        session.Dispose();
        session.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.ReadWordsAsync(PageRange.All, cancellationToken: Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.ReadLettersAsync(PageRange.All, Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.ReadBlocksAsync(PageRange.All, cancellationToken: Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.ReadHyperlinksAsync(PageRange.All, Token));
    }

    /// <summary>
    /// Opening a session of a missing source is refused the same way a one-shot read is.
    /// </summary>
    [Fact]
    public async Task Session_open_refuses_a_null_source() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Reader().OpenAsync(null!, cancellationToken: Token));
}