using System.Collections.Immutable;
using System.Globalization;
using SharpCorePDF.Domain.Ports;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// One stationery template and the pages it covers: the background a page is drawn over, and how that
/// background is placed on it.
/// </summary>
/// <remarks>
/// A stationery template is a page of another document, recorded under the pages of the one being
/// edited. A <see langword="null"/> <see cref="Source"/> says the pages are left without stationery,
/// which is how a caller excludes a page from a default template.
/// </remarks>
public sealed record PdfStationeryAssignment
{
    /// <summary>
    /// Gets the stationery document, or <see langword="null"/> to leave the pages without stationery.
    /// </summary>
    public IPdfContentSource? Source { get; init; }

    /// <summary>Gets the one-based pages the template is recorded under.</summary>
    public PageRange Pages { get; init; } = PageRange.All;

    /// <summary>Gets how the stationery page is placed and composited.</summary>
    public PdfOverlayOptions Options { get; init; } = PdfOverlayOptions.Default;

    /// <summary>Gets the user or owner password of <see cref="Source"/>, when it is encrypted.</summary>
    public string? Password { get; init; }
}

/// <summary>
/// The stationery plan of an edit: an ordered set of templates, each covering a page range, plus a
/// default template for the pages no assignment covers.
/// </summary>
/// <remarks>
/// <para>
/// This is the aggregate behind <see cref="Aggregates.PdfEditPlan.ApplyStationery"/>: it describes
/// which background goes behind which page, and the editor expands it into the underlay layers it
/// already records. A plan is immutable, so it can be built once, shared and applied to several
/// documents:
/// </para>
/// <code>
/// var stationery = PdfStationery.Create()
///     .Assign(new PdfFileSource("letterhead.pdf"), PageRange.Single(1))
///     .AssignDefault(new PdfFileSource("footer.pdf"));
///
/// var plan = PdfEditPlan.Create().ApplyStationery(stationery);
/// </code>
/// <para>
/// Assignments are tried last first, so a later, narrower assignment overrides an earlier, broader
/// one. A page no assignment covers receives <see cref="Default"/>; a page whose resolved assignment
/// has no source is left alone.
/// </para>
/// <para>
/// A one-page stationery document repeats across the pages it covers. A multi-page document maps one
/// page for one page, starting at the first page of the assignment, and a range that reaches past its
/// last page is refused when the plan is executed.
/// </para>
/// </remarks>
public sealed record PdfStationery
{
    /// <summary>Gets the templates in the order they were added.</summary>
    public ImmutableArray<PdfStationeryAssignment> Assignments { get; init; } = [];

    /// <summary>Gets the template recorded under every page an assignment does not cover.</summary>
    public PdfStationeryAssignment? Default { get; init; }

    /// <summary>Creates stationery with no assignments and no default.</summary>
    /// <returns>The new stationery.</returns>
    public static PdfStationery Create() => new();

    /// <summary>Records <paramref name="source"/> under the pages of <paramref name="pages"/>.</summary>
    /// <param name="source">The stationery document; a one-page document repeats across the range.</param>
    /// <param name="pages">The one-based pages the template covers.</param>
    /// <param name="options">How the template is placed and composited; defaults to <see cref="PdfOverlayOptions.Default"/>.</param>
    /// <param name="password">The user or owner password of <paramref name="source"/>, when it is encrypted.</param>
    /// <returns>The new stationery.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public PdfStationery Assign(
        IPdfContentSource source,
        PageRange pages,
        PdfOverlayOptions? options = null,
        string? password = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        return this with
        {
            Assignments = Assignments.Add(new PdfStationeryAssignment
            {
                Source = source,
                Pages = pages,
                Options = options ?? PdfOverlayOptions.Default,
                Password = password,
            }),
        };
    }

    /// <summary>Records <paramref name="source"/> under every page an assignment does not cover.</summary>
    /// <param name="source">The default stationery document; a one-page document repeats across the pages.</param>
    /// <param name="options">How the template is placed and composited; defaults to <see cref="PdfOverlayOptions.Default"/>.</param>
    /// <param name="password">The user or owner password of <paramref name="source"/>, when it is encrypted.</param>
    /// <returns>The new stationery.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public PdfStationery AssignDefault(
        IPdfContentSource source,
        PdfOverlayOptions? options = null,
        string? password = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        return this with
        {
            Default = new PdfStationeryAssignment
            {
                Source = source,
                Pages = PageRange.All,
                Options = options ?? PdfOverlayOptions.Default,
                Password = password,
            },
        };
    }

    /// <summary>Leaves the pages of <paramref name="pages"/> without stationery.</summary>
    /// <param name="pages">The one-based pages that receive no template, even when a default exists.</param>
    /// <returns>The new stationery.</returns>
    public PdfStationery AssignNone(PageRange pages) =>
        this with
        {
            Assignments = Assignments.Add(new PdfStationeryAssignment
            {
                Source = null,
                Pages = pages,
                Options = PdfOverlayOptions.Default,
            }),
        };

    /// <summary>Resolves the template that covers <paramref name="pageNumber"/>.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <returns>
    /// The last assignment that covers the page, the default when no assignment does, or
    /// <see langword="null"/> when neither exists. An assignment with no source means the page is left
    /// without stationery, so callers should check <see cref="PdfStationeryAssignment.Source"/>.
    /// </returns>
    public PdfStationeryAssignment? ResolveForPage(int pageNumber)
    {
        for (var index = Assignments.Length - 1; index >= 0; index--)
        {
            var assignment = Assignments[index];

            if (assignment.Pages.Contains(pageNumber))
            {
                return assignment;
            }
        }

        return Default;
    }

    /// <summary>Checks every invariant that can be verified without reading the stationery documents.</summary>
    /// <returns>Every problem found, or an empty list when the stationery is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Assignments.Length == 0 && Default is null)
        {
            errors.Add("the stationery has neither a page assignment nor a default template.");
        }

        for (var index = 0; index < Assignments.Length; index++)
        {
            var assignment = Assignments[index];

            if (assignment.Pages.From < 1)
            {
                errors.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"stationery assignment {index}: page range '{assignment.Pages}' does not start at a one-based page."));
            }
            else if (!assignment.Pages.IsOpenEnded && assignment.Pages.To < assignment.Pages.From)
            {
                errors.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"stationery assignment {index}: page range '{assignment.Pages}' ends before it starts."));
            }

            if (assignment.Options is not { } options)
            {
                errors.Add($"stationery assignment {index}: the options are null.");
                continue;
            }

            foreach (var problem in options.Validate())
            {
                errors.Add($"stationery assignment {index}: {problem}");
            }
        }

        if (Default is { } fallback)
        {
            if (fallback.Options is not { } options)
            {
                errors.Add("the default stationery: the options are null.");
            }
            else
            {
                foreach (var problem in options.Validate())
                {
                    errors.Add($"the default stationery: {problem}");
                }
            }
        }

        return errors;
    }

    /// <summary>Determines whether two stationery plans describe the same backgrounds.</summary>
    /// <param name="other">The stationery to compare with.</param>
    /// <returns><see langword="true"/> when the assignments and the default are identical.</returns>
    public bool Equals(PdfStationery? other) =>
        other is not null && Assignments.SequenceEqual(other.Assignments) && Default == other.Default;

    /// <summary>Returns a hash code over the assignments and the default.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var assignment in Assignments)
        {
            hash.Add(assignment);
        }

        hash.Add(Default);
        return hash.ToHashCode();
    }

    /// <summary>Formats the stationery for diagnostics.</summary>
    /// <returns>The number of assignments and whether a default exists.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Assignments.Length} stationery assignment(s){(Default is null ? string.Empty : " with a default")}");
}
