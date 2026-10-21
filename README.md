# Wrath of Azeroth Launcher

The Windows launcher displays `LauncherArtwork.png` at its original 1672×941 size. Its image is used unchanged; invisible click areas activate **Play**, **Check for Updates**, Settings, and the window controls. **Play** checks the update feed, installs or repairs managed server files and addons, writes the current login endpoint into the client's `realmlist.wtf`, and starts WoW after a successful update. **Check for Updates** performs the same update without launching the game. Use the gear in the image to choose the client folder or update-feed URL. Update results appear in a small message window so the artwork remains untouched.

The artwork's green realm indicator and “Client up to date” text are part of the supplied image and remain static. The launcher checks the realm connection in the background, but does not paint over the image to change those pixels.

## Install

1. Use a compatible Wrath 3.3.5a client.
2. Download `WoAClientLauncher-bundle.zip` from this repository and extract the complete bundle beside `Wow.exe`, including `LauncherArtwork.png`.
3. Run `WoAClientLauncher.exe`, open Settings if needed, and select **Play**.

Keep `LauncherArtwork.png` beside `WoAClientLauncher.exe`. The launcher does not include the base WoW client.

The public update feed distributes one combined client patch with all-races/all-classes, Auto Shot at its original range while moving, level-1 shard-free Summon Voidwalker, and the General-tab Summon Stat Exchanger spell. The launcher verifies SHA-256 for listed client files and repairs missing or changed files. After confirming the combined patch, it removes obsolete WoA patches 4–8 only when their hashes match the known old files. The server requires Stat Tracker 1.9.0 and disconnects clients that do not report it within 30 seconds. Players can keep their own addons in other addon folders.

The launcher executable itself does not update in place. Existing players need this new bundle for launcher interface changes; after installation, the launcher updates managed game files from the feed on **Play** or **Check for Updates**.

## Rebuild

Run `powershell -ExecutionPolicy Bypass -File .\build.ps1`. The script builds the Windows executable and bundle from `src\Program.cs`, `assets\LauncherArtwork.png`, and the current `launcher-feed`.
