// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Serialized;

namespace Injure.Weaver;

public static class Program {
	public static int Main(string[] args) {
		string argv0 = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);

		if (args.Length == 0) {
			Console.Error.WriteLine($"""
				usage: {argv0} [options] assembly
				try '--help' for more info
				""");
			return args.Length == 0 ? 2 : 0;
		}

		Options options;
		try {
			options = Options.Parse(args);
		} catch (OptionsParseException e) {
			Console.Error.WriteLine($"{argv0}: {e.Message}");
			return 2;
		} catch (OptionsHelpException) {
			Console.Error.WriteLine($"""
				usage: {argv0} [options] assembly
				emit a mirror tree of modif targets into a game assembly, and generate a reference-only publicized version

				options:
				  -o, --output <PATH>               shipped output (default: overwrite input)
				  -r, --ref-output <PATH>           reference output (default: infer by appending `-ref`)
				  -c, --clean                       remove the mirror tree instead of emitting one
				      --mirror-root-segment <NAME>  namespace segment for the mirror tree (default: Modif)
				      --publicize-markers <MODE>    none|types|types+members (default: none)
				      --overload-naming <MODE>      always-qualified|short-when-unique (default: always-qualified)
				      --unsafe-unseal               remove sealed from classes (risky/unsafe)
				      --include-lambdas             emit targets for lambda bodies (unstable names)
				      --no-verify                   skip the post-write token check
				  -I, --include <PREFIX>            restrict target emission by type full name prefix
				  -E, --exclude <PREFIX>            exclude target emission by type full name prefix
				  -v, --verbose                     log more information
				  -h, --help                        display this help and exit
				  -V, --version                     output version information and exit
				""");
			return 0;
		} catch (OptionsVersionException) {
			Console.Error.WriteLine("Injure.Weaver v0.1.0-alpha");
			return 0;
		}

		try {
			if (options.Clean)
				runClean(options);
			else
				run(options);
			return 0;
		} catch (PatchException e) {
			Console.Error.WriteLine($"{argv0}: {e.Message}");
			return 1;
		}
	}

	private static void run(Options options) {
		ModuleReaderParameters read = readerParameters(options.InputPath);
		var module = ModuleDefinition.FromFile(options.InputPath, read);

		if (hasAssemblyAttribute(module, "ModifInjectedAttribute"))
			throw new PatchException("input already has the mirror tree; use the original assembly, or run --clean first if it's not available");
		if (hasAssemblyAttribute(module, "ModifPublicizedAttribute"))
			throw new PatchException("input is the reference assembly; patch the shipped one instead");

		Context context = new(module, options);

		checkNamespaceCollision(module, options);

		TargetCollector collector = new(options);
		List<MirrorScope> scopes = collector.Collect(module);

		NameMangler.Assign(scopes, options);

		Emitter emitter = new(context);
		int count = emitter.Emit(scopes);

		write(module, options.OutputPath, preserveRids: true);
		if (options.NoVerify)
			Verifier.Verify(options.OutputPath, emitter.Emitted, read);

		// reread just in case to make sure it's derived from the exact same bytes as the shipped one
		var refModule = ModuleDefinition.FromFile(
			options.OutputPath,
			readerParameters(options.OutputPath)
		);
		Context refContext = new(refModule, options);

		Publicizer publicizer = new(refContext);
		publicizer.Run(refModule);
		Refify.Finish(refModule, refContext);

		// no need for rid preservation
		write(refModule, options.RefOutputPath!, preserveRids: false);

		if (options.Verbose)
			report(options, collector, scopes, publicizer, count);
	}

	private static void runClean(Options options) {
		ModuleReaderParameters read = readerParameters(options.InputPath);
		var module = ModuleDefinition.FromFile(options.InputPath, read);

		int removed = Cleaner.Clean(module);
		write(module, options.OutputPath, preserveRids: false);

		if (options.Verbose)
			Console.Error.WriteLine($"removed {removed} mirror roots");
	}

	private static ModuleReaderParameters readerParameters(string path) =>
		new(new AsmResolver.DiagnosticBag()) {
			PEReaderParameters = new AsmResolver.PE.PEReaderParameters(),
			WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path)),
		};

	private static void write(ModuleDefinition module, string path, bool preserveRids) {
		ManagedPEImageBuilder builder = preserveRids
			? new ManagedPEImageBuilder {
				DotNetDirectoryFactory = new DotNetDirectoryFactory {
					MetadataBuilderFlags = MetadataBuilderFlags.PreserveAll,
				},
			}
			: new ManagedPEImageBuilder();
		module.Write(path, builder);
	}

	private static void checkNamespaceCollision(ModuleDefinition module, Options options) {
		HashSet<string> existing = new(StringComparer.Ordinal);
		foreach (TypeDefinition type in module.TopLevelTypes)
			existing.Add(type.Namespace?.Value ?? "");

		foreach (string ns in existing) {
			string mirror = ns.Length == 0 ? options.MirrorRootSegment : $"{ns}.{options.MirrorRootSegment}";
			foreach (string candidate in existing)
				if (candidate == mirror || candidate.StartsWith(mirror + ".", StringComparison.Ordinal))
					throw new PatchException($"namespace '{mirror}' already exists in the input; pick a different --mirror-root-segment");
		}
	}

	private static void report(
		Options options,
		TargetCollector collector,
		List<MirrorScope> scopes,
		Publicizer publicizer,
		int count
	) {
		Console.Error.WriteLine($"emitted {count} targets; publicized {publicizer.TypesChanged} types, {publicizer.MembersChanged} members");

		foreach (string skip in collector.Skipped)
			Console.Error.WriteLine($"skipped {skip}");

		if (options.OverloadNaming != OverloadNamingMode.ShortWhenUnique)
			return;

		List<string> t1 = new();
		collectTier1(scopes, t1);
		foreach (string name in t1)
			Console.Error.WriteLine($"mangled to tier 1 name (renames if an overload is added): {name}");
	}

	private static void collectTier1(List<MirrorScope> scopes, List<string> into) {
		foreach (MirrorScope scope in scopes) {
			foreach (NameGroup g in scope.Groups)
				if (g.Method is not null && g.Tier == 1)
					into.Add($"{scope.Source.FullName}::{g.Resolved}");
			collectTier1(scope.Children, into);
		}
	}

	private static bool hasAssemblyAttribute(ModuleDefinition module, string name) {
		if (module.Assembly is not AssemblyDefinition asm)
			return false;
		foreach (CustomAttribute a in asm.CustomAttributes)
			if (a.Constructor?.DeclaringType?.Name?.Value == name)
				return true;
		return false;
	}
}
