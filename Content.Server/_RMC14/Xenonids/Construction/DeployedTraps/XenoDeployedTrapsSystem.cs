using Content.Server.Destructible;
using Content.Shared._RMC14.Cultist;
using Content.Shared._RMC14.Xenonids.Construction.DeployedTraps;
using Content.Shared.Damage;

namespace Content.Server._RMC14.Xenonids.Construction.DeployedTraps;

public sealed class XenoDeployedTrapsSystem : EntitySystem
{
    [Dependency] private readonly CultistInteractionSystem _cultist = default!;
    [Dependency] private readonly DestructibleSystem _destructible = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDeployedTrapsComponent, DamageChangedEvent>(OnTrapDamaged);
    }

    private void OnTrapDamaged(Entity<XenoDeployedTrapsComponent> trap, ref DamageChangedEvent args)
    {
        if (args.Origin is { } origin && _cultist.IsHiveStructureProtected(origin, trap.Owner))
            return;

        if (args.DamageDelta == null)
            return;

        if (!_destructible.TryGetDestroyedAt(trap.Owner, out var totalHealth))
            return;

        if (args.Damageable.TotalDamage + args.DamageDelta.GetTotal() > totalHealth)
            QueueDel(trap);
    }
}
