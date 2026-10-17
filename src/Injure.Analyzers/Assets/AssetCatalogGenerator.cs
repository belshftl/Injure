// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injure.Analyzers.Assets;

internal readonly struct EquatableArray<T>(ImmutableArray<T> items)
	: IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T> {
	private readonly ImmutableArray<T> items = items;
	public ImmutableArray<T> Items => items.IsDefault ? ImmutableArray<T>.Empty : items;
	public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);
	public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);
	public override int GetHashCode() {
		// System.HashCode isn't in netstandard2.0
		int h = 17;
		foreach (T item in Items)
			h = h * 31 + (item?.GetHashCode() ?? 0);
		return h;
	}
	public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed record CatalogTarget(
	string? Namespace,
	string ClassName,
	string Accessibility,
	string Directory,
	string AssetNamespace,
	bool Valid,
	Location Location
);

internal readonly record struct FileKind(string Suffix, string TypeName);

internal readonly record struct FileKindProblem(string Suffix, string Reason, Location Location);

internal sealed record FileKinds(EquatableArray<FileKind> Kinds, EquatableArray<FileKindProblem> Problems);

[Generator(LanguageNames.CSharp)]
public sealed class AssetCatalogGenerator : IIncrementalGenerator {
	private static readonly (string Suffix, string MetadataName)[] builtinKinds = [
		(".png", KnownNames.Texture2d),
		(".jpg", KnownNames.Texture2d),
		(".jpeg", KnownNames.Texture2d),
		(".bmp", KnownNames.Texture2d),
		(".tga", KnownNames.Texture2d),
		(".tex.json", KnownNames.Texture2d),
		(".ttf", KnownNames.Font),
		(".otf", KnownNames.Font),
		(".ttc", KnownNames.Font),
	];

	public void Initialize(IncrementalGeneratorInitializationContext context) {
		IncrementalValuesProvider<CatalogTarget> targets = context.SyntaxProvider.ForAttributeWithMetadataName(
			KnownNames.AssetCatalogAttribute,
			static (node, _) => node is ClassDeclarationSyntax,
			static (ctx, _) => toTarget(ctx)
		).Where(static t => t is not null)!;

		IncrementalValueProvider<FileKinds> kinds = context.CompilationProvider.Select(static (c, _) => readKinds(c));
		IncrementalValueProvider<EquatableArray<string>> paths = context.AdditionalTextsProvider
			.Select(static (t, _) => t.Path)
			.Collect()
			.Select(static (p, _) => new EquatableArray<string>(p));
		IncrementalValueProvider<string?> projectDir = context.AnalyzerConfigOptionsProvider.Select(
			static (o, _) => o.GlobalOptions.TryGetValue("build_property.projectdir", out string? dir) && !string.IsNullOrEmpty(dir) ? dir : null
		);

		context.RegisterSourceOutput(kinds, static (spc, k) => {
			foreach (FileKindProblem p in k.Problems)
				spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.Assets.FileKindInvalid, p.Location, p.Suffix, p.Reason));
		});
		context.RegisterSourceOutput(
			targets.Combine(kinds).Combine(paths).Combine(projectDir),
			static (spc, input) => generate(
				spc,
				input.Left.Left.Left, // CatalogTarget target
				input.Left.Left.Right, // FileKinds kinds
				input.Left.Right, // EquatableArray<string> paths
				input.Right // string? projectDir
			)
		);
	}

	private static CatalogTarget? toTarget(GeneratorAttributeSyntaxContext ctx) {
		if (ctx.TargetSymbol is not INamedTypeSymbol type)
			return null;
		AttributeData attr = ctx.Attributes[0];
		if (attr.ConstructorArguments.Length != 2 || attr.ConstructorArguments[0].Value is not string dir || attr.ConstructorArguments[1].Value is not string ns)
			return null;
		var decl = (ClassDeclarationSyntax)ctx.TargetNode;
		bool valid = type.ContainingType is null
			&& !type.IsGenericType
			&& !type.IsStatic
			&& decl.Modifiers.Any(SyntaxKind.PartialKeyword)
			&& type.BaseType?.SpecialType == SpecialType.System_Object;
		return new CatalogTarget(
			type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
			type.Name,
			SyntaxFacts.GetText(type.DeclaredAccessibility),
			dir,
			ns,
			valid,
			attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? decl.Identifier.GetLocation()
		);
	}

	private static FileKinds readKinds(Compilation compilation) {
		Dictionary<string, string> bySuffix = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string suffix, string metadataName) in builtinKinds)
			if (compilation.GetTypeByMetadataName(metadataName) is INamedTypeSymbol t)
				bySuffix[suffix] = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

		ImmutableArray<FileKindProblem>.Builder problems = ImmutableArray.CreateBuilder<FileKindProblem>();
		HashSet<string> declared = new(StringComparer.OrdinalIgnoreCase);
		foreach (AttributeData attr in compilation.Assembly.GetAttributes()) {
			if (attr.AttributeClass?.ToDisplayString() != KnownNames.AssetFileKindAttribute || attr.ConstructorArguments.Length != 2)
				continue;
			if (attr.ConstructorArguments[0].Value is not string suffix || attr.ConstructorArguments[1].Value is not ITypeSymbol type)
				continue;
			Location loc = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;
			if (suffix.Length < 2 || suffix[0] != '.' || suffix.IndexOfAny(['/', '\\']) >= 0) {
				problems.Add(new FileKindProblem(suffix, "it must start with '.', have at least one more character, and contain no path separators", loc));
				continue;
			}
			if (!declared.Add(suffix)) {
				problems.Add(new FileKindProblem(suffix, "another [AssetFileKind] already declares it", loc));
				continue;
			}
			bySuffix[suffix] = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		}
		return new FileKinds(
			new EquatableArray<FileKind>(
				bySuffix.Select(static kvp => new FileKind(kvp.Key, kvp.Value))
					.OrderBy(static k => k.Suffix, StringComparer.Ordinal)
					.ToImmutableArray()
			),
			new EquatableArray<FileKindProblem>(problems.ToImmutable())
		);
	}

	// ==========================================================================
	// tree
	private sealed class Dir(string name, string relPath) {
		public readonly string Name = name;
		public readonly string RelPath = relPath;
		public readonly SortedDictionary<string, Dir> Dirs = new(StringComparer.Ordinal);
		public readonly List<(string FileName, string RelPath, FileKind Kind)> Files = new();
	}

	private static string normalize(string path) => path.Replace('\\', '/');

	private static void generate(SourceProductionContext spc, CatalogTarget target, FileKinds kinds, EquatableArray<string> paths, string? projectDir) {
		if (!target.Valid) {
			spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.Assets.CatalogInvalidTarget, target.Location, target.ClassName));
			return;
		}
		if (projectDir is null) {
			spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.Assets.CatalogNoProjectDir, target.Location));
			return;
		}

		string root = normalize(Path.GetFullPath(Path.Combine(projectDir, target.Directory))).TrimEnd('/') + "/";
		Dir top = new(target.ClassName, "");
		List<string> unknown = new();
		bool any = false;
		foreach (string full in paths.OrderBy(static p => p, StringComparer.Ordinal)) {
			string p = normalize(full);
			if (!p.StartsWith(root, StringComparison.Ordinal))
				continue;
			string rel = p[root.Length..];
			string[] segs = rel.Split('/');
			if (segs.Any(static s => s.Length == 0 || s[0] == '.'))
				continue;
			any = true;
			string fileName = segs[^1];
			FileKind? kind = kinds.Kinds
				.Where(k => fileName.Length > k.Suffix.Length && fileName.EndsWith(k.Suffix, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(static k => k.Suffix.Length)
				.Select(static k => (FileKind?)k)
				.FirstOrDefault();
			if (kind is null) {
				unknown.Add(rel);
				continue;
			}
			Dir d = top;
			string relDir = "";
			for (int i = 0; i < segs.Length - 1; i++) {
				relDir = relDir.Length == 0 ? segs[i] : relDir + "/" + segs[i];
				if (!d.Dirs.TryGetValue(segs[i], out Dir? sub))
					d.Dirs.Add(segs[i], sub = new Dir(segs[i], relDir));
				d = sub;
			}
			d.Files.Add((fileName, rel, kind.Value));
		}
		if (!any)
			spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.Assets.CatalogHasNoFiles, target.Location, target.Directory));
		if (unknown.Count != 0) {
			string list = string.Join(", ", unknown.Take(5)) + (unknown.Count > 5 ? ", ..." : "");
			spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.Assets.CatalogUnknownKinds, target.Location, unknown.Count, target.Directory, list));
		}

		StringBuilder sb = new();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		sb.AppendLine();
		if (target.Namespace is not null)
			sb.Append("namespace ").Append(target.Namespace).AppendLine(";").AppendLine();
		emitGroup(sb, spc, target, top, isRoot: true, indent: "");
		spc.AddSource(target.ClassName + ".AssetCatalog.g.cs", sb.ToString());
	}

	// ==========================================================================
	// names

	// PascalCase out of the letters and digits, treating everything else as a word break
	private static string? toIdentifier(string name) {
		StringBuilder sb = new();
		bool upper = true;
		foreach (char c in name) {
			if (char.IsLetterOrDigit(c)) {
				sb.Append(upper ? char.ToUpperInvariant(c) : c);
				upper = false;
			} else {
				upper = true;
			}
		}
		if (sb.Length == 0)
			return null;
		if (char.IsDigit(sb[0]))
			sb.Insert(0, '_');
		return sb.ToString();
	}

	private sealed record Member(string Name, string Display, bool IsDir, string? GroupClass, Dir? Dir, string? RelPath, FileKind Kind);

	private static readonly HashSet<string> reservedNames = new(StringComparer.Ordinal) {
		"Equals", "GetHashCode", "GetType", "ToString", "MemberwiseClone", "Finalize",
	};

	private static List<Member> members(SourceProductionContext spc, CatalogTarget target, Dir dir, string className) {
		List<Member> all = new();
		foreach (Dir sub in dir.Dirs.Values) {
			string? id = toIdentifier(sub.Name);
			if (id is null) {
				spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.Assets.CatalogUnmappableName, target.Location, target.Directory + "/" + sub.RelPath));
				continue;
			}
			all.Add(new Member(id, sub.Name + "/", true, id + "Group", sub, null, default));
		}
		foreach ((string fileName, string relPath, FileKind kind) in dir.Files) {
			string stem = fileName[..^kind.Suffix.Length];
			string? id = toIdentifier(stem);
			if (id is null) {
				spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.Assets.CatalogUnmappableName, target.Location, target.Directory + "/" + relPath));
				continue;
			}
			all.Add(new Member(id, fileName, false, null, null, relPath, kind));
		}

		// a member's property name and a dir's group class name share one scope, together with
		// the members inherited from AssetGroup and object, and the enclosing class's own name
		Dictionary<string, List<Member>> byName = new(StringComparer.Ordinal);
		void claim(string name, Member m) {
			if (!byName.TryGetValue(name, out List<Member>? list))
				byName.Add(name, list = new List<Member>());
			if (!list.Contains(m))
				list.Add(m);
		}
		foreach (Member m in all) {
			claim(m.Name, m);
			if (m.GroupClass is not null)
				claim(m.GroupClass, m);
		}
		HashSet<Member> rejected = new();
		foreach (KeyValuePair<string, List<Member>> kv in byName) {
			bool reserved = reservedNames.Contains(kv.Key) || kv.Key == className;
			if (kv.Value.Count < 2 && !reserved)
				continue;
			string who = string.Join(", ", kv.Value.Select(static m => $"'{m.Display}'"));
			if (reserved)
				who += kv.Value.Count == 1 ? " and a name the generated class needs itself" : ", and a name the generated class needs itself";
			string where = dir.RelPath.Length == 0 ? target.Directory : target.Directory + "/" + dir.RelPath;
			spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.Assets.CatalogNameCollision, target.Location, where, who, kv.Key));
			foreach (Member m in kv.Value)
				rejected.Add(m);
		}
		return all.Where(m => !rejected.Contains(m)).ToList();
	}

	// ==========================================================================
	// emission
	private const string assetGroup = "global::Injure.Assets.AssetGroup";
	private const string untypedAsset = "global::Injure.Assets.IUntypedAsset";
	private const string assetStore = "global::Injure.Assets.AssetStore";

	private static void emitGroup(StringBuilder sb, SourceProductionContext spc, CatalogTarget target, Dir dir, bool isRoot, string indent) {
		string className = isRoot ? target.ClassName : toIdentifier(dir.Name)! + "Group";
		List<Member> ms = members(spc, target, dir, className);
		string where = dir.RelPath.Length == 0 ? target.Directory : target.Directory + "/" + dir.RelPath;
		string inner = indent + "\t";

		sb.Append(indent).AppendLine("/// <summary>");
		sb.Append(indent).Append("/// The assets in <c>").Append(xml(where)).AppendLine("</c>, including its subdirectories.");
		sb.Append(indent).AppendLine("/// </summary>");
		if (isRoot)
			sb.Append(indent).Append("partial class ").Append(className).Append(" : ").Append(assetGroup).AppendLine(" {");
		else
			sb.Append(indent).Append("public sealed class ").Append(className).Append(" : ").Append(assetGroup).AppendLine(" {");
		sb.Append(inner).AppendLine("/// <summary>");
		sb.Append(inner).Append("/// Gets the handles of the assets in <c>").Append(xml(where)).AppendLine("</c> from <paramref name=\"store\"/>.");
		sb.Append(inner).AppendLine("/// </summary>");
		sb.Append(inner).AppendLine("/// <param name=\"store\">The store the assets are in.</param>");
		if (isRoot) {
			sb.Append(inner).AppendLine("/// <exception cref=\"global::System.ArgumentNullException\">");
			sb.Append(inner).AppendLine("/// Thrown if <paramref name=\"store\"/> is <see langword=\"null\"/>.");
			sb.Append(inner).AppendLine("/// </exception>");
		}
		// AssetGroup keeps the list it's given, so the chained constructor hands it an empty one and
		// fills it once the properties exist
		string list = "global::System.Collections.Generic.List<" + untypedAsset + ">";
		sb.Append(inner).Append(isRoot ? "public " : "internal ").Append(className).Append('(').Append(assetStore)
			.Append(" store) : this(store, new ").Append(list).AppendLine("()) {}");
		sb.AppendLine();
		sb.Append(inner).Append("private ").Append(className).Append('(').Append(assetStore).Append(" store, ").Append(list)
			.AppendLine(" all) : base(all) {");
		if (isRoot)
			sb.Append(inner).AppendLine("\tglobal::System.ArgumentNullException.ThrowIfNull(store);");
		foreach (Member m in ms) {
			if (m.IsDir) {
				sb.Append(inner).Append('\t').Append(m.Name).Append(" = new ").Append(m.GroupClass).AppendLine("(store);");
				sb.Append(inner).Append("\tall.AddRange(").Append(m.Name).AppendLine(");");
			} else {
				sb.Append(inner).Append('\t').Append(m.Name).Append(" = store.GetAsset<").Append(m.Kind.TypeName).Append(">(new global::Injure.Assets.AssetId(")
					.Append(SymbolDisplay.FormatLiteral(target.AssetNamespace, true)).Append(", ")
					.Append(SymbolDisplay.FormatLiteral(m.RelPath!, true)).AppendLine("));");
				sb.Append(inner).Append("\tall.Add(").Append(m.Name).AppendLine(");");
			}
		}
		sb.Append(inner).AppendLine("}");

		foreach (Member m in ms) {
			if (m.IsDir) {
				sb.Append(inner).AppendLine("/// <summary>");
				sb.Append(inner).Append("/// <c>").Append(xml(target.Directory + "/" + m.Dir!.RelPath)).AppendLine("/</c>.");
				sb.Append(inner).AppendLine("/// </summary>");
				sb.Append(inner).Append("public ").Append(m.GroupClass).Append(' ').Append(m.Name).AppendLine(" { get; }");
			} else {
				sb.Append(inner).AppendLine("/// <summary>");
				sb.Append(inner).Append("/// <c>").Append(xml(target.Directory + "/" + m.RelPath)).AppendLine("</c>.");
				sb.Append(inner).AppendLine("/// </summary>");
				sb.Append(inner).Append("public global::Injure.Assets.Asset<").Append(m.Kind.TypeName).Append("> ").Append(m.Name).AppendLine(" { get; }");
			}
		}


		foreach (Member m in ms) {
			if (!m.IsDir)
				continue;
			sb.AppendLine();
			emitGroup(sb, spc, target, m.Dir!, isRoot: false, inner);
		}
		sb.Append(indent).AppendLine("}");
	}

	private static string xml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
