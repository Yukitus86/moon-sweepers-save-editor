# Changelog

## 2.1.0

Saves from modded games, the Mars Sweepers mod, and belts with bends.

- **Mars Sweepers** (Nexus mod 148): Mars save slots are listed below the Moon slots as "Mars · Slot N" and can be edited like Moon saves. Editor backups for them go to `EditorBackups\mars-slot-N`. The mod's `mars-production.json` is linked to the new save automatically (otherwise the mod rejects it with "production file does not match this save") and is backed up and restored together with it.
- Saves with conveyor belts that have bends could not be opened ("Player data not found", reading stopped in section "rotations"). Since the 16 November update each belt stores a list of extra points, which is empty for straight belts; the editor now reads that list.
- Saves that contain tools or upgrades added by mods could not be opened ("Player data not found"). The editor now accepts mod ids with upper case, several dots, `:` or `-`.
- Entries from mods are kept as they are: their levels are only changed when you change them yourself, and "Unlock all" / "All to max level" / the "All …" tree buttons only touch the game's own tools and upgrades.
- Tools and upgrades added by mods carry a yellow **MOD** tag (with a tooltip) in the Tools and Upgrade tree tabs, and are marked "· Mod" in the hotbar lists.
- If a save still cannot be read, the error now names the section where reading stopped, so it can be reported.
- Tested on Windows 11 with Mars Sweepers 0.1.1, Belt Overdrive 1.0.1 and Titan Broom 1.0.0 (edited saves load in the game), see the README.
- Linux build: same changes; passes the automated UI test but is **not tested on a real Linux system yet**. Reports are welcome through the new "Linux test report" issue form.

## 2.0.1

Fix for the Moon Sweepers update of 16 November 2026 (Steam build 26682829).

- Saves written by the new game version could not be opened ("Player data not found"). The game now stores 2 more bytes per conveyor; the editor reads both the old and the new layout.
- Windows and Linux build

## 2.0.0 – Linux build

Separate Linux build of 2.0, made on request of a user. Not tested on Linux at release; Linux players have since reported that it works. The Windows build is unchanged.

- Avalonia UI with the same tabs and functions as the Windows build
- Finds the save in the Proton prefix of every Steam library (normal, Flatpak, Snap); otherwise "Choose save folder …"
- Detects the running game under Proton
- Single self-contained file for x86-64 (`MoonSweeperEditor-linux-x64.tar.gz`)

## 2.0.0

Rewrite. 1.0 could only change the money; 2.0 reads the whole player block of the save.

### New

- **Tools**: own any tool, set tool levels (1–3), equipped tool, hotbar slots and active slot
- **Upgrade tree**: all 22 nodes, single steps or all at once
- **Storage & robots**: contents of silo, garage storage, robots, robot docks and dust catchers, with filter and fill/empty
- **Money**: other accounts, pending kilograms and lifetime statistics besides the team balance
- **Backups tab**: list of all backups, restore any of them (not just the latest), manual backup
- **Advanced tab**: export the decompressed save as `.bin` and import an edited one (size and SHA256 are recalculated)
- Warning banner and save lock while the game is running
- Refuses to overwrite a save the game wrote after the editor loaded it
- New icon, new dark layout with tabs and a dark Windows title bar
- Command-line options `--root`, `--lang`, `--mask-user`, `--snapshot`

### Changed

- English is now always the default language; German stays available through the flags and is remembered
- The exe is now `MoonSweeperEditor.exe` (was `MoonSaveEditor.exe`)
- The save is parsed structurally and only written if the result parses again; unknown data is copied byte for byte
- The lifetime statistics in front of the wallet are recognised in both layouts the game uses (with and without per-player entries)
- Settings are stored in `%APPDATA%\MoonSweeperEditor\settings.json`

### Unchanged

- Backups still go to `MoonSweepers\EditorBackups\slot-N\`, so backups made by 1.0 can be restored
- `slot.json` (main-menu preview) is still updated with the new balance

## 1.0.0

- Lists all save slots and changes the team balance
- Recalculates SHA256 and size so the game accepts the file
- Automatic backup before every change, restore of the latest backup
- Dark theme, German/English UI
