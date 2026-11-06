using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

// The Native AOT smoke test of SharpCorePDF.
//
// It does what a consumer does, in the smallest shape that touches every surface a trimmed, AOT-compiled
// publish has to keep: a page is authored with PdfDocumentBuilder, two documents are merged through the
// interfaces AddSharpCorePDF registered, and the result is read back as text with the content reader. A
// failure is a non-zero exit code and a line that says which step disagreed.
//
//   dotnet run --project .\examples\SharpCorePDF.AotSmoke
//   dotnet publish .\examples\SharpCorePDF.AotSmoke -c Release -r win-x64 -p:PublishAot=true
//   .\...\publish\SharpCorePDF.AotSmoke.exe --expect-aot
var expectAot = args.Contains("--expect-aot", StringComparer.Ordinal);
var heading = PdfTypeface.Standard(StandardFont.Helvetica);

var services = new ServiceCollection();
services.AddSharpCorePDF(PdfEngineOptions.Default with { VerifyOutput = true });

using var provider = services.BuildServiceProvider();

var plan = MergePlan.Create()
    .AddSource(PdfSource.Create(Author("AOT smoke one", "first.pdf")))
    .AddSource(PdfSource.Create(Author("AOT smoke two", "second.pdf")));

var merger = provider.GetRequiredService<IPdfMerger>();
using var merged = new MemoryStream();

var result = await merger.MergeAsync(plan, merged, CancellationToken.None);

var reader = provider.GetRequiredService<IPdfContentReader>();
var read = await reader.ReadTextAsync(
    new PdfByteArraySource(merged.ToArray(), "merged.pdf"),
    PageRange.All,
    cancellationToken: CancellationToken.None);

var text = string.Concat(read.Items.Select(block => block.Text));
var pages = read.Items.Select(block => block.PageNumber).Distinct().Count();
var dynamicCode = RuntimeFeature.IsDynamicCodeSupported;
var problems = new List<string>();

if (result.PageCount != 2)
{
    problems.Add($"the merge reported {result.PageCount} page(s) rather than 2");
}

if (pages != 2)
{
    problems.Add($"the read found {pages} page(s) rather than 2");
}

if (!text.Contains("AOT smoke one", StringComparison.Ordinal))
{
    problems.Add("the first page's text is missing");
}

if (!text.Contains("AOT smoke two", StringComparison.Ordinal))
{
    problems.Add("the second page's text is missing");
}

if (result.OutputBytes <= 0)
{
    problems.Add("the merge reported no output bytes");
}

if (expectAot && dynamicCode)
{
    problems.Add("this process supports dynamic code, so it is not an AOT publish");
}

Console.WriteLine($"aot={!dynamicCode} pages={pages} chars={text.Length} bytes={result.OutputBytes}");

foreach (var problem in problems)
{
    Console.Error.WriteLine("  " + problem);
}

Console.WriteLine(problems.Count == 0 ? "AOT smoke: ok" : "AOT smoke: failed");

return problems.Count == 0 ? 0 : 1;

// One page with one line of text, written by the library's own authoring surface.
PdfByteArraySource Author(string text, string name) =>
    new(
        PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText(text, 60, 80, heading, 24))
            .Build()
            .Bytes.ToArray(),
        name);
