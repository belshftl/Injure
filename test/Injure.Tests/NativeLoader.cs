// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Injure.Tests;

// loads the native libraries Injure needs from native/, preferring native/pack/<rid> and falling back
// to each library's own build output
public static class NativeLibsLoader {
	private static int inited = 0;
	private static nint ijmisc;
	private static nint ijtext;

	public static void Init() {
		if (Interlocked.Exchange(ref inited, 1) != 0)
			return;
		ijmisc = load("ijmisc", Path.Combine("misc", "out", getRid()));
		ijtext = load("ijtext", Path.Combine("text", "target", "debug"));
		NativeLibrary.SetDllImportResolver(typeof(Injure.Draw.Text.IjText).Assembly, dllImportResolver);
	}

	private static nint load(string name, string buildDir) {
		string native = Path.Combine(Paths.RepoRoot, "native");
		string path = Path.Combine(native, "pack", getRid(), getLibName(name));
		if (!File.Exists(path))
			path = Path.Combine(native, buildDir, getLibName(name));
		if (!File.Exists(path))
			throw new FileNotFoundException($"'{path}' not found");
		return NativeLibrary.Load(path);
	}

	private static string getRid() {
		string arch = RuntimeInformation.ProcessArchitecture switch {
			Architecture.X64 => "x64",
			Architecture.Arm64 => "arm64",
			_ => throw new NotSupportedException($"arch '{RuntimeInformation.ProcessArchitecture}' not supported (supported: x64, arm64)"),
		};
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return "win-" + arch;
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return "osx-" + arch;
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			return "linux-" + arch;
		throw new NotSupportedException("OS not supported (supported: Windows, OSX, Linux)");
	}

	private static string getLibName(string name) {
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return name + ".dll";
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return "lib" + name + ".dylib";
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			return "lib" + name + ".so";
		throw new NotSupportedException("OS not supported (supported: Windows, OSX, Linux)");
	}

	private static bool matchLib(string target, string given) => Regex.IsMatch(
		given,
		@"^(?:lib)?" + Regex.Escape(target) + @"(?:\.dll|(?:-\d+(?:\.\d+)*)?\.dylib|(?:-\d+(?:\.\d+)*)?\.so(?:\.\d+)*)?$",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
	);

	private static nint dllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) {
		if (matchLib("ijmisc", libraryName))
			return ijmisc;
		if (matchLib("ijtext", libraryName))
			return ijtext;
		return 0;
	}
}

public sealed class NativeLibsFixture {
	public NativeLibsFixture() {
		NativeLibsLoader.Init();
	}
}

[CollectionDefinition("needs_native_libs")]
public sealed class NeedsNativeLibsCollection : ICollectionFixture<NativeLibsFixture>;
