# GryphonMods
Lumentale mods made by CrimsonGryphon.

This repository contains a modded launcher for LumenTale: Memories of Trey and a growing collection of BepInEx mods. The launcher handles mod installation and updates automatically — no manual file management required.

## Requirements
- [LumenTale: Memories of Trey](https://store.steampowered.com/app/2261430) (Steam)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) (the launcher will prompt you if missing)

## Installation

1. Download [**BepInEx_win_x64_6.0.0.755.zip**](https://github.com/AegeanGryphon/GryphonMods/releases/tag/BepInEx-v6.0.0.755) and extract the ZIP directly into your game folder:
   ```
   ..\steamapps\common\LumenTale Memories of Trey\
   ```
2. Download [**LumenTaleLauncher.zip**](https://github.com/AegeanGryphon/GryphonMods/releases/tag/Launcher-v1.0.0) and extract it anywhere on your computer.
3. Launch LumenTale once through Steam and let it fully load, then close it. This allows BepInEx to initialize.
4. Open **LumenTaleLauncher.exe** from wherever you extracted it.

## Using the Launcher

**▶ Play Modded** — Launches the game via Steam with all mods in your BepInEx `plugins` folder active. All Steam features remain available.

**⚙ Mod Management** — Browse, install, and update mods hosted in this repo. Shows available mods on the right and your currently installed mods on the left.

**📋 Copy Launcher Path for Steam** — Copies the full path to `LumenTaleLauncher.exe` to your clipboard, useful for adding it as a non-Steam game in your Steam library.

**Show BepInEx Console** — Toggles the BepInEx debug console window during gameplay.

## Mods

| Mod | Description |
|-----|-------------|
| **ScanIndicator** | Displays scan-progress diamonds on battle portrait cards showing wiki knowledge level for each animon. |
| **HoloboardHoloken** | Allows throwing the holoken while riding the holoboard. |
| **DualTypeHoloken** | Allows an animon's hidden secondary type to activate matching elemental objects in the overworld. |
| **EvoScan** | Automatically grants full research level 3 to an animon's evolved form when it evolves. |
