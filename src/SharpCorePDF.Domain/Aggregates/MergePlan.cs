using System.Collections.Immutable;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.Aggregates;

/// <summary>
/// The aggregate root of a merge: an ordered set of <see cref="PdfSource"/> values plus the options
/// that control the result.
/// </summary>
/// <remarks>
/// <para>
/// A plan is immutable. Every mutation returns a new instance, so a plan can be built once at start
/// up, shared, and executed concurrently without locks:
/// </para>
/// <code>
/// var plan = MergePlan.Create()
///     .AddFile(@"C:\a.pdf")
///     .AddFile(@"C:\b.pdf", pages: "2-3")
///     .WithOutput(options => options with { OutlinePolicy = OutlinePolicy.Drop });
/// </code>
/// <para>
/// The invariants a plan can check on its own are enforced by <see cref="Validate"/> and
/// <see cref="EnsureValid"/>. Invariants that depend on the actual documents — a page selection that
/// reaches beyond the last page, for example — are enforced when the plan is executed, because only
/// then are the source documents read.
/// </para>
/// </remarks>
public sealed record MergePlan
{
    private readonly ImmutableArray<PdfSource> sources;

    private MergePlan(ImmutableArray<PdfSource> sources, MergeOutputOptions output)
    {
        this.sources = sources;
        Output = output;
    }

    /// <summary>Gets the sources in the order in which they contribute pages.</summary>
    public IReadOnlyList<PdfSource> Sources => sources;

    /// <summary>Gets the options that control the merged document.</summary>
    public MergeOutputOptions Output { get; }

    /// <summary>Gets the number of sources in the plan.</summary>
    public int SourceCount => sources.Length;

    /// <summary>Gets a value indicating whether the plan has no sources yet.</summary>
    public bool IsEmpty => sources.Length == 0;

    /// <summary>Gets the source whose document level features lead the merge.</summary>
    /// <exception cref="MergePlanValidationException">The plan has no sources, or the configured primary source is not part of the plan.</exception>
    public PdfSource PrimarySource => ResolvePrimarySource();

    /// <summary>Creates an empty plan.</summary>
    /// <param name="output">The options for the merged document; defaults to <see cref="MergeOutputOptions.Default"/>.</param>
    /// <returns>The new plan.</returns>
    public static MergePlan Create(MergeOutputOptions? output = null) =>
        new([], output ?? MergeOutputOptions.Default);

    /// <summary>Creates a plan from a set of sources.</summary>
    /// <param name="sources">The sources to merge, in merge order.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sources"/> is <see langword="null"/>.</exception>
    public static MergePlan For(params PdfSource[] sources) => Create().AddSources(sources);

    /// <summary>Appends a source.</summary>
    /// <param name="source">The source to append.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="MergePlanValidationException">A source with the same identity is already part of the plan.</exception>
    public MergePlan AddSource(PdfSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (sources.Any(existing => existing.Id == source.Id))
        {
            throw new MergePlanValidationException(
                $"a source with identity '{source.Id}' is already part of the plan.");
        }

        return new MergePlan(sources.Add(source), Output);
    }

    /// <summary>Appends several sources.</summary>
    /// <param name="sources">The sources to append, in merge order.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sources"/> is <see langword="null"/>.</exception>
    public MergePlan AddSources(IEnumerable<PdfSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var plan = this;

        foreach (var source in sources)
        {
            plan = plan.AddSource(source);
        }

        return plan;
    }

    /// <summary>Removes a source.</summary>
    /// <param name="id">The identity of the source to remove.</param>
    /// <returns>The new plan, unchanged when no source carried that identity.</returns>
    public MergePlan RemoveSource(PdfSourceId id)
    {
        for (var index = 0; index < sources.Length; index++)
        {
            if (sources[index].Id == id)
            {
                return new MergePlan(sources.RemoveAt(index), Output);
            }
        }

        return this;
    }

    /// <summary>Replaces the options of the plan.</summary>
    /// <param name="output">The new options.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public MergePlan WithOutput(MergeOutputOptions output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new MergePlan(sources, output);
    }

    /// <summary>Adjusts the options of the plan.</summary>
    /// <param name="mutate">A function that produces the new options, typically a <c>with</c> expression.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mutate"/> is <see langword="null"/>.</exception>
    public MergePlan WithOutput(Func<MergeOutputOptions, MergeOutputOptions> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        return WithOutput(mutate(Output));
    }

    /// <summary>Sets, or clears, the primary source of the merge.</summary>
    /// <param name="primarySourceId">The identity of the primary source, or <see langword="null"/> to fall back to the first source.</param>
    /// <returns>The new plan.</returns>
    public MergePlan WithPrimarySource(PdfSourceId? primarySourceId) =>
        WithOutput(Output with { PrimarySourceId = primarySourceId });

    /// <summary>Finds a source by identity.</summary>
    /// <param name="id">The identity to look for.</param>
    /// <returns>The matching source, or <see langword="null"/>.</returns>
    public PdfSource? FindSource(PdfSourceId id) => sources.FirstOrDefault(source => source.Id == id);

    /// <summary>Checks every invariant that can be verified without reading the source documents.</summary>
    /// <returns>Every problem found, or an empty list when the plan is executable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (sources.Length == 0)
        {
            errors.Add("the plan contains no sources; add at least one document to merge.");
        }

        errors.AddRange(Output.Validate());

        var duplicate = sources
            .GroupBy(source => source.Id)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            errors.Add($"the identity '{duplicate.Key}' is used by {duplicate.Count()} sources; every source needs its own identity.");
        }

        if (Output.PrimarySourceId is { } primarySourceId && sources.All(source => source.Id != primarySourceId))
        {
            errors.Add($"the configured primary source '{primarySourceId}' is not part of the plan.");
        }

        foreach (var source in sources.Where(source => string.IsNullOrWhiteSpace(source.DisplayName)))
        {
            errors.Add($"the source '{source.Id}' has an empty display name.");
        }

        return errors;
    }

    /// <summary>Throws when the plan violates an invariant.</summary>
    /// <exception cref="MergePlanValidationException">The plan is not executable.</exception>
    public void EnsureValid()
    {
        var errors = Validate();

        if (errors.Count > 0)
        {
            throw new MergePlanValidationException(errors);
        }
    }

    /// <summary>Determines whether two plans describe the same merge.</summary>
    /// <param name="other">The plan to compare with.</param>
    /// <returns><see langword="true"/> when the sources and the options are identical.</returns>
    public bool Equals(MergePlan? other) =>
        other is not null && sources.SequenceEqual(other.sources) && Output == other.Output;

    /// <summary>Returns a hash code over the source identities and the options.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var source in sources)
        {
            hash.Add(source.Id);
        }

        hash.Add(Output);
        return hash.ToHashCode();
    }

    /// <summary>Formats the plan for diagnostics.</summary>
    /// <returns>The formatted plan.</returns>
    public override string ToString() =>
        $"{SourceCount} source(s) to PDF {Output.TargetVersion}, outlines {Output.OutlinePolicy}, metadata {Output.MetadataPolicy}";

    private PdfSource ResolvePrimarySource()
    {
        if (sources.Length == 0)
        {
            throw new MergePlanValidationException("the plan contains no sources; add at least one document to merge.");
        }

        if (Output.PrimarySourceId is not { } primarySourceId)
        {
            return sources[0];
        }

        return FindSource(primarySourceId)
            ?? throw new MergePlanValidationException(
                $"the configured primary source '{primarySourceId}' is not part of the plan.");
    }
}
