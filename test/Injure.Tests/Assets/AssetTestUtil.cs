// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Text;
using Injure.Assets;
using Injure.Mods;

namespace Injure.Tests.Assets;

// a value without resources, which the store just drops
public sealed class PlainValue(string text) {
	public string Text { get; } = text;
}

// a value with resources, which records how and where it got disposed
public sealed class ResourceValue(string text) : IAssetStoreDisposable {
	private AssetStoreCustody? custody;

	public string Text {
		get {
			ObjectDisposedException.ThrowIf(Disposed, this);
			return field;
		}
	} = text;
	public bool Disposed { get; private set; }
	public int DisposeThreadId { get; private set; }
	public AssetStoreCustody? Custody => custody;

	public bool IsAssetStoreOwned => custody is { IsReleased: false };
	void IAssetStoreValue.AttachCustody(AssetStoreCustody custody) => this.custody = custody;

	public void Dispose() {
		custody?.ThrowIfHeld(this);
		if (Disposed)
			return;
		Disposed = true;
		DisposeThreadId = Environment.CurrentManagedThreadId;
	}
}

public sealed record MemoryDep(AssetId Id) : IAssetDep {
	public string DebugDescription => $"memory:{Id}";
}

public sealed class MemorySource : IAssetSource {
	private readonly ConcurrentDictionary<AssetId, byte[]> files = new();

	public void Set(AssetId id, string text) => files[id] = Encoding.UTF8.GetBytes(text);
	public void Set(AssetId id, byte[] bytes) => files[id] = bytes;

	public ValueTask<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDepCollector coll, CancellationToken ct = default) {
		if (!files.TryGetValue(info.AssetId, out byte[]? bytes))
			return ValueTask.FromResult(AssetSourceResult.NotHandled());
		coll.Add(new MemoryDep(info.AssetId));
		return ValueTask.FromResult(AssetSourceResult.Success(new MemoryStream(bytes)));
	}
}

// creates values from the asset's text; the text "fail" makes creation throw
public sealed class FuncCreator<T>(Func<string, T> make) : IAssetCreator<T> where T : class {
	public async ValueTask<AssetCreateResult<T>> TryCreateAsync(AssetCreateInfo info, IAssetDepCollector coll, CancellationToken ct = default) {
		// disposing the store's stream is allowed and does nothing
		await using Stream stream = await info.OpenAsync(ct).ConfigureAwait(false);
		using StreamReader sr = new(stream);
		string text = await sr.ReadToEndAsync(ct).ConfigureAwait(false);
		if (text == "fail")
			throw new InvalidDataException("asked to fail");
		return AssetCreateResult<T>.Success(make(text));
	}
}

// a creator from a lambda over the whole create info
public sealed class LambdaCreator<T>(Func<AssetCreateInfo, ValueTask<AssetCreateResult<T>>> create) : IAssetCreator<T> where T : class {
	public ValueTask<AssetCreateResult<T>> TryCreateAsync(AssetCreateInfo info, IAssetDepCollector coll, CancellationToken ct = default) => create(info);
}

public sealed class TestStore : IDisposable {
	public const string Ns = "test";
	public readonly AssetStore Store = new();
	public readonly MemorySource Source = new();

	public TestStore() {
		Store.RegisterSource(new OwnerOrderedEntry<IAssetSource>(Source, Ns, "memory"));
		Register(static text => new PlainValue(text));
		Register(static text => new ResourceValue(text));
	}

	public AssetStoreRegistration Register<T>(Func<string, T> make) where T : class =>
		Store.RegisterCreator(new OwnerOrderedEntry<IAssetCreator<T>>(new FuncCreator<T>(make), Ns, typeof(T).Name));

	public AssetStoreRegistration Register<T>(string localId, Func<AssetCreateInfo, ValueTask<AssetCreateResult<T>>> create) where T : class =>
		Store.RegisterCreator(new OwnerOrderedEntry<IAssetCreator<T>>(new LambdaCreator<T>(create), Ns, localId));

	public static AssetId Id(string path) => new(Ns, path);

	public Asset<T> Get<T>(string path, string text) where T : class {
		Source.Set(Id(path), text);
		return Store.GetAsset<T>(Id(path));
	}

	// replaces the asset's text and publishes the reload
	public void Reload<T>(Asset<T> asset, string text) where T : class {
		Source.Set(asset.AssetId, text);
		asset.QueueReload();
		Store.ApplyQueuedReloadsOrThrow();
	}

	public void Dispose() => Store.Dispose();
}

public static class ThreadUtil {
	// runs an action on a fresh thread and rethrows what it threw
	public static void OnNewThread(Action action) {
		Exception? caught = null;
		Thread t = new(() => {
			try {
				action();
			} catch (Exception ex) {
				caught = ex;
			}
		});
		t.Start();
		t.Join();
		if (caught is not null)
			throw new AggregateException(caught);
	}
}
