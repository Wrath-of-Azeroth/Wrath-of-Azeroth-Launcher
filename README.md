# Wrath of Azeroth Launcher

Download `WoAClientLauncher-bundle.zip`, extract it into a compatible Wrath 3.3.5a client folder beside `Wow.exe`, and run `WoAClientLauncher.exe`.

The launcher checks the public update manifest before playing:

`https://github.com/Wrath-of-Azeroth/Wrath-of-Azeroth-Launcher/raw/refs/heads/main/launcher-feed/manifest.json`

Before launching WoW, it writes the public login endpoint from the manifest to `Data\\<locale>\\realmlist.wtf`. The current endpoint is `wrath-of-azeroth.playit.plus:6946`.

The feed distributes the all-races/all-classes client patch and Stat Tracker. The launcher verifies SHA-256 for listed client files and repairs missing or changed files on **Update & Play**. The server requires Stat Tracker 1.9.0 and disconnects clients that do not report it within 30 seconds. Players can keep their own addons in other addon folders.

The base WoW client is not included. Players must provide a compatible 3.3.5a client. The launcher does not update its own executable; download a new bundle when the launcher program changes.
