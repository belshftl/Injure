// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Injure.Tests;

public static class LibmiscNativeLoader {
	private static int inited = 0;
	private static nint ijmisc;

	public static void Init() {
		if (Interlocked.Exchange(ref inited, 1) != 0)
			return;
		string path = Path.Combine(Paths.RepoRoot, "native", "misc", "out", getRID(), getLibName());
		if (!File.Exists(path))
			path = Path.Combine(Paths.RepoRoot, "native", "pack", getRID(), getLibName());
		if (!File.Exists(path))
			throw new FileNotFoundException($"'{path}' not found");
		ijmisc = NativeLibrary.Load(path);
		NativeLibrary.SetDllImportResolver(typeof(Injure.Draw.Text.Unibreak).Assembly, dllImportResolver);
	}

	private static string getRID() {
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

	private static string getLibName() {
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return "ijmisc.dll";
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return "libijmisc.dylib";
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			return "libijmisc.so";
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
		return 0;
	}
}

public sealed class LibmiscNativeFixture {
	public LibmiscNativeFixture() {
		LibmiscNativeLoader.Init();
	}
}

[CollectionDefinition("needs_libmisc")]
public sealed class NeedsLibmiscCollection : ICollectionFixture<LibmiscNativeFixture>;
