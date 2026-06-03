using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Glamourer.Config;
using Glamourer.State;
using Luna;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace Glamourer.Services;

/// <summary>
/// Mirrors the player's glamour onto the in-game dye-preview character (object index 443). That actor
/// is rendered by a self-contained <see cref="ColorantCharaView"/> owned by <see cref="AgentColorant"/>
/// which bypasses the draw-object/equipment hooks the rest of the UI-actor mirror relies on, so the
/// colorant's own <see cref="CharaView"/> populate functions are hooked directly here. Both functions
/// are shared with every other CharaView, so each detour is gated on <see cref="IsColorant"/> and only
/// ever forwards through to the original.
/// </summary>
public sealed unsafe class ColorantPreviewService : IDisposable, IRequiredService
{
    private readonly Configuration _config;
    private readonly StateManager  _state;
    private readonly ActorManager  _actors;

    private delegate void SetItemSlotDataDelegate(CharaView* self, byte slotId, uint itemId, byte stain0Id, byte stain1Id,
        uint glamourItemId, bool applyCompanyCrest);

    private delegate void SetModelDataDelegate(CharaView* self, CharaViewModelData* data);

    private readonly Hook<SetItemSlotDataDelegate> _setItemSlotDataHook;
    private readonly Hook<SetModelDataDelegate>    _setModelDataHook;

    public ColorantPreviewService(IGameInteropProvider interop, Configuration config, StateManager state, ActorManager actors)
    {
        _config = config;
        _state  = state;
        _actors = actors;

        _setItemSlotDataHook =
            interop.HookFromAddress<SetItemSlotDataDelegate>((nint)CharaView.MemberFunctionPointers.SetItemSlotData, SetItemSlotDataDetour);
        _setModelDataHook =
            interop.HookFromAddress<SetModelDataDelegate>((nint)CharaView.MemberFunctionPointers.SetModelData, SetModelDataDetour);

        _setItemSlotDataHook.Enable();
        _setModelDataHook.Enable();
    }

    public void Dispose()
    {
        _setItemSlotDataHook.Dispose();
        _setModelDataHook.Dispose();
    }

    private bool TryGetPlayerState(out ActorState state)
        => _state.TryGetValue(_actors.GetCurrentPlayer(), out state!);

    private static bool IsColorant(CharaView* self)
    {
        if (self == null)
            return false;

        var agent = AgentColorant.Instance();
        return agent != null && self == (CharaView*)&agent->CharaView;
    }

    private void SetModelDataDetour(CharaView* self, CharaViewModelData* data)
    {
        try
        {
            if (_config.MirrorUiActors
             && _config.MirrorDyePreview
             && _config.MirrorDyePreviewCustomize
             && data != null
             && IsColorant(self)
             && TryGetPlayerState(out var playerState))
                *(CustomizeArray*)&data->CustomizeData = playerState.ModelData.Customize;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"[ColorantPreview] SetModelData mirror failed: {ex}");
        }

        _setModelDataHook.Original(self, data);
    }

    private void SetItemSlotDataDetour(CharaView* self, byte slotId, uint itemId, byte stain0Id, byte stain1Id, uint glamourItemId,
        bool applyCompanyCrest)
    {
        try
        {
            if (!_config.MirrorUiActors
             || !_config.MirrorDyePreview
             || !_config.MirrorDyePreviewGear
             || !IsColorant(self)
             || !TryGetPlayerState(out var playerState))
            {
                _setItemSlotDataHook.Original(self, slotId, itemId, stain0Id, stain1Id, glamourItemId, applyCompanyCrest);
                return;
            }

            var agent = AgentColorant.Instance();
            if (agent == null)
            {
                _setItemSlotDataHook.Original(self, slotId, itemId, stain0Id, stain1Id, glamourItemId, applyCompanyCrest);
                return;
            }

            ref var cv = ref agent->CharaView;

            // Window is showing only the dyed piece: leave the game's data alone entirely.
            if (cv.HideOtherEquipment)
            {
                _setItemSlotDataHook.Original(self, slotId, itemId, stain0Id, stain1Id, glamourItemId, applyCompanyCrest);
                return;
            }

            // The actively-dyed slot carries the picker's chosen stain; pass it through untouched so the
            // real item and its preview dye stay visible.
            if (cv.SelectedStain != 0 && stain0Id == cv.SelectedStain)
            {
                _setItemSlotDataHook.Original(self, slotId, itemId, stain0Id, stain1Id, glamourItemId, applyCompanyCrest);
                return;
            }

            var slot = ((uint)slotId).ToEquipSlot().ToSlot();
            if (slot is EquipSlot.Unknown)
            {
                _setItemSlotDataHook.Original(self, slotId, itemId, stain0Id, stain1Id, glamourItemId, applyCompanyCrest);
                return;
            }

            var stains = playerState.ModelData.ArmorWithState(slot).Stains;
            _setItemSlotDataHook.Original(self, slotId, playerState.ModelData.Item(slot).ItemId.Id, stains.Stain1.Id, stains.Stain2.Id,
                glamourItemId, applyCompanyCrest);
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"[ColorantPreview] SetItemSlotData mirror failed: {ex}");
            _setItemSlotDataHook.Original(self, slotId, itemId, stain0Id, stain1Id, glamourItemId, applyCompanyCrest);
        }
    }
}
