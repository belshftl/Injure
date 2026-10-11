// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Runtime.InteropServices;
using Injure.Gpu;

namespace Injure.Draw;

// =================================================================================================
//
// A *chunk* is a CPU array + a GPU buffer of the same capacity.
//
// Everything an OwnedCanvas draws ends up in a CanvasArena; vertices and indices go into
// *geometry chunks*, and per-draw-range uniforms go into *uniform chunks*.
//
// While the canvas draws, only the CPU arrays are written into. Once the OwnedCanvas is disposed,
// the used portions of every chunk are written into the corresponding GPU buffers; vertex/index
// arrays are written into vertex/index buffers, and the byte-blob array for uniforms is written
// into a buffer from which the uniforms are bound via dynamic offset.
//
// Queue writes (WriteToBuffer) are ordered against submissions, so given two frames A and B where
// B comes later than A, the uploads for B are ordered after A's submit, allowing the same GPU
// buffers to be overwritten every frame. The problem with that is running out of space; GPU buffers
// can't be grown, and you can't "re-point" a recorded draw to a new buffer, so there has to exist
// a chunk system, where running out of room moves on to another, suitably-sized chunk.
//
// (Note: the above only works if the arena isn't written for a new frame before the previous frame
// that used it was submitted, so an arena is only returned to the pool once the frame that was
// using it is done with it, via RenderFrame.AddOrderedDisposable. This also has the useful property
// of letting two canvases draw into the same frame one-after-another: if a canvas draws into a
// frame, is disposed, and then another canvas is opened for that frame to draw into it, the arena
// the first canvas has rented hasn't been returned to the pool yet, so the second canvas will rent
// a different arena.)
//
// The geometry chunk list, within one use of an arena, looks like:
//   [ used this arena use ... | current | leftovers from earlier arena uses, all empty ... ]
//                               ^ geometryIndex
//
// A draw that fits the current chunk goes into there; a draw that doesn't:
//   1. Advances geometryIndex by 1 if the current chunk is non-empty;
//   2. Searches for the first leftover that's big enough for the draw;
//   3.
//     a. If such a leftover chunk is found, swaps it into place.
//     b. If no such leftover chunk is found, allocates and inserts a new one of sufficient size.
//
// This works on an empty geometry chunk list by treating "geometryIndex past the end" as an empty
// current chunk. This whole setup has the property of making it so that the chunks used in an arena
// use always form a contiguous prefix of the list.
//
// Uniform chunks all have the same capacity, so any leftover fits, so they're just taken in order.
//
// To avoid memory only ever growing, memory that stopped being needed is released once it's been
// unused for at least CanvasMemoryPolicy.RetainForCanvases arena uses and at least
// CanvasMemoryPolicy.RetainForTime time. This applies to both chunks and whole arenas in the pool.
// Requiring both fixes the FPS problem:
// - If only a certain amount of elapsed real time is required, it's possible that the FPS is
//   particularly low, and as such trimming happens too eagerly / based on too few samples; and
// - If only a certain amount of canvases (frames) is required, it's possible that the FPS is
//   particularly high, as such meaning less time passes for game/world state to change, and a
//   higher possibility of all the draws in the timeframe being similar.
//
// Releasing a GPU buffer only drops a reference, and wgpu-native keeps it alive until submitted
// work that uses it is done, so trimming is safe while earlier frames are still in flight.
//
// ==============================================================================================

/// <summary>
/// The geometry and uniforms one <see cref="OwnedCanvas"/> records, uploaded once when it finishes;
/// see the comment at the top of this file.
/// </summary>
internal sealed class CanvasArena(CanvasDeviceResources res) : IDisposable {
	public const int DefaultChunkVertices = 16384;
	public const int DefaultChunkIndices = 32768;
	public const int DefaultChunkUniforms = 256;

	/// <summary>
	/// Decay bookkeeping shared by both chunk kinds.
	/// </summary>
	public abstract class Chunk : IDisposable {
		// consecutive arena uses this chunk went unused in, and when it was last used
		public int UnusedUses;
		public long LastUsedTimestamp = Stopwatch.GetTimestamp();

		public abstract bool IsEmpty { get; }

		public abstract void Clear();

		public abstract void Dispose();
	}

	public sealed class GeometryChunk(GpuDevice device, int vertexCapacity, int indexCapacity) : Chunk {
		public readonly Vertex2dTextureColor[] Vertices = new Vertex2dTextureColor[vertexCapacity];
		public readonly uint[] Indices = new uint[indexCapacity];
		public readonly GpuBuffer VertexBuffer = device.CreateBuffer(
			(ulong)(vertexCapacity * Vertex2dTextureColor.Size), BufferUsage.Vertex | BufferUsage.CopyDst
		);
		public readonly GpuBuffer IndexBuffer = device.CreateBuffer(
			(ulong)(indexCapacity * sizeof(uint)), BufferUsage.Index | BufferUsage.CopyDst
		);
		public int VertexCount;
		public int IndexCount;

		public override bool IsEmpty => VertexCount == 0 && IndexCount == 0;

		public bool Fits(int vertices, int indices) =>
			VertexCount + vertices <= Vertices.Length && IndexCount + indices <= Indices.Length;

		public override void Clear() {
			VertexCount = 0;
			IndexCount = 0;
		}

		public override void Dispose() {
			IndexBuffer.Dispose();
			VertexBuffer.Dispose();
		}
	}

	public sealed class UniformChunk : Chunk {
		private readonly uint stride;
		public readonly byte[] Data;
		public readonly GpuBuffer Buffer;
		public readonly GpuBindGroup BindGroup;
		public int Count;

		public UniformChunk(CanvasDeviceResources res, int capacity) {
			stride = res.UniformStride;
			Data = new byte[capacity * stride];
			Buffer = res.Device.CreateBuffer((ulong)Data.Length, BufferUsage.Uniform | BufferUsage.CopyDst);
			BindGroup = res.Device.CreateUniformBufferBindGroup(res.UniformLayout, Buffer, size: (ulong)CanvasUniform.Size);
		}

		public override bool IsEmpty => Count == 0;

		public bool HasRoom => (Count + 1) * stride <= Data.Length;

		/// <summary>
		/// Appends <paramref name="u"/> and returns its dynamic offset.
		/// </summary>
		public uint Add(in CanvasUniform u) {
			uint offset = (uint)Count * stride;
			MemoryMarshal.Write(Data.AsSpan((int)offset, CanvasUniform.Size), in u);
			Count++;
			return offset;
		}

		public override void Clear() => Count = 0;

		public override void Dispose() {
			BindGroup.Dispose();
			Buffer.Dispose();
		}
	}

	private readonly CanvasDeviceResources res = res;
	private readonly List<GeometryChunk> geometry = new();
	private readonly List<UniformChunk> uniforms = new();
	private int geometryIndex;
	private int uniformIndex;

	/// <summary>
	/// Returns a chunk with room for the given amount of vertices and indices: the current one if it
	/// has room, otherwise either a suitable one from the leftovers or a newly allocated one.
	/// </summary>
	public GeometryChunk ReserveGeometry(int vertices, int indices) {
		if (geometryIndex < geometry.Count && geometry[geometryIndex].Fits(vertices, indices))
			return geometry[geometryIndex];
		if (geometryIndex < geometry.Count && !geometry[geometryIndex].IsEmpty)
			geometryIndex++;
		for (int i = geometryIndex; i < geometry.Count; i++) {
			if (!geometry[i].Fits(vertices, indices))
				continue;
			(geometry[i], geometry[geometryIndex]) = (geometry[geometryIndex], geometry[i]);
			return geometry[geometryIndex];
		}
		GeometryChunk chunk = new(res.Device, Math.Max(DefaultChunkVertices, vertices), Math.Max(DefaultChunkIndices, indices));
		geometry.Insert(geometryIndex, chunk);
		return chunk;
	}

	/// <summary>
	/// Appends <paramref name="u"/> and returns the chunk it's in and its dynamic offset there.
	/// </summary>
	public (UniformChunk Chunk, uint Offset) AddUniform(in CanvasUniform u) {
		if (uniformIndex < uniforms.Count && !uniforms[uniformIndex].HasRoom)
			uniformIndex++;
		if (uniformIndex == uniforms.Count)
			uniforms.Add(new UniformChunk(res, DefaultChunkUniforms));
		UniformChunk chunk = uniforms[uniformIndex];
		return (chunk, chunk.Add(in u));
	}

	/// <summary>
	/// Uploads the used part of every chunk.
	/// </summary>
	public void Upload() {
		foreach (GeometryChunk c in geometry) {
			if (c.VertexCount != 0)
				res.Device.WriteToBuffer(c.VertexBuffer, 0, c.Vertices.AsSpan(0, c.VertexCount));
			if (c.IndexCount != 0)
				res.Device.WriteToBuffer(c.IndexBuffer, 0, c.Indices.AsSpan(0, c.IndexCount));
		}
		foreach (UniformChunk c in uniforms)
			if (c.Count != 0)
				res.Device.WriteToBuffer(c.Buffer, 0, c.Data.AsSpan(0, c.Count * (int)res.UniformStride));
	}

	/// <summary>
	/// Prepares the arena for its next use, after the frame of its last use is done with it, and
	/// releases chunks that <paramref name="policy"/> says have been unused for long enough.
	/// </summary>
	public void Reset(in CanvasMemoryPolicy policy, long now) {
		resetChunks(geometry, in policy, now);
		resetChunks(uniforms, in policy, now);
		geometryIndex = 0;
		uniformIndex = 0;
	}

	private static void resetChunks<T>(List<T> chunks, in CanvasMemoryPolicy policy, long now) where T : Chunk {
		for (int i = chunks.Count - 1; i >= 0; i--) {
			T c = chunks[i];
			if (!c.IsEmpty) {
				c.UnusedUses = 0;
				c.LastUsedTimestamp = now;
				c.Clear();
			} else if (++c.UnusedUses >= policy.RetainForCanvases && Stopwatch.GetElapsedTime(c.LastUsedTimestamp, now) >= policy.RetainForTime) {
				c.Dispose();
				chunks.RemoveAt(i);
			}
		}
	}

	// for tests
	internal int GeometryChunkCount => geometry.Count;
	internal int UniformChunkCount => uniforms.Count;

	public void Dispose() {
		foreach (GeometryChunk c in geometry)
			c.Dispose();
		foreach (UniformChunk c in uniforms)
			c.Dispose();
		geometry.Clear();
		uniforms.Clear();
	}
}

/// <summary>
/// The arenas of one device; see the comment at the top of this file.
/// </summary>
internal sealed class CanvasArenaPool(CanvasDeviceResources res) : IDisposable {
	// returns the arena to the pool when the frame it was used in is done with it
	private sealed class Lease(CanvasArenaPool pool, CanvasArena arena) : IDisposable {
		private bool returned;

		public void Dispose() {
			if (returned)
				return;
			returned = true;
			pool.ret(arena);
		}
	}

	/// <summary>
	/// A free arena, with the rent count and timestamp as of when it came back, for decay.
	/// </summary>
	/// <remarks>
	/// The <see langword="default"/> value is invalid.
	/// </remarks>
	private readonly record struct FreeArena(CanvasArena Arena, long ReturnedAtRent, long ReturnedAt);

	private readonly Lock poolLock = new();
	private readonly List<FreeArena> free = new(); // used as a stack, the most recently returned last
	private readonly List<CanvasArena> all = new();
	private CanvasMemoryPolicy policy = CanvasMemoryPolicy.Default;
	private long rentCount;
	private bool disposed;

	public CanvasMemoryPolicy Policy {
		get {
			lock (poolLock)
				return policy;
		}
		set {
			lock (poolLock)
				policy = value;
		}
	}

	/// <summary>
	/// Rents an arena for a canvas drawing into <paramref name="frame"/>.
	/// </summary>
	/// <remarks>
	/// The arena is returned once <paramref name="frame"/> disposes its ordered disposables, i.e.
	/// after it was submitted or dropped.
	/// </remarks>
	public CanvasArena Rent(RenderFrame frame) {
		CanvasArena arena;
		lock (poolLock) {
			ObjectDisposedException.ThrowIf(disposed, this);
			rentCount++;
			if (free.Count != 0) {
				arena = free[^1].Arena;
				free.RemoveAt(free.Count - 1);
			} else {
				arena = new CanvasArena(res);
				all.Add(arena);
			}
			trimFree();
		}
		try {
			frame.AddOrderedDisposable(new Lease(this, arena));
		} catch {
			ret(arena);
			throw;
		}
		return arena;
	}

	private void ret(CanvasArena arena) {
		lock (poolLock) {
			if (disposed)
				return;
			long now = Stopwatch.GetTimestamp();
			arena.Reset(in policy, now);
			free.Add(new FreeArena(arena, rentCount, now));
		}
	}

	// releases arenas that went unrented for long enough; the most recently returned ones are at the
	// end and the least likely to qualify, so the list stays in order
	private void trimFree() {
		long now = Stopwatch.GetTimestamp();
		for (int i = free.Count - 1; i >= 0; i--) {
			FreeArena f = free[i];
			if (rentCount - f.ReturnedAtRent < policy.RetainForCanvases || Stopwatch.GetElapsedTime(f.ReturnedAt, now) < policy.RetainForTime)
				continue;
			f.Arena.Dispose();
			all.Remove(f.Arena);
			free.RemoveAt(i);
		}
	}

	// for tests
	internal int TotalGeometryChunkCount {
		get {
			lock (poolLock)
				return all.Sum(static a => a.GeometryChunkCount);
		}
	}

	internal int ArenaCount {
		get {
			lock (poolLock)
				return all.Count;
		}
	}

	public void Dispose() {
		lock (poolLock) {
			if (disposed)
				return;
			disposed = true;
			foreach (CanvasArena a in all)
				a.Dispose();
			all.Clear();
			free.Clear();
		}
	}
}
