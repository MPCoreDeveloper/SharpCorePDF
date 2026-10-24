using System.Globalization;
using System.Text;

namespace SharpCorePDF.Manual;

/// <summary>The values the cover states about the book.</summary>
/// <param name="Version">The version the manual describes.</param>
/// <param name="Product">The product name.</param>
/// <param name="Title">The document title, as metadata and the browser tab see it.</param>
internal sealed record ManualIdentity(string Version, string Product, string Title);

/// <summary>
/// Assembles the cover, the contents and the chapters into the one HTML document Chromium prints.
/// </summary>
internal static class ManualHtml
{
    private const string Description =
        "The complete SharpCorePDF manual: creating, editing and reading PDFs in .NET - merge plans, "
        + "page selections, output policies, encryption, inspection, authoring, extraction, the object "
        + "graph, signatures and PDF/A.";

    /// <summary>Builds the printable document.</summary>
    /// <param name="headings">Every heading of the manual, in document order.</param>
    /// <param name="bodies">The HTML of every chapter, in reading order.</param>
    /// <param name="pages">The measured page of every anchor, or null for the first pass.</param>
    /// <param name="identity">The values the cover states.</param>
    /// <param name="stylesheetPath">The path of the print stylesheet.</param>
    /// <param name="logoPath">The path of the cover logo.</param>
    /// <returns>The complete HTML document.</returns>
    public static string Assemble(
        IReadOnlyList<Heading> headings,
        IReadOnlyList<string> bodies,
        IReadOnlyDictionary<string, int>? pages,
        ManualIdentity identity,
        string stylesheetPath,
        string logoPath)
    {
        ArgumentNullException.ThrowIfNull(headings);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(identity);

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\" />\n");
        html.Append("<title>").Append(Escape(identity.Title)).Append("</title>\n");
        html.Append("<meta name=\"author\" content=\"MPCoreDeveloper\" />\n");
        html.Append("<meta name=\"description\" content=\"").Append(Escape(Description)).Append("\" />\n");
        html.Append("<link rel=\"stylesheet\" href=\"").Append(FileUri(stylesheetPath)).Append("\" />\n");
        html.Append("</head>\n<body>\n");
        html.Append(Cover(identity, logoPath));
        html.Append(Contents(headings, pages));
        html.Append("<main class=\"manual-body\">\n");
        html.AppendJoin('\n', bodies);
        html.Append("\n</main>\n</body>\n</html>\n");

        return html.ToString();
    }

    private static string Cover(ManualIdentity identity, string logoPath)
    {
        var logo = File.Exists(logoPath)
            ? "<img class=\"logo\" src=\"" + FileUri(logoPath) + "\" alt=\"SharpCorePDF\" />"
            : string.Empty;

        return $"""
<div class="cover">
  {logo}
  <h1 class="title">The {identity.Product}<br />Manual</h1>
  <div class="rule"></div>
  <div class="version">Version {identity.Version} &middot; .NET 10 and .NET 11</div>
  <p class="subtitle">Create, edit and read PDFs in .NET without a third-party engine: merge plans,
  page editing, authoring, text and layout extraction, signatures and PDF/A &mdash; behind a domain you
  can reason about.</p>
  <ul class="promise">
    <li>The whole public API, from <strong>MergePlan</strong> to <strong>MergeResult</strong></li>
    <li>Page selections, output policies, encryption and document inspection</li>
    <li>Editing, authoring a page, extraction in four formats and the object graph</li>
    <li>Digital signatures, archival output, and a cookbook of recipes for services that ship documents</li>
  </ul>
  <div class="imprint">
    {identity.Product} is published as the <strong>SharpCorePDF</strong> meta-package, with separate
    <strong>SharpCorePDF.Domain</strong>, <strong>.Application</strong>,
    <strong>.Infrastructure</strong> and <strong>.DependencyInjection</strong> packages on nuget.org.<br />
    This manual describes version {identity.Version} exactly.
  </div>
</div>

""";
    }

    private static string Contents(
        IReadOnlyList<Heading> headings,
        IReadOnlyDictionary<string, int>? pages)
    {
        var html = new StringBuilder();
        html.Append("<div class=\"toc-page\">\n<h1 id=\"contents\">Contents</h1>\n");

        foreach (var heading in headings)
        {
            if (heading.Level > 2)
            {
                continue;
            }

            var number = pages is null
                ? "&middot;"
                : pages.TryGetValue(heading.Anchor, out var page)
                    ? page.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;

            html.Append("<div class=\"toc-entry lvl")
                .Append(heading.Level.ToString(CultureInfo.InvariantCulture)).Append("\">")
                .Append("<span class=\"title\"><a href=\"#").Append(Escape(heading.Anchor)).Append("\">")
                .Append(Escape(heading.Title)).Append("</a></span>")
                .Append("<span class=\"dots\"></span><span class=\"pg\">").Append(number)
                .Append("</span></div>\n");
        }

        html.Append("<p class=\"toc-note\">Page numbers are the physical pages of this PDF; ")
            .Append("the cover is page 1. Every chapter starts on a new page. ")
            .Append("The running footer is added after printing.</p>\n</div>\n");

        return html.ToString();
    }

    private static string FileUri(string path) =>
        new Uri(Path.GetFullPath(path), UriKind.Absolute).AbsoluteUri;

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}

