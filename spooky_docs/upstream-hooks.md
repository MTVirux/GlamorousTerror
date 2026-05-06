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
- After the "Enable Auto Designs" checkbox, change the cursor offset from `Im.Style.FrameHeightWithSpacing * 4` to `Im.Style.FrameHeightWithSpacing`. Upstream reserves 4 lines for the stacked support-button column; GT only draws a single "Show Changelogs" button (see #19), so the larger offset leaves a visible empty gap above the settings child.

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
- **`DrawBehavior(in EquipItem, out EquipItem, float)`**: Mirror `Draw`'s state lifecycle so compact-mode surfaces (Equipment Bar) get hover-preview tracking. At the top, reset `IsPopupOpen = false; HoveredItem = null;`. In both selection branches (`CustomVariant.Id is not 0 && Identify(...)` and `if (ret) { ... }`), set `ItemSelected = true;` before `return true;`.

**Partial method declarations** (inside `ItemFilter`):
```csharp
private partial bool GTPreFilterItem(in CacheItem item);
private partial bool GTFallbackNameMatch(in CacheItem item);
```

### 7. `Glamourer/Gui/Customization/CustomizationDrawer.cs`

- **Constructor**: Add `PreviewService previewService` parameter
- Call `ApplyHoverPreview()` in draw method
- `ApplyHoverPreview()` is implemented in `CustomizationDrawer.Preview.cs`
- **`Init(...)`**: Call `GTResetPopupFlags();` as the first statement (before `UpdateSizes()`). `Init` runs at the top of every `Draw(...)` so this happens once per frame.
- Add the partial-method declaration directly after `Init` (or anywhere inside the partial class):
  ```csharp
  private partial void GTResetPopupFlags();
  ```
  The body lives in `CustomizationDrawer.Preview.cs` and clears `_iconPopupOpen`, `_listPopupOpen`, and `_colorPopupOpen`. **Why this matters:** popup bodies don't run when their popup is closed, so the flags would never be cleared from inside the popup itself. Without a once-per-frame reset they latch `true`, `ApplyHoverPreview` keeps a `SingleCustomization` preview alive forever, and the fall-through restore re-applies the captured `OriginalCustomizeValue` over external mutations like "Revert to Game State".

### 8. `Glamourer/Gui/Customization/CustomizationDrawer.Icon.cs`

- Remove GT preview method (moved to `CustomizationDrawer.Preview.cs`)

### 9. `Glamourer/Gui/Customization/CustomizationDrawer.Simple.cs`

- Remove GT preview method (moved to `CustomizationDrawer.Preview.cs`)

### 10. `Glamourer/Gui/Customization/CustomizationDrawer.Color.cs`

- Remove GT preview method (moved to `CustomizationDrawer.Preview.cs`)

### 11. `Glamourer/Automation/AutoDesignApplier.cs`

- Add `partial` keyword to class declaration
- Wildcard methods moved to `AutoDesignApplier.Wildcard.cs`
- `GetPlayerSet()` Player branch calls `TryGettingSetExactOrWildcard(identifier, out set)` as the final fallthrough

### 12. `Glamourer/Gui/Tabs/ActorTab/ActorPanel.cs`

- Call `_customizationDrawer.ApplyHoverPreview(_stateManager, _selection.State!)` at the end of `DrawCustomizationsHeader`
- After `EquipmentDrawer.DrawKeepItemFilter(_config)`, call `EquipmentDrawer.DrawOwnedOnlyFilter(_config)`
- Wrap the equipment-slot iteration in `if (_config.UseIconEquipmentDrawer) { ...icon rows... } else { ...combo rows... }`. The icon branch uses `DrawSingleWeaponIcon` (GT-only) and `EquipSlotExtensions.EquipmentSlots`/`AccessorySlots` to lay out 3 rows.
- After `_equipmentDrawer.DrawDragDropTooltip()`, call `_equipmentDrawer.ApplyHoverPreview(...)` and `_equipmentDrawer.ApplyAllStainHoverPreview(...)`

### 13. `Glamourer/Gui/Tabs/DesignTab/DesignPanel.cs`

- Same icon-mode branch as `ActorPanel`, plus `EquipmentDrawer.DrawOwnedOnlyFilter(_config)` after the keep-item filter

### 14. `Glamourer/Gui/Tabs/NpcTab/NpcPanel.cs`

- Same icon-mode branch as `ActorPanel`, no preview/owned-filter (NPCs are read-only)

### 15. `Glamourer/Services/CommandService.cs`

- Add ctor param `ImmersiveDresserManager immersiveDresser`, store in `_immersiveDresser`
- Register command aliases `/glam`, `/glamorous`, `/gt` (dispatch to `OnGlamourer`)
- Add `case "dresser": case "im":` in `OnGlamourer` argument switch — calls `_immersiveDresser.Open()`

### 16. `Glamourer/Gui/Equipment/GlamourerColorCombo.cs`

GT additions to power preview-on-hover for stain combos:

- After ctor, add `ResetFrameState()`, `HoveredStain`/`IsPopupOpen`/`StainSelected` properties, `ResetSelection()`, and an override of `PreDrawList()` that sets `IsPopupOpen = true` before delegating to base
- In `DrawItem`, capture `var ret = base.DrawItem(...)`, then `if (Im.Item.Hovered()) HoveredStain = new StainId((byte)item.Id);` before `return ret;`
- In `Draw(...)`, set `StainSelected = true;` inside the `if (ret) { ... }` block before `return true;`

### 17. `Glamourer/Services/FilenameService.cs`

GT adds a per-character path helper:

```csharp
public string UnlockFileItemsForCharacter(ulong contentId)
    => Path.Combine(ConfigurationDirectory, $"unlocks_items_{contentId:X16}.dat");
```

Use `ConfigurationDirectory` (from `BaseFilePathProvider`), not `pi.ConfigDirectory.FullName`, to avoid the CS9107 capture warning on the primary-constructor parameter.

### 18. `Glamourer/Gui/Equipment/{ItemCombo,BonusItemCombo,WeaponCombo}.cs`

Each subclass primary ctor must take `ItemNameService itemNameService, ItemUnlockManager itemUnlockManager` and forward them to `BaseItemCombo(...)`.

### 19. `Glamourer/Gui/MainWindow.cs`

Replace the body of `DrawSupportButtons` so only the "Show Changelogs" button is drawn (upstream draws a stack of Discord / Copy Support Info / ReniGuide / Show Changelogs / Ko-Fi-Patreon). The button is tinted with `ImGuiColor.Button.Push(0xFF000080)`:

```csharp
public static void DrawSupportButtons(Glamourer glamourer, Changelog changelog)
{
    var width = new Vector2(Im.Font.CalculateSize(SupportInfoButtonText).X + Im.Style.FramePadding.X * 2, 0);
    var xPos  = Im.Window.Width - width.X;

    Im.Cursor.Position = new Vector2(xPos, 0);
    using (ImGuiColor.Button.Push(0xFF000080))
    {
        if (Im.Button("Show Changelogs"u8, new Vector2(width.X, 0)))
            changelog.ForceOpen = true;
    }
}
```

`SupportInfoButtonText` and the private `DrawSupportButton` helper stay in the file (the former is still referenced for width sizing; the latter is dead code retained to minimise the diff against upstream).

Also update `GetLabel()` to render the window title as `Glamorous Terror` instead of upstream's `Glamourer`, with a ` (Testing Version)` suffix under `#if DEBUG`:

```csharp
private string GetLabel()
{
#if DEBUG
    const string suffix = " (Testing Version)";
#else
    const string suffix = "";
#endif
    return (Glamourer.Version.Length is 0, _config.Ephemeral.IncognitoMode) switch
    {
        (true, true)   => $"Glamorous Terror (Incognito Mode){suffix}###GlamourerMainWindow",
        (true, false)  => $"Glamorous Terror{suffix}###GlamourerMainWindow",
        (false, false) => $"Glamorous Terror v{Glamourer.Version}{suffix}###GlamourerMainWindow",
        (false, true)  => $"Glamorous Terror v{Glamourer.Version} (Incognito Mode){suffix}###GlamourerMainWindow",
    };
}
```

### 20. `Glamourer/Gui/EquipmentBarWindow.cs`

After `_equipmentDrawer.DrawDragDropTooltip();` in `Draw()`, call:

```csharp
_equipmentDrawer.ApplyHoverPreview(_stateManager, _selection.State!);
```

This wires preview-on-hover into the floating equipment bar (which uses compact-mode combos). `ApplyAllStainHoverPreview` is **not** needed here because the bar does not draw `DrawAllStain`. `_selection.State` is non-null here because `DrawConditions()` already gates on it.

### 21. `Glamourer/Gui/Materials/AdvancedDyePopup.cs`

**`forceFloating` parameter** — `Draw(Actor, ActorState, bool centered)` gains a trailing `bool forceFloating = false` parameter, threaded through to `DrawWindow(...)`. In `DrawWindow`, the attachment branch becomes:

```csharp
if (!forceFloating && config.KeepAdvancedDyesAttached)
{
    ...
}
```

Default behavior is unchanged for upstream callers (`ActorPanel`, `EquipmentBarWindow`). The Immersive Dresser's `EquipmentPanel`/`AccessoryPanel` pass `forceFloating: true` so the popup opens as a free-floating window the user can drag, instead of pinning to the right of the calling panel and overlapping the sibling panel in split-window mode.

**Right-click slot reset** — In the private `DrawButton(MaterialValueIndex, ColorParameter, bool)` overload, after the icon button, the upstream `Im.Tooltip.OnHover("Open advanced dyes for this slot.")` line is replaced with a conditional tooltip and a right-click handler that wipes every advanced-dye row across every material in the slot:

```csharp
var hasDyes = _state is not null && _state.Materials.CheckExistenceSlot(index);
if (Im.Item.Hovered())
{
    using var tt = Im.Tooltip.Begin();
    Im.Text("Open advanced dyes for this slot."u8);
    if (hasDyes)
        Im.Text("Right-click to revert this slot's advanced dyes to game state."u8);
}

if (hasDyes && Im.Item.RightClicked())
{
    var state = _state!;
    for (byte mat = 0; mat < MaterialService.MaterialsPerModel; ++mat)
    for (byte row = 0; row < ColorTable.NumRows; ++row)
        stateManager.ResetMaterialValue(state, index with { MaterialIndex = mat, RowIndex = row }, ApplySettings.Game);
}
```

`_state` is set by `Draw(actor, state, ...)` which always runs after the buttons, so on the very first frame (before any `Draw` has run) `_state` is null — the `is not null` guard prevents the right-click reset before there's a known actor. After the first frame, `_state` is the most recently rendered actor, which matches the panel the button lives in. The reset uses `ApplySettings.Game`, mirroring the existing per-row "Reset this row to game state" / per-material `DrawTabBar` right-click handlers.

### 22. `Glamourer/Gui/GlamourerChangelog.cs`

Upstream rewrites this file each version to append a new `Add1_X_Y_Z(Changelog)` entry. After overlaying, re-add the GT changelog block:

- In the constructor, append `AddGlamorousTerrorFeatures(Changelog);` as the **last** call, after the newest upstream `Add1_X_Y_Z(Changelog);`
- Re-add the `AddGlamorousTerrorFeatures(Changelog log)` method (titled `"Glamorous Terror Features"u8`) — the canonical entry list lives in this file on `main` and should be copied forward verbatim, then extended with any new GT features added during the port
- Change the `new Changelog(...)` window title to `"Glamourer Changelog (synced with upstream Glamourer X.Y.Z.W)"`, bumping `X.Y.Z.W` to the upstream version just overlaid

---

## Configuration Field Conflicts (1.6.0.5 → 1.6.1.4)

Upstream 1.6.1.4 introduced its own `EnableGameContextMenu` config field on `Configuration`. The duplicate field was removed from `Configuration.GT.cs`; the GT context-menu wiring (`contextMenuService.Enable/Disable()`) still hangs off the GT settings checkbox. Two checkboxes now bind to the same flag — upstream's (added in 1.6.1.4) and the GT one in `SettingsTab.GT.cs`. Consolidate before publish.

---

## Sync Procedure

1. Branch off `main` to `upstream-port-vX.Y.Z.W`; tag the pre-overlay commit as `backup/pre-upstream-port-vX.Y.Z.W`
2. Snapshot `Glamourer/GlamorousTerror/` and the GT-modified upstream files into `_custom_backup/` (gitignored)
3. Update submodules: rebase MTVirux Penumbra forks onto the latest upstream Ottermandias commits, push to a new branch on the MTVirux fork (`wildcard-on-vX.Y.Z.W`), and bump `.gitmodules` `branch = ` to that
4. `git checkout <new-tag> -- <list-of-Glamourer/-paths>` and `git rm` upstream copies of files we keep in GT folders (`Unlocks/*`, `Interop/ContextMenuService.cs`)
5. Re-apply hooks above. Verify partial method signatures match GT-folder declarations.
6. Restore the `AddGlamorousTerrorFeatures` block in `GlamourerChangelog.cs` (see #22) — upstream overwrites it on every version bump
7. Build: `.\scripts\build\debug.ps1` until 0 errors / 0 warnings
8. Smoke-test the Critical Invariants checklist from `CLAUDE.md` in-game
