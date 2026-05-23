using Dalamud.Game.ClientState.Objects.Enums;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Penumbra.String;

namespace Glamourer.GlamorousTerror.WildcardAutomation;

internal static class WildcardIdentifier
{
    public static bool IsWildcard(ByteString name)
        => !name.IsEmpty && name.IndexOf((byte)'*') >= 0;

    public static bool IsWildcard(string? name)
        => !string.IsNullOrEmpty(name) && name.Contains('*');

    public static ActorIdentifier PlayerOrFallback(ActorManager actors, ByteString name, WorldId world)
        => IsWildcard(name)
            ? actors.CreateIndividualUnchecked(IdentifierType.Player, name, world.Id, ObjectKind.Pc, 0)
            : actors.CreatePlayer(name, world);

    public static ActorIdentifier RetainerOrFallback(ActorManager actors, ByteString name, ActorIdentifier.RetainerType type)
        => IsWildcard(name)
            ? actors.CreateIndividualUnchecked(IdentifierType.Retainer, name, (ushort)type, ObjectKind.Retainer, 0)
            : actors.CreateRetainer(name, type);

    public static ActorIdentifier OwnedOrFallback(ActorManager actors, ByteString name, WorldId world, ObjectKind kind, NpcId data)
        => IsWildcard(name)
            ? actors.CreateIndividualUnchecked(IdentifierType.Owned, name, world.Id, kind, data)
            : actors.CreateOwned(name, world, kind, data);
}
