using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Luna;
using Penumbra.GameData.Enums;

namespace Glamourer.Services;

/// <summary>
/// Reads the try-on / dye-preview agent to determine which armor slots the game is actively
/// previewing, so glamour mirroring can preserve those slots.
/// </summary>
public sealed unsafe class UiActorPreviewSlots : IService
{
    /// <summary>
    /// Computes a bitmask of armor-buffer indices (bit n = slot whose ToIndex()==n) currently
    /// previewed by the try-on/dye agent.
    /// </summary>
    /// <returns> True if the agent was readable (mask is valid, possibly 0); false if the agent
    /// is unavailable — caller should treat this as "cannot detect" and fall back. </returns>
    public bool TryGetPreviewedSlotMask(out ushort mask)
    {
        mask = 0;
        var agent = AgentTryon.Instance();
        if (agent == null || !agent->AgentInterface.IsAgentActive())
            return false;

        for (var i = 0; i < 14; ++i)
        {
            var item = agent->TryOnItems[i];
            if (item.Id == 0 && !item.IsDyePreviewEnabled)
                continue;

            var slot = ((EquipSlot)item.EquipSlotCategory).ToSlot();
            var idx  = (int)slot.ToIndex();
            if (idx is >= 0 and < 10)
                mask |= (ushort)(1 << idx);
        }

        return true;
    }
}
