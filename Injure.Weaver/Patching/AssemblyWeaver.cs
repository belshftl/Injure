// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

using Injure.Weaver.Model;

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

		DotnetAssemblyResolver resolver = new(options.InputPath);
		resolver.AddSearchDirectory(inDir);
		resolver.AddSearchDirectory(outDir);

		ReaderParameters readerParameters = new() {
			AssemblyResolver = resolver,
			ReadingMode = ReadingMode.Deferred,
			ReadSymbols = false,
		};

		using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(options.InputPath, readerParameters);
		ModuleDefinition module = assembly.MainModule;
		InjureReferences ij = InjureReferenceResolver.Resolve(module);

		PrePublicizeAnnotations.AnnotateBeforePublicize(module, ij);
		Publicizer.Publicize(module);

		string assemblyName = TypeNameUtility.SanitizeIdentifier(assembly.Name.Name);
		string hooksRoot = options.HooksRoot ?? assemblyName + ".Hooks";
		string rawHooksRoot = options.RawHooksRoot ?? assemblyName + ".RawHooks";

		List<HookCandidate> candidates = HookDiscoverer.Discover(module, options.OwnerID);
		Dictionary<HookCandidate, TypeDefinition> delegateTypes = HookEmitter.Emit(module, candidates, hooksRoot, rawHooksRoot);

		StoreEmitter.Emit(module, ij, candidates, delegateTypes, assemblyName);

		WriterParameters writerParameters = new() {
			WriteSymbols = false,
		};
		if (Path.GetFullPath(options.InputPath) == Path.GetFullPath(options.OutputPath)) {
			string tmp = options.OutputPath + ".injure-modkit-tmp";
			assembly.Write(tmp, writerParameters);
			File.Copy(tmp, options.OutputPath, overwrite: true);
			File.Delete(tmp);
		} else {
			assembly.Write(options.OutputPath, writerParameters);
		}

		return candidates.Count;
	}
}
