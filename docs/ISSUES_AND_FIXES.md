# Aisa: Issues and Fixes

> **Purpose:** a record of problems that took several attempts or outside help to solve, so they do not return and so the reason behind unusual code is never lost.
> **Last verified against the repo:** 2026-11-18.
> **Rule for every model and developer:** if code has a comment pointing here, or an entry below says "do not simplify", read the entry before changing that code.
> Add new entries at the **bottom**, numbered `ISSUE-002`, `ISSUE-003`, and so on. Never delete an entry; mark it superseded instead.

## Entry template

```
## ISSUE-NNN: Short title
Status / Date / Area / Files
Symptom            what the user saw or heard
Why it was hard    what made it misleading
How it was diagnosed
Attempts that did not work (and why)
Root cause
Fix
Why the code looks like this (do not simplify)
Regression guard   test name, and what it does NOT cover
If it returns      checklist
Who / how solved
```

## Index

| ID | Title | Area | Status | Date |
|---|---|---|---|---|
| ISSUE-001 | UI sound cues play as a frozen 100 Hz buzz | Aisa.Audio | Fixed (verified by listening) | 2026-11-18 |

---

## ISSUE-001: UI sound cues play as a frozen 100 Hz buzz

- **Status:** Fixed. Verified by listening on 2026-11-18 (hover over the notch, click, open the assistant): no sound problems. **Not** re-verified by a new recording and signal analysis.
- **Area:** `Aisa.Audio`
- **Files involved:**
  - `src/Aisa.Audio/SoundService.cs` (`RootProvider.Read`)
  - `src/Aisa.Audio/Types.cs` (`SafeSampleToWaveProvider`, `WasapiOutput`)
  - `tests/Aisa.Audio.Tests/SoundTests.cs` (`ReusedWaveBufferSilencesAfterCueEnds`)
  - `src/Aisa.Audio/Aisa.Audio.csproj` (a temporary `NAudio.WinMM` reference was added and then removed)

### Symptom

The WAV files sounded perfect when opened directly from the folder. When Aisa played them, each cue started correctly for a moment and then turned into a buzzy, robotic tone that lasted about 1.5 seconds after the cue should have ended. It sounded as if the file had been mixed with synthesized audio, or as if the audio format were unsupported.

### Why it was hard

- It sounded like a *content* problem (wrong file, synth fallback, bad format), but the files, formats, resampling and volume were all fine.
- The first ~60 ms of every cue was correct, which made it look like a device or driver glitch rather than a buffer bug.
- The first fix attempt (changing the output API) changed nothing, which pointed away from the real cause.
- Unit tests passed, because they use a fake output that pulls into fresh buffers instead of reusing one.

### How it was diagnosed

A screen recording of the app (hover, open, close) was analyzed offline (audio extracted with ffmpeg, then numpy/scipy):

1. **The cues were not the Greeting.** The three sounds were Peek (~0.95 s), Open (~4.66 s) and Close (~9.2 s).
2. **The first ~60 ms was the correct file.** Waveform correlation with the repo WAVs was 0.9999 (Peek) and 1.0000 (Close) at a constant gain of about 0.078. So the WAVs, the loader and the files in `%AppData%\Aisa\sounds` were correct.
3. **Then the output froze.** After ~60 ms the signal became a single **480-sample (10.00 ms at 48 kHz)** chunk repeated over and over: autocorrelation at 480 samples was about 0.99, adjacent 10 ms blocks had similarity 1.00, and the spectrum never changed (lines at 500/600/700/800 Hz, i.e. multiples of 100 Hz).
4. **The buzz lasted cue length + ~1.5 s** (bursts of 1.76 s, 2.02 s and 2.01 s for WAVs of 0.2 s, 0.55 s and 0.55 s). That equals `SoundService.IdleTimeout` (1.5 s): the buzz ended exactly when the audio device was released.
5. **The app's playback loop was still running in real time.** If the playback thread had died, the voice would never have been consumed, `HasActive` would have stayed true, and the device would never have been released. It was released on schedule, so `Read` kept being called and the fault was in what `Read` wrote into the buffer.

### Attempts that did not work

| Attempt | Result | Why it failed |
|---|---|---|
| Replace `WasapiOut` (Shared, event-driven, 60 ms) with `WaveOutEvent` (MME, 80 ms, 3 buffers) and add `NAudio.WinMM` | Same symptom | Both outputs go through the same `SampleToWaveProvider`/`WaveBuffer` path, so the bug was upstream of the output API. The change was reverted and the package removed. |
| Suspecting the WASAPI event-driven stream, the 60 ms latency, the DMO resampler, a dead playback thread, or the audio driver | Not the cause | These were hypotheses from the recording only. The evidence above (real-time consumption, exact 10 ms period) pointed instead at stale buffer contents. |

### Root cause

NAudio's `SampleToWaveProvider` implements `Read(byte[] ...)` by wrapping the byte buffer in a `WaveBuffer`, an explicit-layout union that exposes the *same memory* as a `float[]`. It then calls `ISampleProvider.Read(float[] buffer, int offset, int count)` with that aliased array. **At runtime the object is still a `byte[]`** (1 byte per element), even though the C# static type is `float[]`.

`RootProvider.Read` cleared its output with `Array.Clear(buffer, offset, count)`. `Array.Clear` takes a `System.Array` and uses the runtime element size, which is 1 byte, so it cleared only `count` **bytes** (a quarter of `count` floats) instead of `count` floats.

NAudio reuses the same `byte[]` for every refill. The consequences:

- The very first fill (60 ms) went into a fresh, zeroed buffer, so the start of each cue was clean.
- Each later 10 ms refill (480 stereo frames = 960 floats = 3840 bytes) cleared only the first 960 bytes, about 2.5 ms. The remaining ~7.5 ms kept stale samples from the start of the cue, and the mixer then added (`+=`) new audio on top of that stale data.
- After the cue ended, nothing new was written, but the stale 7.5 ms stayed in the buffer and was replayed every 10 ms. That is a 100 Hz buzz, matching the measured 480-sample period, and it continued until the device was released after the idle timeout.

### Fix

Two layers, both in `Aisa.Audio`:

1. **Typed zeroing in `RootProvider.Read`** (`SoundService.cs`). Replaced `Array.Clear(buffer, offset, count)` with a `for` loop that assigns `buffer[offset + i] = 0f`. Indexing uses the static element type (float, 4 bytes), so it is correct whether the caller passes a real `float[]` or a `WaveBuffer`-aliased array. This is the actual root-cause fix.
2. **`SafeSampleToWaveProvider`** (`Types.cs`). Replaces NAudio's `SampleToWaveProvider` for the real output. It reads into a genuine `float[]` scratch buffer it owns and copies to the `byte[]` with `Buffer.BlockCopy`, so no aliasing exists at all. `WasapiOutput.Init` wraps the source with it. This is defense in depth.

The `WaveOutEvent` workaround and the `NAudio.WinMM` reference were removed. The default output is `WasapiOut` (Shared, 60 ms) again.

### Why the code looks like this (do not simplify)

- **Do not replace the zeroing loop in `RootProvider.Read` with `Array.Clear`, `Array.Fill`, `Span.Clear()` or `Buffer.BlockCopy`/`Array.Copy` on `buffer`.** They look equivalent and are shorter, but on an aliased array they use the byte element size and silently corrupt the buffer. Use typed indexing loops, or work only on arrays this code owns.
- **Do not rely on `buffer.Length` in `ISampleProvider.Read` implementations.** For an aliased array it may reflect bytes, not floats. Use `count` and `offset`.
- `Array.Clear(_mono, 0, frames)` inside `MixBank` is fine: `_mono` is a real `float[]` that this class allocates.
- **Do not swap `SafeSampleToWaveProvider` back to `Init(ISampleProvider)` or `SampleToWaveProvider`** to "remove custom code". It exists to remove the aliasing hazard.
- Any **new** `ISampleProvider` added to this project must follow the same rules, because NAudio may call it with an aliased array.

### Regression guard

- **Test:** `ReusedWaveBufferSilencesAfterCueEnds` (`SoundTests.cs`, class `ServiceTests`). It deliberately uses NAudio's own `SampleToWaveProvider` (the aliasing one), reads a 60 ms initial fill and then forty 10 ms refills into one reused `byte[]`, and asserts that the tail is exactly silent after the cue ends.
- **What it covers:** the zeroing behavior of `RootProvider.Read` (the root cause).
- **What it does not cover:** `SafeSampleToWaveProvider` itself, and real devices or drivers. Audio output still needs a manual listening check on Windows after any change in `Aisa.Audio`.
- The older `SynthTests` and `SoundSynth` are unrelated to this bug. `SoundSynth` is not used at runtime; only tests reference it.

### If it returns

Symptoms: a buzz or short looping fragment after cues, lasting until the device is released.

1. Confirm `RootProvider.Read` still zeroes with a typed `for` loop (no `Array.Clear` on `buffer`).
2. Confirm the Windows output still wraps its source in `SafeSampleToWaveProvider`.
3. Run `dotnet test tests/Aisa.Audio.Tests` and confirm `ReusedWaveBufferSilencesAfterCueEnds` passes.
4. Search `Aisa.Audio` for new uses of `Array.Clear`, `Array.Copy`, `Buffer.BlockCopy`, `Span` helpers or `.Length` on the `buffer` parameter of any `Read` method.
5. To check a recording objectively: extract the audio and test for repetition at 480 samples.

```python
# Detects the "frozen 10 ms chunk" signature in a mono 48 kHz segment `x` (numpy float array)
import numpy as np
def repeat_score(x, period=480):
    a, b = x[:-period], x[period:]
    return float(np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b) + 1e-9))
# Healthy cue tail or silence: low score or near-zero energy.
# This bug: score close to 1.0 for ~1.5 s after the cue ended.
```

### Who and how it was solved

- **2026-11-18, Claude:** analyzed the screen recording (steps above), proved the problem was in the output chain and not in the WAVs, synth or resampling, and tried the `WaveOutEvent` change (did not help; reverted).
- **2026-11-18, Gemini via Antigravity:** found the `Array.Clear` / `WaveBuffer` byte-versus-float mechanism, applied the zeroing-loop fix and `SafeSampleToWaveProvider`, added the regression test, and rebuilt the `Release` and `release\` builds.
- **Verified by the project owner by listening** after the rebuild.
