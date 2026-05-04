# Glamorous Terror

> **To Otter:** I hereby give full permission for any and all changes in this fork to be ported back into upstream Glamourer, and I'd genuinely thank you if you did. No credit required.

**Glamourer but with extra features.**
A custom fork of [Glamourer](https://github.com/Ottermandias/Glamourer) with an added features, in-game context menus, and an immersive glamour dresser.

`/glamourer` (or `/glam`, `/glamorous`, `/gt`) - toggle the main window

---

## Why Glamorous Terror?

This is just base Glamourer with features people have asked for over time.

The fork exists because a [wildcard-automation contribution](https://github.com/Ottermandias/Glamourer/pull/118) I attempted to original Glamourer was rejected as "abuse," despite being built entirely on systems Glamourer already exposes.
Rather than argue, I started maintaining a parallel build where requested features can be accessed.
That's it, thank you for coming to my TED Talk.

---

## Added Features

**Game Context Menu**
Right-click any character for direct glamour, design, and dresser actions inside the FFXIV context menu.

**Preview-on-Hover**
Hover any equipment, dye, or customization combo entry to see the change live on your character before clicking. Reverts instantly when the popup closes or you move away.

**Immersive Dresser**
Floating three-panel glamour editor with optional game-UI hide, camera height slider, free-cam button, disable-first-person toggle, design clipboard/save/undo, and panel locking. Open via right-click on your own character or `/gt dresser`.

**Icon-Grid Equipment Drawer**
Optional icon picker that replaces the text-list combo with a multi-row icon grid - pinnable, scroll-persistent, configurable rows, and groupable by base model.

**Owned-Only Filter**
Filter equipment and customize combos to items your character has actually unlocked. Sources are individually toggleable (inventory, glamour dresser, armoury, unlocks).

**Cross-Language Equipment Search**
Type in any supported game language (EN/DE/FR/JA) and search any other - useful for tracking down items by their localized name without changing your client locale.

**Equipment Name Language Override**
Display equipment names in a different language than your client, independent of search language.

**Wildcard Automation**
Extends automation triggers with wildcard / pattern matching for character identifiers, for setups that span multiple alts or world-roaming targets.

**Character Rotation**
Rotate the character preview directly inside the Glamourer window.

**Fun Modes**
Remove the *completely unecessary* gatekeeping of features behind

---

## Requirements

- [Penumbra](https://github.com/xivdev/Penumbra) installed and active

## Installation

Add the following custom plugin repository in Dalamud Settings:

**Sea of Terror:**
```
https://raw.githubusercontent.com/MTVirux/SeaOfTerror/master/repo.json
```

> **This is not the original Glamourer.** Do not run both side-by-side - pick one.

## Commands

| Command | Description |
|---------|-------------|
| `/glamourer`, `/glam`, `/glamorous`, `/gt` | Toggle the main Glamourer window |
| `/glamour` | Apply designs, items, customizations, automation, and more - use `help` or `?` for the full argument list |
| `/glamour dresser` | Open the Immersive Dresser on your own character |
