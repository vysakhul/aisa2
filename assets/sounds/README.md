# Sound files

Aisa plays these WAV files from the runtime folder below. It does not synthesize a fallback:
if a cue's WAV is missing or invalid, that cue is silent.

```
%AppData%\Aisa\sounds\
```

Files in this folder are source copies kept in git; the app does not load directly from the repo.
Run `install.ps1` to copy them into place, then restart Aisa (sounds are cached per process).
If PowerShell blocks the script, use `powershell -ExecutionPolicy Bypass -File .\install.ps1`.

The WAVs here are a generated baseline: warm bell/mallet tones with soft attacks and light
reverb, peak about -4.4 dBFS (0.6). Replace any single file and re-run the script.

## File format

- `.wav` only — PCM or float, any sample rate/channels (converted to 48 kHz mono).
- Recommended: 48 kHz, 16-bit PCM, mono, peak about -3 dBFS.
- Max 60 s per file; anything longer is truncated.
- Add a short fade in/out to avoid clicks.
- `AlarmLoop.wav` loops seamlessly — export exactly one cycle, no leading/trailing silence.
- Filename must match the cue exactly; unknown names are ignored silently.

## Cues

| File | When it plays |
|---|---|
| `Peek.wav` | notch peeks on hover |
| `Open.wav` | island expands |
| `Close.wav` | island folds |
| `Send.wav` | chat message sent |
| `Finish.wav` | assistant reply / confirmation card |
| `Blip.wav` | help or greeting answer |
| `Error.wav` | message not understood, save failed |
| `Proud.wav` | alarm/reminder saved |
| `Pop.wav` | item removed or cancelled |
| `Hover.wav` | cursor over the face |
| `Love.wav` | character petted |
| `Slap.wav` | face clicked |
| `Annoyed.wav` | repeated slaps |
| `Dizzy.wav` | too many slaps |
| `Happy.wav` | happy emote |
| `Greeting.wav` | app launch melody |
| `AlarmLoop.wav` | alarm ringing (loops) |
| `ReminderChime.wav` | reminder chime |

UI cues play at 12% volume and alert tones at 80%; the speaker toggle mutes UI cues only.
