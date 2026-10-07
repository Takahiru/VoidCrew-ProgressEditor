# Progress Editor

Edit your Void Crew progression through a menu styled to match the game.

## How to use

Open **Edit Progress** above **Settings** in the main menu, or below **Settings** in the pause menu.

### Rank

Adjust rank, experience and favor rank using sliders or text fields, then select **Apply**. **Undo** restores the previous edit during the current game session. Applying unchanged values leaves your undo available.

- Rank and experience editing is restricted to follow ingame rules.

### Cosmetics

Check **Unlock All Lootbox Cosmetics**, **Unlock All Seasonal Cosmetics**, or **Unlock All Achievement Cosmetics** to unlock that category. Unchecking restores the items and achievement progress tracked by the plugin, preserving cosmetics you already owned.

Achievement cosmetics are marked completed and claimed in the game profile. The plugin blocks Steam achievement awards for the achievements it tracks; unrelated achievements are unaffected.

## Installation

Install through Thunderstore Mod Manager or r2modman and launch the game from that manager. For local testing, import this ZIP as a local mod into your Void Crew profile; ensure the listed BepInEx dependency is installed.

For manual installation, install BepInEx 5 and copy `BepInEx/plugins/ProgressEditor/ProgressEditor.dll` into the matching folder in your game installation. The rank-card image is embedded in the DLL.

Do not install a second copy of this plugin alongside an existing manual installation.

## Building from source

Run `build.ps1` with the path to your Void Crew installation.

```powershell
.\build.ps1 -GamePath 'D:\SteamLibrary\steamapps\common\Void Crew'
```
