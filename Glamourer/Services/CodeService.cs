using Penumbra.GameData.Enums;

namespace Glamourer.Services;

public class CodeService
{
    private readonly Configuration _config;

    [Flags]
    public enum CodeFlag : ulong
    {
        Clown        = 0x000001,
        Emperor      = 0x000002,
        Individual   = 0x000004,
        Dwarf        = 0x000008,
        Giant        = 0x000010,
        OopsHyur     = 0x000020,
        OopsElezen   = 0x000040,
        OopsLalafell = 0x000080,
        OopsMiqote   = 0x000100,
        OopsRoegadyn = 0x000200,
        OopsAuRa     = 0x000400,
        OopsHrothgar = 0x000800,
        OopsViera    = 0x001000,
        //Artisan      = 0x002000,
        SixtyThree   = 0x004000,
        Shirts       = 0x008000,
        World        = 0x010000,
        Elephants    = 0x020000,
        Crown        = 0x040000,
        Dolphins     = 0x080000,
        Face         = 0x100000,
        Manderville  = 0x200000,
        Smiles       = 0x400000,
    }

    public const CodeFlag DyeCodes =
        CodeFlag.Clown | CodeFlag.World | CodeFlag.Elephants | CodeFlag.Dolphins;

    public const CodeFlag GearCodes =
        CodeFlag.Emperor | CodeFlag.World | CodeFlag.Elephants | CodeFlag.Dolphins;

    public const CodeFlag RaceCodes = CodeFlag.OopsHyur
      | CodeFlag.OopsElezen
      | CodeFlag.OopsLalafell
      | CodeFlag.OopsMiqote
      | CodeFlag.OopsRoegadyn
      | CodeFlag.OopsAuRa
      | CodeFlag.OopsHrothgar
      | CodeFlag.OopsViera;

    public const CodeFlag FullCodes = CodeFlag.Face | CodeFlag.Manderville | CodeFlag.Smiles;

    public const CodeFlag SizeCodes = CodeFlag.Dwarf | CodeFlag.Giant;

    public CodeFlag AllEnabled
        => _config.EnabledCheats;

    public bool Enabled(CodeFlag flag)
        => _config.EnabledCheats.HasFlag(flag);

    public bool AnyEnabled(CodeFlag flag)
        => (_config.EnabledCheats & flag) != 0;

    public CodeFlag Masked(CodeFlag mask)
        => _config.EnabledCheats & mask;

    public Race GetRace()
        => (_config.EnabledCheats & RaceCodes) switch
        {
            CodeFlag.OopsHyur     => Race.Hyur,
            CodeFlag.OopsElezen   => Race.Elezen,
            CodeFlag.OopsLalafell => Race.Lalafell,
            CodeFlag.OopsMiqote   => Race.Miqote,
            CodeFlag.OopsRoegadyn => Race.Roegadyn,
            CodeFlag.OopsAuRa     => Race.AuRa,
            CodeFlag.OopsHrothgar => Race.Hrothgar,
            CodeFlag.OopsViera    => Race.Viera,
            _                     => Race.Unknown,
        };

    public CodeService(Configuration config)
    {
        _config = config;
    }

    public void Toggle(CodeFlag flag, bool enable)
    {
        if (enable)
        {
            var badFlags = ~GetMutuallyExclusive(flag);
            _config.EnabledCheats = (_config.EnabledCheats | flag) & badFlags;
        }
        else
        {
            _config.EnabledCheats &= ~flag;
        }
        _config.Save();
    }

    // @formatter:off
    private static CodeFlag GetMutuallyExclusive(CodeFlag flag)
        => flag switch
        {
            CodeFlag.Clown        => (FullCodes | DyeCodes) & ~CodeFlag.Clown,
            CodeFlag.Emperor      => (FullCodes | GearCodes) & ~CodeFlag.Emperor,
            CodeFlag.Individual   => FullCodes,
            CodeFlag.Dwarf        => (FullCodes | SizeCodes) & ~CodeFlag.Dwarf,
            CodeFlag.Giant        => (FullCodes | SizeCodes) & ~CodeFlag.Giant,
            CodeFlag.OopsHyur     => (FullCodes | RaceCodes) & ~CodeFlag.OopsHyur,
            CodeFlag.OopsElezen   => (FullCodes | RaceCodes) & ~CodeFlag.OopsElezen,
            CodeFlag.OopsLalafell => (FullCodes | RaceCodes) & ~CodeFlag.OopsLalafell,
            CodeFlag.OopsMiqote   => (FullCodes | RaceCodes) & ~CodeFlag.OopsMiqote,
            CodeFlag.OopsRoegadyn => (FullCodes | RaceCodes) & ~CodeFlag.OopsRoegadyn,
            CodeFlag.OopsAuRa     => (FullCodes | RaceCodes) & ~CodeFlag.OopsAuRa,
            CodeFlag.OopsHrothgar => (FullCodes | RaceCodes) & ~CodeFlag.OopsHrothgar,
            CodeFlag.OopsViera    => (FullCodes | RaceCodes) & ~CodeFlag.OopsViera,
            CodeFlag.SixtyThree   => FullCodes,
            CodeFlag.Shirts       => 0,
            CodeFlag.World        => (FullCodes | DyeCodes | GearCodes) & ~CodeFlag.World,
            CodeFlag.Elephants    => (FullCodes | DyeCodes | GearCodes) & ~CodeFlag.Elephants,
            CodeFlag.Crown        => FullCodes,
            CodeFlag.Dolphins     => (FullCodes | DyeCodes | GearCodes) & ~CodeFlag.Dolphins,
            CodeFlag.Face         => (FullCodes | RaceCodes | SizeCodes | GearCodes | DyeCodes | CodeFlag.Crown | CodeFlag.SixtyThree) & ~CodeFlag.Face,
            CodeFlag.Manderville  => (FullCodes | RaceCodes | SizeCodes | GearCodes | DyeCodes | CodeFlag.Crown | CodeFlag.SixtyThree) & ~CodeFlag.Manderville,
            CodeFlag.Smiles       => (FullCodes | RaceCodes | SizeCodes | GearCodes | DyeCodes | CodeFlag.Crown | CodeFlag.SixtyThree) & ~CodeFlag.Smiles,
            _                     => 0,
        };

    public static string GetDescription(CodeFlag flag)
        => flag switch
        {
            CodeFlag.Clown        => "Randomizes dyes for every player.",
            CodeFlag.Emperor      => "Randomizes clothing for every player.",
            CodeFlag.Individual   => "Randomizes customizations for every player.",
            CodeFlag.Dwarf        => "Sets the player character to minimum height and all other players to maximum height.",
            CodeFlag.Giant        => "Sets the player character to maximum height and all other players to minimum height.",
            CodeFlag.OopsHyur     => "Turns all players to Hyur.",
            CodeFlag.OopsElezen   => "Turns all players to Elezen.",
            CodeFlag.OopsLalafell => "Turns all players to Lalafell.",
            CodeFlag.OopsMiqote   => "Turns all players to Miqo'te.",
            CodeFlag.OopsRoegadyn => "Turns all players to Roegadyn.",
            CodeFlag.OopsAuRa     => "Turns all players to Au Ra.",
            CodeFlag.OopsHrothgar => "Turns all players to Hrothgar.",
            CodeFlag.OopsViera    => "Turns all players to Viera.",
            CodeFlag.SixtyThree   => "Inverts the gender of every player.",
            CodeFlag.Shirts       => "Highlights all items in the Unlocks tab as if they were unlocked.",
            CodeFlag.World        => "Sets every player except the player character themselves to job-appropriate gear.",
            CodeFlag.Elephants    => "Sets every player to the elephant costume in varying shades of pink.",
            CodeFlag.Crown        => "Sets every player with a mentor symbol enabled to the clown's hat.",
            CodeFlag.Dolphins     => "Sets every player to a Namazu hat with different costume bodies.",
            CodeFlag.Face         => "Enable a debugging mode for the UI. Not really useful.",
            CodeFlag.Manderville  => "Enable a debugging mode for the UI. Not really useful.",
            CodeFlag.Smiles       => "Enable a debugging mode for the UI. Not really useful.",
            _                     => string.Empty,
        };

    public static string GetName(CodeFlag flag)
        => flag switch
        {
            CodeFlag.Clown        => "Random Dyes",
            CodeFlag.Emperor      => "Random Clothing",
            CodeFlag.Individual   => "Random Customizations",
            CodeFlag.Dwarf        => "Player Dwarf Mode",
            CodeFlag.Giant        => "Player Giant Mode",
            CodeFlag.OopsHyur     => "All Hyur",
            CodeFlag.OopsElezen   => "All Elezen",
            CodeFlag.OopsLalafell => "All Lalafell",
            CodeFlag.OopsMiqote   => "All Miqo'te",
            CodeFlag.OopsRoegadyn => "All Roegadyn",
            CodeFlag.OopsAuRa     => "All Au Ra",
            CodeFlag.OopsHrothgar => "All Hrothgar",
            CodeFlag.OopsViera    => "All Viera",
            CodeFlag.SixtyThree   => "Invert Genders",
            CodeFlag.Shirts       => "Show All Items Unlocked",
            CodeFlag.World        => "Job-Appropriate Gear",
            CodeFlag.Elephants    => "Everyone Elephants",
            CodeFlag.Crown        => "Clown Mentors",
            CodeFlag.Dolphins     => "Everyone Namazu",
            CodeFlag.Face         => "Debug Mode (Face)",
            CodeFlag.Manderville  => "Debug Mode (Manderville)",
            CodeFlag.Smiles       => "Debug Mode (Smiles)",
            _                     => "Unknown",
        };
}

