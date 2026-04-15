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
7. [Owned-Only Combo Filter](#7-owned-only-combo-filter)

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
| Stain/dye combos | Single stain | All slots, per-stain-index. Immediate on hover |
| Customization icon popups (Face, Hairstyle, Face Paint) | Single customization | Requires CTRL held |
| Customization list combos (Eye shape, Nose, etc.) | Single customization | Requires CTRL held |
| Customization color popups (Hair color, Eye color, etc.) | Single customization | Requires CTRL held |
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

- **Start**: `StartSingleItemPreview()`, `StartSingleBonusItemPreview()`, `StartSingleCustomizationPreview(state, index, requiresCtrl)`, `StartSingleStainPreview()`
- **Apply**: `PreviewSingleItem()`, `PreviewSingleBonusItem()`, `PreviewSingleStain()`, `HandleCustomizationPopupFrame(state, index, hoveredIndex, hoveredValue, ctrlHeld)`
- **Restore**: `RestoreSingleValuePreview()`, `EndSingleValuePreview(wasSelectionMade)`, `EndCustomizationPopupFrame()`
- **Query**: `IsSingleItemPreview(slot)`, `IsSingleBonusItemPreview(slot)`, `IsSingleCustomizationPreview(index)`, `IsSingleStainPreview(slot, stainIndex)`

**Key implementation detail for `IsSingleStainPreview`**: Must check `IsActive && Type == PreviewType.SingleStain` (not just slot/index match). Without this, stale values after `End()` can falsely match.

### Implementation — Equipment Drawer

| File | Key Addition |
|------|-------------|
| `Glamourer/Gui/Equipment/BaseItemCombo.cs` | `HoveredItem`, `IsPopupOpen`, `ItemSelected`, `ResetSelection()` properties |
| `Glamourer/Gui/Equipment/GlamourerColorCombo.cs` | `HoveredStain`, `IsPopupOpen`, `StainSelected`, `ResetSelection()`, `ResetFrameState()` properties |
| `Glamourer/Gui/Equipment/EquipmentDrawer.cs` | `ApplyHoverPreview(StateManager, ActorState)` method (~130 lines), stain slot tracking fields |

**BaseItemCombo** tracks hover state per combo:

- `HoveredItem` (`EquipItem?`) — set in `DrawItem()` when `Im.Item.Hovered()` is true after a selectable
- `IsPopupOpen` (`bool`) — set to `true` in `PreDrawList()` (which is only called when the popup is actually rendering), reset to `false` at the start of each `Draw()` call
- `ItemSelected` (`bool`) — set to `true` when an item is clicked

**GlamourerColorCombo** tracks hover state for stain/dye previews:

- `HoveredStain` (`StainId?`) — set in overridden `DrawItem()` when `Im.Item.Hovered()` is true after the button
- `IsPopupOpen` (`bool`) — set to `true` in overridden `PreDrawList()`, **NOT** reset per `Draw()` call
- `StainSelected` (`bool`) — set to `true` when `Draw()` returns `true` (selection made)
- `ResetFrameState()` — **must be called once per frame** from `EquipmentDrawer.Prepare()` to clear `IsPopupOpen` and `HoveredStain`

**CRITICAL: Stain combo clobbering pattern** — There is only **one** `_stainCombo` instance shared across all equipment slots. `Draw()` is called many times per frame (once per stain index per slot). If `IsPopupOpen` were reset in `Draw()`, the slot that has its popup open would set `true`, then the next slot's `Draw()` would immediately reset it to `false`. Solution: `ResetFrameState()` resets once at start of frame in `Prepare()`, and `PreDrawList()` only ever sets `true`.

**EquipmentDrawer stain slot tracking**:

- `_stainPreviewSlot` (`EquipSlot`) — which equipment slot the stain popup belongs to
- `_stainPreviewIndex` (`int`) — which stain index within that slot (for multi-stain items)
- `_stainPreviewValid` (`bool`) — whether the above values are meaningful (guards against "Dye All Slots" combo)

In `DrawStain()`, slot/index are captured on the **false→true transition** of `_stainCombo.IsPopupOpen` (not just when `IsPopupOpen` is true). This prevents later `DrawStain()` calls for other slots from overwriting the values. `_stainPreviewValid` is set `true` only in `DrawStain()`, not in `DrawAllStain()`.

**EquipmentDrawer.ApplyHoverPreview()** runs every frame after drawing equipment:

1. Iterates all equipment combos — if any combo's popup is open, starts/applies item preview, `return`s early
2. Iterates all bonus combos — same pattern with bonus item preview, `return`s early if open
3. Iterates all weapon combos — same pattern, `return`s early if open
4. Checks stain combo — if `_stainCombo.IsPopupOpen && _stainPreviewValid`:
   - Calls `StartSingleStainPreview(state, slot, stainIndex)` then `PreviewSingleStain()` if hovering
   - Handles `StainSelected` → `EndSingleValuePreview(wasSelectionMade: true)`
   - `return`s early
5. **Fall-through** (no popup open): Only ends preview if `State.Type is PreviewType.SingleItem or PreviewType.SingleStain` — **must NOT end `SingleCustomization` previews** since the customization drawer manages those separately

**CRITICAL: Cross-drawer interference** — `EquipmentDrawer.ApplyHoverPreview()` runs AFTER `CustomizationDrawer.ApplyHoverPreview()` in `ActorPanel`. If the equipment drawer's fall-through unconditionally called `RestoreSingleValuePreview()`, it would kill any active customization preview every frame. The type guard prevents this.

### Implementation — Customization Drawer

| File | Key Addition |
|------|-------------|
| `CustomizationDrawer.cs` | `PreviewService` constructor param, public `ApplyHoverPreview()` dispatcher, popup flag reset in `DrawInternal()` |
| `CustomizationDrawer.Icon.cs` | `_iconPopupOpen/Index/HoveredValue/SelectionMade` state, hover tracking in `DrawIconPickerPopup()`, `ApplyIconHoverPreview()` |
| `CustomizationDrawer.Simple.cs` | `_listPopupOpen/Index/HoveredValue/SelectionMade` state, hover tracking in `ListCombo0()`/`ListCombo1()`, `ApplyListHoverPreview()` |
| `CustomizationDrawer.Color.cs` | `_colorPopupOpen/Index/HoveredValue/SelectionMade` state, hover tracking in `DrawColorPickerPopup()`, `ApplyColorHoverPreview()` |

**CRITICAL: Popup flag clobbering pattern** — Multiple icon selectors (Face, Hairstyle, etc.) and multiple color pickers are drawn in a loop. Each popup draw method was originally setting `_iconPopupOpen = false` when *its* popup wasn't open. If Face's popup was open, Face's draw set `true`, then Hairstyle's draw immediately set `false`. Solution:

- **Reset all three flags once** at the start of `DrawInternal()`: `_iconPopupOpen = false; _listPopupOpen = false; _colorPopupOpen = false;`
- **Popup draw methods only set `true`**, never `false` — when `Im.Popup.Begin()` returns false, the method just `return`s without touching the flag

Each popup type tracks 4 fields: `_xxxPopupOpen` (bool), `_xxxPopupIndex` (CustomizeIndex), `_xxxHoveredValue` (CustomizeValue), `_xxxSelectionMade` (bool).

**Drawing popups** (same pattern for Icon, List, Color):

1. **When popup renders**: Set `_xxxPopupOpen = true` and `_xxxPopupIndex = _currentIndex`. Reset `_xxxHoveredValue = default`
2. **Per item in popup**: After drawing selectable/button, check `Im.Item.Hovered()` → set `_xxxHoveredValue`
3. **On selection**: Set `_xxxSelectionMade = true`, call `Im.Popup.CloseCurrent()`/`UpdateValue()`

**ApplyHoverPreview() dispatcher** — Uses `if/else if/else` to call exactly ONE sub-method:

```csharp
if (_iconPopupOpen)
    ApplyIconHoverPreview(stateManager, state);
else if (_listPopupOpen)
    ApplyListHoverPreview(stateManager, state);
else if (_colorPopupOpen)
    ApplyColorHoverPreview(stateManager, state);
else
    previewService.EndCustomizationPopupFrame(state);
```

**CRITICAL: Only one EndCustomizationPopupFrame call** — If all three sub-methods were called sequentially with `else { End() }` branches, the two inactive ones would each call `End`, which resets `PopupActiveThisFrame = false`. The third call would then see `false` and **kill the active preview**. The `if/else if/else` pattern ensures exactly one code path runs per frame.

**ApplyXxxHoverPreview()** sub-method pattern (Icon/List/Color are identical except field names):

```csharp
if (_xxxPopupOpen)
{
    previewService.StartSingleCustomizationPreview(state, _xxxPopupIndex, requiresCtrl: true);

    if (_xxxHoveredValue.Value != 0)
        previewService.HandleCustomizationPopupFrame(state, _xxxPopupIndex, (int)_xxxHoveredValue.Value, _xxxHoveredValue, Im.Io.KeyControl);
    else
        previewService.HandleCustomizationPopupFrame(state, _xxxPopupIndex, null, default, Im.Io.KeyControl);

    if (_xxxSelectionMade)
    {
        previewService.MarkPopupSelectionMade();
        previewService.EndSingleValuePreview(wasSelectionMade: true);
        _xxxSelectionMade = false;
    }
}
```

Key parameters:
- `requiresCtrl: true` — All customization previews require CTRL to be held
- `Im.Io.KeyControl` — Passes actual CTRL key state each frame to `HandleCustomizationPopupFrame`
- `HandleCustomizationPopupFrame` logic: if hovering AND (`!RequiresCtrl || ctrlHeld`) → apply preview; otherwise restore original

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
    → HoveredItem = null → StartSingleItemPreview keeps state but no change applied
  → User clicks item
    → BaseItemCombo returns true, ItemSelected = true, IsPopupOpen = false
    → Next frame: EndSingleValuePreview(wasSelectionMade=true) → keep new value
  → User closes popup without selecting
    → IsPopupOpen = false, ItemSelected = false
    → Fall-through: EndSingleValuePreview(wasSelectionMade=false) → restore original
```

### Data Flow (Stain/Dye Example)

```
EquipmentDrawer.Prepare() runs at start of frame
  → _stainCombo.ResetFrameState() → IsPopupOpen=false, HoveredStain=null
  → _stainPreviewValid = false

Stain combo opened for Head slot, stain index 0
  → GlamourerColorCombo.PreDrawList() → IsPopupOpen = true
  → DrawStain() detects false→true transition → _stainPreviewSlot=Head, _stainPreviewIndex=0, _stainPreviewValid=true
  → All subsequent DrawStain() calls for other slots: IsPopupOpen already true, no transition, slot NOT overwritten
  → User hovers a color → DrawItem() → Im.Item.Hovered() → HoveredStain = stainId
  → ApplyHoverPreview() → _stainCombo.IsPopupOpen && _stainPreviewValid
    → PreviewService.StartSingleStainPreview(state, Head, 0)
    → PreviewService.PreviewSingleStain(state, Head, 0, hoveredStain)
      → StateManager.ChangeStains(state, Head, newStains, Manual) → game memory
  → User clicks color
    → Draw() returns true → StainSelected = true
    → ApplyHoverPreview() → EndSingleValuePreview(wasSelectionMade=true)
  → Popup closes without selection
    → IsPopupOpen = false next frame (ResetFrameState)
    → Fall-through: EndSingleValuePreview(wasSelectionMade=false) → restore original stain
```

### Data Flow (Customization Example)

```
CustomizationDrawer.DrawInternal() starts
  → _iconPopupOpen = false; _listPopupOpen = false; _colorPopupOpen = false

Icon popup opened (e.g. hairstyle)
  → DrawIconPickerPopup() → popup renders → _iconPopupOpen = true, _iconPopupIndex = currentIndex
  → Other icon selectors' draw methods → their popup is not open → return without touching _iconPopupOpen
  → User hovers icon button → Im.Item.Hovered() → _iconHoveredValue = custom.Value
  → ApplyHoverPreview() → _iconPopupOpen is true → calls ApplyIconHoverPreview()
    → StartSingleCustomizationPreview(state, index, requiresCtrl=true)
    → HandleCustomizationPopupFrame(state, index, hoveredIndex, value, ctrlHeld=Im.Io.KeyControl)
      → If CTRL held: ChangeCustomize(state, index, value) → game memory
      → If CTRL not held: restore original value
  → List and Color sub-methods NOT called (if/else if/else)
  → EndCustomizationPopupFrame NOT called (popup is still open)
  → Popup closes (Im.Popup.CloseCurrent or user clicks outside)
    → Next frame: DrawInternal resets _iconPopupOpen = false
    → ApplyHoverPreview() → all three flags false → else branch → EndCustomizationPopupFrame()
      → Checks PopupActiveThisFrame (false) → restores original + End()
```

### Known Pitfalls (for future upstream merges)

These bugs were discovered and fixed during integration. Document them to prevent regression:

1. **Popup flag clobbering**: Multiple selectors drawn in a loop each reset the popup flag. Flags must be reset ONCE before the draw loop, and popup methods must only set `true`, never `false`.

2. **Cross-drawer preview interference**: `EquipmentDrawer.ApplyHoverPreview()` runs after `CustomizationDrawer.ApplyHoverPreview()`. Its fall-through must ONLY end equipment-related previews (`SingleItem`, `SingleStain`), not `SingleCustomization`.

3. **Stain combo shared instance**: One `GlamourerColorCombo` is used for all slots. `ResetFrameState()` must run once per frame from `Prepare()`. Slot tracking must use false→true transition detection to capture the correct slot.

4. **`IsSingleStainPreview` must check `IsActive && Type`**: Unlike simple field comparisons, all query methods must gate on `IsActive` and `Type` to prevent stale values from matching after `End()` clears state.

5. **`requiresCtrl: true` and `Im.Io.KeyControl`**: All customization previews pass `requiresCtrl: true` to `StartSingleCustomizationPreview` and pass `Im.Io.KeyControl` (not hardcoded `false`) to `HandleCustomizationPopupFrame`.

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
- Supports **partwise matching**: multi-word searches like "iron chain" require all words to appear in the same language's name (not mixed across languages)
- Special items (Nothing, Smallclothes — ID 0 or ≥ `uint.MaxValue - 512`) are skipped since they have no translations

### Implementation

| File | Lines | Role |
|------|-------|------|
| `Glamourer/Gui/Equipment/BaseItemCombo.cs` | ~89–120 | `ItemFilter` — sealed filter class with `MatchesCrossLanguage()` |
| `Glamourer/Services/ItemNameService.cs` | ~210–245 | `GetAllLanguageNames(uint)` — returns `string[4]` of all language names, cached |
| `Glamourer/Services/ItemNameService.cs` | ~175–205 | `MatchesAnyLanguage(EquipItem, string)` — standalone helper (not used by filter) |
| `Glamourer/Gui/Equipment/EquipmentDrawer.cs` | ~48 | Injects `ItemNameService` into all combo constructors |
| `Glamourer/Gui/Tabs/SettingsTab/SettingsTab.cs` | — | Checkbox UI + `ClearCache()` on toggle |
| `Glamourer/Config/Configuration.cs` | — | `CrossLanguageEquipmentSearch` property |

**ItemFilter integration** — The sealed `ItemFilter` class inside `BaseItemCombo` is a primary-parameter class that receives `ItemNameService`, `Configuration`, and `ItemUnlockManager`:

```csharp
sealed class ItemFilter(ItemNameService itemNameService, Configuration config, ItemUnlockManager itemUnlockManager)
    : PartwiseFilterBase<CacheItem>
```

`WouldBeVisible(in CacheItem, int)` has four checks evaluated in order:

1. **Owned check**: If `config.OwnedOnlyComboFilter` is true and item is not owned from selected sources → reject immediately (see [Owned-Only Combo Filter](#7-owned-only-combo-filter))
2. `base.WouldBeVisible(in item, globalIndex)` — matches display-language name via `ToFilterString()` → `item.Name.Utf16` (inherited partwise matching)
3. `WouldBeVisible(item.Model.Utf16)` — matches model string like `(12345-1)`
4. `MatchesCrossLanguage(in item)` — cross-language check (only when enabled)

**`MatchesCrossLanguage(in CacheItem)`** — Private method:

1. Early-out if `config.CrossLanguageEquipmentSearch` is `false` or `Parts.Length is 0`
2. Early-out for special items (ID 0 or ≥ `uint.MaxValue - 512`)
3. Calls `itemNameService.GetAllLanguageNames(itemId)` → `string[4]` (EN, JP, DE, FR), cached in `_allLanguageNamesCache`
4. For each non-empty name, calls inherited `WouldBeVisible(string)` — this reuses the partwise filter logic (`Parts.All(p => text.Contains(p, Comparison))`)
5. Returns `true` if **any** language name passes all filter tokens

**Key design: per-language partwise matching** — All filter tokens must match within the **same** language name. The filter does NOT mix matches across languages. This is achieved by calling `WouldBeVisible(name)` (the `PartwiseFilterBase<T>.WouldBeVisible(string)` overload) per language, which checks that every token in `Parts` appears in that single string.

**Dependency injection chain:**

```
EquipmentDrawer(…, ItemNameService itemNameService, ItemUnlockManager itemUnlockManager)
  → new EquipCombo(…, itemNameService, itemUnlockManager, …)       → BaseItemCombo(…, itemNameService, itemUnlockManager)
  → new WeaponCombo(…, itemNameService, itemUnlockManager, …)      → BaseItemCombo(…, itemNameService, itemUnlockManager)
  → new BonusItemCombo(…, itemNameService, itemUnlockManager, …)   → BaseItemCombo(…, itemNameService, itemUnlockManager)
    → base(new ItemFilter(itemNameService, config, itemUnlockManager), …)
```

`EquipmentDrawer` receives `ItemNameService` and `ItemUnlockManager` via DI (auto-discovered as `IService` singletons) and passes both to all three combo types. Each combo forwards them to `BaseItemCombo`, which creates the `ItemFilter` with `ItemNameService`, `Configuration`, and `ItemUnlockManager`.

**ItemNameService.GetAllLanguageNames(uint itemId)** — Public method:

- Checks `_allLanguageNamesCache` (separate from single-language `_nameCache`)
- On miss: loads names from all 4 `ExcelSheet<Item>` language sheets via `row.Name.ExtractText()`
- Caches and returns `string[4]`, or `null` if no names found
- `ClearCache()` clears both `_nameCache` and `_allLanguageNamesCache`

**Settings UI** — `DrawEquipmentLanguageSettings()` in `SettingsTab.cs`:

- Checkbox labeled "Cross-Language Equipment Search"
- Help text: "When enabled, equipment combo searches will match item names in all available languages, not just the selected display language."
- On toggle: sets `config.CrossLanguageEquipmentSearch` and calls `itemNameService.ClearCache()`

### Data Flow

```
User enables "Cross-Language Equipment Search" in Settings
  → config.CrossLanguageEquipmentSearch = true
  → itemNameService.ClearCache() → _allLanguageNamesCache.Clear()

User types filter text in equipment combo (e.g. "鉄")
  → PartwiseFilterBase.SetInternal() → Parts = ["鉄"]
  → For each CacheItem in combo list:
    → ItemFilter.WouldBeVisible(in item, globalIndex)
      → 1. base.WouldBeVisible() → ToFilterString() = item.Name.Utf16 (display language)
           → Parts.All(p => displayName.Contains(p)) → false (English name doesn't contain "鉄")
      → 2. WouldBeVisible(item.Model.Utf16) → false (model string doesn't match)
      → 3. MatchesCrossLanguage(in item)
           → config.CrossLanguageEquipmentSearch? → true
           → itemId valid? (not 0, not special) → true
           → itemNameService.GetAllLanguageNames(itemId)
             → _allLanguageNamesCache miss
             → Load from 4 ExcelSheet<Item> sheets → cache string[4]
           → foreach name in [EN, JP, DE, FR]:
               → WouldBeVisible("鉄の鎖帷子") → Parts.All(p => name.Contains(p)) → true!
           → return true → item is visible in filtered list
```

### Configuration

| Property | Type | Default |
|----------|------|---------|
| `CrossLanguageEquipmentSearch` | `bool` | `false` |

---

## 7. Owned-Only Combo Filter

Filter equipment, weapon, and bonus item combo dropdowns to show only items the player currently owns. Ownership is tracked **per-character** with automatic **pruning** when items leave inventories.

### User-Facing Functionality

- **Master toggle**: "Show Only Owned Items in Combos" checkbox in the equipment section of the Actor panel, Design panel, and Settings tab (next to "Keep Item and Dye Filters After Selection")
- **Per-source toggles**: When the master toggle is enabled, 6 indented checkboxes appear to control which sources count as "owned":
  - Inventory (player bags, armory chest, equipped items, mail)
  - Glamour Dresser (prism box + glamour plates)
  - Armoire (cabinet)
  - Saddlebags (chocobo saddlebags + premium)
  - Retainers (all retainer pages, equipped, market)
  - Quest / Achievement (items unlockable via quests, achievements, or state requirements)
- **Per-character tracking**: Each character has an independent save file. Switching characters loads that character's owned item data.
- **Automatic pruning**: Items removed from transient inventories (Inventory, Saddlebags, Retainers, Glamour Dresser) are detected and have their source flags removed. Items with no remaining sources are removed entirely.
- **Pseudo items always visible**: "Nothing", "Smallclothes", and other special items (ID 0 or ≥ `uint.MaxValue - 512`) are always shown regardless of filter state.
- **Cache refresh**: Combo filter caches are dirtied when the popup closes (`DirtyCacheOnClose = true`), so ownership changes are reflected the next time a combo is opened.

### Implementation — Per-Character Storage

| File | Role |
|------|------|
| `Glamourer/Services/FilenameService.cs` | `UnlockFileItemsForCharacter(ulong contentId)` → `{ConfigDir}/unlocks_items_{contentId:X16}.dat` |
| `Glamourer/Unlocks/ItemUnlockManager.cs` | Per-character lifecycle: login/logout handlers, `_currentContentId` field |

**Character lifecycle** in `ItemUnlockManager`:

- **Constructor**: Subscribes to `_clientState.Login += OnLogin` and `_clientState.Logout += OnLogout`. If `_playerState.ContentId != 0` (plugin reload while logged in), immediately calls `OnLogin()`.
- **`OnLogin()`**: Captures `_playerState.ContentId`, clears all dictionaries and scan state, calls `Load()` then `Scan()`.
- **`OnLogout(int, int)`**: Calls `Save()`, clears all dictionaries and scan state, resets `_currentContentId = 0`.
- **`OnFramework` guard**: Early-returns if `_currentContentId == 0` (no character logged in).
- **`ToFilePath()`**: Returns `fileNames.UnlockFileItemsForCharacter(_currentContentId)` when a character is logged in, falls back to `fileNames.UnlockFileItems` otherwise.
- **`ResetScanState()`**: Resets all scan-related fields: `_currentInventory`, `_currentInventoryIndex`, armoire/achievement/glamour/plate state booleans, `_seenThisCycle`, `_fullyScannedSources`.

### Implementation — Source Tracking

| File | Role |
|------|------|
| `Glamourer/Unlocks/ItemUnlockManager.cs` | `ItemSource` flags enum, `_sources` dictionary, source-aware `AddItem()` |
| `Glamourer/Unlocks/UnlockDictionaryHelpers.cs` | v3 binary format persisting source byte per entry |

**`ItemSource` flags enum** (`[Flags] enum ItemSource : byte`):

| Flag | Value | Source |
|------|-------|--------|
| `Inventory` | `0x01` | Player bags (1-4), equipped, mail, armory chest |
| `GlamourDresser` | `0x02` | Prism box + glamour plates |
| `Armoire` | `0x04` | Cabinet items |
| `Saddlebags` | `0x08` | Chocobo saddlebag + premium saddlebag |
| `Retainers` | `0x10` | All retainer pages, equipped, market |
| `QuestAchievement` | `0x20` | Quest/achievement/state-gated shop items |
| `All` | `0x3F` | Combination of all flags |

**`_sources` dictionary** (`Dictionary<uint, ItemSource>`) — Parallel to `_unlocked`, stores the OR-combination of all sources an item has been detected from. Updated in `AddItem()`: sources always OR-in, even for already-unlocked items.

**`GetInventorySource(InventoryType)`** — Maps 29 `InventoryType` values to `ItemSource`:
- `Inventory1–4`, `EquippedItems`, `Mail`, `Armory*` → `Inventory`
- `SaddleBag1/2`, `PremiumSaddleBag1/2` → `Saddlebags`
- `RetainerPage1–7`, `RetainerEquippedItems`, `RetainerMarket` → `Retainers`

**Binary format v3** (`UnlockDictionaryHelpers`):
- Header: `[Magic:0x00C0FFEE (uint32)] [Version:3 (int32)] [Count (int32)]`
- Per entry: `[ItemId (uint32)] [Timestamp (int64)] [Source (byte)]`
- Backward compatible: v1/v2 files load with `ItemSource.All` default. v3 reads the source byte.
- The non-source `Save()` overload (used by `CustomizeUnlockManager`) writes `0x00` as the source byte.

### Implementation — Pruning

Two pruning mechanisms ensure stale items are removed:

**Inventory scan cycle pruning** — Handles Inventory, Saddlebags, Retainers:

- **Tracking fields**: `Dictionary<uint, ItemSource> _seenThisCycle` and `ItemSource _fullyScannedSources`
- **Per-frame scanning**: After each `AddItem()` call in the inventory scan block, `MarkSeen(itemId, source)` records the item (and its variants) in `_seenThisCycle`
- **Inventory advancement**: When advancing past a fully-iterated inventory type (container was loaded and all slots scanned), its mapped `ItemSource` is OR'd into `_fullyScannedSources`. Containers that are null or not loaded are silently skipped (not marked).
- **Cycle completion**: When `_currentInventory` wraps from the last type back to 0, `PruneInventorySources()` runs:
  1. Computes `pruneMask = PrunableSources & _fullyScannedSources` (only prunes sources that were fully scanned)
  2. For each item in `_sources` with a prunable flag that was fully scanned but NOT seen in `_seenThisCycle` → removes that flag
  3. Items with no remaining source flags are removed from both `_sources` and `_unlocked`
  4. Clears `_seenThisCycle` and `_fullyScannedSources`
- **Prunable sources constant**: `Inventory | Saddlebags | Retainers` — Armoire and QuestAchievement are permanent and never pruned by this mechanism
- **Retainer safety**: Retainer inventories are only available at the retainer bell. If not loaded, `_fullyScannedSources` won't include `Retainers`, preventing false pruning.

**Glamour Dresser pruning** — Handles `GlamourDresser` flag:

- Triggered when `PrismBoxLoaded` state changes to `true`
- Collects all current dresser item IDs into a `HashSet<uint>` (resolved through `ItemData.TryGetValue` for model normalization)
- Calls `PruneSource(ItemSource.GlamourDresser, currentDresserItems)`: for each item with `GlamourDresser` flag NOT in the current set → removes the flag. Items with no remaining flags are removed entirely.
- After pruning, adds/updates all current dresser items with `AddItem(item, time, GlamourDresser)`
- **Glamour plates are additive only** — Plates are a subset of the dresser. They add `GlamourDresser` flags but don't trigger pruning. The prism box is the authoritative source.

**Non-prunable sources** — Armoire (`Cabinet`) and Quest/Achievement items are detected via game API (`IsUnlocked`) and represent permanent unlocks. These flags are never removed by pruning. They are set in `Scan()` (which runs on login and when armoire/achievement state loads) and in `IsUnlocked()` (lazy detection path).

### Implementation — Combo Filter Integration

| File | Role |
|------|------|
| `Glamourer/Gui/Equipment/BaseItemCombo.cs` | `ItemFilter` receives `ItemUnlockManager`, ownership pre-gate in `WouldBeVisible()` |
| `Glamourer/Gui/Equipment/ItemCombo.cs` | `EquipCombo` constructor accepts and forwards `ItemUnlockManager` |
| `Glamourer/Gui/Equipment/WeaponCombo.cs` | `WeaponCombo` constructor accepts and forwards `ItemUnlockManager` |
| `Glamourer/Gui/Equipment/BonusItemCombo.cs` | `BonusItemCombo` constructor accepts and forwards `ItemUnlockManager` |
| `Glamourer/Gui/Equipment/EquipmentDrawer.cs` | Injects `ItemUnlockManager` into all combo constructors, `DrawOwnedOnlyFilter()` UI |

**ItemFilter integration** — The `ItemFilter` class receives `ItemUnlockManager` as a third primary constructor parameter:

```csharp
sealed class ItemFilter(ItemNameService itemNameService, Configuration config, ItemUnlockManager itemUnlockManager)
    : PartwiseFilterBase<CacheItem>
```

`WouldBeVisible(in CacheItem, int)` now has a **pre-gate** before the three existing checks:

1. **Owned check** (new): If `config.OwnedOnlyComboFilter` is true and `itemUnlockManager.IsOwnedFromSources(item.Item.ItemId, config.OwnedComboFilterSources)` returns false → **reject immediately** (return false)
2. `base.WouldBeVisible()` — display-language name match
3. `WouldBeVisible(item.Model.Utf16)` — model string match
4. `MatchesCrossLanguage(in item)` — cross-language match

**`IsOwnedFromSources(CustomItemId, ItemSource filter)`** — Public query method on `ItemUnlockManager`:
- Pseudo items (ID 0 or ≥ `uint.MaxValue - 512`) always return `true`
- Otherwise checks `(_sources[id] & filter) != 0`

**Cache invalidation** — `BaseItemCombo` sets `DirtyCacheOnClose = true` in `ConfigData`, ensuring the filter re-evaluates ownership each time the combo popup opens. This avoids needing explicit event-driven cache invalidation.

**Dependency injection chain:**

```
EquipmentDrawer(…, ItemNameService, ItemUnlockManager)
  → new EquipCombo(…, itemNameService, itemUnlockManager, …)   → BaseItemCombo(…, itemNameService, itemUnlockManager)
  → new WeaponCombo(…, itemNameService, itemUnlockManager, …)  → BaseItemCombo(…, itemNameService, itemUnlockManager)
  → new BonusItemCombo(…, itemNameService, itemUnlockManager, …) → BaseItemCombo(…, itemNameService, itemUnlockManager)
    → base(new ItemFilter(itemNameService, config, itemUnlockManager), …)
```

### Implementation — Settings UI

`EquipmentDrawer.DrawOwnedOnlyFilter(Configuration config)` — Static method drawn alongside `DrawKeepItemFilter()`:

- Master checkbox: "Show Only Owned Items in Combos" — toggles `config.OwnedOnlyComboFilter`, saves on change
- Tooltip explaining the feature
- When master is enabled, 6 indented source checkboxes via `DrawSourceToggle()`:
  - Each toggles a flag in `config.OwnedComboFilterSources` via XOR, saves on change
- Called from 3 locations: `ActorPanel`, `DesignPanel`, and `SettingsTab` (same places as `DrawKeepItemFilter`)

### Data Flow

```
Character logs in
  → IClientState.Login event → ItemUnlockManager.OnLogin()
    → _currentContentId = _playerState.ContentId
    → Clear _unlocked, _sources, scan state
    → Load(unlocks_items_{contentId:X16}.dat) → populate _unlocked + _sources
    → Scan() → detect Armoire + QuestAchievement items

Per frame (OnFramework):
  → Early-return if _currentContentId == 0
  → Check armoire/achievement state changes → Scan() if needed
  → Check glamour dresser state → prune removed items, add current items
  → Check glamour plates state → add plate items (additive only)
  → Scan one inventory slot:
    → AddItem(itemId, time, source) → OR source into _sources
    → MarkSeen(itemId, source) → record in _seenThisCycle
  → When inventory type fully scanned → mark in _fullyScannedSources
  → When cycle wraps to 0 → PruneInventorySources()
    → Remove stale source flags from items not seen
    → Remove items with no remaining flags
  → If changes → Save() (10-second delay)

User opens equipment combo:
  → FilterComboBase cache was dirtied on last popup close
  → UpdateFilter() runs → calls WouldBeVisible() for each item
    → config.OwnedOnlyComboFilter? → check IsOwnedFromSources()
      → (_sources[id] & config.OwnedComboFilterSources) != 0 → show/hide item
    → Then text/model/cross-language matching as normal

User toggles source checkbox:
  → config.OwnedComboFilterSources ^= flag → config.Save()
  → Next combo open → filter re-evaluates with new source mask

Character logs out:
  → IClientState.Logout event → ItemUnlockManager.OnLogout()
    → Save() → persist to per-character file
    → Clear all state, _currentContentId = 0
```

### Bug Fix: Glamour Dresser Save Trigger

The original `OnFramework` had a bug where `changes = false;` was written before the inventory scanning block, discarding any `changes = true` set by the glamour dresser/plates scanning above. This silently prevented `Save()` from being called when new items were detected in the glamour dresser. Fixed by removing the erroneous reset — `changes` now accumulates across both dresser and inventory scanning sections.

### Configuration

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `OwnedOnlyComboFilter` | `bool` | `false` | Master toggle for owned-only filtering |
| `OwnedComboFilterSources` | `ItemUnlockManager.ItemSource` | `All` (`0x3F`) | Bitmask of sources that count as "owned" |

### Known Pitfalls (for future upstream merges)

1. **Per-character file path**: `ToFilePath()` now returns per-character paths. The `ISavable` system calls this on save — ensure upstream changes to `SaveService` or `FilenameService` don't break the dynamic path resolution.

2. **Login/Logout lifecycle**: `ItemUnlockManager` no longer loads in its constructor. If other services depend on `ItemUnlockManager` being populated at construction time, they may see empty data until login fires. The constructor handles the "already logged in" case for plugin reloads.

3. **DirtyCacheOnClose**: The combo cache is now dirtied every time the popup closes. This is necessary for ownership changes to be reflected but has a minor performance cost (one filter pass per popup open). Upstream changes to `FilterComboBase.ConfigData` field names should be monitored.

4. **PruneSource modifies _sources during iteration**: Both `PruneInventorySources()` and `PruneSource()` iterate `_sources` and collect removals into a separate `List<uint>`, then remove after iteration. This avoids collection-modified-during-enumeration exceptions.

5. **Retainer scan safety**: Retainer containers are only loaded when at a retainer bell. The `_fullyScannedSources` mechanism prevents false pruning — retainer items won't be pruned unless all retainer inventory types were actually loaded and fully iterated that cycle.

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
| `OwnedOnlyComboFilter` | `bool` | `false` | Owned-Only Combo Filter |
| `OwnedComboFilterSources` | `ItemUnlockManager.ItemSource` | `All` | Owned-Only Combo Filter |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│                   User Interface                     │
│  ┌──────────────┐  ┌────────────┐  ┌──────────────┐ │
│  │ ActorPanel   │  │ContextMenu │  │ CodeDrawer   │ │
│  │ (Equipment + │  │ Service +  │  │ (Fun Modes   │ │
│  │  Customize + │  │ PopupMenu  │  │  Settings)   │ │
│  │  Stain)      │  │            │  │              │ │
│  └──────┬───────┘  └─────┬──────┘  └──────┬───────┘ │
│         │                │                 │         │
│  ┌──────▼────────────────▼─────┐  ┌───────▼───────┐ │
│  │      PreviewService         │  │  CodeService   │ │
│  │  (Live preview state mgmt)  │  │  (Flag toggle) │ │
│  │  Types: SingleItem,         │  │               │ │
│  │   SingleCustomization,      │  │               │ │
│  │   SingleStain, Equipment,   │  │               │ │
│  │   Appearance, Design        │  │               │ │
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
│                                                      │
│  ┌──────────────────────────────────────────────┐   │
│  │          ItemUnlockManager                    │   │
│  │  (Per-character owned item tracking,          │   │
│  │   source flags, inventory pruning,            │   │
│  │   combo filter integration)                   │   │
│  └──────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
```
