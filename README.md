# Wrath of Azeroth Launcher

Download `WoAClientLauncher-bundle.zip`, extract it into a clean Wrath 3.3.5a client folder beside `Wow.exe`, and run `WoAClientLauncher.exe`.

The launcher checks the public update manifest before playing:

`https://raw.githubusercontent.com/Wrath-of-Azeroth/Wrath-of-Azeroth-Launcher/main/launcher-feed/manifest.json`

The feed currently distributes the all-races/all-classes client patch and the Stat Tracker addon. To publish future addon or client file updates, update `launcher-feed/manifest.json` and upload each referenced file to this repository. Existing players then receive those feed updates the next time they select **Update & Play**.

The base WoW client is not included. Players must provide a compatible 3.3.5a client. The launcher also does not update its own executable; distribute a new bundle when the launcher program itself changes.
