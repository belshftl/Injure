// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil;

namespace Injure.Weaver.Patching;

public sealed class DotnetAssemblyResolver : BaseAssemblyResolver {
	public DotnetAssemblyResolver(string targetAssemblyPath) {
		string path = Path.GetFullPath(targetAssemblyPath);
		string? dir = Path.GetDirectoryName(path);
		AddSearchDirectory(dir);

		foreach (string p in trustedPlatformAssemblies())
			AddSearchDirectory(Path.GetDirectoryName(p));

		foreach (string p in netCoreRefPackDirs())
			AddSearchDirectory(p);
	}

	private static string[] trustedPlatformAssemblies() =>
		((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];

	private static IEnumerable<string> netCoreRefPackDirs() {
		foreach (string root in dotnetRoots()) {
			string packs = Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref");
			if (!Directory.Exists(packs))
				continue;
			foreach (string versionDir in Directory.EnumerateDirectories(packs))
				foreach (string refDir in Directory.EnumerateDirectories(Path.Combine(versionDir, "ref")))
					yield return refDir;
		}
	}

	private static IEnumerable<string> dotnetRoots() {
		string? env = Environment.GetEnvironmentVariable("DOTNET_ROOT");
		if (!string.IsNullOrEmpty(env))
			yield return env;

		if (OperatingSystem.IsWindows()) {
			string path = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			if (!string.IsNullOrEmpty(path))
				yield return Path.Combine(path, "dotnet");
		} else {
			yield return "/usr/share/dotnet";
			yield return "/usr/local/share/dotnet";
			yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
		}
	}
}
