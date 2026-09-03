// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Weaver;

public enum PublicizeMarkerMode {
	None,
	Types,
	TypesAndMembers,
}

public enum OverloadNamingMode {
	AlwaysQualified,
	ShortWhenUnique,
}

public sealed class OptionsParseException(string message) : Exception(message);

/// <summary>
/// Thrown if <c>--help</c> was passed.
/// </summary>
public sealed class OptionsHelpException : Exception;

/// <summary>
/// Thrown if <c>--version</c> was passed.
/// </summary>
public sealed class OptionsVersionException : Exception;

public sealed class Options {
	public required string InputPath { get; init; }
	public required string OutputPath { get; init; }

	/// <remarks>
	/// Null only in <c>--clean</c> mode.
	/// </remarks>
	public string? RefOutputPath { get; init; }

	public bool Clean { get; init; }

	public string MirrorRootSegment { get; init; } = "Modif";

	public PublicizeMarkerMode PublicizeMarkers { get; init; } = PublicizeMarkerMode.None;
	public OverloadNamingMode OverloadNaming { get; init; } = OverloadNamingMode.AlwaysQualified;

	public bool Unseal { get; init; }
	public bool IncludeLambdas { get; init; }
	public bool NoVerify { get; init; }
	public bool Verbose { get; init; }

	public List<string> Include { get; init; } = new();
	public List<string> Exclude { get; init; } = new();

	public string NamingModeName =>
		OverloadNaming == OverloadNamingMode.AlwaysQualified ? "always-qualified" : "short-when-unique";

	public bool MatchesTargetFilter(string typeFullName) {
		foreach (string e in Exclude)
			if (typeFullName.StartsWith(e, StringComparison.Ordinal))
				return false;
		if (Include.Count == 0)
			return true;
		foreach (string i in Include)
			if (typeFullName.StartsWith(i, StringComparison.Ordinal))
				return true;
		return false;
	}

	/// <exception cref="OptionsParseException" />
	/// <exception cref="OptionsHelpException" />
	/// <exception cref="OptionsVersionException" />
	public static Options Parse(string[] args) {
		static void add(List<string> list, string? v) {
			if (!string.IsNullOrEmpty(v))
				list.Add(v);
		}

		string? input = null;
		string? output = null;
		string? refOutput = null;
		string mirrorRootSeg = "Modif";
		PublicizeMarkerMode publicizeMarkers = PublicizeMarkerMode.None;
		OverloadNamingMode overloadNaming = OverloadNamingMode.AlwaysQualified;
		bool clean = false, unseal = false, includeLambdas = false, noVerify = false, verbose = false;
		List<string> include = new(), exclude = new();
		bool stop = false;

		for (int i = 0; !stop && i < args.Length; i++) {
			string a = args[i];
			string? tryNext() => i + 1 < args.Length ? args[++i] : null;
			string next(string opt) => tryNext() ?? throw new OptionsParseException($"{opt} requires an argument");

			switch (a) {
			case "-o" or "--output":
				output = next("-o/--output");
				break;
			case "-r" or "--ref-output":
				refOutput = next("-r/--ref-output");
				break;
			case "-c" or "--clean":
				clean = true;
				break;
			case "--mirror-root-segment":
				mirrorRootSeg = next("--mirror-root-segment");
				break;
			case "--publicize-markers":
				publicizeMarkers = next("--publicize-markers") switch {
					"none" => PublicizeMarkerMode.None,
					"types" => PublicizeMarkerMode.Types,
					"types+members" => PublicizeMarkerMode.TypesAndMembers,
					_ => throw new OptionsParseException("--publicize-markers value must be one of 'none', 'types', 'types+members'"),
				};
				break;
			case "--overload-naming":
				overloadNaming = next("--overload-naming") switch {
					"always-qualified" => OverloadNamingMode.AlwaysQualified,
					"short-when-unique" => OverloadNamingMode.ShortWhenUnique,
					_ => throw new OptionsParseException("--overload-naming must be one of 'always-qualified', 'short-when-unique'"),
				};
				break;
			case "--unsafe-unseal":
				unseal = true;
				break;
			case "--include-lambdas":
				includeLambdas = true;
				break;
			case "--no-verify":
				noVerify = true;
				break;
			case "-v" or "--verbose":
				verbose = true;
				break;
			case "-I" or "--include":
				add(include, next("-I/--include"));
				break;
			case "-E" or "--exclude":
				add(exclude, next("-E/--exclude"));
				break;
			case "-h" or "--help":
				throw new OptionsHelpException();
			case "-V" or "--version":
				throw new OptionsVersionException();
			case "--":
				input = tryNext();
				if (tryNext() is not null)
					throw new OptionsParseException("only one input assembly can be provided");
				stop = true;
				break;
			default:
				if (a[0] == '-')
					throw new OptionsParseException($"unknown option '{a}'");
				if (input is not null)
					throw new OptionsParseException("only one input assembly can be provided");
				input = a;
				break;
			}
		}

		if (input is null)
			throw new OptionsParseException("no input assembly provided");
		output ??= input;
		if (clean) {
			if (refOutput is not null)
				throw new OptionsParseException("-r/--ref-output is meaningless with --clean");
		} else {
			if (output.EndsWith(".dll", StringComparison.Ordinal))
				refOutput = output[..^".dll".Length] + "-ref.dll";
			if (refOutput is null)
				throw new OptionsParseException("failed to infer ref output filename from the shipped output name; specify -r/--ref-output explicitly");
		}
		return new Options {
			InputPath = input,
			OutputPath = output ?? input,
			RefOutputPath = refOutput,
			Clean = clean,
			MirrorRootSegment = mirrorRootSeg,
			PublicizeMarkers = publicizeMarkers,
			OverloadNaming = overloadNaming,
			Unseal = unseal,
			IncludeLambdas = includeLambdas,
			NoVerify = noVerify,
			Verbose = verbose,
			Include = include,
			Exclude = exclude,
		};
	}
}
