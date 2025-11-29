using Dalamud.Interface;
using Glamourer.Services;
using Glamourer.State;
using Dalamud.Bindings.ImGui;
using OtterGui.Raii;
using OtterGui.Services;
using OtterGui.Text;
using Penumbra.GameData.Interop;

namespace Glamourer.Gui.Tabs.SettingsTab;

public class CodeDrawer(Configuration config, CodeService codeService, FunModule funModule, StateManager stateManager, ActorObjectManager actors) : IUiService
{
    private static ReadOnlySpan<byte> Tooltip
        => "Fun Modes allow for some easter-egg features that usually manipulate the appearance of all players you see (including yourself) in some way."u8;

    public void Draw()
    {
        var show = ImGui.CollapsingHeader("Fun Modes");
        DrawTooltip();

        if (!show)
            return;

        DrawCopyButtons();
        DrawFeatureToggles();
    }

    private void DrawCopyButtons()
    {
        var buttonSize = new Vector2(250 * ImUtf8.GlobalScale, 0);
        if (ImUtf8.Button("Who am I?!?"u8, buttonSize))
            funModule.WhoAmI();
        ImUtf8.HoverTooltip(
            "Copy your characters actual current appearance including fun modes or holiday events to the clipboard as a design."u8);

        ImGui.SameLine();

        if (ImUtf8.Button("Who is that!?!"u8, buttonSize))
            funModule.WhoIsThat();
        ImUtf8.HoverTooltip(
            "Copy your targets actual current appearance including fun modes or holiday events to the clipboard as a design."u8);

        ImGui.Dummy(Vector2.Zero);
        ImGui.Separator();
        ImGui.Dummy(Vector2.Zero);
    }

    private void DrawFeatureToggles()
    {
        ImUtf8.Text("Enable Fun Modes:"u8);
        ImGui.Dummy(Vector2.Zero);

        // Get all code flags except the debug ones
        var flags = Enum.GetValues<CodeService.CodeFlag>()
            .Where(f => f != 0 && f != CodeService.CodeFlag.Face && f != CodeService.CodeFlag.Manderville && f != CodeService.CodeFlag.Smiles)
            .ToArray();

        foreach (var flag in flags)
        {
            using var id      = ImUtf8.PushId((int)flag);
            var       enabled = codeService.Enabled(flag);
            var       name    = CodeService.GetName(flag);
            var       desc    = CodeService.GetDescription(flag);

            if (ImUtf8.Checkbox(""u8, ref enabled))
            {
                codeService.Toggle(flag, enabled);
                ForceRedrawAll();
            }

            ImGui.SameLine();
            ImUtf8.Text(name);
            ImUtf8.HoverTooltip(desc);
        }
    }

    private static void DrawTooltip()
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.SetNextWindowSize(new Vector2(400, 0));
        using var tt = ImUtf8.Tooltip();
        ImUtf8.TextWrapped(Tooltip);
    }

    private void ForceRedrawAll()
    {
        foreach (var actor in actors.Objects.Where(a => a.Valid))
            stateManager.ReapplyState(actor, true, StateSource.Manual);
    }
}
