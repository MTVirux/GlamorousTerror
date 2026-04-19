using Glamourer.Services;
using Glamourer.Unlocks;

namespace Glamourer.Gui.Equipment;

public abstract partial class BaseItemCombo
{
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
