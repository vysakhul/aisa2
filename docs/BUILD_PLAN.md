# Aisa / Notch Assistant: Initial Build Plan

> **Status: historical snapshot.** This is the plan the project started from. **Do not edit it to describe new work.**
> Current state lives in `FEATURES_AND_INVARIANTS.md`; what changed on which day lives in `PROGRESS_LOG.md`.
>
> **Origin:** drafted with Claude on another portal, targeting .NET 8. During actual code generation the project moved to **.NET 10**, so this copy is updated to .NET 10 and Section 4 records every other place where the build departed from the plan.
>
> **Last verified against the repo:** 2026-11-18. Verified: `global.json`, all `.csproj` files, folder layout. **Not** re-verified here: runtime behavior (see `NOTCH_BEHAVIORAL_SPEC.md` acceptance checks).
>
> **Companion document:** `NOTCH_BEHAVIORAL_SPEC.md` defines *what* the product does. This file defines *how it was meant to be built*.

**Naming note.** The spec and this plan say "Notch". The code and solution say **Aisa** (`Aisa.sln`, `Aisa.*` projects, `Aisa.exe`, `%AppData%\Aisa`). Treat "Notch" as the UI concept and "Aisa" as the product/code name. Plan names map as: `Notch.Core` -> `Aisa.Core` (plus split-out projects, see Section 4), `Notch.App` -> `Aisa.App`.

---

## 1. Tech stack (as planned)

**Use WPF on .NET 10 (planned as .NET 8), not WinUI 3.** The spec's hardest requirements are see-through pixels, click-through that switches on and off, and a window that never takes focus.

- **WPF:** setting `AllowsTransparency="True"` gives a "layered" window. Windows already draws per-pixel transparency for these, and clicks pass through fully transparent pixels. Toggling the extended style `WS_EX_TRANSPARENT` makes the whole window click-through.
- **WinUI 3:** it has no supported fully transparent top-level window. It would need composition workarounds, extra click-through handling and packaging overhead, with nothing gained in return.
- **Cost:** a layered window's redraw cost grows with its size, but 720x360 at 60 fps is small. The render loop also stops when nothing is moving.

**Draw the character with SkiaSharp (`SKElement`); draw everything else in WPF.**

- Win2D only works with WinUI/UWP, so it is out.
- WPF's own drawing API has no additive blending for particles, and its blur effects are expensive.
- Skia covers everything the character needs: gradients, additive particles, blur for the glow, and the 12 eye shapes.
- The character canvas is about 130x110 at 2x resolution, so CPU drawing is cheap. Cards, chat and text stay in normal WPF.

**Supporting pieces (as planned; several changed, see Section 4):**

| Concern | Planned choice |
|---|---|
| App wiring | Microsoft.Extensions.Hosting (DI), CommunityToolkit.Mvvm (view models) |
| Sound | NAudio, two outputs so alarms have their own channel and volume |
| Tray | H.NotifyIcon.Wpf |
| Time parsing | Microsoft.Recognizers.Text.DateTime for the deterministic parser |
| LM Studio | Plain `HttpClient` against LM Studio's OpenAI-compatible endpoint |
| Alarm toast fallback | CommunityToolkit notifications package |

---

## 2. Architecture (as planned)

```
Notch.Core      (no WPF; unit-testable with FakeTimeProvider)
  Animation/    TrackedValue, Spring2D (jelly), CubicBezier
  Island/       IslandStateMachine: 5 timers via TimeProvider, pinned + hover-opened flags
  Character/    ICharacterEngine -> CharacterPose (immutable: color, eyes, badge, squash, roll, particles)
  Scheduling/   SchedulerService, JsonScheduleStore (write temp file, then File.Move overwrite)
  Nlu/          DeterministicParser -> LmStudioParser -> NluPipeline
Notch.Platform  Win32: Placement, GlobalCursorMonitor, HitTest, Focus, Tray, SoundService
Notch.App       OverlayWindow, FrameLoop, views, MochiRenderer (Skia), composition root
```

### Which thread owns what

- **UI thread:** the island state machine and the character engine. Both are headless and single-threaded. The engine advances once per frame from the frame loop and reports when it is "busy" so the loop can sleep.
- **Cursor thread:** samples the cursor and flips click-through itself, so a busy UI thread never delays a click passing through. It sends coalesced Entered / Left / Moved events to the UI. Moved drives eye tracking.
- **Scheduler:** runs its own loop. It waits until the next due item, but at most 30 s, and re-checks after the PC resumes from sleep. It pushes full list snapshots to the UI and sends `AlertDue` to the island state machine, which holds alerts during the greeting.
- **NLU:** runs as async tasks off the UI thread. The parse is wrapped in `Task.WhenAll(parse, Task.Delay(350))` so "Thinking" always lasts at least 350 ms.

### LM Studio rules

- Connect timeout 1.5 s (`SocketsHttpHandler.ConnectTimeout`); answer timeout 15 s via a cancellation token.
- After a failure, skip LM Studio for 30 s.
- Call `POST http://localhost:1234/v1/chat/completions` with `response_format: { type: "json_schema", ... }` and `temperature 0`.
- Put the current local time in the prompt, and validate the returned time in C# before trusting it.

### Swappable character

`ICharacterEngine` produces a `CharacterPose`, and a separate renderer draws it. This keeps the Mochi art replaceable for a distributed build, as the spec's licensing note requires.

---

## 3. Build order (as planned)

1. **Overlay window** (the starter). Check: clicks outside the black shape reach the app underneath, clicks on it never steal focus, and hit zones line up at 100/150/200% scaling for left/center/right placement and with a top-docked taskbar. (Spec acceptance checks 2 and 6.)
2. **Motion.** Record at 60 fps and compare side by side with the demo GIF. Then add the content cross-fade, the countdown bar and the 420 ms window shrink. (Check 1.)
3. **Headless island state machine.** Unit-test every timer and the pinned rules with a fake clock, then replace the starter's temporary mode switching with it.
4. **Character engine and Skia renderer.** Order: eye tracking and blinks, gliding between mode slots, emotes, slap and jelly wobble, dizzy, hover-love. (Checks 3 and 4.)
5. **Chat view and deterministic parser,** including the focus switching and the 240 + 40 px-per-message panel growth.
6. **Scheduler, alert card and sound alert channel.** (Check 5.)
7. **LM Studio fallback.**
8. **The rest:** greeting scene, tray, settings, start with Windows, toast fallback.

---

## 4. Planned vs as-built

Legend: OK = matches plan. CHANGED = deliberately or incidentally different. CHECK = could not be confirmed from project files alone; confirm in `FEATURES_AND_INVARIANTS.md`.

### 4.1 Runtime and projects

| Area | Plan | As built | Status |
|---|---|---|---|
| .NET version | .NET 8 | **.NET 10.** `global.json` pins SDK `10.0.100` with `rollForward: latestFeature`. App targets `net10.0-windows10.0.19041.0`; libraries target `net10.0`. | CHANGED |
| Project layout | `Notch.Core`, `Notch.Platform`, `Notch.App` | `Aisa.Core`, `Aisa.Scheduling`, `Aisa.Nlu`, `Aisa.Audio`, `Aisa.Character`, `Aisa.Character.Rendering`, `Aisa.App`, plus per-module test projects and `tools/Aisa.CharacterPreview` | CHANGED (finer split; no `Platform` project) |
| Win32 platform code | `Notch.Platform` | `Aisa.App/Platform` (FocusController, GlobalCursorMonitor, HitTestController, PlacementService) and `Aisa.App/Interop` (NativeMethods, WindowStyles) | CHANGED |
| Scheduling | inside Core | own project `Aisa.Scheduling` (SchedulerService, Models, Stores) | CHANGED |
| NLU | inside Core | own project `Aisa.Nlu` (DeterministicParser, TimeExtractor, LmStudioClient, NluPipeline, Models) | CHANGED |
| Character engine | inside Core (`Character/`) | own project `Aisa.Character` (CharacterEngine, CharacterTypes, StateConfig) | CHANGED |
| Skia renderer | `MochiRenderer` in `Notch.App` | `Aisa.Character.Rendering/CharacterRenderer.cs`, own project, SkiaSharp 3.119.4. Keeps the character art swappable. | CHANGED |
| Settings | not in plan | `Aisa.Core/Settings/AisaSettings.cs` and `Aisa.App/Shell/SettingsWindow` | added |

### 4.2 Packages and libraries

| Concern | Plan | As built | Status |
|---|---|---|---|
| DI / hosting | Microsoft.Extensions.Hosting | Not referenced by any project. Wiring lives in `App.xaml.cs`. | CHANGED |
| MVVM | CommunityToolkit.Mvvm | Not referenced. UI is driven by controllers (`ChatController`, `AlertCoordinator`) and code-behind views. | CHANGED |
| Skia in WPF | SkiaSharp (`SKElement`) | `SkiaSharp.Views.WPF` 3.119.4 in the App, `SkiaSharp` 3.119.4 in the renderer project | OK |
| Tray icon | H.NotifyIcon.Wpf | **WinForms `NotifyIcon`** (`Shell/TrayIcon.cs`). `UseWindowsForms` is enabled in the App csproj *only* for this, and the WinForms implicit usings are removed to avoid clashes with WPF types. | CHANGED |
| Time parsing | Microsoft.Recognizers.Text.DateTime | Hand-written `TimeExtractor.cs` and `DeterministicParser.cs`; no recognizers package | CHANGED |
| LM Studio client | plain `HttpClient` | `LmStudioClient.cs` in `Aisa.Nlu`; no extra package | OK |
| Toast fallback | CommunityToolkit notifications | No toast package referenced. The `ToastFallback` setting exists but is **not implemented** (verified 2026-11-18). | CHANGED (not built) |
| Sound | NAudio, **two outputs** | `Aisa.Audio` with NAudio.Core and NAudio.Wasapi 2.2.1. **One output device** with two mixed banks (UI and alert) that have separate volumes. Alerts are still on their own channel and volume, just not their own device. | CHANGED |

### 4.3 Code-level details

| Area | Plan | As built | Status |
|---|---|---|---|
| Easing and springs | `TrackedValue`, `Spring2D`, `CubicBezier` as separate items | `Aisa.Core/Animation/TrackedValue.cs` holds both `CubicBezier` (with the `Shrink` preset 0.45, 0, 0.2, 1) and `TrackedValue`, which runs either a damped spring (response 0.5, damping 0.72, 1/240 s sub-steps) or a timed bezier. The 2D jelly spring lives in `Aisa.Character/CharacterEngine.cs` (stiffness 60, damping 5). | OK (resolved 2026-11-18) |
| Schedule store | temp file then `File.Move` overwrite | Implemented as planned. Also: versioned JSON envelope (`Version = 1`), and an unreadable file is renamed to `<name>.corrupt-<timestamp>` instead of being lost. An `InMemoryScheduleStore` exists for tests. | OK (plus extras) |
| Character interface | `ICharacterEngine` -> `CharacterPose` | No interface: `CharacterEngine` is a concrete sealed class that exposes a `CharacterPose`, and the renderer takes the pose. The art stays swappable at the pose boundary. | CHANGED (verified 2026-11-18) |
| Sound location | in `Platform` | `Aisa.Audio` project, loading WAVs from `%AppData%\Aisa\sounds` (copied from `assets\sounds` by `install.ps1`) | CHANGED |

### 4.4 Extras not in the plan

- Unit-test projects for Audio, Character, Core, Nlu and Scheduling.
- `tools/Aisa.CharacterPreview` for previewing the character.
- `assets/sounds` with generated baseline WAVs and `install.ps1`.
- `Directory.Build.props` (latest C#, nullable, implicit usings, deterministic builds).
- A self-contained `release\` build folder (a build artifact, rebuilt on demand; may be stale).

---

## 5. Build-order status

"Code present" means the files for that step exist in the repo. It does **not** mean the spec's acceptance checks were re-run.

| Step | Plan item | Evidence in repo | Status |
|---|---|---|---|
| 1 | Overlay window | `OverlayWindow`, `Platform/*`, `Interop/*`, `app.manifest` | Code present |
| 2 | Motion | `TrackedValue.cs`, `Rendering/FrameLoop.cs` | Code present |
| 3 | Island state machine | `Aisa.Core/Island/IslandStateMachine.cs` | Code present |
| 4 | Character engine and renderer | `Aisa.Character`, `Aisa.Character.Rendering` | Code present |
| 5 | Chat view and parser | `Views/ChatView`, `Chat/*`, `DeterministicParser`, `TimeExtractor` | Code present |
| 6 | Scheduler, alert card, sound channel | `Aisa.Scheduling`, `Views/AlertView`, `Alerts/AlertCoordinator`, `Aisa.Audio` | Code present |
| 7 | LM Studio fallback | `LmStudioClient.cs`, `NluPipeline.cs` | Code present |
| 8 | Greeting, tray, settings, start with Windows, toast fallback | `Shell/TrayIcon.cs`, `Shell/SettingsWindow`, `Shell/StartupRegistration.cs`; greeting is driven by `OverlayWindow.StartGreeting` and the island state machine; **toast fallback is not implemented** | Present except toast fallback |

---

## 6. How to use this document

- Use it to understand **why the architecture looks the way it does** (thread ownership, why Skia, why WPF, why the character is swappable).
- Do not use it as the source of truth for what exists today; use `FEATURES_AND_INVARIANTS.md`.
- If you change the architecture in a way that contradicts this plan, record the new reality in `FEATURES_AND_INVARIANTS.md` and the date in `PROGRESS_LOG.md`. Add a row to Section 4 here only if the departure is significant enough that someone comparing plan to code would be confused.
