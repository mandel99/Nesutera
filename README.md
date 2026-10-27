# Nesutera

**Nesutera** is a project to reimplement the classic city-building game **Pharaoh** and its expansion, **Cleopatra: Queen of the Nile**, in modern **C#**.

The primary goal is to preserve **all original game behavior** as faithfully as possible while rebuilding the underlying systems in clean, readable, maintainable code. This is intended to be a faithful reimplementation first—not a gameplay redesign.

## Approach

The original game is being studied through reverse engineering, supported by:

- **Ghidra** — disassembly, decompilation, and analysis of the original executable.
- **REA** — assistance with reverse-engineering research and understanding game systems.
- **Codex** — assistance with C# implementation, code organization, and refactoring.

The original game's behavior is the reference for the new implementation. The aim is to reproduce its rules, simulation, mechanics, interactions, and even relevant quirks, while replacing the legacy implementation with a modern C# codebase.

## Build

The Windows application targets .NET 9. Install the .NET 9 SDK and build from
the repository root:

```powershell
dotnet build .\Nesutera.sln -c Release
```

Create a framework-dependent desktop distribution with:

```powershell
.\tools\Build-Desktop.ps1 -Publish
```

The output is `artifacts/desktop/windows/`. The target computer needs the
.NET 9 Desktop Runtime. Copy **all files** from that output directory into
your own Pharaoh + Cleopatra 2.1 game directory, alongside the original data,
then launch **Nesutera.exe**.

## Game data location

Nesutera uses **only the directory containing Nesutera.exe** as the game root.
The current working directory, command-line paths, registry entries and
machine-specific installation paths are not used to find another installation.

The existing game layout must remain intact:

```text
Game/
  Nesutera.exe
  Nesutera.dll
  Pharaoh.Assets.dll
  Pharaoh.Engine.dll
  Pharaoh_Text.eng
  Pharaoh_MM.eng
  Pharaoh.inf
  campaign.txt
  mission1.pak
  Data/
  Save/
  Maps/
  ...other original game files and published runtime files
```

Settings, family profiles, saves and custom maps use `Pharaoh.inf`, `Save/`
and `Maps/` under the same root. That location must be writable to save changes.
Original game archives, movies, audio, maps and saved games are not included in
this repository; a user-owned copy of the game is required.

## Current implementation

The code includes the legacy menus and campaign flow, original asset and save
formats, city rendering, construction previews, housing and migration,
floodplain simulation, labor recruitment, and the initial food and safety
systems. The full campaign and all specialized building behavior are still
under development.

The repository contains the application and shared engine/assets projects.
Local research output, captures, diagnostic projects and build output are
excluded.

## Roadmap

### Phase 1 — Faithful reimplementation

- Reconstruct the original game systems and logic in C#.
- Preserve the behavior of **Pharaoh** and **Cleopatra**, including gameplay rules, simulation outcomes, and interactions.
- Prioritize compatibility and behavioral accuracy over feature changes.
- Build a maintainable foundation for testing, debugging, and future development.

### Phase 2 — Quality-of-life (QOL) edition

Once the classic experience is faithfully reproduced, develop a separate **QOL version** guided by modern game standards. Potential improvements include a more flexible interface, better usability and accessibility, modern display support, and smoother controls.

These enhancements should be clearly separated from the faithful version so that the **original gameplay experience remains available**.

## Guiding principle

> **Preserve the game. Modernize the code. Improve the experience later.**

## Disclaimer

Nesutera is an independent, fan-made project and is not affiliated with or endorsed by the original game's creators or rights holders. **Pharaoh**, **Cleopatra: Queen of the Nile**, and their original assets remain the property of their respective owners.
