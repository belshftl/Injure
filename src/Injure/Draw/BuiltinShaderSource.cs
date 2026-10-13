// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Draw;

// loads the WGSL sources under shaders/, which are embedded into the assembly
internal static class BuiltinShaderSource {
	public static string Load(string name) {
		string path = "shaders/" + name;
		using Stream stream = typeof(BuiltinShaderSource).Assembly.GetManifestResourceStream(path) ??
			throw new InternalStateException($"built-in shader '{path}' is missing from the assembly");
		using StreamReader sr = new(stream);
		return sr.ReadToEnd();
	}
}
