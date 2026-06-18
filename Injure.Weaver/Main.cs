// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using Injure.Weaver.Model;
using Injure.Weaver.Patching;

namespace Injure.Weaver;

public static class MainClass {
	public static void Main(string[] args) {
		Dictionary<string, string?> values = new(StringComparer.Ordinal);
		for (int i = 0; i < args.Length; i += 2) {
			string arg = args[i];
			if (arg is "-h" or "--help")
				usage();
			if (!arg.StartsWith("--", StringComparison.Ordinal))
				unexpected(arg);
			if (i + 1 >= args.Length)
				expectedarg(arg);
			values.Add(arg[2..], args[i + 1]);
		}
		string output = require(values, "output");
		int emitted = AssemblyWeaver.Run(
			new Options {
				InputPath = require(values, "input"),
				OutputPath = output,
				OwnerID = require(values, "owner-id"),
				HooksRoot = get(values, "hooks-root"),
				RawHooksRoot = get(values, "raw-hooks-root"),
			}
		);
		Console.WriteLine($"wrote '{output}' with {emitted} hook target(s)");
	}

	[DoesNotReturn]
	private static void usage() {
		Console.WriteLine(
			$"""
usage: {Environment.GetCommandLineArgs()[0]} --input <dll> --output <dll> --owner-id <owner id> [options]
process an Injure game assembly to make it more suitable for modding

options:
      --input <dll>                      input assembly (required)
      --output <dll>                     output assembly path (required)
      --owner-id <owner id>              owner id of the game, used for internal id generation (required)
      --hooks-root <full type name>      root static class to emit intended hooks into (default: <assembly name>.Hooks)
      --raw-hooks-root <full type name>  root static class to emit raw hooks into (default: <assembly name>.RawHooks)
  -h, --help                             display this help and exit
"""
		);
		Environment.Exit(2);
		throw new UnreachableException();
	}

	[DoesNotReturn]
	private static void unexpected(string arg) {
		Console.Error.WriteLine($"unexpected argument '{arg}'");
		Environment.Exit(2);
		throw new UnreachableException();
	}

	[DoesNotReturn]
	private static void expectedarg(string opt) {
		Console.Error.WriteLine($"expected argument for option '--{opt}'");
		Environment.Exit(2);
		throw new UnreachableException();
	}

	private static string? get(Dictionary<string, string?> values, string opt) {
		if (!values.TryGetValue(opt, out string? val) || string.IsNullOrWhiteSpace(val))
			return null;
		return val;
	}

	private static string require(Dictionary<string, string?> values, string opt) {
		if (get(values, opt) is not string s) {
			Console.Error.WriteLine($"missing required option '--{opt}'");
			Environment.Exit(2);
			throw new UnreachableException();
		}
		return s;
	}
}
