# smoke/

One-off programs that were meant to test the end-to-end process of writing a game (or other program) that uses some combination of Injure's features, to find friction and rough edges, and make sure certain usecases are possible and the APIs work well for them. They aren't demos / samples, and aren't kept polished / commented.

## AvaloniaEmbed

Tests two ways to embed Injure in an Avalonia window:
- a `NativeControlHost` whose native view is rendered to through `SurfaceSource`, `SurfaceRenderOutput`, and `RenderFrame`
- a custom `IRenderOutput` that renders into an offscreen and copies every frame to a `WriteableBitmap` via readback.

The former is the recommended way to embed into an Avalonia window; the latter is a proof-of-concept.

Windows support is currently unimplemented.

Run it with `dotnet run` from its directory; `--title <text>` sets the window title. For unattended runs, set `INJURE_SMOKE_SECONDS` to close the window after that many seconds (resizing it halfway, and detaching and reattaching the native view along the way) and print frame counts.

## WaylandEmbed

Avalonia doesn't have a native Wayland backend, so there's a separate test to embed a native child surface the way a UI framework would, by making a `wl_subsurface` to present into.

Linux + Wayland only. `--title` and `INJURE_SMOKE_SECONDS` are supported, as in AvaloniaEmbed; the latter destroys and recreates the subsurface rather than detaching the native view.
