using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Luna;
using Penumbra.GameData.Enums;

namespace Glamourer.Services;

/// <summary>
/// Reads the try-on / dye-preview agent to determine which armor slots the game is actively
/// previewing and whether it is showing the rest of the outfit, so glamour mirroring can preserve
/// the previewed slot and honour the window's "show other gear" toggle.
/// </summary>
public sealed unsafe class UiActorPreviewSlots : IService
{
    /// <summary>
    /// Reads the try-on/dye agent state: a bitmask of armor-buffer indices (bit n = slot whose
    /// ToIndex()==n) currently previewed, and whether the window is displaying the rest of the gear.
    /// </summary>
    /// <returns> True if the agent was readable; false if it is unavailable (caller should fall back). </returns>
    public bool TryGetPreviewState(out ushort previewMask, out bool displayGear)
    {
        previewMask = 0;
        displayGear = false;
        var agent = AgentTryon.Instance();
        if (agent == null || !agent->AgentInterface.IsAgentActive())
            return false;

        displayGear = agent->DisplayGear;
        for (var i = 0; i < 14; ++i)
        {
            var item = agent->TryOnItems[i];
            if (item.Id == 0 && !item.IsDyePreviewEnabled)
                continue;

            var slot = ((EquipSlot)item.EquipSlotCategory).ToSlot();
            var idx  = (int)slot.ToIndex();
            if (idx is >= 0 and < 10)
                previewMask |= (ushort)(1 << idx);
        }

        return true;
    }
}
