using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Penumbra.String;

namespace Glamourer.Automation;

public sealed partial class AutoDesignApplier
{
    private bool TryGettingSetExactOrWildcard(ActorIdentifier identifier, [NotNullWhen(true)] out AutoDesignSet? set)
    {
        foreach (var (key, value) in _manager.EnabledSets)
        {
            if (!key.PlayerName.ToString().Contains('*'))
                continue;

            if (key.Type != identifier.Type
                && key.Type is not IdentifierType.Player
                && identifier.Type is not IdentifierType.Player)
                continue;

            if (key.HomeWorld != WorldId.AnyWorld && key.HomeWorld != identifier.HomeWorld)
                continue;

            if (MatchesWildcard(identifier.PlayerName, key.PlayerName))
            {
                set = value;
                return true;
            }
        }

        set = null;
        return false;
    }

    private static unsafe bool MatchesWildcard(ByteString name, ByteString pattern)
    {
        fixed (byte* n = name.Span)
        fixed (byte* p = pattern.Span)
            return MatchesWildcardInternal(n, name.Length, p, pattern.Length);
    }

    private static unsafe bool MatchesWildcardInternal(byte* name, int nameLen, byte* pattern, int patternLen)
    {
        var nameIdx    = 0;
        var patternIdx = 0;
        var starIdx    = -1;
        var matchIdx   = 0;

        while (nameIdx < nameLen)
        {
            if (patternIdx < patternLen && pattern[patternIdx] == (byte)'*')
            {
                starIdx  = patternIdx++;
                matchIdx = nameIdx;
            }
            else if (patternIdx < patternLen &&
                     (pattern[patternIdx] == (byte)'?' || AsciiToLower(name[nameIdx]) == AsciiToLower(pattern[patternIdx])))
            {
                nameIdx++;
                patternIdx++;
            }
            else if (starIdx != -1)
            {
                patternIdx = starIdx + 1;
                nameIdx    = ++matchIdx;
            }
            else
            {
                return false;
            }
        }

        while (patternIdx < patternLen && pattern[patternIdx] == (byte)'*')
            patternIdx++;

        return patternIdx == patternLen;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte AsciiToLower(byte b)
        => b >= (byte)'A' && b <= (byte)'Z' ? (byte)(b + 32) : b;
}
