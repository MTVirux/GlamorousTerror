# GlamorousTerror — Upstream Hooks Reference

> This document lists every modification made to upstream Glamourer files to
> support GlamorousTerror features. When syncing from upstream, re-apply these
> changes after the merge.

## Architecture

All GT feature code lives in `Glamourer/GlamorousTerror/` organized by feature.
Upstream files are modified minimally — most use C# **partial classes** with
**extended partial methods** so that GT logic is compiled in from separate files.

---

## GT Feature Directory

```
Glamourer/GlamorousTerror/
  Config/
    Configuration.GT.cs          ← GT config properties (partial of Configuration)
    SettingsTab.GT.cs            ← GT settings UI (partial of SettingsTab)
  ContextMenu/
    CharacterPopupMenu.cs        ← In-game character context menu (standalone)
    ContextMenuService.cs        ← Context menu hook service (standalone)
  EquipmentLanguage/
    ItemNameService.cs           ← Multi-language item name lookups (standalone)
  IconEquipment/
    EquipmentDrawer.IconMode.cs  ← Icon picker mode (partial of EquipmentDrawer)
  ImmersiveDresser/
    ImmersiveDresserWindow.cs    ← Immersive dresser panels (standalone)
  ItemOwnership/
    BaseItemCombo.GT.cs          ← Owned-only + cross-language filter (partial of ItemFilter)
    CustomizeUnlockManager.cs    ← Customization unlock tracking (standalone)
    EquipmentDrawer.OwnedFilter.cs ← Owned-only filter UI (partial of EquipmentDrawer)
    FavoriteManager.cs           ← Favorite items tracking (standalone)
    ItemUnlockManager.cs         ← Item unlock tracking (standalone)
    UnlockDictionaryHelpers.cs   ← Unlock dict utilities (standalone)
    UnlockRequirements.cs        ← Unlock requirement definitions (standalone)
  PreviewOnHover/
    CustomizationDrawer.Preview.cs ← Customization hover preview (partial of CustomizationDrawer)
    DesignPreviewService.cs      ← Design preview orchestration (standalone)
    EquipmentDrawer.Preview.cs   ← Equipment hover preview (partial of EquipmentDrawer)
    PreviewService.cs            ← Core preview state machine (standalone)
  WildcardAutomation/
    AutoDesignApplier.Wildcard.cs ← Wildcard name matching (partial of AutoDesignApplier)
```

---

## Upstream File Modifications

### 1. `Glamourer/Glamourer.cs` (Entry Point)

**Line ~50** — Add GT service initialization:
```csharp
_services.GetService<CharacterPopupMenu>();    // initialize custom popup menu.
```

### 2. `Glamourer/Gui/GlamourerWindowSystem.cs`

**Constructor parameter** — Add `ImmersiveDresserManager immersiveDresser`

**Constructor body** — Register immersive dresser windows:
```csharp
_windowSystem.AddWindow(immersiveDresser.Left);
_windowSystem.AddWindow(immersiveDresser.Right);
_windowSystem.AddWindow(immersiveDresser.Options);
```

### 3. `Glamourer/Config/Configuration.cs`

- Add `partial` keyword to class declaration
- GT properties are defined in `Configuration.GT.cs`

### 4. `Glamourer/Gui/Tabs/SettingsTab/SettingsTab.cs`

- Add `partial` keyword to class declaration
- Call `DrawGlamorousTerrorSettings()` in the draw method (line ~229)
- GT methods are defined in `SettingsTab.GT.cs`

### 5. `Glamourer/Gui/Equipment/EquipmentDrawer.cs`

- Add `sealed partial class` keywords
- **Constructor**: Add `PreviewService`, `ItemNameService`, `ItemUnlockManager` parameters
- **Fields**: Keep `_previewService` and `_itemUnlockManager` declarations (assigned in constructor)
- **`Prepare()`**: Call `GTResetPreviewState()` and `GTResetIconState()`
- **`DrawStain()`**: Call `GTCaptureStainSlot()` after popup open detection
- **`DrawEquip()`**: Call `GTTryDrawEquipIcon()` with early return
- **`DrawBonusItem()`**: Call `GTTryDrawBonusItemIcon()` with early return
- **`DrawWeapons()`**: Call `GTTryDrawWeaponsIcon()` with early return

**Partial method declarations** (at end of class):
```csharp
partial void GTResetPreviewState();
partial void GTResetIconState();
partial void GTCaptureStainSlot(EquipSlot slot, int index, bool isOpen);
private partial bool GTTryDrawEquipIcon(in DesignData designData, EquipSlot slot, State.ActorState? lockedState);
private partial bool GTTryDrawBonusItemIcon(in DesignData designData, BonusItemFlag slot, State.ActorState? lockedState);
private partial bool GTTryDrawWeaponsIcon(in DesignData designData, State.ActorState? lockedState);
```

### 6. `Glamourer/Gui/Equipment/BaseItemCombo.cs`

- Add `partial` to `BaseItemCombo` class declaration
- Add `partial` to nested `ItemFilter` class declaration
- **Constructor**: Add `ItemNameService`, `ItemUnlockManager` parameters
- Replace `WouldBeVisible()` body with `GTPreFilterItem` / `GTFallbackNameMatch` calls
- Remove inline `MatchesCrossLanguage()` method

**Partial method declarations** (inside `ItemFilter`):
```csharp
private partial bool GTPreFilterItem(in CacheItem item);
private partial bool GTFallbackNameMatch(in CacheItem item);
```

### 7. `Glamourer/Gui/Customization/CustomizationDrawer.cs`

- **Constructor**: Add `PreviewService previewService` parameter
- Call `ApplyHoverPreview()` in draw method
- `ApplyHoverPreview()` is implemented in `CustomizationDrawer.Preview.cs`

### 8. `Glamourer/Gui/Customization/CustomizationDrawer.Icon.cs`

- Remove GT preview method (moved to `CustomizationDrawer.Preview.cs`)

### 9. `Glamourer/Gui/Customization/CustomizationDrawer.Simple.cs`

- Remove GT preview method (moved to `CustomizationDrawer.Preview.cs`)

### 10. `Glamourer/Gui/Customization/CustomizationDrawer.Color.cs`

- Remove GT preview method (moved to `CustomizationDrawer.Preview.cs`)

### 11. `Glamourer/Automation/AutoDesignApplier.cs`

- Add `partial` keyword to class declaration
- Wildcard methods moved to `AutoDesignApplier.Wildcard.cs`
- `GetPlayerSet()` calls `TryGettingSetExactOrWildcard()` which is in the partial

### 12. `Glamourer/Gui/Tabs/ActorTab/ActorPanel.cs`

- **Line ~157**: Call `customizationDrawer.ApplyHoverPreview(...)` (1 line)
- **Line ~238**: Call `equipmentDrawer.ApplyHoverPreview(...)` (1 line)

---

## Sync Procedure

1. Merge/rebase upstream Glamourer changes
2. Resolve conflicts in the upstream files listed above
3. Re-apply `partial` keywords and GT hook calls where removed
4. Verify `Glamourer/GlamorousTerror/` files compile (partial implementations must match declarations)
5. Build: `dotnet build Glamourer/Glamourer.csproj -c Debug`
