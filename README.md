# ClutchKit

Stream toolkit for **TikTok LIVE Studio** on Windows. Its first tool is a set of on-stream **counters**
(deaths, wins, kills, attempts, ...) that you change with hotkeys while you play.

- As many counters as you need (up to 8), each with its own hotkeys and its own look
  (font, colors, outline, shadow, animation)
- Hotkeys work while you're in a game, and the game still gets the key press
- One see-through overlay for all counters: paste one link into LIVE Studio once
  (or use a capture window instead)
- Works no matter whether LIVE Studio or ClutchKit is started first
- No internet connection: the overlay talks to ClutchKit on your own PC only

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

## Code signing

See [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

## License

[MIT](LICENSE)
