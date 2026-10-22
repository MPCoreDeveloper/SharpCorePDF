using System.Buffers;
using System.Text;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;
using PdfAffine = SharpCorePDF.Domain.ValueObjects.PdfMatrix;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// Writes the PDF operators of one content stream. It has no page and no document: a path can be
/// serialised on its own, which is what the authoring tests and the page writer both rely on.
/// </summary>
/// <remarks>
/// Numbers use <see cref="PdfNumberFormat"/> so the output is invariant and stable. Graphics state is
/// tracked per <c>q</c>/<c>Q</c> and every restore is checked, so a page cannot silently inherit a
/// transform or a clipping path from a block that forgot to close it.
/// </remarks>
internal sealed class ContentStreamBuilder
{
    private readonly ArrayBufferWriter<byte> content = new();
    private readonly GraphicsState graphics;

    /// <summary>Initializes a new instance of the <see cref="ContentStreamBuilder"/> class.</summary>
    /// <param name="resources">The resource registry to use, or null to create one.</param>
    public ContentStreamBuilder(ContentResourceRegistry? resources = null)
    {
        Resources = resources ?? new ContentResourceRegistry();
        graphics = new GraphicsState(this);
    }

    /// <summary>Gets the resource registry the stream selects from.</summary>
    public ContentResourceRegistry Resources { get; }

    /// <summary>Gets the graphics-state stack.</summary>
    public GraphicsState GraphicsState => graphics;

    /// <summary>Gets the number of bytes written so far.</summary>
    public int Length => content.WrittenCount;

    /// <summary>Gets the graphics state that is currently in effect.</summary>
    public PdfGraphicsState CurrentState => graphics.Current;

    /// <summary>Starts a path.</summary>
    /// <returns>A writer that appends to this stream.</returns>
    public PathWriter Path() => new(this);

    /// <summary>Appends a path without painting it.</summary>
    /// <param name="path">The path.</param>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder Append(PdfPath path) => Path().Append(path).Builder;

    /// <summary>Paints a path with a fill, a stroke, or both.</summary>
    /// <param name="path">The path.</param>
    /// <param name="fill">The fill, or null for no fill.</param>
    /// <param name="stroke">The stroke, or null for no stroke.</param>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder DrawPath(PdfPath path, PdfPaint? fill, PdfStroke? stroke) =>
        Path().Append(path).Paint(fill, stroke).Builder;

    /// <summary>Uses a path as the clipping region.</summary>
    /// <param name="path">The path.</param>
    /// <param name="fillRule">The rule that decides what is inside the path.</param>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder SetClip(PdfPath path, PdfFillRule fillRule = PdfFillRule.NonZero)
    {
        graphics.SetClip(path, fillRule);
        return this;
    }

    /// <summary>Saves the graphics state (<c>q</c>).</summary>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder SaveState()
    {
        graphics.Save();
        return this;
    }

    /// <summary>Restores the graphics state (<c>Q</c>).</summary>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">There is no saved state to restore.</exception>
    public ContentStreamBuilder RestoreState()
    {
        graphics.Restore();
        return this;
    }

    /// <summary>Concatenates a transform onto the current transform (<c>cm</c>).</summary>
    /// <param name="matrix">The transform.</param>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder Transform(PdfAffine matrix)
    {
        graphics.Transform(matrix);
        return this;
    }

    /// <summary>Sets the fill paint (<c>g</c>, <c>rg</c> or <c>k</c>).</summary>
    /// <param name="paint">The paint.</param>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder SetFill(PdfPaint paint)
    {
        graphics.SetFill(paint);
        return this;
    }

    /// <summary>Sets the stroke style and its paint.</summary>
    /// <param name="stroke">The stroke.</param>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder SetStroke(PdfStroke stroke)
    {
        graphics.SetStroke(stroke);
        return this;
    }

    /// <summary>Selects a device colour space (<c>cs</c> or <c>CS</c>).</summary>
    /// <param name="colorSpace">The colour space.</param>
    /// <param name="fill">Whether the non-stroking or the stroking colour space is selected.</param>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder SetColorSpace(PdfColorSpace colorSpace, bool fill = true)
    {
        graphics.SetColorSpace(colorSpace, fill);
        return this;
    }

    /// <summary>Sets colour components in the selected colour space (<c>sc</c> or <c>SC</c>).</summary>
    /// <param name="components">One, three or four finite components.</param>
    /// <param name="fill">Whether the non-stroking or the stroking colour is set.</param>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder SetColor(IReadOnlyList<double> components, bool fill = true)
    {
        graphics.SetColor(components, fill);
        return this;
    }

    /// <summary>Sets a colour and a pattern or spot name (<c>scn</c> or <c>SCN</c>).</summary>
    /// <param name="name">The resource name of the pattern or separation.</param>
    /// <param name="components">The colour components, or an empty list for an uncoloured pattern.</param>
    /// <param name="fill">Whether the non-stroking or the stroking colour is set.</param>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder SetColor(string name, IReadOnlyList<double> components, bool fill = true)
    {
        graphics.SetColor(name, components, fill);
        return this;
    }

    /// <summary>Sets the line width (<c>w</c>).</summary>
    /// <param name="width">The width in points.</param>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder SetLineWidth(double width)
    {
        graphics.SetLineWidth(width);
        return this;
    }

    /// <summary>Sets the dash pattern (<c>d</c>).</summary>
    /// <param name="dashArray">The pattern; null or empty means a solid line.</param>
    /// <param name="phase">The distance into the pattern at which the line starts.</param>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder SetDash(IReadOnlyList<double>? dashArray, double phase = 0.0)
    {
        graphics.SetDash(dashArray, phase);
        return this;
    }

    /// <summary>Sets both alpha values and the blend mode (<c>gs</c>).</summary>
    /// <param name="alpha">The alpha, from 0 to 1.</param>
    /// <param name="blendMode">The blend mode.</param>
    /// <returns>This builder.</returns>
    public ContentStreamBuilder SetAlpha(double alpha, PdfBlendMode blendMode = PdfBlendMode.Normal)
    {
        graphics.SetAlpha(alpha, blendMode);
        return this;
    }

    /// <summary>Finishes the stream.</summary>
    /// <returns>The bytes of the content stream.</returns>
    /// <exception cref="InvalidOperationException">A saved graphics state was not restored.</exception>
    public byte[] Build()
    {
        graphics.EnsureBalanced();
        return content.WrittenSpan.ToArray();
    }

    /// <summary>Writes an operator with no operands.</summary>
    /// <param name="operatorName">The operator.</param>
    internal void WriteOperator(string operatorName)
    {
        Write(operatorName);
        WriteByte((byte)'\n');
    }

    /// <summary>Writes an operator and one number.</summary>
    /// <param name="operatorName">The operator.</param>
    /// <param name="value">The operand.</param>
    internal void WriteNumberOperator(string operatorName, double value)
    {
        WriteNumber(value);
        WriteByte((byte)' ');
        WriteOperator(operatorName);
    }

    /// <summary>Writes an operator and two numbers.</summary>
    /// <param name="operatorName">The operator.</param>
    /// <param name="first">The first operand.</param>
    /// <param name="second">The second operand.</param>
    internal void WriteNumberOperator(string operatorName, double first, double second)
    {
        WriteNumber(first);
        WriteByte((byte)' ');
        WriteNumber(second);
        WriteByte((byte)' ');
        WriteOperator(operatorName);
    }

    /// <summary>Writes a point followed by an operator.</summary>
    /// <param name="operatorName">The operator.</param>
    /// <param name="point">The point.</param>
    internal void WritePointOperator(string operatorName, PdfPoint point)
    {
        WriteNumber(point.X);
        WriteByte((byte)' ');
        WriteNumber(point.Y);
        WriteByte((byte)' ');
        WriteOperator(operatorName);
    }

    /// <summary>Writes six numbers followed by an operator.</summary>
    /// <param name="operatorName">The operator.</param>
    /// <param name="matrix">The numbers.</param>
    internal void WriteMatrixOperator(string operatorName, PdfAffine matrix)
    {
        WriteNumber(matrix.A);
        WriteByte((byte)' ');
        WriteNumber(matrix.B);
        WriteByte((byte)' ');
        WriteNumber(matrix.C);
        WriteByte((byte)' ');
        WriteNumber(matrix.D);
        WriteByte((byte)' ');
        WriteNumber(matrix.E);
        WriteByte((byte)' ');
        WriteNumber(matrix.F);
        WriteByte((byte)' ');
        WriteOperator(operatorName);
    }

    /// <summary>Writes a name preceded by a slash and followed by an operator.</summary>
    /// <param name="operatorName">The operator.</param>
    /// <param name="name">The resource name.</param>
    internal void WriteNameOperator(string operatorName, string name)
    {
        WriteByte((byte)'/');
        Write(name);
        WriteByte((byte)' ');
        WriteOperator(operatorName);
    }

    /// <summary>Writes a number into the stream.</summary>
    /// <param name="value">The number.</param>
    internal void WriteNumber(double value)
    {
        Span<byte> scratch = stackalloc byte[32];

        if (!PdfNumberFormat.TryFormat(value, scratch, out var written))
        {
            throw new InvalidOperationException($"The number {value} could not be formatted for a content stream.");
        }

        Write(scratch[..written]);
    }

    /// <summary>Writes an ASCII keyword.</summary>
    /// <param name="text">The keyword.</param>
    internal void Write(string text)
    {
        var length = Encoding.ASCII.GetByteCount(text);
        var destination = content.GetSpan(length);
        Encoding.ASCII.GetBytes(text, destination);
        content.Advance(length);
    }

    private void WriteByte(byte value)
    {
        content.GetSpan(1)[0] = value;
        content.Advance(1);
    }

    private void Write(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(content.GetSpan(bytes.Length));
        content.Advance(bytes.Length);
    }
}
