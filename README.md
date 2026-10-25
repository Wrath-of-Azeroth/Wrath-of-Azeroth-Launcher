# Wrath of Azeroth Launcher

The Windows launcher displays `LauncherArtwork.png` at its original 1672×941 size. Its image is used unchanged; invisible click areas activate **Play**, **Check for Updates**, Settings, and the window controls. **Play** checks the update feed, installs or repairs managed server files and addons, and starts WoW after a successful update. **Check for Updates** performs the same update without launching the game. The launcher does not change `realmlist.wtf`; set your realm address in the client yourself. Use the gear in the image to choose the client folder or update-feed URL. Update results appear in a small message window so the artwork remains untouched.

The artwork's green realm indicator and “Client up to date” text are part of the supplied image and remain static. The launcher checks the realm connection in the background, but does not paint over the image to change those pixels.

## Install

1. Use a compatible Wrath 3.3.5a client.
2. Download `WoAClientLauncher-bundle.zip` from this repository and extract the complete bundle beside `Wow.exe`, including `LauncherArtwork.png`.
3. Run `WoAClientLauncher.exe`, open Settings if needed, and select **Play**.

Keep `LauncherArtwork.png` beside `WoAClientLauncher.exe`. The launcher does not include the base WoW client.

The public update feed distributes one combined client patch with all-races/all-classes, Auto Shot at its original range while moving, level-1 shard-free Summon Voidwalker, and the General-tab Summon Stat Exchanger spell. The launcher verifies SHA-256 for listed client files and repairs missing or changed files. After confirming the combined patch, it removes obsolete WoA patches 4–8 only when their hashes match the known old files. The server requires Stat Tracker 1.9.0 and disconnects clients that do not report it within 30 seconds. Players can keep their own addons in other addon folders.

On **Play**, the launcher checks the feed for a newer launcher version after updating managed game files. It verifies the new executable's SHA-256, replaces itself after closing, and then starts WoW. This first self-updating release must be installed from the new bundle once; later launcher releases can arrive through **Play**.

## Rebuild

Set `launcher-version.txt` to the new numeric `major.minor.patch` version, then run `powershell -ExecutionPolicy Bypass -File .\build.ps1`. The build script compiles the launcher, publishes its versioned executable into `launcher-feed/files`, and updates the launcher entry and hash in the feed manifest.
