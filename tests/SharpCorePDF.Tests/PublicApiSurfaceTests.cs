using System.Reflection;
using SharpCorePDF.DependencyInjection;

namespace SharpCorePDF.Tests;

/// <summary>
/// Holds the approved-API snapshots to the assemblies they describe: every public type and member a
/// layer exports is declared in its <c>PublicAPI.Shipped.txt</c> or <c>PublicAPI.Unshipped.txt</c>,
/// every declared entry names something the layer still has, and no entry is declared twice.
/// </summary>
/// <remarks>
/// <para>
/// <c>Microsoft.CodeAnalysis.PublicApiAnalyzers</c> already fails the build on an undocumented addition
/// (RS0016) and on an entry that outlives its member (RS0017), and both are errors in
/// <c>src/Directory.Build.props</c>. This test is the second, independent statement of the same
/// contract: it reads the files and reflects over the shipped assemblies itself, so it still holds if a
/// project loses the analyzer reference, and it fails in the suite rather than at the end of a build
/// somebody may have skipped.
/// </para>
/// <para>
/// The check is deliberately name-based rather than signature-based. The analyzer owns the exact
/// spelling of an entry (<c>Type.Member(string! value = null) -> bool</c>); a test that re-derived it
/// would be a second implementation of the analyzer, and the day the two disagreed the test would be
/// wrong rather than the snapshot. Names plus the one-entry-per-accessor rule catch what a release
/// review has to catch — a type or member that appears without a line, a line that describes something
/// that is gone — and leave the spelling to the tool that already enforces it.
/// </para>
/// </remarks>
public sealed class PublicApiSurfaceTests
{
    /// <summary>The four layers that carry a snapshot, and a type that proves the assembly loaded.</summary>
    private static readonly (string Layer, Assembly Assembly)[] Layers =
    [
        ("SharpCorePDF.Domain", typeof(SharpCorePDF.Domain.ValueObjects.PdfSourceId).Assembly),
        ("SharpCorePDF.Application", typeof(SharpCorePDF.Application.Ports.IPdfMergeEngine).Assembly),
        ("SharpCorePDF.Infrastructure", typeof(SharpCorePDF.Infrastructure.Pdf.PdfFileReader).Assembly),
        ("SharpCorePDF.DependencyInjection", typeof(SharpCorePDFServiceCollectionExtensions).Assembly),
    ];

    /// <summary>The two files an approved-API snapshot is made of.</summary>
    private static readonly string[] SnapshotFiles = ["PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt"];

    /// <summary>The modifiers the analyzer writes in front of the signature, longest first.</summary>
    private static readonly string[] Modifiers =
    [
        "~override ",
        "override sealed ",
        "static ",
        "abstract ",
        "virtual ",
        "override ",
        "sealed ",
        "readonly ",
        "const ",
    ];

    /// <summary>The metadata name of an operator and the symbol the snapshot spells it with.</summary>
    private static readonly Dictionary<string, string> Operators = new(StringComparer.Ordinal)
    {
        ["op_Equality"] = "operator ==",
        ["op_Inequality"] = "operator !=",
        ["op_LessThan"] = "operator <",
        ["op_LessThanOrEqual"] = "operator <=",
        ["op_GreaterThan"] = "operator >",
        ["op_GreaterThanOrEqual"] = "operator >=",
        ["op_Addition"] = "operator +",
        ["op_Subtraction"] = "operator -",
    };

    [Fact]
    public void Every_public_type_and_member_is_declared_in_its_layers_snapshot()
    {
        var missing = new List<string>();

        foreach (var (layer, assembly) in Layers)
        {
            var signatures = Signatures(layer);

            foreach (var reflected in ExportedTypes(assembly))
            {
                var path = reflected.DeclaredPath;
                var angle = path.IndexOf('<', StringComparison.Ordinal);
                var spelled = angle < 0 ? path : path[..angle];

                if (!Declares(signatures, path) && !Declares(signatures, spelled))
                {
                    missing.Add($"{layer}: the type {path} has no entry of its own.");
                }

                foreach (var member in Members(reflected.Type))
                {
                    var declared = Declares(signatures, $"{path}.{member}")
                        || Declares(signatures, $"{spelled}.{member}")
                        || DeclaresAnExtensionMember(signatures, path, member);

                    if (!declared)
                    {
                        missing.Add($"{layer}: {path}.{member} is part of the surface but not declared.");
                    }
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            $"The snapshot does not describe the compiled surface:{Environment.NewLine}{string.Join(Environment.NewLine, missing.Take(25))}");
    }

    [Fact]
    public void Every_snapshot_entry_names_a_type_or_member_the_layer_still_has()
    {
        var stale = new List<string>();

        foreach (var (layer, assembly) in Layers)
        {
            var types = ExportedTypes(assembly);

            foreach (var signature in Signatures(layer))
            {
                var (type, member) = Split(signature, types);

                if (type is null)
                {
                    stale.Add($"{layer}: no public type owns the entry `{signature}`.");
                    continue;
                }

                if (member is null || member == "extension")
                {
                    continue;
                }

                var members = Members(type.Type);
                var accessor = member.Length > 4
                    && (member.StartsWith("get_", StringComparison.Ordinal) || member.StartsWith("set_", StringComparison.Ordinal))
                    ? member[4..]
                    : null;

                var known = members.Contains(member, StringComparer.Ordinal)
                    || (accessor is not null && members.Contains(accessor, StringComparer.Ordinal))
                    || member == type.Type.Name
                    || type.Type.GetNestedType(member, BindingFlags.Public | BindingFlags.NonPublic) is not null;

                if (!known)
                {
                    stale.Add($"{layer}: {type.DeclaredPath} has no member named {member}, but `{signature}` declares one.");
                }
            }
        }

        Assert.True(
            stale.Count == 0,
            $"The snapshot declares API that is gone:{Environment.NewLine}{string.Join(Environment.NewLine, stale.Take(25))}");
    }

    [Fact]
    public void No_layer_declares_an_entry_twice()
    {
        foreach (var (layer, _) in Layers)
        {
            var duplicate = Entries(layer)
                .GroupBy(entry => entry, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => $"`{group.Key}` ({group.Count()} times)")
                .ToList();

            Assert.True(duplicate.Count == 0, $"{layer} declares {string.Join(", ", duplicate)} twice.");
        }
    }

    [Fact]
    public void Every_snapshot_opens_with_the_nullable_directive()
    {
        // The meta package is in the list on purpose: it ships no code of its own, so its two files are
        // the directive and nothing else, which is what this test keeps true.
        foreach (var layer in Layers.Select(layer => layer.Layer).Append("SharpCorePDF"))
        {
            foreach (var file in SnapshotFiles)
            {
                var lines = File.ReadAllLines(Path.Combine(Folder(layer), file));

                Assert.True(
                    lines.Length > 0 && lines[0] == "#nullable enable",
                    $"{layer}/{file} must start with #nullable enable.");
            }
        }
    }

    /// <summary>Reads the two snapshots of a layer as the entries they declare.</summary>
    /// <param name="layer">The folder name under <c>src/</c>.</param>
    /// <returns>Every non-empty, non-comment line, in file order.</returns>
    private static List<string> Entries(string layer) =>
        SnapshotFiles
            .SelectMany(file => File.ReadAllLines(Path.Combine(Folder(layer), file)))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

    /// <summary>Reads the entries of a layer with the modifiers and the return type removed.</summary>
    /// <param name="layer">The folder name under <c>src/</c>.</param>
    /// <returns>The signature of each entry, ready to be matched against a declared name.</returns>
    private static List<string> Signatures(string layer) => Entries(layer).Select(Signature).ToList();

    /// <summary>Reduces an entry to the part that names a type or member.</summary>
    /// <param name="entry">A line of a snapshot.</param>
    /// <returns>The entry without its modifiers and without its return type.</returns>
    private static string Signature(string entry)
    {
        var head = entry;
        var arrow = head.IndexOf(" -> ", StringComparison.Ordinal);

        if (arrow >= 0)
        {
            head = head[..arrow];
        }

        foreach (var modifier in Modifiers)
        {
            while (head.StartsWith(modifier, StringComparison.Ordinal))
            {
                head = head[modifier.Length..];
            }
        }

        return head;
    }

    /// <summary>Gets the folder of a layer.</summary>
    /// <param name="layer">The folder name under <c>src/</c>.</param>
    /// <returns>The absolute path of the project folder.</returns>
    private static string Folder(string layer) => Path.Combine(RepositoryRoot(), "src", layer);

    /// <summary>Walks up from the test assembly to the folder that holds <c>src/</c>.</summary>
    /// <returns>The root of the repository.</returns>
    /// <exception cref="InvalidOperationException">The tests are running outside the repository.</exception>
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "SharpCorePDF.Domain")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No src/SharpCorePDF.Domain above {AppContext.BaseDirectory}.");
    }

    /// <summary>Lists the types a layer exports, in their snapshot spelling.</summary>
    /// <param name="assembly">The layer.</param>
    /// <returns>The exported types, nested ones included, each with the path it is written as.</returns>
    /// <remarks>
    /// Types the compiler names are left out: a C# 14 extension block is emitted as a public nested type
    /// called <c>&lt;G&gt;$…</c>, and the snapshot describes its members inside the static class instead.
    /// </remarks>
    private static List<ReflectedType> ExportedTypes(Assembly assembly) =>
        assembly.GetExportedTypes()
            .Where(type => !type.Name.StartsWith('<'))
            .Select(type => new ReflectedType(DeclaredPath(type), type))
            .ToList();

    /// <summary>Writes a type the way the snapshot does: nested types dotted, arguments named.</summary>
    /// <param name="type">The type.</param>
    /// <returns>For example <c>SharpCorePDF.Domain.ReadModels.PdfReadResult&lt;T&gt;</c>.</returns>
    private static string DeclaredPath(Type type)
    {
        var name = type.FullName!.Replace('+', '.');
        var tick = name.IndexOf('`', StringComparison.Ordinal);

        if (tick < 0)
        {
            return name;
        }

        var arguments = type.IsGenericTypeDefinition
            ? type.GetGenericArguments()
            : type.GetGenericTypeDefinition().GetGenericArguments();

        return string.Concat(name[..tick], "<", string.Join(", ", arguments.Select(argument => argument.Name)), ">");
    }

    /// <summary>Lists the members of a type that a consumer outside the assembly can reach.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The names the snapshot must declare, one per entry it writes.</returns>
    /// <remarks>
    /// A property contributes its name once even when it has two accessors, because the forward check
    /// only asks whether the property is declared at all; the analyzer's own rules own the per-accessor
    /// spelling. Accessor methods are not members of their own: the snapshot writes
    /// <c>Type.Property.get</c>, which the <c>Type.Property</c> prefix already matches.
    /// </remarks>
    private static List<string> Members(Type type)
    {
        var names = new List<string>();
        var tick = type.Name.IndexOf('`', StringComparison.Ordinal);
        var simple = tick < 0 ? type.Name : type.Name[..tick];

        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (Visible(constructor))
            {
                names.Add(simple);
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (!Visible(method))
            {
                continue;
            }

            // A protected override of a protected member adds nothing: the base declaration already
            // carries it in the snapshot, which is why derived records do not repeat PrintMembers or
            // EqualityContract. A public override is listed per type, so it stays.
            if (!method.IsPublic && method.GetBaseDefinition() != method)
            {
                continue;
            }

            if (method.IsSpecialName)
            {
                // Accessors belong to the property or event that declares them, handled below.
                if (method.Name.StartsWith("get_", StringComparison.Ordinal)
                    || method.Name.StartsWith("set_", StringComparison.Ordinal)
                    || method.Name.StartsWith("add_", StringComparison.Ordinal)
                    || method.Name.StartsWith("remove_", StringComparison.Ordinal))
                {
                    continue;
                }

                // An operator the map does not know is reported as its metadata name, so the failure
                // names the spelling to add to Operators instead of passing quietly.
                names.Add(Operators.TryGetValue(method.Name, out var symbol) ? symbol : method.Name);
                continue;
            }

            // A C# 14 extension property is emitted as an ordinary getter on the static class, so it is
            // known to the snapshot by the name after the prefix and declared inside an extension block.
            if (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal))
            {
                names.Add(method.Name[4..]);
                continue;
            }

            names.Add(method.Name);
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            var reachable = (property.GetMethod is { } getter && Visible(getter))
                || (property.SetMethod is { } setter && Visible(setter));

            // The same rule as for methods: a protected override of a protected property is already
            // declared by the type that introduced it.
            var accessor = property.GetMethod ?? property.SetMethod;
            var overrides = accessor is not null && !accessor.IsPublic && accessor.GetBaseDefinition() != accessor;

            if (reachable && !overrides)
            {
                names.Add(property.GetIndexParameters().Length > 0 ? "this" : property.Name);
            }
        }

        foreach (var raised in type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if ((raised.AddMethod is { } add && Visible(add)) || (raised.RemoveMethod is { } remove && Visible(remove)))
            {
                names.Add(raised.Name);
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            // value__ is the storage an enum's underlying type gets, not a member anybody declares.
            if (!field.IsSpecialName && Visible(field))
            {
                names.Add(field.Name);
            }
        }

        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Tells whether a member is part of the surface a consumer can reach.</summary>
    /// <param name="member">The member.</param>
    /// <returns><see langword="true"/> for public, protected and protected-internal members.</returns>
    private static bool Visible(MemberInfo member) => member switch
    {
        MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
        FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
        _ => false,
    };

    /// <summary>Tells whether an entry declares a type or member, allowing for what follows the name.</summary>
    /// <param name="signatures">The snapshot, reduced to signatures.</param>
    /// <param name="prefix">The declared type or <c>Type.Member</c> to look for.</param>
    /// <returns><see langword="true"/> when an entry names it.</returns>
    /// <remarks>
    /// The character after the name decides, so <c>Type.Items</c> is not mistaken for <c>Type.Item</c>:
    /// a dot begins an accessor, a parenthesis a parameter list, a space a return type, an equals sign an
    /// enumeration value, and the end of the line a type or a field.
    /// </remarks>
    private static bool Declares(List<string> signatures, string prefix) =>
        signatures.Any(entry => entry.StartsWith(prefix, StringComparison.Ordinal)
            && (entry.Length == prefix.Length || entry[prefix.Length] is '.' or '(' or ' ' or '='));

    /// <summary>
    /// Tells whether an entry declares an extension member, which the analyzer writes as
    /// <c>Type.extension(Receiver).Member</c> rather than <c>Type.Member</c>.
    /// </summary>
    /// <param name="signatures">The snapshot, reduced to signatures.</param>
    /// <param name="type">The declared path of the static class.</param>
    /// <param name="member">The method or property name.</param>
    /// <returns><see langword="true"/> when an entry declares the member.</returns>
    private static bool DeclaresAnExtensionMember(List<string> signatures, string type, string member) =>
        signatures.Any(entry =>
        {
            if (!entry.StartsWith($"{type}.extension(", StringComparison.Ordinal))
            {
                return false;
            }

            var closed = entry.IndexOf($").{member}", StringComparison.Ordinal);

            return closed >= 0
                && closed + member.Length + 2 < entry.Length
                && entry[closed + member.Length + 2] is '(' or '.';
        });

    /// <summary>Splits an entry into the type that owns it and the member it names.</summary>
    /// <param name="signature">An entry reduced by <see cref="Signature"/>.</param>
    /// <param name="types">The exported types of the layer, in their declared spelling.</param>
    /// <returns>The owning type, or <see langword="null"/> when no exported type owns the entry.</returns>
    private static (ReflectedType? Type, string? Member) Split(string signature, List<ReflectedType> types)
    {
        var owner = types
            .Where(type => signature.StartsWith(type.DeclaredPath, StringComparison.Ordinal))
            .OrderByDescending(type => type.DeclaredPath.Length)
            .FirstOrDefault();

        if (owner is null)
        {
            return (null, null);
        }

        var rest = signature[owner.DeclaredPath.Length..].TrimStart('.');

        if (rest.Length == 0)
        {
            return (owner, null);
        }

        // An operator is spelled with a space and an extension block with a receiver list: both stop at
        // the parenthesis that opens the parameter list. A record's synthesized <Clone>$ has no name the
        // identifier rules would find, so it stops there too.
        if (rest.StartsWith("operator ", StringComparison.Ordinal)
            || rest.StartsWith("extension(", StringComparison.Ordinal)
            || rest.StartsWith('<'))
        {
            var open = rest.IndexOf('(', StringComparison.Ordinal);
            return (owner, open < 0 ? rest : rest[..open].TrimEnd());
        }

        var end = rest.IndexOfAny(['.', '(', '<', ' ', '=']);
        return (owner, end < 0 ? rest : rest[..end]);
    }

    /// <summary>An exported type and the path its snapshot entries are written under.</summary>
    /// <param name="DeclaredPath">For example <c>SharpCorePDF.Domain.Aggregates.MergePlan</c>.</param>
    /// <param name="Type">The type itself.</param>
    private sealed record ReflectedType(string DeclaredPath, Type Type);
}
