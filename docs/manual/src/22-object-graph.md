# 22. The Object Graph

Chapters 20 and 21 read what a page **means**. This chapter reads what the file **is**: one node per object
the cross-reference table lists, and one entry per operator a page's content stream states. It is the
surface to reach for when a document misbehaves, when a producer writes something unexpected, or when an
audit has to show that nothing was hidden.

`IPdfObjectInspector` offers the two reads and writes nothing:

```csharp
using SharpCorePDF.Domain.Ports;

var objects = await objectInspector.ReadObjectsAsync(
    new PdfFileSource("suspect.pdf"),
    cancellationToken: cancellationToken);

Console.WriteLine($"{objects.Count} object(s), {objects.Warnings.Count} warning(s)");
```

Reading the graph resolves every object the file lists. That is what makes it the diagnostic surface: a
document that merges badly is a document whose graph you can look at.

## A node per object

`PdfObjectView` is an immutable record. The shape is deliberately small — it says what an object is, not
what it is for:

| Property | Type | Meaning |
| --- | --- | --- |
| `Kind` | `PdfObjectViewKind` | The type of the object: null, boolean, integer, real, string, name, array, dictionary, stream or reference |
| `Key` | `string?` | The key this node holds in its parent dictionary, when it has one |
| `ObjectNumber` | `long?` | The object number, for the roots of the graph |
| `Generation` | `int` | The generation of that object number |
| `Scalar` | `object?` | The value of a scalar node |
| `Reference` | `PdfObjectReference?` | The object number and generation a reference names |
| `IsResolved` | `bool` | Whether the document actually contains what this node names |
| `Children` | `IReadOnlyList<PdfObjectView>` | The entries of an array or a dictionary, in document order |
| `StreamLength` | `long?` | The encoded length of a stream |
| `Filter` | `string?` | The filters a stream declares |

The nodes come back in ascending object number, one per object the file lists. A walk is depth first, and
`Key` is what makes a projection readable: `/Root` → `/Pages` → `/Kids` is visible in the result rather than
inferred from a position.

## References are reported, never followed

This is the one rule that makes the graph safe and finite:

* A reference is a node of kind `Reference` that states the number and generation it names. It is not
  replaced by the object it points at, and it is not walked into.
* An object that points back at itself — a page whose `/Parent` chain returns to it — therefore produces a
  graph that terminates, without a visited set and without a depth limit on the graph read.
* A reference to an object the file does not list stays in the graph, with `IsResolved` cleared and one
  `DanglingReference` warning for that target. The page that pointed at it is still visible where it was
  named.

An object the file **lists** but cannot open — a broken offset, a stream that will not decode — is also a
node, of kind `Null` and unresolved. That is the difference between "there is no object 42" (no node) and
"object 42 is there but cannot be read" (an unresolved node): a caller sees a gap in the file rather than a
gap in the answer.

```csharp
// A reference that the file does not satisfy is where a document usually breaks first.
var dangling = objects.Items
    .SelectMany(Flatten)
    .Where(node => !node.IsResolved)
    .Select(node => node.Reference?.ObjectNumber ?? node.ObjectNumber)
    .Distinct()
    .ToList();
```

`Flatten` is the reader's own four-line depth-first walk — `Children` is the whole traversal API, and there
is no visitor, callback or cursor to learn.

## Streams without their bytes

A stream node carries `StreamLength` and `Filter`, and never the bytes. Three reasons, all of which a
diagnostic tool wants:

* **The read stays a description.** Handing back megabytes of compressed data would make an inspection of a
  large document a memory event; a length and a filter name is enough to decide whether to look further.
* **The decoder is elsewhere.** `IPdfContentReader` is the port that decodes images, attachments and
  metadata payloads, under the inline budget of Chapter 8. The graph says *that* a stream exists and how
  large it is; the content reader says *what* it decodes to.
* **`Filter` is stated, not applied.** `/FlateDecode`, `/DCTDecode` and a chain of them are reported as the
  document declares them, including filters this library does not implement. Nothing here guesses.

## The operators of a page

`ReadOperationsAsync` reads one page's decoded content stream and reports it as the operators it states, in
the order the page paints them:

```csharp
var operations = await objectInspector.ReadOperationsAsync(
    source,
    pageNumber: 1,
    cancellationToken: cancellationToken);

foreach (var operation in operations.Items)
{
    Console.WriteLine($"{operation.Offset,6}  {operation.Operator} {string.Join(' ', operation.Operands)}");
}
```

`PdfContentOperationView` has four members: `Operator` (the two-or-three-letter name), `Operands` (the
`PdfObjectView` nodes that came before it), `Offset` (where in the decoded stream it starts) and
`PageNumber`.

The stream is **tokenized, not interpreted**. An operator this library implements nothing for is reported
with its operands exactly like one it does — an audit has to see what the document says, not what the reader
understood. Two consequences of that restraint:

* **Inline images are one operation.** `BI` is reported with the entries of its header; its samples are
  skipped, because samples are not operators, and the image read is where an inline image is described.
* **Nesting is bounded.** An operand that nests deeper than `PdfEngineOptions.MaxNestingDepth` stops being
  descended, and the read says so once with `ContentNestingTooDeep`. A hostile document cannot turn an
  inspection into an unbounded walk.

Two edges of the call are deliberate:

* **A page that does not exist is an empty result, not an exception.** "There is no page 500 in a
  three-page file" is an answer to a question about a document.
* **A page number below one throws `ArgumentOutOfRangeException`.** Page numbers are one-based everywhere
  in this library (Chapter 20's convention, stated in the preface); zero is a caller's mistake, not a
  document's.

## What the graph is for

| Question | Where the answer is |
| --- | --- |
| Does this file reference objects it does not contain? | `DanglingReference` warnings, and nodes with `IsResolved` cleared |
| Which objects did the file list, and how do they hang together? | `ReadObjectsAsync`, walked through `Children` |
| What does page 3 actually paint, in order? | `ReadOperationsAsync(source, 3)` |
| Why does a viewer show a page this library says is blank? | The page's `/Contents` stream length and filters, then the operation list |
| Is the producer writing something this library refuses to execute? | The operations: a `Do`, `BI`, or an action entry the merger copies or drops with a warning |

What it is not:

* **It is not a mutation API.** There is no setter, no `Save`, and no way to edit an object and write the
  document back. Editing is `IPdfDocumentEditor`, and it works on the page assembly, not on objects
  (Chapter 14).
* **It is not a round-trip parser.** The graph does not preserve the byte layout of what it read, so it
  cannot be written back as the same file.
* **It is not an interpreter.** The operators are reported; nothing is executed — no JavaScript, no action,
  no external file ([ADR-001](../adr/0001-pdf-platform-boundary.md)).
* **It is not unlimited.** Reading the graph of a large file builds one node per object plus one per entry,
  so an inspection of a very large document allocates in proportion to its object count. Read it when
  something is wrong, not on every request; the recorded cost is in
  [`docs/measurements.md`](../measurements.md).

The `inspect-objects` example prints the first objects of the sample document, counts the dangling
references, and lists the first operators page 1 paints.
