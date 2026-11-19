# Aisa: Features and Invariants

> **Purpose:** the living description of what Aisa does today, the rules that must not be broken, and how to extend it safely.
> **Last verified against the repo:** 2026-11-18, by reading the source (not by running the app).
> **Read in full for this document:** `IslandStateMachine`, `CharacterEngine`, `CharacterTypes`, `StateConfig`, `TrackedValue`, `NluPipeline`, `DeterministicParser`, `LmStudioClient`, `TimeExtractor` (first 70 lines), `SchedulerService`, scheduling models, `App.xaml.cs`, `ChatController`, `ChatModels`, `AlertCoordinator`, `OverlayWindow.xaml.cs`, `GlobalCursorMonitor`, `HitTestController`, plus the audio files covered in `ISSUES_AND_FIXES.md`.
> **Read in the follow-up review (same day):** `FocusController`, `PlacementService`, `TrayIcon`, `StartupRegistration`, `FrameLoop`, `WindowStyles`, `SettingsWindow`, `SettingsStore`/`AisaSettings`, `ChatView`, `AlertView`, `OverlayWindow.xaml`, `Aisa.Audio/Types.cs`, `CharacterRenderer` (first 90 lines), `tools/Aisa.CharacterPreview`.
> **Not read in detail (marked ⚠️):** `CharacterRenderer` drawing internals, `NativeMethods`, `app.manifest`, the markup of `ChatView.xaml` / `AlertView.xaml` / `SettingsWindow.xaml`, and the contents of the test files (only their names are listed).
> **Behavior source of truth:** `NOTCH_BEHAVIORAL_SPEC.md`. This file describes the code; if they disagree, the spec says what was *intended*.

---

## 1. Feature inventory

### 1.1 Island and window (`Aisa.Core/Island`, `Aisa.App/OverlayWindow*`)

| Feature | Behavior as coded | Where |
|---|---|---|
| Three modes | Closed (120x24, radius 10), Compact (288x32, radius 14), Expanded (640 wide, radius 22). Expanded height: greeting 150, alert 160, empty chat 160, chat with messages `min(300, 240 + 40 x messages)`. | `OverlayWindow.ShapeFor` |
| Three views | Greeting, Chat, Alert | `IslandView` |
| Peek | Pointer enters while Closed -> Compact. Pointer leaves Compact -> hides after `CompactHide` (10 s default). | `IslandStateMachine` |
| Face-hover open | In Compact, resting on the face for 0.5 s opens the island (if `OpenOnFaceHover`). Opens the Alert view if alerts exist. A hover-opened island folds 0.6 s after the pointer leaves, unless any interaction converts it to a normal open. | `FaceEntered`, `OnFaceOpenElapsed` |
| Click | Click on Compact opens (Alert if alerts, else Chat). Click inside Expanded counts as activity. A click on the face while Expanded is a slap instead. | `Click`, `OverlayWindow.OnIslandMouseDown` |
| Auto-close | Expanded folds to Compact after `AutoClose` (15 s) once the pointer is outside. Any key, click, typing or slap restarts the countdown. A thin countdown bar (160 DIP -> 0) shows during the last `min(10 s, 0.6 x delay)`. | `StartExpandedCollapse`, `UpdateCountdown` |
| Close | X button or Esc folds to Compact. On an alert it folds but stays pinned. | `CloseRequested` |
| Pinned alerts | An alert pins the island. Pinned means no timers run and `Hide()` is ignored. | `RaiseAlert`, `ShowAlert` |
| Greeting | At launch: Expanded on the Greeting view, face drops in (0.45 s), then Happy emote at 0.7 s, Wink at 1.4 s, Happy at 2.9 s, timeline ends at 4.6 s. Then folds after 0.6 s (10 s if hovered). Pointer leaving folds it immediately. Alerts that arrive during the greeting are held and raised at its end. | `StartGreeting`, `StepGreeting`, `GreetingFinished` |
| Animation split | Grow uses a damped spring (response 0.5, damping 0.72). Shrink uses a 0.34 s bezier (0.45, 0, 0.2, 1) with **no overshoot**. Content cross-fade: out 160 ms (fade + 0.97 scale), in after 160 ms over 300 ms with slight overshoot. | `Animate`, `ShowView`, `TrackedValue` |
| Face slots | The face glides between slots (island-relative DIP): Closed (-50, 12, size 6, hidden), Compact (-104, 16, 20), Greeting (0, 50, 44), Expanded (-264, 84, 44). | `MoveFaceToSlot` |
| OS window | Open window is 720x360 DIP; folded is (120 + 28) x (24 + 14). The window grows *before* the island animates and folds 420 ms *after* it closes. | `ResizeWindow`, `WindowFoldLag` |
| Placement | **Primary monitor only.** Anchor Left / Center / Right with `EdgeMargin` (the 14 DIP hit margin is subtracted so the visible edge sits exactly `EdgeMargin` from the corner). Left and Right anchor to the work area; Center uses the full monitor width. A top-docked taskbar pushes the window down (inset clamped to 200 DIP). DPI comes from `GetDpiForMonitor`. Re-evaluated every 0.5 s and on DPI/display changes; TOPMOST is re-asserted on each pass. | `ResizeWindow`, `PlacementService` |
| Keys | Esc closes. Ctrl +/-/0 zoom shortcuts are suppressed. Alt+F4 is blocked; only Quit closes the window. | `OnPreviewKeyDown`, `OnClosing` |
| Header buttons | Gear (opens Settings), speaker (toggles sound), X (close). | `OnGear`, `OnSpeaker`, `OnClose` |
| Pause | Tray "Pause" hides the overlay. Alarms still ring and bring the overlay back. | `App.SetPaused`, `OnIslandChanged` |

### 1.2 Character (`Aisa.Character`, `Aisa.Character.Rendering`)

| Feature | Behavior as coded |
|---|---|
| States (11) | Idle, Working, Thinking, Searching, Approval, Question, Error, Finished, RateLimited, Sleeping, Dizzy. Each has body color, accent, eye shape, badge, glow and motion flags (`StateConfig`). |
| Layering | Effective state priority: **Dizzy > Flash > Held override > Alert > Dialog > Idle**. `SetAlertState`, `SetDialogState`, `Flash(state, duration)`, `BeginOverride`/`EndOverride`. |
| Emotes (7) | Love (hearts, hop, blush), Surprised (hop, round eyes), Proud (stars), Wink, Yawn (stretch), Happy, Annoyed. |
| Eyes (12) | Pill, Wide, Flat, Happy, Tired, Closed, Spiral, Heart, Dot, Star, Wink, Annoyed. |
| Blinks | Every 2.2-5.4 s, 22% chance of a double blink. Disabled in Sleeping and Dizzy. |
| Gaze | `SetGaze(dx, dy)` (tanh-shaped, host calls it within 420 px). Thinking looks away; Sleeping looks down; Searching scans. |
| Hover love | Resting on the face while expanded: Hover sound, then after 1.9 s a Love emote. 6 s cooldown. Re-arms after moving more than 40 DIP. |
| Slaps | Click on the face while Expanded: squash, jelly wobble, Slap sound, Annoyed emote (0.8 s). **3 slaps within 1.7 s -> Dizzy for 3.3 s** (spiral eyes, roll, sway), then Happy emote. |
| Jelly | Spring on the face offset (stiffness 60, damping 5, 1/240 s sub-steps). Lives in `CharacterEngine`. |
| Particles | Hearts, stars, sparks, sweat (RateLimited), Z (Sleeping). |
| Sound requests | `SoundRequested` raises Hover, Love, Slap, Annoyed, Dizzy, Happy. `App` maps them to `SoundCue`. |
| Idle frames | `IsBusy` / `NextWakeIn` / `Invalidated` let the frame loop sleep when nothing moves. |
| Renderer | `CharacterRenderer.Draw` (body, sprout, blush, eyes, badge, particles) and `DrawHalo` (radial glow): procedural SkiaSharp with a squircle body. Paints and paths are cached statically, so it must be called from one render thread at a time. Drawing details were not reviewed line by line. |
| Interface | **There is no `ICharacterEngine`.** `CharacterEngine` is a concrete sealed class; the swap point is `CharacterPose` (renderer input). |

### 1.3 Chat and language understanding (`Aisa.App/Chat`, `Aisa.Nlu`)

| Feature | Behavior as coded |
|---|---|
| Turn flow | User message -> Send cue -> character override Thinking -> parse (at least **350 ms** of Thinking) -> response card + character outcome + sound. |
| Intents | SetAlarm, SetReminder, List, Cancel, Help, Greeting, Unknown. |
| Missing time | If an alarm or reminder has no time, the pending intent is kept, the assistant asks "When should I ring/remind you?", and the character holds the Question state until answered. |
| Cards | `ConfirmationCard` (Set / Edit), `UpcomingCard` (rows with remove), `CancelCard` (confirm). Tags on rows: missed, ringing, snoozed. |
| Cancel matching | "all/everything" -> all; alarm/reminder filters; label contains text; or matches the time of day parsed from the query ("7am alarm"). One live item with no query matches that item. |
| Chips | "alarm" prefills "Wake me at ", "remind" prefills "Remind me to ", "upcoming" sends "What's coming up?". |
| Deterministic parser | Cleans text, then greeting, pending-time answer, cancel, list/help, remind vs alarm detection, time extraction, label cleanup. Needs no network. |
| Time phrases | In N minutes/hours, half an hour, half past, quarter to/past, noon/midday/midnight, am/pm, HH:mm, o'clock, bare numbers after "at/for/by/@" or after alarm/day words, today/tomorrow/tonight, weekdays with next/this/on, parts of the day. |
| LM Studio use | Only when the parser returns Unknown, or the intent still needs a time but the text looks timed, or time words remain in the label. |
| LM Studio call | `POST {base}/chat/completions`, `json_schema` strict, temperature 0, current local time in the prompt. Result time must be in the future (checked in C#). Markdown-fenced JSON is tolerated. Connect timeout 1.5 s, request timeout 15 s, **30 s back-off** after any failure. |
| Sounds | Send, Finish (confirm/list/ask), Proud (saved), Error, Blip (help/greeting), Pop (removed). Unknown input also flashes the Error state for 1.2 s. |

### 1.4 Scheduler and alerts (`Aisa.Scheduling`, `Aisa.App/Alerts`)

| Feature | Behavior as coded |
|---|---|
| Item states | Scheduled, Ringing, Done, Missed. Kinds: Alarm, Reminder. Items carry snooze count. |
| Timing | Own loop; sleeps until the next due item, at most 30 s; `RecheckNow()` wakes it (after add/cancel/snooze, power resume, system time change). |
| Startup recovery | Items still Ringing, or overdue by more than 120 s (`Grace`), become Missed. Items overdue by 120 s or less ring immediately. |
| Adding | A time in the past is rejected ("That time has already passed"). |
| Alarm | Rings in the alert channel (`AlarmLoop`); character state Approval. Auto-snoozes after 5 min of ringing. |
| Reminder | Chime; character state Question and a Surprised emote on first alert; re-chime after 1 min; island folds (still pinned) after 5 min. |
| Missed | Title "Missed alarm/reminder - was due ..."; presented like a reminder (chime). |
| Queue | Oldest first; shows "+N more". Dismiss marks Done. Snooze reschedules (`SnoozeMinutes`, default 10). |
| Cleared | When the queue empties: alert sound stops, Happy emote, island unpins and folds. |
| Retention | Done items are kept 7 days. |
| Persistence | `schedule.json` via atomic write; corrupt file is quarantined (see `SETUP.md`). |
| Events | `Due`, `AutoSnoozed`, `Changed`, raised outside the lock and wrapped so a faulty handler cannot stop the loop. |

### 1.5 Platform and shell (`Aisa.App`)

| Feature | Behavior as coded |
|---|---|
| Overlay window | Borderless, per-pixel transparent, topmost, `WS_EX_TOOLWINDOW` + `WS_EX_NOACTIVATE`, starts `WS_EX_TRANSPARENT`. Not shown in Alt-Tab. Created with `ShowActivated=False`. |
| Click-through | The island rect (+14 DIP margin) is published in physical pixels; a cursor thread flips `WS_EX_TRANSPARENT` only when the inside/outside state changes. A press that began on the island keeps capture. |
| Cursor monitor | Thread "Aisa cursor monitor" (AboveNormal) using a high-resolution waitable timer: 16 ms near, 50 ms far, 250 ms while folded. Near = within 420 DIP of the window. Move events are coalesced; one final "far" event is sent. |
| Focus | The window is non-activating by default. When the chat view appears (`EnterTextInput`) it remembers the foreground window, clears `WS_EX_NOACTIVATE`, activates, and focuses the input after 120 ms. On exit (`ExitTextInput`) it clears focus, restores `WS_EX_NOACTIVATE` and returns foreground to the previous app. A hover-only open may not receive foreground (a Windows rule); the caret then appears on the first click. `WM_MOUSEACTIVATE` returns no-activate unless focus is accepted. | `FocusController`, `OverlayWindow.WndProc` |
| Tray | WinForms `NotifyIcon` with an icon drawn at runtime (no icon asset). Left-click opens. Menu: Open, Upcoming (opens and asks "What's coming up?"), Position (Left/Center/Right with a check mark; writes the setting), Settings..., Pause (check item), Quit. | `TrayIcon` |
| Settings | `SettingsWindow` applies every control change immediately through `SettingsStore.Update` -> `Changed` -> `App.ApplySettings`; values are clamped by `AisaSettings.Normalized()` (defaults in `SETUP.md`). Editable: anchor, edge margin, auto-close, compact hide, face-hover open, snooze, sound on/off, UI volume, alarm volume, LM Studio (on/off, URL, model), start with Windows. **`ToastFallback` has no control.** | `SettingsWindow`, `SettingsStore` |
| Start with Windows | Per-user Run key `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Aisa` = quoted exe path (no admin rights). Disabling deletes the value. Re-applied on every settings change; registry errors are swallowed. | `StartupRegistration` |
| Single instance | Named mutex `Local\Aisa.SingleInstance`; a second launch exits. |
| Power/time | Resume from sleep and system time changes call `RecheckNow()`. |
| Frame loop | `CompositionTarget.Rendering` (vsync), dt capped at 0.05 s, de-duplicated per frame. It unsubscribes when the tick reports nothing is moving (idle costs about 0% CPU); `Wake()` re-subscribes. | `FrameLoop` |
| Window styles | All `WS_EX_*` read-modify-write goes through one lock (`WindowStyles.Update`) because the cursor thread flips `TRANSPARENT` while the UI thread flips `NOACTIVATE`. | `WindowStyles` |
| Views | `OverlayWindow.xaml` hosts `ChatView`, `AlertView` and a greeting pane ("Hi, I'm Aisa") in a fixed 640 px content layer clipped by the island, so nothing reflows while it animates. The face, halo, particles and countdown bar live in a separate overlay layer that is not clipped and not hit-testable. Chat: Enter sends, chips prefill, and the message count drives island height. | `OverlayWindow.xaml`, `ChatView` |

### 1.6 Audio (`Aisa.Audio`)

See `SETUP.md` (sound workflow, cue list) and `ISSUES_AND_FIXES.md` (ISSUE-001). In short: WAV cues load from `%AppData%\Aisa\sounds`; one `WasapiOut` device (opened on demand, released after 1.5 s idle) mixes a UI bank and an alert bank with separate volumes; missing files are silent. Cue sets: 16 `SoundCue` values (Peek, Open, Close, Send, Finish, Blip, Error, Proud, Pop, Hover, Love, Slap, Annoyed, Dizzy, Happy, Greeting) and 2 `AlertTone` values (AlarmLoop, ReminderChime), defined in `Aisa.Audio/Types.cs`. Output is `WasapiOut` (Shared, 60 ms) fed through `SafeSampleToWaveProvider`.

**Which event plays which cue:** island Closed->Compact = Peek; ->Expanded (not Greeting) = Open; Expanded->Compact = Close; launch = Greeting; character events = Hover/Love/Slap/Annoyed/Dizzy/Happy; chat = Send/Finish/Proud/Error/Blip/Pop; alerts = `AlarmLoop`, `ReminderChime`. The island cues are triggered in `App.OnIslandChanged`.

### 1.7 Tests and tools

| Item | Notes |
|---|---|
| `Aisa.Core.Tests` | `IslandStateMachineTests` (fake clock) |
| `Aisa.Character.Tests` | `CharacterEngineTests`, `CharacterRendererTests` |
| `Aisa.Nlu.Tests` | `ParserTests`, `PipelineTests`, `LmStudioTests` |
| `Aisa.Scheduling.Tests` | `SchedulerTests` |
| `Aisa.Audio.Tests` | `SoundTests` (includes the ISSUE-001 regression test) |
| `tools/Aisa.CharacterPreview` | Renders 26 scenes (states, emotes, hover, slap and dizzy moments) plus a size ramp to PNG files and a labelled `contact-sheet.png`. The default output folder is the Linux path `/tmp/aisa-previews`; pass a folder argument on Windows. |

No automated tests cover `Aisa.App` (overlay, click-through, focus, placement). Those need the manual checklist in Section 6. Only the test file names are listed above; individual test names were not enumerated.

---

## 2. Invariants (rules that must not be broken)

Each rule says why. If you must change one, record it in `ISSUES_AND_FIXES.md` or `PROGRESS_LOG.md`.

1. **Audio buffers:** never use `Array.Clear`, `Array.Fill`, `Buffer.BlockCopy`, `Array.Copy` or `buffer.Length` on the `buffer` parameter of an `ISampleProvider.Read`. Keep `SafeSampleToWaveProvider`. *Why:* NAudio can pass a `byte[]` aliased as `float[]`. See ISSUE-001.
2. **Sounds come from `%AppData%\Aisa\sounds` only,** and missing or invalid files stay silent. *Why:* there is deliberately no synthesized fallback.
3. **The island machine and character engine are headless and single-threaded, owned by the UI thread.** They never touch WPF types. *Why:* testability with a fake clock.
4. **Set the SynchronizationContext before constructing `IslandStateMachine`** (`App.OnStartup` does this). *Why:* its timers post back through the context captured at construction; without one they would fire on thread-pool threads.
5. **Click-through is owned by the cursor thread via `HitTestController`.** Touch `WS_EX_TRANSPARENT` only when the state flips; keep press-capture. *Why:* a busy UI thread must never delay a click passing through, and drags must not fall through mid-gesture.
6. **The window must never steal focus.** Keep `WS_EX_NOACTIVATE`, the `WM_MOUSEACTIVATE` handling and `ShowActivated=False`. Take keyboard focus only through `FocusController` while the chat input is active.
7. **The hit rect is computed analytically** from tracked values and published in physical pixels, not read from layout. *Why:* layout for the current frame has not run yet.
8. **Shrinking uses the timed bezier with no overshoot; growing uses the spring.** *Why:* spec behavior; the island must never bounce while closing.
9. **The OS window grows before the island animates and shrinks 420 ms after it closes.** *Why:* otherwise the animation is clipped.
10. **Re-assert TOPMOST on every placement pass** (0.5 s timer). *Why:* other topmost windows can bury the overlay.
11. **Alt+F4 must not close the app;** only Quit does.
12. **The frame loop sleeps when idle.** The character reports `IsBusy`/`NextWakeIn`; do not add always-on animation. The engine's timers use real elapsed time while animation uses a capped dt (`MaxDt` 0.05).
13. **Chat Thinking lasts at least 350 ms.**
14. **LM Studio never blocks the UI and never decides alone:** connect 1.5 s, request 15 s, back-off 30 s, and any returned time is validated in C# (must be in the future).
15. **Alerts must always be able to ring:** pause hides the overlay but a due alert re-shows it. `App` swallows UI dispatcher exceptions so a glitch cannot kill alarms (see Section 5).
16. **Persisted files are written atomically** (temp file then `File.Move` overwrite); a broken `settings.json` or `schedule.json` must never stop the app from starting.
17. **Scheduler events are raised outside the lock** and wrapped in a try/catch. *Why:* a faulty subscriber must not stall or crash the loop.
18. **Call `GreetingFinished()` when the greeting ends.** Alerts raised during the greeting are held until then.
19. **Character state priority is Dizzy > Flash > Held > Alert > Dialog > Idle.** Add new layers deliberately.
20. **Every `CharacterState` needs a row in `StateConfig`.** The table is built by enum index, so a missing row leaves a null entry.
21. **Headless libraries target plain `net10.0` and stay free of WPF.** Windows-specific code lives in `Aisa.App`.
22. **Single instance** via the named mutex.
23. **The character art and its original sounds are for personal builds only;** keep the engine/renderer split so they can be replaced.
24. **The transparent hit margin is painted at alpha 1/255** (`#01000000` on `IslandHost` in `OverlayWindow.xaml`). *Why:* a layered window lets fully transparent pixels fall through even when input is enabled, so without it a cursor grazing the island edge would reach the app underneath.
25. **All `WS_EX_*` changes go through `WindowStyles.Update`.** *Why:* the cursor thread and UI thread both modify the extended style; unsynchronized read-modify-write could silently undo the other's change.
26. **The face canvas and overlay layer are not hit-testable.** Clicks on the face are detected by distance in `OnIslandMouseDown` using `FaceCenterDip()`; keep that in sync with `PlaceFaceCanvas`.
27. **The content layer stays a fixed 640 px wide and is clipped by the island.** *Why:* nothing may reflow while the island animates.
28. **`CharacterRenderer` is not thread-safe** (static cached paints and paths); call it from one render thread only.
29. **Change settings only through `SettingsStore.Update`** (normalizes and clamps values, saves atomically, raises `Changed`).

---

## 3. Key flows

**Launch:** single-instance check -> load settings -> create `SoundService`, `CharacterEngine`, `IslandStateMachine`, `SchedulerService`, `LmStudioClient`, `NluPipeline`, `ChatController`, `AlertCoordinator` -> show `OverlayWindow` -> tray -> apply settings -> `StartGreeting()` + Greeting cue -> start the scheduler (held alerts wait for the greeting) -> `alerts.Refresh()`.

**Chat turn:** `ChatController.SendAsync` -> `NluPipeline.ParseAsync` (parser, then LM Studio only if needed) -> `Handle` -> card, character reaction, sound. Saving an alarm calls `SchedulerService.AddAsync` and plays the Finished flash and Proud emote.

**Alert:** scheduler `Due` -> `AlertCoordinator.Refresh` -> `IslandStateMachine.RaiseAlert` (pins, shows Alert) + character state + alert-channel sound -> Dismiss/Snooze -> `Refresh` -> when empty, `AlertsCleared` (unpin, fold).

---

## 4. Extension recipes

**Add a sound cue:** add the value to `SoundCue` (`Aisa.Audio/Types.cs`); add `<Name>.wav` to `assets/sounds`; add the name to the cue list in `install.ps1`; run `install.ps1`; restart; trigger it from the right event (`OnIslandChanged`, `ChatController`, `CharacterSound` mapping in `App.Map`). Update `SETUP.md`.

**Add an emote:** add to `Emote`; handle it in `PlayEmote` (hop/particles) and the eye mapping in `ComposePose`; add an `EyeShape` and renderer support if the eyes are new; add a test in `CharacterEngineTests`.

**Add a character state:** add to `CharacterState`; **add its row in `StateConfig.BuildTable`**; handle entry effects in `Refresh` if needed; decide which layer sets it (alert, dialog, flash or override).

**Add an intent:** add to `IntentKind`; extend `DeterministicParser`; extend the LM Studio schema enum, `ToWire` and `ParseResponse`; handle it in `ChatController.Handle`; add parser, pipeline and LM Studio tests.

**Add an island view:** add to `IslandView`; add its size in `OverlayWindow.ShapeFor`; add a pane in XAML and wire it in `ShowView`; update focus handling in `ApplySnapshot`; add state-machine tests.

**Add a setting:** add the field to `AisaSettings`; add the UI in `SettingsWindow`; apply it in `App.ApplySettings`; document the default in `SETUP.md`.

---

## 5. Known limitations, observations and backlog

**Resolved items from `BUILD_PLAN.md` Section 4:** the jelly spring is in `CharacterEngine`; there is no `ICharacterEngine` interface; the toast fallback is not implemented.

- **Toast fallback is not implemented:** `AisaSettings.ToastFallback` exists, but no code reads it and `SettingsWindow` has no control for it. Either implement it or remove the setting.
- **Launch sound sequence (observation):** at the end of the greeting the island folds Expanded -> Compact, which plays the **Close** cue, and no Peek cue plays. Confirm this is intended.
- **Exceptions on the UI thread are swallowed** (`DispatcherUnhandledException` handled) to protect alarms. This hides bugs; consider logging them.
- **Stereo WAVs** are averaged to mono by the loader, which can dull wide sounds. Current files are mono.
- **`SoundSynth`** is dead at runtime (tests only). Remove it or keep it deliberately.
- **`release\`** is a build artifact and can be stale.
- **LM Studio live settings:** `ApplySettings` updates enabled, URL and model on the running client; timeouts are fixed in code.
- **No automated tests for `Aisa.App`.**
- **Primary monitor only:** placement ignores other monitors.
- **Hover-only open and typing:** Windows may refuse to give the app foreground without a recent click, so the caret can appear only after the first click.
- **`CharacterPreview` defaults to a Linux path** and DejaVu font; pass an output folder on Windows.

---

## 6. Manual test checklist (Windows)

Run after any change to the overlay, platform, audio or animation code.

1. **Launch:** the greeting plays (drop, Happy, Wink, Happy), then folds; no focus is stolen from the app you were using.
2. **Click-through:** clicks beside the island reach the window underneath; clicks on the island do not; dragging from the island never falls through.
3. **Focus:** typing in the chat works; closing returns focus to the previous app.
4. **Peek and open:** hover the notch (peek), rest on the face (opens after about 0.5 s), click (opens), Esc and X (close), auto-close after 15 s.
5. **Motion:** growing springs, closing is firm with no bounce, content cross-fades, countdown bar shrinks in the last seconds.
6. **Character:** eye tracking, blinks, hover love after about 2 s, three quick slaps -> Dizzy -> Happy.
7. **Chat:** "wake me at 7", "remind me to call mom in 20 minutes", a message without a time (asks when), "what's coming up?", "cancel the 7am alarm", gibberish (error flash).
8. **Alerts:** alarm rings and pins; Snooze and Dismiss work; a reminder re-chimes after 1 min and folds after 5 min; an alert still appears while Paused.
9. **Sleep/time:** after resume from sleep, due items fire; changing the system clock is picked up.
10. **DPI and placement:** 100/150/200% scaling, Left/Center/Right anchors, top-docked taskbar. The overlay always uses the primary monitor; confirm that is acceptable if you use several.
11. **Audio:** every cue plays cleanly and ends when the file ends (no buzzing tail). See ISSUE-001.
12. **LM Studio:** with the server running, an unusual phrasing is understood; with it stopped, the app falls back within about 1.5 s and does not retry for 30 s.
