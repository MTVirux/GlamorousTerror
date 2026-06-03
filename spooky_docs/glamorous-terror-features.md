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
12. [UI Actor Glamour Mirroring](#12-ui-actor-glamour-mirroring)

### Source Layout

Fork-specific code lives under `Glamourer/GlamorousTerror/` and is organized by feature:

| Subdirectory | Feature(s) |
|--------------|-----------|
| `ContextMenu/` | Context Menu, CharacterPopupMenu |
| `PreviewOnHover/` | PreviewService, per-drawer hover wiring, DesignPreviewService |
| `WildcardAutomation/` | `AutoDesignApplier.Wildcard.cs`, `WildcardIdentifier.cs`, `GTActorIdentifierJson.cs` |
| `EquipmentLanguage/` | `ItemNameService.cs` (language override + cross-language names) |
| `ItemOwnership/` | `ItemUnlockManager.cs`, `CustomizeUnlockManager.cs`, `FavoriteManager.cs`, owned-only combo filter UI, unlock serialization |
| `IconEquipment/` | `EquipmentDrawer.IconMode.cs` (icon grid + icon picker popup) |
| `ImmersiveDresser/` | `ImmersiveDresserWindow.cs` (manager + 3 panel classes) |
| `CharacterRotation/` | `RotationService.cs`, `RotationDrawer.cs` |
| `UiActorMirror/` | `UiActorMirrorService.cs`, `UiActorPreviewSlots.cs`, `UiActorSurface.cs`, `StateListener.UiActor.cs` (UI/menu actor glamour mirroring) |
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
| `Glamourer/GlamorousTerror/ContextMenu/ContextMenuService.cs` | Hooks Dalamud's `IContextMenu`. Adds a player-character entry ("Glamorous Terror"), a "Try On" entry on inventory/ItemSearch/ChatLog/RecipeNote addons, and an "Immersive Dresser" entry on the local player only. Partial class — store/exchange handling lives in `ContextMenuService.Shops.cs` |
| `Glamourer/GlamorousTerror/ContextMenu/ContextMenuService.Shops.cs` | `GTTryAddShopItem` — adds the same "Try On" entry in vendor/exchange addons (`Shop`, `ShopExchangeItem`, `ShopExchangeCurrency`, `ShopExchangeCoin`, `GrandCompanyExchange`, `FreeCompanyExchange`, `InclusionShop`). Reads the hovered item from `AgentItemDetail.ItemId` (the tooltip agent), falling back to `AgentRecipeItemContext.ResultItemId`. Called from `OnMenuOpened`'s default branch |
| `Glamourer/GlamorousTerror/ContextMenu/CharacterPopupMenu.cs` | ~1000-line custom ImGui popup with all menu logic |
| `Glamourer/GlamorousTerror/PreviewOnHover/PreviewService.cs` | Preview engine shared with equipment/customization drawers |

**ContextMenuService** implements `IRequiredService`. The `MenuItem` instances (`_inventoryItem`, `_characterItem`, `_immersiveDresserItem`) are built **once in the constructor**, not per-event. `OnMenuOpened` filters by addon and adds the appropriate pre-built item:

- Character-type menus → `_characterItem` (`PrefixChar = 'G'`, name "Glamorous Terror"); the click handler fires `_popupMenu.Open(actor, name)`
- Local-player character menus AND `EnableImmersiveDresser` is true → additionally append `_immersiveDresserItem`
- Inventory / ItemSearch / ChatLog / RecipeNote → `_inventoryItem` ("Try On"), which opens the on-self preview path
- Vendor / exchange addons (`Shop`, `ShopExchangeItem`, `ShopExchangeCurrency`, `ShopExchangeCoin`, `GrandCompanyExchange`, `FreeCompanyExchange`, `InclusionShop`) → `GTTryAddShopItem` adds the same `_inventoryItem` ("Try On"). These addons do **not** populate `AgentRecipeItemContext`, so the item is read from `AgentItemDetail.ItemId` (the tooltip agent that tracks the hovered item), with `AgentRecipeItemContext.ResultItemId` as a fallback

**CharacterPopupMenu** registers on `_uiBuilder.Draw += OnDraw` and renders an ImGui popup (`Im.Popup.Begin("GlamorousTerrorPopup")`). Key draw methods:

- `DrawEquipmentSubmenu()` — iterates the 14-entry `EquipmentSlots` array (the first entry is "All" / `EquipSlot.Unknown`; the remaining 13 are Head → Facewear) and calls `PreviewService.StartEquipmentPreview(_lastActor, slot, isBonusItem, toSelf)` on hover. Weapon entries (main/off hand) are filtered out via `AreJobsCompatible()` when source and target jobs differ — gear that the target couldn't realistically equip is hidden to avoid offering nonsensical transfers.
- `DrawAppearanceSubmenu()` — iterates 19 appearance groups and calls `PreviewService.StartAppearancePreview(_lastActor, flag, toSelf)` on hover
- `DrawDesignSubmenu()` — recursively walks `DesignFileSystem` tree and calls `_previewService.ApplyDesignPreview(design)` on hover
- `ApplyPreviewPermanently(Action)` — clears the preview state without restoring (so the value stays written into game memory) then runs the action. The action itself is responsible for applying with `ApplySettings.Manual with { IsFinal = true }` — `ApplyPreviewPermanently` does NOT set `IsFinal` itself
- `CheckAndEndPreview()` — restores original state when nothing is hovered. Submenu open paths additionally call `_previewService.EndPreview()` when entering a submenu whose preview *type* differs from the currently active preview — without this "submenu cross-revert", hovering an Equipment submenu after just leaving an Appearance submenu would leak the appearance preview onto the actor

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
| `EnableGameContextMenu` | `bool` | `true` | Upstream `Configuration.cs:39` (NOT `Configuration.GT.cs` — this is one of the known config-placement quirks; see [upstream-hooks.md → Configuration Field Conflicts](upstream-hooks.md#configuration-field-conflicts-carried-since-1614-still-present-as-of-1616)) |

Controlled in the Settings tab (Glamorous Terror section) via `ContextMenuService.Enable()` / `Disable()`. Because the field lives on the upstream `Configuration`, upstream's own settings tab also surfaces a checkbox for it — two checkboxes bind to the same flag until one is consolidated.

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
| `LastAppliedItem` | `EquipItem?` | Last item written by `PreviewSingleItem`/`PreviewSingleBonusItem`; restore is gated on the current state still matching this so external mutations (right-click clear, IPC) aren't reverted |
| `LastAppliedStain` | `StainIds?` | Last stain set written by `PreviewSingleStain`; same gating rule as `LastAppliedItem` for `SingleStain` restores |
| `ToSelf` | `bool` | Whether previewing changes to self |
| `PopupActiveThisFrame` | `bool` | Per-frame flag set by popup drawing code |
| `ActivePopupType` | `PopupType` | Icon, List, or Color |
| `PopupHoveredIndex` | `int?` | Which option is being hovered |
| `PopupHoveredValue` | `CustomizeValue` | The customize value of the hovered option |
| `PopupSelectionMade` | `bool` | Whether the user clicked to select |
| `RequiresCtrl` | `bool` | Whether CTRL must be held for this preview |

**`PreviewService`** — Methods organized by lifecycle:

- **Start**: `StartSingleItemPreview()`, `StartSingleBonusItemPreview()`, `StartSingleCustomizationPreview(state, index, requiresCtrl)`, `StartSingleStainPreview()`, `StartAllSlotsStainPreview()`
- **Apply**: `PreviewSingleItem()`, `PreviewSingleBonusItem()`, `PreviewSingleStain()`, `PreviewSingleCustomization(state, index, value)`, `PreviewAllSlotsStain(state, stainValue)`, `HandleCustomizationPopupFrame(state, index, hoveredIndex, hoveredValue, ctrlHeld)`
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
- `IsPopupOpen` (`bool`) — set to `true` in `PreDrawList()` (which is only called when the popup is actually rendering), reset to `false` at the start of each `Draw()` / `DrawBehavior()` call
- `ItemSelected` (`bool`) — set to `true` when an item is clicked, in **both** the `Draw` and `DrawBehavior` selection branches

**CRITICAL: `Draw` and `DrawBehavior` parity** — `Draw` renders the combo button; `DrawBehavior` runs only the popup behavior (used by surfaces that draw their own button — compact mode and the Equipment Bar). Both must reset `IsPopupOpen`/`HoveredItem` at frame start and set `ItemSelected = true` on selection. Upstream's `DrawBehavior` does not, so GT extends it. Without this, compact-mode dropdowns leak state between frames: after the first popup opens, `IsPopupOpen` stays `true`, `ApplyHoverPreview`'s loop keeps `return`ing on that slot, and other slots' previews never run.

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
| `Glamourer/Gui/Customization/CustomizationDrawer.cs` | `PreviewService` constructor param, popup flag reset via `GTResetPopupFlags()` partial-method call in `Init()` (run at the top of every `Draw(...)`) |
| `Glamourer/GlamorousTerror/PreviewOnHover/DesignPreviewService.cs` | Parallel preview service used by the design tab's `DesignFileSystemSelector` for hover preview. Duplicates a chunk of `PreviewService.ApplyDesignPreview` — flagged as a future cleanup target (consolidate into `PreviewService`) |
| `Glamourer/GlamorousTerror/PreviewOnHover/CustomizationDrawer.Preview.cs` | Public `ApplyHoverPreview()` dispatcher, `_iconPopupOpen/_listPopupOpen/_colorPopupOpen` state, `ApplyIconHoverPreview()`/`ApplyListHoverPreview()`/`ApplyColorHoverPreview()` sub-methods |
| `Glamourer/Gui/Customization/CustomizationDrawer.Icon.cs` | Hover tracking in `DrawIconPickerPopup()` — sets `_iconPopupOpen`, `_iconHoveredValue`, `_iconSelectionMade` |
| `Glamourer/Gui/Customization/CustomizationDrawer.Simple.cs` | Hover tracking in `ListCombo0()`/`ListCombo1()` — sets `_listPopupOpen`, `_listHoveredValue`, `_listSelectionMade` |
| `Glamourer/Gui/Customization/CustomizationDrawer.Color.cs` | Hover tracking in `DrawColorPickerPopup()` — sets `_colorPopupOpen`, `_colorHoveredValue`, `_colorSelectionMade` |

**CRITICAL: Popup flag clobbering pattern** — Multiple icon selectors (Face, Hairstyle, etc.) and multiple color pickers are drawn in a loop. Each popup draw method was originally setting `_iconPopupOpen = false` when *its* popup wasn't open. If Face's popup was open, Face's draw set `true`, then Hairstyle's draw immediately set `false`. Solution:

- **Reset all three flags once per frame** in `Init()` via the partial method `GTResetPopupFlags()` (declared in upstream `CustomizationDrawer.cs`, body in `CustomizationDrawer.Preview.cs`). `Init` runs at the top of every `Draw(...)`, so this fires every frame even when no popup body actually executes — which is the whole point: when a popup *closes*, its body never runs again, so the flag would otherwise latch `true` forever and keep a stale `SingleCustomization` preview alive (re-applying the captured value over external mutations like "Revert to Game State")
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

### Wiring in Panels

In `Glamourer/Gui/Tabs/ActorTab/ActorPanel.cs`:

- `DrawCustomizationsHeader()` calls `_customizationDrawer.ApplyHoverPreview(_stateManager, _selection.State!)` after drawing customizations
- `DrawEquipmentHeader()` calls, in this order after equipment draws and drag-drop tooltip:
  1. `_equipmentDrawer.ApplyHoverPreview(_stateManager, _selection.State!)` — per-slot combos + icon picker
  2. `_equipmentDrawer.ApplyAllStainHoverPreview(_stateManager, _selection.State!)` — Dye All Slots combo

The Immersive Dresser's `OptionsPanel` likewise calls `ApplyAllStainHoverPreview` after `DrawAllStain` (the panel that actually draws the all-stain combo).

`Glamourer/Gui/EquipmentBarWindow.cs` (the floating equipment bar) calls `_equipmentDrawer.ApplyHoverPreview(_stateManager, _selection.State!)` after `DrawDragDropTooltip()`. The bar does **not** draw `DrawAllStain`, so `ApplyAllStainHoverPreview` is intentionally omitted — per the rule that the all-stain dispatcher must be called only from panels that contain that combo.

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
CustomizationDrawer.Init() runs at top of Draw(...)
  → GTResetPopupFlags() → _iconPopupOpen = false; _listPopupOpen = false; _colorPopupOpen = false

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

8. **Compact-mode `DrawBehavior` parity**: `BaseItemCombo` exposes both `Draw` (renders the combo button + popup) and `DrawBehavior` (popup only — used by `CompactDrawer.ItemCombo` and `CompactSmallDrawer.ItemCombo`, which means the Equipment Bar in particular). Upstream's `DrawBehavior` neither resets `IsPopupOpen`/`HoveredItem` per frame nor sets `ItemSelected` on click. GT must extend it to match `Draw`. Without this fix the very first popup that opens latches `IsPopupOpen = true` for that slot's combo forever, which makes `ApplyHoverPreview`'s loop short-circuit on that stale slot and silently drop preview for every other slot once the user makes a selection.

9. **External mutation while preview active (`LastAppliedItem`/`LastAppliedStain`)**: `RestoreSingleValuePreview` for `SingleItem` and `SingleStain` is gated on whether the current state still matches the value the preview last wrote. `PreviewSingleItem`/`PreviewSingleBonusItem`/`PreviewSingleStain` record their write into `State.LastAppliedItem` / `State.LastAppliedStain`; the restore branches early-return when the field is null or the live state has diverged. This prevents the fall-through `EndSingleValuePreview` from clobbering side actions that fired the same frame the popup closed — most notably right-click clear (`ResetOrClear` → `SetItem(NothingItem)`), which would otherwise be reverted to the originally-captured item. The fields are reset in `PreviewState.End()`. `SingleCustomization` and `AllSlotsStain` are not affected by this gating because they don't share the right-click clear pathway.

10. **`CustomizationDrawer.ApplyHoverPreview` must use `if/else if/else`**: Sequential `if`s would each invoke `EndCustomizationPopupFrame` for the inactive branches, killing the active preview every frame. The dispatcher in `Glamourer/GlamorousTerror/PreviewOnHover/CustomizationDrawer.Preview.cs` must remain `if (_iconPopupOpen) / else if (_listPopupOpen) / else if (_colorPopupOpen) / else EndCustomizationPopupFrame()`. (This is implied by the implementation section's "Only one EndCustomizationPopupFrame call" rule, but it is also CLAUDE.md Critical Invariant #6 — listed here so upstream-merge readers see all critical invariants in the same place.)

11. **Panel call order matters in `ActorPanel.DrawHumanPanel`**: The header methods run in the order `DrawCustomizationsHeader()` then `DrawEquipmentHeader()` (`ActorPanel.cs:135-136`). This means `CustomizationDrawer.ApplyHoverPreview` runs FIRST per frame, then `EquipmentDrawer.ApplyHoverPreview` observes the state the customization dispatcher just set. Inverting the order would break the type-guarded fall-throughs that protect customization previews from being ended by the equipment drawer (and vice versa).

---

## 3. Wildcard Automation Targets

Extends the automation system to allow **wildcard patterns** (`*`) and **single-character wildcards** (`?`) in character names for auto-design sets.

### User-Facing Functionality

- Pattern matching in character names: `Tank*@Excalibur`, `*Healer`, `Raid Alt*`
- `*` matches zero or more characters; `?` matches exactly one character
- Case-insensitive matching on raw UTF-8 bytes — but case folding only applies to **ASCII A–Z** (the `AsciiToLower` helper). Non-ASCII bytes (accented letters, em-dashes, CJK) match byte-for-byte without case folding
- **Runtime wildcard matching is Player-only.** Wildcard identifiers can be *authored* in the automation editor for Player, Owned, and Retainer types (see `WildcardIdentifier.OwnedOrFallback` / `RetainerOrFallback`), and they will be serialized to disk correctly, but the dispatch in `GetPlayerSet` only calls `TryGettingSetExactOrWildcard` for `IdentifierType.Player`. Owned-type and Retainer-type sets fall back to exact lookup only. This will surprise users who configure `Tank*Mount` (Owned) wildcards
- World matching: exact world or `AnyWorld` (where `AnyWorld` matches everything)
- Exact match is tried first (with the original world, then a second pass against `WorldId.AnyWorld`); wildcard iteration only runs on miss
- No user-facing toggle — wildcard support is always on. Whether a given set uses wildcards is determined by `*` / `?` characters in the configured `PlayerName`

### Implementation

| File | Role |
|------|------|
| `Glamourer/GlamorousTerror/WildcardAutomation/AutoDesignApplier.Wildcard.cs` | Partial-class extension of upstream `AutoDesignApplier`; wildcard name matching against enabled sets |
| `Glamourer/GlamorousTerror/WildcardAutomation/WildcardIdentifier.cs` | Constructs `ActorIdentifier`s for `*`-bearing names via upstream's public `ActorManager.CreateIndividualUnchecked` API (bypasses SE name validation); falls back to the validated factory for non-wildcard names. Exposes `PlayerOrFallback`, `RetainerOrFallback`, `OwnedOrFallback`, and two `IsWildcard` overloads (`ByteString` / `string?`) for callers that want to check before authoring |
| `Glamourer/GlamorousTerror/WildcardAutomation/GTActorIdentifierJson.cs` | Wraps `ActorManager.FromJson` so config loads with `*` in `PlayerName` route through `WildcardIdentifier` instead of the validated parse. Falls back to `actors.FromJson(data)` for non-wildcard names and for unknown identifier types |

The feature **no longer requires a `Penumbra.GameData` fork**: wildcard identifiers are constructed entirely through upstream's public `CreateIndividualUnchecked` entry point, so the submodule tracks vanilla Ottermandias `upstream/main`. JSON load is intercepted in `AutoDesignManager.LoadV1` (see [upstream-hooks.md #11a](upstream-hooks.md)), and the automation editor is intercepted in `IdentifierDrawer.UpdateIdentifiers` (#11b), which means wildcard names can now be typed directly into the UI rather than only being authored by hand-editing the config file. The loaded identifier is also passed through `WithoutIndex()` before being stored (`AutoDesignManager.cs:526, 560`), stripping any stale object index so equality checks against runtime-generated identifiers remain stable.

**Combined dispatch via upstream `GetPlayerSet` + GT fallback** (`AutoDesignApplier.cs:315-346`):

The full Player-type lookup runs three steps before giving up:

1. `_manager.EnabledSets.TryGetValue(identifier)` — exact match with the original `WorldId`. Fast path; most sets resolve here.
2. **AnyWorld retry**: build a second identifier with `actors.CreatePlayer(name, WorldId.AnyWorld)` and try `TryGetValue` again. This is how a single set authored against `Tank Alt@AnyWorld` matches the same name on any data center.
3. `TryGettingSetExactOrWildcard(identifier, out set)` — the GT fallback (only called for `IdentifierType.Player`). Iterates `EnabledSets` looking for keys with wildcard names.

For Owned / Retainer / NPC identifier types, only step 1 runs (no AnyWorld retry, no wildcard fallback). Wildcards authored for those types will appear in the UI but never match at runtime through this code path.

**`TryGettingSetExactOrWildcard(ActorIdentifier identifier, out AutoDesignSet? set) → bool`** — GT entry point (`AutoDesignApplier.Wildcard.cs:10`):

For each `(key, set)` pair in `EnabledSets`:

1. **Type compatibility**: `if (key.Type != identifier.Type && key.Type is not IdentifierType.Player && identifier.Type is not IdentifierType.Player) continue;` — accepts the pair when either both types are identical OR one side is `Player`. Owned and Retainer wildcards therefore *only* match identifiers of the same type, or against a Player identifier (which is what reaches this method at runtime).
2. **World compatibility**: `if (key.HomeWorld != WorldId.AnyWorld && key.HomeWorld != identifier.HomeWorld) continue;` — skips the pair unless the set's world is `AnyWorld` or matches exactly.
3. **Wildcard name match**: `MatchesWildcard(identifier.PlayerName, key.PlayerName)` (skipping non-wildcard names via `WildcardIdentifier.IsWildcard`).
4. First successful match: assign `set` and return `true`.

If no key matches, `set = null` and returns `false`.

**`MatchesWildcard(ByteString name, ByteString pattern)`** — Unsafe entry point:

- Delegates to `MatchesWildcardInternal` with raw byte pointers and lengths

**`MatchesWildcardInternal(byte* name, int nameLen, byte* pattern, int patternLen)`** — Classic wildcard matching algorithm with backtracking:

- Maintains `nameIdx`, `patternIdx`, `starIdx` (last `*` position), `matchIdx` (backtrack point)
- On `*`: records position, advances pattern
- On `?`: matches any single byte at `nameIdx`, advances both indices (line 58)
- On mismatch: backtracks to last `*` position, advances `matchIdx`
- Case-insensitive via `AsciiToLower(byte)` which converts A-Z to a-z inline — non-ASCII bytes pass through unchanged

**`AsciiToLower(byte)`** — Single-expression helper: `b >= (byte)'A' && b <= (byte)'Z' ? (byte)(b + 32) : b`

### Data Flow

```
Character loads → AutoDesignApplier.GetPlayerSet(identifier)
  ├── if identifier.Type is Player:
  │     1. EnabledSets.TryGetValue(identifier) → exact match with current world? → return
  │     2. Build identifier' = actors.CreatePlayer(name, WorldId.AnyWorld)
  │        EnabledSets.TryGetValue(identifier') → exact match with AnyWorld? → return
  │     3. TryGettingSetExactOrWildcard(identifier, out set):
  │        For each (key, set) in EnabledSets:
  │          → type compat (same OR one is Player)?
  │          → world compat (key is AnyWorld OR equal)?
  │          → MatchesWildcard(identifier.PlayerName, key.PlayerName) →
  │              MatchesWildcardInternal (byte-level, '*'/'?', case-insensitive ASCII, backtracking)
  │          → first match wins, return set
  │     4. No match → return null → no automation applied
  └── else (Owned / Retainer / Npc):
        EnabledSets.TryGetValue(identifier) only — no AnyWorld retry, no wildcard fallback
```

---

## 4. Fun Modes

Cosmetic transformation modes that modify visible players' appearances in real-time (random dyes/clothing, race/gender/size overrides, full-NPC replacements, holiday costumes). The GT fork replaced the old upstream SHA-256 passphrase entry with a plain **checkbox UI** backed by a single serialized `EnabledCheats` bitmask. There is no passphrase, hashing, hint, or per-code plaintext list anymore. The transformation engine (`FunModule`/`FunEquipSet`) is otherwise upstream-shaped and interacts with the GT context menu via the WhoAmI/WhoIsThat clipboard actions.

### How Modes Are Enabled

Each mode is a `CodeService.CodeFlag` bit. The Settings-tab "Fun Modes" header (`CodeDrawer`) renders one checkbox per flag; ticking a box calls `CodeService.Toggle(flag, true)`, which ORs the bit in (after clearing mutually-exclusive bits) and persists the whole mask to `Configuration.EnabledCheats`. Unticking calls `Toggle(flag, false)`. There is also a single **"Disable All"** button (`CodeService.DisableAll()` → mask `0`). State survives sessions because `EnabledCheats` is a serialized config field — `CodeService._enabled` is simply loaded from it in the constructor and written back to it on every toggle.

### CodeFlag enum (`CodeService.cs:11-39`)

```csharp
[Flags] public enum CodeFlag : ulong
{
    Clown        = 0x000001,  // Random Dyes
    Emperor      = 0x000002,  // Random Clothing
    Individual   = 0x000004,  // Random Customizations
    Dwarf        = 0x000008,  // Player Dwarf Mode
    Giant        = 0x000010,  // Player Giant Mode
    OopsHyur     = 0x000020,  // All Hyur
    OopsElezen   = 0x000040,  // All Elezen
    OopsLalafell = 0x000080,  // All Lalafell
    OopsMiqote   = 0x000100,  // All Miqo'te
    OopsRoegadyn = 0x000200,  // All Roegadyn
    OopsAuRa     = 0x000400,  // All Au Ra
    OopsHrothgar = 0x000800,  // All Hrothgar
    OopsViera    = 0x001000,  // All Viera
    AllMale      = 0x002000,  // All Male
    AllFemale    = 0x004000,  // All Female
    SixtyThree   = 0x008000,  // Invert Genders
    Shirts       = 0x010000,  // Show All Items Unlocked
    World        = 0x020000,  // Job-Appropriate Gear
    Elephants    = 0x040000,  // Everyone Elephants
    Crown        = 0x080000,  // Clown Mentors
    Dolphins     = 0x100000,  // Everyone Namazu
    Face         = 0x200000,  // Debug — hidden from UI
    Manderville  = 0x400000,  // Debug — hidden from UI
    Smiles       = 0x800000,  // Debug — hidden from UI
}
```

The enum is `: ulong`. `AllMale = 0x002000` and `AllFemale = 0x004000` occupy what was previously a single reserved bit; they shift every later flag up by two bits relative to the old layout (`SixtyThree` → `0x008000`, `Shirts` → `0x010000`, …, `Smiles` → `0x800000`).

### Mutually Exclusive Groups (`CodeService.cs:41-60`)

Constants combine flags that conflict:

- `DyeCodes = Clown | World | Elephants | Dolphins`
- `GearCodes = Emperor | World | Elephants | Dolphins`
- `RaceCodes = OopsHyur | OopsElezen | OopsLalafell | OopsMiqote | OopsRoegadyn | OopsAuRa | OopsHrothgar | OopsViera`
- `GenderCodes = AllMale | AllFemale | SixtyThree`
- `FullCodes = Face | Manderville | Smiles`
- `SizeCodes = Dwarf | Giant`

`private static GetMutuallyExclusive(CodeFlag flag)` (`CodeService.cs:120-148`) returns the conflict mask cleared when a flag is enabled. The pattern is `(FullCodes | <ownGroup>) & ~self` — `FullCodes` is always added so any full-replacement mode is dropped when a "lesser" mode is enabled. For the three gender flags, `GetMutuallyExclusive` returns `(FullCodes | GenderCodes) & ~self` (so ticking `AllMale` clears `AllFemale` and `SixtyThree` as well as the full codes). The `FullCodes` flags themselves (`Face`/`Manderville`/`Smiles`) clear nearly everything: `(FullCodes | RaceCodes | SizeCodes | GearCodes | DyeCodes | GenderCodes | Crown) & ~self`. `Shirts` is a one-flag group (returns `0` — coexists with anything).

### CodeService Public Surface (`Glamourer/Services/CodeService.cs`)

| Member | Purpose |
|--------|---------|
| `AllEnabled` (property → `CodeFlag`) | Returns the full `_enabled` mask |
| `Enabled(CodeFlag) → bool` | Single-flag query (`HasFlag`) |
| `AnyEnabled(CodeFlag mask) → bool` | Any-of-mask query |
| `Masked(CodeFlag mask) → CodeFlag` | Returns `_enabled & mask` |
| `GetRace() → Race` | If a `RaceCodes` flag is enabled, returns the race it maps to; otherwise `Unknown` |
| `Toggle(CodeFlag flag, bool enable)` | Enable: OR the flag in, then AND with `~GetMutuallyExclusive(flag)`. Disable: AND with `~flag`. Either way writes `_config.EnabledCheats = _enabled` and `Save()`s |
| `DisableAll()` | Sets `_enabled = 0`, `_config.EnabledCheats = 0`, saves |
| `static GetName(CodeFlag) → string` | UI label (e.g. `Clown` → "Random Dyes", `AllMale` → "All Male") |
| `static GetDescription(CodeFlag) → string` | Tooltip text for the checkbox |

`_enabled` is loaded directly from `_config.EnabledCheats` in the constructor (`CodeService.cs:90-94`) and saved straight back on every `Toggle`/`DisableAll`. There is **no** `CheckCode`, `GetSha`, `GetData`, `AddCode`, `GetCode`, `SaveState`, `Load`, or hint-metadata surface anymore — the SHA-256 passphrase system was removed.

### FunModule (`Glamourer/State/FunModule.cs`)

`FunModule` (~500 lines, `IDisposable`, `IRequiredService`) applies the actual transformations. Constructor dependencies are `IDalamudPluginInterface`, `CodeService`, `CustomizeService`, `ItemManager`, `Configuration`, **`StateManager`**, `ActorObjectManager`, `DesignConverter`, `DesignManager`, `NpcCustomizeSet`, and `FestivalNotification`. It subscribes to `DayChangeTracker.DayChanged += OnDayChange` and unsubscribes in `Dispose`. Public entry points:

- **`ApplyFunOnLoad(Actor actor, Span<CharacterArmor> armor, ref CustomizeArray customize)`** (`FunModule.cs:274`) — called when an actor's full state loads. Order of operations:
  1. `ValidFunTarget(actor)` guard (must be a PC `ObjectKind.Pc`, not transformed, `ModelCharaId == 0`).
  2. `ApplyFullCode(...)` — if a `FullCodes` flag is set, replace the entire NPC appearance + armor from one of the `PrioritizedList<NpcId>` pools (see below) and return early.
  3. `SetRace(ref customize)` — when a `RaceCodes` flag is set, derive `targetClan = (SubRace)((int)race * 2 - (int)customize.Clan % 2)` (preserving clan parity within the new race) and call `ChangeClan`.
  4. `SetGender(ref customize)` — see explicit branches below.
  5. `RandomizeCustomize(ref customize)` — when `Individual` is set, randomize every customize index except `Face` (and skipping unavailable indices).
  6. `SetSize(actor, ref customize)` — when `Dwarf`/`Giant` is set, set the player to one extreme and other actors to the opposite (`Height`, plus `BustSize` for female).
  7. Then gear: if `IsInFestival`, apply the festival set and return; else `Crown` mentor hat; else per-`GearCodes` gear (`Emperor` random items / `Elephants` / `Dolphins` / `World`); then per-`DyeCodes` dye (`Clown` random dyes).

  The order is **ApplyFullCode → SetRace → SetGender → RandomizeCustomize → SetSize**, then gear/dye.

- **`SetGender(ref CustomizeArray)`** (`FunModule.cs:433`) — explicit branches: `AllMale` → `ChangeGender(Male)`; else `AllFemale` → `ChangeGender(Female)`; else `SixtyThree` → flip (`Male ? Female : Male`). No-op if none set.

- **`ApplyFunToSlot(Actor, ref CharacterArmor, EquipSlot)`** (`FunModule.cs:112`) — per-equipment-piece hook. **Returns early when `IsInFestival` is true** (calling `KeepOldArmor` so the festival path in `ApplyFunOnLoad` owns gear). Otherwise: `FullCodes`/`Crown` keep-old or mentor-hat special cases, then per-`GearCodes` (`Emperor` → `SetRandomItem`, `Elephants`/`Dolphins`/`World` → keep-old here) and per-`DyeCodes` (`Clown` → `SetRandomDye`).

- **`ApplyFunToWeapon(Actor, ref CharacterWeapon, EquipSlot)`** (`FunModule.cs:332`) — applies `World` job-appropriate weapon via `_worldSets` for non-player actors.

- **`WhoAmI()` / `WhoIsThat()`** (`FunModule.cs:477-481`) — export the actor's *post-fun-mode* visible state as a Glamourer base64 design to the clipboard. Both delegate to a shared `private WhoIsThat(Actor)` (`FunModule.cs:483`) against `_objects.Player` / `_objects.Target` respectively.

- **`IsInFestival`** (private property, `FunModule.cs:107`) — `true` only when ALL three hold: `IDalamudPluginInterface.AllowSeasonalEvents`, `Configuration.FestivalMode is AskYes or NeverAskYes`, and `_festivalSet is not null`.

### PrioritizedList<T> (`FunModule.cs:157-181`)

Internal sealed class (`List<(T Item, int Priority)>` subclass) used for the Face/Manderville/Smiles full-NPC pools. The constructor drops zero-priority entries, sorts by descending priority, and rewrites each entry's stored value to a **running cumulative sum**. `GetRandom(Random rng)` then picks a random integer in `[0, _cumulative)` and walks the list returning the first entry whose cumulative sum exceeds it — so a Priority-10 entry is 10× more likely than a Priority-1 entry. Pools (all `private static readonly` fields on `FunModule`):

- `MandervilleMale` / `MandervilleFemale` (Hildibrand-cast NPCs, split by player gender)
- `Smile` (Smile-variant NPCs)
- `FaceMale` / `FaceFemale` (random face NPCs)

### Festival System (`Glamourer/State/FunEquipSet.cs`)

The festival pathway is driven by `DayChangeTracker.DayChanged → FunModule.OnDayChange(day, month, year)` (`FunModule.cs:46`), which maps the calendar date to a `FestivalType` (`Halloween` 31 Oct / 1 Nov, `Christmas` 24-26 Dec, `AprilFirst` 1 Apr, else `None`), resolves `_festivalSet = FunEquipSet.GetSet(type)`, and updates the opt-in `FestivalNotification` if the user hasn't been asked recently. Detection therefore only flips at a midnight tick, not per-frame. `Configuration.LastFestivalPopup` (`DateOnly`) gates re-prompting.

`FunEquipSet` is an `internal class` holding a `Group[]`. Each **`Group`** is a `readonly record struct` of five `CharacterArmor` slots (`Head, Body, Hands, Legs, Feet`) plus an optional `StainId[]? Stains` (null → use all stains). There is **no `KeepOldArmor` flag on `Group`** — `KeepOldArmor` is a *method on `FunModule`* (`FunModule.cs:347`) that copies the actor's existing armor. `Group` has helper constructors (`FullSet`, `FullSetWithoutHat`) and a `(set, variant)` shorthand ctor. `FunEquipSet.Apply(StainId[] allStains, Random rng, Span<CharacterArmor> armor)` picks a random group, picks a random stain from `group.Stains ?? allStains`, and writes each non-empty slot (`Set != 0`) into `armor`. The three sets `Christmas`, `Halloween`, `AprilFirst` are `public static readonly` instances.

When `IsInFestival` is true, `ApplyFunOnLoad` applies the festival set (after race/gender/size codes have already run) and `ApplyFunToSlot` short-circuits to keep-old; race/gender/size codes still apply, only gear/dye defer to the festival outfit.

### CodeDrawer (`Glamourer/Gui/Tabs/SettingsTab/CodeDrawer.cs`, ~72 lines)

The Settings-tab UI. The class is a primary-constructor `IUiService` taking `CodeService`, `FunModule`, **`StateManager`**, and **`ActorObjectManager`** (the old `Configuration` dependency is gone). `Draw()` renders an `Im.Tree.Header("Fun Modes")` and, when open:

1. **`DrawFeatureToggles()`** — iterates `CodeService.CodeFlag.Values`, skipping the three debug flags (`Face`/`Manderville`/`Smiles`). For each, draws an `Im.Checkbox(CodeService.GetName(flag), ref enabled)`; on change calls `codeService.Toggle(flag, enabled)` then `ForceRedrawAll()`. Hovering shows `GetDescription(flag)` as a tooltip.
2. **`DrawCopyButtons()`** — "Who am I?!?" / "Who is that!?!" 250-wide buttons calling `funModule.WhoAmI()` / `funModule.WhoIsThat()`, each with an explanatory tooltip.
3. **"Disable All" button** — calls `codeService.DisableAll()` then `ForceRedrawAll()`.

`ForceRedrawAll()` walks `actorObjectManager.Objects` and calls `stateManager.ReapplyState(actor, true, StateSource.Manual)` for each valid actor so the change is visible immediately. There is no passphrase input, no per-code list, and no hint section.

### Configuration

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `EnabledCheats` | `CodeService.CodeFlag` | `0` | **The live serialized Fun Modes bitmask** (`Configuration.GT.cs:49`). `CodeService._enabled` mirrors this field |
| `FestivalMode` | `FestivalSetting` | `Undefined` | Festival opt-in state (`Undefined` / `AskYes` / `AskNo` / `NeverAskYes` / `NeverAskNo`) |
| `LastFestivalPopup` | `DateOnly` | `MinValue` | Last date the festival opt-in popup was shown |

`Configuration.Codes` (the upstream `List<(string Code, bool Enabled)>` passphrase list) still exists on the upstream `Configuration` partial but is now **orphaned/dead** for this feature — nothing in the GT Fun Modes path reads or writes it.

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
| `Glamourer/GlamorousTerror/EquipmentLanguage/ItemNameService.cs` | Language-specific Lumina sheet loading, name caching. **Note:** lives in the GT folder by file layout but is declared in the upstream-shaped `Glamourer.Services` namespace (not a GT-namespaced service), since it's injected throughout upstream combo code |
| `Glamourer/GlamorousTerror/Config/SettingsTab.GT.cs` | `DrawGlamorousTerrorSettings()` / `DrawEquipmentLanguageSettings()` combo UI |
| `Glamourer/GlamorousTerror/Config/Configuration.GT.cs` | `EquipmentNameLanguage` property |

**`EquipmentNameLanguage`** enum: `GameDefault`, `English`, `Japanese`, `German`, `French`

**ItemNameService** — `IService` that maintains two parallel sheet structures:

- An `_allLanguageSheets[4]` array of `ExcelSheet<Item>` (one per language; loaded once in the constructor) used by cross-language search.
- A single `_itemSheet` reflecting the **currently selected display language**, refreshed via `RefreshSheet()` (line 64). `RefreshSheet` resolves the configured `EquipmentNameLanguage` to a Lumina `ClientLanguage`, reloads `_itemSheet`, and clears `_nameCache`.
- `GetItemName(EquipItem)` — returns the configured-language display name, caching into `_nameCache`.
- `GetItemName(uint itemId, string fallback)` — id-keyed overload for callers that don't have an `EquipItem` (e.g. icon picker tooltips). Returns `fallback` when the row is missing.
- `GetCurrentLanguageDisplay()` — UI-facing helper that resolves the configured enum to a display string ("English", "Japanese", etc., or the game-default name).
- `_nameCache: Dictionary<uint, string>` — **single dictionary** for the currently-selected display language (cleared by `RefreshSheet`/`ClearCache`). NOT per-language; the per-language structure lives in `_allLanguageNamesCache` and is used by §6 cross-language search.
- `CheckLanguageChange()` (line 71) — detects a config language change and, if changed, calls **only** `RefreshSheet()` (which reloads `_itemSheet` and clears `_nameCache`, but does NOT clear `_allLanguageNamesCache`). It does not call `ClearCache()`.
- `ClearCache()` — called only from the settings UI (language change / cross-language toggle) to drop stale entries; clears both `_nameCache` and `_allLanguageNamesCache` and additionally re-runs `RefreshSheet()`.

**SettingsTab** exposes the combo in **two places** with different ImGui IDs so the same setting is reachable from either entry point:

- The GT section in `DrawGlamorousTerrorSettings()` uses `##gtEquipLangCombo`.
- The standalone equipment-language panel `DrawEquipmentLanguageSettings()` uses `##equipLangCombo`.

Both call `ItemNameService.ClearCache()` on change. The tooltip in `SettingsTab.GT.cs:61` notes "Requires a UI reload to take full effect" — existing rendered items keep their captured names until the next combo re-population, so a complete refresh sometimes needs `/xlreload`.

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
| `Glamourer/GlamorousTerror/EquipmentLanguage/ItemNameService.cs` | `GetAllLanguageNames(uint)` — returns `string[4]?` (nullable; `null` when no language has a name for the id) of all language names, cached in `_allLanguageNamesCache`. `MatchesAnyLanguage(EquipItem, string)` exists as a standalone helper but has **no in-tree callers** today — the active integration path goes through `GetAllLanguageNames` + the inherited partwise overload, not `MatchesAnyLanguage` |
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
3. Calls `itemNameService.GetAllLanguageNames(itemId)` → `string[4]?` cached in `_allLanguageNamesCache`. **Array order is `[English, Japanese, German, French]` matching the order of the `AllLanguages` array** — reordering would silently rebind every consumer to the wrong language. The result is nullable: `null` means no language returned a name for this id (e.g. a row that exists in the type table but has empty `Name` cells everywhere).
4. For each non-empty name, calls the inherited `WouldBeVisible(string)` — this is the `PartwiseFilterBase<T>.WouldBeVisible(string)` overload from Luna; it reuses the partwise filter logic (`Parts.All(p => text.Contains(p, Comparison))`)
5. Returns `true` if **any** language name passes all filter tokens

**Key design: per-language partwise matching** — All filter tokens must match within the **same** language name. The filter does NOT mix matches across languages. This is achieved by calling `WouldBeVisible(name)` (the `PartwiseFilterBase<T>.WouldBeVisible(string)` overload) per language, which checks that every token in `Parts` appears in that single string.

**Short-circuit OR** at `BaseItemCombo.cs:129` — the visibility check reads `return base.WouldBeVisible(...) || WouldBeVisible(item.Model.Utf16) || GTFallbackNameMatch(in item);`. The cross-language fallback is the most expensive of the three (per-language hash lookup + four partwise scans), and the short-circuit ensures it only runs when both the display-language name and the model string have already missed.

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
- Caches and returns `string[4]?` in `[English, Japanese, German, French]` order, or `null` if no names found in any sheet
- `ClearCache()` clears both `_nameCache` and `_allLanguageNamesCache`, and additionally calls `RefreshSheet()` (line 165) — which reloads `_itemSheet` for the current display language and clears `_nameCache` a second time. The double-clear of `_nameCache` is harmless but worth knowing about if you're debugging cache-warming behavior

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
      → 3. GTFallbackNameMatch(in item)
           → config.CrossLanguageEquipmentSearch? → true
           → itemId valid? (not 0, not special) → true
           → itemNameService.GetAllLanguageNames(itemId)
             → _allLanguageNamesCache miss
             → Load from 4 ExcelSheet<Item> sheets → cache string[4] in [EN, JP, DE, FR] order
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
| `Glamourer/Services/FilenameService.cs` | `UnlockFileItemsForCharacter(ulong contentId)` → `{ConfigDir}/unlocks_items_{contentId:X16}.dat` (binary v3 format with magic header). The legacy `UnlockFileItems` (`.json`) path is still referenced for `GetBackupFiles()` but no longer used for live persistence when a character is logged in |
| `Glamourer/GlamorousTerror/ItemOwnership/ItemUnlockManager.cs` | Per-character lifecycle: login/logout handlers, `_currentContentId` field, source tracking, pruning |

**Character lifecycle** in `ItemUnlockManager`:

- **Constructor**: Subscribes to `_clientState.Login += OnLogin` and `_clientState.Logout += OnLogout`. If `_playerState.ContentId != 0` (plugin reload while logged in), immediately calls `OnLogin()`.
- **`OnLogin()`**: Captures `_playerState.ContentId`, clears all dictionaries and scan state, calls `Load()` then `Scan()`.
- **`OnLogout(int, int)`**: Calls `Save()`, clears all dictionaries and scan state, resets `_currentContentId = 0`.
- **`OnFramework` guard**: Early-returns if `_currentContentId == 0` (no character logged in).
- **`ToFilePath()`**: Returns `fileNames.UnlockFileItemsForCharacter(_currentContentId)` when a character is logged in, falls back to `fileNames.UnlockFileItems` otherwise.
- **`ResetScanState()`**: Resets all scan-related fields: `_currentInventory`, `_currentInventoryIndex`, armoire/achievement/glamour/plate state booleans, `_seenThisCycle`, `_fullyScannedSources`.

### Implementation — CustomizeUnlockManager

`Glamourer/GlamorousTerror/ItemOwnership/CustomizeUnlockManager.cs` (`IDisposable`, `ISavable`, `IRequiredService`) is the parallel manager for **unlockable customizations** (purchasable hairstyles and face paints), used to gate those entries in the customization drawer the same way `ItemUnlockManager` gates equipment. Unlike item unlocks it is **not per-character** — `ToFilePath` returns the single global `fileNames.UnlockFileCustomize`, and persistence goes through `UnlockDictionaryHelpers.Save/Load` (it has no source byte, so the no-source `Save` overload writes `0x00`).

- **`CreateUnlockableCustomizations(customizations, gameData)`** (line 180) — built once in the constructor. Walks every `(clan, gender)` set; for each hairstyle and face paint it looks up the matching `CharaMakeCustomize` row (English sheet) by `FeatureID` and keeps only rows where `IsPurchasable is true`. The display name comes from the row's `HintItem` (stripping the `"Modern Aesthetics - "` / `"Modern Cosmetics - "` prefixes), with a hardcoded "Eternal Bond" name for `FeatureID == 61`. Result is `Unlockable: IReadOnlyDictionary<CustomizeData, (uint Data, StringU8 Name)>`.
- **`Scan()`** (line 98) — runs in the constructor and on `_clientState.Login`. Guards on `_objects.Player.Valid` and `UIState.Instance()`, then for every `Unlockable` entry whose `UnlockLink` reports `IsUnlockLinkUnlocked(id)` and isn't already recorded, adds it to `_unlocked` (timestamped), fires `ObjectUnlocked`, and `Save()`s if anything new was found.
- **`SetUnlockLinkValueDetour(nint uiState, uint data, byte value)`** (line 138) — a `[Signature(Sigs.SetUnlockLinkValue)]` hook that detours the game's unlock-link writer. After calling the original, on a non-zero `value` it matches `data` against the `Unlockable` map's `UnlockLink` ids and, on first sight, records the unlock + fires the event + saves. This catches unlocks that happen mid-session (e.g. buying a hairstyle) without waiting for the next `Scan`.
- **`IsUnlocked(CustomizeData, out time)`** (line 53) — returns `true` for any non-hairstyle/non-facepaint index and for non-purchasable rows; otherwise checks `_unlocked`, falling back to a lazy `IsUnlockedGame` (`IsUnlockLinkUnlocked`) check that records-on-hit (a second write site, like `ItemUnlockManager.IsUnlocked`).

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

**`GetInventorySource(InventoryType)`** — Method body only explicitly matches Saddlebags and Retainers; everything else (`Inventory1–4`, `EquippedItems`, `Mail`, `Armory*`) falls through to the default `_ => ItemSource.Inventory`. The behavior matches the enumeration below, but the implementation is a default-catch-all switch, not an explicit per-type map:
- `Inventory1–4`, `EquippedItems`, `Mail`, `Armory*` → `Inventory` (default case)
- `SaddleBag1/2`, `PremiumSaddleBag1/2` → `Saddlebags`
- `RetainerPage1–7`, `RetainerEquippedItems`, `RetainerMarket` → `Retainers`

**Binary format v3** (`UnlockDictionaryHelpers`):
- Header: `[Magic:0x00C0FFEE (uint32)] [Version:3 (int32)] [Count (int32)]`
- Per entry: `[ItemId (uint32)] [Timestamp (int64)] [Source (byte)]`
- Backward compatible: v1/v2 files load with `ItemSource.All` default. v3 reads the source byte.
- The non-source `Save()` overload (used by `CustomizeUnlockManager`) writes `0x00` as the source byte.
- **Cross-endian load**: the magic is accepted in either byte order — `0x00C0FFEE` (native) or the byte-swapped `0xEEFFC000`. When the swapped magic is seen, `id` and `timestamp` are run through `RevertEndianness` per entry, so a file written on the opposite endianness still loads.

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

`WouldBeVisible(in CacheItem, int)` order (fastest reject first — the owned gate is an O(1) dictionary lookup, while the partwise text checks scan every search token against every character of every name):

1. **Owned pre-gate** (new, via `GTPreFilterItem`): If `config.OwnedOnlyComboFilter` is true and `itemUnlockManager.IsOwnedFromSources(item.Item.ItemId, config.OwnedComboFilterSources)` returns false → **reject immediately** (return false). Note: this also short-circuits cross-language search — an item the user doesn't own never reaches step 4, even if its non-English name matches the search.
2. `base.WouldBeVisible()` — display-language name match (inherited partwise filter from Luna's `PartwiseFilterBase<T>`)
3. `WouldBeVisible(item.Model.Utf16)` — model string match
4. `GTFallbackNameMatch(in item)` — cross-language match (only invoked when steps 2 and 3 both miss, due to the short-circuit OR at `BaseItemCombo.cs:129`)

**`IsOwnedFromSources(CustomItemId, ItemSource filter)`** — Public query method on `ItemUnlockManager`:
- **`itemId.IsItem` guard first** (load-bearing — `ItemUnlockManager.cs:473`): if the ID does not pass `IsItem`, the method falls back to the default-source check. Bonus items and custom items have high flag bits set in their ID; without the guard they would coincidentally land in the pseudo-item range below and always report owned. The CLAUDE.md invariant on this is critical to preserve.
- Pseudo items (ID 0 or ≥ `uint.MaxValue - 512`) always return `true`
- Otherwise checks `(_sources[id] & filter) != 0` — i.e. an item is "owned" when it has been seen in **at least one** of the user-selected sources (OR-match), not when it has been seen in all of them. This is the user-intent semantic: ticking only "Inventory" means "items I have in my inventory now"; ticking "Inventory + Glamour Dresser" means "items I have in either".

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
- Called from four locations: `ActorPanel.cs:208`, `DesignPanel.cs:88`, the icon picker's inline settings panel (`EquipmentDrawer.IconMode.cs:199`, see [Icon Equipment Drawer](#8-icon-equipment-drawer)), and the GT section of `SettingsTab` (`SettingsTab.GT.cs:75`). The SettingsTab call site is the "legacy" one left from before the filter became icon-picker-inline
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

The original `OnFramework` had a bug where `changes = false;` was written **between** the glamour-dresser scanning block and the inventory scanning block, discarding any `changes = true` set by the dresser/plates scanning above. This silently prevented `Save()` from being called when new items were detected in the glamour dresser (the inventory block alone wouldn't re-flip `changes` for items it had already seen). Fixed by removing the erroneous reset — `changes` is now declared once at the top of `OnFramework` and accumulates across both dresser and inventory scanning sections.

### Configuration

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `OwnedOnlyComboFilter` | `bool` | `false` | Master toggle for owned-only filtering |
| `OwnedComboFilterSources` | `ItemUnlockManager.ItemSource` | `All` (`0x3F`) | Bitmask of sources that count as "owned". OR-matched: an item passes when `(item._sources & OwnedComboFilterSources) != 0`, not when it intersects every set bit |

### Known Pitfalls (for future upstream merges)

1. **Per-character file path**: `ToFilePath()` now returns per-character paths. The `ISavable` system calls this on save — ensure upstream changes to `SaveService` or `FilenameService` don't break the dynamic path resolution.

2. **Login/Logout lifecycle**: `ItemUnlockManager` no longer loads in its constructor. If other services depend on `ItemUnlockManager` being populated at construction time, they may see empty data until login fires. The constructor handles the "already logged in" case for plugin reloads.

3. **DirtyCacheOnClose**: The combo cache is now dirtied every time the popup closes. This is necessary for ownership changes to be reflected but has a minor performance cost (one filter pass per popup open). Upstream changes to `FilterComboBase.ConfigData` field names should be monitored.

4. **PruneSource modifies _sources during iteration**: Both `PruneInventorySources()` and `PruneSource()` iterate `_sources` and collect removals into a separate `List<uint>`, then remove after iteration. This avoids collection-modified-during-enumeration exceptions.

5. **Retainer scan safety**: Retainer containers are only loaded when at a retainer bell. The `_fullyScannedSources` mechanism prevents false pruning — retainer items won't be pruned unless all retainer inventory types were actually loaded and fully iterated that cycle.

6. **`ItemUnlockManager` is empty until login fires** (CLAUDE.md invariant): Construction does NOT populate `_unlocked`/`_sources` — only the login handler does. Any service that queries owned-state during construction will see zero unlocks. `Configuration` is loaded before login, so the bitmask is set, but every `IsOwnedFromSources` call returns `false` for non-pseudo items until the first `OnLogin()` completes its `Load() → Scan()` sequence.

7. **`UpdateModels(int version)` backfill** (`ItemUnlockManager.cs:582-605`): v1 files predate the source byte, so on load they backfill every entry's `_sources` value to `ItemSource.All`. This is why an upgraded user sees every old unlock as "owned from all sources" until the next scan cycle prunes the false positives. Don't change the backfill default to anything narrower — it would silently drop items below the user's source filter without explanation.

8. **`IsUnlocked` is also a write site** (`ItemUnlockManager.cs:416-451`): The lazy-add path inside `IsUnlocked` writes to both `_unlocked` and `_sources` outside the scan cycle. So source tracking has two entry points: the scan-cycle `AddItem` and the lazy `IsUnlocked`. Both must keep `_sources` in sync.

---

## 8. Icon Equipment Drawer

Replaces the name-based equipment combo list with a compact **icon grid**. Clicking an icon opens a filterable icon picker popup; right-clicking clears/reverts the slot (and inside the popup, toggles favorite state).

### User-Facing Functionality

- **Master toggle**: `UseIconEquipmentDrawer` in Settings → Glamorous Terror section ("Icon Equipment Drawer")
- Renders armor slots, weapons, and bonus items as square icon buttons in the Actor panel, Design panel, NPC panel, and Immersive Dresser equipment panels (the Equipment Bar window does NOT use icon mode — it sticks with compact combos)
- **Click an icon** → opens icon picker popup anchored next to the button, edge-clamped to the viewport
- **Right-click an icon (outside popup)** → revert/clear that slot (standard upstream behavior). For weapons this is implemented separately inside `DrawWeaponSlotIcon` (mainhand/offhand take their own paths through `ResetOrClear`, since the weapon icon does not flow through `DrawEquipIcon`). When a mainhand right-click changes weapon type, the offhand is auto-replaced with `_items.GetDefaultOffhand(...)` to keep the pair compatible. The compatibility test is `!changedItem.Value.Type.ValidOffhand().IsCompatible(mainhand.CurrentItem.Type.ValidOffhand())` (the left-click selection path uses a stricter direct `!=` check instead — preserve both branches when overlaying upstream changes).
- **Right-click an item (inside popup)** → toggles favorite for that item (see [Favorites](#9-favorites)); favorited items are highlighted with a yellow frame (ABGR `0xFF00CFFF`)
- **Filter bar** at the top of the popup:
  - Text search (case-insensitive substring, auto-focused on open)
  - **Star** button — favorites-only filter
  - **K** button — toggles `KeepIconPickerOpen` (if set, the popup stays open after each selection)
  - **Thumbtack (pin)** button — toggles `IconPickerPinned`. When pinned the picker is hoisted out of the transient popup into a regular ImGui window that survives click-off; click the source equipment icon again to close, or another icon to switch slots
  - **Cog** button — expands an inline settings panel with `DrawOwnedOnlyFilter(config)` + "Group by Model" + "Remember Scroll Per Slot"
  - Job filter combo (by role: Tanks, Healers, Melee DPS, Physical Ranged, Magical Ranged, Crafters, Gatherers; plus "Unrestricted" shortcut for gear equippable by all jobs)
  - Dye channel filter combo (Any, 0, 1, 2)
  - Sort combo (A → Z, Z → A, ID ↑, ID ↓)
- **Grouping by model** (`GroupIconPickerByModel`, default `true`) — deduplicates items that share the same `(Type, PrimaryId, SecondaryId, Variant)`, keeping only the first after sorting
- **Owned-only gate** runs before the filter for each item (fast reject)
- **Preview-on-hover** — hovering an icon while CTRL is held previews the item (see [Preview-on-Hover](#2-preview-on-hover))
- **Scroll behavior** — by default the popup's scroll position is forced to 0 for 2 frames after opening (prevents inheriting the previous popup's scroll). When `RememberIconPickerScroll` is enabled, the picker instead restores the per-slot scroll position recorded the last time the user closed/switched the picker for that slot. Scroll is tracked by **two separate dictionaries** (`EquipmentDrawer.IconMode.cs:46-47`): `_iconPickerSlotScroll` keyed by `EquipSlot` and `_iconPickerBonusSlotScroll` keyed by `BonusItemFlag`. Weapons reuse `_iconPickerSlotScroll` keyed by their `EquipSlot` (MainHand/OffHand) — there is no `isWeapon`/`isBonus` component to the key
- **Max rows** — popup height is capped at `IconPickerMaxRows` rows (default `10`), configurable via slider in Settings

### Implementation

| File | Role |
|------|------|
| `Glamourer/GlamorousTerror/IconEquipment/EquipmentDrawer.IconMode.cs` | ~1085-line partial class extension of `EquipmentDrawer` — all icon mode state, filter/sort logic, popup/window layout, pin handling, item/bonus/weapon icon draws |
| `Glamourer/Gui/Equipment/EquipmentDrawer.cs` | Upstream drawer declares `GTTryDrawEquipIcon`, `GTTryDrawBonusItemIcon`, `GTTryDrawWeaponsIcon`, `GTResetIconState` partial-method hooks that early-short-circuit when the icon drawer is active |
| `Glamourer/GlamorousTerror/Config/SettingsTab.GT.cs` | Master toggle + sub-settings (Group by Model, Keep Picker Open, Max Rows slider) |

**Icon picker state (session-scoped, not persisted):**

| Field | Purpose |
|-------|---------|
| `_iconPickerSlot` / `_iconPickerBonusSlot` | Which slot the open popup belongs to |
| `_iconPickerIsWeapon` / `_iconPickerIsBonus` | Popup variant |
| `_iconPickerPopupOpen` | Set `true` each frame the popup renders (for `ApplyHoverPreview`); resets via `GTResetIconState()` at start of frame |
| `_iconPickerActive`, `_iconPickerRepositionRequested` | Pinned-mode state — when `IconPickerPinned`, the picker is rendered as a regular window instead of a popup; carry-over and reposition flags handle the popup↔window transition |
| `_iconPickerHoveredItem` | Item under the mouse inside the popup |
| `_iconPickerSelectionMade` | Click-to-commit flag |
| `_iconPickerClickY` | Vertical anchor point so the popup opens at the clicked icon's Y |
| `_iconPickerScrollResetFrames` | Countdown that zeros `Im.Scroll.Y` for 2 frames after popup appears (when `RememberIconPickerScroll` is off) |
| `_iconPickerSlotScroll` (`Dictionary<EquipSlot, float>`) / `_iconPickerBonusSlotScroll` (`Dictionary<BonusItemFlag, float>`) | When `RememberIconPickerScroll` is on, the picker restores the previous scroll keyed by `EquipSlot` (armor + weapon slots) or `BonusItemFlag` (bonus slots) — no `isWeapon`/`isBonus` dimension in the key |
| `_iconPickerNameFilter`, `_iconPickerFavoritesOnly`, `_iconPickerJobFilter`, `_iconPickerNeutralJobFilter`, `_iconPickerDyeChannelFilter`, `_iconPickerSortMode`, `_iconPickerShowSettings` | Filter & sort state |

**Popup positioning** (`PositionIconPickerPopup`) opens the popup to the left or right of the source window's center (whichever side has more space), clamps the anchor inside the viewport, and sizes the popup to fit `IconPickerMaxRows` rows and 8 columns (`IconPickerColumns = 8`).

**Two parallel render pipelines — popup vs. pinned window.** Each draw entry point (`DrawEquipIconPickerPopup`, `DrawBonusIconPickerPopup`, `DrawWeaponIconPickerPopup`) branches on `_config.IconPickerPinned`:

- **Popup mode** (default) — opened via `Im.Popup.Open(IconPickerPopup)` using the `##` ID prefix (transient popup state).
- **Pinned mode** — rendered as a regular `Im.Window.Begin` window with the `###GTIconPickerWindow` ID. The `###` prefix is intentional: the window's *title* is updated to reflect the active slot, but ImGui keeps the window state (position, size, scroll) keyed by the trailing ID. The popup and window use different ID conventions because their state systems are different — don't unify them.

A future change adding a new picker entry MUST replicate both branches; touching only one will silently break either popup users or pinned-mode users. `OpenOrToggleIconPicker(slot, isWeapon, isBonus)` is the sole sanctioned entry point — it handles the popup↔window transition and the pinned-mode "click the same icon again to close" behavior. Bypassing it (e.g., raw `Im.Popup.Open(IconPickerPopup)`) skips the same-target close path.

**Item pipeline** (per popup):

1. Iterate items of the slot's `FullEquipType` from `ItemData.ByType`
2. **Ownership filter**: `OwnedOnlyComboFilter` + `IsOwnedFromSources` (before any sort/group work)
3. `FilterIconPickerItem` — favorite, text, job, and dye-channel checks
4. **Model dedup** (if `GroupIconPickerByModel`): `HashSet<(FullEquipType, PrimaryId, SecondaryId, Variant)>`
5. `SortIconPickerItems` — alphabetical / ID sort
6. `DrawIconPickerItem` / `DrawBonusIconPickerItem` — renders the icon button with selected-red frame for the current equipped item or yellow frame for favorites

**Job filter** — Uses `JobService.AllJobGroups` to map `item.JobRestrictions.Id` → `JobGroup.Flags`, and checks against `_iconPickerJobFilter` (`JobFlag` bitmask) or the "Unrestricted" mode (only items equippable by every available job).

**Weapon picker special case** — When `comboType is FullEquipType.Unknown` (the "all weapons" mode), the popup iterates every `FullEquipType` whose `ToSlot()` is `MainHand`, with model-dedup applied across the flattened list.

**Layout-customization parameters (used by Immersive Dresser)** — `DrawEquipIcon`, `DrawBonusItemIcon`, `DrawSingleWeaponIcon`, and the private `DrawWeaponSlotIcon` accept two optional parameters: `bool stainsBeside = false` and `bool simplified = false`. `DrawIconStainIndicators` likewise accepts `bool vertical = false`. Defaults preserve the original behavior for the Actor / Design panel callers; the Immersive Dresser passes both as `true` to (a) render the stain indicators in a column to the right of the icon (`stainsBeside`) and (b) stack them vertically while suppressing the advanced-dye button column (`simplified` / `vertical`). When overlaying upstream changes to these signatures, retain the optional parameters and the `if (!stainsBeside) DrawIconStainIndicators(data); else { ...beside group... }` branches.

### Configuration

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `UseIconEquipmentDrawer` | `bool` | `false` | Master toggle |
| `GroupIconPickerByModel` | `bool` | `true` | Dedup items sharing the same visual model |
| `KeepIconPickerOpen` | `bool` | `false` | Popup stays open after each selection |
| `IconPickerPinned` | `bool` | `false` | Pin the picker into a regular window so click-off does not close it |
| `RememberIconPickerScroll` | `bool` | `false` | Remember the picker's scroll position separately for each slot |
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
| `Glamourer/Gui/Equipment/BaseItemCombo.cs` (upstream, with GT edits) | Owns the `protected readonly FavoriteManager Favorites;` field (line ~14) and calls `UiHelpers.DrawFavoriteStar(Favorites, item.Item)` at line ~163. The favorite-star rendering hooks into the upstream combo, not `BaseItemCombo.GT.cs` |
| `Glamourer/GlamorousTerror/IconEquipment/EquipmentDrawer.IconMode.cs` | Consumes `Favorites.Contains(item)` to apply the yellow icon frame, and routes right-click-inside-popup to `TryAdd`/`Remove` |

**`FavoriteManager`** — Dependencies: `SaveService`. Stores:

| Set | Type | Key |
|-----|------|-----|
| `_favorites` | `HashSet<ItemId>` | Equipment item IDs |
| `_favoriteColors` | `HashSet<StainId>` | Stain IDs |
| `_favoriteBonusItems` | `HashSet<BonusItemId>` | Bonus item IDs |
| `_favoriteHairStyles` | `HashSet<FavoriteHairStyle>` | Packed `(Gender, Race, Type, Id)` — see below |

**`FavoriteHairStyle`** — A `readonly record struct` with named fields `(Gender Gender, SubRace Race, CustomizeIndex Type, CustomizeValue Id)` that packs four bytes into one `uint`:

```
bits 24–31: Gender
bits 16–23: Race (SubRace)
bits  8–15: Type (CustomizeIndex)
bits  0–7:  Id (CustomizeValue)
```

`ToValue()` packs, the constructor overload `FavoriteHairStyle(uint)` unpacks. This uint is what gets persisted in JSON.

**Persistence** — JSON with a version header. V0 was a bare `uint[]` of item IDs only; V1 is `{ Version, FavoriteItems, FavoriteColors, FavoriteHairStyles, FavoriteBonusItems }`. V0 files auto-migrate to V1 on load — the trigger is the heuristic `text.StartsWith('[')` in `Load()` (a fragile but workable detection because V1 starts with `{`).

**Storage path** — `FilenameService.FavoriteFile` (global, not per-character) — resolves to `favorites.json` under `pi.ConfigDirectory`. Uses `SaveService.DelaySave(this)` for debounced writes.

**Public API:**

- `TryAdd(EquipItem)`, `TryAdd(ItemId)`, `TryAdd(BonusItemId)`, `TryAdd(StainId)`, `TryAdd(Gender, SubRace, CustomizeIndex, CustomizeValue)` — returns `false` if already present or id is 0
- `Remove(...)` — mostly symmetric, with **one known bug**: `Remove(Gender, SubRace, CustomizeIndex, CustomizeValue)` (line ~244 of `FavoriteManager.cs`) fires `FavoriteChanged?.Invoke(FavoriteType.Customization, id.ToValue(), added: true)` — the `added` arg should be `false`. Subscribers that act on the `added` flag will mis-classify a customization remove as an add. Left as-is for now because no in-tree subscriber depends on the flag (the favorite star rendering reads the set directly, not the event).
- `Contains(EquipItem)` (line ~249) — O(1) hash-set lookup with a special-case carve-out: if `item.Id.IsBonusItem`, it checks `_favoriteBonusItems.Contains(item.Id.BonusItem)` instead. Callers that hand-roll `_favorites.Contains(itemId)` will miss bonus favorites — always go through `Contains(EquipItem)` when you have an `EquipItem` in hand.
- `FavoriteChanged` event fires with `(FavoriteType, uint id, bool added)` — intended for combo cache invalidation, but currently has no in-tree subscribers (favorite-star rendering reads `Favorites` directly each frame).
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

- `Dictionary<nint, RotationOverride> _overrides` — keyed by actor address. Struct field order (load-bearing — matches `RotationService.cs:178`): `record struct RotationOverride(Quaternion Rotation, nint LastModelAddress, Quaternion OriginalRotation, Vector3 OffsetDegrees)`
- `IFramework` subscription — activated lazily when the first override is added (`_overrides.Count` transitions `0 → 1`), deactivated when the last is cleared (`Count → 0`). Zero per-frame overhead when no overrides are active
- `ClearAll()` is called from `Dispose()` so plugin unload removes every override cleanly

**Per-frame loop** (`OnFrameworkUpdate`):

1. For each override, read the current actor; if invalid, queue for removal
2. Write `ov.Rotation` onto `drawObj->Object.Rotation`, set `IsTransformChanged = true`
3. Also write onto `drawObj->Object.ChildObject->Rotation` AND set `IsTransformChanged = true` on the child draw object so weapons follow the body rotation (both writes are required — the child has its own dirty flag)
4. Remove stale overrides after iteration

**`SetRotation(actor, offsetDegrees)`** — Computes the final quaternion as `OriginalRotation × EulerOffset`. The "original" is captured once on first `SetRotation` and reused for subsequent updates (so setting a new offset composes with the game's current rotation at capture time, not with the previous override).

**`ClearRotation(actor)`** — Restores the yaw quaternion derived from the game object's `Rotation` field (`y = sin(halfYaw), w = cos(halfYaw)`) so the character snaps back to what the game considers canonical.

**`RotationDrawer`** — Tracks the last actor it drew for; on actor change, clears the previous override and re-initializes its local `_euler` buffer from `RotationService.TryGetEuler` (so reopening the dresser on the same actor restores the user's Euler values).

**Euler axis mapping** (non-obvious — `RotationDrawer.cs:38, 45, 52`): the UI labels read "Yaw / Pitch / Roll" but they are written into the offset `Vector3` as `Yaw → _euler.Y`, `Pitch → _euler.X`, `Roll → _euler.Z`. The vector is then passed to `Quaternion.CreateFromEuler(new Vector3(X, Y, Z))`. Reordering the axes in the UI without also reordering the writes will silently rotate around the wrong axis.

**Stateful per instance**: `RotationDrawer` holds a single `(_lastActor, _euler, _initialized)` triple. Multiple concurrent drawer surfaces would interfere — today only the dresser hosts it, but a future surface that also wants per-frame Yaw/Pitch/Roll drags needs its own drawer instance.

**Wiring** — `ImmersiveDresserManager.OptionsPanel.Draw()` wraps the drawer in a `Character Rotation` tree header and calls `manager._rotationDrawer.Draw(playerData.Objects[0])` — passing the resolved actor from `ResolveTarget()` (defaults to the player; can be any actor when the dresser target picker is used). `ImmersiveDresserManager.Close()` calls `_rotationDrawer.Reset()` which in turn clears the override for the last actor. `SetTarget()` also calls `Reset()` before the swap so the old target's override is not left orphaned on the new one.

---

## 11. Immersive Dresser

Right-clicking the **local player character** in-game adds an **"Immersive Dresser"** entry to the context menu. Clicking it — or running `/glamour dresser` / `/gt dresser` — opens a multi-panel glamour editor anchored to the player character, optionally hiding the game HUD. The dresser can be **retargeted to any actor** post-open via the target picker in the Options panel; the context-menu entry and command both open against the player as the initial target.

### User-Facing Functionality

- Context menu entry appears only when right-clicking the player's own character; also openable via `/glamour dresser` / `/gt dresser`. Once open, the target can be swapped to any actor via the Options-panel target picker (combo + "switch to current in-game target" icon + "Return to Self" button)
- Three floating panels, each with a title bar (collapsible) and movable independently:
  - **Equipment / Customization** (left of center) — switches between armor slots + bonus items and the full customization drawer depending on mode
  - **Accessories / Parameters** (right of center) — switches between off-hand + accessory slots and the customize-parameter drawer
  - **Options** (below center) — target picker, mode toggle, design actions (clipboard/save/undo), game-UI/panel-lock/free-cam icon buttons, Dye All Slots, meta toggles, camera settings, character rotation
- Two **modes** toggled from the Options panel: `Equipment` (default) and `Appearance`
- **Single-window layout** (`SingleWindowDresser`) — when enabled, the Left panel renders both equipment and accessories (iterating `EquipSlotExtensions.EqdpSlots` plus the offhand) and the Right panel hides itself in Equipment mode. Useful for collapsing the dresser to one floating window
- **Simplified layout** (`SimplifiedDresserLayout`) — stacks dye channels vertically beside each icon and hides the advanced-dye buttons. Implemented by passing `simplified: SimplifiedDresserLayout` into `DrawEquipIcon` / `DrawBonusItemIcon` / `DrawSingleWeaponIcon` (the config flag drives the `simplified` parameter; `stainsBeside: true` is passed unconditionally regardless of the simplified flag)
- **Override window background** (`OverrideDresserBgColor` + `ImmersiveDresserBgColor`) — replaces the Left/Right window background with a user-picked `Rgba32`. To keep checkboxes/inputs readable when the background is translucent, the panels also force `FrameBackground{,Hovered,Active}` to `| 0xFF000000` (alpha-`0xFF`) every frame
- Full **preview-on-hover** support — uses the same `EquipmentDrawer` combos as the Actor panel (see [Preview-on-Hover](#2-preview-on-hover)); the Options panel also drives the Dye-All-Slots preview via `ApplyAllStainHoverPreview`
- **Panel lock** — icon button in the Options panel sets `WindowFlags.NoMove` on all three panels
- **Game UI toggle** — icon button hides/shows the native FFXIV HUD while keeping ImGui windows visible (controlled by `AutoHideGameUi`; only toggles UI visibility, never forces it off at open unless `AutoHideGameUi` is persisted as `true`)
- **Free cam** — icon button runs `/cammy freecam` via Dalamud's `ICommandManager` when the [Cammy plugin](https://github.com/Ottermandias/Cammy) is installed; the button stays disabled otherwise and highlights green when free cam is active (detected by `cam->MaxDistance <= 0.1f`)
- **Camera height slider** — `ImmersiveDresserCameraY` (range `-2`…`2`) offsets the scene camera's Y while the dresser is open. The camera-update detour clamps to ground via a `BGCollisionModule` raycast (unless `AllowCameraClipping` is enabled) and writes the clamped value back so the slider reflects reality
- **Disable first person** — `DisableFirstPerson` overrides FFXIV's "Switch to 1st person view when fully zoomed in" game-config option (`UiControlOption.AutoChangePointOfView`) to `false` for the dresser session via `IGameConfig`, snapshotting the player's original value on open and restoring on close. On activation it also one-shots the camera back to third-person if it was already in first-person (the game-config option only governs *future* zoom-driven transitions)
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
| `Glamourer/GlamorousTerror/IconEquipment/EquipmentDrawer.IconMode.cs` | `DrawEquipIcon` / `DrawBonusItemIcon` / `DrawSingleWeaponIcon` — GT-only icon-render entry points the dresser drives directly |
| `Glamourer/Gui/Equipment/EquipmentDrawer.cs` | `DrawAllStain` / `DrawMetaToggle` (static) — upstream entry points reused by the Options panel |
| `Glamourer/Gui/Materials/AdvancedDyePopup.cs` | `Draw(...)` accepts a `forceFloating` flag (defaults `false`) so the dresser panels can render the popup as a free-floating window regardless of `KeepAdvancedDyesAttached` |
| `Glamourer/GlamorousTerror/Config/Configuration.GT.cs` | `EnableImmersiveDresser`, `SingleWindowDresser`, `SimplifiedDresserLayout`, `OverrideDresserBgColor`, `ImmersiveDresserBgColor`, `AutoHideGameUi`, `LockImmersiveDresserPanels`, `ImmersiveDresserCameraY`, `AllowCameraClipping`, `DisableFirstPerson` |
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
| `_targetIdentifier` | Permanent `ActorIdentifier` for the dresser's current target. `ActorIdentifier.Invalid` (the default) means "follow the local player"; any other value pins the dresser to that actor via `ResolveTarget()` |
| `_objects` | `ActorObjectManager` — used by `ResolveTarget` and the target picker to enumerate live actors |
| `_advancedDyes` | The shared `AdvancedDyePopup` instance (passed in via DI); the equipment/accessory panels invoke `_advancedDyes.Draw(...)` themselves with `forceFloating: true` |
| `_advancedDyesDrawnFrame` | Frame counter so the advanced-dye popup only `Begin`s once per frame across the Left and Right panels |
| `_didHideUi`, `_wasUiVisible`, `_savedDisableUserUiHide` | Save/restore state for the game-UI hide toggle |
| `_savedAutoChangePointOfView` | Snapshot of the player's original "Switch to 1st person view when fully zoomed in" game-config value; `null` when no override is in flight |
| `_cammyFreeCamActive`, `_lastValidCameraY` | Free-cam and camera-height detour state |
| `_cameraUpdateHook` | Dalamud `Hook<>` attached via vtable offset 3 on the active `CameraBase` (camera-update detour for the camera-height slider) |

**Constructor** — Receives `EquipmentDrawer`, `CustomizationDrawer`, `CustomizeParameterDrawer`, `PreviewService`, `StateManager`, `ActorObjectManager`, `Configuration`, `IUiBuilder`, `IKeyState`, `IFramework`, `ICommandManager`, `IGameConfig`, `IGameInteropProvider`, `RotationDrawer`, `DesignConverter`, `DesignManager`, `EditorHistory`, `AdvancedDyePopup` via DI. Creates the three panel instances. The camera-update hook is installed lazily via `EnsureCameraHook()` on the first `Open()` (the lobby camera has a different vtable from the world camera). The `AdvancedDyePopup` reference is exposed as `_advancedDyes` so the equipment/accessory panels can render the popup window themselves (see Panel Details).

### Target Resolution

The dresser is no longer pinned to the local player — it can be retargeted to any actor with a model. Three pieces drive this:

- **`Open(ActorIdentifier identifier = default)`** — Initial target. The context-menu entry and `/glamour dresser` command both pass `default` (→ player); IPC or other callers may pass a specific identifier. Stored as a permanent copy via `identifier.CreatePermanent()` so the dresser doesn't hold a borrowed reference.
- **`ResolveTarget() → (ActorIdentifier, ActorData)`** — Called by each panel's `DrawConditions()` and at the top of each panel's `Draw()`. Resolution order:
  1. If `_targetIdentifier.IsValid` AND `_objects.TryGetValue(_targetIdentifier, ...)` returns a valid `ActorData`, use that.
  2. Otherwise fall back to `_objects.PlayerData`.
  
  Falling back when the override target despawns means a dressed-up alt or party member doesn't crash the dresser when they leave the area — it just snaps back to the player until the user re-picks.
- **`SetTarget(ActorIdentifier identifier)`** — Swaps the target. **Critical sequence** (see Pitfall #11):
  1. Resolve the new identifier (permanent copy or `Invalid`)
  2. No-op if it equals the current target
  3. Call `_previewService.EndPreview()` — restore any in-flight preview on the OLD target before the swap (otherwise the old actor is left with the preview state still written into game memory)
  4. Call `_rotationDrawer.Reset()` — clear the rotation override on the old target (so it doesn't orphan)
  5. Then assign `_targetIdentifier = resolved`

Passing `default` (or `ActorIdentifier.Invalid`) to `SetTarget` reverts to "follow the player" mode.

**`DrawTargetPicker(currentId)`** (Options panel, line ~932) — the UI that drives `SetTarget`:

- A 220-px wide combo (`##dresserTarget`) listing every entry in `_objects` (preview text is `{name} (Self)` for the player, `{name}` for others). Selecting a row calls `SetTarget(pair.Key)`. The combo has a top-row "Return to Self" entry when an override is active.
- A "hand-pointer" icon button next to the combo — switches the dresser to whatever actor the player currently has targeted in-game (only enabled when a target exists and differs from the dresser's current target).
- A separate "Return to Self" button to the right of the combo when an override is active — equivalent to selecting the combo's first row but always visible for quick access.

The picker is rendered above the Equipment-mode block in the Options panel.

**`Open()`** — Guarded by `_isOpen`:

1. Resets `ImmersiveDresserCameraY` to `0f`
2. Saves the current `IUiBuilder.DisableUserUiHide` value, sets it to `true`
3. Subscribes to `IFramework.Update` for ESC polling
4. Lazily installs (`EnsureCameraHook()`) and enables the camera-update hook
5. If `DisableFirstPerson` is `true`, calls `ApplyFirstPersonOverride()` — snapshots `UiControlOption.AutoChangePointOfView`, writes `false`, and one-shots the camera to third-person if it was already in first-person
6. If `AutoHideGameUi` is `true`, hides the game UI and records `_didHideUi = true`
7. Opens all three panel windows

**`Close()`** — Guarded by `_isOpen`:

1. Unsubscribes from `IFramework.Update`
2. Calls `_rotationDrawer.Reset()` so any active rotation override is cleared
3. If free-cam was active, toggles it back off by sending `/cammy freecam` again
4. Disables the camera-update hook
5. Closes all three windows
6. Restores game-UI visibility (only if it was hidden by the dresser)
7. Calls `RestoreFirstPersonOverride()` — writes the snapshotted `AutoChangePointOfView` value back (no-op if no override was active)
8. Restores the saved `DisableUserUiHide` value

**`Dispose()`** — Calls `Close()` if still open, then disposes the camera-update hook.

### Panel Details

All three panels share:

- **`PanelFlags`**: `NoTitleBar | NoDocking | AlwaysAutoResize | NoCollapse` — auto-sized to content
- In **Appearance mode** the left panel clears `NoTitleBar | NoCollapse` so the customization drawer has a title bar (easier to resize/move). The **Options** panel always clears those flags (so the user can always collapse it)
- **`DrawConditions()`**: Returns `manager.ResolveTarget().Data.Valid` — panels only render when the resolved target (player by default, override if `_targetIdentifier` is set) has a valid actor. The Right panel additionally returns `false` in Equipment mode when `SingleWindowDresser` is on (everything is in the Left panel) and only renders in Appearance mode when `_showParameters` is set
- **`PreDraw()`**: Positions the window via `Im.Window.SetNextPosition(center ± offset, Condition.FirstUseEver, pivot)` — ImGui remembers user repositioning via its ini file. When `LockImmersiveDresserPanels` is set, `WindowFlags.NoMove` is OR'd in each frame
- **Style stack (Left/Right only)** — Each panel holds an `Im.ColorStyleDisposable _style` field that is pushed in `PreDraw()` and disposed in `PostDraw()`. In Equipment mode it stacks `WindowPadding = (GlobalScale * 4, GlobalScale * 4)` and `WindowBorderThickness = 0`. When `OverrideDresserBgColor` is on, `ImGuiColor.WindowBackground` is pushed from `ImmersiveDresserBgColor`. Unconditionally, `FrameBackground{,Hovered,Active}` are pushed with their existing color OR'd with `0xFF000000` to keep frame widgets opaque even when the window background is translucent. The Options panel does not currently use this stack
- **`OnClose()`**: Delegates to `manager.Close()` — safe against re-entrancy due to the `_isOpen` guard

**EquipmentPanel** (left of center, pivot `1, 0.5`):

- **Equipment mode**: `equipmentDrawer.Prepare(false)` (the `compact` overload — passed `false` so the dresser uses full-width combos), draws Main Hand weapon icon (`DrawSingleWeaponIcon`, `stainsBeside: true`), iterates `EquipSlotExtensions.EquipmentSlots` (or `EqdpSlots` when `SingleWindowDresser` is on, which extends through the accessory range) → `DrawEquipIcon`, optionally draws the offhand at the end (single-window-mode-only, when offhand `Type` is not `Unknown`), iterates `BonusExtensions.AllFlags` → `DrawBonusItemIcon`, then `ApplyHoverPreview`. All three icon-draw calls forward `stainsBeside: true` (unconditional) and `simplified: SimplifiedDresserLayout` (config-driven)
- **Appearance mode**: draws `CustomizationDrawer` (with full-customize change dispatch), then `customizationDrawer.ApplyHoverPreview(stateManager, state)`
- **Advanced dye popup** — after the mode-specific block, when `manager._advancedDyesDrawnFrame != Im.State.FrameCount`, **stamps the counter first** then calls `manager._advancedDyes.Draw(playerData.Objects[0], state, centered: false, forceFloating: true)`. Stamping before the draw means a re-entrant call from inside Draw (or from the sibling panel later in the same frame) sees the counter already current and skips. `forceFloating: true` overrides `KeepAdvancedDyesAttached` so the popup opens as a free-floating ImGui window the user can drag, instead of pinning to the right of the panel

**AccessoryPanel** (right of center, pivot `0, 0.5`):

- **Equipment mode** (only renders when `SingleWindowDresser` is off): draws off-hand only when `offhand.CurrentItem.Type is not FullEquipType.Unknown` (classes like DRG/MNK show no gap), iterates `EquipSlotExtensions.AccessorySlots` → `DrawEquipIcon`, then `ApplyHoverPreview`. Same `stainsBeside: true` / `simplified: SimplifiedDresserLayout` forwarding as the Left panel
- **Appearance mode**: draws `CustomizeParameterDrawer` (gated by `_showParameters`)
- **Advanced dye popup** — same call/guard pair as the EquipmentPanel. Both panels invoke `Draw(...)` so accessory clicks are responsive in split-window mode; the `_advancedDyesDrawnFrame` guard ensures `Im.Window.Begin("###Glamourer Advanced Dyes")` only runs once per frame

**OptionsPanel** (below center, pivot `0.5, 0`):

1. **Mode switch** — "Switch to Appearance" / "Switch to Equipment" button (leaving Appearance calls `PreviewService.EndCustomizationPopupFrame(state)` to drop any active popup preview)
2. **Reset to Game State** — `stateManager.ResetState(state, StateSource.Manual, isFinal: true)`
3. **Design actions row** (`DrawDesignActions`) — clipboard-in / clipboard-out / save / undo icon buttons, with modifier-driven apply rules (`UiHelpers.ConvertKeysToBool()`)
4. **Right-aligned icon buttons** — game-UI eye, panel lock, free-cam video; positioning computed from `Im.ContentRegion.Available.X` so they sit flush right on the same line as the design actions row
5. **Target picker** — `DrawTargetPicker(id)` renders the actor combo + "switch to current in-game target" hand-pointer icon + "Return to Self" button (see [Target Resolution](#target-resolution) above). Sits above the Equipment-mode block
6. **Dye All Slots + meta toggles** (Equipment mode only):
   - `equipmentDrawer.DrawAllStain()` combo; on selection, writes `StainIds.All(newAllStain)` to every `EqdpSlot` via `stateManager.ChangeStains`
   - Always followed by `equipmentDrawer.ApplyAllStainHoverPreview(stateManager, state)` (the preview dispatcher specific to this panel)
   - Four inline meta-toggle groups: `HatState + Head Crest`, `VisorState + Body Crest`, `WeaponState + OffHand Crest`, `EarState` alone
7. **Dresser Settings tree header** (Equipment mode only) — `Single Window Layout`, `Simplified Layout`, and `Override Window Background` checkboxes. The override row inlines an `Im.Color.Editor` swatch (`AlphaBar | AlphaPreviewHalf | NoInputs`) bound to `ImmersiveDresserBgColor`, gated on `OverrideDresserBgColor`
8. **Camera tree header** (hidden when free-cam is active) — `ImmersiveDresserCameraY` slider + Reset, `AllowCameraClipping` checkbox, `DisableFirstPerson` checkbox. Toggling the checkbox while the dresser is open calls `ApplyFirstPersonOverride()` / `RestoreFirstPersonOverride()` live; toggling while closed only persists the bool and waits for the next `Open()`
9. **Character Rotation tree header** — `manager._rotationDrawer.Draw(objects.Player)` (see [Character Rotation](#10-character-rotation))
10. **Show Color Customization** (Appearance mode only) — toggles `_showParameters` to reveal the Right panel's parameter drawer
11. **Save as Design popup** — `InputPopup.OpenName(...)` prompts for a name and calls `DesignManager.CreateClone(_newDesign, name, true)`

### Camera Hooks

One vtable hook on the active `CameraBase`:

- **`CameraUpdateDetour` (vtable[3])**: Runs after the original camera update. If `ImmersiveDresserCameraY != 0`, it computes a candidate Y, optionally clamps against ground (via `BGCollisionModule.RaycastMaterialFilter` with a `minHeightAboveGround = 0.5f`), writes the clamped offset to both `SceneCamera.Position.Y` and `SceneCamera.LookAtVector.Y`, and — if the clamp changed the offset — writes the clamped value back into `ImmersiveDresserCameraY` so the slider and reality stay in sync.

The hook is enabled in `Open()` and disabled in `Close()`. It is installed lazily via `EnsureCameraHook()` on the first `Open()` because the lobby camera has a different vtable from the world camera — hooking at construction time would silently miss the world camera once the player zones in. The hook field is nullable and silently no-ops if no active camera exists at the time of installation.

### First-Person Lockout

`DisableFirstPerson` is implemented as a snapshot/override/restore of FFXIV's "Switch to 1st person view when fully zoomed in" game-config option (`UiControlOption.AutoChangePointOfView`, exposed by Dalamud as a `bool`):

- **`ApplyFirstPersonOverride()`**: If no override is in flight, reads the player's current `AutoChangePointOfView` via `_gameConfig.TryGet`, stores it in `_savedAutoChangePointOfView`, then writes `false` (auto-switch off). Also one-shots `cam->ZoomMode = ThirdPerson` (with `ControlMode`, `Distance`, `InterpDistance` writes mirroring the prior force-flip) if the camera was already in first-person — `AutoChangePointOfView` only governs *future* zoom-driven transitions, so the player would otherwise stay stuck in first-person until something else moved the camera.
- **`RestoreFirstPersonOverride()`**: No-op if `_savedAutoChangePointOfView` is null. Otherwise writes the snapshotted value back via `_gameConfig.Set` and clears the snapshot.

Called from `Open()` (gated on `DisableFirstPerson`), `Close()` (always — restore is no-op if no override), and the Options-panel checkbox handler (gated on `manager._isOpen`). Both methods must run on the framework thread; all callers already do.

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
| `SingleWindowDresser` | `bool` | `false` | Collapse equipment + accessories into the Left panel; hide the Right panel in Equipment mode |
| `SimplifiedDresserLayout` | `bool` | `false` | Stack dye channels vertically beside each icon and hide the advanced-dye buttons |
| `OverrideDresserBgColor` | `bool` | `false` | Replace the Left/Right window backgrounds with `ImmersiveDresserBgColor` |
| `ImmersiveDresserBgColor` | `Rgba32` | `default` | Custom background color used when `OverrideDresserBgColor` is on |
| `AutoHideGameUi` | `bool` | `false` | Hide the FFXIV HUD automatically when the dresser opens |
| `LockImmersiveDresserPanels` | `bool` | `false` | Pins all three panels in place (`WindowFlags.NoMove`) |
| `ImmersiveDresserCameraY` | `float` | `0f` | Camera Y-offset while open; auto-reset to 0 on `Open()` |
| `AllowCameraClipping` | `bool` | `false` | Skip ground-ray clamping on the camera Y offset |
| `DisableFirstPerson` | `bool` | `false` | While the dresser is open, override the in-game "Switch to 1st person view when fully zoomed in" game-config option to `false` (and snap out of first-person if active); restore the player's value on close |

### Known Pitfalls

1. **`DisableUserUiHide` must be set before hiding**: Hiding the game UI via `RaptureAtkModule.IsUiVisible = false` also hides all ImGui windows unless `IUiBuilder.DisableUserUiHide` is set to `true` first.

2. **`Close()` re-entrancy**: Each window's `OnClose()` callback calls `manager.Close()`. Without the `_isOpen` guard, `Close()` would execute its restore logic multiple times. The guard ensures only the first call runs the full teardown.

3. **ESC requires `IKeyState` via `IFramework.Update`**: The ImGui keyboard API only detects keys when an ImGui window has focus. With `NoTitleBar` panels and no guaranteed focus, `IKeyState` (Dalamud's game keyboard state) must be used instead, polled on the framework update thread. After detecting ESC, the key must be consumed (`_keyState[VirtualKey.ESCAPE] = false`) to prevent the game from also processing it and opening the system menu.

4. **Camera hook vtable offset**: The camera-update detour attaches to vtable index 3 on `CameraBase`. If FFXIVClientStructs changes its layout or the game patches the camera class, this offset must be re-verified. The hook is nullable and silently no-ops if no active camera exists at install time.

5. **`AutoChangePointOfView` is a one-way override, not a hard prohibition**: Setting it to `false` only blocks the *zoom-driven auto-switch* into first-person. The player can still enter first-person via `/firstperson`, `/freecam`-style commands, or anything else that writes `cam->ZoomMode` directly. The one-shot snap in `ApplyFirstPersonOverride()` ejects an active first-person camera *at apply time* — but if the player force-enters first-person mid-session, nothing here pulls them back out. This is by design (we replaced the per-frame force-flip with the native config option to stop fighting the player's other tools); if a future surface needs hard prohibition, it has to add its own per-frame check or a different hook.

6. **Snapshot must be symmetric**: `_savedAutoChangePointOfView` is a `bool?` — `null` means "not currently overriding." Both `ApplyFirstPersonOverride()` (early-returns if non-null) and `RestoreFirstPersonOverride()` (early-returns if null) gate on this, so double-apply / double-restore are safe. Without this, an `Open()` → mid-session checkbox-toggle-off → `Close()` sequence would try to restore a null snapshot, or a re-entrant apply would clobber the originally captured value.

7. **Camera-offset writeback**: When the ground raycast clamps `candidateY`, the clamped offset is written back into `ImmersiveDresserCameraY` so the slider reflects the actual applied offset. Without this writeback the slider would show a larger value than the camera is using.

8. **Free-cam detection is heuristic**: Free-cam is inferred from `cam->MaxDistance <= 0.1f` plus the `/cammy` command being registered. There is no IPC handshake with Cammy — if Cammy changes how it marks the camera, this detection must be revisited.

9. **`ApplyAllStainHoverPreview` must be called by whatever panel draws `DrawAllStain`**: The Options panel draws it, so the Options panel calls it. If a new surface adds a Dye-All-Slots combo and forgets the dispatcher, the preview will stick on the character after the popup closes.

10. **Advanced dye popup needs an explicit `Draw()` call**: `EquipmentDrawer.DrawEquipIcon` only renders the palette **button** (which sets `_drawIndex` on `AdvancedDyePopup`). The popup window itself is rendered by `AdvancedDyePopup.Draw(...)` — `EquipmentPanel.Draw()` and `AccessoryPanel.Draw()` both call it after the equipment loop, gated by `_advancedDyesDrawnFrame == Im.State.FrameCount` so split-window mode does not double-`Begin` the popup window in the same frame. `forceFloating: true` is passed so the popup ignores `KeepAdvancedDyesAttached` (otherwise it would pin to the calling panel and visually overlap the other panel in split mode).

11. **`SetTarget` re-entrancy during active preview**: `SetTarget` calls `_previewService.EndPreview()` AND `_rotationDrawer.Reset()` **before** writing `_targetIdentifier`. Without the `EndPreview` call, an in-flight preview on the old target would remain written into game memory after the swap — leaving the old actor dressed up in whatever the user was hovering. Without the `Reset` call, a rotation override on the old target would silently re-bind to the new target's address on the next framework update (since the override dictionary is keyed by actor address, not identifier). Both restores must run before the field write; reorder at your peril.

12. **Free-cam close-time toggle is best-effort**: `Close()` sends `/cammy freecam` again if `_cammyFreeCamActive` is true, to toggle Cammy back off. If Cammy was unloaded between `Open()` and `Close()` (the user can `/xlplugins` disable it mid-session), the command is a no-op and the player is left in free-cam. There is no IPC handshake — the heuristic is "if we thought it was on, try to turn it off." Acceptable failure mode; documented here so a future maintainer doesn't see the "extra" toggle as a bug.

---

## 12. UI Actor Glamour Mirroring

Makes the character models the game renders inside menus reflect a character's glamoured appearance instead of their real, equipped gear. These "UI actors" are special menu actors — object indices 440–447, which resolve to `IdentifierType.Special` — that FFXIV spawns to preview a character in a window. Out of the box they show the real character; with mirroring on they show whatever glamour Glamourer has active for that character.

### Surfaces

Six surfaces are in scope, each independently toggleable with its own customize/gear sub-toggles:

| Surface | Config prefix | Real character resolved via |
|---------|---------------|------------------------------|
| Own character window | `MirrorCharacterWindow` | `ActorManager.GetCurrentPlayer()` (`ScreenActor.CharacterScreen`) |
| Examine (others) | `MirrorExamine` | `ActorManager.GetInspectPlayer()` (`ScreenActor.ExamineScreen`) |
| Fitting room | `MirrorFittingRoom` | `ActorManager.GetCurrentPlayer()` (`ScreenActor.FittingRoom`) |
| Dye preview | `MirrorDyePreview` | `ActorManager.GetCurrentPlayer()` (`ScreenActor.DyePreview`) |
| Adventurer plate portrait | `MirrorAdventurerPlate` | `ActorManager.GetCurrentPlayer()` (`ScreenActor.Portrait`) |
| Party / PvP banners | `MirrorBanner` | `ActorManager.ResolvePartyBannerPlayer()` / `ResolvePvPBannerPlayer()` |

The master switch `MirrorUiActors` gates all six; with it off the feature is fully inert. Each surface has `Mirror<Surface>` (enable), `Mirror<Surface>Customize` (mirror appearance), and `Mirror<Surface>Gear` (mirror equipment) flags.

**Deferred / out of scope** (v1): mahjong portraits, the login character-select lobby, the glamour-plate editor mannequin, and other players' synced (Mare) appearance.

### Appearance Source

The remapped identifier flows through the existing automation/state pipeline, so the appearance shown is exactly what Glamourer would apply to that character normally:

- **Self** — the character's **active state** (`AutoDesignApplier.Reduce` resolves the live `ActorState`), including any manual tweaks the user has made in the Glamourer window.
- **Others** — the **matching automation design** for that identifier, if one is enabled. No automation set → nothing mirrored (the real appearance shows through).

### Apply Mechanism

Two GT partial methods on `StateListener` (declared in `Glamourer/State/StateListener.cs`, implemented in `Glamourer/GlamorousTerror/UiActorMirror/StateListener.UiActor.cs`) hook `OnCreatingCharacterBase`:

1. **`GTRemapUiActor()`** — runs immediately after `_creatingIdentifier = actor.GetIdentifier(_actors);`. It calls `UiActorMirrorService.TryResolve(specialId, …)`, which:
   - Returns early unless `MirrorUiActors` is on and the identifier is `IdentifierType.Special`.
   - Maps the `ScreenActor` to a surface and resolves the real character identifier (table above), resolving banner/card contexts first since they can occupy `CharacterScreen..Card8`.
   - Builds a `UiActorMask(Customize, Gear)` from the surface's config flags.

   On success it rewrites `_creatingIdentifier` to the **real character id**, so the unchanged upstream `AutoDesignApplier.Reduce(actor, _creatingIdentifier, …)` resolves that character's glamour state into `_creatingState`.

2. **`GTApplyUiActor(nint customizePtr, nint equipDataPtr)`** — runs after the `switch (UpdateBaseData(...))` block and before `_creatingState.TempUnlock();`. It authoritatively writes from `_creatingState.ModelData` into the game's customize/equip buffers for the enabled aspects:
   - If `mask.Customize`: `*(CustomizeArray*)customizePtr = _creatingState.ModelData.Customize`.
   - If gear is allowed: for each `EqdpSlot` (armor buffer indices 0–9), writes `_creatingState.ModelData.ArmorWithState(slot)` — **skipping any slot flagged in the previewed-slot mask** (see below).

This is an authoritative write rather than a diff, so it overrides whatever the game put in the buffers for the menu actor.

### Preview-Slot Preservation

The fitting room and dye preview are *meant* to show the item the player is currently trying on or dyeing. Mirroring gear over those slots would defeat the window's purpose, so for the `FittingRoom` and `DyePreview` surfaces (when gear mirroring is on) `GTRemapUiActor` consults `UiActorPreviewSlots.TryGetPreviewedSlotMask(out mask)`:

- `UiActorPreviewSlots` reads `AgentTryon` (`AgentTryon.Instance()`), iterating its `TryOnItems` and setting a bit per armor-buffer index for each slot with a non-zero item or an active dye preview. `GTApplyUiActor` then skips those bits, so the tried-on/dyed slots keep the game's preview while the rest of the body mirrors the glamour.
- **Customize-only fallback** — if the agent is unavailable (null or inactive), `TryGetPreviewedSlotMask` returns `false`. Rather than risk clobbering the player's in-progress preview, `GTRemapUiActor` then clears `_gtUiGearAllowed` for that surface, mirroring **customizations only** and leaving all gear as the game rendered it.

### Safety & Limitations

- **Render-only** — nothing is persisted. The feature only writes into the transient buffers the game hands to the menu actor at creation time; no design, automation, or character state is modified or saved.
- **No live re-mirror (v1)** — mirroring is applied once, when the UI actor is created. If the source character's glamour changes while a mirroring window stays open, the window does **not** refresh. Close and reopen the window to pick up the new appearance.

### Configuration

All flags live on the GT config partial (`Glamourer/GlamorousTerror/Config/Configuration.GT.cs`). The settings UI is a "UI Actors" tree node in the Glamorous Terror settings section (`SettingsTab.GT.cs` → `DrawUiActorMirrorSettings`); the per-surface toggles are hidden until the `MirrorUiActors` master switch is on.

| Property | Type | Default |
|----------|------|---------|
| `MirrorUiActors` | `bool` | `false` |
| `Mirror<Surface>` | `bool` | `false` |
| `Mirror<Surface>Customize` | `bool` | `true` |
| `Mirror<Surface>Gear` | `bool` | `true` |

where `<Surface>` ∈ { `CharacterWindow`, `Examine`, `FittingRoom`, `DyePreview`, `AdventurerPlate`, `Banner` }.

---

## Configuration Summary

All GlamorousTerror-specific properties live in `Glamourer/GlamorousTerror/Config/Configuration.GT.cs` (a partial of the upstream `Configuration` class). The serialized Fun Modes bitmask (`EnabledCheats`) lives on the GT partial too; the upstream festival fields (`FestivalMode`, `LastFestivalPopup`) and the now-dead `Codes` list remain on the upstream `Configuration` partial.

| Property | Type | Default | Feature |
|----------|------|---------|---------|
| `EnableGameContextMenu` | `bool` | `true` | Context Menu |
| `EnableImmersiveDresser` | `bool` | `true` | Immersive Dresser |
| `SingleWindowDresser` | `bool` | `false` | Immersive Dresser (layout) |
| `SimplifiedDresserLayout` | `bool` | `false` | Immersive Dresser (layout) |
| `OverrideDresserBgColor` | `bool` | `false` | Immersive Dresser (styling) |
| `ImmersiveDresserBgColor` | `Rgba32` | `default` | Immersive Dresser (styling) |
| `AutoHideGameUi` | `bool` | `false` | Immersive Dresser |
| `LockImmersiveDresserPanels` | `bool` | `false` | Immersive Dresser |
| `ImmersiveDresserCameraY` | `float` | `0f` | Immersive Dresser (camera) |
| `AllowCameraClipping` | `bool` | `false` | Immersive Dresser (camera) |
| `DisableFirstPerson` | `bool` | `false` | Immersive Dresser (camera) |
| `UseIconEquipmentDrawer` | `bool` | `false` | Icon Equipment Drawer |
| `IconPickerMaxRows` | `int` | `10` | Icon Equipment Drawer |
| `GroupIconPickerByModel` | `bool` | `true` | Icon Equipment Drawer |
| `KeepIconPickerOpen` | `bool` | `false` | Icon Equipment Drawer |
| `IconPickerPinned` | `bool` | `false` | Icon Equipment Drawer |
| `RememberIconPickerScroll` | `bool` | `false` | Icon Equipment Drawer |
| `EnabledCheats` | `CodeService.CodeFlag` | `0` | Fun Modes (the live serialized mode bitmask; `Configuration.GT.cs:49`) |
| `Codes` | `List<(string Code, bool Enabled)>` | `[]` | Fun Modes (upstream — orphaned/dead plaintext passphrase list) |
| `FestivalMode` | `FestivalSetting` | `Undefined` | Fun Modes (festivals; upstream) |
| `LastFestivalPopup` | `DateOnly` | `MinValue` | Fun Modes (festivals; upstream) |
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
