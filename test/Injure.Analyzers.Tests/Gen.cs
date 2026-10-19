// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Injure.Analyzers.Tests;

internal static class Gen {
	public const string ProjectDir = "/proj/";

	public sealed record Result(
		ImmutableArray<Diagnostic> GeneratorDiagnostics,
		string GeneratedSource,
		Assembly Assembly
	) {
		public Diagnostic[] WithId(string id) => GeneratorDiagnostics.Where(d => d.Id == id).ToArray();
	}

	private sealed class PathOnlyText(string path) : AdditionalText {
		public override string Path { get; } = path;
		public override SourceText? GetText(CancellationToken ct = default) => null;
	}

	private sealed class Options(ImmutableDictionary<string, string> globals) : AnalyzerConfigOptions {
		public override bool TryGetValue(string key, out string value) => globals.TryGetValue(key, out value!);
	}

	private sealed class OptionsProvider(ImmutableDictionary<string, string> globals) : AnalyzerConfigOptionsProvider {
		private static readonly Options empty = new(ImmutableDictionary<string, string>.Empty);
		public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(globals);
		public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => empty;
		public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => empty;
	}

	private static readonly MetadataReference[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
		.Split(Path.PathSeparator)
		.Append(typeof(Injure.Assets.AssetStore).Assembly.Location)
		.Distinct()
		.Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
		.ToArray();

	private static CSharpCompilation compile(string name, string source) => CSharpCompilation.Create(
		name + Guid.NewGuid().ToString("N"),
		[CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
		references,
		new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
	);

	private static void assertNoErrors(IEnumerable<Diagnostic> diagnostics, string what) {
		Diagnostic[] errors = diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
		Assert.True(errors.Length == 0, what + ":\n" + string.Join("\n", errors.Select(static e => e.ToString())));
	}

	// the format arguments of a diagnostic's message, recovered by matching against the diagnostic
	// descriptor's format string
	public static string[] MessageArgs(Diagnostic d) {
		string format = d.Descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture);
		StringBuilder pattern = new("^");
		HashSet<string> seen = new();
		int last = 0;
		int count = 0;
		foreach (Match m in Regex.Matches(format, @"\{(\d+)\}")) {
			pattern.Append(Regex.Escape(format[last..m.Index]));
			string n = m.Groups[1].Value;
			pattern.Append(seen.Add(n) ? $"(?<a{n}>.*?)" : $@"\k<a{n}>");
			count = Math.Max(count, int.Parse(n, CultureInfo.InvariantCulture) + 1);
			last = m.Index + m.Length;
		}
		pattern.Append(Regex.Escape(format[last..])).Append('$');
		Match match = Regex.Match(d.GetMessage(CultureInfo.InvariantCulture), pattern.ToString(), RegexOptions.Singleline);
		Assert.True(match.Success, $"message of {d.Id} doesn't match its format");
		return Enumerable.Range(0, count).Select(i => match.Groups["a" + i].Value).ToArray();
	}

	// diagnostics of one analyzer over a compilation of `source`; compile errors fail the test
	public static ImmutableArray<Diagnostic> Analyze(DiagnosticAnalyzer analyzer, string source) {
		CSharpCompilation compilation = compile("AnalyzerTest", source);
		assertNoErrors(compilation.GetDiagnostics(), "test source doesn't compile");
		return compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
	}

	// runs the asset catalog generator over `source` and loads the result; compile errors, either in
	// `source` itself or after adding the generated code, fail the test
	/// <param name="files">Additional file paths, relative to the project directory.</param>
	public static Result Run(string source, IEnumerable<string> files, bool withProjectDir = true) {
		CSharpCompilation compilation = compile("GeneratorTest", source);
		assertNoErrors(compilation.GetDiagnostics(), "test source doesn't compile");
		ImmutableDictionary<string, string> globals = withProjectDir
			? ImmutableDictionary<string, string>.Empty.Add("build_property.projectdir", ProjectDir)
			: ImmutableDictionary<string, string>.Empty;
		GeneratorDriver driver = CSharpGeneratorDriver.Create(
			[new Analyzers.Assets.AssetCatalogGenerator().AsSourceGenerator()],
			files.Select(static f => (AdditionalText)new PathOnlyText(ProjectDir + f)),
			new CSharpParseOptions(LanguageVersion.Latest),
			new OptionsProvider(globals)
		);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
		GeneratorRunResult run = driver.GetRunResult().Results.Single();
		string generated = string.Join("\n", run.GeneratedSources.Select(static s => s.SourceText.ToString()));

		using MemoryStream ms = new();
		EmitResult emit = output.Emit(ms);
		assertNoErrors(emit.Diagnostics, "generated code doesn't compile");
		ms.Position = 0;
		return new Result(run.Diagnostics, generated, AssemblyLoadContext.Default.LoadFromStream(ms));
	}
}
