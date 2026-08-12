// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Abstractions.MethodModification.Il.Metadata;

namespace Injure.Internals.Tests.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Decodes every method body in a fixed set of assemblies, re-encodes it, decodes the result, and
/// requires the two decoded bodies to be semantically identical.
/// </summary>
/// <remarks>
/// <para>
/// This is a very large umbrella test; failures here usually don't really have much info about what
/// went wrong. These exist as an extra correctness check and a way to catch the cases that haven't
/// been thought of.
/// </para>
/// <para>
/// The comparison is semantic rather than byte-for-byte since things like prefix order and compact
/// form choice is not preserved.
/// </para>
/// </remarks>
public sealed class IlRoundtripBlanketTests {
	private const int failureReportLimit = 25;

	public static TheoryData<string> Assemblies => new() {
		typeof(IlFixture.Mechanism).Assembly.Location,
		typeof(IlMethodBody).Assembly.Location,
		typeof(object).Assembly.Location,
	};

	[Theory]
	[MemberData(nameof(Assemblies))]
	public static void RoundtripsEveryMethodBody(string assemblyPath) {
		Assert.SkipWhen(string.IsNullOrEmpty(assemblyPath), "assembly has no on-disk location (single-file or trimmed host?)");

		using FileStream stream = File.OpenRead(assemblyPath);
		using PEReader peReader = new(stream);
		MetadataReader metadata = peReader.GetMetadataReader();
		BlanketTestsTokenResolver resolver = new(metadata);

		int considered = 0;
		int skipped = 0;
		int failed = 0;
		List<string> failures = new();

		foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions) {
			MethodDefinition method = metadata.GetMethodDefinition(handle);
			if (method.RelativeVirtualAddress == 0) {
				skipped++;
				continue;
			}
			considered++;
			try {
				roundtrip(peReader, metadata, handle, resolver);
			} catch (Exception ex) {
				failed++;
				if (failures.Count < failureReportLimit)
					failures.Add($"{describe(metadata, handle)}: {ex.GetType().Name}: {ex.Message}");
			}
		}

		Assert.True(considered > 0, $"no method bodies found in {assemblyPath}");
		if (failed != 0) {
			StringBuilder sb = new();
			sb.Append(failed).Append(" of ").Append(considered)
				.Append(" method bodies failed to round-trip (").Append(skipped).AppendLine(" bodyless methods skipped):");
			foreach (string failure in failures)
				sb.Append("  ").AppendLine(failure);
			if (failed > failures.Count)
				sb.Append("  ... and ").Append(failed - failures.Count).AppendLine(" more");
			Assert.Fail(sb.ToString());
		}
	}

	private static void roundtrip(PEReader peReader, MetadataReader metadata, MethodDefinitionHandle handle, BlanketTestsTokenResolver resolver) {
		MethodDefinition method = metadata.GetMethodDefinition(handle);
		MethodBodyBlock original = peReader.GetMethodBody(method.RelativeVirtualAddress);

		IlMethodBody decoded = SrmMethodBodyDecoder.Decode(metadata, handle, original, default);
		IlEncodedMethodBody encoded = SrmMethodBodyEncoder.Prepare(decoded, resolver);

		Span<byte> bytes = encoded.Size <= 1024 ? stackalloc byte[encoded.Size] : new byte[encoded.Size];
		encoded.WriteTo(bytes);
		Assert.Equal(encoded.Size, bytes.Length);

		IlMethodBody redecoded;
		unsafe {
			fixed (byte* p = bytes)
				redecoded = SrmMethodBodyDecoder.Decode(metadata, handle, new BlobReader(p, bytes.Length), default);
		}

		AssertSemanticallyEqual(decoded, redecoded);
		Assert.Equal(IlMaxStackAnalyzer.Analyze(decoded), IlMaxStackAnalyzer.Analyze(redecoded));
	}

	/// <summary>
	/// Asserts two decoded bodies to be equal in everything the encoder is responsible for preserving.
	/// </summary>
	/// <remarks>
	/// Anchor IDs are comparable across the two decodes because the decoder assigns them by position,
	/// so equal instruction counts imply equal anchor numbering. That is what lets branch and
	/// exception-region targets be compared directly rather than through a boundary mapping.
	/// </remarks>
	internal static void AssertSemanticallyEqual(IlMethodBody expected, IlMethodBody actual) {
		Assert.Equal(expected.Instructions.Count, actual.Instructions.Count);
		Assert.Equal(expected.InitLocals, actual.InitLocals);
		Assert.Equal(expected.Locals.Length, actual.Locals.Length);
		for (int i = 0; i < expected.Locals.Length; i++)
			assertTypeEqual(expected.Locals[i], actual.Locals[i], $"local {i}");

		for (int i = 0; i < expected.Instructions.Count; i++) {
			IlInstruction a = expected.Instructions[i];
			IlInstruction b = actual.Instructions[i];
			Assert.True(a.OpCode == b.OpCode, $"instruction {i}: opcode {a.OpCode} became {b.OpCode}");
			Assert.True(
				IlReferenceMatching.OperandEquals(a.Operand, b.Operand),
				$"instruction {i} ({a.OpCode}): operand {a.Operand} became {b.Operand}"
			);
			Assert.True(a.Prefixes == b.Prefixes, $"instruction {i} ({a.OpCode}): prefixes {a.Prefixes} became {b.Prefixes}");
		}

		Assert.Equal(expected.ExceptionRegions.Count, actual.ExceptionRegions.Count);
		for (int i = 0; i < expected.ExceptionRegions.Count; i++) {
			IlExceptionRegion a = expected.ExceptionRegions[i];
			IlExceptionRegion b = actual.ExceptionRegions[i];
			Assert.True(a.Kind == b.Kind, $"exception region {i}: kind {a.Kind} became {b.Kind}");
			Assert.True(a.TryStart == b.TryStart && a.TryEnd == b.TryEnd, $"exception region {i}: try range moved");
			Assert.True(
				a.HandlerStart == b.HandlerStart && a.HandlerEnd == b.HandlerEnd,
				$"exception region {i}: handler range moved"
			);
			Assert.True(a.FilterStart == b.FilterStart, $"exception region {i}: filter start moved");
			if (a.CatchType is null || b.CatchType is null)
				Assert.True(a.CatchType is null && b.CatchType is null, $"exception region {i}: catch type appeared or vanished");
			else
				assertTypeEqual(a.CatchType, b.CatchType, $"exception region {i} catch type");
		}
	}

	private static void assertTypeEqual(IlTypeRef expected, IlTypeRef actual, string what) =>
		Assert.True(
			IlReferenceMatching.OperandEquals(new IlTypeOperand(expected), new IlTypeOperand(actual)),
			$"{what}: {expected} became {actual}"
		);

	private static string describe(MetadataReader metadata, MethodDefinitionHandle handle) {
		MethodDefinition method = metadata.GetMethodDefinition(handle);
		TypeDefinition declaring = metadata.GetTypeDefinition(method.GetDeclaringType());
		string ns = declaring.Namespace.IsNil ? "" : metadata.GetString(declaring.Namespace) + ".";
		return $"{ns}{metadata.GetString(declaring.Name)}::{metadata.GetString(method.Name)}";
	}
}

/// <summary>
/// An <see cref="IIlTokenResolver"/> that maps decoded references back to the handles they came
/// from, so encoder output can be decoded again through the same metadata.
/// </summary>
/// <remarks>
/// <para>
/// The map is built by resolving every row of every reference table once, rather than by recording
/// what the decoder saw. That keeps the actual decoder free of test stuff, and has the side
/// benefit of exercising <c>SrmReferenceDecoder</c> against every row in the module, including rows
/// no method body references.
/// </para>
/// <para>
/// Distinct handles can decode to equal references, e.g. a <c>TypeDef</c> and a <c>TypeRef</c> naming
/// the same type or duplicate <c>MemberRef</c> rows. Either handle round-trips correctly, so the
/// map keeps the first and ignores the rest.
/// </para>
/// <para>
/// Map building is intentionally tolerant, where a row that fails to decode is skipped rather than
/// throwing, because a genuinely unresolvable reference on a real method body should already surface as
/// an encode failure.
/// </para>
/// </remarks>
internal sealed class BlanketTestsTokenResolver : IIlTokenResolver {
	private readonly Dictionary<IlTypeRef, EntityHandle> types = new();
	private readonly Dictionary<IlMethodRef, EntityHandle> methods = new();
	private readonly Dictionary<IlFieldRef, EntityHandle> fields = new();
	private readonly Dictionary<IlMethodSignature, StandaloneSignatureHandle> callSites = new();
	private readonly Dictionary<string, UserStringHandle> userStrings = new(StringComparer.Ordinal);
	private readonly IlModuleIdentity moduleIdentity;

	/// <summary>
	/// How many times <see cref="ResolveLocals"/> was able to use the origin hint instead of searching.
	/// </summary>
	public int LocalSignatureOriginHits { get; private set; }

	public BlanketTestsTokenResolver(MetadataReader metadata) {
		ArgumentNullException.ThrowIfNull(metadata);
		SrmReferenceDecoder decoder = new(metadata);
		moduleIdentity = decoder.ModuleIdentity;

		foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
			tryAdd(types, () => decoder.ResolveType(handle), handle);
		foreach (TypeReferenceHandle handle in metadata.TypeReferences)
			tryAdd(types, () => decoder.ResolveType(handle), handle);
		forEachRow(metadata, TableIndex.TypeSpec, rowId => {
			TypeSpecificationHandle handle = MetadataTokens.TypeSpecificationHandle(rowId);
			tryAdd(types, () => decoder.ResolveType(handle), handle);
		});

		foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
			tryAdd(methods, () => decoder.ResolveMethod(handle), handle);
		forEachRow(metadata, TableIndex.MethodSpec, rowId => {
			MethodSpecificationHandle handle = MetadataTokens.MethodSpecificationHandle(rowId);
			tryAdd(methods, () => decoder.ResolveMethod(handle), handle);
		});
		foreach (FieldDefinitionHandle handle in metadata.FieldDefinitions)
			tryAdd(fields, () => decoder.ResolveField(handle), handle);

		// a MemberRef is either a method or a field, and the row doesn't say which, try both
		foreach (MemberReferenceHandle handle in metadata.MemberReferences) {
			tryAdd(methods, () => decoder.ResolveMethod(handle), handle);
			tryAdd(fields, () => decoder.ResolveField(handle), handle);
		}

		// a StandaloneSig is a callsite or locals signature, only callsites are resolvable here
		forEachRow(metadata, TableIndex.StandAloneSig, rowId => {
			StandaloneSignatureHandle handle = MetadataTokens.StandaloneSignatureHandle(rowId);
			try {
				IlMethodSignature signature = decoder.ResolveCallSite(handle);
				callSites.TryAdd(signature, handle);
			} catch (BadImageFormatException) {
			} catch (NotSupportedException) {
			} catch (Exception ex) {
				throw new InvalidOperationException($"resolving token 0x{MetadataTokens.GetToken(handle):x8} failed", ex);
			}
		});

		buildUserStringMap(metadata);
	}

	public EntityHandle ResolveType(IlTypeRef type) => lookup(types, type, "type");
	public EntityHandle ResolveMethod(IlMethodRef method) => lookup(methods, method, "method");
	public EntityHandle ResolveField(IlFieldRef field) => lookup(fields, field, "field");

	public StandaloneSignatureHandle ResolveCallSite(IlMethodSignature signature) =>
		callSites.TryGetValue(signature, out StandaloneSignatureHandle handle)
			? handle
			: throw new InvalidOperationException($"no call site row for signature '{signature}'");

	public StandaloneSignatureHandle ResolveLocals(ImmutableArray<IlTypeRef> locals, IlLocalSignatureOrigin origin) {
		// a roundtrip never changes the locals so an origin hint from this module is usable
		// anything else would need a locals signature we can't create
		if (!origin.IsValid || origin.Module != moduleIdentity)
			throw new InvalidOperationException("locals signature origin is missing or from another module");
		LocalSignatureOriginHits++;
		return origin.Handle;
	}

	public UserStringHandle ResolveUserString(string value) =>
		userStrings.TryGetValue(value, out UserStringHandle handle)
			? handle
			: throw new InvalidOperationException($"no user string heap entry for a string of length {value.Length}");

	private void buildUserStringMap(MetadataReader metadata) {
		int size = metadata.GetHeapSize(HeapIndex.UserString);
		int offset = 1; // offset 0 is the empty entry
		while (offset < size) {
			UserStringHandle handle = MetadataTokens.UserStringHandle(offset);
			string val;
			try {
				val = metadata.GetUserString(handle);
			} catch (BadImageFormatException) {
				return;
			}
			userStrings.TryAdd(val, handle);
			int blobLength = val.Length == 0 ? 0 : 2 * val.Length + 1;
			offset += compressedSize(blobLength) + blobLength;
		}
	}

	private static int compressedSize(int value) => value switch {
		< 0x80 => 1,
		< 0x4000 => 2,
		_ => 4,
	};

	private static void forEachRow(MetadataReader metadata, TableIndex table, Action<int> action) {
		int count = metadata.GetTableRowCount(table);
		for (int rowId = 1; rowId <= count; rowId++)
			action(rowId);
	}

	private static void tryAdd<T>(Dictionary<T, EntityHandle> map, Func<T> resolve, EntityHandle handle) where T : notnull {
		try {
			map.TryAdd(resolve(), handle);
		} catch (BadImageFormatException) {
		} catch (NotSupportedException) {
		} catch (Exception ex) {
			throw new InvalidOperationException($"resolving token 0x{MetadataTokens.GetToken(handle):x8} failed", ex);
		}
	}

	private static EntityHandle lookup<T>(Dictionary<T, EntityHandle> map, T key, string what) where T : notnull =>
		map.TryGetValue(key, out EntityHandle handle)
			? handle
			: throw new InvalidOperationException($"no metadata row for {what} '{key}'");
}
