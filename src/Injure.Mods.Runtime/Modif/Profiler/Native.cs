// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Injure.Mods.Runtime.Modif.Profiler;

internal static unsafe partial class Native {
	internal enum EventKind : int {
		ModuleLoaded = 1,
		ModuleUnloading = 2,
		ReJitFailed = 3,
		Shutdown = 4,
		Wakeup = 5,
	}

	[StructLayout(LayoutKind.Sequential)]
	internal readonly struct Event {
		public readonly EventKind Kind;
		// [+ 4 bytes of padding here on targets with 64 bit wide pointers]
		public readonly nuint Module;
		public readonly uint Token;
		public readonly int Hresult;
	} // aligned to 8 bytes on 64 bit and 4 bytes on 32 bit

	private const string clrprof = "injureclrprof";

	static Native() {
#if DEBUG
		if (IntPtr.Size == 8) {
			if (Unsafe.SizeOf<Event>() != 24)
				throw new InternalStateException("expected Event size to be 24 bytes");
			// c# has no alignof; skip alignment assertion
			if (Marshal.OffsetOf<Event>(nameof(Event.Kind)) != 0)
				throw new InternalStateException("expected Event Kind offset to be 0");
			if (Marshal.OffsetOf<Event>(nameof(Event.Module)) != 8)
				throw new InternalStateException("expected Event Module offset to be 8");
			if (Marshal.OffsetOf<Event>(nameof(Event.Token)) != 16)
				throw new InternalStateException("expected Event Token offset to be 16");
			if (Marshal.OffsetOf<Event>(nameof(Event.Hresult)) != 20)
				throw new InternalStateException("expected Event Hresult offset to be 20");
		} else if (IntPtr.Size == 4) {
			if (Unsafe.SizeOf<Event>() != 16)
				throw new InternalStateException("expected Event size to be 16 bytes");
			// c# has no alignof; skip alignment assertion
			if (Marshal.OffsetOf<Event>(nameof(Event.Kind)) != 0)
				throw new InternalStateException("expected Event Kind offset to be 0");
			if (Marshal.OffsetOf<Event>(nameof(Event.Module)) != 4)
				throw new InternalStateException("expected Event Module offset to be 4");
			if (Marshal.OffsetOf<Event>(nameof(Event.Token)) != 8)
				throw new InternalStateException("expected Event Token offset to be 8");
			if (Marshal.OffsetOf<Event>(nameof(Event.Hresult)) != 12)
				throw new InternalStateException("expected Event Hresult offset to be 12");
		} else {
			throw new NotSupportedException("was expecting a platform where the pointer width is 4 bytes or 8 bytes");
		}
#endif
		NativeLibrary.SetDllImportResolver(
			typeof(Native).Assembly,
			static (name, assembly, searchPath) => name == clrprof ? load() : IntPtr.Zero
		);
	}

	private static IntPtr load() {
		string? path = Environment.GetEnvironmentVariable("CORECLR_PROFILER_PATH");
		if (string.IsNullOrEmpty(path))
			throw new InvalidOperationException(
				"CORECLR_PROFILER_PATH is not set, so the profiler library cannot be located; the process must be started with the profiling env vars already set"
			);
		return NativeLibrary.Load(path);
	}

	// ============================================================================================
	// attachment and events
	[LibraryImport(clrprof)]
	public static partial int prof_is_attached();

	[LibraryImport(clrprof)]
	public static partial nuint prof_wait_events(Event* buf, nuint capacity, ulong timeoutMs);

	[LibraryImport(clrprof)]
	public static partial void prof_wakeup();

	// ============================================================================================
	// modules
	[LibraryImport(clrprof)]
	public static partial nuint prof_find_module(char* suffix, nuint len);

	[LibraryImport(clrprof)]
	public static partial int prof_module_path(nuint module, char* buf, nuint capacity, nuint* needed);

	[LibraryImport(clrprof)]
	public static partial int prof_module_mvid(nuint module, byte* mvid);

	/// <remarks>
	/// Tri-state; 1 = collectible, 0 = not collectible, -1 = no such module.
	/// </remarks>
	[LibraryImport(clrprof)]
	public static partial int prof_module_is_collectible(nuint module);

	// ============================================================================================
	// il
	[LibraryImport(clrprof)]
	public static partial int prof_get_il(nuint module, uint token, byte* buf, nuint capacity, nuint* needed);

	[LibraryImport(clrprof)]
	public static partial int prof_set_prepared(nuint module, uint token, byte* body, nuint len);

	[LibraryImport(clrprof)]
	public static partial int prof_request_rejit(nuint* modules, uint* tokens, nuint count);

	[LibraryImport(clrprof)]
	public static partial int prof_request_revert(nuint* modules, uint* tokens, nuint count);

	// ============================================================================================
	// metadata
	[LibraryImport(clrprof)]
	public static partial int prof_define_assembly_ref(
		nuint module,
		char* name,
		nuint nameLen,
		ushort major,
		ushort minor,
		ushort build,
		ushort revision,
		byte* publicKey,
		nuint publicKeyLen,
		uint flags,
		uint* token
	);

	[LibraryImport(clrprof)]
	public static partial int prof_define_type_ref(
		nuint module,
		uint scope,
		char* @namespace,
		nuint namespaceLen,
		char* name,
		nuint nameLen,
		uint* token
	);

	[LibraryImport(clrprof)]
	public static partial int prof_define_member_ref(
		nuint module,
		uint parent,
		char* name,
		nuint nameLen,
		byte* signature,
		nuint signatureLen,
		uint* token
	);

	[LibraryImport(clrprof)]
	public static partial int prof_define_type_spec(
		nuint module,
		byte* signature,
		nuint signatureLen,
		uint* token
	);

	[LibraryImport(clrprof)]
	public static partial int prof_define_method_spec(
		nuint module,
		uint method,
		byte* signature,
		nuint signatureLen,
		uint* token
	);

	[LibraryImport(clrprof)]
	public static partial int prof_define_standalone_sig(
		nuint module,
		byte* signature,
		nuint signatureLen,
		uint* token
	);

	[LibraryImport(clrprof)]
	public static partial int prof_define_user_string(
		nuint module,
		char* value,
		nuint len,
		uint* token
	);

	[LibraryImport(clrprof)]
	public static partial int prof_apply_metadata(nuint module);
}
