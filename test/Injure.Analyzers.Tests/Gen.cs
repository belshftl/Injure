// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection;
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
		ImmutableArray<Diagnostic> CompileErrors,
		string GeneratedSource,
		Assembly? Assembly
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

	// diagnostics of one analyzer over a compilation of `source`; compile errors fail the test
	public static ImmutableArray<Diagnostic> Analyze(DiagnosticAnalyzer analyzer, string source) {
		var compilation = CSharpCompilation.Create(
			"AnalyzerTest" + Guid.NewGuid().ToString("N"),
			[CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
		);
		Diagnostic[] errors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
		Assert.True(errors.Length == 0, string.Join("\n", errors.Select(static e => e.ToString())));
		return compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
	}

	/// <param name="files">Additional file paths, relative to the project directory.</param>
	public static Result Run(string source, IEnumerable<string> files, bool withProjectDir = true) {
		var compilation = CSharpCompilation.Create(
			"GeneratorTest" + Guid.NewGuid().ToString("N"),
			[CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
		);
		ImmutableDictionary<string, string> globals = withProjectDir
			? ImmutableDictionary<string, string>.Empty.Add("build_property.projectdir", ProjectDir)
			: ImmutableDictionary<string, string>.Empty;
		GeneratorDriver driver = CSharpGeneratorDriver.Create(
			[new Assets.AssetCatalogGenerator().AsSourceGenerator()],
			files.Select(static f => (AdditionalText)new PathOnlyText(ProjectDir + f)),
			new CSharpParseOptions(LanguageVersion.Latest),
			new OptionsProvider(globals)
		);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
		GeneratorRunResult run = driver.GetRunResult().Results.Single();
		string generated = string.Join("\n", run.GeneratedSources.Select(static s => s.SourceText.ToString()));

		using MemoryStream ms = new();
		EmitResult emit = output.Emit(ms);
		var errors = emit.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
		Assembly? asm = null;
		if (emit.Success) {
			ms.Position = 0;
			asm = AssemblyLoadContext.Default.LoadFromStream(ms);
		}
		return new Result(run.Diagnostics, errors, generated, asm);
	}
}
