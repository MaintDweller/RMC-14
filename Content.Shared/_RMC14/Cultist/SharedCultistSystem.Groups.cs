using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared.Access.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistSystem
{
    private static readonly EntProtoId GroupPrototype = "RMCCultistDevotion";

    private void InitializeGroups()
    {
        SubscribeLocalEvent<CultistComponent, GetAccessTagsEvent>(OnGetAccessTags);
        SubscribeLocalEvent<HiveComponent, EntityTerminatingEvent>(OnSupportedHiveTerminating);
    }

    private void OnGetAccessTags(Entity<CultistComponent> ent, ref GetAccessTagsEvent args)
    {
        args.Tags.Add(ent.Comp.DoorAccess);
    }

    private void OnSupportedHiveTerminating(Entity<HiveComponent> ent, ref EntityTerminatingEvent args)
    {
        if (_net.IsServer && GetGroupForHive(ent) is { } group)
            QueueDel(group);
    }

    public Entity<CultistDevotionComponent>? GetGroup(Entity<CultistComponent?> member)
    {
        if (!Resolve(member, ref member.Comp, false) || member.Comp.Group is not { } uid ||
            TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) ||
            !TryComp<CultistDevotionComponent>(uid, out var group))
            return null;

        return (uid, group);
    }

    public Entity<CultistDevotionComponent>? GetGroupForHive(EntityUid? hive)
    {
        if (hive == null)
            return null;

        var query = EntityQueryEnumerator<CultistDevotionComponent>();
        while (query.MoveNext(out var uid, out var group))
        {
            if (group.Hive == hive && !TerminatingOrDeleted(uid))
                return (uid, group);
        }

        return null;
    }

    public Entity<HiveComponent>? GetSupportedHive(Entity<CultistComponent?> member)
    {
        if (GetGroup(member) is not { } group || group.Comp.Hive is not { } uid ||
            TerminatingOrDeleted(uid) || !TryComp<HiveComponent>(uid, out var hive))
            return null;

        return (uid, hive);
    }

    private Entity<CultistDevotionComponent> GetOrCreateGroup(EntityUid hive)
    {
        if (GetGroupForHive(hive) is { } existing)
            return existing;

        var uid = Spawn(GroupPrototype, MapCoordinates.Nullspace);
        var group = Comp<CultistDevotionComponent>(uid);
        group.Hive = hive;
        Dirty(uid, group);
        return (uid, group);
    }
}
