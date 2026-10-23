"""Scores a layout read against the labelled expectations of the parity corpus (SC-004).

Run with:  python docs/tools/score-layout.py <exports> [--labels DIR] [--known-order-mismatch NAME ...]

``<exports>`` is a folder of ALTO documents, one per labelled document, named after the label file:
``two-columns.alto.xml`` is scored against ``two-columns.json``. ``ExportAsync`` writes that format, and the
``extract-layout`` example writes it for its own sample, so any folder of reads can be scored:

    python docs/tools/score-layout.py artifacts/layout-exports

Three measurements, one per SC-004 threshold:

* **Character recall** — how much of the labelled text the read found. The export writes words, so a
  space has no box of its own and the recall is measured over the characters of the words. Matched words
  are paired by page and text rather than by position, so a page read in the wrong order still scores its
  recall: the order has a measurement of its own.
* **Box agreement** — how many matched words cover the same area, within half a point on every edge.
* **Reading-order agreement** — the share of pairs of matched words that sit on different lines on both
  sides and that the read places in the labelled order. Words whose line was split or merged carry no
  evidence and are not counted either way, so the measurement is about order and not about segmentation.
  A page read column by column around a table's rows scores below one even though every word was found.

``--known-order-mismatch NAME`` records a document's order score without failing the run. It exists for a
limitation the labels cannot express, and every name given to it belongs to a test that fails if the
limitation ever goes away.

The tool only reads files: it does not run the reader, and it needs no third-party package.
"""

from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ElementTree
from dataclasses import dataclass
from pathlib import Path

ALTO = "{http://www.loc.gov/standards/alto/ns-v4#}"
BOX_TOLERANCE = 0.5
MIN_RECALL = 0.995
MIN_BOX_AGREEMENT = 0.95
MIN_READING_ORDER = 0.95


@dataclass(frozen=True)
class Box:
    """A box in PDF user space: bottom-left origin, y running up, in points."""

    x0: float
    y0: float
    x1: float
    y1: float

    @staticmethod
    def union(boxes: list[Box]) -> Box:
        """Returns the smallest box that covers every box in ``boxes``."""
        return Box(
            min(box.x0 for box in boxes),
            min(box.y0 for box in boxes),
            max(box.x1 for box in boxes),
            max(box.y1 for box in boxes),
        )

    def agrees_with(self, other: Box, tolerance: float = BOX_TOLERANCE) -> bool:
        """Reports whether every edge of this box is within ``tolerance`` of ``other``."""
        return (
            abs(self.x0 - other.x0) <= tolerance
            and abs(self.y0 - other.y0) <= tolerance
            and abs(self.x1 - other.x1) <= tolerance
            and abs(self.y1 - other.y1) <= tolerance
        )


@dataclass(frozen=True)
class Word:
    """A word and the area it covers."""

    text: str
    box: Box


@dataclass(frozen=True)
class Line:
    """A line of text: its words, in order, and the text they spell."""

    words: list[Word]

    @property
    def text(self) -> str:
        """Returns the words of the line joined by a single space."""
        return " ".join(word.text for word in self.words)


@dataclass(frozen=True)
class Page:
    """One page of a document: its size and the lines it carries, in reading order."""

    number: int
    height: float
    lines: list[Line]


def load_labels(path: Path) -> list[Page]:
    """Reads a label file into pages of lines and words.

    The label records one box per character, so a word is a run of characters that are not spaces, and
    the box of a word is the box that covers those characters.
    """
    data = json.loads(path.read_text(encoding="utf-8"))
    pages = []

    for page in data["pages"]:
        lines = []

        for block in sorted(page["blocks"], key=lambda entry: entry["order"]):
            words: list[Word] = []
            run: list[dict] = []

            for character in block["characters"]:
                if character["text"].strip():
                    run.append(character)
                elif run:
                    words.append(_word(run))
                    run = []

            if run:
                words.append(_word(run))

            lines.append(Line(words))

        pages.append(Page(int(page["page"]), float(page["height"]), lines))

    return pages


def _word(characters: list[dict]) -> Word:
    """Returns the word the characters spell, and the box that covers them."""
    return Word(
        "".join(character["text"] for character in characters),
        Box.union([Box(*[float(value) for value in character["box"]]) for character in characters]),
    )


def load_alto(path: Path) -> list[Page]:
    """Reads an ALTO document into the same shape as a label file.

    ALTO places the origin at the top left corner, so every box is flipped against the height of its
    page to come back to the user space the labels are written in.
    """
    root = ElementTree.parse(path).getroot()
    pages = []

    for number, page in enumerate(root.iter(ALTO + "Page"), start=1):
        height = float(page.get("HEIGHT") or 0)
        lines = []

        for element in page.iter(ALTO + "TextLine"):
            words = [_alto_word(string, height) for string in element.findall(ALTO + "String")]
            lines.append(Line([word for word in words if word is not None]))

        pages.append(Page(int(page.get("PHYSICAL_IMG_NR") or number), height, lines))

    return pages


def _alto_word(element: ElementTree.Element, page_height: float) -> Word | None:
    """Reads one ALTO string, or nothing when it carries no text."""
    text = element.get("CONTENT") or ""

    if not text:
        return None

    hpos = float(element.get("HPOS") or 0)
    vpos = float(element.get("VPOS") or 0)
    width = float(element.get("WIDTH") or 0)
    height = float(element.get("HEIGHT") or 0)

    return Word(text, Box(hpos, page_height - vpos - height, hpos + width, page_height - vpos))


def words_of(pages: list[Page]) -> list[Word]:
    """Returns every word of every page, in the order the pages and lines are in."""
    return [word for page in pages for line in page.lines for word in line.words]


@dataclass(frozen=True)
class Count:
    """A fraction, kept as the two counts that produced it so the output can show them."""

    part: int
    whole: int

    @property
    def value(self) -> float:
        """Returns the fraction, or one when there is nothing to divide."""
        return 1.0 if self.whole == 0 else self.part / self.whole


@dataclass(frozen=True)
class Score:
    """What one document scored on each of the three SC-004 measurements."""

    document: str
    characters: Count
    boxes: Count
    order: Count


def index_words(pages: list[Page]) -> dict[tuple[int, str], list[tuple[int, Word]]]:
    """Indexes the words of a document by page and text, in the order they appear.

    Indexing by text rather than by position is what keeps the text and box measurements independent of
    the reading order: a page read column by column still found the words, and the reading order is the
    measurement that says it read them in the wrong order.
    """
    index: dict[tuple[int, str], list[tuple[int, Word]]] = {}

    for page in pages:
        for line_number, line in enumerate(page.lines):
            for word in line.words:
                index.setdefault((page.number, word.text), []).append((line_number, word))

    return index


def match_words(
    expected: list[Page],
    actual: list[Page],
) -> list[tuple[tuple[int, int, Word], tuple[int, int, Word]]]:
    """Pairs the words both sides found, page by page and text by text.

    A text that occurs more than once is paired in the order it occurs, which is the only pairing two
    spellings of the same word allow without a position: the box measurement then says whether the
    occurrences were found where the labels put them.
    """
    wanted = index_words(expected)
    found = index_words(actual)
    pairs = []

    for key, occurrences in wanted.items():
        for (wanted_line, wanted_word), (found_line, found_word) in zip(
            occurrences, found.get(key, [])
        ):
            pairs.append(
                (
                    (key[0], wanted_line, wanted_word),
                    (key[0], found_line, found_word),
                )
            )

    return pairs


def score_words(expected: list[Page], actual: list[Page]) -> tuple[Count, Count]:
    """Measures how much of the labelled text was found, and how many boxes cover the labelled area."""
    pairs = match_words(expected, actual)

    matched_characters = sum(len(wanted[2].text) for wanted, _ in pairs)
    agreeing_boxes = sum(1 for wanted, found in pairs if wanted[2].box.agrees_with(found[2].box))
    total_characters = sum(len(word.text) for word in words_of(expected))

    return (
        Count(matched_characters, total_characters),
        Count(agreeing_boxes, len(pairs)),
    )


def score_order(expected: list[Page], actual: list[Page]) -> Count:
    """Measures how many pairs of labelled lines the read places in the labelled order.

    Two matched words are compared only when both sit on different lines on both sides: words on one
    labelled line that the read split, or words one block of the read merged, carry no evidence about
    order and are not counted either way. That keeps the measurement about the order and not about the
    segmentation.
    """
    pairs = match_words(expected, actual)
    pairs.sort(key=lambda pair: (pair[0][0], pair[0][1]))

    agreeing = 0
    compared = 0

    for first in range(len(pairs)):
        for second in range(first + 1, len(pairs)):
            wanted_first, found_first = pairs[first]
            wanted_second, found_second = pairs[second]

            if wanted_first[1] == wanted_second[1] or found_first[1] == found_second[1]:
                continue

            compared += 1

            if found_first[1] < found_second[1]:
                agreeing += 1

    return Count(agreeing, compared)


def score_document(labels_path: Path, alto_path: Path) -> Score:
    """Scores one document: its label file against the ALTO export of its read."""
    expected = load_labels(labels_path)
    actual = load_alto(alto_path)

    characters, boxes = score_words(expected, actual)

    return Score(labels_path.stem, characters, boxes, score_order(expected, actual))


def find_export(exports: Path, stem: str) -> Path | None:
    """Returns the export that reports the document ``stem``, or nothing when there is none."""
    for name in (f"{stem}.alto.xml", f"{stem}.xml", f"{stem}.alto"):
        candidate = exports / name

        if candidate.is_file():
            return candidate

    return None


def report(score: Score, known: set[str]) -> bool:
    """Prints one document's scores and reports whether it meets every threshold."""
    print(f"== {score.document}")

    passed = True

    for name, count, threshold in (
        ("recall", score.characters, MIN_RECALL),
        ("boxes", score.boxes, MIN_BOX_AGREEMENT),
        ("reading order", score.order, MIN_READING_ORDER),
    ):
        # Nothing to compare is not a shortfall — a page with one line has no pairs to order — but it is
        # printed as a dash rather than as an agreement, so a vacuous measurement cannot be read as evidence.
        met = count.whole == 0 or count.value >= threshold
        recorded = name == "reading order" and score.document in known
        verdict = "pass" if met else ("recorded" if recorded else "FAIL")
        share = "     --" if count.whole == 0 else f"{count.value:6.1%}"
        note = "  (nothing to compare)" if count.whole == 0 else ""

        print(
            f"   {name:<14} {share} ({count.part:4d}/{count.whole:<4d}) "
            f"needs {threshold:5.1%}  {verdict}{note}"
        )

        passed = passed and (met or recorded)

    return passed


def main() -> int:
    """Scores every labelled document that has an export and prints the result."""
    arguments = sys.argv[1:]
    exports = Path(".")
    labels = Path("tests/SharpCorePDF.Tests/Assets/parity/labelled")
    known: set[str] = set()
    only: set[str] = set()

    index = 0

    while index < len(arguments):
        argument = arguments[index]

        if argument == "--labels":
            labels = Path(arguments[index + 1])
            index += 2
        elif argument == "--known-order-mismatch":
            known.add(arguments[index + 1])
            index += 2
        elif argument == "--only":
            only.add(arguments[index + 1])
            index += 2
        elif argument.startswith("-"):
            print(f"unknown option {argument}")
            return 2
        else:
            exports = Path(argument)
            index += 1

    if not exports.is_dir():
        print(f"{exports} is not a folder")
        return 2

    documents = sorted(path.stem for path in labels.glob("*.json"))

    if only:
        documents = [document for document in documents if document in only]

    scored = 0
    failed = []

    for document in documents:
        export = find_export(exports, document)

        if export is None:
            print(f"== {document}")
            print(f"   no export: expected {document}.alto.xml in {exports}")
            failed.append(document)
            continue

        score = score_document(labels / f"{document}.json", export)
        scored += 1

        if not report(score, known):
            failed.append(document)

    print()
    print(f"{scored} document(s) scored, {len(failed)} short of a threshold")
    print(f"thresholds: recall {MIN_RECALL:.1%}, boxes {MIN_BOX_AGREEMENT:.1%}, "
          f"order {MIN_READING_ORDER:.1%}, tolerance {BOX_TOLERANCE} pt")

    if failed:
        print(f"short: {', '.join(failed)}")

    return 0 if not failed else 1


if __name__ == "__main__":
    sys.exit(main())
