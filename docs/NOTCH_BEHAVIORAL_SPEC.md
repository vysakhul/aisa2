# Notch Assistant: Behavioral Specification

**Scope:** A clean-room behavioral blueprint for a Windows desktop "notch" assistant with an animated character. The spec combines (a) a frame-by-frame reading of `demo.gif` and (b) the behavior of the existing AISA codebase (island state machine, character engine, chat, alarm/reminder scheduler, LM Studio fallback). It describes what the product does and how it should feel. It does not describe how the existing code is written.

**Reading the GIF:** 760 × 450 px, 337 frames at 15 fps (one frame is about 67 ms), 22.5 s long. It is a zoomed capture of a macOS screen. Scaling the expanded panel (≈727 GIF px) against the 640 px logical design width gives about **1.135 GIF px per logical px**, and the compact island (≈328 GIF px → 288 logical) confirms this. Every "logical px" figure below uses that conversion or the design constants.

**Licensing:** The demo's character ("Mochi") and its sounds are reserved by their author. They are fine for a personal build. A distributed build must use a different character and sounds. The architecture below therefore treats the character and sound set as swappable modules.

---

## 1. Window & UI Physics

### 1.1 Anchor model

| Property | Behavior |
|---|---|
| Anchor edge | The island is always glued to the **top edge** of the monitor work area. Its top corners are square and merge with the screen edge. Only the bottom two corners are rounded. |
| Horizontal anchor | The user picks **left, center or right**. Center is the default and is what the GIF shows. Left and right sit a 12 px margin (logical) from the screen corner. |
| Growth direction | The island grows **downward only** from the top edge. Horizontally it grows **symmetrically about its center** when centered, and **away from the screen corner** when anchored left or right. The top edge never moves. |
| Vertical offset | If the Windows taskbar is docked at the top, the island sits just below it (inset 0–200 px). Otherwise it sits at y = 0. |
| Monitor | Primary monitor. The position is re-evaluated when the display configuration or DPI changes. The check runs every 0.5 s. |
| Fill & depth | Solid pure black (#000). No drop shadow and no concave "shoulders", so it reads as a hardware cutout. |

### 1.2 Shape states and pixel drops

| Island mode | Size (logical W × H) | Bottom corner radius | GIF evidence |
|---|---|---|---|
| **Folded / closed notch** | 120 × 24 (Windows build). The GIF's Mac-style notch measures ≈206 × 37. | 10 | Frame 0: black notch with a camera dot. The face is not visible. |
| **Compact / peek** | 288 × 32 | 14 | Frames 78–82: wider black bar flush with the menu bar. Mini face at the left, app-tile glyphs at the right. |
| **Expanded, standard card view** (chat, alert, home) | 640 × 160 | 22 | Frames 88–102 and 210–300: ≈727 × 180 GIF px. |
| **Expanded, chat with history** | 640 × (240 + 40 per message, capped at 300) | 22 | Not in GIF. Behavior from the custom logic. |
| **Expanded, tall detail view** | 640 × up to 320 (largest panel) | 22 | Frames 107–206: code view ≈727 × 365 GIF px. |
| **Expanded, launch greeting** | 640 × 150 | 22 | Frames 1–60. |

**Estimated drops (bottom-edge travel):**
- Closed → compact: +8 px down, +168 px wider (a subtle "peek").
- Closed or compact → standard expanded: **≈ +128 to +136 px** down, about 3.5× wider.
- Standard → tall detail view: an additional ≈ +160 px down (frame 103 onward in the GIF).
- Expanded → closed: the full reverse, ≈ –136 px.

### 1.3 Animation curves

The motion is **asymmetric**: it opens springy and closes with a firm curve.

| Motion | Curve | Measured / specified |
|---|---|---|
| **Grow** (any mode moving "up" the ladder: closed → compact → expanded, or a taller view) | **Damped spring**, SwiftUI-style: response 0.5 s, damping fraction 0.72. This gives roughly 3–4% overshoot and visually settles in about 0.45–0.55 s. Width, height and corner radius animate independently on the same spring. | GIF frames 1–7: width increments of +88, +78, +63, +44, +24 GIF px, decelerating, reaching full width in about 0.45 s. Re-open at frames 83–88 takes ≈0.33 s from compact. |
| **Shrink** (moving "down" the ladder) | **Time-based ease-in-out curve**, 340 ms, cubic-bezier (0.45, 0, 0.2, 1). No overshoot, so the island must never bounce while closing. | GIF frames 76–78: fold in about 1–2 frames, consistent with a fast ease. |
| Physics integration | Fixed sub-steps of 1/240 s. Frame delta capped at 50 ms. The render loop **stops itself** once every value has settled, so idle CPU is ~0. | — |
| Character position & size | Plain springs (0.5 / 0.72) toward the character's target slot for the current mode, so the character **glides with** the island rather than snapping. | GIF frames 103–108: the character slides from the card center to the left column as the panel grows. |
| **Content cross-fade** | Outgoing view: fade to 0 and scale to 0.97 over 160 ms, ease-in. Incoming view: **waits 160 ms** (the island leads), then fades in over 300 ms ease-out while scaling to 1.0 on a slightly overshooting curve (0.3, 1.2, 0.4, 1). | GIF frames 85–91: the black panel arrives first, then the content appears dim and brightens. Frames 213–216 show the Claude card dissolving into the Stripe card. |
| Header row (tabs, gear, speaker, close) | Fades in with the content. It is never visible in compact or closed modes. | Frames 6–8. |
| Auto-close countdown bar | 2 px line, white at 35% opacity, centered on the island's bottom edge. It appears only during the last min(10 s, 0.6 × auto-close delay) and shrinks linearly from 160 px to 0. | Not visible in the GIF (the demo stays interactive). |
| Window resize lag | When the island reaches the closed state, the OS window itself shrinks to the notch footprint **420 ms later**, after the visual fold has finished. On wake, the window grows back **before** the island starts animating. | — |

### 1.4 Overlay ↔ desktop interaction

| Concern | Required behavior |
|---|---|
| Window type | Borderless, fully transparent background, always on top, hidden from the taskbar and Alt-Tab, not resizable/minimizable/closable. No OS shadow. The window is larger than the island (720 × 320 logical while open) and everything outside the black shape is transparent. |
| **Click-through** | The window ignores mouse input everywhere **except** inside the island's current rectangle plus a **14 px hit margin**. The margin keeps a cursor that just grazes the edge counted as "on" the island. Holding the left button also keeps input captured so drags don't fall through. Click-through toggles only when the inside/outside state flips. |
| Cursor sampling | A background poll reads the global cursor every **16 ms** when near and every **50 ms** when far. Positions are forwarded to the UI only within **420 px** of the window, which drives eye tracking. Beyond that, one final "far" event is sent and then nothing. |
| Folded idle cost | While closed, the poll **parks** and only wakes every **250 ms** to check whether the cursor is on the notch. If it is, input is re-enabled and a "closed-hover" wake event is sent. Target CPU: ~0%. |
| Focus | The window is **non-activating**: clicking the island never steals focus from the user's app. Only when a text-entry view is shown does the window become focusable. It takes keyboard focus, then focuses the input **120 ms later**, and gives focus back when that view closes. |
| DPI | Layout is in logical pixels. If the rendering scale differs from the monitor scale by more than 2%, a zoom correction is applied before anything is drawn. Ctrl+wheel and Ctrl+/–/0 zoom are suppressed. |
| Full-screen apps | The island stays on top. An optional OS toast fallback for alarms (off by default) covers exclusive full-screen games. |
| Audio | Allowed to play without a prior user gesture, because alarms must ring unattended. |

---

## 2. Character State Machine

Two machines run together:

- The **island machine** controls geometry and which surface is shown.
- The **character machine** controls mood: color, eyes, badge, glow and motion.

The character machine is layered: a **momentary override** (thinking, dizzy, flash states) takes precedence over the **alert state**, which takes precedence over the **dialog state** (asking a question), which takes precedence over **idle**. **Emotes** are short eye/particle overlays (≈1.8 s by default) that play on top of whatever state is active.

### 2.1 Island machine (geometry level)

| State | Visual | Entered by | Left by |
|---|---|---|---|
| **Greeting** (launch) | Expanded 640 × 150 with a dedicated greeting scene. | App start. | Greeting finishes → Compact after 0.6 s. If the pointer is over it: Compact after 10 s. Pointer leaves → Compact immediately. |
| **Closed** (folded) | Small black notch with the face hidden (opacity 0, shrunk to a 6 px dot at the notch's left). | Compact idle timeout, or explicit hide. | Pointer enters the notch's hit-rect → Compact. A non-alert event can also reveal Compact. Alerts force Expanded. |
| **Compact** (peek) | 288 × 32 bar with the face at the left (center 40, 16; Ø 20). In the GIF a status glyph cluster sits at the right. | Pointer onto the notch. Fold from Expanded. Launch greeting end. | **Rest on the face 0.5 s → Expanded** (hover-to-open). **Click anywhere on it → Expanded.** Pointer away for the hide delay (default 10 s; reference app 60 s) → Closed, **unless pinned**. |
| **Expanded** (home/chat/alert) | 640 × 160+ panel with header, cards and a large face (Ø 44, left column). | As above, plus tray "Open"/"Upcoming", plus any due alert. | Close button or Esc → Compact. Auto-close timer → Compact. **Never auto-closes while pinned.** |

**Idle timers in detail:**

| Timer | Default | Starts | Resets / cancels |
|---|---|---|---|
| Expanded → Compact | 15 s (user range 5–120) | Pointer leaves the island's hit-rect. | Pointer re-enters (cancels). Any key, click or typing inside (restarts from full). Changing the setting while it runs restarts it with the new value. |
| **Hover-opened grace** | 0.6 s | Island was opened by face-hover and the user has **not clicked inside**, then the pointer leaves. | Any click inside converts it to a normal open, which uses the 15 s timer. |
| Compact → Closed | 10 s | Pointer leaves the compact island. | Pointer re-enters. Activity. Disabled while pinned. |
| Face-hover open | 0.5 s | Pointer enters the face's circular hit-zone in Compact. | Pointer leaves the face or the island. |
| Greeting collapse | 0.6 s / 10 s | Greeting ends (no hover / hover). | Pointer leaves → immediate fold. |

### 2.2 Character visual states observed in demo.gif

| # | GIF time (frames) | State | Visual description | Trigger shown |
|---|---|---|---|---|
| 1 | 0.00 s (0) | **Hidden** | Notch only. No face. | App idle, before launch. |
| 2 | 0.07–0.47 s (1–7) | **Drop-in** | The notch springs open. The face starts at the notch's top center (compact-face size) and **falls** into the panel center, accelerating (ease-in). A starfield/warp-streak particle field radiates around it. | Launch. |
| 3 | ≈0.6–0.9 s (9–14) | **Content blink** | Eyes curve to "^ ^" (content), then reopen. A soft white halo appears behind the body. | Greeting timeline. |
| 4 | ≈1.3 s (19–20) | **Landing squash** | The body flattens wider and shorter (≈+14% width, –14% height) and rebounds. | Impact at the bottom of the drop. |
| 5 | ≈1.4–2.7 s (21–40) | **Wave** | Two small hands pop out with an overshooting "back" ease. The body slides slightly sideways. One hand waves at about 2.5 Hz while the other holds still and breathes (±0.04 rad). The head tilts with the wave. | Greeting timeline. |
| 6 | ≈2.9 s (44) | **Settle + badge** | Hands tuck away (ease-in). Eyes "^ ^". A circular **badge** (blue with "…") pops onto the top-left of the body with an overshoot scale-in. | Greeting end. |
| 7 | 3.0–5.1 s (45–76) | **Idle / attentive** | Pill-shaped eyes **track the cursor**. Random blinks. A slight glow. The badge shows the background task state ("…" means working). | Pointer present. |
| 8 | 5.2–5.5 s (78–82) | **Compact peek** | The mini face sits at the left end of the compact bar with its badge. A 2×2 tile glyph sits at the right. The cursor rests on the face. | Pointer on the notch. |
| 9 | 5.5–5.9 s (83–88) | **Expand from face** | The island springs open. The face glides from the compact slot to the expanded left slot and grows. | Resting on / clicking the face. |
| 10 | 5.9–13.6 s (88–205) | **Working** | Blue-tinted underside, "…" badge, eyes neutral and following the cursor. The panel grows to the tall detail view while a background task streams progress (Read → Edit → Bash with a spinner). | Background job running. |
| 11 | ≈13.6–14.2 s (204–212) | **Finished** | Badge morphs to a solid **green dot**, the body tint shifts to mint, eyes become "^ ^" (happy). In the full engine this is accompanied by a one-turn **roll** and sparks. | Background job completed. |
| 12 | ≈14.3 s (213–216) | **Context switch / re-skin** | A tile button is pressed (highlight + slight press-in). The old card fades. The face **re-colors** (white → brand blue) with a small squash. | Clicking a shortcut tile. |
| 13 | 14.4–17.8 s (216–266) | **Reactive happy** | A numeric balance ticks up, new rows slide in, and each event flashes happy "^ ^" eyes. | Data event arrives. |
| 14 | 18.1–20.4 s (270–306) | **Annoyed (slapped)** | Eyes become "> <" lines. A lilac/purple glow blooms. Each hit squashes the body and kicks it sideways/upward with a jelly-spring wobble. | Clicking the face (one click = one slap). |
| 15 | 20.5–22.5 s (307–336) | **Dizzy** | Content fades. The panel re-forms as a single card ("Too many hits at once. Give me a sec — back to work in three seconds.") with a pink/red radial wash. Eyes become **spirals**, the head sways rapidly side to side, and the body does a two-turn roll. | **Third slap within 1.7 s.** Recovers after ≈3.3 s. |

### 2.3 Complete character state catalog (beyond the GIF)

| State | Color / tint | Eyes | Badge | Motion | Glow |
|---|---|---|---|---|---|
| Idle | Soft white | Pill | None | Eye-tracking, idle blinks | White, 15% |
| Working | Blue #3B9EFF | Pill | "…" dots | — | Blue, 65% |
| Thinking | Violet #A78BFA | Pill, gaze fixed up-right | "…" dots | Gaze mostly ignores the cursor | Violet, 65% |
| Searching | Indigo #6366F1 | Pill | "…" dots | Eyes **scan** left↔right (≈2.6 rad/s) | Indigo, 65% |
| Approval / alarm | Amber #F5A524 | Wide | "!" | Continuous **bounce** (abs-sine hop); initial jump | Amber, 65% |
| Question | Cyan | Pill | "?" | Head **tilted** ≈0.17 rad; blink on entry | 65% |
| Error | Red #F4505E | Flat | Red dot | Quick horizontal **shake** (≈280 ms, 4 beats) | Red, 65% |
| Finished | Mint #34D399 | Happy "^ ^" | Green dot | One full **roll** in 0.95 s + 5 sparks | Mint, 65% |
| Rate-limited | Orange | Tired | Orange dot | Sweat drops | 65% |
| Sleeping | Grey-blue | Closed | None | Slow **breathing** scale; "z" particles; gaze fixed down | 15% |
| Dizzy | Pink | Spiral | None | Two-turn **roll** in 1.3 s; fast head sway (≈9 rad/s); no blinking | None |

**Emotes (overlays, about 1.8 s each):** love (heart eyes, blush, 4 heart particles, small hop), surprised (dot eyes, jump), proud (star eyes), wink, yawn (tired eyes), happy ("^ ^"), annoyed ("> <").

### 2.4 Interaction triggers (character level)

| Input | Condition | Result |
|---|---|---|
| Cursor anywhere within 420 px | Not greeting, not dizzy/sleeping | **Eye tracking**: horizontal gaze is the hyperbolic tangent of the cursor's horizontal distance to the face divided by 260 px. Vertical gaze is the same with 200 px. Yaw is scaled to 0.62 and pitch to 0.5, then low-pass smoothed, so the eyes saturate gently and never snap. |
| Pointer enters the face (expanded) | No override active; ≥6 s since the last love | Immediate blink, **eye scale-up to 108%**, "hover" sound. If the pointer **rests 1.9 s** (moving >40 px restarts the wait): **love** emote + sound. 6 s cooldown. |
| Pointer enters the face (compact) | Hover-to-open enabled; not pinned | After 0.5 s the island opens to the chat (Section 3). |
| Click on the face (expanded) | Not dizzy | **Slap**: squash, an upward + random-sideways velocity kick on a jelly spring (stiffness ≈60, damping ≈9, so it wobbles about 2–3 times), "> <" eyes for 0.8 s, slap + annoyed sounds. |
| 3 slaps within 1.7 s | — | **Dizzy** for 3.3 s, then a happy emote. Slaps are ignored while dizzy. Hover-love is suppressed while any override is active. |
| Idle time | Any non-sleeping state | Blink every 2.2–5.4 s (random); 22% chance of a double blink 230 ms later. |
| Badge change | — | The old badge scales out (90 ms), then the new one scales in with overshoot (280 ms). |
| Color/state change | — | Color cross-fades exponentially over ≈0.3 s. Tint and tilt ease to their new targets. |

### 2.5 Movement mechanics summary

- **Dropping:** At launch, the face falls from the notch line to the panel center with an ease-in curve over ≈0.4 s. It squashes on impact, rebounds with an ease-out to a small peak, then settles.
- **Swinging / wobble:** Not a pendulum on a string. Slaps and appearances inject velocity into a 2D under-damped spring, so the body jiggles around its anchor and returns.
- **Squash & stretch:** On slap and appearance, height goes to 0.78 then 1.10 then 1.0 while width goes to 1.16 then 0.95 then 1.0, over ≈370 ms. Volume is visually preserved.
- **Gliding:** The face's center and size spring to a per-mode slot: closed (hidden), compact (left end), expanded (left column, vertically centered on the 84 px card under the 42 px header).
- **Rolling:** A whole-body rotation for finished (1 turn) and dizzy (2 turns).
- **Head pose:** Yaw/pitch shift the eyes within the face to fake a 3D turn. Tilt rotates the whole body (question state, wave).
- **Halo:** A radial gradient 2.2× the face diameter centered on the face, colored and faded by state. It is hidden in compact and closed modes.

---

## 3. Integration of Custom Functionalities

The repository's custom features are a **chat-driven alarm & reminder assistant** with a **deterministic natural-language parser**, an **optional local LLM fallback (LM Studio)**, a **background scheduler**, a **tray menu**, and **user settings**. The GIF's demo content (code session, payments, shortcut tiles) is the *reference app's* integrations. In this build those surfaces are replaced by the custom features as follows.

### 3.1 Surface mapping

| GIF surface | Custom equivalent |
|---|---|
| Closed notch (frame 0) | Closed notch, 120 × 24, face hidden. The scheduler keeps running in the background regardless. |
| Compact with face + tile glyphs (frame 82) | Compact bar with the face at the left. **No tile cluster.** The face carries the **alert badge** when something is waiting. |
| Home card + shortcut tiles (frame 89) | **Chat card**: three quick-action chips ("⏰ Set an alarm", "🔔 Remind me", "📋 Upcoming"), a scrolling message log, a text input with a send button, and the face in the left column. The header has a single Chat tab plus Settings (gear), Mute (speaker) and Close (✕). |
| Tall detail view with step list (frames 107–206) | Chat growth: the panel deepens by 40 px per message, from 240 up to 300 px. Confirmation, list and cancel cards render inline in the log. |
| "Working → Done" badge transition (frame 204) | **Thinking → Finished** around a chat turn and a successful "Set". |
| Tile press → re-skin (frame 214) | Chip press → prefills or sends the input. No re-skin. |
| Dizzy card (frame 309) | Dizzy is a **character state only** in this build. An optional "Too many hits" message card can be added as a visual extra. |
| (not in GIF) | **Alert card** (alarm/reminder due) and the **pinned** island. |

### 3.2 Lifecycle: when each custom feature fires

**A. App launch**
1. The island enters **Greeting**. The full drop → squash → wave → settle sequence plays (≈4.6 s), with its own sound, then folds to **Compact**.
2. The scheduler loads saved items. Anything that came due while the app was closed (more than 120 s overdue), or was ringing at quit, becomes **Missed**.
3. **Alerts that arrive during the greeting are held** and raised the moment the greeting ends, so the greeting is never interrupted.

**B. Opening the chat (user-initiated)**

| Trigger | Island path | Character |
|---|---|---|
| Pointer onto the notch | Closed → Compact (spring), "peek" sound | Face glides in from its hidden dot. |
| Pointer rests on the face 0.5 s (setting "open on face hover", default on) | Compact → Expanded (chat), "open" sound | Face grows into the left column. The input receives focus after 120 ms. |
| Click on the compact island | Compact → Expanded (chat) | Same. Counts as a normal open (15 s auto-close, not the 0.6 s grace). |
| Tray → Open | Any → Expanded (chat) | — |
| Tray → Upcoming | Any → Expanded (chat), with "What's coming up?" auto-sent | List card appears. |

**C. A chat turn**
1. User sends text (Enter / send button / auto-sending chip). A "send" sound plays and the face enters **Thinking** (violet glow, "…" badge, gaze up-right). Thinking lasts **at least 350 ms**, even when the answer is instant.
2. The **deterministic parser** classifies intent (alarm / reminder / list / cancel / help) and extracts the time and label.
3. **LM Studio fallback** is consulted only when the parser fails, when the parser asked "When?" but the text looks timed, or when time words leaked into the label. It connects within 1.5 s or gives up, with a 15 s answer timeout and a 30 s backoff after a failure. Visually this only **lengthens Thinking**; there is no separate state.
4. Outcome → character:

| Outcome | Character | Sound |
|---|---|---|
| Understood → confirmation card ("⏰ Alarm · Tomorrow 07:00 — Wake up [Set] [Edit]") | Happy emote | finish |
| No time given → "When?" follow-up | **Question** state (cyan "?", head tilt), held until the user answers | finish |
| Help / greeting text | Wink emote | blip |
| LM Studio consulted and still not understood | **Error** flash for 1.2 s (red, flat eyes, shake) | error |
| List or cancel card shown | Happy emote | finish |

5. **Set** pressed on the card → the item is stored → **Finished** flash for 1.6 s (mint, happy eyes, roll + sparks) plus **proud** emote (star eyes) and "proud" sound. If storage fails (e.g. "That time has already passed"), an **Error** flash plays and an inline error note appears.
6. **✕ on a list row** → the item is deleted with a "pop" sound.
7. Every keystroke, click or non-empty input counts as **activity** and restarts the 15 s auto-close countdown. Esc or ✕ → Compact.

**D. Inactivity hide**
- Pointer leaves the expanded island → 15 s countdown (the bar is visible during the last 9 s) → **Compact** (340 ms ease, "close" sound, keyboard focus released).
- Pointer leaves the compact island → 10 s → **Closed** → the window shrinks to the notch footprint 420 ms later and cursor polling parks.
- Opened by face-hover and never clicked → leaving folds it after **0.6 s**.

**E. An alarm or reminder comes due (system-initiated)**
1. The scheduler (independent of the UI, so it fires while folded, re-checking the wall clock at least every 30 s to survive sleep/resume) marks the item **Ringing**. If the item is noticed more than 120 s late, it is marked **Missed** instead.
2. The island is **forced to Expanded** on the **Alert card** and **pinned**, so no idle timer can close it and pointer-leave has no effect. Hover-love is cancelled.
3. Per kind:

| Kind | Character state | Card wash | Sound | Unanswered behavior |
|---|---|---|---|---|
| **Alarm (ringing)** | **Approval**: amber, "!" badge, continuous bounce | Amber | Alarm tone **loops** on its own volume channel, ignoring the UI mute toggle | Scheduler **auto-snoozes after 5 min** (by the snooze length). Ringing stops and the island unpins/folds. |
| **Reminder (ringing)** | **Question**: cyan "?", tilt, plus **surprised** emote | Cyan | Chime once, **repeated once after 1 min** | After 5 min, **folds to Compact but stays pinned** (badge visible, no auto-hide) until answered. |
| **Missed item** | Question | Per kind | Chime once | Same 5 min fold-to-compact, pinned. |

4. The card shows icon + kind + due time ("Missed alarm · was due Yesterday 18:00" when missed), the label, "+N more" if several are queued (oldest first), **Snooze N min** and **Dismiss**.
5. **Dismiss** or **Snooze** → the ringing stops, the next queued alert (if any) is raised, otherwise the island **unpins**, the face plays a **happy** emote, the island folds to Compact, and the view resets to chat after the 340 ms fold finishes.
6. **✕ or Esc on an alert** → the island folds to Compact but **stays pinned**: the badge shows, it never auto-hides, and hovering the face or clicking reopens the alert.

**F. Character play (unchanged from the reference)**
Hover-love (1.9 s rest), slap (click), dizzy (3 slaps in 1.7 s for 3.3 s), eye tracking and blinks work in every expanded view **except** that hover-love is suppressed while any override (thinking, error, finished, dizzy) is active. Slaps also count as activity, so they restart the auto-close countdown.

**G. Settings and tray (live)**
Placement (left/center/right, edge margin), auto-close and hide delays, face-hover open, sound on/off and volume, alarm volume, snooze length, LM Studio enable/URL/model, and start-with-Windows. Changes apply immediately. A placement change re-anchors the notch. A delay change restarts any running countdown. The tray offers Open, Upcoming, Position, Settings, Pause and Quit.

### 3.3 Integrated state diagram (prose)

Greeting → Compact.
Compact ⇄ Closed via pointer in / 10 s idle.
Compact → Expanded(Chat) via a 0.5 s face rest or a click.
Expanded(Chat) → Compact via ✕, Esc, 15 s idle, or a 0.6 s grace after a hover-only open.
**Any state → Expanded(Alert, pinned)** when an item is due. This is held during the greeting.
Expanded(Alert) → Compact(pinned) via ✕, Esc, or a 5 min reminder timeout.
Alert answered → Compact (unpinned), or the next alert.
Within Expanded(Chat), the character cycles Idle → Thinking → {Happy | Question | Error} → Finished (on Set).
Slap and dizzy overlays are available in any Expanded view.

---

## 4. Implementation Requirements (visual layer, framework-agnostic; e.g. WinUI 3 / WPF / Avalonia)

### 4.1 Window & host

| Component | Purpose |
|---|---|
| **Top-level overlay window** | Borderless, per-pixel transparent, topmost, tool-window (no taskbar/Alt-Tab), non-activating by default. Sized to the largest panel (720 × 320 logical) while open and shrunk to the notch footprint while folded. |
| **Placement service** | Computes window x/y per placement, monitor work area, top-docked taskbar inset and DPI scale. Re-runs on display/DPI/setting changes. |
| **Global cursor monitor** | High-rate (16/50 ms) global cursor sampling with a parked 250 ms wake check while folded. Publishes "entered", "left" and "position" events. |
| **Hit-test / click-through controller** | Holds the island rectangle (+14 px margin) and toggles the window's input transparency only on state flips. Honors the held-mouse-button rule. |
| **Focus controller** | Switches the window between non-activating and focusable when the chat input is shown/hidden, with a 120 ms delayed input focus. |
| **Tray icon & menu** | Open, Upcoming, Position, Settings, Pause, Quit. |

### 4.2 Island visual tree

| Element | Notes |
|---|---|
| **Island shape** | A single black rounded rectangle with only the bottom corners rounded. Width, height and radius are bound to three animated values. |
| **Clip container** | Same shape. Clips view content (cards, greeting scene) but **not** the character, its halo or the countdown bar, which may overflow the edge (hearts and z's fly above it). |
| **Header bar** | Tab button(s), gear, speaker toggle, close (✕). Part of the fading content layer. |
| **View host** | Stacked views (Chat, Alert, Greeting, optional Dizzy card) with the asymmetric cross-fade (160 ms out, 300 ms in delayed by 160 ms, slight scale overshoot). |
| **Cards** | Dark surface (#141518), radius 20, 1 px faint border, a per-view radial color "wash" (chat indigo, alarm amber, reminder cyan, dizzy rose). |
| **Chat view** | Chip row, virtualized message log (user bubble vs. assistant reply with optional inline card), typing indicator, single-line input + send button. It reports height changes so the island can spring to a new height. |
| **Inline cards** | Confirmation (when/label + Set/Edit + result note), Upcoming list (rows with time, label, "missed"/"snoozed" tags and ✕), Cancel confirmation. |
| **Alert view** | Title line, label, "+N more", Snooze and Dismiss buttons. |
| **Countdown bar** | 2 px, 160 px max, bottom-center; width bound to the remaining auto-close time. |
| **Character halo** | Radial-gradient ellipse, 2.2 × face diameter, color/opacity bound to the character state. |

### 4.3 Character rendering

| Component | Notes |
|---|---|
| **Immediate-mode drawing surface** (Canvas / Win2D / SkiaSharp / composition drawing) | The character is procedurally drawn every frame: body, gradient underside tint, eyes (12 shapes), badge, hands, blush, outfit/skin color. Use a high-DPI backing store (up to 2×). |
| **Overhang padding** | ≈40 px above and ≈24 px on each side of the face, so particles and wobble never clip. |
| **Particle system** | Hearts, stars, sparks, sweat drops, z's, plus the greeting's starfield/warp streaks. Short-lived and additive. |
| **Greeting scene surface** | A separate 640 × 150 drawing surface used only during launch, with its own scripted ≈4.6 s timeline (drop, squash, wave, tuck, badge, tint) and a 340 ms hand-off that matches the island's fold curve. |
| **Character engine (headless)** | State config table (color, tint, eye, badge, bounce/scan/breathe/tilt flags), emote overlays with expiry, keyframed one-shots (blink, squash, roll, shake, hop), exponential smoothing for gaze/tilt/scale/color, and the 2D jelly spring. Exposes a "busy" flag so the render loop can sleep. Behind a swappable interface for a non-Mochi character. |

### 4.4 Animation infrastructure

| Component | Notes |
|---|---|
| **Tracked value** | One animated scalar that can be driven either by a **spring** (response/damping, 1/240 s sub-steps) or by a **timed cubic-bezier curve**, chosen per transition (grow = spring, shrink = curve). |
| **Spring value** | Used for the character's center X/Y and size. |
| **Single frame-loop driver** | One composition/render callback per frame (dt capped at 50 ms) that steps every value, applies geometry, pushes the hit-rect only when it changes by more than 0.5 px, draws the character, updates the countdown, and **unsubscribes when nothing is moving**. |
| **Storyboards / implicit transitions** | Used for simple, non-physical motion only: view cross-fades, header fade, button press scale (≈80 ms), badge pop. |

### 4.5 Collision / hit zones

| Zone | Shape | Used for |
|---|---|---|
| **Island hit-rect** | Current animated island rectangle + 14 px on all sides | Enter/leave, click-through toggle, idle timers. |
| **Face hit-circle** | Circle at the animated face center, radius = half the animated face size | Compact: 0.5 s hover-to-open. Expanded: hover-love, blink-on-hover, slap on click. |
| **Hover re-arm distance** | 40 px of travel inside the face circle | Restarts the 1.9 s love wait. |
| **Proximity radius** | 420 px around the window | Eye-tracking event gating. |
| **Folded wake zone** | The notch footprint, polled at 250 ms | Waking from Closed. |

### 4.6 Timers & services the visuals depend on

- **Island state machine:** headless, with five cancellable timers (face-open, hover-open, compact-hide, expanded-collapse with due-time exposed for the countdown bar, greeting-collapse), a pinned flag and a "hover-opened" flag.
- **Scheduler service:** runs outside the UI thread. Persists to a JSON file with atomic writes. Has scheduled/ringing/done/missed states, a 120 s grace, 5 min alarm auto-snooze, ≤30 s wall-clock re-check, and missed-at-startup detection. Pushes the full list to the UI on every change.
- **Sound service:** a UI channel (master ≈0.12, mutable) plus a separate **alert channel** (own volume, ignores mute, looping alarm, single-shot reminder, synthesized fallback tones so alerts are never silent). It suspends the audio device after 1.5 s of silence.
- **NLU pipeline:** deterministic parser first, LM Studio JSON-schema fallback second, with the timeouts and backoff above.
- **Settings store:** live-applied, broadcast to the overlay and the settings window.

### 4.7 Acceptance checks (visual)

1. Closed → Compact → Expanded → Compact → Closed, side by side with demo.gif, matches the timing within one frame at 60 fps: grows overshoot slightly, shrinks never do.
2. Clicks outside the black shape always reach the app underneath. Clicks on it never steal focus, except in the chat input.
3. Eyes follow the cursor smoothly from anywhere within 420 px. A rest of 0.5 s on the compact face opens the chat. A rest of 1.9 s on the expanded face shows hearts.
4. Three quick clicks on the face produce dizzy for about 3 s, followed by a happy recovery.
5. An alarm due while folded opens the island, pins it, bounces amber and loops sound. It survives pointer-leave and the idle timers, and auto-snoozes at 5 min.
6. At 100%, 150% and 200% DPI, on left/center/right placement, hit zones line up exactly with the drawn shape.
