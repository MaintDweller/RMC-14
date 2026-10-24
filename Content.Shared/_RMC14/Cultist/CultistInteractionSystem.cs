using Content.Shared._RMC14.Marines;
using Content.Shared._RMC14.Xenonids;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared.Mobs.Systems;

namespace Content.Shared._RMC14.Cultist;

public sealed class CultistInteractionSystem : EntitySystem
{
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public bool HasHiveProtection(Entity<CultistComponent?> member)
    {
        return Resolve(member, ref member.Comp, false) && !member.Comp.AllowFriendlyFire;
    }

    public bool CanHarmHive(Entity<CultistComponent?> member)
    {
        return Resolve(member, ref member.Comp, false) && member.Comp.AllowFriendlyFire;
    }

    public bool IsProtectedFrom(EntityUid source, Entity<CultistComponent?> target)
    {
        return HasHiveProtection(target) && _hive.FromSameHive(source, target.Owner);
    }

    public bool IsProtectedFromHive(Entity<CultistComponent?> member, EntityUid? hive)
    {
        return HasHiveProtection(member) && _hive.IsMember(member.Owner, hive);
    }

    public bool BlocksCultistDamage(Entity<CultistComponent?> source, Entity<CultistComponent?> target)
    {
        if (!_hive.FromSameHive(source.Owner, target.Owner))
            return false;

        Resolve(source, ref source.Comp, false);
        Resolve(target, ref target.Comp, false);
        return source.Comp != null && target.Comp == null && HasHiveProtection(source) ||
               target.Comp != null && source.Comp == null && HasHiveProtection(target);
    }

    /// <summary>
    /// Xeno damage normally spares hive allies; cultists can opt into that damage.
    /// </summary>
    public bool IsXenoDamageAlly(EntityUid source, EntityUid target)
    {
        return _hive.FromSameHive(source, target) && !CanHarmHive(target);
    }

    /// <summary>
    /// Cultists remain valid targets for xeno control in both damage modes.
    /// </summary>
    public bool IsXenoControlAlly(EntityUid source, EntityUid target)
    {
        return _hive.FromSameHive(source, target) && !HasComp<CultistComponent>(target);
    }

    public bool IsXenoAbilityTarget(EntityUid target)
    {
        return HasComp<MarineComponent>(target) || HasComp<XenoComponent>(target) || HasComp<CultistComponent>(target);
    }

    public bool IsHiveStructureProtected(EntityUid source, EntityUid structure)
    {
        return _hive.FromSameHive(source, structure) && !CanHarmHive(source);
    }

    public bool IsNpcAlly(EntityUid entity, EntityUid other)
    {
        return (HasComp<CultistComponent>(entity) &&
                (HasComp<XenoComponent>(other) || HasComp<CultistComponent>(other)) ||
                HasComp<CultistComponent>(other) && HasComp<XenoComponent>(entity)) &&
               _hive.FromSameHive(entity, other);
    }

    public bool CanHandleParasite(EntityUid parasite, EntityUid user)
    {
        return !HasComp<CultistComponent>(user) || _hive.FromSameHive(parasite, user);
    }

    public bool CanRescueXeno(EntityUid user, EntityUid target)
    {
        return HasComp<CultistComponent>(user) && HasComp<XenoComponent>(target) &&
               _mobState.IsCritical(target) && _hive.FromSameHive(user, target);
    }

    public bool CanReceiveResinSpeedBoost(EntityUid member)
    {
        return !HasComp<CultistComponent>(member);
    }

    public bool CanUseHiveCommunication(EntityUid member)
    {
        return !HasComp<CultistComponent>(member);
    }

    public bool CanBeWatched(EntityUid member)
    {
        return !HasComp<CultistComponent>(member);
    }
}
