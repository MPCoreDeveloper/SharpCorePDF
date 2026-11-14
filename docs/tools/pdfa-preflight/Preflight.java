/*
 * The second opinion for docs/tools/validate-pdfa-cross.py: Apache PDFBox preflight, run against a file
 * this library wrote.
 *
 * It exists because veraPDF is one implementation, and one implementation can be wrong in a way that is
 * invisible from inside a repository that trusts it. PDFBox shares no code with veraPDF and none with
 * SharpCorePDF, so agreement between the two is evidence that neither invented the answer.
 *
 * PDFBox 3.0.8 implements PDF/A-1 only -- org.apache.pdfbox.preflight.Format has PDF_A1B and PDF_A1A and
 * nothing else -- so `1b` is the only profile it can vote on. For a PDF/A-2b or -3b document this program
 * is asked to `parse` instead, which is the honest smaller claim: a completely different parser read the
 * file, its cross-reference table, its streams and its filters. It is not a conformance verdict and the
 * tool never prints one for those profiles.
 *
 * Compiled by the tool that calls it, against the preflight-app jar, so nothing here has to be built ahead
 * of time and no Java build system enters the repository.
 *
 *   javac -cp preflight-app-<version>.jar -d <directory> docs/tools/pdfa-preflight/Preflight.java
 *   java -cp preflight-app-<version>.jar:<directory> Preflight 1b archive-1b.pdf
 */

import java.io.File;
import java.util.List;

import org.apache.pdfbox.pdmodel.PDDocument;
import org.apache.pdfbox.preflight.Format;
import org.apache.pdfbox.preflight.PreflightDocument;
import org.apache.pdfbox.preflight.ValidationResult;
import org.apache.pdfbox.preflight.parser.PreflightParser;

public final class Preflight {
    public static void main(String[] args) {
        if (args.length != 2) {
            System.err.println("usage: Preflight <1b|1a|parse> <file.pdf>");
            System.exit(2);
        }

        String mode = args[0];
        File file = new File(args[1]);

        try {
            if ("parse".equals(mode)) {
                try (PDDocument document = new PreflightParser(file).parse()) {
                    System.out.println("PARSED " + document.getNumberOfPages() + " page(s)");
                }

                return;
            }

            Format format = switch (mode) {
                case "1b" -> Format.PDF_A1B;
                case "1a" -> Format.PDF_A1A;
                default -> throw new IllegalArgumentException("unknown profile: " + mode);
            };

            ValidationResult result;

            try (PreflightDocument document =
                     (PreflightDocument) new PreflightParser(file).parse(format)) {
                result = document.validate();
            }

            List<ValidationResult.ValidationError> errors = result.getErrorsList();

            if (result.isValid()) {
                System.out.println(
                    "PASS " + (errors.isEmpty() ? "no errors" : errors.size() + " warning(s)"));

                return;
            }

            System.out.println("FAIL " + errors.size() + " error(s)");

            for (ValidationResult.ValidationError error : errors) {
                System.out.println("  " + error.getErrorCode() + ": " + error.getDetails());
            }

            System.exit(1);
        } catch (Exception failure) {
            System.out.println(
                "ERROR " + failure.getClass().getSimpleName() + ": " + failure.getMessage());
            System.exit(1);
        }
    }

    private Preflight() {
    }
}
