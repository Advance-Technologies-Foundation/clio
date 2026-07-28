# clio-ring AOT desktop spike — report

**Machine:** K-KRYLOV-NB · **Runtime:** .NET 10.0.301 · **RID:** win-x64
**UI stack:** Avalonia 12.1.0 + CommunityToolkit.Mvvm 8.4.2
**Branch:** `spike/aot-claude`
**Date:** 2026-07-11

> Scope note (per coordinator redirect): this pass delivers the **11 → 12.1.0 migration**,
> the **ring wiring + visual polish**, and **headless screenshots**. The full **NativeAOT
> measurement pass (cold-start / hotkey-latency / RSS gates) is DEFERRED** — see
> [Deferred](#deferred-next-pass). Warnings→0 was achieved earlier on the 11.3.11 AOT baseline;
> a fresh AOT publish on 12.1.0 is part of the deferred pass.

---

## 1. Screenshots (headless RenderTargetBitmap)

Rendered offscreen via `tools/ScreenshotTool` — Avalonia headless platform with **real Skia
drawing** (`UseHeadlessDrawing = false`). Each frame is rendered to a **scaled**
`RenderTargetBitmap` (true DPI scaling at 96·s) and **composited over an opaque dark matte
(`#0B0F14`)** so PNGs are viewer-independent (fixes the earlier "mostly black" report). No
visible window, no blocking.

| State | 1x | 1.5x |
|---|---|---|
| Default (compact, ring only) | `measurements/screenshots/default@1x.png` | `default@1.5x.png` |
| Keyboard-focused node | `focused@1x.png` | `focused@1.5x.png` |
| Running (drawer open, RUNNING badge) | `running@1x.png` | `running@1.5x.png` |
| Success (SUCCESS badge, green node) | `success@1x.png` | `success@1.5x.png` |
| Failure (FAILED badge, red node) | `failure@1x.png` | `failure@1.5x.png` |
| Destructive-confirm dialog | `destructive-confirm@1x.png` | — |

All under `measurements/screenshots/`. Regenerate:

```cmd
dotnet run --project tools/ScreenshotTool/ScreenshotTool.csproj -c Debug -- measurements/screenshots
```

### Visual rework — item-by-item (design review)

1. **Compact default** — `SizeToContent=Height`; the window is a near-square ring-only launcher
   when idle. Output is a **bottom-sheet drawer** that expands only after a run and collapses via
   a chevron (`CollapseOutputCommand`). No idle empty panel, no ghost Cancel.
2. **Environments ≠ actions** — two concentric orbits with stable positions: **inner = environments**,
   **outer = actions**. Separate radii + independent layout passes.
3. **No overlap** — radii 82 (inner) / 150 (outer), nodes 54/58, wide negative space; action labels
   sit *outward* on a 196 radius. No intersecting circles.
4. **One icon family** — stroke-based (outline) `StreamGeometry` icons (`RingIcons`): info, tag,
   package, docs, folder, restart + a single globe glyph for every environment. Accent/colour
   encodes **state**, never per-icon.
5. **Typography** — one concise single-line label per node (env name inside the node; action label
   outward, ellipsized). No ragged multi-line wrap. Inter throughout.
6. **Streaming bottom sheet** — monospaced output, auto-scrolls to latest, command header
   (`clio get-info -e ve`), explicit end-state badge **RUNNING / SUCCESS / FAILED / CANCELED**
   (colour + label), collapse affordance; Cancel shows only while running.
7. **Interaction states** — hover (subtle elevation), keyboard-focus (strong accent ring, `Focusable`
   + Enter/Space activation), pressed (scale-down), running/success/failure (node colour + the one
   reserved glow), destructive-confirm dialog overlay, and a `ReducedMotion` flag (disables entrance
   stagger + pulse). Focused/running/success/failure/destructive are screenshot-verified.
8. **Re-rendered** — the five required states at **1x and 1.5x** (10 PNGs) plus destructive-confirm,
   via the scaled-RTB + matte path above.

**Aesthetic refinements:** outline (not filled/duotone) icons; **one** translucent surface — nodes
are distinguished by subtle elevation/shadow + state, with the strong accent glow **reserved for the
focused/active node only**; quiet resting state (motion/focus create the energy); and **stable
geometry across all states** — the ring's centre/radius/node coordinates never move; the drawer
expands into reserved space below and the confirm dialog is an overlay, so nothing reflows the ring.

---

## 2. Build health on Avalonia 12.1.0 (JIT)

`dotnet build ClioRing.sln -c Release` → **Build succeeded, 0 Warning(s), 0 Error(s)**
(Debug likewise clean). Verified for all three projects: `ClioRing`,
`ClioRing.Desktop`, `tools/ScreenshotTool`.

---

## 3. Resolved package versions

Exact-pinned in `Directory.Packages.props` via `[x]` version ranges.

| Package | Requested | Resolved |
|---|---|---|
| Avalonia | `[12.1.0]` | 12.1.0 |
| Avalonia.Desktop | `[12.1.0]` | 12.1.0 |
| Avalonia.Themes.Fluent | `[12.1.0]` | 12.1.0 |
| Avalonia.Fonts.Inter | `[12.1.0]` | 12.1.0 |
| Avalonia.Skia (transitive/tool) | `[12.1.0]` | 12.1.0 |
| Avalonia.Headless (tool only) | `[12.1.0]` | 12.1.0 |
| CommunityToolkit.Mvvm | `[8.4.2]` | 8.4.2 |
| Microsoft.Extensions.DependencyInjection | `[10.0.0]` | 10.0.0 |

Notable transitive: **`Tmds.DBus.Protocol` 0.21.2 → 0.94.1**. On the 11.3.11 baseline the 0.21.2
pull triggered the **NU1903** high-severity advisory (GHSA-xrw6-gwf8-vvr9); the 12.x train pulls
the patched 0.94.1, so the migration **incidentally resolves NU1903**. (It is a Linux/DBus
transitive and is not loaded on win-x64 regardless.)

`Avalonia.Diagnostics` is **no longer referenced** — see migration deltas.

---

## 4. 11 → 12 migration deltas

Four breaking changes surfaced and were resolved to reach a clean build:

1. **`Avalonia.Diagnostics` discontinued (12.x).** No 12.x release exists (max 11.3.18). It was
   DevTools-only and already excluded from Release. **Action:** dropped the package reference
   from both projects and `Directory.Packages.props`.
2. **`TransformOperations` namespace moved** from `Avalonia.Media` →
   `Avalonia.Media.Transformation`. **Action:** updated the `using` in `RingView.axaml.cs`.
3. **`BoxShadow` is a `Border` property, not `Button`.** The style setter `Button.ring { BoxShadow }`
   failed (`AVLN2000`). **Action:** ring nodes are now `Border` cells with a `PointerPressed`
   handler invoking the item's `SelectCommand` (Border supports `BoxShadow`, so the glow-on-focus
   works).
4. **`Window.SystemDecorations` obsolete** (`AVLN5001`) → replaced with `WindowDecorations="None"`
   (enum `Avalonia.Controls.WindowDecorations`).

No CommunityToolkit.Mvvm 8.4.0 → 8.4.2 source-generator changes required.

---

## 5. What is implemented (delivered)

- **Warnings→0 (AOT, 11.3.11 baseline):** explicit compiled `ViewLocator` map (was `Type.GetType`
  reflection, IL2057); source-generated `RingJsonContext` for all config (was reflection
  `JsonSerializer`, IL2026/IL3050); removed `DisableAvaloniaDataAnnotationValidation`
  (BindingPlugins.DataValidators, IL2026 ×2). Baseline `aot-publish.log` (5 distinct IL*) →
  `aot-publish-item1.log` (0 IL*). **Re-verify on 12.1.0 is part of the deferred AOT pass.**
- **Closed-envelope action contract:** `ActionCatalog`/`RingAction` with `ActionKind`-tagged
  typed blocks (`ClioCommand`/`OpenUrl`/`OpenPath`), `ParameterDescriptor`, `Risk`; no
  `object`/`JsonElement`/type-name discriminators; whole graph in `RingJsonContext`;
  `ActionCatalogLoader` validates kind↔block match and throws `ActionCatalogException`
  (never a partial object). Sample `actions.json` ships beside the exe.
- **clio adapter (`IClioAdapter`/`ClioAdapter`, DI-registered):** async child process, event-driven
  `BeginOutput/ErrorReadLine` (UI thread never blocks), per-line callback + `OutputReceived`
  event with `Stopwatch` timestamps, raw stdout/stderr + exit code preserved, cancellation kills
  the whole tree (`Process.Kill(entireProcessTree: true)`). `ListEnvironmentsAsync` parses
  `clio show-web-app-list` JSON via AOT-safe `JsonDocument`.
- **Ring UI:** frameless translucent `RingWindow` is the resident, DI-built main window. Radial
  nodes (actions + discovered environments). Global hotkey **Ctrl+Alt+Space** via P/Invoke
  `RegisterHotKey` + a WndProc subclass using an **`[UnmanagedCallersOnly]` static thunk**
  (AOT-safe, no runtime delegate marshalling). Selecting an environment streams
  `get-info -e <env>`; **Cancel** cancels + kills the tree. Falls back to `clio --version` when no
  environment is registered.
- **Instrumentation (wired, not yet exercised):** `Metrics` stamps process-start (top of `Main`,
  QPC), hotkey-received (native thunk), first-paint & interactive (`TopLevel.RequestAnimationFrame`).
  Harness modes `--bench-hotkey N`, `--auto-clio`, `--exit-after-paint` drive automated samples via
  synthetic `WM_HOTKEY` posts. CSV output to `measurements/{startup,hotkey}.csv`, tagged with
  machine/runtime/rid/build-mode.

---

## 6. Acceptance gates status

| # | Gate | Status |
|---|---|---|
| 1 | hotkey→interactive <100 ms p95 | **DEFERRED** — instrumentation + `--bench-hotkey` harness in place, not yet run |
| 2 | cold→first-paint <300 ms p95 | **DEFERRED** — stamps + `--exit-after-paint` loop hook in place, not yet run |
| 3 | idle RSS <60 MB (+peak during clio) | **DEFERRED** — `--auto-clio` hook for peak sampling in place, not yet run |
| 4 | no UI-thread block; Cancel kills process tree | **IMPLEMENTED** — event-driven streaming + `Kill(entireProcessTree:true)`; end-to-end timing deferred |
| 5 | clean AOT publish, 0 IL warnings | **ACHIEVED on 11.3.11**; **re-verify on 12.1.0 deferred** |

---

## <a name="deferred-next-pass"></a>7. Deferred (next pass)

Per coordinator, the AOT measurement pass is out of scope for this cycle:

1. Fresh NativeAOT publish on **12.1.0** via `measurements/publish-aot.cmd` (vcvars bootstrap);
   re-confirm 0 IL warnings on the new Avalonia train.
2. Run gates 1–3: `--exit-after-paint` cold loop (≥10), `--bench-hotkey` warm samples (≥30),
   idle RSS (60 s stabilization) + peak WS during a clio call. Report p50/p95.
3. Confirm NU1903 fully clears on the AOT publish output (expected — 0.94.1) and that
   `Tmds.DBus.Protocol` is absent from `publish/win-x64`.

---

## 8. Reproducible publish command (for the deferred AOT pass)

```cmd
:: from repo root — publishes NativeAOT self-contained win-x64
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
set "PATH=%PATH%;C:\Program Files (x86)\Microsoft Visual Studio\Installer"
dotnet publish ClioRing.Desktop\ClioRing.Desktop.csproj -c Release -r win-x64 ^
  --self-contained true -p:PublishAot=true -o publish\win-x64
```

(Scripted as `measurements/publish-aot.cmd`. A bare `dotnet publish` from a plain shell fails the
native link — `vswhere.exe not recognized`, link exit 123 — so the vcvars bootstrap is required.)
