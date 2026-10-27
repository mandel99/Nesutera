# Nesutera

**Nesutera** is a project to reimplement the classic city-building game **Pharaoh** and its expansion, **Cleopatra: Queen of the Nile**, in modern **C#**.

The primary goal is to preserve **all original game behavior** as faithfully as possible while rebuilding the underlying systems in clean, readable, maintainable code. This is intended to be a faithful reimplementation first—not a gameplay redesign.

## Approach

The original game is being studied through reverse engineering, supported by:

- **Ghidra** — disassembly, decompilation, and analysis of the original executable.
- **REA** — assistance with reverse-engineering research and understanding game systems.
- **Codex** — assistance with C# implementation, code organization, refactoring, and verification.

The original game's behavior is the reference for the new implementation. The aim is to reproduce its rules, simulation, mechanics, interactions, and even relevant quirks, while replacing the legacy implementation with a modern C# codebase.

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
