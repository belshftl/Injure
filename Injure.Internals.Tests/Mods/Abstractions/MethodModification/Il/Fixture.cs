// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Abstractions.MethodModification.Il.Metadata;

namespace Injure.Internals.Tests.Mods.Abstractions.MethodModification.Il;

internal static class Fixture {
	public static MethodDefinitionHandle Find(MetadataReader metadata, string declaringTypeName, string methodName) {
		foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions) {
			MethodDefinition method = metadata.GetMethodDefinition(handle);
			if (method.RelativeVirtualAddress == 0 || metadata.GetString(method.Name) != methodName)
				continue;
			TypeDefinition owner = metadata.GetTypeDefinition(method.GetDeclaringType());
			if (metadata.GetString(owner.Name) == declaringTypeName)
				return handle;
		}
		throw new InternalStateException($"{declaringTypeName}::{methodName} not found in the fixture");
	}

	public static IlMethodBody Decode(
		PEReader peReader,
		MetadataReader metadata,
		string declaringTypeName,
		string methodName,
		InternalIlProvenance baseline = default
	) {
		MethodDefinitionHandle handle = Find(metadata, declaringTypeName, methodName);
		MethodDefinition method = metadata.GetMethodDefinition(handle);
		return SrmMethodBodyDecoder.Decode(
			metadata,
			handle,
			peReader.GetMethodBody(method.RelativeVirtualAddress),
			baseline
		);
	}

	public static IlMethodBody RoundtripThroughMetadata(
		PEReader peReader,
		MetadataReader metadata,
		IlMethodBody body,
		string declaringTypeName,
		string methodName
	) {
		IlEncodedMethodBody encoded = SrmMethodBodyEncoder.Prepare(body, new BlanketTestsTokenResolver(metadata));
		byte[] bytes = encoded.ToArray();
		unsafe {
			fixed (byte* p = bytes)
				return SrmMethodBodyDecoder.Decode(
					metadata,
					Find(metadata, declaringTypeName, methodName),
					new BlobReader(p, bytes.Length),
					default
				);
		}
	}

	public static IlMethodRef SoleCall(IlMethodBody body, string name) =>
		body.Instructions
			.Where(static i => i.OpCode is ILOpCode.Call or ILOpCode.Callvirt)
			.Select(static i => ((IlMethodOperand)i.Operand).Method)
			.Single(m => m.Name == name);
}
