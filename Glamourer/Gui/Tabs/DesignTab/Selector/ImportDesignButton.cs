using Dalamud.Interface.ImGuiNotification;
using Glamourer.Config;
using Glamourer.Designs;
using Glamourer.GlamorousTerror.DesignImport;
using ImSharp;
using Luna;

namespace Glamourer.Gui.Tabs.DesignTab;

public sealed class ImportDesignButton(DesignConverter converter, DesignManager manager, Configuration config) : BaseIconButton<AwesomeIcon>
{
    private string _clipboardText = string.Empty;

    public override AwesomeIcon Icon
        => LunaStyle.ImportIcon;

    public override bool HasTooltip
        => true;

    public override void DrawTooltip()
        => Im.Text("Try to import a design from your clipboard."u8);

    public override void OnClick()
    {
        try
        {
            _clipboardText = Im.Clipboard.GetUtf16();
            Im.Popup.Open("##ImportDesign"u8);
        }
        catch (Exception)
        {
            Glamourer.Messager.NotificationMessage("Could not import data from clipboard.", NotificationType.Error, false);
        }
    }

    protected override void PostDraw()
    {
        if (!InputPopup.OpenName("##ImportDesign"u8, out var newName))
            return;

        if (_clipboardText.Length is 0)
            return;

        var design = converter.FromBase64(_clipboardText, true, true, out _);
        var path   = config.WithImportFolder(newName);
        if (design is Design d)
            manager.CreateClone(d, path, true);
        else if (design is not null)
            manager.CreateClone(design, path, true);
        else
            Glamourer.Messager.NotificationMessage("Could not create a design, clipboard did not contain valid design data.",
                NotificationType.Error, false);
        _clipboardText = string.Empty;
    }
}
