// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace Injure.Internals.Tests;

public static class LibmiscNativeLoader {
	private static int inited = 0;
	private static IntPtr injuremisc;

	public static void Init() {
		if (Interlocked.Exchange(ref inited, 1) != 0)
			return;
		string path = Path.Combine(Paths.RepoRoot, "Injure.Native", "misc", "out", getRID(), getLibName());
		if (!File.Exists(path))
			path = Path.Combine(Paths.RepoRoot, "Injure.Native", "pack", getRID(), getLibName());
		if (!File.Exists(path))
			throw new FileNotFoundException($"'{path}' not found");
		injuremisc = NativeLibrary.Load(path);
		NativeLibrary.SetDllImportResolver(typeof(Native.Unibreak).Assembly, dllImportResolver);
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
			return "injuremisc.dll";
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return "libinjuremisc.dylib";
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			return "libinjuremisc.so";
		throw new NotSupportedException("OS not supported (supported: Windows, OSX, Linux)");
	}

	private static bool matchLib(string target, string given) => Regex.IsMatch(
		given,
		@"^(?:lib)?" + Regex.Escape(target) + @"(?:\.dll|(?:-\d+(?:\.\d+)*)?\.dylib|(?:-\d+(?:\.\d+)*)?\.so(?:\.\d+)*)?$",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
	);

	private static IntPtr dllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) {
		if (matchLib("injuremisc", libraryName))
			return injuremisc;
		return IntPtr.Zero;
	}
}

public sealed class LibmiscNativeFixture {
	public LibmiscNativeFixture() {
		LibmiscNativeLoader.Init();
	}
}

[CollectionDefinition("needs_libinjuremisc")]
public sealed class NeedsLibmiscCollection : ICollectionFixture<LibmiscNativeFixture> {
}
