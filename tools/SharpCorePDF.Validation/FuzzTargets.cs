using System.Globalization;
using System.Text;
using SharpCorePDF.Domain.Exceptions;

namespace SharpCorePDF.Validation;

/// <summary>
/// The inputs libFuzzer mutates, and how each one reaches the engine.
/// </summary>
/// <remarks>
/// <para>
/// <c>document</c> hands the input to the engine as a whole file, which is where the lexer, the
/// cross-reference reader, the recovery scan, the security handler and the writers are reached. The other
/// targets wrap the input in a small well-formed document as the data of an image, so the mutations land on
/// one decoder instead of being spent on a cross-reference table: a fuzzer that has to rediscover a valid
/// file before it reaches the JPEG decoder rarely gets there.
/// </para>
/// <para>
/// A decoder target reads its first bytes as the image parameters — size, bit depth, colour space,
/// predictor, CCITT options — so the fuzzer explores those too. Everything goes through the public read use
/// cases, never an internal type, so the harness tests what a caller can reach.
/// </para>
/// <para>
/// A <see cref="PdfException"/> is the contract's way of refusing a document and is not a finding. Any
/// other exception is, and so is a process that dies: libFuzzer records both as crashes.
/// </para>
/// </remarks>
internal static class FuzzTargets
{
    private const int DecoderHeaderLength = 5;

    private static readonly string[] Bits = ["1", "2", "4", "8", "16"];
    private static readonly string[] Spaces = ["/DeviceGray", "/DeviceRGB", "/DeviceCMYK", "[/Indexed /DeviceRGB 1 <000000FFFFFF>]"];
    private static readonly string[] Predictors = ["1", "2", "10", "11", "12", "13", "14", "15"];

    /// <summary>Gets the names of the targets.</summary>
    public static IReadOnlyList<string> Names { get; } = ["document", "dct", "ccitt", "lzw", "flate", "runlength"];

    /// <summary>Determines whether a name is a target.</summary>
    /// <param name="target">The name.</param>
    /// <returns><see langword="true"/> for a known target.</returns>
    public static bool IsKnown(string target) => Names.Contains(target, StringComparer.Ordinal);

    /// <summary>Runs one input through one target.</summary>
    /// <param name="target">The target.</param>
    /// <param name="input">The bytes libFuzzer produced.</param>
    public static void Run(string target, ReadOnlySpan<byte> input)
    {
        var document = Wrap(target, input);

        try
        {
            Probe.ExerciseAsync(document, target + ".pdf", password: null, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (PdfException)
        {
            // The contract's refusal of a document; not a finding.
        }
    }

    /// <summary>Runs a target over saved inputs in this process and reports every input that broke it.</summary>
    /// <param name="target">The target.</param>
    /// <param name="paths">Files, or directories whose files are the inputs.</param>
    /// <returns>0 when every input was read or refused, 3 when one of them broke the engine.</returns>
    public static int Replay(string target, IEnumerable<string> paths)
    {
        if (!IsKnown(target))
        {
            Console.Error.WriteLine($"no such target: {target}");
            return 1;
        }

        var inputs = paths
            .SelectMany(path => Directory.Exists(path) ? Directory.EnumerateFiles(path) : [path])
            .Order(StringComparer.Ordinal)
            .ToList();
        var faults = 0;

        foreach (var input in inputs)
        {
            try
            {
                Run(target, File.ReadAllBytes(input));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                faults++;
                Console.WriteLine($"fault {input}: {exception.GetType().FullName}: {exception.Message}");
                Console.WriteLine(exception.StackTrace);
            }
        }

        Console.WriteLine($"{target}: {inputs.Count} input(s), {faults} fault(s)");
        return faults == 0 ? Probe.Read : Probe.EngineFault;
    }

    /// <summary>Writes sample files as seeds for a target, prefixing the parameter header a decoder target reads.</summary>
    /// <param name="target">The target.</param>
    /// <param name="directory">The directory the seeds are written to.</param>
    /// <param name="files">The sample files: documents for <c>document</c>, encoded data for the others.</param>
    /// <returns>0 when the seeds were written.</returns>
    public static int Seed(string target, string directory, IEnumerable<string> files)
    {
        if (!IsKnown(target))
        {
            Console.Error.WriteLine($"no such target: {target}");
            return 1;
        }

        Directory.CreateDirectory(directory);
        var count = 0;

        foreach (var file in files.SelectMany(path => Directory.Exists(path) ? Directory.EnumerateFiles(path) : [path]))
        {
            var data = File.ReadAllBytes(file);

            // A decoder seed starts with a header that asks for an 8-bit grey 64 x 64 image with no predictor;
            // the fuzzer moves away from it as it mutates.
            byte[] seed = target == "document" ? data : [3, 0, 0, 63, 63, .. data];

            File.WriteAllBytes(Path.Combine(directory, $"{target}-{count++:D4}"), seed);
        }

        Console.WriteLine($"{target}: {count} seed(s) in {directory}");
        return 0;
    }

    /// <summary>Turns an input into the document the target hands to the engine.</summary>
    /// <param name="target">The target.</param>
    /// <param name="input">The input.</param>
    /// <returns>The document.</returns>
    public static byte[] Wrap(string target, ReadOnlySpan<byte> input)
    {
        if (target == "document")
        {
            return input.ToArray();
        }

        Span<byte> header = stackalloc byte[DecoderHeaderLength];
        input[..Math.Min(DecoderHeaderLength, input.Length)].CopyTo(header);
        var data = input.Length > DecoderHeaderLength ? input[DecoderHeaderLength..] : [];

        var width = 1 + header[3];
        var height = 1 + header[4];
        var bits = Bits[header[0] % Bits.Length];
        var space = Spaces[header[1] % Spaces.Length];
        var parameters = string.Empty;
        string filter;

        switch (target)
        {
            case "dct":
                filter = "/DCTDecode";
                bits = "8";
                space = Spaces[header[1] % 3];
                break;

            case "ccitt":
                // K below zero is Group 4, zero is one-dimensional Group 3 and above zero is mixed Group 3.
                filter = "/CCITTFaxDecode";
                bits = "1";
                space = "/DeviceGray";
                parameters = string.Create(
                    CultureInfo.InvariantCulture,
                    $"/DecodeParms << /K {(header[0] % 3) - 1} /Columns {width} /Rows {height} /BlackIs1 {Flag(header[2], 0)} /EncodedByteAlign {Flag(header[2], 1)} /EndOfLine {Flag(header[2], 2)} >>");
                break;

            case "lzw":
                filter = "/LZWDecode";
                parameters = Predicted(header, earlyChange: (header[2] & 0x80) == 0 ? 1 : 0, space, bits, width);
                break;

            case "flate":
                filter = "/FlateDecode";
                parameters = Predicted(header, earlyChange: null, space, bits, width);
                break;

            case "runlength":
                filter = "/RunLengthDecode";
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "no such target");
        }

        var image = string.Create(
            CultureInfo.InvariantCulture,
            $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /BitsPerComponent {bits} /ColorSpace {space} /Filter {filter} {parameters} /Length {data.Length} >>");

        return MinimalDocument(image, data);
    }

    private static string Flag(byte value, int bit) => ((value >> bit) & 1) == 1 ? "true" : "false";

    private static string Predicted(ReadOnlySpan<byte> header, int? earlyChange, string space, string bits, int width)
    {
        var colors = space switch
        {
            "/DeviceRGB" => 3,
            "/DeviceCMYK" => 4,
            _ => 1,
        };
        var early = earlyChange is { } value ? $" /EarlyChange {value}" : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"/DecodeParms << /Predictor {Predictors[header[2] % Predictors.Length]} /Colors {colors} /BitsPerComponent {bits} /Columns {width}{early} >>");
    }

    /// <summary>Writes a one page document that paints one image, with a cross-reference table that is right.</summary>
    /// <param name="imageDictionary">The dictionary of the image stream.</param>
    /// <param name="imageData">The encoded data of the image.</param>
    /// <returns>The document.</returns>
    private static byte[] MinimalDocument(string imageDictionary, ReadOnlySpan<byte> imageData)
    {
        using var output = new MemoryStream();
        var offsets = new long[6];

        Write(output, "%PDF-1.7\n%âãÏÓ\n");

        WriteObject(output, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(output, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject(output, offsets, 3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>");

        const string Content = "q 100 0 0 100 50 50 cm /Im0 Do Q";
        WriteObject(output, offsets, 4, $"<< /Length {Content.Length} >>\nstream\n{Content}\nendstream");

        offsets[5] = output.Position;
        Write(output, $"5 0 obj\n{imageDictionary}\nstream\n");
        output.Write(imageData);
        Write(output, "\nendstream\nendobj\n");

        var xref = output.Position;
        var table = new StringBuilder("xref\n0 6\n0000000000 65535 f \n");

        for (var number = 1; number < offsets.Length; number++)
        {
            table.Append(CultureInfo.InvariantCulture, $"{offsets[number]:D10} 00000 n \n");
        }

        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Write(output, table.ToString());

        return output.ToArray();
    }

    private static void WriteObject(MemoryStream output, long[] offsets, int number, string body)
    {
        offsets[number] = output.Position;
        Write(output, string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
    }

    private static void Write(MemoryStream output, string text) => output.Write(Encoding.Latin1.GetBytes(text));
}
