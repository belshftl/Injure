# conventions/dangerous-get-create.md

This document describes `DangerousGet*` and `DangerousCreate*` members: what makes a member "dangerous", what the caller takes on by using one, and what an API author has to do when adding one.

---

## What "dangerous" means

Injure's APIs enforce invariants: an owning object releases its resource exactly once, a revoked asset becomes unusable, a `HostTick` only ever comes from a clock (and usually the same clock throughout the whole program), etc. A member prefixed with `Dangerous` lets the caller bypass such an invariant. Calling one means taking on a responsibility that the API normally handles itself.

"Dangerous" does not mean memory-unsafe, and it is unrelated to the `unsafe` keyword. `GpuDevice.WriteToBuffer(GpuBufferHandle buffer, ulong offset, void* data, nuint size)` takes a pointer and needs an `unsafe` context, but it is not dangerous: its pointer only has to be valid for the duration of the call, which is the ordinary contract of any pointer parameter. Conversely, `Texture2d.DangerousGetGpuTexture()` involves no pointers at all, but it is dangerous, because the returned texture keeps working after the `Texture2d` it came from has been revoked.

These members exist because the alternative is worse. Being able to reach the native WebGPU object behind a `GpuBuffer`, or the GPU resources behind a `Texture2d`, is sometimes necessary, and hiding them in the name of safety only pushes people to fish them out of private fields with reflection, which ends up not buying any extra safety and breaking on every internal refactor. So they are public, under a name that says plainly that the caller has to know what they're doing.

---

## When a member is "dangerous"

A member gets the prefix if, and only if, it lets the caller break an invariant that Injure otherwise enforces. In practice, that happens in one of these ways:

- **Outliving a lifetime.** The member hands out something that stays usable, or at least reachable, after the lifetime it belongs to has ended.
  - `Gpu*Handle.DangerousGetNative()` returns the native WebGPU object, which dangles once the owning wrapper is disposed. Nothing stops the caller from releasing it through the bindings either, even if it was obtained through a non-owning `*Ref`.
  - `GpuBuffer.DangerousGetMappedPointer()` returns a pointer into mapped memory, which dangles once the buffer is unmapped or disposed.
  - `Texture2d.DangerousGetBindGroup()` returns a bind group that keeps working after the texture has been revoked, which defeats the point of revocation.
- **Unverifiable input.** The member accepts a value that Injure can't check, and trusts the caller that it is valid.
  - `DangerousCreateFromRaw(ulong ns)` trusts that the raw value comes from the same clock as every other `HostTick` in the program. A value from any other clock creates a bogus value that's, on the surface, indistinguishable from a correct one.
  - `SurfaceSource.DangerousCreateFrom*` trusts that the native handles are valid and stay valid while a surface uses them; only null handles are rejected.
  - `SdlWindow.DangerousCreateFromProperties(SdlContext context, uint props)` trusts the caller with raw SDL window creation properties, including setting the ones Injure relies on.
- **Bypassing engine processing.** The member gives access to data before, or instead of, the processing Injure would normally do on it.
  - `SdlEventSource.DangerousGetNextRaw()` removes an SDL event from the queue before Injure translates it. The caller becomes responsible for passing every such event to `DangerousCreateHostEvent(in SDLEvent ev, out HostEvent result)` exactly once, in order to keep things such as Injure's gamepad tracking up to date.
- **Foreign types.** The member exposes a type from a dependency, such as the WebGPU or SDL bindings, as a return value or parameter. This is never the only reason for the prefix: a foreign type always comes with one of the reasons above, typically an unmanaged lifetime. It does, however, affect stability; see [Stability](#stability).

Members that are merely low-level are not dangerous as long as Injure still enforces invariants. `GpuBuffer.ReadMapped<T>(ulong offset, Span<T> dst)` and `GpuBuffer.WriteMapped<T>(ulong offset, ReadOnlySpan<T> src)` access mapped memory too, but they check the mapping state, mode, and bounds on every call, so they are ordinary members. `DangerousGetMappedPointer()`, on the other hand, has no such checks, and exists for advanced zero-copy usage.

---

## Naming

- `DangerousGet*` for members that hand a value out, e.g. `DangerousGetNative()`, `DangerousGetRaw()`.
- `DangerousCreate*` for members that build an Injure value from something the caller hands in. Static factories usually take the form `DangerousCreateFromSomeSource`, e.g. `DangerousCreateFromRaw`, `DangerousCreateFromMetalLayer`. Instance members that convert a foreign value into an Injure one use `DangerousCreateSomeResult`, e.g. `SdlEventSource.DangerousCreateHostEvent(in SDLEvent, out HostEvent)`.

---

## The caller's contract

When using a `DangerousGet*` member, the caller must not:

- release, destroy, or otherwise end the lifetime of the returned value, unless the member's docs say otherwise;
- use the returned value after the lifetime it belongs to has ended (the owner is disposed, the asset is revoked, the buffer is unmapped, the device is lost, etc.);
- change state that Injure tracks through the returned value, e.g. reconfiguring a WebGPU surface behind a `SurfaceRenderOutput`'s back;
- use the returned value from threads other than the ones the owning object allows; a dangerous member doesn't change an object's thread-safety rules.

When using a `DangerousCreate*` member, the caller must make sure the input is valid, and stays valid for as long as the member's docs require. Injure checks only what's easy and cheap to check; for instance, it can (and typically will) check if a pointer is null, but can't check that it's a live `wl_surface` handle with a sufficient lifetime.

In return, Injure promises only that a `DangerousGet*` member returns the current value at the time of the call, with the semantics its docs describe.

**If the contract is violated, nothing is promised.** The consequences may range from simply using a stale asset version to dereferencing a dangling pointer. The program may throw, misbehave, or abort in native code. It can be thought of as similar to UB. In particular, Injure does not promise that misuse can't corrupt managed state.

---

## Stability

A dangerous member whose signature only uses Injure and BCL types, such as `HostTick.DangerousGetRaw()`, is as stable as any other member; only what it lets the caller do is different.

A dangerous member whose signature includes a foreign type is **not** stable API: its return or parameter type may change without notice, for example when a dependency is replaced or updated, or when internals are reorganized. Such members say so in their docs (see below).

---

## Documentation requirements

Every dangerous member's doc comment has:

- a statement of what it bypasses, typically in the `<summary>`;
- a statement of when its result becomes invalid (for `DangerousGet*`) or what the input must satisfy (for `DangerousCreate*`), including which of those requirements Injure checks;
- for members with foreign types in their signature, a statement that the type is not a stable API;
- a reference to this document.

For example:

```csharp
/// <summary>
/// Returns the underlying <see cref="WGPUBuffer"/>, bypassing ownership/lifetime. Dangles once
/// freed by <see cref="GpuBuffer.Dispose()"/>.
/// </summary>
/// <remarks>
/// <b>The return type is not a stable API and may change without notice.</b> See
/// <c>docs/conventions/dangerous-get-create.md</c>.
/// </remarks>
public WGPUBuffer DangerousGetNative() { /* ... */ }
```

---

## For Injure devs: tooling

Dangerous members bypass the "don't expose non-BCL/Injure types in public API" rule: the return type of a `DangerousGet*` method and the parameter types of a `DangerousCreate*` method may be foreign.

The analyzer only looks at the name, so the prefix has to be honest. Don't add it to a member just to silence `IJDEV0100`; if a member exposes a foreign type without bypassing anything, the foreign type should be wrapped instead.

---

## For Injure devs: when not to add one

Dangerous members are escape hatches, not the primary way to do something. If there's a routine need for one, that's a sign that some safe API is missing and should be devised. For example, `GpuBuffer.ReadMapped<T>(ulong offset, Span<T> dst)` and `GpuBuffer.WriteMapped<T>(ulong offset, ReadOnlySpan<T> src)` cover the common uses of mapped memory, leaving `DangerousGetMappedPointer()` for the cases where copying isn't acceptable.
