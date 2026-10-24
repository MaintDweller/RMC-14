using Content.Shared._RMC14.Cultist;

// ReSharper disable CheckNamespace
namespace Content.Shared.NPC.Systems;

public sealed partial class NpcFactionSystem
{
    [Dependency] private readonly CultistInteractionSystem _cultist = default!;
}
