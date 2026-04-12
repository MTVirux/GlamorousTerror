# GlamorousTerror — Custom Feature Documentation

GlamorousTerror is a custom fork of [Glamourer](https://github.com/Ottermandias/Glamourer) for FFXIV. This document describes every fork-specific feature, its implementation, data flow, and configuration.

---

## Table of Contents

1. [Context Menu](#1-context-menu)
2. [Preview-on-Hover](#2-preview-on-hover)
3. [Wildcard Automation Targets](#3-wildcard-automation-targets)
4. [Fun Modes](#4-fun-modes)
5. [Equipment Name Language](#5-equipment-name-language)
6. [Cross-Language Equipment Search](#6-cross-language-equipment-search)

---

## 1. Context Menu

Right-clicking any player character in-game adds a **"Glamorous Terror"** entry to the native FFXIV context menu. Clicking it opens a rich ImGui popup with appearance transfer, design application, and live preview.

### User-Facing Functionality

- **Import as Design** — saves the right-clicked target's appearance as a new design
- **Apply Target → Self** — copy equipment, appearance, or full design from the target to the player (per-slot or all)
- **Apply Self → Target** — copy from the player to the target
- **Apply Design → Target** — browse the full design file tree and apply any saved design
- **Revert to Automation / Reset to Game State** — undo overrides on the target
- All submenu items show a **live preview on hover** (see [Preview-on-Hover](#2-preview-on-hover))
- Hold **Shift** to keep the menu open after an action
- Equipment/appearance items display match indicators when source and target already match

### Implementation

| File | Role |
|------|------|
| `Glamourer/Interop/ContextMenuService.cs` | Hooks Dalamud's `IContextMenu`, adds "Glamorous Terror" to character right-click menus |
| `Glamourer/Gui/CharacterPopupMenu.cs` | ~850-line custom ImGui popup with all menu logic |
| `Glamourer/Services/PreviewService.cs` | Preview engine shared with equipment/customization drawers |

**ContextMenuService** implements `IRequiredService`. On `OnMenuOpened`, it filters for character-type context menus, creates a `MenuItem` with `PrefixChar = 'G'`, and fires `_popupMenu.Open(actor, name)` on click.

**CharacterPopupMenu** registers on `_uiBuilder.Draw += OnDraw` and renders an ImGui popup (`Im.Popup.Begin("GlamorousTerrorPopup")`). Key draw methods:

- `DrawEquipmentSubmenu()` — iterates 14 equipment slots (Head → Facewear) and calls `PreviewService.StartEquipment()` on hover
- `DrawAppearanceSubmenu()` — iterates 19 appearance groups and calls `PreviewService.StartAppearance()` on hover
- `DrawDesignSubmenu()` — recursively walks `DesignFileSystem` tree and calls `PreviewService.StartDesign()` on hover
- `ApplyPreviewPermanently()` — clears preview state, then runs the action with `IsFinal = true`
- `CheckAndEndPreview()` — restores original state when nothing is hovered

### Data Flow

```
Right-click character
  → Dalamud IContextMenu hook → ContextMenuService.OnMenuOpened()
    → Creates MenuItem("Glamorous Terror", PrefixChar='G')
  → User clicks menu entry
    → ContextMenuService → CharacterPopupMenu.Open(actor, name)
  → Next ImGui frame
    → CharacterPopupMenu.OnDraw() → Im.Popup.Begin()
      → DrawMenuContent() → submenu rendering
  → User hovers submenu item
    → PreviewService.StartEquipmentPreview / StartAppearancePreview / etc.
      → StateManager.ApplyDesign(state, tempDesign, IsFinal=false) → game memory
  → User leaves hover
    → PreviewService.EndPreview() → RestoreToOriginalState()
  → User clicks item
    → ApplyPreviewPermanently() → action with IsFinal=true (permanent change)
```

### Configuration

| Property | Type | Default | Location |
|----------|------|---------|----------|
| `EnableGameContextMenu` | `bool` | `true` | `Configuration.cs` line 56 |

Controlled in the Settings tab via `ContextMenuService.Enable()` / `Disable()`.

---

## 2. Preview-on-Hover

When hovering over items in any UI combo or popup, the character's appearance **updates in real-time** to preview the change. Moving away restores the original appearance. Clicking an item makes the change permanent.

### Coverage

| UI Element | Preview Type | Notes |
|------------|-------------|-------|
| Equipment slot combos (Head, Body, etc.) | Single item | All 14 equipment slots |
| Bonus item combos (Glasses, etc.) | Single bonus item | All bonus item flags |
| Weapon combos (Main Hand, Off Hand) | Single item | Both weapon slots |
| Customization icon popups (Face, Hairstyle, Face Paint) | Single customization | Requires CTRL held |
| Customization list combos (Eye shape, Nose, etc.) | Single customization | Immediate on hover |
| Customization color popups (Hair color, Eye color, etc.) | Single customization | Immediate on hover |
| Context menu equipment/appearance items | Equipment/Appearance | See [Context Menu](#1-context-menu) |
| Context menu design tree | Full design | Previews entire saved design |

### Implementation — PreviewService (~1010 lines)

The central preview engine in `Glamourer/Services/PreviewService.cs` contains two key types:

**`PreviewState`** — Tracks all preview state:

| Property | Type | Purpose |
|----------|------|---------|
| `IsActive` | `bool` | Whether any preview is in progress |
| `Type` | `PreviewType` enum | Which kind of preview (SingleItem, SingleCustomization, Equipment, Appearance, Design, etc.) |
| `TargetState` | `ActorState?` | The actor being previewed |
| `OriginalData` | `DesignData` | Snapshot of actor state before preview started |
| `OriginalMaterials` | material data | Material state snapshot for restoration |
| `ToSelf` | `bool` | Whether previewing changes to self |
| `PopupActiveThisFrame` | `bool` | Per-frame flag set by popup drawing code |
| `ActivePopupType` | `PopupType` | Icon, List, or Color |
| `PopupHoveredIndex` | `int?` | Which option is being hovered |
| `PopupHoveredValue` | `CustomizeValue` | The customize value of the hovered option |
| `PopupSelectionMade` | `bool` | Whether the user clicked to select |
| `RequiresCtrl` | `bool` | Whether CTRL must be held for this preview |

**`PreviewService`** — Methods organized by lifecycle:

- **Start**: `StartSingleItemPreview()`, `StartSingleBonusItemPreview()`, `StartSingleCustomizationPreview()`, `StartSingleStainPreview()`
- **Apply**: `PreviewSingleItem()`, `PreviewSingleBonusItem()`, `HandleCustomizationPopupFrame()`
- **Restore**: `RestoreSingleValuePreview()`, `EndSingleValuePreview()`, `EndCustomizationPopupFrame()`
- **Query**: `IsSingleItemPreview(slot)`, `IsSingleBonusItemPreview(slot)`, `IsSingleCustomizationPreview(index)`

### Implementation — Equipment Drawer

| File | Key Addition |
|------|-------------|
| `Glamourer/Gui/Equipment/BaseItemCombo.cs` | `HoveredItem`, `IsPopupOpen`, `ItemSelected`, `ResetSelection()` properties |
| `Glamourer/Gui/Equipment/EquipmentDrawer.cs` | `ApplyHoverPreview(StateManager, ActorState)` method (~100 lines) |

**BaseItemCombo** tracks hover state per combo:

- `HoveredItem` (`EquipItem?`) — set in `DrawItem()` when `Im.Item.Hovered()` is true after a selectable
- `IsPopupOpen` (`bool`) — set to `true` in `PreDrawList()` (which is only called when the popup is actually rendering), reset to `false` at the start of each `Draw()` call
- `ItemSelected` (`bool`) — set to `true` when an item is clicked

**EquipmentDrawer.ApplyHoverPreview()** runs every frame after drawing equipment:

1. Iterates all equipment combos, bonus combos, and weapon combos
2. For any combo with `IsPopupOpen == true`:
   - Calls `PreviewService.StartSingleItemPreview(state, slot)` if not already previewing that slot
   - If `HoveredItem.HasValue`, calls `PreviewService.PreviewSingleItem(state, slot, item)`
3. If no combo is open and a single-item preview is active:
   - Checks if any combo had `ItemSelected` (user clicked)
   - Calls `EndSingleValuePreview(wasSelectionMade)` — restores original if no selection was made
4. If a combo is open but nothing is hovered: calls `RestoreSingleValuePreview()`

### Implementation — Customization Drawer

| File | Key Addition |
|------|-------------|
| `CustomizationDrawer.cs` | `PreviewService` constructor param, public `ApplyHoverPreview()` dispatcher |
| `CustomizationDrawer.Icon.cs` | `_iconPopupOpen/Index` state, hover tracking in `DrawIconPickerPopup()`, `ApplyIconHoverPreview()` |
| `CustomizationDrawer.Simple.cs` | `_listPopupOpen/Index` state, hover tracking in `ListCombo0()`/`ListCombo1()`, `ApplyListHoverPreview()` |
| `CustomizationDrawer.Color.cs` | `_colorPopupOpen/Index` state, hover tracking in `DrawColorPickerPopup()`, `ApplyColorHoverPreview()` |

Each popup type follows the same pattern:

1. **When popup opens**: Set `previewState.PopupActiveThisFrame = true`, set `ActivePopupType`, track `_*PopupOpen = true` and `_*PopupIndex = _currentIndex`
2. **Per item in popup**: After drawing selectable/button, check `Im.Item.Hovered()` → set `previewState.PopupHoveredIndex` and `PopupHoveredValue`
3. **On selection**: Set `previewState.PopupSelectionMade = true`
4. **ApplyXHoverPreview()**: If `PopupActiveThisFrame` and popup type matches:
   - Ensure `StartSingleCustomizationPreview()` is called for the correct index
   - Call `HandleCustomizationPopupFrame(state, index, hoveredIndex, hoveredValue, ctrlHeld)` — applies preview if hovering (and CTRL held for face/hairstyle/facepaint)
   - Clear `PopupActiveThisFrame` flag
5. **On popup close**: `_*PopupOpen` is still true but `PopupActiveThisFrame` is false → call `EndCustomizationPopupFrame()` → restore original, reset state

### Wiring in ActorPanel

In `Glamourer/Gui/Tabs/ActorTab/ActorPanel.cs`:

- `DrawCustomizationsHeader()` calls `_customizationDrawer.ApplyHoverPreview(_stateManager, _selection.State!)` after drawing customizations
- `DrawEquipmentHeader()` calls `_equipmentDrawer.ApplyHoverPreview(_stateManager, _selection.State!)` after drawing equipment and drag-drop tooltip

### Data Flow (Equipment Example)

```
Equipment combo opened
  → BaseItemCombo.PreDrawList() → IsPopupOpen = true
  → User hovers item row → DrawItem() → Im.Item.Hovered() → HoveredItem = item
  → EquipmentDrawer.ApplyHoverPreview() detects open combo
    → PreviewService.StartSingleItemPreview(state, slot) — saves original
    → PreviewService.PreviewSingleItem(state, slot, hoveredItem)
      → StateManager.ChangeItem(state, slot, item, ApplySettings.Manual) → game memory updated
  → User moves mouse to different item
    → HoveredItem changes → PreviewSingleItem with new item
  → User moves off all items
    → HoveredItem = null → RestoreSingleValuePreview() → restore to saved original
  → User clicks item
    → BaseItemCombo returns true, ItemSelected = true, IsPopupOpen = false
    → Next frame: EndSingleValuePreview(wasSelectionMade=true) → keep new value
```

### Data Flow (Customization Example)

```
Icon popup opened (e.g. hairstyle)
  → DrawIconPickerPopup() → previewState.PopupActiveThisFrame = true
  → _iconPopupOpen = true, _iconPopupIndex = currentIndex
  → User hovers icon button → Im.Item.Hovered() → PopupHoveredIndex/Value set
  → ApplyIconHoverPreview() detects popup active for icon type
    → StartSingleCustomizationPreview(state, index, requiresCtrl=true)
    → HandleCustomizationPopupFrame(state, index, hoveredIndex, value, ctrlHeld=Im.Io.KeyControl)
      → If CTRL held: ChangeCustomize(state, index, value) → game memory
      → If CTRL not held: RestoreSingleValuePreview()
  → Popup closes (Im.Popup.CloseCurrent or user clicks outside)
    → _iconPopupOpen still true, but PopupActiveThisFrame = false
    → ApplyIconHoverPreview() → EndCustomizationPopupFrame() → restore + End()
```

---

## 3. Wildcard Automation Targets

Extends the automation system to allow **wildcard patterns** (`*`) in character names for auto-design sets.

### User-Facing Functionality

- Pattern matching in character names: `Tank*@Excalibur`, `*Healer`, `Raid Alt*`
- `*` matches zero or more characters
- Case-insensitive matching on raw UTF-8 bytes
- Supports Player, Owned, and Retainer identifier types
- World matching: exact world or `AnyWorld`
- Falls back to exact match first for performance

### Implementation

All code is in `Glamourer/Automation/AutoDesignApplier.cs` (~70 lines added):

**`TryGettingSetExactOrWildcard(ActorIdentifier)`** — Entry point replacing the original `GetPlayerSet`:

1. Attempts exact match via `EnabledSets.TryGetValue(identifier)` — fast path
2. On miss, iterates all `EnabledSets` looking for identifiers whose `PlayerName` contains `*`
3. For each wildcard candidate:
   - Checks type compatibility (Player, Owned, Retainer)
   - Checks world match (exact or `AnyWorld`)
   - Calls `MatchesWildcard(identifier.PlayerName, key.PlayerName)`
4. Returns first match or `null`

**`MatchesWildcard(ByteString name, ByteString pattern)`** — Unsafe entry point:

- Delegates to `MatchesWildcardInternal` with raw byte pointers and lengths

**`MatchesWildcardInternal(byte* name, int nameLen, byte* pattern, int patternLen)`** — Classic wildcard matching algorithm with backtracking:

- Maintains `nameIdx`, `patternIdx`, `starIdx` (last `*` position), `matchIdx` (backtrack point)
- On `*`: records position, advances pattern
- On mismatch: backtracks to last `*` position, advances `matchIdx`
- Case-insensitive via `AsciiToLower(byte)` which converts A-Z to a-z inline

**`AsciiToLower(byte)`** — Single-expression helper: `b >= (byte)'A' && b <= (byte)'Z' ? (byte)(b + 32) : b`

### Data Flow

```
Character loads → AutoDesignApplier.GetPlayerSet(identifier)
  → EnabledSets.TryGetValue(identifier) → exact match? → return set
  → No exact match → iterate all EnabledSets
    → For each key containing '*':
      → Type compatible? (Player/Owned/Retainer)
      → World matches? (exact or AnyWorld)
      → MatchesWildcard(identifier.PlayerName, key.PlayerName)
        → MatchesWildcardInternal (byte-level, case-insensitive, backtracking)
        → Match → return set
  → No match → return null → no automation applied
```

---

## 4. Fun Modes

23 cosmetic transformation modes that modify visible players' appearances in real-time. Replaces upstream Glamourer's SHA-256 passphrase system with direct checkbox toggles.

### Difference from Upstream

| Aspect | Upstream Glamourer | GlamorousTerror |
|--------|-------------------|-----------------|
| Storage | `List<(string Code, bool Enabled)> Codes` — plaintext passphrase list | `CodeFlag EnabledCheats` — direct bitmask |
| Unlocking | User types secret passphrase → SHA-256 hash compared to hardcoded digests | All modes visible as labeled checkboxes |
| UI | Text input + hints system (capital count, punctuation, riddle) | Checkbox list with names and descriptions |
| Extra modes | — | `AllMale` (0x002000), `AllFemale` (0x004000) |
| Mutual exclusivity | `GenderCodes` not defined (only `SixtyThree`) | `GenderCodes = AllMale \| AllFemale \| SixtyThree` |

### Available Modes

| Flag | Name | Category | Effect |
|------|------|----------|--------|
| `Clown` | Random Dyes | Dye | Randomizes dyes on every armor piece |
| `Emperor` | Random Clothing | Gear | Randomizes equipment per slot |
| `Individual` | Random Customizations | — | Randomizes all customize values (except Face) |
| `Dwarf` | Player Dwarf Mode | Size | Player = min height, others = max height |
| `Giant` | Player Giant Mode | Size | Player = max height, others = min height |
| `OopsHyur` – `OopsViera` | All [Race] | Race | Changes all players to specified race |
| `AllMale` | All Male | Gender | Changes all players to male **[GT-only]** |
| `AllFemale` | All Female | Gender | Changes all players to female **[GT-only]** |
| `SixtyThree` | Invert Genders | Gender | Flips male ↔ female for all players |
| `Shirts` | Show All Items Unlocked | — | Removes unavailable tint on locked items in Unlocks tab |
| `World` | Job-Appropriate Gear | Gear+Dye | Sets NPCs to job-appropriate gear and weapons |
| `Elephants` | Everyone Elephants | Gear+Dye | Elephant costume (item 6133) with random pink stains |
| `Crown` | Clown Mentors | — | Mentors get clown outfit (item 6117) |
| `Dolphins` | Everyone Namazu | Gear+Dye | Namazu head (item 5040) + random costume bodies |
| `Face` | Debug Mode (Face) | Full | Replace with random NPC appearance |
| `Manderville` | Debug Mode (Manderville) | Full | Replace with Hildi/Manderville NPC appearance |
| `Smiles` | Debug Mode (Smiles) | Full | Replace with Smile variants |

Debug modes (Face, Manderville, Smiles) are hidden from the UI.

### Mutual Exclusivity

Only one mode per category can be active:

- **Dye**: Clown, World, Elephants, Dolphins
- **Gear**: Emperor, World, Elephants, Dolphins
- **Race**: One of eight race codes
- **Gender**: AllMale, AllFemale, SixtyThree
- **Size**: Dwarf, Giant
- **Full**: Face, Manderville, Smiles (mutually exclusive with nearly everything)

### Implementation

| File | Lines | Role |
|------|-------|------|
| `Glamourer/Services/CodeService.cs` | ~210 | `CodeFlag` enum, `Toggle()`, `DisableAll()`, `GetMutuallyExclusive()`, `GetName()`, `GetDescription()` |
| `Glamourer/State/FunModule.cs` | ~440 | `IRequiredService` applying transformations on character load/equip/weapon changes |
| `Glamourer/Gui/Tabs/SettingsTab/CodeDrawer.cs` | ~95 | Settings UI: checkbox list, Disable All button, Who Am I / Who Is That clipboard buttons |
| `Glamourer/State/FunEquipSet.cs` | — | Festival-specific outfit definitions |

**CodeService** — Reads/writes `Configuration.EnabledCheats` directly:

- `Toggle(CodeFlag flag, bool enable)` — applies mutual exclusivity via `GetMutuallyExclusive()` then saves
- `DisableAll()` — sets `EnabledCheats = 0` and saves
- `Enabled(CodeFlag)` / `AnyEnabled(CodeFlag)` / `Masked(CodeFlag)` — query methods

**FunModule** — Hooks into `StateListener` and modifies character data:

- `ApplyFunOnLoad(actor, armor[], customize)` — main entry point on character load:
  1. `ValidFunTarget?` (must be PC, not transformed, ModelCharaId = 0)
  2. `ApplyFullCode` — NPC replacement from weighted random pools
  3. `SetRace` — maps CodeFlag to target clan via `ChangeClan()`
  4. `SetGender` — `AllMale` → `ChangeGender(Male)`, `AllFemale` → `ChangeGender(Female)`, `SixtyThree` → flip
  5. `RandomizeCustomize` — randomizes all non-face indices
  6. `SetSize` — Dwarf/Giant based on actor index
  7. Festival gear or code-specific gear
- `ApplyFunToSlot(actor, armor, slot)` — individual equipment changes
- `ApplyFunToWeapon(actor, weapon, slot)` — weapon changes
- `WhoAmI()` / `WhoIsThat()` — export actual in-game appearance (including fun mode effects) as clipboard design

**CodeDrawer** — UI in Settings tab:

- `DrawFeatureToggles()` — iterates all `CodeFlag.Values` except debug modes, draws checkbox per flag
- `DrawCopyButtons()` — "Who am I?!?" and "Who is that!?!" buttons
- `ForceRedrawAll()` — after toggling, iterates `ActorObjectManager.Objects` and calls `StateManager.ReapplyState()` on each valid actor

### Festival System

`FunModule` also includes an automatic festival system:

- **Halloween** (Oct 31, Nov 1): Spooky costumes
- **Christmas** (Dec 24–26): Holiday outfits
- **April Fools** (Apr 1): Joke gear
- `Configuration.FestivalMode` (`FestivalSetting.Undefined` / enabled / disabled)
- `Configuration.LastFestivalPopup` — tracks when the user last saw the permission notification

### Configuration

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `EnabledCheats` | `CodeService.CodeFlag` | `0` | Active fun mode bitmask |
| `FestivalMode` | `FestivalSetting` | `Undefined` | Festival costume behavior |
| `LastFestivalPopup` | `DateOnly` | `MinValue` | Last festival notification date |

---

## 5. Equipment Name Language

Override the display language for equipment item names throughout the plugin independent of the game's language setting.

### User-Facing Functionality

- Select from: **Game Default**, **English**, **Japanese**, **German**, **French**
- All item combos, design panels, and search results reflect the chosen language
- Setting accessible in the Settings tab under equipment options

### Implementation

| File | Lines | Role |
|------|-------|------|
| `Glamourer/Services/ItemNameService.cs` | ~236 | Language-specific Lumina sheet loading, name caching |
| `Glamourer/Gui/Tabs/SettingsTab/SettingsTab.cs` | — | `DrawEquipmentLanguageSettings()` combo UI |
| `Glamourer/Config/Configuration.cs` | — | `EquipmentNameLanguage` property |

**`EquipmentNameLanguage`** enum: `GameDefault`, `English`, `Japanese`, `German`, `French`

**ItemNameService** — `IService` that:

- Loads all 4 language `ExcelSheet<Item>` sheets from Lumina in constructor
- `GetItemName(EquipItem)` / `GetItemName(uint itemId, string fallback)` — returns name in configured language
- Uses a per-language `Dictionary<uint, string>` cache (`_nameCache`) to avoid repeated Lumina lookups
- `CheckLanguageChange()` — detects config change, clears cache and refreshes active sheet
- `ClearCache()` — called from settings UI when language changes

**SettingsTab.DrawEquipmentLanguageSettings()** — renders a language selection combo and calls `ItemNameService.ClearCache()` on change.

### Data Flow

```
User selects language in Settings
  → Configuration.EquipmentNameLanguage = selected
  → ItemNameService.ClearCache() → _nameCache.Clear()
  → Next item combo draw → ItemNameService.GetItemName(item)
    → Check _nameCache → miss
    → Load row from ExcelSheet<Item> for selected language
    → Cache result → return localized name
```

### Configuration

| Property | Type | Default |
|----------|------|---------|
| `EquipmentNameLanguage` | `EquipmentNameLanguage` | `GameDefault` |

---

## 6. Cross-Language Equipment Search

Search for equipment items in **any language** regardless of the current display language setting.

### User-Facing Functionality

- Enable in Settings alongside the language selector
- When active, typing in any equipment combo filter checks all 4 language variants of each item name
- Example: With display set to English, typing "鉄" (Japanese for "iron") will find iron equipment

### Implementation

Shares code with the language system in `ItemNameService`:

- `MatchesAnyLanguage(EquipItem item, string filter)` — checks:
  1. The display-language name (fast path)
  2. All 4 language sheets via `GetAllLanguageNames(uint itemId)` — returns `string[]` of 4 names
- `_allLanguageNamesCache` (`Dictionary<uint, string[]>`) — caches all-language lookups separately from single-language lookups
- `ClearCache()` clears both caches

The feature integrates into the existing `BaseItemCombo` filter system — when `CrossLanguageEquipmentSearch` is enabled, the filter's `WouldBeVisible` check uses `MatchesAnyLanguage` instead of single-language comparison.

### Configuration

| Property | Type | Default |
|----------|------|---------|
| `CrossLanguageEquipmentSearch` | `bool` | `false` |

---

## Configuration Summary

All GlamorousTerror-specific properties in `Glamourer/Config/Configuration.cs`:

| Property | Type | Default | Feature |
|----------|------|---------|---------|
| `EnableGameContextMenu` | `bool` | `true` | Context Menu |
| `EnabledCheats` | `CodeService.CodeFlag` | `0` | Fun Modes |
| `FestivalMode` | `FestivalSetting` | `Undefined` | Fun Modes (festivals) |
| `LastFestivalPopup` | `DateOnly` | `MinValue` | Fun Modes (festivals) |
| `EquipmentNameLanguage` | `EquipmentNameLanguage` | `GameDefault` | Equipment Language |
| `CrossLanguageEquipmentSearch` | `bool` | `false` | Cross-Language Search |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│                   User Interface                     │
│  ┌──────────────┐  ┌────────────┐  ┌──────────────┐ │
│  │ ActorPanel   │  │ContextMenu │  │ CodeDrawer   │ │
│  │ (Equipment + │  │ Service +  │  │ (Fun Modes   │ │
│  │  Customize)  │  │ PopupMenu  │  │  Settings)   │ │
│  └──────┬───────┘  └─────┬──────┘  └──────┬───────┘ │
│         │                │                 │         │
│  ┌──────▼────────────────▼─────┐  ┌───────▼───────┐ │
│  │      PreviewService         │  │  CodeService   │ │
│  │  (Live preview state mgmt)  │  │  (Flag toggle) │ │
│  └──────┬──────────────────────┘  └───────┬───────┘ │
│         │                                 │         │
│  ┌──────▼─────────────────────────────────▼───────┐ │
│  │              StateManager                       │ │
│  │  (Actor state, design application, game memory) │ │
│  └──────┬──────────────────────────────────────────┘ │
│         │                                            │
│  ┌──────▼──────────┐  ┌───────────────────────┐     │
│  │   FunModule     │  │  AutoDesignApplier    │     │
│  │  (Transforms    │  │  (Wildcard matching   │     │
│  │   on load)      │  │   + auto-application) │     │
│  └─────────────────┘  └───────────────────────┘     │
│                                                      │
│  ┌──────────────────────────────────────────────┐   │
│  │            ItemNameService                    │   │
│  │  (Language override + cross-language search)  │   │
│  └──────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
```
