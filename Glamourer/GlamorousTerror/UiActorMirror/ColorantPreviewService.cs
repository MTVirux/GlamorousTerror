using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Glamourer.Config;
using Glamourer.State;
using Luna;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Structs;

namespace Glamourer.Services;

// The dye-preview character (index 443) is drawn by AgentColorant's own CharaView, which bypasses the
// draw-object hooks the rest of the UI-actor mirror uses, so its model setup is hooked here. Customize
// only: the window shows the item set being dyed, so its gear is left alone.
public sealed unsafe class ColorantPreviewService : IDisposable, IRequiredService
{
    private readonly Configuration _config;
    private readonly StateManager  _state;
    private readonly ActorManager  _actors;

    private delegate void SetModelDataDelegate(CharaView* self, CharaViewModelData* data);

    private readonly Hook<SetModelDataDelegate> _setModelDataHook;

    public ColorantPreviewService(IGameInteropProvider interop, Configuration config, StateManager state, ActorManager actors)
    {
        _config = config;
        _state  = state;
        _actors = actors;

        _setModelDataHook =
            interop.HookFromAddress<SetModelDataDelegate>((nint)CharaView.MemberFunctionPointers.SetModelData, SetModelDataDetour);
        _setModelDataHook.Enable();
    }

    public void Dispose()
        => _setModelDataHook.Dispose();

    private static bool IsColorant(CharaView* self)
    {
        if (self == null)
            return false;

        var agent = AgentColorant.Instance();
        return agent != null && agent->AgentInterface.IsAgentActive() && self == (CharaView*)&agent->CharaView;
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
             && _state.TryGetValue(_actors.GetCurrentPlayer(), out var playerState))
                *(CustomizeArray*)&data->CustomizeData = playerState.ModelData.Customize;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"[ColorantPreview] customize mirror failed: {ex}");
        }

        _setModelDataHook.Original(self, data);
    }
}
