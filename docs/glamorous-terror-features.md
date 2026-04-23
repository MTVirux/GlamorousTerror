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
8. [Icon Equipment Drawer](#8-icon-equipment-drawer)
9. [Favorites](#9-favorites)
10. [Character Rotation](#10-character-rotation)
11. [Immersive Dresser](#11-immersive-dresser)

### Source Layout

Fork-specific code lives under `Glamourer/GlamorousTerror/` and is organized by feature:

| Subdirectory | Feature(s) |
|--------------|-----------|
| `ContextMenu/` | Context Menu, CharacterPopupMenu |
| `PreviewOnHover/` | PreviewService, per-drawer hover wiring, DesignPreviewService |
| `WildcardAutomation/` | `AutoDesignApplier.Wildcard.cs` |
| `EquipmentLanguage/` | `ItemNameService.cs` (language override + cross-language names) |
| `ItemOwnership/` | `ItemUnlockManager.cs`, `CustomizeUnlockManager.cs`, `FavoriteManager.cs`, owned-only combo filter UI, unlock serialization |
| `IconEquipment/` | `EquipmentDrawer.IconMode.cs` (icon grid + icon picker popup) |
| `ImmersiveDresser/` | `ImmersiveDresserWindow.cs` (manager + 3 panel classes) |
| `CharacterRotation/` | `RotationService.cs`, `RotationDrawer.cs` |
| `Config/` | `Configuration.GT.cs` (GT-only fields), `SettingsTab.GT.cs` (GT section UI) |

Upstream-shaped code that needed hooks to call into fork code uses the partial-class + partial-method pattern (e.g., `EquipmentDrawer` has `GTTryDrawEquipIcon`, `GTResetIconState`, `GTCaptureStainSlot`, `GTPreFilterItem`, etc. — implemented in the GT folder, declared in the upstream file).

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
| `Glamourer/GlamorousTerror/ContextMenu/ContextMenuService.cs` | Hooks Dalamud's `IContextMenu`, adds "Glamorous Terror" (and "Immersive Dresser") to character right-click menus |
| `Glamourer/GlamorousTerror/ContextMenu/CharacterPopupMenu.cs` | ~850-line custom ImGui popup with all menu logic |
| `Glamourer/GlamorousTerror/PreviewOnHover/PreviewService.cs` | Preview engine shared with equipment/customization drawers |

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
| `EnableGameContextMenu` | `bool` | `true` | `Configuration.GT.cs` |

Controlled in the Settings tab (Glamorous Terror section) via `ContextMenuService.Enable()` / `Disable()`.

---

## 2. Preview-on-Hover

When hovering over items in any UI combo or popup, the character's appearance **updates in real-time** to preview the change. Moving away restores the original appearance. Clicking an item makes the change permanent.

### Coverage

| UI Element | Preview Type | Notes |
|------------|-------------|-------|
| Equipment slot combos (Head, Body, etc.) | Single item | All 14 equipment slots |
| Bonus item combos (Glasses, etc.) | Single bonus item | All bonus item flags |
| Weapon combos (Main Hand, Off Hand) | Single item | Both weapon slots |
| Stain/dye combos (per-slot) | Single stain | All slots, per-stain-index. Immediate on hover |
| **Dye All Slots combo** | **All-slots stain** | Previews the chosen stain written to every EqdpSlot at once |
| **Icon picker popup (icon equipment drawer)** | **Single item / Single bonus item** | Requires CTRL held (matches customization popup pattern) |
| Customization icon popups (Face, Hairstyle, Face Paint) | Single customization | Requires CTRL held |
| Customization list combos (Eye shape, Nose, etc.) | Single customization | Requires CTRL held |
| Customization color popups (Hair color, Eye color, etc.) | Single customization | Requires CTRL held |
| Context menu equipment/appearance items | Equipment/Appearance | See [Context Menu](#1-context-menu) |
| Context menu design tree | Full design | Previews entire saved design |

### Implementation — PreviewService

The central preview engine in `Glamourer/GlamorousTerror/PreviewOnHover/PreviewService.cs` contains two key types:

**`PreviewType` enum**: `None`, `SingleItem`, `SingleCustomization`, `SingleStain`, `AllSlotsStain`, `Equipment`, `Appearance`, `Design`, `FullDesignToSelf`, `FullDesignToTarget`, `Automation`, `Reset`.

**`PreviewState`** — Tracks all preview state:

| Property | Type | Purpose |
|----------|------|---------|
| `IsActive` | `bool` | Whether any preview is in progress |
| `Type` | `PreviewType` | Which kind of preview is active |
| `TargetState` | `ActorState?` | The actor being previewed |
| `OriginalData` | `DesignData` | Snapshot of actor state before preview started |
| `OriginalMaterials` | material data | Material state snapshot for restoration |
| `OriginalAllSlotStains` | `Dictionary<EquipSlot, StainIds>` | Per-slot stain snapshot for `AllSlotsStain` restore |
| `ToSelf` | `bool` | Whether previewing changes to self |
| `PopupActiveThisFrame` | `bool` | Per-frame flag set by popup drawing code |
| `ActivePopupType` | `PopupType` | Icon, List, or Color |
| `PopupHoveredIndex` | `int?` | Which option is being hovered |
| `PopupHoveredValue` | `CustomizeValue` | The customize value of the hovered option |
| `PopupSelectionMade` | `bool` | Whether the user clicked to select |
| `RequiresCtrl` | `bool` | Whether CTRL must be held for this preview |

**`PreviewService`** — Methods organized by lifecycle:

- **Start**: `StartSingleItemPreview()`, `StartSingleBonusItemPreview()`, `StartSingleCustomizationPreview(state, index, requiresCtrl)`, `StartSingleStainPreview()`, `StartAllSlotsStainPreview()`
- **Apply**: `PreviewSingleItem()`, `PreviewSingleBonusItem()`, `PreviewSingleStain()`, `PreviewAllSlotsStain(state, stainValue)`, `HandleCustomizationPopupFrame(state, index, hoveredIndex, hoveredValue, ctrlHeld)`
- **Restore**: `RestoreSingleValuePreview()`, `EndSingleValuePreview(wasSelectionMade)`, `EndCustomizationPopupFrame()`, `EndPreviewIfType(PreviewType)`
- **Query**: `IsSingleItemPreview(slot)`, `IsSingleBonusItemPreview(slot)`, `IsSingleCustomizationPreview(index)`, `IsSingleStainPreview(slot, stainIndex)`, `IsAllSlotsStainPreview()`, `IsSameTypePreview(PreviewType)`

**Key implementation detail for query methods**: All must check `IsActive && Type == <expected>` (not just slot/index match). Without this, stale values after `End()` can falsely match.

### Implementation — Equipment Drawer

| File | Key Addition |
|------|-------------|
| `Glamourer/Gui/Equipment/BaseItemCombo.cs` | `HoveredItem`, `IsPopupOpen`, `ItemSelected`, `ResetSelection()` properties |
| `Glamourer/Gui/Equipment/GlamourerColorCombo.cs` | `HoveredStain`, `IsPopupOpen`, `StainSelected`, `ResetSelection()`, `ResetFrameState()` properties |
| `Glamourer/GlamorousTerror/PreviewOnHover/EquipmentDrawer.Preview.cs` | `ApplyHoverPreview(StateManager, ActorState)`, `ApplyAllStainHoverPreview(StateManager, ActorState)`, stain/all-stain slot tracking fields, `GTResetPreviewState()`, `GTCaptureStainSlot()`, `GTCaptureAllStain()` partial method implementations |

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
5. Checks icon picker popup — if `_iconPickerPopupOpen`:
   - Starts single item/bonus preview for the captured slot
   - Only calls `PreviewSingleItem`/`PreviewSingleBonusItem` when **CTRL is held** and an item is hovered; otherwise `RestoreSingleValuePreview()`
   - On selection → `EndSingleValuePreview(wasSelectionMade: true)`
   - `return`s early
6. **Fall-through** (no popup open): Only ends preview if `State.Type is PreviewType.SingleItem or PreviewType.SingleStain` — **must NOT end `SingleCustomization` or `AllSlotsStain`** since those are managed by other drawers/dispatchers

**`ApplyAllStainHoverPreview(StateManager, ActorState)`** — Separate dispatcher for the "Dye All Slots" combo. Must be called from whatever panel actually contains `DrawAllStain()` (e.g., `ActorPanel`, `ImmersiveDresser.OptionsPanel`). It uses the same shared `_stainCombo` instance but tracks the all-stain path via `_allStainPreviewValid` (set in `GTCaptureAllStain()` when `DrawAllStain` renders the popup).

- On popup open: `StartAllSlotsStainPreview(state)` snapshots stains for every `EqdpSlot`
- On hover: `PreviewAllSlotsStain(state, hoveredStain)` writes `StainIds.All(hoveredStain)` to all EqdpSlots
- On selection: `EndSingleValuePreview(wasSelectionMade: true)` commits
- On fall-through: ends preview only if `Type == PreviewType.AllSlotsStain`, so this dispatcher does not interfere with per-slot stain previews running in parallel panels

**CRITICAL: Cross-drawer interference** — `EquipmentDrawer.ApplyHoverPreview()` runs AFTER `CustomizationDrawer.ApplyHoverPreview()` in `ActorPanel`. If the equipment drawer's fall-through unconditionally called `RestoreSingleValuePreview()`, it would kill any active customization preview every frame. The type guard prevents this. The same logic applies to the all-stain dispatcher — it only ends `AllSlotsStain` previews.

### Implementation — Customization Drawer

| File | Key Addition |
|------|-------------|
| `Glamourer/Gui/Customization/CustomizationDrawer.cs` | `PreviewService` constructor param, popup flag reset in `DrawInternal()` |
| `Glamourer/GlamorousTerror/PreviewOnHover/CustomizationDrawer.Preview.cs` | Public `ApplyHoverPreview()` dispatcher, `_iconPopupOpen/_listPopupOpen/_colorPopupOpen` state, `ApplyIconHoverPreview()`/`ApplyListHoverPreview()`/`ApplyColorHoverPreview()` sub-methods |
| `Glamourer/Gui/Customization/CustomizationDrawer.Icon.cs` | Hover tracking in `DrawIconPickerPopup()` — sets `_iconPopupOpen`, `_iconHoveredValue`, `_iconSelectionMade` |
| `Glamourer/Gui/Customization/CustomizationDrawer.Simple.cs` | Hover tracking in `ListCombo0()`/`ListCombo1()` — sets `_listPopupOpen`, `_listHoveredValue`, `_listSelectionMade` |
| `Glamourer/Gui/Customization/CustomizationDrawer.Color.cs` | Hover tracking in `DrawColorPickerPopup()` — sets `_colorPopupOpen`, `_colorHoveredValue`, `_colorSelectionMade` |

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
- `DrawEquipmentHeader()` calls, in this order after equipment draws and drag-drop tooltip:
  1. `_equipmentDrawer.ApplyHoverPreview(_stateManager, _selection.State!)` — per-slot combos + icon picker
  2. `_equipmentDrawer.ApplyAllStainHoverPreview(_stateManager, _selection.State!)` — Dye All Slots combo

The Immersive Dresser's `OptionsPanel` likewise calls `ApplyAllStainHoverPreview` after `DrawAllStain` (the panel that actually draws the all-stain combo).

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

6. **AllSlotsStain must live in its own dispatcher**: The Dye All Slots combo shares the one `_stainCombo` instance with per-slot stain draws, so the per-slot dispatcher and `ApplyAllStainHoverPreview` must be mutually exclusive about which preview type they end. The per-slot fall-through ends `SingleItem`/`SingleStain`; the all-stain fall-through ends only `AllSlotsStain`. Calling only one dispatcher (e.g., forgetting `ApplyAllStainHoverPreview` in a panel that draws `DrawAllStain`) will leave the all-stain preview stuck on the character.

7. **Icon picker CTRL gate**: The icon picker popup inside the icon equipment drawer uses the same pattern as customization popups — preview only when CTRL is held. The selection path bypasses the CTRL gate (clicking without CTRL commits immediately). Any change to the popup must preserve both branches.

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

All code is in `Glamourer/GlamorousTerror/WildcardAutomation/AutoDesignApplier.Wildcard.cs` (partial class extension of the upstream `AutoDesignApplier`):

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

| File | Role |
|------|------|
| `Glamourer/GlamorousTerror/EquipmentLanguage/ItemNameService.cs` | Language-specific Lumina sheet loading, name caching |
| `Glamourer/GlamorousTerror/Config/SettingsTab.GT.cs` | `DrawGlamorousTerrorSettings()` / `DrawEquipmentLanguageSettings()` combo UI |
| `Glamourer/GlamorousTerror/Config/Configuration.GT.cs` | `EquipmentNameLanguage` property |

**`EquipmentNameLanguage`** enum: `GameDefault`, `English`, `Japanese`, `German`, `French`

**ItemNameService** — `IService` that:

- Loads all 4 language `ExcelSheet<Item>` sheets from Lumina in constructor
- `GetItemName(EquipItem)` / `GetItemName(uint itemId, string fallback)` — returns name in configured language
- Uses a per-language `Dictionary<uint, string>` cache (`_nameCache`) to avoid repeated Lumina lookups
- `CheckLanguageChange()` — detects config change, clears cache and refreshes active sheet
- `ClearCache()` — called from settings UI when language changes

**SettingsTab.DrawGlamorousTerrorSettings()** (and the standalone `DrawEquipmentLanguageSettings()` panel) renders a language selection combo and calls `ItemNameService.ClearCache()` on change.

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

| File | Role |
|------|------|
| `Glamourer/Gui/Equipment/BaseItemCombo.cs` | `ItemFilter` base class with `WouldBeVisible(...)` pipeline and partial-method hooks (`GTPreFilterItem`, `GTFallbackNameMatch`) |
| `Glamourer/GlamorousTerror/ItemOwnership/BaseItemCombo.GT.cs` | Implements `GTFallbackNameMatch` (cross-language) and `GTPreFilterItem` (owned check) |
| `Glamourer/GlamorousTerror/EquipmentLanguage/ItemNameService.cs` | `GetAllLanguageNames(uint)` — returns `string[4]` of all language names, cached; `MatchesAnyLanguage(EquipItem, string)` standalone helper |
| `Glamourer/Gui/Equipment/EquipmentDrawer.cs` | Injects `ItemNameService` into all combo constructors |
| `Glamourer/GlamorousTerror/Config/SettingsTab.GT.cs` | Checkbox UI + `ClearCache()` on toggle |
| `Glamourer/GlamorousTerror/Config/Configuration.GT.cs` | `CrossLanguageEquipmentSearch` property |

**ItemFilter integration** — The `ItemFilter` nested class inside `BaseItemCombo` receives `ItemNameService`, `Configuration`, and `ItemUnlockManager` via its primary constructor. It declares two GT partial-method hooks (`GTPreFilterItem` and `GTFallbackNameMatch`) whose bodies live in `Glamourer/GlamorousTerror/ItemOwnership/BaseItemCombo.GT.cs`.

`WouldBeVisible(in CacheItem, int)` evaluates in order:

1. **Owned pre-gate** (`GTPreFilterItem`): If `config.OwnedOnlyComboFilter` is true and item is not owned from selected sources → reject immediately (see [Owned-Only Combo Filter](#7-owned-only-combo-filter))
2. `base.WouldBeVisible(in item, globalIndex)` — matches display-language name via `ToFilterString()` → `item.Name.Utf16` (inherited partwise matching)
3. `WouldBeVisible(item.Model.Utf16)` — matches model string like `(12345-1)`
4. **Cross-language fallback** (`GTFallbackNameMatch`): when enabled, checks all language names

**`GTFallbackNameMatch(in CacheItem)`** — GT partial-method implementation:

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
| `Glamourer/GlamorousTerror/ItemOwnership/ItemUnlockManager.cs` | Per-character lifecycle: login/logout handlers, `_currentContentId` field, source tracking, pruning |

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
| `Glamourer/GlamorousTerror/ItemOwnership/ItemUnlockManager.cs` | `ItemSource` flags enum, `_sources` dictionary, source-aware `AddItem()` |
| `Glamourer/GlamorousTerror/ItemOwnership/UnlockDictionaryHelpers.cs` | v3 binary format persisting source byte per entry |

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
| `Glamourer/Gui/Equipment/BaseItemCombo.cs` | `ItemFilter` declares the `GTPreFilterItem` partial hook |
| `Glamourer/GlamorousTerror/ItemOwnership/BaseItemCombo.GT.cs` | Implements `GTPreFilterItem` (owned pre-gate) |
| `Glamourer/Gui/Equipment/ItemCombo.cs` | `EquipCombo` constructor accepts and forwards `ItemUnlockManager` |
| `Glamourer/Gui/Equipment/WeaponCombo.cs` | `WeaponCombo` constructor accepts and forwards `ItemUnlockManager` |
| `Glamourer/Gui/Equipment/BonusItemCombo.cs` | `BonusItemCombo` constructor accepts and forwards `ItemUnlockManager` |
| `Glamourer/Gui/Equipment/EquipmentDrawer.cs` | Injects `ItemUnlockManager` into all combo constructors |
| `Glamourer/GlamorousTerror/ItemOwnership/EquipmentDrawer.OwnedFilter.cs` | Static `DrawOwnedOnlyFilter(config)` UI with master toggle + per-source summary combo |

**ItemFilter integration** — The `ItemFilter` nested class receives `ItemUnlockManager` as a primary constructor parameter and declares the `GTPreFilterItem(in CacheItem)` partial-method hook (implemented in `BaseItemCombo.GT.cs`).

`WouldBeVisible(in CacheItem, int)` order (fastest reject first):

1. **Owned pre-gate** (new, via `GTPreFilterItem`): If `config.OwnedOnlyComboFilter` is true and `itemUnlockManager.IsOwnedFromSources(item.Item.ItemId, config.OwnedComboFilterSources)` returns false → **reject immediately** (return false)
2. `base.WouldBeVisible()` — display-language name match
3. `WouldBeVisible(item.Model.Utf16)` — model string match
4. `GTFallbackNameMatch(in item)` — cross-language match

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

`EquipmentDrawer.DrawOwnedOnlyFilter(Configuration config)` — Static method in `Glamourer/GlamorousTerror/ItemOwnership/EquipmentDrawer.OwnedFilter.cs`:

- Master checkbox: "Show Only Owned Items in Combos" — toggles `config.OwnedOnlyComboFilter`, saves on change
- When master is enabled, a summary combo ("All", "None", or "N sources") expands into six source checkboxes via `DrawSourceToggle()`, each XOR-toggling one `ItemSource` flag in `config.OwnedComboFilterSources`
- Called from three locations: `ActorPanel`, `DesignPanel`, and the icon picker's inline settings panel (see [Icon Equipment Drawer](#8-icon-equipment-drawer)). The GT section of `SettingsTab` also calls it under its own header — this is the only "legacy" call site left from before the filter became icon-picker-inline
- **Keep Item Filter** (upstream behavior) and the owned filter are drawn alongside each other wherever they appear

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

## 8. Icon Equipment Drawer

Replaces the name-based equipment combo list with a compact **icon grid**. Clicking an icon opens a filterable icon picker popup; right-clicking clears/reverts the slot (and inside the popup, toggles favorite state).

### User-Facing Functionality

- **Master toggle**: `UseIconEquipmentDrawer` in Settings → Glamorous Terror section ("Icon Equipment Drawer")
- Renders armor slots, weapons, and bonus items as square icon buttons in the Actor panel, Design panel, and Immersive Dresser equipment panels
- **Click an icon** → opens icon picker popup anchored next to the button, edge-clamped to the viewport
- **Right-click an icon (outside popup)** → revert/clear that slot (standard upstream behavior)
- **Right-click an item (inside popup)** → toggles favorite for that item (see [Favorites](#9-favorites)); favorited items are highlighted with a yellow frame (ABGR `0xFF00CFFF`)
- **Filter bar** at the top of the popup:
  - Text search (case-insensitive substring, auto-focused on open)
  - **Star** button — favorites-only filter
  - **K** button — toggles `KeepIconPickerOpen` (if set, the popup stays open after each selection)
  - **Cog** button — expands an inline settings panel with `DrawOwnedOnlyFilter(config)` + "Group by Model"
  - Job filter combo (by role: Tanks, Healers, Melee, Physical Ranged, Magical Ranged, Crafters, Gatherers; plus "Unrestricted" shortcut for gear equippable by all jobs)
  - Dye channel filter combo (Any, 0, 1, 2)
  - Sort combo (A → Z, Z → A, ID ↑, ID ↓)
- **Grouping by model** (`GroupIconPickerByModel`, default `true`) — deduplicates items that share the same `(Type, PrimaryId, SecondaryId, Variant)`, keeping only the first after sorting
- **Owned-only gate** runs before the filter for each item (fast reject)
- **Preview-on-hover** — hovering an icon while CTRL is held previews the item (see [Preview-on-Hover](#2-preview-on-hover))
- **Scroll reset** — the popup's scroll position is forced to 0 for 2 frames after opening (prevents inheriting the previous popup's scroll)
- **Max rows** — popup height is capped at `IconPickerMaxRows` rows (default `10`), configurable via slider in Settings

### Implementation

| File | Role |
|------|------|
| `Glamourer/GlamorousTerror/IconEquipment/EquipmentDrawer.IconMode.cs` | ~880-line partial class extension of `EquipmentDrawer` — all icon mode state, filter/sort logic, popup layout, item/bonus/weapon icon draws |
| `Glamourer/Gui/Equipment/EquipmentDrawer.cs` | Upstream drawer declares `GTTryDrawEquipIcon`, `GTTryDrawBonusItemIcon`, `GTTryDrawWeaponsIcon`, `GTResetIconState` partial-method hooks that early-short-circuit when the icon drawer is active |
| `Glamourer/GlamorousTerror/Config/SettingsTab.GT.cs` | Master toggle + sub-settings (Group by Model, Keep Picker Open, Max Rows slider) |

**Icon picker state (session-scoped, not persisted):**

| Field | Purpose |
|-------|---------|
| `_iconPickerSlot` / `_iconPickerBonusSlot` | Which slot the open popup belongs to |
| `_iconPickerIsWeapon` / `_iconPickerIsBonus` | Popup variant |
| `_iconPickerPopupOpen` | Set `true` each frame the popup renders (for `ApplyHoverPreview`); resets via `GTResetIconState()` at start of frame |
| `_iconPickerHoveredItem` | Item under the mouse inside the popup |
| `_iconPickerSelectionMade` | Click-to-commit flag |
| `_iconPickerClickY` | Vertical anchor point so the popup opens at the clicked icon's Y |
| `_iconPickerScrollResetFrames` | Countdown that zeros `Im.Scroll.Y` for 2 frames after popup appears |
| `_iconPickerNameFilter`, `_iconPickerFavoritesOnly`, `_iconPickerJobFilter`, `_iconPickerNeutralJobFilter`, `_iconPickerDyeChannelFilter`, `_iconPickerSortMode`, `_iconPickerShowSettings` | Filter & sort state |

**Popup positioning** (`PositionIconPickerPopup`) opens the popup to the left or right of the source window's center (whichever side has more space), clamps the anchor inside the viewport, and sizes the popup to fit `IconPickerMaxRows` rows and 8 columns (`IconPickerColumns = 8`).

**Item pipeline** (per popup):

1. Iterate items of the slot's `FullEquipType` from `ItemData.ByType`
2. **Ownership filter**: `OwnedOnlyComboFilter` + `IsOwnedFromSources` (before any sort/group work)
3. `FilterIconPickerItem` — favorite, text, job, and dye-channel checks
4. **Model dedup** (if `GroupIconPickerByModel`): `HashSet<(FullEquipType, PrimaryId, SecondaryId, Variant)>`
5. `SortIconPickerItems` — alphabetical / ID sort
6. `DrawIconPickerItem` / `DrawBonusIconPickerItem` — renders the icon button with selected-red frame for the current equipped item or yellow frame for favorites

**Job filter** — Uses `JobService.AllJobGroups` to map `item.JobRestrictions.Id` → `JobGroup.Flags`, and checks against `_iconPickerJobFilter` (`JobFlag` bitmask) or the "Unrestricted" mode (only items equippable by every available job).

**Weapon picker special case** — When `comboType is FullEquipType.Unknown` (the "all weapons" mode), the popup iterates every `FullEquipType` whose `ToSlot()` is `MainHand`, with model-dedup applied across the flattened list.

### Configuration

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `UseIconEquipmentDrawer` | `bool` | `false` | Master toggle |
| `GroupIconPickerByModel` | `bool` | `true` | Dedup items sharing the same visual model |
| `KeepIconPickerOpen` | `bool` | `false` | Popup stays open after each selection |
| `IconPickerMaxRows` | `int` | `10` | Max visible rows (1–20) before scrolling |

---

## 9. Favorites

Users can **favorite** equipment items, stains, bonus items, and a specific set of customization values (hairstyles and face paints, keyed by gender/race/type/id). Favorites surface in the icon picker (highlighted + "favorites-only" filter) and are persisted to disk.

### User-Facing Functionality

- **Right-click an item** inside the icon picker popup → toggles favorite; the item's icon is framed in yellow (`0xFF00CFFF`, ABGR) when favorited
- **Star toggle** in the icon picker filter bar restricts the picker to favorited items only
- Customization favorites are scoped to hairstyles (`CustomizeIndex.Hairstyle`) and face paints (`CustomizeIndex.FacePaint`) only — other indices are rejected by `TypeAllowed`
- Favorites persist across sessions and character switches (global, not per-character)

### Implementation

| File | Role |
|------|------|
| `Glamourer/GlamorousTerror/ItemOwnership/FavoriteManager.cs` | `ISavable + IService` — four `HashSet`s (items, stains, hair styles, bonus items), JSON v1 persistence, `FavoriteChanged` event |

**`FavoriteManager`** — Dependencies: `SaveService`. Stores:

| Set | Type | Key |
|-----|------|-----|
| `_favorites` | `HashSet<ItemId>` | Equipment item IDs |
| `_favoriteColors` | `HashSet<StainId>` | Stain IDs |
| `_favoriteBonusItems` | `HashSet<BonusItemId>` | Bonus item IDs |
| `_favoriteHairStyles` | `HashSet<FavoriteHairStyle>` | Packed `(Gender, SubRace, CustomizeIndex, CustomizeValue)` — see below |

**`FavoriteHairStyle`** — A `readonly record struct` that packs four bytes into one `uint`:

```
bits 24–31: Gender
bits 16–23: SubRace
bits  8–15: CustomizeIndex
bits  0–7:  CustomizeValue
```

`ToValue()` packs, the constructor overload `FavoriteHairStyle(uint)` unpacks. This uint is what gets persisted in JSON.

**Persistence** — JSON with a version header. V0 was a bare `uint[]` of item IDs only; V1 is `{ Version, FavoriteItems, FavoriteColors, FavoriteHairStyles, FavoriteBonusItems }`. V0 files auto-migrate to V1 on load.

**Storage path** — `FilenameService.FavoriteFile` (a global, not per-character, file). Uses `SaveService.DelaySave(this)` for debounced writes.

**Public API:**

- `TryAdd(EquipItem)`, `TryAdd(ItemId)`, `TryAdd(BonusItemId)`, `TryAdd(StainId)`, `TryAdd(Gender, SubRace, CustomizeIndex, CustomizeValue)` — returns `false` if already present or id is 0
- `Remove(...)` — symmetric
- `Contains(...)` — O(1) hash-set lookup
- `FavoriteChanged` event fires with `(FavoriteType, uint id, bool added)` — consumers (e.g., combo caches) can invalidate on change
- `TypeAllowed(CustomizeIndex)` — `true` only for `Hairstyle` and `FacePaint`; enforced by `TryAdd(...)` for customizations

---

## 10. Character Rotation

Allows the user to freely rotate any target actor around all three axes (yaw / pitch / roll) independently of the game's normal character rotation. Exposed from the Immersive Dresser's Options panel.

### User-Facing Functionality

- Three **drag inputs** — Yaw, Pitch, Roll (degrees, wraps at 360° with a "+N" suffix past one full turn)
- **Reset Rotation** button clears the override for the target actor
- Rotation persists as long as the actor is valid; if the actor despawns or the Immersive Dresser closes, the override is cleared
- When the target actor changes, the previous override is auto-cleared

### Implementation

| File | Role |
|------|------|
| `Glamourer/GlamorousTerror/CharacterRotation/RotationService.cs` | `IService + IDisposable` — tracks per-actor quaternion overrides, framework-update hook that rewrites the draw object's rotation each frame |
| `Glamourer/GlamorousTerror/CharacterRotation/RotationDrawer.cs` | `IService` — draws Yaw/Pitch/Roll drags, calls `SetRotation`/`ClearRotation` |

**`RotationService`** — Key state:

- `Dictionary<nint, RotationOverride> _overrides` — keyed by actor address; the override struct stores the computed final quaternion, the original quaternion, the last model address, and the Euler offset the user set
- `IFramework` subscription — activated lazily when the first override is added, deactivated when the last is cleared (zero overhead when unused)

**Per-frame loop** (`OnFrameworkUpdate`):

1. For each override, read the current actor; if invalid, queue for removal
2. Write `ov.Rotation` onto `drawObj->Object.Rotation`, set `IsTransformChanged = true`
3. Also write onto `drawObj->Object.ChildObject` so weapons follow the body rotation
4. Remove stale overrides after iteration

**`SetRotation(actor, offsetDegrees)`** — Computes the final quaternion as `OriginalRotation × EulerOffset`. The "original" is captured once on first `SetRotation` and reused for subsequent updates (so setting a new offset composes with the game's current rotation at capture time, not with the previous override).

**`ClearRotation(actor)`** — Restores the yaw quaternion derived from the game object's `Rotation` field (`y = sin(halfYaw), w = cos(halfYaw)`) so the character snaps back to what the game considers canonical.

**`RotationDrawer`** — Tracks the last actor it drew for; on actor change, clears the previous override and re-initializes its local `_euler` buffer from `RotationService.TryGetEuler` (so reopening the dresser on the same actor restores the user's Euler values).

**Wiring** — `ImmersiveDresserManager.OptionsPanel.Draw()` wraps the drawer in a `Character Rotation` tree header and calls `manager._rotationDrawer.Draw(objects.Player)`. `ImmersiveDresserManager.Close()` calls `_rotationDrawer.Reset()` which in turn clears the override for the last actor.

---

## 11. Immersive Dresser

Right-clicking the **local player character** in-game adds an **"Immersive Dresser"** entry to the context menu. Clicking it — or running `/glamour dresser` / `/gt dresser` — opens a multi-panel glamour editor anchored to the player character, optionally hiding the game HUD.

### User-Facing Functionality

- Context menu entry appears only when right-clicking the player's own character; also openable via `/glamour dresser` / `/gt dresser`
- Three floating panels, each with a title bar (collapsible) and movable independently:
  - **Equipment / Customization** (left of center) — switches between armor slots + bonus items and the full customization drawer depending on mode
  - **Accessories / Parameters** (right of center) — switches between off-hand + accessory slots and the customize-parameter drawer
  - **Options** (below center) — mode toggle, design actions (clipboard/save/undo), game-UI/panel-lock/free-cam icon buttons, Dye All Slots, meta toggles, camera settings, character rotation
- Two **modes** toggled from the Options panel: `Equipment` (default) and `Appearance`
- Full **preview-on-hover** support — uses the same `EquipmentDrawer` combos as the Actor panel (see [Preview-on-Hover](#2-preview-on-hover)); the Options panel also drives the Dye-All-Slots preview via `ApplyAllStainHoverPreview`
- **Panel lock** — icon button in the Options panel sets `WindowFlags.NoMove` on all three panels
- **Game UI toggle** — icon button hides/shows the native FFXIV HUD while keeping ImGui windows visible (controlled by `AutoHideGameUi`; only toggles UI visibility, never forces it off at open unless `AutoHideGameUi` is persisted as `true`)
- **Free cam** — icon button runs `/cammy freecam` via Dalamud's `ICommandManager` when the [Cammy plugin](https://github.com/Ottermandias/Cammy) is installed; the button stays disabled otherwise and highlights green when free cam is active (detected by `cam->MaxDistance <= 0.1f`)
- **Camera height slider** — `ImmersiveDresserCameraY` (range `-2`…`2`) offsets the scene camera's Y while the dresser is open. The camera-update detour clamps to ground via a `BGCollisionModule` raycast (unless `AllowCameraClipping` is enabled) and writes the clamped value back so the slider reflects reality
- **Disable first person** — `DisableFirstPerson` hooks `CanChangePerspective` to force 0, and snaps out of first-person on toggle-on if already there
- **Character rotation** — tree header in the Options panel exposes the [Character Rotation](#10-character-rotation) drawer
- **Design actions row** (Options panel, Equipment mode) — icon buttons for clipboard-in / clipboard-out / save-as-design / undo using `DesignConverter` + `EditorHistory`. Modifiers (CTRL/Shift) toggle gear-only vs. customization-only, matching upstream's standard apply-rules pattern
- **ESC** closes the dresser (detected globally regardless of window focus via `IKeyState`, input consumed to prevent the game system menu)
- **Individual panel close** — closing any panel's title bar calls `manager.Close()`, which tears down all three plus overlay state
- Window positions are **remembered across sessions** — ImGui persists positions via its ini file; default centered layout only applies on first use
- Gated by the `EnableImmersiveDresser` configuration toggle in the Glamorous Terror section of Settings

### Implementation

| File | Role |
|------|------|
| `Glamourer/GlamorousTerror/ImmersiveDresser/ImmersiveDresserWindow.cs` | `ImmersiveDresserManager` (`IService + IDisposable`) + three nested `Window` panel classes + camera hooks |
| `Glamourer/GlamorousTerror/ContextMenu/ContextMenuService.cs` | Adds "Immersive Dresser" `MenuItem` with player-only guard, click → `Open()` |
| `Glamourer/Services/CommandService.cs` | Registers `/glamour`/`/gt` alias; `dresser` sub-command → `Open()` |
| `Glamourer/Gui/GlamourerWindowSystem.cs` | Registers `Left`, `Right`, `Options` windows with Dalamud's window system |
| `Glamourer/Gui/Equipment/EquipmentDrawer.cs` | `DrawEquipIcon` / `DrawBonusItemIcon` / `DrawSingleWeaponIcon` / `DrawAllStain` / `DrawMetaToggle` — all `internal`/`public` so the dresser panels can drive them |
| `Glamourer/GlamorousTerror/Config/Configuration.GT.cs` | `EnableImmersiveDresser`, `AutoHideGameUi`, `LockImmersiveDresserPanels`, `ImmersiveDresserCameraY`, `AllowCameraClipping`, `DisableFirstPerson` |
| `Glamourer/GlamorousTerror/Config/SettingsTab.GT.cs` | Master toggle in the Glamorous Terror section |

**ContextMenuService** adds a `MenuItem` with `PrefixChar = 'G'` and `Name = "Immersive Dresser"`. In `OnMenuOpened`, the item is only added when `config.EnableImmersiveDresser` is true AND the target game object is the local player (`(nint)gameObject.Address == (nint)_objects.Player`). The click handler calls `_immersiveDresser.Open()`.

**Command** — `CommandService` recognizes `dresser` as an argument to `/glamour` (alias `/gt`) and calls `ImmersiveDresserManager.Open()`.

### ImmersiveDresserManager

`ImmersiveDresserManager` implements `IService` and `IDisposable`. Key fields:

| Field | Purpose |
|-------|---------|
| `Left`, `Right`, `Options` | The three panel `Window` instances |
| `_currentMode` | `DresserMode.Equipment` or `DresserMode.Appearance` — controls which drawer each panel shows |
| `_showParameters` | Whether the Right panel shows `CustomizeParameterDrawer` in Appearance mode |
| `_isOpen` | Re-entrancy guard for `Open()` / `Close()` |
| `_didHideUi`, `_wasUiVisible`, `_savedDisableUserUiHide` | Save/restore state for the game-UI hide toggle |
| `_cammyFreeCamActive`, `_lastValidCameraY` | Free-cam and camera-height detour state |
| `_cameraUpdateHook`, `_canChangePerspectiveHook` | Dalamud `Hook<>`s attached via vtable offsets 3 and 22 on the active `CameraBase` |

**Constructor** — Receives `EquipmentDrawer`, `CustomizationDrawer`, `CustomizeParameterDrawer`, `PreviewService`, `StateManager`, `ActorObjectManager`, `Configuration`, `IUiBuilder`, `IKeyState`, `IFramework`, `ICommandManager`, `IGameInteropProvider`, `RotationDrawer`, `DesignConverter`, `DesignManager`, `EditorHistory` via DI. Creates the three panel instances and installs the camera-update / `CanChangePerspective` hooks from the active camera's vtable.

**`Open()`** — Guarded by `_isOpen`:

1. Resets `ImmersiveDresserCameraY` to `0f`
2. Saves the current `IUiBuilder.DisableUserUiHide` value, sets it to `true`
3. Subscribes to `IFramework.Update` for ESC polling
4. Enables both camera hooks
5. If `AutoHideGameUi` is `true`, hides the game UI and records `_didHideUi = true`
6. Opens all three panel windows

**`Close()`** — Guarded by `_isOpen`:

1. Unsubscribes from `IFramework.Update`
2. Calls `_rotationDrawer.Reset()` so any active rotation override is cleared
3. If free-cam was active, toggles it back off by sending `/cammy freecam` again
4. Disables both camera hooks
5. Closes all three windows
6. Restores game-UI visibility (only if it was hidden by the dresser)
7. Restores the saved `DisableUserUiHide` value

**`Dispose()`** — Calls `Close()` if still open, then disposes both camera hooks.

### Panel Details

All three panels share:

- **`PanelFlags`**: `NoTitleBar | NoDocking | AlwaysAutoResize | NoCollapse` — auto-sized to content
- In **Appearance mode** the left panel clears `NoTitleBar | NoCollapse` so the customization drawer has a title bar (easier to resize/move). The **Options** panel always clears those flags (so the user can always collapse it)
- **`DrawConditions()`**: Returns `objects.Player.Valid` — panels only render when the player object exists (the Right panel additionally requires `_currentMode is Equipment || _showParameters`)
- **`PreDraw()`**: Positions the window via `Im.Window.SetNextPosition(center ± offset, Condition.FirstUseEver, pivot)` — ImGui remembers user repositioning via its ini file. When `LockImmersiveDresserPanels` is set, `WindowFlags.NoMove` is OR'd in each frame
- **`OnClose()`**: Delegates to `manager.Close()` — safe against re-entrancy due to the `_isOpen` guard

**EquipmentPanel** (left of center, pivot `1, 0.5`):

- **Equipment mode**: `equipmentDrawer.Prepare()`, draws Main Hand weapon icon (`DrawSingleWeaponIcon`), iterates `EquipSlotExtensions.EquipmentSlots` → `DrawEquipIcon`, iterates `BonusExtensions.AllFlags` → `DrawBonusItemIcon`, then `ApplyHoverPreview`
- **Appearance mode**: draws `CustomizationDrawer` (with full-customize change dispatch), then `customizationDrawer.ApplyHoverPreview(stateManager, state)`

**AccessoryPanel** (right of center, pivot `0, 0.5`):

- **Equipment mode**: draws off-hand only when `offhand.CurrentItem.Type is not FullEquipType.Unknown` (classes like DRG/MNK show no gap), iterates `EquipSlotExtensions.AccessorySlots` → `DrawEquipIcon`, then `ApplyHoverPreview`
- **Appearance mode**: draws `CustomizeParameterDrawer` (gated by `_showParameters`)

**OptionsPanel** (below center, pivot `0.5, 0`):

1. **Mode switch** — "Switch to Appearance" / "Switch to Equipment" button (leaving Appearance calls `PreviewService.EndCustomizationPopupFrame(state)` to drop any active popup preview)
2. **Reset to Game State** — `stateManager.ResetState(state, StateSource.Manual, isFinal: true)`
3. **Design actions row** (`DrawDesignActions`) — clipboard-in / clipboard-out / save / undo icon buttons, with modifier-driven apply rules (`UiHelpers.ConvertKeysToBool()`)
4. **Right-aligned icon buttons** — game-UI eye, panel lock, free-cam video; positioning computed from `Im.ContentRegion.Available.X` so they sit flush right on the same line as the design actions row
5. **Dye All Slots + meta toggles** (Equipment mode only):
   - `equipmentDrawer.DrawAllStain()` combo; on selection, writes `StainIds.All(newAllStain)` to every `EqdpSlot` via `stateManager.ChangeStains`
   - Always followed by `equipmentDrawer.ApplyAllStainHoverPreview(stateManager, state)` (the preview dispatcher specific to this panel)
   - Four inline meta-toggle groups: `HatState + Head Crest`, `VisorState + Body Crest`, `WeaponState + OffHand Crest`, `EarState` alone
6. **Camera tree header** (hidden when free-cam is active) — `ImmersiveDresserCameraY` slider + Reset, `AllowCameraClipping` checkbox, `DisableFirstPerson` checkbox (the latter forces a third-person snap when toggled on while already in first person)
7. **Character Rotation tree header** — `manager._rotationDrawer.Draw(objects.Player)` (see [Character Rotation](#10-character-rotation))
8. **Show Color Customization** (Appearance mode only) — toggles `_showParameters` to reveal the Right panel's parameter drawer
9. **Save as Design popup** — `InputPopup.OpenName(...)` prompts for a name and calls `DesignManager.CreateClone(_newDesign, name, true)`

### Camera Hooks

Two vtable hooks on the active `CameraBase`:

- **`CameraUpdateDetour` (vtable[3])**: Runs after the original camera update. If `ImmersiveDresserCameraY != 0`, it computes a candidate Y, optionally clamps against ground (via `BGCollisionModule.RaycastMaterialFilter` with a `minHeightAboveGround = 0.5f`), writes the clamped offset to both `SceneCamera.Position.Y` and `SceneCamera.LookAtVector.Y`, and — if the clamp changed the offset — writes the clamped value back into `ImmersiveDresserCameraY` so the slider and reality stay in sync.
- **`CanChangePerspectiveDetour` (vtable[22])**: Returns `0` when `DisableFirstPerson` is set, otherwise defers to the original.

Both hooks are enabled in `Open()` and disabled in `Close()`. They are installed lazily in the constructor from the active camera's vtable — if no active camera exists at startup, both hook fields stay null.

### Game UI Hiding

- `RaptureAtkModule.Instance()->IsUiVisible` is used to toggle the native HUD
- `_wasUiVisible` records the state before hiding so `RestoreGameUi` restores correctly even when the UI was already hidden when the dresser opened
- `IUiBuilder.DisableUserUiHide = true` is set on `Open()` so Dalamud keeps rendering ImGui when the game HUD is hidden — **required**, otherwise the dresser panels disappear with the game UI
- The eye-icon button in the Options panel routes through `SetAutoHideUi(bool)` which flips the module bit and persists `AutoHideGameUi`

### ESC Key Handling

- `IKeyState` is polled on `IFramework.Update` — this reads the game's keyboard state directly, working regardless of whether any ImGui window has focus
- When ESC is detected while the dresser is open, the key is consumed (`_keyState[VirtualKey.ESCAPE] = false`) to prevent the game's system menu from opening simultaneously
- The framework subscription is only active while the dresser is open — added in `Open()`, removed in `Close()` — so there is zero overhead when the dresser is closed

### Configuration

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `EnableImmersiveDresser` | `bool` | `true` | Gates the context menu entry (command is always available when the service is loaded) |
| `AutoHideGameUi` | `bool` | `false` | Hide the FFXIV HUD automatically when the dresser opens |
| `LockImmersiveDresserPanels` | `bool` | `false` | Pins all three panels in place (`WindowFlags.NoMove`) |
| `ImmersiveDresserCameraY` | `float` | `0f` | Camera Y-offset while open; auto-reset to 0 on `Open()` |
| `AllowCameraClipping` | `bool` | `false` | Skip ground-ray clamping on the camera Y offset |
| `DisableFirstPerson` | `bool` | `false` | Force the `CanChangePerspective` detour to deny first-person |

### Known Pitfalls

1. **`DisableUserUiHide` must be set before hiding**: Hiding the game UI via `RaptureAtkModule.IsUiVisible = false` also hides all ImGui windows unless `IUiBuilder.DisableUserUiHide` is set to `true` first.

2. **`Close()` re-entrancy**: Each window's `OnClose()` callback calls `manager.Close()`. Without the `_isOpen` guard, `Close()` would execute its restore logic multiple times. The guard ensures only the first call runs the full teardown.

3. **ESC requires `IKeyState` via `IFramework.Update`**: The ImGui keyboard API only detects keys when an ImGui window has focus. With `NoTitleBar` panels and no guaranteed focus, `IKeyState` (Dalamud's game keyboard state) must be used instead, polled on the framework update thread. After detecting ESC, the key must be consumed (`_keyState[VirtualKey.ESCAPE] = false`) to prevent the game from also processing it and opening the system menu.

4. **Camera hook vtable offsets**: The detours attach to vtable indices 3 (camera update) and 22 (`CanChangePerspective`). If FFXIVClientStructs changes its layout or the game patches the camera class, these offsets must be re-verified. The hooks are nullable and silently no-op if the active camera was null at construction.

5. **Camera-offset writeback**: When the ground raycast clamps `candidateY`, the clamped offset is written back into `ImmersiveDresserCameraY` so the slider reflects the actual applied offset. Without this writeback the slider would show a larger value than the camera is using.

6. **Free-cam detection is heuristic**: Free-cam is inferred from `cam->MaxDistance <= 0.1f` plus the `/cammy` command being registered. There is no IPC handshake with Cammy — if Cammy changes how it marks the camera, this detection must be revisited.

7. **`ApplyAllStainHoverPreview` must be called by whatever panel draws `DrawAllStain`**: The Options panel draws it, so the Options panel calls it. If a new surface adds a Dye-All-Slots combo and forgets the dispatcher, the preview will stick on the character after the popup closes.

---

## Configuration Summary

All GlamorousTerror-specific properties live in `Glamourer/GlamorousTerror/Config/Configuration.GT.cs` (a partial of the upstream `Configuration` class). Fun Modes properties remain on the upstream `Configuration` partial.

| Property | Type | Default | Feature |
|----------|------|---------|---------|
| `EnableGameContextMenu` | `bool` | `true` | Context Menu |
| `EnableImmersiveDresser` | `bool` | `true` | Immersive Dresser |
| `AutoHideGameUi` | `bool` | `false` | Immersive Dresser |
| `LockImmersiveDresserPanels` | `bool` | `false` | Immersive Dresser |
| `ImmersiveDresserCameraY` | `float` | `0f` | Immersive Dresser (camera) |
| `AllowCameraClipping` | `bool` | `false` | Immersive Dresser (camera) |
| `DisableFirstPerson` | `bool` | `false` | Immersive Dresser (camera) |
| `UseIconEquipmentDrawer` | `bool` | `false` | Icon Equipment Drawer |
| `IconPickerMaxRows` | `int` | `10` | Icon Equipment Drawer |
| `GroupIconPickerByModel` | `bool` | `true` | Icon Equipment Drawer |
| `KeepIconPickerOpen` | `bool` | `false` | Icon Equipment Drawer |
| `EnabledCheats` | `CodeService.CodeFlag` | `0` | Fun Modes |
| `FestivalMode` | `FestivalSetting` | `Undefined` | Fun Modes (festivals) |
| `LastFestivalPopup` | `DateOnly` | `MinValue` | Fun Modes (festivals) |
| `EquipmentNameLanguage` | `EquipmentNameLanguage` | `GameDefault` | Equipment Language |
| `CrossLanguageEquipmentSearch` | `bool` | `false` | Cross-Language Search |
| `OwnedOnlyComboFilter` | `bool` | `false` | Owned-Only Combo Filter |
| `OwnedComboFilterSources` | `ItemUnlockManager.ItemSource` | `All` | Owned-Only Combo Filter |

Session-scoped state (not persisted): icon picker filter/sort state, `RotationService` overrides, `ImmersiveDresserManager._currentMode`, `_showParameters`, and all `PreviewService` state.

Separate on-disk files:

- `unlocks_items_{contentId:X16}.dat` — per-character item ownership (see [Owned-Only Combo Filter](#7-owned-only-combo-filter))
- `FavoriteFile` (global) — JSON v1 favorites (see [Favorites](#9-favorites))

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────┐
│                       User Interface                          │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐    │
│  │ ActorPanel   │  │ ContextMenu  │  │ CodeDrawer       │    │
│  │ DesignPanel  │  │ Service +    │  │ (Fun Modes UI)   │    │
│  │ (icon or     │  │ PopupMenu    │  │                  │    │
│  │  name combos)│  │              │  │                  │    │
│  └──────┬───────┘  └──────┬───────┘  └────────┬─────────┘    │
│         │                 │                    │              │
│  ┌──────────────────────────────────────────────────────┐    │
│  │             ImmersiveDresserManager                  │    │
│  │  3 panels (Equipment/Customization, Accessories/     │    │
│  │  Parameters, Options) + camera hooks + /gt dresser   │    │
│  │  command + Character Rotation drawer                 │    │
│  └──────────────┬───────────────────────────────────────┘    │
│                 │                                             │
│  ┌──────────────▼────────┐  ┌──────────────────────────┐     │
│  │    EquipmentDrawer    │  │   CustomizationDrawer    │     │
│  │  (IconMode + combos,  │  │  (popup hover dispatch,  │     │
│  │   ApplyHoverPreview,  │  │   icon/list/color)       │     │
│  │   ApplyAllStainHover) │  │                          │     │
│  └──────────────┬────────┘  └──────────┬───────────────┘     │
│                 │                       │                     │
│  ┌──────────────▼───────────────────────▼──────────────┐     │
│  │               PreviewService                         │     │
│  │  Types: SingleItem, SingleCustomization, SingleStain │     │
│  │  AllSlotsStain, Equipment, Appearance, Design,       │     │
│  │  FullDesignToSelf/Target, Automation, Reset          │     │
│  └──────────────┬───────────────────────────────────────┘     │
│                 │                                             │
│  ┌──────────────▼──────────────────────────────────────┐     │
│  │                 StateManager                         │     │
│  │  (Actor state, design application, game memory)      │     │
│  └──────────────┬───────────────────────────────────────┘     │
│                 │                                             │
│  ┌──────────────▼──────┐  ┌─────────────────────────┐        │
│  │   FunModule         │  │  AutoDesignApplier      │        │
│  │   (Transforms on    │  │  (.Wildcard partial —   │        │
│  │    load)            │  │   wildcard targeting)   │        │
│  └─────────────────────┘  └─────────────────────────┘        │
│                                                               │
│  ┌────────────────────┐  ┌─────────────────────────┐         │
│  │  ItemNameService   │  │   RotationService       │         │
│  │  (lang override +  │  │   (per-actor quaternion │         │
│  │   cross-language)  │  │    overrides, framework │         │
│  │                    │  │    update loop)         │         │
│  └────────────────────┘  └─────────────────────────┘         │
│                                                               │
│  ┌─────────────────────┐  ┌────────────────────────┐         │
│  │  ItemUnlockManager  │  │   FavoriteManager      │         │
│  │  (per-character     │  │   (global favorites,   │         │
│  │   ownership, source │  │    items/stains/hair/  │         │
│  │   flags, pruning)   │  │    bonus items)        │         │
│  └─────────────────────┘  └────────────────────────┘         │
└──────────────────────────────────────────────────────────────┘
```
