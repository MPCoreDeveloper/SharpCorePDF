# 7. Encryption and Permissions

A merged document is written in the clear unless you set `Encryption`. That is true even when every
source was encrypted: a merge is a new document, and the passwords of its sources are not carried into
it. Encryption is therefore a decision about the output, made once, with `PdfEncryptionOptions`.

```csharp
var encryption = new PdfEncryptionOptions
{
    Algorithm = PdfEncryptionAlgorithm.AesWith256BitKey,
    UserPassword = "reader",        // opens the document with the permissions below
    OwnerPassword = "publisher",    // opens it with every permission
    Permissions = PdfPermissions.PrintOnly,
};

var plan = MergePlan.Create()
    .AddFile("report.pdf", password: "source-password")
    .WithOutput(options => options with { Encryption = encryption });

plan.EnsureValid();
```

| Setting | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Algorithm` | `PdfEncryptionAlgorithm` | `AesWith256BitKey` | The cryptosystem written into the security dictionary |
| `UserPassword` | `string` | `""` | Opens the document with `Permissions` |
| `OwnerPassword` | `string` | `""` | Opens it with every permission, ignoring `Permissions` |
| `Permissions` | `PdfPermissions` | `All` | What a reader who supplied the user password may do |

## Algorithms

| Value | Security handler | Key | Minimum target version | Notes |
| --- | --- | --- | --- | --- |
| `Rc4With40BitKey` | Revision 2 | 40-bit RC4 | 1.4 (any allowed) | Legacy compatibility only; not protection |
| `Rc4With128BitKey` | Revision 3 | 128-bit RC4 | 1.4 (any allowed) | Legacy; RC4 is broken |
| `AesWith128BitKey` | Revision 4 | 128-bit AES | 1.5 | AESV2 crypt filter |
| `AesWith256BitKey` | Revision 6 | 256-bit AES | 1.7 | AESV3 crypt filter, the default |

`AesWith256BitKey` is the default for the same reason it is the default almost everywhere: AES-256
with the hardened revision 6 password hash is the strongest option, and every current reader supports
it. Revision 5 of the standard security handler is deliberately not offered, because its single-pass
SHA-256 password hash was replaced by revision 6 precisely because it was too cheap to attack. The
RC4 algorithms exist for workflows that include a tool which cannot go beyond them; do not choose them
for new documents.

The algorithm and the target version must agree. `Validate(TargetVersion)` reports AES-128 below PDF
1.5 and AES-256 below PDF 1.7, and `MergePlan.Validate()` includes those errors, so the mismatch is
caught before anything is written rather than producing a file that readers reject.

## Passwords

The two passwords have different jobs:

- The **user password** opens the document and is subject to `Permissions`.
- The **owner password** opens the document with every permission, whatever `Permissions` says.

The rules that follow from those jobs:

- **Empty user password.** The document opens without a prompt. This is the shape of a document that
  is only restricted against editing: anyone can read it, and only the owner password lifts the
  restrictions. A cooperating viewer still honours `Permissions` for the reader who did not supply the
  owner password.
- **Empty owner password.** The user password becomes the owner password as well. A document never
  needs more credentials than it was written with, and there is no separate owner secret to lose.
- **Both empty.** Encryption is still applied, with a key derived from empty passwords. Anyone can
  derive that key from the file, so the document is effectively unprotected. The library allows it
  because the difference between "no encryption" and "meaningless encryption" can matter to a
  downstream tool; it is not a sensible choice for a real document.
- **Truncation.** Each revision defines a maximum password length — 32 bytes for revisions 2 to 4 and
  127 bytes for revision 6. A longer password is truncated by the format. `Validate` rejects a
  password whose UTF-8 encoding exceeds the limit, so you learn about the truncation at validation
  time instead of discovering that a shorter password opens the file.
- **Encoding.** Revisions 2 to 4 predate an agreement on how passwords are encoded. A password that
  survives a Latin-1 round trip is written as Latin-1 and anything else as UTF-8, which is what Adobe,
  qpdf and pypdf hash as well. Revisions 5 and 6 are UTF-8 by definition. SASL preparation is not
  applied, matching what readers accept.

Both passwords are checked by validation, so a password that is too long for the chosen algorithm is
reported as a validation problem before the merge starts, rather than silently truncated.

## Permissions

`PdfPermissions` is a flags enumeration whose values are the permission bits of the PDF encryption
dictionary. Combine them with `|`.

| Flag | Allows |
| --- | --- |
| `Print` | Printing |
| `PrintHighQuality` | Printing at a quality that reproduces the content faithfully |
| `ModifyContents` | Changing the pages and the document as a whole |
| `CopyContent` | Copying text and graphics out |
| `Annotate` | Adding and changing annotations, and filling in form fields |
| `FillFormFields` | Filling in form fields even when `Annotate` is not allowed |
| `ExtractForAccessibility` | Screen readers and similar tools extracting content even without `CopyContent` |
| `Assemble` | Inserting, rotating and deleting pages, and building bookmarks and thumbnails |
| `All` | Every flag above — a document that only needs a password to open |
| `PrintOnly` | `Print \| ExtractForAccessibility` — readable and printable, nothing else |
| `None` | Nothing beyond viewing the pages |

Permissions are **advisory, not enforcement**. They tell a cooperating viewer what to offer. A reader
that ignores them, a tool that parses the file directly, or anyone with the owner password is not
stopped. PDF encryption protects the bytes of strings and streams; it does not implement digital
rights management, and it should not be described to users as if it did. For documents that must not
be copied, the realistic control is who receives the file.

Two smaller consequences of the flags:

- Revision 2 does not define `FillFormFields` or the flags after it. They are still written, but a
  reader that predates them ignores those bits, so the extra actions you allowed are simply not
  offered. If you target the oldest readers, restrict yourself to `Print`, `ModifyContents`,
  `CopyContent` and `Annotate`.
- An empty user password with `PrintOnly` is the usual shape of a "read and print, do not edit"
  document: no password to open, an owner password for anyone who needs to change it, and a viewer
  that offers only the permitted actions.

## Encrypted sources

The encryption of a source and the encryption of the result are unrelated. A source is opened with
the password given to `PdfSource.Create`, `AddFile`, `AddBytes` or `AddStream`; that password may be
either the user or the owner password, and `null` is correct for a document that needs none.

```csharp
var plan = MergePlan.Create()
    .AddFile("confidential.pdf", password: "reader")
    .AddFile("public.pdf")
    .WithOutput(options => options with
    {
        Encryption = new PdfEncryptionOptions
        {
            UserPassword = "reader-2025",
            OwnerPassword = "publisher-2025",
            Permissions = PdfPermissions.Print | PdfPermissions.CopyContent,
        },
    });
```

Decryption happens while the source is read. The result is written from the parsed object model and
then encrypted with the new settings, so the source passwords leave no trace in the output and do not
have to be reused. It also means the result inherits nothing: an encrypted source merged without
`Encryption` produces a clear document, and a clear source merged with `Encryption` produces an
encrypted one.

When a source cannot be opened, the merger throws `PdfEncryptionException` with `RequiresPassword`
telling you which problem you have:

- `RequiresPassword == true` — the password was missing or wrong. Ask for one and retry.
- `RequiresPassword == false` — the document uses a security handler the engine does not implement, or
  its encryption dictionary is malformed. No password will help.

Inspection never throws for an encrypted document (Chapter 8). It reports `RequiresPassword` and
`SecurityHandlerRevision` on `PdfDocumentInfo` instead, which is how a caller can ask for a password
before attempting a merge:

```csharp
var info = await inspector.InspectAsync(new PdfFileSource("confidential.pdf"));

if (info.RequiresPassword)
{
    Console.WriteLine($"'{info.SourceName}' needs a password.");
}
```

## Reproducibility and encryption

Encryption draws fresh random material for the file key, the password salts and every initialization
vector. Two encrypted merges of identical input with identical settings are therefore not
byte-identical, even with a fixed `ModificationDate` (Chapter 6). They are equivalent documents with
different bytes; compare their content, not their hashes.

This is the expected behaviour, not a defect: reusing an initialization vector or a salt across
documents would weaken the encryption. If you need reproducible bytes for caching or auditing, produce
the unencrypted document reproducibly and encrypt it in a separate step whose output you do not
compare byte for byte.

