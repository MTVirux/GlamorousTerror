using Glamourer.Config;
using Glamourer.Services;
using Glamourer.State;
using ImSharp;
using Luna;
using Penumbra.GameData.Interop;

namespace Glamourer.Gui.Tabs.SettingsTab;

public class CodeDrawer(CodeService codeService, FunModule funModule, StateManager stateManager, ActorObjectManager actorObjectManager) : IUiService
{
    public void Draw()
    {
        var show = Im.Tree.Header("Fun Modes"u8);
        if (!show)
            return;

        DrawFeatureToggles();
        DrawCopyButtons();

        if (Im.Button("Disable All"u8))
        {
            codeService.DisableAll();
            ForceRedrawAll();
        }
    }

    private void DrawFeatureToggles()
    {
        foreach (var flag in CodeService.CodeFlag.Values)
        {
            // Skip debug modes from the UI.
            if (flag is CodeService.CodeFlag.Face or CodeService.CodeFlag.Manderville or CodeService.CodeFlag.Smiles)
                continue;

            var enabled = codeService.Enabled(flag);
            if (Im.Checkbox(CodeService.GetName(flag), ref enabled))
            {
                codeService.Toggle(flag, enabled);
                ForceRedrawAll();
            }

            if (Im.Item.Hovered())
            {
                using var tt = Im.Tooltip.Begin();
                Im.Text(CodeService.GetDescription(flag));
            }
        }
    }

    private void DrawCopyButtons()
    {
        var buttonSize = ImEx.ScaledVectorX(250);
        if (Im.Button("Who am I?!?"u8, buttonSize))
            funModule.WhoAmI();
        Im.Tooltip.OnHover("Copy your characters actual current appearance including fun modes or holiday events to the clipboard as a design."u8);

        Im.Line.Same();

        if (Im.Button("Who is that!?!"u8, buttonSize))
            funModule.WhoIsThat();
        Im.Tooltip.OnHover("Copy your targets actual current appearance including fun modes or holiday events to the clipboard as a design."u8);
    }

    private void ForceRedrawAll()
    {
        foreach (var (identifier, data) in actorObjectManager)
        {
            if (!stateManager.TryGetValue(identifier, out var state))
                continue;

            foreach (var actor in data.Objects)
            {
                if (!actor.Valid)
                    continue;

                stateManager.ReapplyState(actor, true, StateSource.Game);
            }
        }
    }
}
