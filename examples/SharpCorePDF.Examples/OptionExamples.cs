using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Examples;

/// <summary>
/// What a merge writes besides its pages: document information, bookmarks, the version, and how to make the
/// result reproducible.
/// </summary>
internal static class OptionExamples
{
    /// <summary>Merges with explicit output options and shows that the result is reproducible.</summary>
    /// <param name="context">The documents to merge and the ports to merge them with.</param>
    /// <param name="cancellationToken">A token to cancel the merge.</param>
    public static async Task MetadataAndVersionAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        // Everything about the merged document that is not its pages lives in MergeOutputOptions, and every
        // plan carries its own copy of it.
        var plan = MergePlan.Create()
            .AddFile(context.Summary)
            .AddFile(context.Appendix, pages: "1-2")
            .WithOutput(output => output with
            {
                TargetVersion = PdfVersion.V2_0,                      // the header of the result
                MetadataPolicy = MetadataPolicy.Merge,                // the summary first, then fill the gaps
                MetadataOverride = new PdfMetadata { Title = "Quarterly report 2026", Author = "Dana Scully" },
                OutlinePolicy = OutlinePolicy.Drop,                   // a result without bookmarks
                PageLabelPolicy = PageLabelPolicy.FromPrimarySource,  // page labels of the primary source
                ModificationDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
                Producer = "SharpCorePDF.Examples",
            });

        // Without the fixed ModificationDate the current time would go into the file, so two merges of the
        // same plan would differ. With it, the same plan writes the same bytes.
        var first = await context.Merger.MergeToBytesAsync(plan, cancellationToken);
        var second = await context.Merger.MergeToBytesAsync(plan, cancellationToken);

        var destination = context.OutputPath("report.pdf");
        await File.WriteAllBytesAsync(destination, first, cancellationToken);

        Report.Item("wrote", destination);
        Report.Item("fingerprint of run 1", Report.Fingerprint(first));
        Report.Item("fingerprint of run 2", Report.Fingerprint(second));
        Report.Item("identical", first.AsSpan().SequenceEqual(second));

        var info = await context.Inspector.InspectAsync(
            PdfFileSource.From(destination),
            cancellationToken: cancellationToken);

        Report.Item("version", info.Version);
        Report.Item("pages", info.PageCount);
        Report.Item("title", info.Metadata.Title);
        Report.Item("author", info.Metadata.Author);
        Report.Item("producer", info.Metadata.Producer);
        Report.Item("creation date", info.Metadata.CreationDate);
        Report.Item("modification date", info.Metadata.ModificationDate);
        Report.Item("bookmarks", info.HasOutlines ? info.OutlineItemCount.ToString() : "none");

        // The policies are values, so the same sources with a different policy are one line away. Here the
        // document information is dropped entirely: what is left is the producer.
        var bare = plan.WithOutput(output => output with
        {
            MetadataPolicy = MetadataPolicy.None,
            MetadataOverride = null,
        });

        var bareBytes = await context.Merger.MergeToBytesAsync(bare, cancellationToken);
        var bareInfo = await context.Inspector.InspectAsync(
            new PdfByteArraySource(bareBytes, "report-without-metadata.pdf"),
            cancellationToken: cancellationToken);

        Report.Item("pages without metadata", bareInfo.PageCount);
        Report.Item("title without metadata", bareInfo.Metadata.Title ?? "(none)");
        Report.Item("producer without metadata", bareInfo.Metadata.Producer);
    }

    /// <summary>
    /// Writes an encrypted result, shows that the passwords are what decides who gets in, and merges an
    /// encrypted source into a document that is sealed again under new passwords.
    /// </summary>
    /// <param name="context">The documents to merge and the ports to merge them with.</param>
    /// <param name="cancellationToken">A token to cancel the merge.</param>
    public static async Task ProtectResultAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        // A merge writes in the clear unless it is told otherwise, and the encryption is one more output
        // option. The result has its own passwords and permissions: nothing a source declared is carried
        // into it, so a document that was only protected against editing can be merged and re-protected in
        // one step.
        var plan = MergePlan.Create()
            .AddFile(context.Summary)
            .AddFile(context.Appendix, pages: "1")
            .WithOutput(output => output with
            {
                Encryption = new PdfEncryptionOptions
                {
                    Algorithm = PdfEncryptionAlgorithm.AesWith256BitKey,  // the default, spelled out
                    UserPassword = "opens-it",                            // what a reader is asked for
                    OwnerPassword = "owns-it",                            // what opens it with every permission
                    Permissions = PdfPermissions.PrintOnly,               // printing, but no copying or editing
                },
            });

        var destination = context.OutputPath("protected.pdf");
        var bytes = await context.Merger.MergeToBytesAsync(plan, cancellationToken);
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken);

        Report.Item("wrote", destination);
        Report.Item("bytes", bytes.Length);

        // What a viewer asks for — and what it lets a reader do — is decided by the two entries of the
        // encryption dictionary, and by nothing else: without the password there is no page tree to read.
        var locked = await context.Inspector.InspectAsync(
            PdfFileSource.From(destination),
            cancellationToken: cancellationToken);

        Report.Item("encrypted", locked.IsEncrypted);
        Report.Item("readable without a password", locked.IsReadable);
        Report.Item("needs a password", locked.RequiresPassword);

        var opened = await context.Inspector.InspectAsync(
            PdfFileSource.From(destination),
            password: "opens-it",
            cancellationToken: cancellationToken);

        Report.Item("readable with the user password", opened.IsReadable);
        Report.Item("pages", opened.PageCount);
        Report.Item("title", opened.Metadata.Title ?? "(none)");
    }

    /// <summary>Prints what the inspection and the merge report about a document that is awkward in two ways.</summary>
    /// <param name="context">The documents to merge and the ports to merge them with.</param>
    /// <param name="cancellationToken">A token to cancel the merge.</param>
    public static async Task WarningsAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        // NamedDests.pdf is awkward in two ways, and the two ways are reported in two different places. Its
        // first page declares no page size: that is a fact about the document, so the inspection reports it.
        // Its catalog carries named destinations, and a merge can only take those from the primary source, so
        // a merge that uses this document as a secondary source has something to say about its own work.
        var secondary = Path.Combine(context.InputFolder, "NamedDests.pdf");
        var source = PdfFileSource.From(secondary);

        Report.Heading("what the inspection reports about the document");
        var info = await context.Inspector.InspectAsync(source, cancellationToken: cancellationToken);

        Report.Item("read", info);
        Report.Item("first page", info.FirstPageSize);

        foreach (var warning in info.Warnings)
        {
            // The kind is the contract: it names the condition, and the text explains it. A caller that
            // reacts to a warning switches on the kind and never on the wording.
            Report.Line($"{warning.Kind}: {warning.Message}");
        }

        Report.Heading("what the merge reports about its own work");
        var plan = MergePlan.Create()
            .AddFile(context.Summary)
            .AddFile(secondary, displayName: "named-destinations");

        var result = await context.Merger.MergeToFileAsync(
            plan,
            context.OutputPath("with-warnings.pdf"),
            cancellationToken);

        Report.Item("pages", result.PageCount);
        Report.Item("warnings", result.Warnings.Count);

        foreach (var warning in result.Warnings)
        {
            Report.Line($"{warning.Kind}: {warning.Message}");
        }

        Report.Line("(one of these lines is the output self check that appsettings.json turns on)");

        // What each source contributed: how many pages out of how many, and of which version.
        Report.Line("contributions (IsComplete means every page of the source was taken):");

        foreach (var contribution in result.Sources)
        {
            Report.Line($"{contribution}  (complete: {contribution.IsComplete})");
        }
    }
}

