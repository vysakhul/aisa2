# Aisa: Progress Log

> **Purpose:** one entry per working day, so any model or developer can see what was done, when, and what is still open.
> **Order:** newest entry first. Dates are ISO (YYYY-MM-DD).
> **Last updated:** 2026-11-18.

## How to add an entry (every model, every session)

At the end of each working session, add or extend **that day's entry** at the top of the "Daily entries" section. Do not rewrite older entries. Use this shape:

```
### YYYY-MM-DD: one-line theme
**Models/tools:** who did the work (e.g. Claude, Gemini via Antigravity)
**Summary:** 2-4 sentences
**Changes:** grouped by area, with file names
**Docs:** documentation files created or updated
**Verification:** tests run, manual checks done, and what was NOT checked
**Issues:** links to entries in ISSUES_AND_FIXES.md
**Open items / next:** what is unfinished or should happen next
```

Rules of thumb: be specific about files, say plainly what was **not** verified, and if a problem took more than one attempt to solve, add it to `ISSUES_AND_FIXES.md` and link it here.

## Milestones

| Date | Milestone |
|---|---|
| 2026-11-16 | Initial plan decided |
| 2026-11-17 | Initial build day |
| 2026-11-18 | First audio bug fixed (ISSUE-001); project documentation started |

---

## Daily entries

### 2026-11-18: Audio buzz fixed; project documentation started

**Models/tools:** Claude (review, recording analysis, documentation); Gemini via Antigravity (root-cause diagnosis and fix of the audio bug, and the rebuild). The application itself is built with Claude; Gemini was used only for this issue.

**Summary:** Investigated why UI sound cues sounded altered inside the app although the WAV files were fine. Analysis of a screen recording showed each cue playing correctly for ~60 ms and then looping a frozen 10 ms chunk until the audio device was released. A first attempt (switching the output API) did not help; Gemini then found the real cause: `Array.Clear` on a buffer that NAudio aliases as `float[]` over a `byte[]`. Fixed, and confirmed by listening. Started the project's documentation set.

**Changes:**
- **Audio fix (see ISSUE-001):**
  - `src/Aisa.Audio/SoundService.cs`: `RootProvider.Read` now zeroes its output with a typed `for` loop instead of `Array.Clear`.
  - `src/Aisa.Audio/Types.cs`: added `SafeSampleToWaveProvider` (real `float[]` scratch buffer plus `Buffer.BlockCopy`); `WasapiOutput` wraps its source with it. `WasapiOut` (Shared, 60 ms) is the default output again.
  - `tests/Aisa.Audio.Tests/SoundTests.cs`: added `ReusedWaveBufferSilencesAfterCueEnds`.
  - `src/Aisa.Audio/Aisa.Audio.csproj`: `NAudio.WinMM` was added during a failed workaround (`WaveOutEvent`) and then removed. Net change: none.
  - Rebuilt the `Release` output and the self-contained `release\` build (reported by Gemini).
- **No other runtime code was changed today.**

**Docs (all in `docs/`):**
- Created: `BUILD_PLAN.md`, `SETUP.md`, `ISSUES_AND_FIXES.md`, `PROGRESS_LOG.md` (this file), `README.md` (documentation index), `FEATURES_AND_INVARIANTS.md` (written from a source read, then reviewed against the remaining platform, shell and view files the same day).
- Updated after the review: `SETUP.md` (`ToastFallback` is not implemented; also corrected a mistake that said `release\` was not git-ignored, it is) and `BUILD_PLAN.md` (the three "CHECK" items resolved).
- Already present before today's documentation work: `NOTCH_BEHAVIORAL_SPEC.md` (the behavior spec the project started from).
- Still to do: a detailed read of the few files marked as not read in `FEATURES_AND_INVARIANTS.md` (`CharacterRenderer` drawing internals, `NativeMethods`, `app.manifest`, the XAML markup of the chat, alert and settings views) and an enumeration of individual test names.

**Verification:**
- Manual: owner tested hover over the notch, click, and opening the assistant after the rebuild; no sound problems.
- Not done: a new screen recording and signal analysis; a recorded `dotnet test` result (Gemini added the test and rebuilt, but a test run result was not captured in this log); behavior of any non-audio feature (not touched today).

**Issues:** ISSUE-001 (UI sound cues play as a frozen 100 Hz buzz): fixed.

**Open items / next:**
- Decide on the **toast fallback**: implement it (setting, UI control and an alert path) or remove the unused `ToastFallback` setting.
- Confirm that the Close cue playing when the greeting folds (and no Peek cue) is intended.
- Resolved during the documentation review: the 2D jelly spring lives in `CharacterEngine`; there is no `ICharacterEngine` interface; the toast fallback is **not implemented** (the setting exists, but nothing reads it and Settings has no control for it).
- Optional: add short `// See docs/ISSUES_AND_FIXES.md ISSUE-001` pointers next to the audio fix in code.
- Run the manual acceptance checks from `NOTCH_BEHAVIORAL_SPEC.md` on Windows (click-through, focus, DPI at 100/150/200%, animation feel) and record the results here.

---

### 2026-11-17: Initial build day

**Models/tools:** Claude (generated the code from the owner's instructions).

**Summary:** The first working version of Aisa was built following `BUILD_PLAN.md` and `NOTCH_BEHAVIORAL_SPEC.md`. The project moved from the planned .NET 8 to **.NET 10** during code generation.

> This entry is **reconstructed from the repo as it stood at the start of 2026-11-18**, not from a log written on the day. The exact order of work within the day was not recorded.

**What existed by the end of the build (from the repo):**
- Solution `Aisa.sln` with `src/Aisa.App` (WPF overlay, click-through, cursor monitor, views, tray, settings, start with Windows), `Aisa.Core` (island state machine, spring/curve animation, settings store), `Aisa.Character` and `Aisa.Character.Rendering` (character engine and SkiaSharp renderer), `Aisa.Scheduling` (alarms/reminders with JSON persistence), `Aisa.Nlu` (deterministic parser, time extractor, LM Studio fallback), `Aisa.Audio` (cue playback through NAudio with a separate alert channel).
- Test projects for Audio, Character, Core, Nlu and Scheduling.
- `tools/Aisa.CharacterPreview` (renders every character state to PNG).
- `assets/sounds` with WAV cues and `install.ps1`; runtime sounds load from `%AppData%\Aisa\sounds`.
- A self-contained `release\` build, `README.md`, `Directory.Build.props`, `global.json` (SDK 10.0.100).
- Departures from the plan (different packages, finer project split, one audio device with two banks) are listed in `BUILD_PLAN.md` Section 4.

**Sound history (date of each step not recorded):** cues were first generated by a built-in synthesizer (`SoundSynth`, now used only by tests); the app was later changed to play WAV files, and the owner added higher-quality WAVs to `assets/sounds`.

**Verification:** not recorded for this day. Acceptance checks from the spec were not logged.

**Open items carried into 2026-11-18:** sounds played incorrectly inside the app (became ISSUE-001).

---

### 2026-11-16: Initial plan decided

**Models/tools:** Claude (on a different portal).

**Summary:** The technical plan was drafted and agreed: WPF (not WinUI 3), SkiaSharp for the character, a headless and testable core, a defined thread model, and an 8-step build order. The plan targeted .NET 8.

**Decisions:**
- WPF with `AllowsTransparency` and a toggled `WS_EX_TRANSPARENT` for click-through, rather than WinUI 3.
- SkiaSharp (`SKElement`) for the character; everything else in WPF.
- Character engine and renderer kept separate so the art can be replaced (licensing note in the spec).
- UI thread owns the island state machine and character engine; a separate cursor thread owns click-through; the scheduler and NLU run off the UI thread.
- LM Studio fallback with short timeouts and a 30 s back-off.
- Build order: overlay, motion, island state machine, character engine and renderer, chat and parser, scheduler and alerts, LM Studio, then the remaining shell features.

**Docs:** the plan is preserved in `BUILD_PLAN.md` (converted on 2026-11-18). `NOTCH_BEHAVIORAL_SPEC.md` (derived from a demo GIF and the earlier AISA code) was the starting point; its authoring date was not recorded.

**Verification:** none (planning only).
