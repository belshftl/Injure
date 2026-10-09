# conventions/reserved-names.md

This document describes name parts that carry a fixed meaning in Injure. A name containing one of them makes a promise about the type or member, so they must not be used for anything else, and code that matches one of the meanings should use the corresponding name.

---

## `Ctx` names

`Ctx` is short for "context" (see [Shortened names](names-and-doc-comments.md#shortened-names)), and is meant to be pronounced "context". The long form `Context` is never used unless it's following an external convention, such as with `InjureJsonContext` or `IlGenericContext` (both internal types).

A type with the `Ctx` suffix is handed to a callback, hook, or lifecycle phase, and is only valid for that one invocation or phase. It carries the inputs of the call and the operations that are allowed during it. Code receiving one must not keep it around after the invocation or phase ends.

That restriction must be enforced, ideally at compile time, as per "strictness is better". In practice, it is enforced either:

- **By ref struct rules,** e.g. `IlCtx<L>` (scoped to one IL transaction) and `LayerTickCtx` (scoped to one layer update); or
- **By a Roslyn analyzer + at runtime,**  by marking the object with the internal `[DontCache]` (and optionally `[DontCaptureIntoClosure]`) attributes and making it throw at runtime on use after expiry; e.g. the mod lifecycle contexts (`IModLoadCtx`, `IModLinkCtx`, `IModActivateCtx`, `IModReloadCtx`).

Using a `ref struct` is the preferred option if how the type is used allows it.

---

## `Untyped` names

The `Untyped` prefix (`IUntyped*` for interfaces, `Untyped*` otherwise) has two meanings:

- **A non-generic view of a generic type.** An `IUntyped*` interface exposes the parts of a generic type that don't depend on its type arguments, so that instances with different type arguments can be handled together, e.g. stored in one collection. Example: `IUntypedAssetRef`, implemented by every `AssetRef<T>`.
- **A variant of a normally generic type without its type parameters.** This is rare and mostly appears in internal mod-related code, when a lifetime identity type can't be named statically. Example: `UntypedModExportTable`, an internal type of the mod runtime. It stores a mod's exports without needing the lifetime identity at compile time, and is converted into the generic form with reflection before being handed to a mod.

---

## `Dangerous` names

The `DangerousGet*` and `DangerousCreate*` prefixes mark members that let the caller bypass an invariant Injure otherwise enforces. Their meaning, naming, caller contract, and documentation requirements are described in [`dangerous-get-create.md`](dangerous-get-create.md).

---

## `*Handle` and `*Ref` names

Resource wrappers that come in owning and non-owning forms use a group of three types:

- `XHandle`: an abstract base class for both forms. APIs that only need to use the resource take an `XHandle`, so that they accept either form.
- `X`: the owning form. It is `IDisposable`, and disposing it ends the resource's lifetime. `AsRef()` creates a non-owning view of it.
- `XRef`: the non-owning form. It can't end the resource's lifetime, and it stops being usable once its owning object is disposed (or, for owning objects with other ways of ending their lifetime, such as `GpuCommandEncoder.Finish()`, once that happens).

Examples: `GpuBufferHandle`/`GpuBuffer`/`GpuBufferRef`, `GpuTextureHandle`/`GpuTexture`/`GpuTextureRef`, and `GpuCommandEncoderHandle`/`GpuCommandEncoder`/`GpuCommandEncoderRef`.

Not every owning/non-owning pair follows this pattern. If the non-owning form must not be retained at all, it is a `ref struct` rather than an `XRef` class, and since a `ref struct` can't share a base class with the owning form, there is no `XHandle` either. There is currently no standardized naming pattern for such pairs; they are currently named such that the more commonly used variant is the "default" one (e.g. `Canvas` vs `OwnedCanvas`).
