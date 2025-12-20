using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using OtterGui.Classes;
using OtterGui.Services;
using Penumbra.GameData.Structs;

namespace Glamourer.Services;

/// <summary>
/// Service that provides equipment item names in the user's selected language.
/// Caches item names to avoid repeated lookups.
/// </summary>
public sealed class ItemNameService : IService
{
    private readonly Configuration _config;
    private readonly IDataManager _gameData;
    
    private ClientLanguage _cachedLanguage;
    private ExcelSheet<Item>? _itemSheet;
    private readonly Dictionary<uint, string> _nameCache = new();
    
    // All language sheets for multi-language search
    private readonly ExcelSheet<Item>?[] _allLanguageSheets;
    private readonly Dictionary<uint, string[]> _allLanguageNamesCache = new();
    private static readonly ClientLanguage[] AllLanguages = [ClientLanguage.English, ClientLanguage.Japanese, ClientLanguage.German, ClientLanguage.French];

    public ItemNameService(Configuration config, IDataManager gameData)
    {
        _config = config;
        _gameData = gameData;
        _cachedLanguage = GetTargetLanguage();
        RefreshSheet();
        
        // Initialize all language sheets for multi-language search
        _allLanguageSheets = new ExcelSheet<Item>?[AllLanguages.Length];
        for (var i = 0; i < AllLanguages.Length; i++)
        {
            _allLanguageSheets[i] = gameData.GetExcelSheet<Item>(AllLanguages[i]);
        }
    }

    /// <summary>
    /// Gets the target language based on configuration.
    /// </summary>
    private ClientLanguage GetTargetLanguage()
    {
        return _config.EquipmentNameLanguage switch
        {
            EquipmentNameLanguage.English => ClientLanguage.English,
            EquipmentNameLanguage.Japanese => ClientLanguage.Japanese,
            EquipmentNameLanguage.German => ClientLanguage.German,
            EquipmentNameLanguage.French => ClientLanguage.French,
            _ => _gameData.Language,
        };
    }

    /// <summary>
    /// Refreshes the cached item sheet when the language changes.
    /// </summary>
    private void RefreshSheet()
    {
        _itemSheet = _gameData.GetExcelSheet<Item>(_cachedLanguage);
        _nameCache.Clear();
    }

    /// <summary>
    /// Checks if the language has changed and refreshes the cache if needed.
    /// </summary>
    private void CheckLanguageChange()
    {
        var targetLanguage = GetTargetLanguage();
        if (targetLanguage != _cachedLanguage)
        {
            _cachedLanguage = targetLanguage;
            RefreshSheet();
        }
    }

    /// <summary>
    /// Gets the display name for an equipment item.
    /// If the item ID is valid, returns the name in the selected language.
    /// Otherwise, returns the original name from the EquipItem.
    /// </summary>
    /// <param name="item">The equipment item to get the name for.</param>
    /// <returns>The item name in the selected language.</returns>
    public string GetItemName(in EquipItem item)
    {
        // If using game default language and no override, just return the original name
        if (_config.EquipmentNameLanguage == EquipmentNameLanguage.GameDefault)
            return item.Name;
        
        CheckLanguageChange();

        var itemId = item.ItemId.Id;
        
        // Special items (Nothing, Smallclothes, etc.) don't have real item IDs
        if (itemId == 0 || itemId >= uint.MaxValue - 512)
            return item.Name;
        
        // Check cache first
        if (_nameCache.TryGetValue(itemId, out var cachedName))
            return cachedName;
        
        // Look up the item name in the selected language sheet
        if (_itemSheet != null && _itemSheet.TryGetRow(itemId, out var row))
        {
            var name = row.Name.ExtractText();
            if (!string.IsNullOrEmpty(name))
            {
                _nameCache[itemId] = name;
                return name;
            }
        }
        
        // Fall back to the original name if lookup fails
        return item.Name;
    }

    /// <summary>
    /// Gets the display name for an equipment item by its item ID.
    /// </summary>
    /// <param name="itemId">The item ID to look up.</param>
    /// <param name="fallbackName">The fallback name if the lookup fails.</param>
    /// <returns>The item name in the selected language.</returns>
    public string GetItemName(uint itemId, string fallbackName)
    {
        // If using game default language and no override, just return the fallback name
        if (_config.EquipmentNameLanguage == EquipmentNameLanguage.GameDefault)
            return fallbackName;
        
        CheckLanguageChange();

        // Special items (Nothing, Smallclothes, etc.) don't have real item IDs
        if (itemId == 0 || itemId >= uint.MaxValue - 512)
            return fallbackName;
        
        // Check cache first
        if (_nameCache.TryGetValue(itemId, out var cachedName))
            return cachedName;
        
        // Look up the item name in the selected language sheet
        if (_itemSheet != null && _itemSheet.TryGetRow(itemId, out var row))
        {
            var name = row.Name.ExtractText();
            if (!string.IsNullOrEmpty(name))
            {
                _nameCache[itemId] = name;
                return name;
            }
        }
        
        // Fall back to the original name if lookup fails
        return fallbackName;
    }

    /// <summary>
    /// Clears the name cache. Call this when the language setting changes.
    /// </summary>
    public void ClearCache()
    {
        _nameCache.Clear();
        _allLanguageNamesCache.Clear();
        RefreshSheet();
    }

    /// <summary>
    /// Checks if the given filter matches the item's name in any supported language.
    /// This allows searching for items using any language regardless of display settings.
    /// </summary>
    /// <param name="item">The equipment item to check.</param>
    /// <param name="filter">The filter string to match against.</param>
    /// <returns>True if the filter matches the item name in any language.</returns>
    public bool MatchesAnyLanguage(in EquipItem item, LowerString filter)
    {
        // If cross-language search is disabled, only match in the selected language
        if (!_config.CrossLanguageEquipmentSearch)
            return filter.IsContained(GetItemName(item));

        // First check the display name (most common case)
        if (filter.IsContained(GetItemName(item)))
            return true;

        var itemId = item.ItemId.Id;

        // Special items don't have translations
        if (itemId == 0 || itemId >= uint.MaxValue - 512)
            return false;

        // Get all language names for this item
        var allNames = GetAllLanguageNames(itemId);
        if (allNames == null)
            return false;

        // Check if filter matches any language
        foreach (var name in allNames)
        {
            if (!string.IsNullOrEmpty(name) && filter.IsContained(name))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the item names in all supported languages.
    /// </summary>
    private string[]? GetAllLanguageNames(uint itemId)
    {
        if (_allLanguageNamesCache.TryGetValue(itemId, out var cached))
            return cached;
        
        var names = new string[AllLanguages.Length];
        var anyFound = false;
        
        for (var i = 0; i < AllLanguages.Length; i++)
        {
            var sheet = _allLanguageSheets[i];
            if (sheet != null && sheet.TryGetRow(itemId, out var row))
            {
                names[i] = row.Name.ExtractText();
                if (!string.IsNullOrEmpty(names[i]))
                    anyFound = true;
            }
            else
            {
                names[i] = string.Empty;
            }
        }
        
        if (anyFound)
        {
            _allLanguageNamesCache[itemId] = names;
            return names;
        }
        
        return null;
    }

    /// <summary>
    /// Gets the currently configured language as a display string.
    /// </summary>
    public string GetCurrentLanguageDisplay()
    {
        return _config.EquipmentNameLanguage switch
        {
            EquipmentNameLanguage.GameDefault => $"Game Default ({_gameData.Language})",
            EquipmentNameLanguage.English => "English",
            EquipmentNameLanguage.Japanese => "日本語 (Japanese)",
            EquipmentNameLanguage.German => "Deutsch (German)",
            EquipmentNameLanguage.French => "Français (French)",
            _ => "Unknown",
        };
    }
}
