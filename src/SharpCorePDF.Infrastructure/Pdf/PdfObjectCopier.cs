namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Copies the objects of one source document into the document being written.
/// </summary>
/// <remarks>
/// <para>
/// Copying happens in two phases, which is what makes it safe for the cyclic graphs that PDF objects
/// form in practice — a form field that points at its widget which points back at the field, a page
/// whose annotation points back at the page. The first phase only <em>reserves</em> a target object
/// number for every object it meets, so a cycle finds the mapping already registered; the second
/// phase then writes each reserved body, discovering and reserving further objects as it goes.
/// </para>
/// <para>
/// Only objects reachable from the pages and the features the caller keeps are copied, so the merged
/// document does not inherit the dead weight of its sources. Objects stored inside object streams of
/// a source are written as ordinary objects, because the merged document uses a classic
/// cross-reference table.
/// </para>
/// </remarks>
internal sealed partial class PdfObjectCopier
{
    private readonly PdfFileReader source;
    private readonly PdfWriter writer;
    private readonly Dictionary<long, int> targets = [];
    private readonly Dictionary<long, Func<PdfObject, PdfObject>> overrides = [];
    private readonly HashSet<long> redirected = [];
    private readonly Queue<long> pending = [];
    private readonly HashSet<long> queued = [];

    /// <summary>Initializes a new instance of the <see cref="PdfObjectCopier"/> class.</summary>
    /// <param name="source">The document to copy from.</param>
    /// <param name="writer">The writer that receives the copies.</param>
    public PdfObjectCopier(PdfFileReader source, PdfWriter writer)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <summary>Gets the number of objects that have been reserved so far.</summary>
    public int ReservedCount => targets.Count;

    /// <summary>
    /// Points every reference to <paramref name="sourceObjectNumber"/> at an object that is written by
    /// someone else, and never copies the object itself.
    /// </summary>
    /// <param name="sourceObjectNumber">The object number in the source document.</param>
    /// <param name="targetObjectNumber">The object number in the merged document.</param>
    /// <remarks>
    /// This is how the page tree of a source is folded into the single page tree node of the merged
    /// document, and how links to pages that were not selected are kept pointing at a real page.
    /// </remarks>
    public void Redirect(long sourceObjectNumber, int targetObjectNumber)
    {
        targets[sourceObjectNumber] = targetObjectNumber;
        redirected.Add(sourceObjectNumber);
    }

    /// <summary>Registers a rewrite that runs on the copied body of an object.</summary>
    /// <param name="sourceObjectNumber">The object number in the source document.</param>
    /// <param name="rewrite">The rewrite; it receives the copy and returns the body to write.</param>
    public void Override(long sourceObjectNumber, Func<PdfObject, PdfObject> rewrite)
    {
        ArgumentNullException.ThrowIfNull(rewrite);
        overrides[sourceObjectNumber] = rewrite;
    }

    /// <summary>Reserves a target object number for an object of the source and queues its body.</summary>
    /// <param name="sourceObjectNumber">The object number in the source document.</param>
    /// <returns>The object number in the merged document.</returns>
    public int Reserve(long sourceObjectNumber)
    {
        if (targets.TryGetValue(sourceObjectNumber, out var existing))
        {
            return existing;
        }

        var target = writer.AllocateObject();
        targets[sourceObjectNumber] = target;
        Queue(sourceObjectNumber);
        return target;
    }
}
