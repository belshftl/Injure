# assets/overview.md

The problem with a typical simple asset system is that once you've read and decoded an asset, the resulting object is completely detached from the original file/whatever. It could have come from anywhere, and now you just have a bunch of loose texture/sound/font/etc objects that got spread around the entire game, and now you want hot reload or a mod wants to replace it with its own version or something like that, and the entire thing falls apart.

The Injure asset system represents and manages logical asset objects, so an asset object genuinely is just a representation of An Asset somewhere instead of the result of decoding the current contents of a file or data blob. Here's an example:
```csharp
string UIDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "ui");
Assets.RegisterSource("mygame", new DirectoryAssetSource("mygame.ui", UIDirectory), "UIDirectory");
AssetRef<Texture2D> button = Assets.GetAsset<Texture2D>(new AssetID("mygame.ui", "tex/button.png"));

// a lot of engine APIs accept these directly:
cv.Texture(button, Vector2.Zero);

// if you want to get whatever value the asset currently holds, you borrow it:
AssetLease<Texture2D> l = button.Borrow();
cv.Texture(l.Value, Vector2.Zero);

// then hot reload is as simple as:
await button.QueueReloadAsync();
```

Design goals include:
- first-class hot reload
- use by multiple owners (game + mods) and safe multithreaded/concurrent use
- extensibility with custom asset types and pipeline components
- ergonomic usage, especially in common cases

Current limitations (all of these are planned to be fixed before a stable release):
- API mostly not as volatile anymore but still prerelease quality and not stabilized
- there is no good way to replace assets yet, something like owner-ordered slot replacements for `AssetRef`s are planned
- path behavior on Windows hasn't been tested yet
- `EngineResourceStore`, etc may need a rename and also being a bit less barebones

---

## Getting started

Assets are managed by an `AssetStore`. Typically, the way you get one is by specifying `Assets: true` in your `ServiceConfig` at game startup, and use `GameServices.Assets`. You can get one yourself by making a `new AssetStore()` and attaching on the main thread, but more on that later.

Assets have IDs, and an asset ID consists of a namespace and a path. An asset ID might look something like `new AssetID("mygame.ui", "tex/button.png")`; this one is for the asset `tex/button.png` in the namespace `mygame.ui`. Asset IDs have a canonical string form `namespace::path`, so this one is `mygame.ui::tex/button.png` when converted to a string.

An asset is represented by an `AssetRef<T>` object, for example `AssetRef<Font>`. You get these like:
```csharp
AssetRef<Font> somefont = Assets.GetAsset<Font>(new AssetID("mygame.ui", "fnt/somefont.ttf"));
```
`AssetRef<T>` is a reference type with normal reference type equality, and `GetAsset<T>()` may return the same one multiple times or may return different ones for different asset IDs; compare `AssetRef<T>.AssetID`s if you want "are these the same asset".

Many engine APIs accept `AssetRef<T>` directly, so you don't have to think about this much, but if you need to get a snapshot of the current underlying value, you borrow it:
```csharp
AssetLease<Font> l = somefont.Borrow();
Font f = l.Value; // don't cache this; more on this later
```
`AssetLease<T>` is a ref struct. It contains:
- `T Value`: the current asset value
- `ulong Version`: a number that monotonically increments with each new asset value. Starts at 1; 0 is an invalid asset version.
- `ReadOnlySpan<IAssetDependency> Dependencies`: dependencies collected while making the asset

`GetAsset<T>()` doesn't actually make the first version; `Borrow()` blocks to make it if there isn't one. You can also make the first version ahead of time with `WarmAsync()` or its blocking non-async wrapper `Warm()`.

For borrowing without creating a first version if there isn't one, there's `TryPassiveBorrow`:
```csharp
AssetRef<Font> somefont = Assets.GetAsset<Font>(new AssetID("mygame.ui", "fnt/somefont.ttf"));
await somefont.WarmAsync();
if (!somefont.TryPassiveBorrow(out AssetLease<Font> l))
    throw new InvalidOperationException("was expecting first asset version to be made");
// use l here ...
```

`TryPassiveBorrow` is guaranteed to succeed after a successful warm. Since the manual `throw` may get cumbersome, there's also a throwing counterpart, `PassiveBorrow`:
```csharp
AssetRef<Font> somefont = Assets.GetAsset<Font>(new AssetID("mygame.ui", "fnt/somefont.ttf"));
await somefont.WarmAsync();
AssetLease<Font> l = somefont.PassiveBorrow();
```

## Hot reload

Hot reload is two-step; you can't directly reload a singular asset, you can only queue up a reload that then gets applied later.

You can make a new version and queue it up to replace the existing one with:
```csharp
await somefont.QueueReloadAsync();
```
If you aren't in an async method, you can use `QueueReload()` instead, which is a blocking non-async wrapper.

Normally, the engine will apply reloads for you between ticker scheduler ticks. If you wanna apply manually, use the `ApplyQueuedReloads()` method on `AssetStore`.

Normally, when an `ApplyQueuedReloads` happens, old `Value`s of all live `AssetLease`s become invalid. All built-in types implement `IRevokable`, so trying to use them past that point will throw. This is all fine in a singlethreaded scenario, but if you have multiple threads, it'd be hard to safely use assets if the main thread can hit the automatic `ApplyQueuedReloads` at any time. The solution:
```csharp
AssetThreadContext ctx = Assets.AttachCurrentThread();

AssetLease<Font> l = somefont.Borrow();
// use l.Value here ...
ctx.AtSafeBoundary();
// l.Value may be invalid now, stop using it ...

ctx.Dispose(); // undoes the attach; you typically may want to use something like `using`, this is just to demonstrate
```
Old lease values will not be invalidated until every attached thread has called `AtSafeBoundary`. So, in other words, call it whenever you're okay with old lease values being invalidated. It is your duty as a well-behaved attached thread to periodically call that, as not doing it may build up old asset versions in memory. You don't strictly need to attach on your thread to use the asset system, but it's pretty much the only way to do it safely in a multithreaded scenario.

The engine automatically attaches the main thread and reports safe boundaries on it; if you're not doing multithreaded use of the asset system, you don't really have to think about this.

Note that, importantly, you can only attach on a real thread (like, a physical OS thread, not a `Task`); your code may resume on a different OS thread after crossing an `await`, so TLS/etc breaks. You can spawn a proper physical thread using `System.Threading.Thread`.

Aside from `QueueReloadAsync`, you can also register dependency watchers that automatically queue reloads when the underlying resource (file/etc) changes. There are no built-in watcher types yet, so this section is a bit barebones, sorry. There will be a built-in file dependency watcher soon.

## Bulk operations

There's a non-generic interface `IUntypedAssetRef` that `AssetRef<T>` implements. It has most of the operations that `AssetRef<T>` has, with the one big difference being that you can't actually borrow it. The purpose of it is making collections of `AssetRef<T>`s of different `T`s.

And the purpose of that is bulk warm/reload. If you have a bunch of `IUntypedAssetRef`s (more specifically, an `IEnumerable<IUntypedAssetRef>`), you can warm them in parallel with the extension methods `WarmAllAsync()`/`QueueReloadAllAsync()` or the blocking non-async wrappers `WarmAll()`/`QueueReloadAll()`. These take in `int maxConcurrency` for how at most many operations should be done at the same time; the default is currently `8`.

If you have a bunch of assets you need to warm ahead of time, it's recommended to use these instead of sequentially warming them with `WarmAsync()`/`Warm()`, since that way you actually get the benefit of async.

## Asset creation

Asset creation is three-step:
- A source is something that can take an asset ID and provide a `Stream` for it. For example, there may be a source for files, a source for HTTP resources on some server, a source for embedded resources in the assembly, a source for a C# dictionary, etc.
- A resolver is something that can take a `Stream` and produce a data blob object if it recognizes the format. It may also fetch other metadata on the side, for example reading a JSON and fetching the file specified in it and creating the asset from that.
- A creator is something that can take in a data blob object and produce the finished asset object. Creators may also be staged, where creation is split into prepare/finalize, but more on that in the dedicated "making a custom asset type" document.

Each stage can return `NotHandled` to let the next registered handler try. If it falls through every single one, it's treated as an asset creation failure.

Each stage can also add additional `IAssetDependency` objects to the total dependency set for that asset, which is then usable by dependency watchers and also exposed as the `Dependencies` you get in `AssetLease<T>`.
