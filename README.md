# Wrath of Azeroth Launcher

The Windows launcher uses the Frostgate siege artwork, with all ten WotLK classes, as its background. A green or red realm indicator checks the public login endpoint every 15 seconds. **Play** checks the update feed, installs or repairs managed server files and addons, writes the current login endpoint into the client's `realmlist.wtf`, and starts WoW after a successful update. **Check for Updates** performs the same update without launching the game. Use the gear button to choose the client folder or update-feed URL.

## Install

1. Use a compatible Wrath 3.3.5a client.
2. Download `WoAClientLauncher-bundle.zip` from this repository and extract the complete bundle beside `Wow.exe`.
3. Run `WoAClientLauncher.exe`, open Settings if needed, and select **Play**.

Keep `Frostgate.png` beside `WoAClientLauncher.exe`. The launcher does not include the base WoW client.

The public update feed distributes one combined client patch with all-races/all-classes and Auto Shot at its original range while moving, plus Stat Tracker. The launcher verifies SHA-256 for listed client files and repairs missing or changed files. After confirming the combined patch, it removes obsolete WoA patches 4–8 only when their hashes match the known old files. The server requires Stat Tracker 1.9.0 and disconnects clients that do not report it within 30 seconds. Players can keep their own addons in other addon folders.

The launcher executable itself does not update in place. Existing players need this new bundle for launcher interface changes; after installation, the launcher updates managed game files from the feed on **Play** or **Check for Updates**.

## Rebuild

Run `powershell -ExecutionPolicy Bypass -File .\build.ps1`. The script builds the Windows executable and bundle from `src\Program.cs`, `assets\Frostgate.png`, and the current `launcher-feed`.
