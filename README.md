A **heavily unfinished** C# game framework/engine focused on first-class mod support.

---

## Current project status

*Very* unfinished. You may be able to make something kind of resembling a game right now, but it'll also probably stop compiling within a few commits, or run into a roadblock because an API for some basic feature hasn't been added yet.

Rough estimate on how much the individual components are finished right now ("completion" is less about "definitively feature-complete" and more about "confident this could ship in a stable release"):

| Subsystem                | Rough status to "completion"  | Pending redesign? | Notes                                                                                                                                                                                                                                                                                           |
| ------------------------ | ----------------------------- | ----------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Asset system             | ~55% `[###########---------]` | No                | Works on paper, but currently quite inconvenient to use in a real game; also missing proper asset overriding by mods.                                                                                                                                                                           |
| Audio engine             | ~10% `[##------------------]` | No                | Playing a simple audio stream works, and that's kind of it. None of the actual useful features are implemented yet. Also needs more than 1 backend, and preferably an ASIO one at some point as a side package.                                                                                 |
| High-level draw system   | ~35% `[#######-------------]` | **Yes**           | Missing most non-basic functionality and meaningful lower-level integration. Needs better-defined ownership. The text renderer needs quite a bit more work. The pixel converter is fine but missing a lot of vectorized kernels.                                                                |
| Input system             | ~40% `[########------------]` | No                | Undocumented mess right now, missing basic features like custom input sources and lower-level integration/driving.                                                                                                                                                                              |
| Layer system             | ~50% `[##########----------]` | No                | Undocumented and kind of magic-y. Also missing the planned ECS system.                                                                                                                                                                                                                          |
| Mod loader               | ~35% `[#######-------------]` | **Minor**         | All the critical stuff like what "reloadable" means and the dependency semantics everything hinges on is completely undocumented. Not battle-tested to any capacity yet, and fails to consider basic cases like NuGet dependencies for mods. Also maybe use typestates for `ModRuntime` phases. |
| Detouring/patching APIs  | ~60% `[############--------]` | No                | Probably the most in-good-shape of this whole list, because it was made more recently, when I understood good API design better. It's going in the right direction, just missing features: IL mutation/deletion, fuzzy/get-operand matching, prefix support, detouring open generics, etc.      |
| Low-level render system  | ~50% `[##########----------]` | **Yes**           | Yet another undocumented one. Currently mired in various little ad hoc constructs/types and tangled with engine internals. Also currently has had zero consideration for 3D at all, and the `ISurfaceHost`/`IRenderOutput`/`RenderFrame` design is broken.                                      |
| OS/window host systems   | ~30% `[######--------------]` | **Ongoing**       | Currently mid-redesign from an old one that was scrapped because it was monolithic and heavily relied on internals rather than being constructed from public API pieces.                                                                                                                        |
| Coroutine system         | ~70% `[##############------]` | No; maybe minor   | The primary problem is just that it's undocumented (and untested). Otherwise it's mostly fine right now and doesn't really have any obvious feature holes.                                                                                                                                      |
| Ticker system            | ~50% `[##########----------]` | **Yes**           | The concept's there, but it needs a more rigorous scheduling/priority/deadline model. ...Also needs documentation and tests. And becoming drivable by the game directly.                                                                                                                        |
| Post-compile weaver tool | ~95% `[###################-]` | No                | Can't think of anything else to really add to it. It'd be workable as a first stable release, just needs to come with conveniences to wire it into MSBuild.                                                                                                                                     |

This section is planned to be cut from the README before a stable release, to be replaced with a "get started" / "usage" / etc. section.

---

## Design philosophy

- **Stricter is better.** APIs should not silently paper over your mistakes; rather, they should fail clearly and, preferably, in an actionable way.
- **APIs are about guarantees, not code.** Most of an API's usefulness comes from the properties you can rely on rather than the types or the signatures of the callable methods. Do not be afraid to make strong guarantees, and if something should be unspecified, explicitly document it as unspecified.
- **The type system is your friend.** Need to enforce usage in a particular order of operations or disallow illegal states? Use a typestate. Need to enforce validity of inputs? Use a type with a smart constructor. Need the caller to not cache a temporary context object? Use a C# `ref struct`. Far from all illegalities are preventable by the type system, but more of them are than you think.
- **A good Roslyn analyzer/sourcegen is invaluable.** Roslyn analyzers / source generators are one of the most powerful features in C# and allow plenty of common mistakes to be caught at compile time, boilerplate-y tasks to become significantly more ergonomic, and some of C#'s limitations to be worked around.
- **If you don't provide a good official way to do it, someone will just figure out a worse one.** Accessing internals with reflection, patching engine methods, you name it. It's not that every case must be covered, obviously, as things are oftentimes intentionally unsupported / out-of-scope and an API can't be one-size-fits-all, but this is a useful concept to keep in mind.
- **Your API is ultimately just an abstraction.** What happens under the hood is oftentimes useful information to the user, not merely an implementation detail. You are an abstraction, and should make it clear what you abstract over and how. Providing good low-level escape hatches will eventually save someone a lot of pain with reflection / runtime patching.
- **Backwards compatibility is important, but not stagnating is more important.** Once a stable release is out, the versioning scheme is planned to be semver, but a new major release will not be a development milestone. If some past design decision ends up having been a bad idea, there will be no hesitation to just rework it in a new major release rather than contort everything around it. That said, it's unlikely major stable releases will be frequent or regular, since bad designs are meant to not get out of the prerelease stage.

As of right now, most of the APIs/codebase doesn't follow these very well as they were designed a while ago; that is a problem currently in progress to being fixed.

---

<TODO: most of the readme. As of right now, I just wanted a place to write down a basic progress tracker and the design philosophy.>

---

## Misc.

#### "Your design philosophy mostly sounds like Rust/Swift/whatever is what you want. Why C#?"

C# has extremely powerful reflection / runtime patching capabilities that most other languages just don't even compare to, an execution model that abstracts away host-architecture differences, garbage collection making complex intertwined game worlds manageable in a tidy way, good support for generics and polymorphism, powerful async capabilities, and generally a long history of gamedev. The upsides outweigh the downsides (lack of clear object ownership / "consume a value", every type being forced to have a sentinel value, badly defined `unsafe` boundaries, legacy background, etc.) by an order of magnitude.

#### "Why the name"?

It was meant to be part of a larger software lineup, but none of the other software in said lineup currently exists, so it ends up just not making much sense. It also typographically looks nice, and mirrors the frustration with XNA-and-friends this was born of.
