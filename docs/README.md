# Aisa Documentation Index

> **Aisa** is a Windows 11 "notch" assistant: a black island glued to the top of the screen with an animated character (Mochi) that sets alarms and reminders from chat, with an optional local-LLM fallback through LM Studio. It is a WPF app on .NET 10 using SkiaSharp for the character.
>
> **Start here.** This index tells any model or developer which document to read, in what order, and how to keep the documentation current.
> **Last verified against the repo:** 2026-11-18.

---

## 1. Reading order

| # | Document | Read it when |
|---|---|---|
| 1 | `README.md` (this file) | Always, first |
| 2 | `SETUP.md` | You need to build, run, test, install sounds, or find where data lives |
| 3 | `NOTCH_BEHAVIORAL_SPEC.md` | You change or judge any behavior. It is the source of truth for what the product must do |
| 4 | `ISSUES_AND_FIXES.md` | Before touching an area that has a past issue (currently: audio) |
| 5 | `FEATURES_AND_INVARIANTS.md` | You need to know what exists, which rules must not be broken, or how to extend the app. Includes the manual test checklist |
| 6 | `BUILD_PLAN.md` | You want the reasoning behind the architecture. Historical; do not treat it as current state |
| 7 | `PROGRESS_LOG.md` | You want to know what happened on which day, or you are finishing a session |

## 2. The documents

| File | What it is | Update policy | Status |
|---|---|---|---|
| `NOTCH_BEHAVIORAL_SPEC.md` | Behavioral specification: window physics, animation curves, character states, chat, scheduler, acceptance checks | Owner-maintained. Change only when the intended behavior changes | Present |
| `BUILD_PLAN.md` | The initial technical plan (decided 2026-11-16), with a "planned vs as-built" section | **Frozen.** Do not edit to describe new work | Present |
| `SETUP.md` | Requirements, first-time setup, commands, sounds workflow, data files, settings defaults, LM Studio, troubleshooting | Update when tooling, commands, settings or file locations change | Present |
| `ISSUES_AND_FIXES.md` | Problems that took several attempts or outside help, and why the code looks the way it does | Append a new numbered entry; never delete | Present (ISSUE-001) |
| `PROGRESS_LOG.md` | One entry per working day, newest first | Add or extend today's entry at the end of every session | Present |
| `FEATURES_AND_INVARIANTS.md` | Feature inventory with file locations, rules that must not be broken, extension recipes, known limitations, manual test checklist | Update whenever a feature is added, changed or removed | Present (reviewed against the source on 2026-11-18; a few items remain marked as not read in detail) |

---

## 3. Quick orientation

| Project | What it does |
|---|---|
| `src/Aisa.App` | WPF overlay window, click-through, cursor monitor, views, tray, settings, start with Windows |
| `src/Aisa.Core` | Island state machine, spring/curve animation, settings store |
| `src/Aisa.Character` | Headless character engine (states, emotes, eye tracking, slaps, dizzy) |
| `src/Aisa.Character.Rendering` | SkiaSharp renderer for the character |
| `src/Aisa.Scheduling` | Alarm and reminder scheduler with JSON persistence |
| `src/Aisa.Nlu` | Deterministic parser, time extractor, LM Studio structured-output fallback |
| `src/Aisa.Audio` | WAV cue playback through NAudio, with a separate alert channel |
| `tests/*.Tests` | xUnit tests for Audio, Character, Core, Nlu and Scheduling |
| `tools/Aisa.CharacterPreview` | Renders every character state to PNG |
| `assets/sounds` | WAV cues and `install.ps1` (the app reads from `%AppData%\Aisa\sounds`, not from here) |
| `release/` | Self-contained build output. A build artifact that can be stale |

**Naming:** the spec and plan say "Notch" (the UI concept); the code says "Aisa" (the product). Runtime data lives in `%AppData%\Aisa`.

## 4. Critical rules (known so far)

The complete list is in `FEATURES_AND_INVARIANTS.md` Section 2. The most important ones, confirmed from the repo and the issue log:

- **Audio buffers:** in any `ISampleProvider.Read`, never use `Array.Clear`, `Array.Fill`, `Buffer.BlockCopy`, `Array.Copy` or `buffer.Length` on the `buffer` parameter. NAudio may pass a `byte[]` aliased as `float[]`. Use typed loops with `offset` and `count`. Keep `SafeSampleToWaveProvider` in the output path. See ISSUE-001.
- **Sounds:** the app plays WAVs from `%AppData%\Aisa\sounds`. After replacing a WAV, run `assets\sounds\install.ps1` and restart Aisa. Missing or invalid files are silent by design.
- **Behavior comes from the spec.** For example, growing uses a damped spring and shrinking uses a timed curve (0.45, 0, 0.2, 1) with no overshoot, so the island must never bounce while closing.
- **Headless projects stay free of WPF.** All libraries target plain `net10.0` so `dotnet test` runs on any OS. Windows-specific code stays in `Aisa.App`.
- **Character and renderer stay separate** so the art can be replaced. The Mochi character and its original sounds are for personal builds only.
- **Persisted files are written atomically** (temp file, then `File.Move` overwrite), and a broken settings or schedule file must never stop the app from starting.
- **Changes to audio, click-through, focus, DPI or animation need a manual check on Windows.** Passing unit tests is not enough.

---

## 5. Working agreement for any model or developer

**At the start of a session**
1. Read this file, then `SETUP.md`. Read the spec section for the behavior you will touch.
2. If your change is near an area with an entry in `ISSUES_AND_FIXES.md`, read that entry first. Treat "do not simplify" notes as binding.
3. Check the latest `PROGRESS_LOG.md` entry for open items.

**During the session**
- Prefer small, targeted changes. Keep the headless/Windows split described above.
- If a problem takes more than one attempt or outside help, plan to record it as a new `ISSUES_AND_FIXES.md` entry.
- Where the code has a comment pointing to a doc entry, keep the comment.

**At the end of the session**
1. Add or extend today's entry in `PROGRESS_LOG.md`: models used, files changed, what was verified, and **what was not**.
2. Append to `ISSUES_AND_FIXES.md` if an issue qualified.
3. Update `FEATURES_AND_INVARIANTS.md` and `SETUP.md` if features, commands, settings or file locations changed.
4. Refresh the "Last verified against the repo" date at the top of any document you re-checked. Only update it if you actually verified the content.

**Conventions**
- Be honest about verification: say what was run or listened to, and what was not.
- Add new issues and new log entries without rewriting history.
- Keep file names as listed here so links keep working.

---

## 6. Who builds this

The application is built with **Claude** from the owner's instructions. **Gemini (via Antigravity)** was used once, on 2026-11-18, to diagnose and fix the audio buzz recorded as ISSUE-001. Always record which model or tool did the work in `PROGRESS_LOG.md`, so a later session knows where the code and its assumptions came from.
