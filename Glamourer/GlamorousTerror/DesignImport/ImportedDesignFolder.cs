using Glamourer.Config;

namespace Glamourer.GlamorousTerror.DesignImport;

/// <summary>
/// Puts newly imported designs into a configurable folder by prefixing the name passed to the design manager.
/// The design file system creates the folder on demand and reuses it if it already exists.
/// </summary>
public static class ImportedDesignFolder
{
    /// <summary> Prepend the configured import folder to <paramref name="name"/> unless the name already contains a path. </summary>
    public static string WithImportFolder(this Configuration config, string name)
    {
        if (!config.AutoFolderImportedDesigns || name.Contains('/'))
            return name;

        var folder = config.ImportedDesignsFolder.Trim().Trim('/');
        return folder.Length is 0 ? name : $"{folder}/{name}";
    }
}
