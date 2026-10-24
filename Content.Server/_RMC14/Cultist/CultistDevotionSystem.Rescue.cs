using Content.Shared._RMC14.Communications;
using Content.Shared._RMC14.Cultist;
using Content.Shared._RMC14.Xenonids;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;

namespace Content.Server._RMC14.Cultist;

public sealed partial class CultistDevotionSystem
{
    private void OnRescueDamage(Entity<DamageableComponent> ent, ref DamageChangedEvent args)
    {
        if (TryComp<CommunicationsTowerComponent>(ent, out var tower))
            OnTowerDamaged((ent, tower), ref args);

        if (!args.DamageIncreased || !HasComp<XenoComponent>(ent) || HasComp<CultistComponent>(ent))
            return;

        if (_rescues.TryGetValue(ent, out var rescue))
            rescue.SafeSince = null;

        if (args.Origin is not { } origin)
            return;
        if (origin == ent.Owner || HasComp<CultistComponent>(origin) || HasComp<XenoComponent>(origin) ||
            _hive.FromSameHive(origin, ent.Owner))
        {
            _rescues.Remove(ent);
            return;
        }

        if (!HasComp<MobStateComponent>(origin) || _rites.GetGroup(ent) is not { } group ||
            !_threshold.TryGetThresholdForState(ent, MobState.Dead, out var threshold))
            return;

        if (rescue == null || rescue.ExpiresAt <= _timing.CurTime)
        {
            rescue = new Rescue(group, Transform(ent).Coordinates, _timing.CurTime + group.Comp.RescueExpiry);
            _rescues[ent] = rescue;
        }
        if (args.DamageDelta != null)
        {
            foreach (var damage in args.DamageDelta.DamageDict.Values)
            {
                if (damage > 0)
                    rescue.EnemyDamage += damage;
            }
        }
        rescue.Eligible |= rescue.EnemyDamage >= threshold * group.Comp.RescueDamageFraction &&
                           args.Damageable.TotalDamage >= threshold * group.Comp.RescueDamageFraction;
        if (rescue.Eligible && TryComp<PullableComponent>(ent, out var pullable) &&
            pullable.Puller is { } puller && HasComp<CultistComponent>(puller) &&
            _rites.GetGroup(puller)?.Owner == group.Owner)
            rescue.Rescuer ??= puller;
    }

    private void OnRescuePullStarted(Entity<CultistComponent> ent, ref PullStartedMessage args)
    {
        if (args.PullerUid != ent.Owner || !_rescues.TryGetValue(args.PulledUid, out var rescue) || !rescue.Eligible ||
            _rites.GetGroup(ent)?.Owner != rescue.Group)
            return;

        rescue.Rescuer = ent;
        rescue.SafeSince = null;
        rescue.Evacuated = false;
    }

    private void UpdateRescues()
    {
        foreach (var (target, rescue) in _rescues)
        {
            if (TerminatingOrDeleted(target) || _timing.CurTime >= rescue.ExpiresAt ||
                !HasComp<XenoComponent>(target) || HasComp<CultistComponent>(target) ||
                _rites.GetGroup(target)?.Owner != rescue.Group)
            {
                _rescues.Remove(target);
                continue;
            }
            if (!rescue.Eligible || rescue.Rescuer is not { } actor || TerminatingOrDeleted(actor) ||
                _rites.GetGroup(actor) is not { } group || group.Owner != rescue.Group)
                continue;

            if (TryComp<PullableComponent>(target, out var pullable) &&
                pullable.Puller is { } puller && puller != actor)
            {
                rescue.Rescuer = null;
                rescue.SafeSince = null;
                continue;
            }

            if (!_weeds.IsOnFriendlyWeeds((target, Transform(target))) ||
                !rescue.Position.TryDistance(EntityManager, Transform(target).Coordinates, out var distance) ||
                distance < group.Comp.RescueDistance)
            {
                rescue.SafeSince = null;
                continue;
            }

            if (!rescue.Evacuated)
            {
                if (pullable?.Puller != actor)
                {
                    rescue.Rescuer = null;
                    continue;
                }
                rescue.Evacuated = true;
            }

            if (!_mobState.IsAlive(target))
            {
                rescue.SafeSince = null;
                continue;
            }

            rescue.SafeSince ??= _timing.CurTime;
            if (_timing.CurTime < rescue.SafeSince + group.Comp.RescueSafetyDelay)
                continue;

            _rites.Award(actor, target, CultistDevotionSource.XenoRescue);
            _rescues.Remove(target);
        }
    }

    private void OnRescuePullStopped(Entity<CultistComponent> ent, ref PullStoppedMessage args)
    {
        if (args.PullerUid != ent.Owner || !_rescues.TryGetValue(args.PulledUid, out var rescue) ||
            rescue.Rescuer != ent.Owner || _rites.GetGroup(ent) is not { } group || group.Owner != rescue.Group ||
            TerminatingOrDeleted(args.PulledUid))
            return;

        var target = args.PulledUid;
        if (rescue.Eligible && _weeds.IsOnFriendlyWeeds((target, Transform(target))) &&
            rescue.Position.TryDistance(EntityManager, Transform(target).Coordinates, out var distance) &&
            distance >= group.Comp.RescueDistance)
            rescue.Evacuated = true;
        else if (!rescue.Evacuated)
            rescue.Rescuer = null;
    }
}
