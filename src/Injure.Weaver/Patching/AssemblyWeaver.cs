// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Weaver.Model;
using Mono.Cecil;

namespace Injure.Weaver.Patching;

public static class AssemblyWeaver {
	public static int Run(in Options options) {
		if (options.OwnerID.Length == 0)
			throw new ArgumentException("owner ID must not be empty");
		if (!char.IsAsciiLetterOrDigit(options.OwnerID[0]))
			throw new ArgumentException("owner ID must start with an ASCII letter or ASCII digit");
		foreach (char c in options.OwnerID)
			if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '.'))
				throw new ArgumentException($"owner ID contains invalid UTF-16 code unit U+{(ushort)c:X4} '{c}' (valid: ASCII letters, ASCII digits, '_', '-', '.')");

		if (!File.Exists(options.InputPath))
			throw new FileNotFoundException("input assembly file does not exist", options.InputPath);

		string inDir = Path.GetDirectoryName(options.InputPath) ?? Directory.GetCurrentDirectory();
		string outDir = Path.GetDirectoryName(options.OutputPath) ?? Directory.GetCurrentDirectory();
		if (!string.IsNullOrEmpty(outDir))
			Directory.CreateDirectory(outDir);

		int ret;
		string? tmpFilePath = null;
		using (DotnetAssemblyResolver resolver = new(options.InputPath)) {
			resolver.AddSearchDirectory(inDir);
			resolver.AddSearchDirectory(outDir);

			ReaderParameters readerParameters = new() {
				AssemblyResolver = resolver,
				ReadingMode = ReadingMode.Deferred,
				ReadSymbols = false,
			};

			using var assembly = AssemblyDefinition.ReadAssembly(options.InputPath, readerParameters);
			ModuleDefinition module = assembly.MainModule;
			InjureReferences ij = InjureReferenceResolver.Resolve(module);
			AssemblyAnalysis analysis = AssemblyAnalyzer.Analyze(module);

			PublicizeAnnotations.Annotate(module, ij, in analysis);
			Publicizer.Publicize(module, in analysis);

			string assemblyName = TypeNameUtil.SanitizeIdentifier(assembly.Name.Name);
			string targetsRoot = options.TargetsRoot ?? assemblyName + ".Methods";

			List<TargetCandidate> candidates = TargetDiscoverer.Discover(module, options.OwnerID);
			Dictionary<TargetCandidate, TypeDefinition> delegateTypes = TargetEmitter.Emit(module, candidates, targetsRoot);

			StoreEmitter.Emit(module, ij, candidates, delegateTypes, assemblyName);

			WriterParameters writerParameters = new() {
				WriteSymbols = false,
			};
			if (Path.GetFullPath(options.InputPath) == Path.GetFullPath(options.OutputPath)) {
				tmpFilePath = options.OutputPath + ".injure-weaver-tmp";
				assembly.Write(tmpFilePath, writerParameters);
			} else {
				assembly.Write(options.OutputPath, writerParameters);
			}
			ret = candidates.Count;
		}
		if (tmpFilePath is not null) {
			File.Copy(tmpFilePath, options.OutputPath, overwrite: true);
			File.Delete(tmpFilePath);
		}

		return ret;
	}
}
