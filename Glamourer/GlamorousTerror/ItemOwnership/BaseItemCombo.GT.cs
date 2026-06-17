using Glamourer.Services;
using Glamourer.Unlocks;

namespace Glamourer.Gui.Equipment;

public abstract partial class BaseItemCombo
{
    /// <summary>
    /// Clears transient per-frame popup state. Called once per frame for every weapon combo so a combo that
    /// stops being drawn mid-session (because a mainhand hover preview changed the weapon type and re-keyed
    /// <c>_weaponCombo</c> to a different instance) cannot retain a stale <see cref="IsPopupOpen"/>/<see cref="HoveredItem"/>.
    /// Without this, the hover-preview loop would stick on the undrawn combo and never revert the preview.
    /// The live combo re-establishes its state when it is redrawn later in the same frame.
    /// </summary>
    public void GTResetFrameState()
    {
        IsPopupOpen = false;
        HoveredItem = null;
    }

    protected sealed partial class ItemFilter
    {
        private partial bool GTPreFilterItem(in CacheItem item)
            => !config.OwnedOnlyComboFilter || itemUnlockManager.IsOwnedFromSources(item.Item.ItemId, config.OwnedComboFilterSources);

        private partial bool GTFallbackNameMatch(in CacheItem item)
        {
            if (!config.CrossLanguageEquipmentSearch || Parts.Length is 0)
                return false;

            var itemId = item.Item.ItemId.Id;
            if (itemId is 0 || itemId >= uint.MaxValue - 512)
                return false;

            var allNames = itemNameService.GetAllLanguageNames(itemId);
            if (allNames == null)
                return false;

            foreach (var name in allNames)
            {
                if (!string.IsNullOrEmpty(name) && WouldBeVisible(name))
                    return true;
            }

            return false;
        }
    }
}
