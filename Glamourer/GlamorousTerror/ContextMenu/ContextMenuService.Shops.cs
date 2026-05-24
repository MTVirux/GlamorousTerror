using System;
using Dalamud.Game.Gui.ContextMenu;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Glamourer.Interop;

public sealed partial class ContextMenuService
{
    // Store/exchange addons whose item right-click populates AgentRecipeItemContext.ResultItemId
    // (the same agent the game uses for its native "Try On"/"Search for Item" submenu).
    private unsafe void GTTryAddShopItem(IMenuOpenedArgs args)
    {
        switch (args.AddonName)
        {
            case "Shop":                 // standard NPC vendors
            case "ShopExchangeItem":     // item-for-item exchange
            case "ShopExchangeCurrency": // currency exchange (e.g. MGP, seals)
            case "ShopExchangeCoin":     // Gold Saucer coin shop
            case "GrandCompanyExchange": // GC seal quartermaster
            case "FreeCompanyExchange":  // FC credit shop
            case "InclusionShop":        // scrip / tomestone
                break;
            default:
                return;
        }

        // Exchange shops (currency/item/coin/GC/FC) don't populate AgentRecipeItemContext, but the
        // item the cursor is over is reflected by the item-detail (tooltip) agent. Prefer that; fall
        // back to the recipe item context for surfaces that do set it (e.g. InclusionShop).
        var itemId = AgentItemDetail.Instance()->ItemId;
        if (itemId == 0)
            itemId = AgentRecipeItemContext.Instance()->ResultItemId;

        if (!HandleItem(itemId))
            return;

        Array.Clear(_lastStains, 0, _lastStains.Length); // shops carry no dye context
        args.AddMenuItem(_inventoryItem);
    }
}
