using Dalamud.Game.ClientState.Objects.Enums;
using Newtonsoft.Json.Linq;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Penumbra.String;

namespace Glamourer.GlamorousTerror.WildcardAutomation;

internal static class GTActorIdentifierJson
{
    public static ActorIdentifier FromJson(ActorManager actors, JObject? data)
    {
        if (data is null)
            return ActorIdentifier.Invalid;

        var rawName = data[nameof(ActorIdentifier.PlayerName)]?.ToObject<string>();
        if (!WildcardIdentifier.IsWildcard(rawName))
            return actors.FromJson(data);

        var type = data[nameof(ActorIdentifier.Type)]?.ToObject<IdentifierType>() ?? IdentifierType.Invalid;
        var name = ByteString.FromStringUnsafe(rawName, false);

        switch (type)
        {
            case IdentifierType.Player:
            {
                var homeWorld = data[nameof(ActorIdentifier.HomeWorld)]?.ToObject<ushort>() ?? 0;
                return WildcardIdentifier.PlayerOrFallback(actors, name, (WorldId)homeWorld);
            }
            case IdentifierType.Retainer:
            {
                var retainerType = data[nameof(ActorIdentifier.Retainer)]?.ToObject<ActorIdentifier.RetainerType>()
                 ?? ActorIdentifier.RetainerType.Both;
                return WildcardIdentifier.RetainerOrFallback(actors, name, retainerType);
            }
            case IdentifierType.Owned:
            {
                var homeWorld = data[nameof(ActorIdentifier.HomeWorld)]?.ToObject<ushort>() ?? 0;
                var kind      = data[nameof(ActorIdentifier.Kind)]?.ToObject<ObjectKind>() ?? ObjectKind.None;
                var dataId    = data[nameof(ActorIdentifier.DataId)]?.ToObject<uint>() ?? 0;
                return WildcardIdentifier.OwnedOrFallback(actors, name, (WorldId)homeWorld, kind, (NpcId)dataId);
            }
            default:
                return actors.FromJson(data);
        }
    }
}
