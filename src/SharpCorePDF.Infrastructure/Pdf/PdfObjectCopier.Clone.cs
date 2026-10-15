namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The cloning and writing half of <see cref="PdfObjectCopier"/>.
/// </summary>
internal sealed partial class PdfObjectCopier
{
    /// <summary>Copies a value, reserving every indirect object it refers to.</summary>
    /// <param name="value">The value to copy.</param>
    /// <returns>The copy, which shares no containers or streams with the source.</returns>
    public PdfObject Clone(PdfObject? value) => value switch
    {
        null or PdfNull => PdfNull.Instance,
        PdfBoolean boolean => PdfBoolean.From(boolean.Value),
        PdfInteger integer => new PdfInteger(integer.Value),
        PdfReal real => new PdfReal(real.Value),
        PdfName name => new PdfName(name.Value),
        PdfString text => new PdfString(text.Bytes, text.IsHexadecimal),
        PdfReference reference => new PdfReference(Reserve(reference.ObjectNumber), 0),
        PdfArray array => CloneArray(array),
        PdfStream stream => CloneStream(stream),
        PdfDictionary dictionary => CloneDictionary(dictionary),
        _ => PdfNull.Instance,
    };

    /// <summary>Writes every reserved object whose body has not been written yet.</summary>
    /// <remarks>
    /// Cloning a body can reserve further objects, so the queue is drained in a loop; because a
    /// mapping is registered before its body is written, a cycle terminates.
    /// </remarks>
    public void Flush()
    {
        while (pending.Count > 0)
        {
            var sourceObjectNumber = pending.Dequeue();
            queued.Remove(sourceObjectNumber);

            if (redirected.Contains(sourceObjectNumber))
            {
                continue;
            }

            var body = Clone(source.Resolve(sourceObjectNumber));

            if (overrides.TryGetValue(sourceObjectNumber, out var rewrite))
            {
                body = rewrite(body);
            }

            writer.WriteObject(targets[sourceObjectNumber], body);
        }
    }

    private void Queue(long sourceObjectNumber)
    {
        if (queued.Add(sourceObjectNumber))
        {
            pending.Enqueue(sourceObjectNumber);
        }
    }

    private void CloneInto(PdfDictionary targetDictionary, PdfDictionary sourceDictionary)
    {
        foreach (var entry in sourceDictionary.Entries)
        {
            targetDictionary.Set(entry.Key, Clone(entry.Value));
        }
    }

    private PdfArray CloneArray(PdfArray array)
    {
        var copy = new PdfArray();

        foreach (var item in array.Items)
        {
            copy.Add(Clone(item));
        }

        return copy;
    }

    private PdfDictionary CloneDictionary(PdfDictionary dictionary)
    {
        var copy = new PdfDictionary();
        CloneInto(copy, dictionary);
        return copy;
    }

    private PdfStream CloneStream(PdfStream stream)
    {
        var dictionary = CloneDictionary(stream.Dictionary);

        // The written form always states the exact length as a direct integer, so a stored /Length
        // (which may be an indirect reference) must not drag another object into the result.
        dictionary.Remove("Length");

        return new PdfStream(dictionary, stream.Raw);
    }
}
