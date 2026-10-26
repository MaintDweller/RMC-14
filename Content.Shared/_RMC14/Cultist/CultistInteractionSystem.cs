using Content.Shared._RMC14.Marines;
using Content.Shared._RMC14.Xenonids;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared.Mobs.Systems;

namespace Content.Shared._RMC14.Cultist;

public sealed class CultistInteractionSystem : EntitySystem
{
    [Dependency] private readonly SharedCultistSystem _cultist = default!;
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public bool IsXenoAbilityTarget(EntityUid target)
    {
        return HasComp<MarineComponent>(target) || HasComp<XenoComponent>(target) || HasComp<CultistComponent>(target);
    }

    public bool IsNpcAlly(EntityUid entity, EntityUid other)
    {
        if (_cultist.GetGroup(entity) is { } group)
        {
            return _cultist.GetGroup(other)?.Owner == group.Owner ||
                   HasComp<XenoComponent>(other) && _hive.IsMember(other, group.Comp.Hive);
        }

        return HasComp<XenoComponent>(entity) && SupportsHive(other, _hive.GetHive(entity)?.Owner);
    }

    public bool SupportsHive(EntityUid cultist, EntityUid? hive)
    {
        return hive != null && _cultist.GetSupportedHive(cultist)?.Owner == hive;
    }

    public bool CanHandleParasite(EntityUid parasite, EntityUid user)
    {
        return !HasComp<CultistComponent>(user) || SupportsHive(user, _hive.GetHive(parasite)?.Owner);
    }

    public bool CanRescueXeno(EntityUid user, EntityUid target)
    {
        return HasComp<CultistComponent>(user) && HasComp<XenoComponent>(target) &&
               _mobState.IsCritical(target) && SupportsHive(user, _hive.GetHive(target)?.Owner);
    }
}
