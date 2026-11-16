"""Checks every font program embedded in a PDF with fontTools and with OTS, the sanitizer browsers run.

Run with:  python docs/tools/validate-fonts.py [--strict-ots] <file.pdf | directory>...
           (after: uv pip install -r docs/tools/validation-requirements.txt)

veraPDF checks that a font is embedded and that the widths the PDF states match the program; it does not
read the program's tables. A subset whose loca table disagrees with glyf, whose checksums are wrong or whose
glyphs do not decompile still passes PDF/A validation and still makes Acrobat report that it "cannot extract
the embedded font", so the program itself is read here, by implementations that share no code with the
subsetter that wrote it:

* TrueType (FontFile2) and OpenType (FontFile3 /OpenType) programs are loaded by fontTools with checksum
  verification on; the tables a PDF TrueType program needs (head, hhea, hmtx, loca, maxp, glyf) must be
  present, loca must index numGlyphs + 1 offsets, every glyph must decompile, and every glyph a composite
  refers to must exist;
* CFF programs (FontFile3 /Type1C and /CIDFontType0C) are parsed by fontTools and every charstring decompiled;
* the widths the PDF states for a CIDFontType2 with an identity CIDToGIDMap must match the program's advance
  widths, scaled to 1000 units, within one unit;
* OTS runs on every TrueType program. A PDF subset may legally omit the cmap, name, OS/2 and post tables OTS
  insists on, so OTS rejections are printed but do not fail the run unless --strict-ots is given.

Type 1 programs (FontFile) are listed and not checked.
"""

import io
import subprocess
import sys
import tempfile
from pathlib import Path

REQUIRED_TRUETYPE_TABLES = {"head", "hhea", "hmtx", "loca", "maxp", "glyf"}


def font_dictionaries(pdf):
    """Yields every font dictionary the pages and the forms they paint reach, once each."""
    import pikepdf

    seen = set()
    pending = [page.obj.get("/Resources") for page in pdf.pages]

    while pending:
        resources = pending.pop()

        if not isinstance(resources, pikepdf.Dictionary):
            continue

        for font in (resources.get("/Font") or {}).values():
            key = font.objgen if font.is_indirect else id(font)

            if key not in seen:
                seen.add(key)
                yield font

                for descendant in font.get("/DescendantFonts", []):
                    descendant_key = descendant.objgen if descendant.is_indirect else id(descendant)

                    if descendant_key not in seen:
                        seen.add(descendant_key)
                        yield descendant

        for xobject in (resources.get("/XObject") or {}).values():
            key = xobject.objgen if xobject.is_indirect else id(xobject)

            if key not in seen and xobject.get("/Subtype") == "/Form":
                seen.add(key)
                pending.append(xobject.get("/Resources"))


def check_truetype(data: bytes, font) -> list[str]:
    from fontTools.ttLib import TTFont

    problems = []

    try:
        program = TTFont(io.BytesIO(data), checkChecksums=2, lazy=False)
    except Exception as error:  # noqa: BLE001 - a program fontTools cannot load is the finding
        return [f"fontTools cannot load it: {type(error).__name__}: {error}"]

    missing = REQUIRED_TRUETYPE_TABLES - set(program.keys())

    if missing:
        return [f"lacks the table(s) {sorted(missing)}"]

    count = program["maxp"].numGlyphs
    glyf = program["glyf"]
    names = program.getGlyphOrder()

    if len(program["loca"]) != count + 1:
        problems.append(f"loca has {len(program['loca'])} offsets for {count} glyphs")

    for name in names:
        try:
            glyph = glyf[name]

            if glyph.isComposite():
                for component in glyph.components:
                    if component.glyphName not in glyf:
                        problems.append(f"composite glyph {name} refers to the missing glyph {component.glyphName}")
        except Exception as error:  # noqa: BLE001
            problems.append(f"glyph {name} does not decompile: {type(error).__name__}: {error}")

    problems.extend(check_cid_widths(program, font))
    return problems


def check_cid_widths(program, font) -> list[str]:
    """Compares the /W array of a CIDFontType2 with an identity CIDToGIDMap against the program's advances."""
    if font.get("/Subtype") != "/CIDFontType2" or "/W" not in font:
        return []

    if font.get("/CIDToGIDMap", "/Identity") != "/Identity":
        return []

    scale = 1000 / program["head"].unitsPerEm
    metrics = program["hmtx"].metrics
    order = program.getGlyphOrder()
    problems = []
    items = list(font["/W"])
    index = 0

    def compare(cid: int, stated: float) -> None:
        if cid >= len(order):
            problems.append(f"/W states a width for CID {cid}, past the {len(order)} glyphs of the program")
            return

        advance = metrics[order[cid]][0] * scale

        if abs(advance - stated) > 1:
            problems.append(f"CID {cid}: /W says {stated:g}, the program's advance is {advance:.1f}")

    while index < len(items):
        first = int(items[index])

        if isinstance(items[index + 1], (list,)) or hasattr(items[index + 1], "as_list"):
            for offset, width in enumerate(items[index + 1]):
                compare(first + offset, float(width))
            index += 2
        else:
            last, width = int(items[index + 1]), float(items[index + 2])
            for cid in range(first, last + 1):
                compare(cid, width)
            index += 3

    return problems


def check_cff(data: bytes) -> list[str]:
    from fontTools.cffLib import CFFFontSet

    try:
        fonts = CFFFontSet()
        fonts.decompile(io.BytesIO(data), None)

        for name in fonts.keys():
            strings = fonts[name].CharStrings

            for glyph in strings.keys():
                strings[glyph].decompile()
    except Exception as error:  # noqa: BLE001
        return [f"fontTools cannot read the CFF program: {type(error).__name__}: {error}"]

    return []


def run_ots(data: bytes) -> str | None:
    """Returns OTS's complaint about a program, or None when it accepts it."""
    import ots

    with tempfile.TemporaryDirectory() as directory:
        source = Path(directory) / "font.ttf"
        source.write_bytes(data)
        result = ots.sanitize(str(source), str(Path(directory) / "sanitized.ttf"), capture_output=True)

        if result.returncode == 0:
            return None

        message = (result.stderr or result.stdout or b"").decode("utf-8", "replace").strip()
        return message.splitlines()[0] if message else f"exit code {result.returncode}"


def check(path: Path, strict_ots: bool) -> int:
    import pikepdf

    print(f"== {path}")
    failures = 0

    with pikepdf.open(path) as pdf:
        for font in font_dictionaries(pdf):
            descriptor = font.get("/FontDescriptor")
            name = str(font.get("/BaseFont", "?"))

            if descriptor is None:
                continue

            for key, kind in (("/FontFile2", "TrueType"), ("/FontFile3", None), ("/FontFile", "Type 1")):
                program = descriptor.get(key)

                if program is None:
                    continue

                data = bytes(program.read_bytes())
                subtype = str(program.get("/Subtype", ""))
                kind = kind or {"/OpenType": "OpenType", "/Type1C": "CFF", "/CIDFontType0C": "CFF"}.get(subtype, subtype)

                if kind in ("TrueType", "OpenType"):
                    problems = check_truetype(data, font)
                    complaint = run_ots(data) if kind == "TrueType" else None
                elif kind == "CFF":
                    problems, complaint = check_cff(data), None
                else:
                    print(f"  {name}: {kind} program, {len(data)} bytes, not checked")
                    continue

                if complaint is not None:
                    note = f"OTS rejects it: {complaint}"

                    if strict_ots:
                        problems.append(note)
                    else:
                        print(f"  {name}: (informational) {note}")

                failures += len(problems)
                print(f"  {name}: {kind} program, {len(data)} bytes: " + ("ok" if not problems else "FAILED"))

                for problem in problems[:20]:
                    print(f"    {problem}")

                if len(problems) > 20:
                    print(f"    ... and {len(problems) - 20} more")

    return failures


def main() -> None:
    arguments = sys.argv[1:]
    strict_ots = "--strict-ots" in arguments
    arguments = [argument for argument in arguments if argument != "--strict-ots"]

    if not arguments or arguments[0] in ("-h", "--help"):
        print(__doc__)
        sys.exit(2)

    files = []

    for argument in arguments:
        path = Path(argument)
        files.extend(sorted(path.glob("*.pdf")) if path.is_dir() else [path])

    failures = 0

    for path in files:
        try:
            failures += check(path, strict_ots)
        except Exception as error:  # noqa: BLE001 - an unreadable or encrypted file is skipped, and said so
            print(f"== {path}\n  skipped: {type(error).__name__}: {error}")

    print()
    print("embedded fonts: " + ("every program reads cleanly" if failures == 0 else f"{failures} failure(s)"))
    sys.exit(0 if failures == 0 else 1)


if __name__ == "__main__":
    main()
