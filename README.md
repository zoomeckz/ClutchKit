# ClutchKit

Stream toolkit for **TikTok LIVE Studio** on Windows: on-stream **counters** (deaths, wins, kills, attempts, ...)
that you change with hotkeys while you play, and **gift actions** that let viewers' gifts press keys in your game.

**Counters**
- As many counters as you need (up to 8), each with its own hotkeys and its own look
  (font, colors, outline, shadow, animation)
- Hotkeys work while you're in a game, and the game still gets the key press
- One see-through overlay for all counters: paste one link into LIVE Studio once
  (or use a capture window instead)
- Works no matter whether LIVE Studio or ClutchKit is started first

**Gift actions**
- A gift (by name, or any gift worth at least N coins) presses a key in your game, or types text
  such as a Minecraft command
- Combos are counted properly: a x10 Rose combo can press the key 10 times, or once
- Keys are sent like a real keyboard (scan codes) and held long enough for games to notice
- Safety: only send keys while your game is in front, an on/off hotkey, a queue limit
- Warns when the game runs as administrator (Windows then blocks the keys) and offers to restart as administrator
- Test buttons and fake gifts, so you can set everything up offline
- Gifts are read through [Euler Stream](https://www.eulerstream.com) (free account and API key), because TikTok
  has no official API for LIVE gifts

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
