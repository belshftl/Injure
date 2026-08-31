// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Injure.Mods.Runtime.Modif.Profiler;

/// <summary>
/// The runtime's view of the CLR profiler.
/// </summary>
/// <remarks>
/// <para>
/// The relationship is one-way. The managed part resolves tokens, transforms bodies, and encodes
/// bytes to then hand over to <see cref="SetPreparedBody(MethodIdentity, ReadOnlySpan{byte})"/>.
/// The profiler then stores them for the ReJIT callback.
/// </para>
/// <para>
/// Calls push requests through a channel to be acted on by a profiler thread, as most <c>ICorProfilerInfo</c>
/// methods are rejected from managed threads. Members are safe to call from any thread, but they may block
/// on work the profiler is already doing. The exception is
/// <see cref="SetPreparedBody(MethodIdentity, ReadOnlySpan{byte})"/>, as it touches only the profiler's own state.
/// </para>
/// </remarks>
internal interface IProfilerHost {
	/// <summary>
	/// Whether a profiler is actually attached.
	/// </summary>
	/// <remarks>
	/// False means the process was started without the profiling environment variables or the
	/// profiler failed to load. Necessary because inferring it from the environment is unreliable.
	/// </remarks>
	bool IsAttached { get; }

	/// <summary>
	/// Every module loaded so far.
	/// </summary>
	/// <remarks>
	/// A snapshot. Subscribe to <see cref="IProfilerEvents"/> rather than polling this.
	/// </remarks>
	ImmutableArray<ModuleInfo> GetLoadedModules();

	/// <summary>
	/// Reads a method's original IL, exactly as it appears in the module, with header and exception sections.
	/// </summary>
	ImmutableArray<byte> GetBaselineIl(MethodIdentity method);

	/// <summary>
	/// Gets the metadata of a loaded module.
	/// </summary>
	MetadataReader GetMetadata(ModuleId module);

	/// <summary>
	/// Gets the metadata emitter for a loaded module.
	/// </summary>
	IMetadataEmitter GetMetadataEmitter(ModuleId module);

	/// <summary>
	/// Stores the IL the profiler will install the next time a method is ReJIT'd.
	/// </summary>
	/// <remarks>
	/// Called before <see cref="RequestReJit"/>, and again on every subsequent change. The profiler
	/// copies the bytes, so <paramref name="body"/> need only remain alive for the duration of the call.
	/// Storing a body does not by itself change any running code and takes effect only on the next ReJIT.
	/// </remarks>
	void SetPreparedBody(MethodIdentity method, ReadOnlySpan<byte> body);

	/// <summary>
	/// Requests that a set of methods be ReJIT'd from their prepared bodies.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A request for a generic method definition covers every instantiation. Methods that have
	/// inlined a requested method are ReJIT'd too, so a patch is not defeated by having already been
	/// inlined somewhere.
	/// </para>
	/// <para>
	/// Asynchronous with respect to running code. Frames already executing continue on the old
	/// version, so a caller cannot assume the new IL is live when this returns.
	/// </para>
	/// </remarks>
	void RequestReJit(ReadOnlySpan<MethodIdentity> methods);

	/// <summary>
	/// Requests that a set of methods revert to the IL in their modules.
	/// </summary>
	/// <remarks>
	/// For when nothing at all should remain applied. Removing one owner's patches while others
	/// stay is a re-transform. Reverting also drops any prepared body, so a later re-patch needs to
	/// store one again.
	/// </remarks>
	void RequestRevert(ReadOnlySpan<MethodIdentity> methods);
}

/// <summary>
/// Lifecycle notifications from the profiler.
/// </summary>
/// <remarks>
/// None of these arrive on a JIT thread, so handlers are free to allocate, take locks, and call back
/// into <see cref="IProfilerHost"/>.
/// </remarks>
internal interface IProfilerEvents {
	/// <summary>
	/// Called once the profiler has initialized and its host is usable.
	/// </summary>
	event Action ProfilerReady;

	/// <summary>
	/// Called when a module's metadata becomes available.
	/// </summary>
	/// <remarks>
	/// Raised for every module already loaded before subscription as well, so a subscriber never has
	/// to reconcile against <see cref="IProfilerHost.GetLoadedModules"/>.
	/// </remarks>
	event Action<ModuleInfo> ModuleLoaded;

	/// <summary>
	/// Called when a module is about to unload.
	/// </summary>
	/// <remarks>
	/// Every cached body, token, and identity for the module must be dropped before this returns. Its
	/// <see cref="ModuleId"/> becomes invalid immediately afterwards and may be reused by a later
	/// module.
	/// </remarks>
	event Action<ModuleId> ModuleUnloading;

	/// <summary>
	/// Called when the profiler fails to install a prepared body.
	/// </summary>
	/// <remarks>
	/// The method keeps running its previous version. This is the only way to detect failures inside
	/// the ReJIT callback.
	/// </remarks>
	event Action<MethodIdentity, string> ReJitFailed;
}
