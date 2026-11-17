# Aisa

A Windows 11 notch assistant: a black island glued to the top of the screen with an animated character that sets alarms and reminders from chat. Unknown phrasing falls back to a local LLM through LM Studio.

## Run it (Windows 11)

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. `dotnet run --project src/Aisa.App -c Release`

Aisa starts with a short greeting, then folds into the notch. Hover the notch to peek, rest on the face or click to open the chat, and use the tray icon for Open, Upcoming, Position, Settings, Pause and Quit.

## LM Studio fallback

Start LM Studio's local server (Developer tab → Start Server, default `http://localhost:1234`) with any chat model loaded. Aisa only calls it when its built-in parser doesn't understand a message. The URL, an optional model id and an on/off switch are in Settings. If the server isn't reachable, Aisa gives up after 1.5 s and doesn't retry for 30 s.

## Layout

| Project | What it does |
|---|---|
| `src/Aisa.App` | WPF overlay window, click-through, cursor monitor, views, tray, settings |
| `src/Aisa.Core` | Island state machine, spring/curve animation, settings store |
| `src/Aisa.Character` | Headless character engine (states, emotes, eye tracking, slaps, dizzy) |
| `src/Aisa.Character.Rendering` | SkiaSharp renderer for the character |
| `src/Aisa.Scheduling` | Alarm/reminder scheduler with JSON persistence |
| `src/Aisa.Nlu` | Deterministic parser + LM Studio structured-output fallback |
| `src/Aisa.Audio` | Procedurally synthesized sounds (NAudio), separate alert channel |
| `tools/Aisa.CharacterPreview` | Renders every character state to PNG |

The headless projects target plain `net10.0`, so `dotnet test` runs on any OS.

## Data

Everything lives in `%AppData%\Aisa`: `settings.json`, `schedule.json`, and an optional `sounds\` folder where a `<Cue>.wav` (for example `Finish.wav` or `AlarmLoop.wav`) replaces that built-in sound.
