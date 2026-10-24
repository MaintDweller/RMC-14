using Content.Shared._RMC14.Communications;
using Content.Shared._RMC14.Cultist;
using Content.Shared._RMC14.Sensor;
using Content.Shared.Damage;

namespace Content.Server._RMC14.Cultist;

public sealed partial class CultistDevotionSystem
{
    private void InitializeSabotage()
    {
        SubscribeLocalEvent<CommunicationsTowerStateChangedEvent>(OnTowerStateChanged);
        SubscribeLocalEvent<SensorTowerStateChangedEvent>(OnSensorStateChanged);
    }

    private void OnTowerDamaged(Entity<CommunicationsTowerComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;
        // Destruction can raise the state change inside the damage event, before it returns.
        if (args.Origin is { } origin && HasComp<CultistComponent>(origin))
            _towerAttackers[ent] = origin;
        else
            _towerAttackers.Remove(ent);
    }

    private void OnTowerStateChanged(CommunicationsTowerStateChangedEvent args)
    {
        var ent = args.Tower;
        if (ent.Comp.State == CommunicationsTowerState.On)
        {
            _sabotage.Remove(ent);
            _towerAttackers.Remove(ent);
            return;
        }
        if (args.PreviousState != CommunicationsTowerState.On)
            return;

        var actor = args.User;
        if (actor == null && ent.Comp.State == CommunicationsTowerState.Broken &&
            _towerAttackers.TryGetValue(ent, out var attacker))
            actor = attacker;
        _towerAttackers.Remove(ent);
        StartSabotage(ent, actor, CultistDevotionSource.Sabotage);
    }

    private void OnSensorStateChanged(ref SensorTowerStateChangedEvent args)
    {
        var ent = args.Tower;
        if (ent.Comp.State == SensorTowerState.On)
        {
            _sabotage.Remove(ent);
            return;
        }
        if (args.PreviousState == SensorTowerState.On)
            StartSabotage(ent, args.User, CultistDevotionSource.SensorSabotage);
    }

    private void StartSabotage(EntityUid target, EntityUid? actor, CultistDevotionSource source)
    {
        if (actor is not { } cultist || !HasComp<CultistComponent>(cultist) ||
            _rites.GetGroup(cultist) is not { } group || Transform(target).MapUid == null)
            return;

        _sabotage[target] = new Sabotage(cultist, group, _timing.CurTime + group.Comp.SabotageDelay, source);
    }

    private bool IsSabotaged(EntityUid target, CultistDevotionSource source)
    {
        return source switch
        {
            CultistDevotionSource.Sabotage => TryComp<CommunicationsTowerComponent>(target, out var comms) &&
                                               comms.State != CommunicationsTowerState.On,
            CultistDevotionSource.SensorSabotage => TryComp<SensorTowerComponent>(target, out var sensor) &&
                                                     sensor.State != SensorTowerState.On,
            _ => false,
        };
    }
}
