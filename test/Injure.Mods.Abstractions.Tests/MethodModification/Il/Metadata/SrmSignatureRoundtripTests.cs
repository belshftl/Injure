// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Abstractions.MethodModification.Il.Metadata;

namespace Injure.Mods.Abstractions.Tests.MethodModification.Il.Metadata;

/// <summary>
/// Re-encodes every signature in a fixed set of assemblies and requires the bytes to match the
/// originals exactly.
/// </summary>
/// <remarks>
/// Signature encoding is canonical, so unlike method bodies this can be compared byte for byte
/// against real compiler output, which makes this a fairly strong test. Unlike method body
/// round-tripping, which needs looser semantic matching due to compact instruction forms and
/// prefix ordering and such, here the encoder has no freedom.
/// </remarks>
public sealed class SrmSignatureRoundtripTests {
	private const int failureReportLimit = 25;

	public static TheoryData<string> Assemblies => new() {
		typeof(IlFixture.Mechanism).Assembly.Location,
		typeof(IlMethodBody).Assembly.Location,
		typeof(object).Assembly.Location,
	};

	[Theory]
	[MemberData(nameof(Assemblies))]
	public static void EverySignatureReencodesToItsOriginalBytes(string assemblyPath) {
		Assert.SkipWhen(string.IsNullOrEmpty(assemblyPath), "assembly has no on-disk location");

		using FileStream stream = File.OpenRead(assemblyPath);
		using PEReader peReader = new(stream);
		MetadataReader metadata = peReader.GetMetadataReader();
		SrmReferenceDecoder decoder = new(metadata);
		RoundtripTestsTokenResolver resolver = new(metadata);

		int considered = 0;
		int failed = 0;
		List<string> failures = new();

		void chk(string what, BlobHandle original, Func<BlobBuilder> reencode) {
			considered++;
			try {
				byte[] expected = metadata.GetBlobBytes(original);
				byte[] actual = reencode().ToArray();
				if (!expected.AsSpan().SequenceEqual(actual))
					throw new InvalidOperationException($"{hex(expected)} became {hex(actual)}");
			} catch (Exception ex) {
				failed++;
				if (failures.Count < failureReportLimit)
					failures.Add($"{what}: {ex.GetType().Name}: {ex.Message}");
			}
		}

		foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions) {
			MethodDefinition method = metadata.GetMethodDefinition(handle);
			chk(
				$"method {metadata.GetString(method.Name)}",
				method.Signature,
				() => SrmSignatureEncoder.EncodeMethodSignature(decoder.ResolveMethod(handle).Signature, resolver)
			);
		}

		foreach (FieldDefinitionHandle handle in metadata.FieldDefinitions) {
			FieldDefinition field = metadata.GetFieldDefinition(handle);
			chk(
				$"field {metadata.GetString(field.Name)}",
				field.Signature,
				() => SrmSignatureEncoder.EncodeFieldSignature(decoder.ResolveField(handle).FieldType, resolver)
			);
		}

		foreach (TypeSpecificationHandle handle in typeSpecifications(metadata)) {
			TypeSpecification spec = metadata.GetTypeSpecification(handle);
			chk(
				"type specification",
				spec.Signature,
				() => SrmSignatureEncoder.EncodeTypeSpecification(decoder.ResolveType(handle), resolver)
			);
		}

		foreach (StandaloneSignatureHandle handle in standaloneSignatures(metadata)) {
			StandaloneSignature signature = metadata.GetStandaloneSignature(handle);
			BlobReader reader = metadata.GetBlobReader(signature.Signature);
			byte header = reader.ReadByte();
			if ((header & 0x0f) == 0x07)
				chk(
					"local signature",
					signature.Signature,
					() => SrmSignatureEncoder.EncodeLocalSignature(decoder.ResolveLocals(handle), resolver)
				);
			else
				chk(
					"call site",
					signature.Signature,
					() => SrmSignatureEncoder.EncodeMethodSignature(decoder.ResolveCallSite(handle), resolver)
				);
		}

		Assert.True(considered > 0, $"no signatures found in {assemblyPath}");
		if (failed != 0) {
			StringBuilder message = new();
			message.Append(failed).Append(" of ").Append(considered).AppendLine(" signatures failed to re-encode:");
			foreach (string failure in failures)
				message.Append("  ").AppendLine(failure);
			if (failed > failures.Count)
				message.Append("  ... and ").Append(failed - failures.Count).AppendLine(" more");
			Assert.Fail(message.ToString());
		}
	}

	private static IEnumerable<TypeSpecificationHandle> typeSpecifications(MetadataReader metadata) {
		int count = metadata.GetTableRowCount(TableIndex.TypeSpec);
		for (int rowId = 1; rowId <= count; rowId++)
			yield return MetadataTokens.TypeSpecificationHandle(rowId);
	}

	private static IEnumerable<StandaloneSignatureHandle> standaloneSignatures(MetadataReader metadata) {
		int count = metadata.GetTableRowCount(TableIndex.StandAloneSig);
		for (int rowId = 1; rowId <= count; rowId++)
			yield return MetadataTokens.StandaloneSignatureHandle(rowId);
	}

	private static string hex(ReadOnlySpan<byte> bytes) {
		StringBuilder sb = new(bytes.Length * 2);
		foreach (byte b in bytes)
			sb.Append(b.ToString("x2"));
		return sb.ToString();
	}
}
