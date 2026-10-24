namespace SharpCorePDF.Examples;

/// <summary>One example: what it is called, what it shows, and the code that shows it.</summary>
/// <param name="Id">The name used on the command line.</param>
/// <param name="Title">The sentence printed before the example runs.</param>
/// <param name="Run">The example itself.</param>
internal sealed record Scenario(string Id, string Title, Func<ExampleContext, CancellationToken, Task> Run);

/// <summary>The examples, in the order they run when none is named.</summary>
internal static class Scenarios
{
    /// <summary>Gets every example, easiest first.</summary>
    public static IReadOnlyList<Scenario> All { get; } =
    [
        new("merge-files", "merge whole documents from disk", MergeExamples.MergeFilesAsync),
        new("select-pages", "take selected pages, in a selected order", MergeExamples.SelectPagesAsync),
        new("merge-streams", "merge documents that are already in memory", SourceExamples.MergeStreamsAsync),
        new("author-page", "write a cover page with a logo and text, and put it in front of a document", AuthoringExamples.WriteCoverAsync),
        new("creator-parity", "write a whole document: two page sizes, a JPEG, bookmarks, metadata and a page imported from another file", CreatorExamples.WriteCreatorParityAsync),
        new("draw-shapes", "draw lines, paths, a clipping region and a transformed group on a page that did not exist", DrawingExamples.DrawShapesAsync),
        new("edit-pages", "edit a document that exists: insert, rotate, delete and crop in one pass", PdfEditExamples.EditPagesAsync),
        new("watermark", "stamp a watermark written from nothing over every page of a document", PdfEditExamples.WatermarkAsync),
        new("annotate", "comment on a document without touching its content: highlights, notes, a stamp and links", PdfEditExamples.AnnotateAsync),
        new("fill-form", "write a form, fill it by name, and flatten it so a reader without forms still shows the values", PdfEditExamples.FillFormAsync),
        new("no-container", "use the packages without a dependency injection container", SourceExamples.WithoutContainerAsync),
        new("inspect-first", "describe documents before merging them", InspectionExamples.InspectFirstAsync),
        new("read-content", "read the images, text, comments, form, bookmarks, attachments and metadata a document carries", ContentInspectionExamples.ReadContentAsync),
        new("extract-layout", "read the letters, blocks and reading order of a page, and write it as ALTO, PAGE XML, hOCR and SVG", LayoutExamples.ExtractLayoutAsync),
        new("inspect-objects", "walk the object graph of a document and the operators of a content stream", LayoutExamples.InspectObjectsAsync),
        new("options", "control metadata, bookmarks, the version and reproducibility", OptionExamples.MetadataAndVersionAsync),
        new("archive-pdfa", "write a document for PDF/A-2b, and refuse one that cannot be", ArchiveExamples.ConvertToPdfAAsync),
        new("sign-and-archive", "archive a page for PDF/A-3b, sign the archive with a certificate made for the run, and validate it", ArchiveExamples.SignAndArchiveAsync),
        new("protect-result", "write a result that needs a password, with the permissions you choose", OptionExamples.ProtectResultAsync),
        new("warnings", "read what the merge reports about what it had to change", OptionExamples.WarningsAsync),
        new("errors", "recognise the ways a merge can be refused", ErrorHandlingExamples.HandleRefusalsAsync),
    ];

    /// <summary>Determines whether the arguments asked for help.</summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns><see langword="true"/> when usage should be printed and nothing should run.</returns>
    public static bool IsHelp(string[] args) =>
        args.Length > 0 && args.All(argument => argument is "--help" or "-h" or "help");

    /// <summary>Determines whether the arguments asked for the list of examples.</summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns><see langword="true"/> when only the list should be printed.</returns>
    public static bool IsList(string[] args) => args is ["list"];

    /// <summary>Finds the examples a caller asked for.</summary>
    /// <param name="args">The command line arguments.</param>
    /// <param name="unknown">The arguments that name no example.</param>
    /// <returns>The examples to run, in the order they were asked for.</returns>
    public static IReadOnlyList<Scenario> Resolve(string[] args, out IReadOnlyList<string> unknown)
    {
        var names = args.Where(argument => !argument.StartsWith('-')).ToArray();

        if (names.Length == 0)
        {
            unknown = [];
            return All;
        }

        var found = new List<Scenario>(names.Length);
        var missing = new List<string>();

        foreach (var name in names)
        {
            var scenario = All.FirstOrDefault(
                candidate => string.Equals(candidate.Id, name, StringComparison.OrdinalIgnoreCase));

            if (scenario is null)
            {
                missing.Add(name);
            }
            else if (!found.Contains(scenario))
            {
                found.Add(scenario);
            }
        }

        unknown = missing;
        return found;
    }

    /// <summary>Prints what every example shows.</summary>
    public static void PrintList()
    {
        Console.WriteLine("Examples:");

        foreach (var scenario in All)
        {
            Console.WriteLine($"  {scenario.Id,-14} {scenario.Title}");
        }
    }

    /// <summary>Prints how the examples are called.</summary>
    public static void PrintUsage()
    {
        Console.WriteLine("SharpCorePDF examples");
        Console.WriteLine();
        Console.WriteLine("  dotnet run --project .\\examples\\SharpCorePDF.Examples                  run every example");
        Console.WriteLine("  dotnet run --project .\\examples\\SharpCorePDF.Examples -- <name> [...]  run some of them");
        Console.WriteLine("  dotnet run --project .\\examples\\SharpCorePDF.Examples -- list          list them");
        Console.WriteLine();
        PrintList();
    }
}
