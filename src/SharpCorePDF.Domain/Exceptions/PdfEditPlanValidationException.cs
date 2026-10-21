using System.Collections.ObjectModel;

namespace SharpCorePDF.Domain.Exceptions;

/// <summary>
/// Thrown when a <see cref="Aggregates.PdfEditPlan"/> violates one of its invariants, either while it
/// is being assembled or while it is being executed against the real page counts of its source.
/// </summary>
public sealed class PdfEditPlanValidationException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="PdfEditPlanValidationException"/> class.</summary>
    /// <param name="errors">Every invariant that was violated.</param>
    public PdfEditPlanValidationException(IEnumerable<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = new ReadOnlyCollection<string>([.. errors]);
    }

    /// <summary>Initializes a new instance of the <see cref="PdfEditPlanValidationException"/> class with a single error.</summary>
    /// <param name="error">The invariant that was violated.</param>
    public PdfEditPlanValidationException(string error)
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
            0 => "The edit plan is not valid.",
            1 => $"The edit plan is not valid: {list[0]}",
            _ => $"The edit plan is not valid ({list.Count} problems): {string.Join(" | ", list)}",
        };
    }
}
