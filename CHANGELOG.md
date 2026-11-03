# Changelog

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
