// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace Injure.Weaver;

public static class Verifier {
	public static void Verify(string path, IReadOnlyDictionary<uint, string> emitted, ModuleReaderParameters read) {
		var module = ModuleDefinition.FromFile(path, read);

		foreach ((uint token, string expected) in emitted) {
			MetadataToken mdToken = new(token);
			if (
				!module.TryLookupMember(mdToken, out IMetadataMember? member)
				|| member is not MethodDefinition method
			)
				throw new PatchException($"token 0x{token:X8} does not resolve to a method in the written module");

			string actual = TypeNameRenderer.Canonical(method);
			if (actual != expected)
				throw new PatchException($"token 0x{token:X8} resolves to '{actual}' but was emitted for '{expected}'");
		}
	}
}
