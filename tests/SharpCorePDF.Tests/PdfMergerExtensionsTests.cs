using Microsoft.Extensions.DependencyInjection;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the convenience overloads that read as if they belonged to <see cref="IPdfMerger"/>.
/// </summary>
/// <remarks>
/// <para>
/// The overloads exist so the three shapes a caller asks for most — merge to a file, to bytes, and a list
/// of sources to a stream — do not need a plan to be built by hand. The walk a caller makes in
/// <see cref="AddSharpCorePDFTests"/> covers all three over the real merger; the tests with an
/// <see cref="RecordingMerger"/> cover them again where what the overload did cannot be mistaken for what
/// the merger did underneath.
/// </para>
/// <para>
/// That distinction is the point rather than a repetition. The real merger refuses a null plan itself, so a
/// guard deleted from an overload raises the same exception and escapes notice: the test passes, the guard is
/// untested. A stub that answers anything it is given is what makes a missing guard visible.
/// </para>
/// </remarks>
public sealed class PdfMergerExtensionsTests
{
    /// <summary>Merging to bytes returns a document that reads back, not a buffer that was never filled.</summary>
    [Fact]
    public async Task Merges_a_plan_to_bytes()
    {
        using var provider = new ServiceCollection().AddSharpCorePDF().BuildServiceProvider();
        var merger = provider.GetRequiredService<IPdfMerger>();
        var inspector = provider.GetRequiredService<IPdfInspector>();

        var bytes = await merger.MergeToBytesAsync(
            MergePlan.Create().AddFile(TestAssets.Outlined),
            TestContext.Current.CancellationToken);

        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5), StringComparison.Ordinal);

        var info = await inspector.InspectAsync(
            new PdfByteArraySource(bytes, "merged.pdf"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, info.PageCount);
        Assert.Equal("Outlined sample", info.Metadata.Title);
    }

    /// <summary>A list of sources becomes a plan that contributes every page of each, in order.</summary>
    [Fact]
    public async Task Merges_a_list_of_sources()
    {
        using var provider = new ServiceCollection().AddSharpCorePDF().BuildServiceProvider();
        var merger = provider.GetRequiredService<IPdfMerger>();
        var inspector = provider.GetRequiredService<IPdfInspector>();

        IPdfContentSource[] sources = [new PdfFileSource(TestAssets.Outlined), new PdfFileSource(TestAssets.Modern)];
        using var destination = new MemoryStream();

        var result = await merger.MergeAsync(sources, destination, TestContext.Current.CancellationToken);

        Assert.Equal(5, result.PageCount);
        Assert.Equal(destination.Length, result.OutputBytes);

        var info = await inspector.InspectAsync(
            new PdfByteArraySource(destination.ToArray(), "merged.pdf"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, info.PageCount);
        Assert.Equal(5, info.Pages.Count);
    }

    /// <summary>
    /// An overload that is handed nothing says so, rather than failing further down. What a caller sees is
    /// pinned here, over the real merger; that the exception comes from the overload and not from the merger
    /// underneath is pinned in <see cref="Refuses_a_missing_plan_before_reaching_the_merger"/>, because this
    /// test cannot tell the two apart.
    /// </summary>
    [Fact]
    public async Task Refuses_a_missing_argument()
    {
        using var provider = new ServiceCollection().AddSharpCorePDF().BuildServiceProvider();
        var merger = provider.GetRequiredService<IPdfMerger>();
        var plan = MergePlan.Create().AddFile(TestAssets.Modern);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => merger.MergeToBytesAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => merger.MergeToFileAsync(null!, "merged.pdf", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => merger.MergeAsync(
                (IEnumerable<IPdfContentSource>)null!,
                new MemoryStream(),
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => merger.MergeAsync([], null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => merger.MergeToFileAsync(plan, "   ", TestContext.Current.CancellationToken));
    }

    /// <summary>The merged bytes land in the file the caller named, and the plan reaches the merger.</summary>
    [Fact]
    public async Task Merges_a_plan_to_a_file()
    {
        using var folder = TestAssets.NewTemporaryFolder("merge-to-file");
        var merger = new RecordingMerger();
        var destination = folder.File("merged.pdf");
        var plan = MergePlan.Create().AddFile(TestAssets.Modern);

        var result = await merger.MergeToFileAsync(
            plan,
            destination,
            TestContext.Current.CancellationToken);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.Same(plan, merger.LastPlan);
        Assert.Equal((long)PayloadLength, result.OutputBytes);
    }

    /// <summary>
    /// A destination whose directory does not exist yet is created, so a caller may name a path instead of
    /// preparing one.
    /// </summary>
    [Fact]
    public async Task Creates_the_directory_a_destination_names()
    {
        using var folder = TestAssets.NewTemporaryFolder("merge-to-file-nested");
        var merger = new RecordingMerger();
        var destination = Path.Combine(folder.Path, "one", "two", "merged.pdf");

        await merger.MergeToFileAsync(
            MergePlan.Create().AddFile(TestAssets.Modern),
            destination,
            TestContext.Current.CancellationToken);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
    }

    /// <summary>A file that is already there is replaced, not appended to.</summary>
    [Fact]
    public async Task Truncates_a_file_that_is_already_there()
    {
        using var folder = TestAssets.NewTemporaryFolder("merge-over-longer");
        var merger = new RecordingMerger();
        var destination = folder.File("merged.pdf");

        await File.WriteAllBytesAsync(
            destination,
            new byte[PayloadLength + 1_000],
            TestContext.Current.CancellationToken);

        await merger.MergeToFileAsync(
            MergePlan.Create().AddFile(TestAssets.Modern),
            destination,
            TestContext.Current.CancellationToken);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The plan is refused by the overload itself: the merger below answers any plan it is given, so a guard
    /// that is missing lets the call through instead of being masked by the port's own validation.
    /// </summary>
    [Fact]
    public async Task Refuses_a_missing_plan_before_reaching_the_merger()
    {
        using var folder = TestAssets.NewTemporaryFolder("merge-refuses");
        var merger = new RecordingMerger();
        var destination = folder.File("merged.pdf");
        var token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => merger.MergeToFileAsync(null!, destination, token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => merger.MergeToBytesAsync(null!, token));

        Assert.Null(merger.LastPlan);
        Assert.False(File.Exists(destination), "a refused call left a file behind");
    }

    /// <summary>A path that is not a path is refused, and nothing is created from it.</summary>
    /// <remarks>
    /// The whitespace case is asserted to its type alone. <see cref="ArgumentException.ThrowIfNullOrWhiteSpace(string)"/>
    /// and <see cref="Path.GetFullPath(string)"/> refuse a whitespace path with the same exception, so deleting the
    /// guard leaves this assertion green; the only thing that changes is <see cref="ArgumentException.ParamName"/>,
    /// which the member's documentation does not promise. That survivor is recorded in
    /// <c>docs/mutation-testing.md</c> rather than pinned here.
    /// </remarks>
    [Fact]
    public async Task Refuses_a_destination_path_that_is_not_a_path()
    {
        var merger = new RecordingMerger();
        var plan = MergePlan.Create().AddFile(TestAssets.Modern);
        var token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(() => merger.MergeToFileAsync(plan, null!, token));
        await Assert.ThrowsAsync<ArgumentException>(() => merger.MergeToFileAsync(plan, string.Empty, token));
        await Assert.ThrowsAsync<ArgumentException>(() => merger.MergeToFileAsync(plan, "   ", token));

        Assert.Null(merger.LastPlan);
    }

    /// <summary>A list of documents becomes a plan carrying every source, in the order given.</summary>
    /// <remarks>
    /// The list is cast to <see cref="IEnumerable{IPdfContentSource}"/> on purpose. The port has an overload
    /// whose first parameter is a <see cref="MergePlan"/>, and an instance member wins over an extension
    /// member, so an argument that could bind to either would test the stub instead of the overload.
    /// </remarks>
    [Fact]
    public async Task Turns_a_list_of_documents_into_a_plan()
    {
        var merger = new RecordingMerger();
        using var destination = new MemoryStream();

        await merger.MergeAsync(
            (IEnumerable<IPdfContentSource>)
            [new PdfByteArraySource(Payload, "first.pdf"), new PdfByteArraySource(Payload, "second.pdf")],
            destination,
            TestContext.Current.CancellationToken);

        Assert.Equal(Payload, destination.ToArray());
        Assert.NotNull(merger.LastPlan);
        Assert.Equal(2, merger.LastPlan.SourceCount);
        Assert.Equal(
            ["first.pdf", "second.pdf"],
            merger.LastPlan.Sources.Select(source => source.DisplayName));
    }

    /// <summary>The list overload refuses a missing list or destination before a plan is built.</summary>
    /// <remarks>The list is cast for the reason given on <see cref="Turns_a_list_of_documents_into_a_plan"/>.</remarks>
    [Fact]
    public async Task Refuses_a_missing_list_or_destination()
    {
        var merger = new RecordingMerger();
        using var destination = new MemoryStream();
        var token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => merger.MergeAsync((IEnumerable<IPdfContentSource>)null!, destination, token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => merger.MergeAsync(
                (IEnumerable<IPdfContentSource>)[new PdfByteArraySource(Payload, "first.pdf")],
                null!,
                token));

        Assert.Null(merger.LastPlan);
    }

    /// <summary>Small enough for a file round trip to be cheap, large enough for a truncated file to show.</summary>
    private const int PayloadLength = 4_096;

    /// <summary>The bytes the stub merger writes, patterned so a truncated or padded file is visible.</summary>
    private static readonly byte[] Payload = CreatePayload();

    /// <summary>Builds the payload the stub writes and the tests compare against.</summary>
    /// <returns>Four kilobytes, cycling through a prime so no window repeats.</returns>
    private static byte[] CreatePayload()
    {
        var payload = new byte[PayloadLength];

        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index % 251);
        }

        return payload;
    }

    /// <summary>Describes the payload the stub wrote, as a merge that carried the sources of the plan.</summary>
    /// <param name="plan">The plan the merge was handed, which may be <see langword="null"/>.</param>
    /// <returns>A result consistent with the bytes written.</returns>
    private static MergeResult Result(MergePlan? plan) => new()
    {
        PageCount = 1,
        SourceCount = plan?.SourceCount ?? 0,
        OutputBytes = PayloadLength,
        OutputVersion = PdfVersion.V1_7,
        Duration = TimeSpan.Zero,
        PrimarySourceId = plan is { SourceCount: > 0 } ? plan.Sources[0].Id : PdfSourceId.New(),
    };

    /// <summary>
    /// A merger that writes a known payload and remembers the plan it was handed.
    /// </summary>
    /// <remarks>
    /// It answers whatever it is given, a null plan and a null destination included. A port implementation is
    /// free not to restate the overloads' contract — the real one does, which is why the guards look worthless
    /// until a merger that stays silent is put in front of them. What is being tested here is the overload's
    /// own promise, and nothing downstream is relied on to keep it.
    /// </remarks>
    private sealed class RecordingMerger : IPdfMerger
    {
        /// <summary>Gets the plan the last merge was handed.</summary>
        public MergePlan? LastPlan { get; private set; }

        /// <inheritdoc/>
        public Task<MergeResult> MergeAsync(
            MergePlan? plan,
            Stream? destination,
            CancellationToken cancellationToken = default)
        {
            LastPlan = plan;

            destination?.Write(Payload, 0, Payload.Length);

            return Task.FromResult(Result(plan));
        }
    }
}
