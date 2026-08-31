// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Injure.Mods.Runtime.Modif.Profiler;

/// <summary>
/// An <see cref="IProfilerHost"/> / <see cref="IProfilerEvents"/> for the real profiler.
/// </summary>
/// <remarks>
/// <para>
/// Events are drained rather than delivered; the profiler queues them and a thread here reads them.
/// Note that this means that the queue starts filling long before managed code starts to run, so the
/// modules loaded during startup are already waiting in the queue when the drain begins and arrive as
/// ordinary <see cref="ModuleLoaded"/> events.
/// </para>
/// <para>
/// A module's ID is meaningful only until its <see cref="ModuleUnloading"/> event has been drained.
/// The profiler drops its own state for that module the moment it publishes the event, so by the time
/// a handler runs, it's already gone and nothing about the module can be asked from the profiler anymore.
/// </para>
/// </remarks>
internal sealed unsafe class ProfilerHost : IProfilerHost, IProfilerEvents, IDisposable {
	private sealed class ModuleMetadataReader : IDisposable {
		private readonly FileStream stream;
		private readonly PEReader peReader;

		public MetadataReader Metadata { get; }

		public ModuleMetadataReader(string path) {
			stream = File.OpenRead(path);
			peReader = new PEReader(stream);
			Metadata = peReader.GetMetadataReader();
		}

		public void Dispose() {
			peReader.Dispose();
			stream.Dispose();
		}
	}

	/// <summary>
	/// Events drained per wait.
	/// </summary>
	/// <remarks>
	/// This amount of events is allocated on the stack. If you change it to a larger value, change it
	/// to something like a regular array allocation instead.
	/// </remarks>
	private const int drainBatchSize = 64;

	/// <summary>
	/// How long the drain thread waits before looping.
	/// </summary>
	/// <remarks>
	/// Events themselves wake the wait immediately, so this primarily just guards against the
	/// thread getting stuck forever in case the wakeup event is somehow missed.
	/// </remarks>
	private const ulong drainTimeoutMs = 2000;

	// the profiler state is global, so two instances would likely mess each other up and have
	// their drain threads race and such
	// as such, enforce at most 1 active instance at a time
	private static int active = 0;

	private readonly Dictionary<ModuleId, ModuleInfo> modules = new();
	private readonly Dictionary<ModuleId, ModuleMetadataReader> readers = new();
	private readonly Dictionary<ModuleId, ProfilerMetadataEmitter> emitters = new();
	private readonly Lock @lock = new();
	private readonly Thread drainThread;
	private int disposed = 0;

	public ProfilerHost() {
		if (!IsAttached)
			throw new InvalidOperationException("the profiler is not attached; the process must be launched with the profiling enviroment variables set");
		if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
			throw new InternalStateException("at most one ProfilerHost instance can be active at a time, as the profiler state is global");
		drainThread = new Thread(drain) {
			Name = "profiler-event-drain",
			IsBackground = true,
		};
		drainThread.Start();
	}

	public bool IsAttached => Native.prof_is_attached() != 0;

	public event Action? ProfilerReady;
	public event Action<ModuleInfo>? ModuleLoaded;
	public event Action<ModuleId>? ModuleUnloading;
	public event Action<MethodIdentity, string>? ReJitFailed;

	public void Dispose() {
		Volatile.Write(ref disposed, 1);
		Native.prof_wakeup();
		drainThread.Join();
		// after the drain thread is down and profiler state isn't being touched anymore it
		// should be safe to let another instance go active
		Volatile.Write(ref active, 0);
		lock (@lock) {
			foreach (ModuleMetadataReader reader in readers.Values)
				reader.Dispose();
			readers.Clear();
			emitters.Clear();
			modules.Clear();
		}
	}

	// ==========================================================================================
	// modules
	public ImmutableArray<ModuleInfo> GetLoadedModules() {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		lock (@lock)
			return modules.Values.ToImmutableArray();
	}

	/// <remarks>
	/// Modules with no files (i.e. dynamically emitted modules) are currently unsupported.
	/// </remarks>
	public MetadataReader GetMetadata(ModuleId module) {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		lock (@lock) {
			if (readers.TryGetValue(module, out ModuleMetadataReader? existing))
				return existing.Metadata;
			if (!modules.TryGetValue(module, out ModuleInfo info))
				throw new ArgumentException($"{module} is not loaded", nameof(module));
			if (string.IsNullOrEmpty(info.Path))
				throw new NotSupportedException($"{module} has no file, so its metadata cannot be read; patching dynamically emitted modules is currently unsupported");

			ModuleMetadataReader reader = new(info.Path);
			readers[module] = reader;
			return reader.Metadata;
		}
	}

	public IMetadataEmitter GetMetadataEmitter(ModuleId module) {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		lock (@lock) {
			if (!emitters.TryGetValue(module, out ProfilerMetadataEmitter? emitter)) {
				emitter = new ProfilerMetadataEmitter(module);
				emitters[module] = emitter;
			}
			return emitter;
		}
	}

	// ==========================================================================================
	// il
	public ImmutableArray<byte> GetBaselineIl(MethodIdentity method) {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		nuint module = (nuint)method.Module.Value;
		uint token = (uint)method.MethodDefToken;

		nuint needed;
		chk(Native.prof_get_il(module, token, null, 0, &needed), $"reading the baseline IL size for {method}");

		byte[] body = new byte[needed];
		fixed (byte* p = body)
			chk(Native.prof_get_il(module, token, p, needed, &needed), $"reading the baseline IL for {method}");
		return ImmutableCollectionsMarshal.AsImmutableArray(body);
	}

	/// <summary>
	/// Stores the IL to install on a method's next re-JIT.
	/// </summary>
	/// <remarks>
	/// The one call here that does not marshal onto the profiler's thread, because it touches only
	/// the profiler's own state. That is deliberate: it runs once per method in a transform pass, and
	/// a worker hop per method would dominate a large mod load.
	/// </remarks>
	public void SetPreparedBody(MethodIdentity method, ReadOnlySpan<byte> body) {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		fixed (byte* p = body)
			chk(
				Native.prof_set_prepared(
					(nuint)method.Module.Value,
					(uint)method.MethodDefToken,
					p,
					(nuint)body.Length
				),
				$"storing the prepared body for {method}"
			);
	}

	public void RequestReJit(ReadOnlySpan<MethodIdentity> methods) {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		batch(methods, &Native.prof_request_rejit, "requesting a ReJIT of");
	}

	public void RequestRevert(ReadOnlySpan<MethodIdentity> methods) {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		batch(methods, &Native.prof_request_revert, "reverting");
	}

	private static void batch(
		ReadOnlySpan<MethodIdentity> methods,
		delegate*<nuint*, uint*, nuint, int> fn,
		string what
	) {
		if (methods.IsEmpty)
			return;

		nuint[] modules = new nuint[methods.Length];
		uint[] tokens = new uint[methods.Length];
		for (int i = 0; i < methods.Length; i++) {
			modules[i] = (nuint)methods[i].Module.Value;
			tokens[i] = (uint)methods[i].MethodDefToken;
		}

		fixed (nuint* pModules = modules)
		fixed (uint* pTokens = tokens)
			chk(fn(pModules, pTokens, (nuint)methods.Length), $"{what} {methods.Length} method(s)");
	}

	// ==========================================================================================
	// event drain
	private void drain() {
		ProfilerReady?.Invoke();
		Native.Event* buf = stackalloc Native.Event[drainBatchSize];
		while (Volatile.Read(ref disposed) == 0) {
			nuint count = Native.prof_wait_events(buf, drainBatchSize, drainTimeoutMs);
			for (nuint i = 0; i < count; i++)
				if (!handle(buf[i]))
					return;
		}
	}

	/// <returns>
	/// <see langword="false"/> if the profiler has shut down and draining should stop.
	/// </returns>
	private bool handle(in Native.Event raised) {
		ModuleId module = new(raised.Module);
		switch (raised.Kind) {
		case Native.EventKind.ModuleLoaded:
			if (tryDescribe(module, out ModuleInfo info)) {
				lock (@lock)
					modules[module] = info;
				ModuleLoaded?.Invoke(info);
			}
			return true;
		case Native.EventKind.ModuleUnloading:
			lock (@lock) {
				modules.Remove(module);
				emitters.Remove(module);
				if (readers.Remove(module, out ModuleMetadataReader? reader))
					reader.Dispose();
			}
			ModuleUnloading?.Invoke(module);
			return true;
		case Native.EventKind.ReJitFailed:
			ReJitFailed?.Invoke(
				new MethodIdentity(module, (int)raised.Token),
				$"runtime rejected the ReJIT (hresult 0x{raised.Hresult:x8})"
			);
			return true;
		case Native.EventKind.Shutdown:
			return false;
		case Native.EventKind.Wakeup:
			return true;
		default:
			throw new InternalStateException($"unknown profiler event kind '{raised.Kind}' (profiler version mismatch)?");
		}
	}

	private static bool tryDescribe(ModuleId module, out ModuleInfo info) {
		info = default;
		nuint moduleId = (nuint)module.Value;

		int collectible = Native.prof_module_is_collectible(moduleId);
		if (collectible < 0)
			return false;

		nuint needed;
		if (Native.prof_module_path(moduleId, null, 0, &needed) < 0)
			return false;

		string path = "";
		if (needed > 0) {
			Span<char> buf = needed <= 512 ? stackalloc char[(int)needed] : new char[needed];
			fixed (char* p = buf) {
				if (Native.prof_module_path(moduleId, p, needed, &needed) < 0)
					return false;
				path = new string(p, 0, (int)needed);
			}
		}

		Span<byte> mvid = stackalloc byte[16];
		fixed (byte* p = mvid)
			if (Native.prof_module_mvid(moduleId, p) < 0)
				return false;

		info = new ModuleInfo(module, path, new Guid(mvid), collectible != 0);
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void chk(int status, string what) {
		if (status < 0)
			throw new ProfilerException(what, status);
	}
}
