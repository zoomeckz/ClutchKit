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

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org)
(once the project is approved). Team roles and the privacy statement are in the
[Code signing policy](CODE_SIGNING_POLICY.md).

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the
user or the person installing or operating it. See the [Code signing policy](CODE_SIGNING_POLICY.md) for details.

## License

[MIT](LICENSE)
