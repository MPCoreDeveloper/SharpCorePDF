# 9. Errors, Exceptions and Warnings

SharpCorePDF separates three kinds of bad news:

- **Programmer errors** — a null argument, a stream that cannot be written, invalid page notation —
  throw the standard .NET exceptions (`ArgumentNullException`, `ArgumentException`,
  `FormatException`). They indicate a bug in the calling code, not a problem with a document.
- **Document problems** — a malformed source, a missing password, an unsupported compression filter,
  an impossible plan — throw a subclass of `PdfException`.
- **Non-fatal remarks** — a repaired cross-reference table, a bookmark that had to be skipped, a
  feature deliberately dropped by policy — are returned in `Warnings` and never throw.

Everything in the third group is reported on `PdfDocumentInfo.Warnings` when inspecting and on
`MergeResult.Warnings` after a merge. Treating a warning as an error is a policy decision, not
something the library forces on you.

## The exception family

`PdfException` is abstract and is the base of every PDF-related error. Catching it catches all of them.

| Exception | Meaning | Useful members |
| --- | --- | --- |
| `MergePlanValidationException` | A plan invariant is broken, or a page selection does not fit its source | `Errors` |
| `PdfSourceFormatException` | A source is not a readable PDF, or its structure cannot be trusted | `SourceName`, `Detail` |
| `PdfEncryptionException` | A source is encrypted and could not be opened | `SourceName`, `RequiresPassword` |
| `PdfUnsupportedFeatureException` | A source uses a PDF feature the engine deliberately does not implement | `SourceName`, `Feature` |

`MergePlanValidationException` is unusual in carrying more than one message. `Errors` lists every
problem found in one pass, and `Message` joins them. Show the list if you are reporting to a developer;
show the first item if you are reporting to a user.

`PdfSourceFormatException` and `PdfEncryptionException` name the offending source, which matters when
a plan has ten sources. `PdfUnsupportedFeatureException` names the feature — for example an unsupported
compression filter for an object stream — so the message can say what the tool would need to support
rather than only that the file failed.

## What throws when

The sequence inside `MergeAsync` explains where each exception comes from:

1. `plan.EnsureValid()` runs first. A plan with no sources, a primary source that is not in the plan, a
   blank display name, an undefined policy value or invalid encryption settings throws
   `MergePlanValidationException` before any file is opened.
2. Each source is opened and read. A missing file throws `IOException` or
   `UnauthorizedAccessException` from the content source; malformed content throws
   `PdfSourceFormatException`; a source that stays encrypted throws `PdfEncryptionException`.
3. Each page selection is resolved against the real page count. A range that starts past the last
   page, or a document with no pages, throws `MergePlanValidationException`.
4. The engine writes pages. Unsupported filters and malformed objects discovered here still surface as
   `PdfException` subclasses, not raw index errors.
5. Cancellation is observed between sources and during writing and surfaces as
   `OperationCanceledException`, the standard .NET contract.

Because steps 1 and 3 happen before the engine writes, a bad plan or a bad page range does not produce a
partial PDF in the destination stream. It does not protect an existing file, however: `MergeToFileAsync`
creates and truncates the destination before it calls the merger. The section on writing files safely
later in this chapter shows the pattern that avoids this.

## Handling exceptions

Catch specific types from the most specific to the least, so each gets the response it deserves:

```csharp
try
{
    plan.EnsureValid();
    var result = await merger.MergeToFileAsync(plan, destination, cancellationToken);
    Console.WriteLine(result);
}
catch (MergePlanValidationException exception)
{
    // The caller can fix this: bad range, empty plan, missing primary source.
    foreach (var error in exception.Errors)
    {
        Console.WriteLine($"plan: {error}");
    }
}
catch (PdfEncryptionException exception) when (exception.RequiresPassword)
{
    // Ask for a password for exception.SourceName and retry.
    Console.WriteLine($"'{exception.SourceName}' needs a password.");
}
catch (PdfEncryptionException exception)
{
    // Unsupported security handler: no password will help.
    Console.WriteLine(exception.Message);
}
catch (PdfSourceFormatException exception)
{
    Console.WriteLine($"'{exception.SourceName}' is damaged: {exception.Detail}");
}
catch (PdfUnsupportedFeatureException exception)
{
    Console.WriteLine($"'{exception.SourceName}' uses {exception.Feature}: {exception.Message}");
}
catch (PdfException exception)
{
    // Any PDF-related failure that the cases above did not name.
    Console.WriteLine(exception.Message);
}
catch (OperationCanceledException)
{
    Console.WriteLine("The merge was cancelled.");
}
catch (IOException exception)
{
    Console.WriteLine($"The file system refused the merge: {exception.Message}");
}
```

Three details are easy to get wrong:

- **`OperationCanceledException` is not a `PdfException`.** It derives from `SystemException` directly,
  so a `catch (PdfException)` will not see a cancellation. Catch it separately, and do not log it as a
  failure.
- **`IOException` is not a `PdfException` either.** A missing input file, a read-only destination or a
  full disk is a file-system problem. It can occur at any step, including while a source is read.
- **Order matters.** `PdfEncryptionException` and `PdfSourceFormatException` both derive from
  `PdfException`; catching the base first would hide the members that make them actionable.

`ArgumentException`, `ArgumentNullException` and `FormatException` are deliberately not caught in this
example. They mean the code built the plan incorrectly — a null stream, an unwritable destination, a
malformed page string — and the fix belongs in the code, not in a retry loop. Validate page text with
`PageSelection.TryParse` before building the plan, so a typo becomes a validation message rather than an
exception in the middle of a request.

## Writing files safely

`MergeToFileAsync` is convenient and is the right call for a scratch file, but it creates the
destination with `FileMode.Create` before the merge begins. If the merge then fails or is cancelled,
the destination is left empty or partially written. Two consequences:

1. **Never merge a file onto itself.** If the destination is also a source path, the source is
   truncated before it is read. The merge fails and the original is gone.
2. **Do not treat a returned file as committed until the call returns successfully.** A watcher that
   processes the directory may see a partial file while the merge is still running.

For anything that matters, write to a temporary file in the same directory and move it into place only
after success:

```csharp
var fullPath = Path.GetFullPath(destination);
var directory = Path.GetDirectoryName(fullPath)!;
Directory.CreateDirectory(directory);

var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

try
{
    await merger.MergeToFileAsync(plan, temporary, cancellationToken);
    File.Move(temporary, fullPath, overwrite: true);
}
finally
{
    if (File.Exists(temporary))
    {
        File.Delete(temporary);
    }
}
```

The temporary file lives in the same directory, so the move stays on one volume, where it is atomic on
Windows and on the common Unix file systems. The `finally` block removes the temporary file on failure
and on cancellation; after a successful move the file is already gone. The destination is either the
previous file or the complete new one, never a half-written document.

`MergeToBytesAsync` has the same all-or-nothing property for a different reason: the buffer is local to
the call, so a failure returns no bytes and leaves nothing behind. It is the simplest safe choice when
the result fits in memory.

## Warnings versus errors

A warning never changes the outcome: the merge completed and the result is usable. The distinction is
worth preserving in your own API, because collapsing warnings into errors makes the library look less
reliable than it is, and ignoring them hides real repairs.

A reasonable policy:

| Warning group | Suggested response |
| --- | --- |
| Structure repaired (`CrossReferenceRebuilt`, `ObjectStreamTruncated`) | Log; keep the source; investigate if it recurs |
| Page tree (`PageTreeMissing`, `PageSizeAssumed`) | Log at warning level; compare the page count with expectations |
| Encryption (`EncryptionOpened`) | Log at information level; the password worked |
| Merge decisions (`OutlinesDroppedByPolicy`, `NamedDestinationsDropped`) | Log at debug level; expected when the plan drops features |
| `OutputVerified` | Log at information level; verification was requested and passed |

If your product has a "strict" mode, implement it in your code by inspecting `result.Warnings` and
deciding which kinds are unacceptable — do not expect the merger to throw for them. Chapter 8 lists the
kinds and their meanings.

## Reproducing a failure

When a merge fails, the report should contain the plan and the exception, not only the message:

```csharp
catch (PdfException exception)
{
    logger.LogError(exception, "Merge failed. Plan: {Plan}", plan.ToString());
}
```

`MergePlan.ToString()` summarises the source count, target version and policies without printing page
content. `PdfDocumentInfo.ToString()` and `MergeResult.ToString()` do the same for a document and a
completed merge. None of them includes passwords: `PdfSource.ToString()` prints the display name and
page selection only.

Do not log the plan *object* with structured logging, though. The properties are public, and
`MergeOutputOptions` is a record whose generated `ToString()` would include the encryption options and
their passwords. Log `plan.ToString()` or the individual non-secret fields.
