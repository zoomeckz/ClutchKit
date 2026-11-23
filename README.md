# ClutchKit

Stream toolkit for **TikTok LIVE Studio** on Windows: on-stream **counters** (deaths, wins, kills, attempts, ...)
that you change with hotkeys while you play, **stream widgets** (goal bars, a subathon timer, top gifters), and
**actions**: gifts, follows, shares, joins, likes and chat commands play sound alerts, press keys in your game or
change your counters.

**Counters**
- As many counters as you need (up to 8), each with its own hotkeys and its own look
  (font, colors, outline, shadow, animation)
- Hotkeys work while you're in a game, and the game still gets the key press
- One see-through overlay for all counters: paste one link into LIVE Studio once
  (or use a capture window instead)
- Works no matter whether LIVE Studio or ClutchKit is started first

**Stream widgets**
- Goal bars for likes, follows, shares, coins, gifts, joins or any counter; a reached goal can raise itself
  (e.g. +1000) so the bar keeps going
- A subathon timer: gifts (per coin), follows, shares and likes add time; start, pause, +/- 5 min, a maximum
- Top gifter and top liker lists, for this LIVE or all time
- Each widget is its own Link source in LIVE Studio, so you can place it anywhere; "Preview" shows it in your browser
- This LIVE's lists and goals start over by themselves when your next LIVE starts

**Actions**
- Triggers: a gift (by name, or any gift worth at least N coins), a follow, a share, a viewer joining,
  every N likes, or a chat command such as `!boom` (with a cooldown per viewer; ClutchKit never writes in chat)
- Change a counter: add, subtract, set, or add the gift's coins (e.g. every Rose = +1 death)
- Sound alerts with no limits: MP3/WAV played by ClutchKit itself (not through the overlay), with volume,
  one after another so they never talk over each other
- Press a key in your game, or type text such as a Minecraft command
- Cooldowns per action, so a busy room can't spam join sounds
- Combos are counted properly: a x10 Rose combo can press the key 10 times, or once
- Keys are sent like a real keyboard (scan codes) and held long enough for games to notice
- Safety: only send keys while your game is in front, an on/off hotkey, a queue limit
- Warns when the game runs as administrator (Windows then blocks the keys) and offers to restart as administrator
- Test buttons and fake events, so you can set everything up offline
- Stays tidy with lots of actions: sort them into topics (e.g. one per game) that fold up and can be switched
  on/off as a whole, fold each action to one line, search, and duplicate actions
- Profiles: save which topics are on (e.g. one per game) and switch with one click
- Export topics with their sounds to a `.ckpack` file and import other streamers' packs (imported topics
  start switched off, so you can check them first)
- LIVE events are read through [Euler Stream](https://www.eulerstream.com) (free account and API key), because
  TikTok has no official API for LIVE events

**Languages**
- English, Español, Português (Brasil), Bahasa Indonesia, Türkçe, Deutsch, Français and Tiếng Việt
- Starts in your Windows language; change it any time on the General page
- Translations live in [src/Translations.cs](src/Translations.cs) (one row per text). Corrections are welcome;
  run `tools\check-translations.ps1` to check that nothing is missing

**Debugging**
- A Debug page with a live status (TikTok messages by type, events, keys sent or blocked, sounds, overlay requests)
- Detailed logging to `debug\debug-log.txt`, including the first raw TikTok messages of each type
- "Save debug report": system info, settings summary (never the API key), errors and the activity log, saved and copied
- A layout check that builds the window in every language, lists text that is cut off or overlaps, and saves
  screenshots of every page
- Crash reports in `debug\` for anything that goes wrong

## Install

Download `ClutchKit-<version>.zip` from [Releases](../../releases), unzip it and run `ClutchKit.exe`.
Choose **Install** (Start menu shortcut + uninstall entry, no admin rights needed) or **Run without installing**.
The full guide is in [docs/How to install ClutchKit.txt](docs/How%20to%20install%20ClutchKit.txt).

## Add it to TikTok LIVE Studio

In ClutchKit open **Overlay** > **Copy LIVE Studio link**, then in LIVE Studio: **Add source** > **Link** > paste.
You only do this once; changes you make in ClutchKit show up on stream automatically.

## Build from source

Requires only Windows:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

The result is in `dist\`. Release builds are made by GitHub Actions from this repository.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org)
(once the project is approved). Team roles and the privacy statement are in the
[Code signing policy](CODE_SIGNING_POLICY.md).

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the
user or the person installing or operating it. The overlay only talks to ClutchKit on your own PC. The only internet
connection is the TikTok LIVE connection, and only after you press Connect: your TikTok username and Euler Stream
API key go to Euler Stream. See the [Code signing policy](CODE_SIGNING_POLICY.md) for details.

## License

[MIT](LICENSE)
