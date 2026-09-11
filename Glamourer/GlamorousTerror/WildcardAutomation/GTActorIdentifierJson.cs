using System.Text.Json;
using Dalamud.Game.ClientState.Objects.Enums;
using Luna;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Penumbra.String;

namespace Glamourer.GlamorousTerror.WildcardAutomation;

internal static class GTActorIdentifierJson
{
    public static ActorIdentifier FromJson(ActorManager actors, in JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } j)
            return ActorIdentifier.Invalid;

        var rawName = j.TryReadProperty("PlayerName"u8, out string? n) ? n : null;
        if (!WildcardIdentifier.IsWildcard(rawName))
            return actors.FromJson(j);

        var type = j.EnumOrDefault("Type"u8, IdentifierType.Invalid);
        var name = ByteString.FromStringUnsafe(rawName, false);

        switch (type)
        {
            case IdentifierType.Player:
            {
                var homeWorld = j.PropertyOrDefault("HomeWorld"u8, (ushort)0);
                return WildcardIdentifier.PlayerOrFallback(actors, name, (WorldId)homeWorld);
            }
            case IdentifierType.Retainer:
            {
                var retainerType = j.EnumOrDefault("Retainer"u8, ActorIdentifier.RetainerType.Both);
                return WildcardIdentifier.RetainerOrFallback(actors, name, retainerType);
            }
            case IdentifierType.Owned:
            {
                var homeWorld = j.PropertyOrDefault("HomeWorld"u8, (ushort)0);
                var kind      = j.EnumOrDefault("Kind"u8, ObjectKind.None);
                var dataId    = j.PropertyOrDefault("DataId"u8, 0u);
                return WildcardIdentifier.OwnedOrFallback(actors, name, (WorldId)homeWorld, kind, (NpcId)dataId);
            }
            default:
                return actors.FromJson(j);
        }
    }
}
