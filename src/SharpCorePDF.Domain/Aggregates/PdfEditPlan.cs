using System.Collections.Immutable;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.Aggregates;

/// <summary>
/// The aggregate root of an edit: an ordered list of operations applied to one source document, plus
/// the options that control the result.
/// </summary>
/// <remarks>
/// <para>
/// A plan is immutable. Every operation returns a new instance, so a plan can be built once, shared,
/// and applied concurrently without locks:
/// </para>
/// <code>
/// var plan = PdfEditPlan.Create()
///     .RotatePage(1, PageRotation.Clockwise90)
///     .DeletePage(4)
///     .MovePage(3, 1);
/// </code>
/// <para>
/// Page numbers are one-based and refer to the document state produced by every preceding operation.
/// The invariants a plan can check on its own are enforced by <see cref="Validate"/> and
/// <see cref="EnsureValid"/>; an operation that reaches beyond the last page is refused when the plan
/// is executed, because only then is the source document read.
/// </para>
/// </remarks>
public sealed record PdfEditPlan
{
    private readonly ImmutableArray<PdfEditOperation> operations;

    private PdfEditPlan(ImmutableArray<PdfEditOperation> operations, PdfEditOutputOptions output)
    {
        this.operations = operations;
        Output = output;
    }

    /// <summary>Gets the operations in the order they are applied.</summary>
    public IReadOnlyList<PdfEditOperation> Operations => operations;

    /// <summary>Gets the options that control the edited document.</summary>
    public PdfEditOutputOptions Output { get; }

    /// <summary>Gets the number of operations in the plan.</summary>
    public int OperationCount => operations.Length;

    /// <summary>Gets a value indicating whether the plan changes nothing.</summary>
    public bool IsEmpty => operations.Length == 0;

    /// <summary>Creates an empty plan.</summary>
    /// <param name="output">The options for the edited document; defaults to <see cref="PdfEditOutputOptions.Default"/>.</param>
    /// <returns>The new plan.</returns>
    public static PdfEditPlan Create(PdfEditOutputOptions? output = null) =>
        new([], output ?? PdfEditOutputOptions.Default);

    /// <summary>Creates an empty plan that writes the result with the supplied options.</summary>
    /// <param name="output">The options for the edited document.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static PdfEditPlan For(PdfEditOutputOptions output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new([], output);
    }

    /// <summary>Adds a rotation to one page.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="rotation">The clockwise rotation to add to the page's existing rotation.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rotation"/> is not a quarter turn.</exception>
    public PdfEditPlan RotatePage(int pageNumber, PageRotation rotation)
    {
        if (!Enum.IsDefined(rotation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rotation),
                rotation,
                "A page rotation must be a quarter turn.");
        }

        return Add(new PdfRotatePageOperation(pageNumber, rotation));
    }

    /// <summary>Removes one page.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <returns>The new plan.</returns>
    public PdfEditPlan DeletePage(int pageNumber) => Add(new PdfDeletePageOperation(pageNumber));

    /// <summary>Moves one page to another position.</summary>
    /// <param name="from">The one-based page number to move.</param>
    /// <param name="to">The one-based position the page ends up at.</param>
    /// <returns>The new plan.</returns>
    public PdfEditPlan MovePage(int from, int to) => Add(new PdfMovePageOperation(from, to));

    /// <summary>Copies one page to another position.</summary>
    /// <param name="pageNumber">The one-based page number to copy.</param>
    /// <param name="insertAt">The one-based position the copy is inserted before; the current page count plus one appends it.</param>
    /// <returns>The new plan.</returns>
    public PdfEditPlan DuplicatePage(int pageNumber, int insertAt) =>
        Add(new PdfDuplicatePageOperation(pageNumber, insertAt));

    /// <summary>Overrides part or all of the document information.</summary>
    /// <param name="metadata">The values to apply; fields left unset keep their source value.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
    public PdfEditPlan SetMetadata(PdfMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return Add(new PdfSetMetadataOperation(metadata));
    }

    /// <summary>Replaces the options of the plan.</summary>
    /// <param name="output">The new options.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public PdfEditPlan WithOutput(PdfEditOutputOptions output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new(operations, output);
    }

    /// <summary>Adjusts the options of the plan.</summary>
    /// <param name="mutate">A function that produces the new options, typically a <c>with</c> expression.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mutate"/> is <see langword="null"/>.</exception>
    public PdfEditPlan WithOutput(Func<PdfEditOutputOptions, PdfEditOutputOptions> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        return WithOutput(mutate(Output));
    }

    /// <summary>Checks every invariant that can be verified without reading the source document.</summary>
    /// <returns>Every problem found, or an empty list when the plan is executable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        errors.AddRange(Output.Validate());

        for (var index = 0; index < operations.Length; index++)
        {
            var operation = operations[index];

            if (operation is null)
            {
                errors.Add($"operation {index} is null.");
                continue;
            }

            errors.AddRange(ValidateOperation(operation, index));
        }

        return errors;
    }

    /// <summary>Throws when the plan violates an invariant.</summary>
    /// <exception cref="PdfEditPlanValidationException">The plan is not executable.</exception>
    public void EnsureValid()
    {
        var errors = Validate();

        if (errors.Count > 0)
        {
            throw new PdfEditPlanValidationException(errors);
        }
    }

    /// <summary>Formats the plan for diagnostics.</summary>
    /// <returns>The formatted plan.</returns>
    public override string ToString() =>
        operations.Length == 0
            ? "edit plan with no operations"
            : $"edit plan: {string.Join(", ", operations.Select(operation => operation.ToString()))}";

    private PdfEditPlan Add(PdfEditOperation operation) =>
        new(operations.Add(operation), Output);

    private static IEnumerable<string> ValidateOperation(PdfEditOperation operation, int index)
    {
        switch (operation)
        {
            case PdfRotatePageOperation rotate:
                if (rotate.PageNumber < 1)
                {
                    yield return $"operation {index}: page number {rotate.PageNumber} is not one-based.";
                }

                if (!Enum.IsDefined(rotate.Rotation))
                {
                    yield return $"operation {index}: rotation '{(int)rotate.Rotation}' is not a quarter turn.";
                }

                break;

            case PdfDeletePageOperation delete when delete.PageNumber < 1:
                yield return $"operation {index}: page number {delete.PageNumber} is not one-based.";
                break;

            case PdfMovePageOperation move:
                if (move.From < 1)
                {
                    yield return $"operation {index}: page number {move.From} is not one-based.";
                }

                if (move.To < 1)
                {
                    yield return $"operation {index}: move position {move.To} is not one-based.";
                }

                break;

            case PdfDuplicatePageOperation duplicate:
                if (duplicate.PageNumber < 1)
                {
                    yield return $"operation {index}: page number {duplicate.PageNumber} is not one-based.";
                }

                if (duplicate.InsertAt < 1)
                {
                    yield return $"operation {index}: insert position {duplicate.InsertAt} is not one-based.";
                }

                break;

            case PdfSetMetadataOperation metadata when metadata.Metadata is null:
                yield return $"operation {index}: the metadata to set is null.";
                break;
        }
    }
}
