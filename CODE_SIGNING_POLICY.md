# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## What gets signed

Only `ClutchKit.exe` built by this repository's GitHub Actions workflow (`.github/workflows/build.yml`) from the
source code in this repository. Nothing built on a personal computer is signed, and no third-party binaries are
included or signed.

## Team roles

- **Committers and reviewers:** the repository owner
- **Approvers** (approve each signing request): the repository owner

All team members use multi-factor authentication for GitHub and SignPath.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the
user or the person installing or operating it.

ClutchKit runs a small web server that is only reachable from the same computer (`127.0.0.1`), so that the
TikTok LIVE Studio overlay on that computer can show the counters.
The keyboard is only used to react to the hotkeys the user sets; key presses are not stored or sent anywhere.
Actions play the sound files the user picked, press the keys the user chose or type the text the user entered when something happens on the LIVE. Sound files the user picks are copied into the ClutchKit data folder.

The only internet connection is the optional TikTok LIVE connection: when the user presses **Connect** (or turns
on "Connect automatically"), ClutchKit sends the TikTok username and the user's own Euler Stream API key to
Euler Stream (`wss://ws.eulerstream.com`), which sends back the LIVE's events. The API key is stored in
`settings.ini` on the user's computer and is not sent anywhere else.
