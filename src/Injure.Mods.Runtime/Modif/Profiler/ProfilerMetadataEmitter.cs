// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using Injure.Mods.Abstractions.Modif.Il;

namespace Injure.Mods.Runtime.Modif.Profiler;

/// <summary>
/// <see cref="IMetadataEmitter"/> for the profiler's <c>prof_define_*</c> exports.
/// </summary>
/// <remarks>
/// One instance per module, held by <see cref="ProfilerHost"/> for as long as the module is loaded.
/// Not thread-safe beyond what the profiler already serializes; a caller is expected to hold
/// whatever per-module lock the resolver used while emitting a batch.
/// </remarks>
internal sealed unsafe class ProfilerMetadataEmitter(ModuleId module) : IMetadataEmitter {
	private readonly nuint module = (nuint)module.Value;

	public int DefineAssemblyReference(IlAssemblyIdentity identity) {
		string name = identity.Name;
		byte[] publicKey = identity.PublicKeyToken.ToArray();

		uint token;
		fixed (char* pName = name)
		fixed (byte* pPublicKey = publicKey) {
			chk(
				Native.prof_define_assembly_ref(
					module,
					pName,
					(nuint)name.Length,
					(ushort)(identity.Version?.Major ?? 0),
					(ushort)(identity.Version?.Minor ?? 0),
					(ushort)Math.Max(identity.Version?.Build ?? 0, 0),
					(ushort)Math.Max(identity.Version?.Revision ?? 0, 0),
					publicKey.Length == 0 ? null : pPublicKey,
					(nuint)publicKey.Length,
					(uint)identity.Flags,
					&token
				),
				$"defining an AssemblyRef for '{name}'"
			);
		}
		return unchecked((int)token);
	}

	public int DefineTypeReference(int resolutionScope, string @namespace, string name) {
		uint token;
		fixed (char* pNamespace = @namespace)
		fixed (char* pName = name) {
			chk(
				Native.prof_define_type_ref(
					module,
					unchecked((uint)resolutionScope),
					@namespace.Length == 0 ? null : pNamespace,
					(nuint)@namespace.Length,
					pName,
					(nuint)name.Length,
					&token
				),
				$"defining a TypeRef for '{@namespace}.{name}'"
			);
		}
		return unchecked((int)token);
	}

	public int DefineTypeSpecification(ReadOnlySpan<byte> signature) {
		uint token;
		fixed (byte* pSignature = signature) {
			chk(
				Native.prof_define_type_spec(module, pSignature, (nuint)signature.Length, &token),
				"defining a TypeSpec"
			);
		}
		return unchecked((int)token);
	}

	public int DefineMemberReference(int parent, string name, ReadOnlySpan<byte> signature) {
		uint token;
		fixed (char* pName = name)
		fixed (byte* pSignature = signature) {
			chk(
				Native.prof_define_member_ref(
					module,
					unchecked((uint)parent),
					pName,
					(nuint)name.Length,
					pSignature,
					(nuint)signature.Length,
					&token
				),
				$"defining a MemberRef for '{name}'"
			);
		}
		return unchecked((int)token);
	}

	public int DefineMethodSpecification(int method, ReadOnlySpan<byte> signature) {
		uint token;
		fixed (byte* pSignature = signature) {
			chk(
				Native.prof_define_method_spec(
					module,
					unchecked((uint)method),
					pSignature,
					(nuint)signature.Length,
					&token
				),
				"defining a MethodSpec"
			);
		}
		return unchecked((int)token);
	}

	public int DefineStandaloneSignature(ReadOnlySpan<byte> signature) {
		uint token;
		fixed (byte* pSignature = signature) {
			chk(
				Native.prof_define_standalone_sig(module, pSignature, (nuint)signature.Length, &token),
				"defining a StandAloneSig"
			);
		}
		return unchecked((int)token);
	}

	public int DefineUserString(string value) {
		uint token;
		fixed (char* pValue = value) {
			chk(
				Native.prof_define_user_string(module, pValue, (nuint)value.Length, &token),
				"defining a user string"
			);
		}
		return unchecked((int)token);
	}

	public void Commit() => chk(Native.prof_apply_metadata(module), "publishing defined metadata");

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void chk(int hresult, string what) {
		if (hresult < 0)
			throw new ProfilerException(what, hresult);
	}
}
