# Aisa: Setup and Requirements

> **Last verified against the repo:** 2026-11-18. Sources: `README.md`, `global.json`, all `.csproj` files, `assets/sounds/install.ps1`, `.hoplite/setup.sh`, `Aisa.Core/Settings/AisaSettings.cs`, `release/Aisa.runtimeconfig.json`.
> Items marked **(unconfirmed)** were reconstructed and not checked against a real run.

---

## 1. Requirements

| Requirement | Detail |
|---|---|
| OS to run the app | **Windows 11** (as stated in `README.md`). The app project targets Windows SDK `10.0.19041.0` as its minimum platform. |
| .NET SDK | **.NET 10 SDK.** `global.json` pins `10.0.100` with `rollForward: latestFeature`, so any newer 10.0.x feature band works. Check with `dotnet --list-sdks`. |
| Editor (optional) | Any. The CLI is enough. If you use Visual Studio, use a release that supports .NET 10 (Visual Studio 2026). |
| LM Studio (optional) | Only for the "unknown phrasing" fallback. The app works without it. |
| Audio output device | Any default Windows playback device. |
| Build-only on Linux/macOS | Possible for the headless libraries and their tests (they target plain `net10.0`). The WPF app compiles with `EnableWindowsTargeting` but **cannot run** off Windows. |

### Packages (restored automatically by NuGet)

| Project | Packages |
|---|---|
| `Aisa.App` | `SkiaSharp.Views.WPF` 3.119.4 (WPF and WinForms are SDK-provided; WinForms is used only for the tray `NotifyIcon`) |
| `Aisa.Character.Rendering` | `SkiaSharp` 3.119.4 |
| `Aisa.Audio` | `NAudio.Core` 2.2.1, `NAudio.Wasapi` 2.2.1 |
| Test projects | `xunit` 2.9.*, `xunit.runner.visualstudio` 2.8.*, `Microsoft.NET.Test.Sdk` 17.*, `Microsoft.Extensions.TimeProvider.Testing` 9.* |
| `Aisa.Core`, `Aisa.Character`, `Aisa.Nlu`, `Aisa.Scheduling` | none |

---

## 2. First-time setup (Windows)

1. Install the **.NET 10 SDK**: https://dotnet.microsoft.com/download/dotnet/10.0
2. Open a terminal in the solution folder (the one containing `Aisa.sln`).
3. Install the sound cues (see Section 4): run `assets\sounds\install.ps1` from PowerShell.
4. Run the app:
   ```powershell
   dotnet run --project src/Aisa.App -c Release
   ```
5. Aisa plays a short greeting and then folds into the notch at the top of the primary monitor. Hover the notch to peek, rest on the face or click to open the chat. The tray icon has Open, Upcoming, Position, Settings, Pause and Quit.

---

## 3. Everyday commands

```powershell
dotnet build Aisa.sln -c Release                 # build everything
dotnet test                                      # run all test projects (headless; works on any OS)
dotnet test tests/Aisa.Audio.Tests               # run one test project
dotnet run --project src/Aisa.App -c Release     # run the app
```

**Where outputs go.** `dotnet build` and `dotnet run` write to each project's own `bin\Release\...` folder under `src\` (for example `src\Aisa.App\bin\Release\net10.0-windows10.0.19041.0\`), never to the root `release\` folder. The root `release\` folder is filled only by `dotnet publish ... -o release`.

**Self-contained release build (unconfirmed command).** The `release\` folder is a self-contained build (it bundles .NET runtime 10.0.15 and the Windows Desktop runtime). The exact flags used to produce it were not recorded. A typical command that produces the same kind of output:

```powershell
dotnet publish src/Aisa.App -c Release -r win-x64 --self-contained true -o release
```

Notes on `release\`:
- It is a **build artifact**, not source. It goes stale the moment the code changes. Rebuild it before testing the "release" copy.
- It is listed in `.gitignore` (`release/`), so it is never committed.
- Aisa is single-instance. **Quit the running copy from the tray** before launching a rebuilt one, or you may keep testing the old build. Quit it before publishing too (the running exe is locked), and delete the old contents of `release\` first so no stale files are left behind.

---

## 4. Sounds

The app does **not** play from the repo. It loads WAV files from:

```
%AppData%\Aisa\sounds\<Cue>.wav
```

- Run `assets\sounds\install.ps1` to copy the WAVs from `assets\sounds` into that folder. **Re-run it whenever you replace or add a WAV, then restart Aisa** (`SoundService` caches loaded files per process).
- `.\install.ps1 -Clear` removes the installed sounds; cues become silent.
- A cue plays only if its file exists **and** is a valid WAV. Missing or invalid files are silent (no error, no fallback synthesis).
- The script warns about WAVs whose names don't match a cue, and lists cues that have no file.

**Known cue names (file name without `.wav`, case as shown):**
`Peek`, `Open`, `Close`, `Send`, `Finish`, `Blip`, `Error`, `Proud`, `Pop`, `Hover`, `Love`, `Slap`, `Annoyed`, `Dizzy`, `Happy`, `Greeting`, `AlarmLoop`, `ReminderChime`

**Recommended WAV format:** 48 kHz, 16-bit PCM, mono (the format of the current files; it avoids any resampling or channel mixing). Stereo files are averaged down to mono by the loader.

**Licensing:** the Mochi character and its original sounds are reserved by their author and are fine for a personal build only. A distributed build must use a different character and sound set (see `NOTCH_BEHAVIORAL_SPEC.md`).

---

## 5. Data and settings

Everything is stored in `%AppData%\Aisa`:

| File / folder | Purpose |
|---|---|
| `settings.json` | User settings (written atomically through a temp file). A broken file is ignored and defaults are used, so the app always starts. |
| `schedule.json` | Alarms and reminders (versioned JSON envelope, written atomically). An unreadable file is renamed to `schedule.json.corrupt-<timestamp>` rather than deleted. |
| `sounds\` | The WAV cues (Section 4). |

### Settings and defaults (`AisaSettings`)

| Setting | Default | Range / notes |
|---|---|---|
| `Anchor` | Center | Left, Center, Right |
| `EdgeMargin` | 12 | 0 to 400 (px, logical) |
| `AutoCloseSeconds` | 15 | 5 to 120 |
| `CompactHideSeconds` | 10 | 2 to 600 |
| `OpenOnFaceHover` | true | |
| `SoundEnabled` | true | |
| `UiVolume` | 0.35 | 0 to 1 (interface cues) |
| `AlarmVolume` | 0.8 | 0 to 1 (alert channel) |
| `SnoozeMinutes` | 10 | 1 to 120 |
| `LmStudioEnabled` | true | |
| `LmStudioUrl` | `http://localhost:1234/v1/` | blank falls back to the default |
| `LmStudioModel` | (none) | optional model id |
| `StartWithWindows` | false | |
| `ToastFallback` | false | Defined in the settings record but **not implemented**: no code reads it and the Settings window has no control for it |

---

## 6. LM Studio fallback (optional)

1. In LM Studio, load any chat model.
2. Open the **Developer** tab and click **Start Server** (default `http://localhost:1234`).
3. In Aisa's Settings, confirm the URL, optionally set a model id, and keep the switch on.

Behavior: Aisa calls LM Studio **only** when its built-in parser doesn't understand a message. If the server isn't reachable it gives up after **1.5 s** and doesn't retry for **30 s**. The answer timeout is 15 s.

---

## 7. Working with AI coding agents (Claude, Gemini, Antigravity, ...)

- Agents running in a **Linux sandbox** can build the solution and run `dotnet test` for the headless projects. They **cannot** run the WPF app, hear audio, or see the overlay.
- `.hoplite/setup.sh` installs the .NET 10 SDK into `~/.dotnet` on Linux sandboxes for exactly this purpose.
- Anything touching **audio, click-through, focus, DPI or animation** needs a **manual check on Windows** after the change. Passing unit tests is not enough. See the acceptance checks in `NOTCH_BEHAVIORAL_SPEC.md` (a manual checklist is planned for `FEATURES_AND_INVARIANTS.md`).
- Before changing audio code, read the audio entry in `ISSUES_AND_FIXES.md`.

---

## 8. Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| No sound at all | WAVs not installed. Run `assets\sounds\install.ps1`, then restart Aisa. Check that file names match the cue list and that `SoundEnabled` is on. |
| Replaced a WAV but hear the old sound | Sounds are copied to `%AppData%\Aisa\sounds` and cached per process. Re-run `install.ps1` and restart. |
| Sound is buzzy, robotic, or loops a short fragment after each cue | A buffer-handling bug that was fixed. If it returns, someone has reintroduced `Array.Clear` or the NAudio `WaveBuffer` alias path. See `ISSUES_AND_FIXES.md`. |
| Changes don't appear after rebuilding | An old instance is still running (single-instance app), or you are running the stale `release\` build. Quit from the tray and run the fresh build. |
| Chat says it doesn't understand, LM Studio never answers | Server not started, wrong URL in Settings, or Aisa is in its 30 s cool-down after a failure. |
| `dotnet` says SDK not found / wrong version | Install the .NET 10 SDK. `global.json` requires `10.0.100` or a newer 10.0 feature band. |
| App builds on Linux but won't start | Expected. The WPF app only runs on Windows. |
