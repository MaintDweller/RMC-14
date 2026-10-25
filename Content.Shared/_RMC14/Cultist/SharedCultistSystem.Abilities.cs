using Content.Shared._RMC14.Slow;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistSystem
{
    private void OnConvertAction(Entity<CultistComponent> ent, ref CultistConvertActionEvent args)
    {
        if (args.Handled || _net.IsClient || ent.Comp.Channeling || ConversionOnCooldown(ent) ||
            !CanTarget(ent, args.Target, true) || !CanRecruit(ent, true))
            return;

        var ev = new CultistConvertDoAfterEvent();
        SaveTarget(ev, args.Target);
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ent, ent.Comp.ConversionDelay,
            ev, ent, args.Target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            MovementThreshold = ent.Comp.MovementThreshold,
            DistanceThreshold = ent.Comp.AbilityRange,
            AttemptFrequency = AttemptFrequency.EveryTick,
            CancelDuplicate = false,
        });
        ent.Comp.Channeling = args.Handled;
    }

    private void OnConvertAttempt(Entity<CultistComponent> ent,
        ref DoAfterAttemptEvent<CultistConvertDoAfterEvent> args)
    {
        if (ConversionOnCooldown(ent) || !CanRecruit(ent) ||
            args.DoAfter.Args.Target is not { } target || !CanTarget(ent, target) ||
            !TargetUnchanged(args.Event, target, ent.Comp.MovementThreshold))
            args.Cancel();
    }

    private void OnConvertDoAfter(Entity<CultistComponent> ent, ref CultistConvertDoAfterEvent args)
    {
        if (args.Handled || _net.IsClient)
            return;

        ent.Comp.Channeling = false;
        if (args.Cancelled || ConversionOnCooldown(ent) || !CanRecruit(ent) || args.Target is not { } target ||
            !CanTarget(ent, target) || _hive.GetHive(ent.Owner) is not { } hive)
            return;

        args.Handled = true;
        _actions.SetCooldown(ent.Comp.ConvertActionEntity, ent.Comp.ConversionCooldown);
        if (TryComp<CultistDevotionComponent>(hive, out var group))
            group.ConversionReadyAt = _timing.CurTime + ent.Comp.ConversionCooldown;
        MakeCultist(target, hive);
        _sizeStun.TryKnockOut(target, ent.Comp.ConversionKnockout);
        _jitter.DoJitter(target, ent.Comp.ConversionKnockout, true);
        _audio.PlayEntity(ent.Comp.ConversionSound, target, target, AudioParams.Default.WithVolume(-6));
        var ev = new CultistConvertedEvent(target, ent);
        RaiseLocalEvent(target, ref ev);
    }

    private void OnStunAction(Entity<CultistComponent> ent, ref CultistStunActionEvent args)
    {
        if (args.Handled || _net.IsClient || ent.Comp.Channeling || !CanTarget(ent, args.Target, true) ||
            HasComp<RMCRootedComponent>(args.Target))
            return;

        var expiresAt = _timing.CurTime + ent.Comp.StunDelay;
        var ev = new CultistStunDoAfterEvent { RootExpiresAt = expiresAt };
        SaveTarget(ev, args.Target);
        if (!_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ent, ent.Comp.StunDelay,
                ev, ent, args.Target)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
                MovementThreshold = ent.Comp.MovementThreshold,
                DistanceThreshold = ent.Comp.AbilityRange,
                AttemptFrequency = AttemptFrequency.EveryTick,
                CancelDuplicate = false,
            }))
            return;

        args.Handled = true;
        ent.Comp.Channeling = true;
        _slow.TryRoot(args.Target, ent.Comp.StunDelay);
        _popup.PopupEntity(Loc.GetString("rmc-cultist-stun-warning"), args.Target, args.Target);
    }

    private void OnStunAttempt(Entity<CultistComponent> ent, ref DoAfterAttemptEvent<CultistStunDoAfterEvent> args)
    {
        if (args.DoAfter.Args.Target is not { } target || !CanTarget(ent, target) ||
            !TargetUnchanged(args.Event, target, ent.Comp.MovementThreshold))
            args.Cancel();
    }

    private void OnStunDoAfter(Entity<CultistComponent> ent, ref CultistStunDoAfterEvent args)
    {
        if (args.Handled || _net.IsClient)
            return;

        args.Handled = true;
        ent.Comp.Channeling = false;
        if (args.Target is { } rooted && TryComp<RMCRootedComponent>(rooted, out var root) &&
            root.ExpiresAt <= args.RootExpiresAt)
            RemCompDeferred<RMCRootedComponent>(rooted);

        if (args.Target is not { } target || args.Cancelled || !CanTarget(ent, target))
        {
            SetStunCooldown(ent, ent.Comp.InterruptedStunCooldown);
            return;
        }

        SetStunCooldown(ent, ent.Comp.StunCooldown);
        _sizeStun.TryKnockOut(target, ent.Comp.StunDuration);
        _jitter.DoJitter(target, ent.Comp.StunDuration, true);
        _audio.PlayPvs(ent.Comp.StunSound, target, AudioParams.Default.WithVolume(-6));
        _popup.PopupEntity(Loc.GetString("rmc-cultist-stun-success"), target, target);
    }

    private bool ConversionOnCooldown(Entity<CultistComponent> ent)
    {
        return _hive.GetHive(ent.Owner) is { } hive &&
               TryComp<CultistDevotionComponent>(hive, out var group) && group.ConversionReadyAt > _timing.CurTime;
    }

    private void SetStunCooldown(Entity<CultistComponent> ent, TimeSpan cooldown)
    {
        _actions.SetCooldown(ent.Comp.StunActionEntity, cooldown);
        if (_hive.GetHive(ent.Owner) is { } hive && TryComp<CultistDevotionComponent>(hive, out var group))
            group.StunReadyAt = _timing.CurTime + cooldown;
    }

    private void SaveTarget(CultistTargetDoAfterEvent ev, EntityUid target)
    {
        ev.TargetPosition = GetNetCoordinates(Transform(target).Coordinates);
        ev.TargetDamage = CompOrNull<DamageableComponent>(target)?.TotalDamage ?? FixedPoint2.Zero;
    }

    private bool TargetUnchanged(CultistTargetDoAfterEvent ev, EntityUid target, float movementThreshold)
    {
        var original = GetCoordinates(ev.TargetPosition);
        return original.TryDistance(EntityManager, Transform(target).Coordinates, out var distance) &&
               distance <= movementThreshold &&
               (CompOrNull<DamageableComponent>(target)?.TotalDamage ?? FixedPoint2.Zero) <= ev.TargetDamage;
    }

    private bool CanRecruit(EntityUid leader, bool popup = false)
    {
        if (_hive.GetHive(leader) is not { } hive ||
            !TryComp<CultistDevotionComponent>(hive, out var group) ||
            group.Revealed || !group.RevelationRequired || CanRecruitCompanion(leader, (hive, group)))
            return true;

        if (popup)
            _popup.PopupEntity(Loc.GetString("rmc-cultist-recruitment-sealed",
                ("limit", group.ConcealedMemberLimit)), leader, leader);
        return false;
    }

    public bool CanRecruitCompanion(EntityUid leader, Entity<CultistDevotionComponent> group)
    {
        if (group.Comp.Leader != leader || !TryComp<CultistComponent>(leader, out var cultist) ||
            !cultist.Leader || !_mobState.IsAlive(leader))
            return false;

        foreach (var member in group.Comp.Members)
        {
            if (member == leader || TerminatingOrDeleted(member) || !HasComp<CultistComponent>(member) ||
                _hive.GetHive(member)?.Owner != group.Owner)
                continue;
            if (!_mobState.IsDead(member) || !_unrevivable.IsUnrevivable(member))
                return false;
        }
        return true;
    }
}
