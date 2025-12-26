using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Glamourer.Unlocks;
using Dalamud.Bindings.ImGui;
using OtterGui.Widgets;
using Penumbra.GameData.DataContainers;
using Penumbra.GameData.Structs;

namespace Glamourer.Gui.Equipment;

public sealed class GlamourerColorCombo(float _comboWidth, DictStain _stains, FavoriteManager _favorites)
    : FilterComboColors(_comboWidth, MouseWheelType.Control, CreateFunc(_stains, _favorites), Glamourer.Log)
{
    private bool     _popupWasOpen;
    private bool     _popupActiveThisFrame;
    private StainId? _hoveredStain;

    public bool PopupActive => _popupActiveThisFrame;

    public StainId? HoveredStain => _hoveredStain;

    public bool PopupJustClosed => _popupWasOpen && !_popupActiveThisFrame;

    public void EndFrame()
    {
        _popupWasOpen = _popupActiveThisFrame;
        _popupActiveThisFrame = false;
        _hoveredStain = null;
    }

    public override bool Draw(string label, uint color, string name, bool found, bool gloss, float previewWidth,
        MouseWheelType mouseWheel = MouseWheelType.Control)
    {
        _popupActiveThisFrame = false;
        _hoveredStain = null;

        return base.Draw(label, color, name, found, gloss, previewWidth, mouseWheel);
    }

    protected override void DrawList(float width, float itemHeight)
    {
        _popupActiveThisFrame = true;
        base.DrawList(width, itemHeight);
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        using (var _ = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(4, 0)))
        {
            if (globalIdx == 0)
            {
                using var font = ImRaii.PushFont(UiBuilder.IconFont);
                ImGui.Dummy(ImGui.CalcTextSize(FontAwesomeIcon.Star.ToIconString()));
            }
            else
            {
                UiHelpers.DrawFavoriteStar(_favorites, Items[globalIdx].Key);
            }

            ImGui.SameLine();
        }

        var       buttonWidth = ImGui.GetContentRegionAvail().X;
        var       totalWidth  = ImGui.GetContentRegionMax().X;
        using var style       = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(buttonWidth / 2 / totalWidth, 0.5f));

        var ret = base.DrawSelectable(globalIdx, selected);

        if (ImGui.IsItemHovered())
            _hoveredStain = new StainId(Items[globalIdx].Key);

        return ret;
    }

    private static Func<IReadOnlyList<KeyValuePair<byte, (string Name, uint Color, bool Gloss)>>> CreateFunc(DictStain stains,
        FavoriteManager favorites)
        => () => stains.Select(kvp => (kvp, favorites.Contains(kvp.Key))).OrderBy(p => !p.Item2).Select(p => p.kvp)
            .Prepend(new KeyValuePair<StainId, Stain>(Stain.None.RowIndex, Stain.None)).Select(kvp
                => new KeyValuePair<byte, (string, uint, bool)>(kvp.Key.Id, (kvp.Value.Name, kvp.Value.RgbaColor, kvp.Value.Gloss))).ToList();
}
