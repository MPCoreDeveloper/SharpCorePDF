using System.Collections.ObjectModel;

namespace SharpCorePDF.Domain.Exceptions;

/// <summary>
/// Thrown when a <see cref="Aggregates.MergePlan"/> violates one of its invariants, either while it
/// is being assembled or while it is being executed against the real page counts of its sources.
/// </summary>
public sealed class MergePlanValidationException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="MergePlanValidationException"/> class.</summary>
    /// <param name="errors">Every invariant that was violated.</param>
    public MergePlanValidationException(IEnumerable<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = new ReadOnlyCollection<string>([.. errors]);
    }

    /// <summary>Initializes a new instance of the <see cref="MergePlanValidationException"/> class with a single error.</summary>
    /// <param name="error">The invariant that was violated.</param>
    public MergePlanValidationException(string error)
        : this([error])
    {
    }

    /// <summary>Gets every invariant that was violated, in the order they were discovered.</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IEnumerable<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var list = errors as IReadOnlyList<string> ?? [.. errors];
        return list.Count switch
        {
            0 => "The merge plan is not valid.",
            1 => $"The merge plan is not valid: {list[0]}",
            _ => $"The merge plan is not valid ({list.Count} problems): {string.Join(" | ", list)}",
        };
    }
}
