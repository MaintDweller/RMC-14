using Content.Shared.Body.Components;
using Content.Shared.Database;
using Content.Shared.DoAfter;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistRitesSystem
{
    private void InitializeSacrificeRite()
    {
        SubscribeLocalEvent<CultistComponent, CultistSacrificeChoiceEvent>(OnSacrificeChoice);
        SubscribeLocalEvent<CultistComponent, CultistSacrificeDoAfterEvent>(OnSacrificeDoAfter);
        SubscribeLocalEvent<CultistComponent, DoAfterAttemptEvent<CultistSacrificeDoAfterEvent>>(OnSacrificeAttempt);
    }

    private void StartSacrifice(Entity<CultistDevotionComponent> group, CultistSacrifice ritual)
    {
        var supporter = ritual.Supporter!.Value;
        ritual.Started = true;
        Comp<CultistComponent>(supporter).Channeling = true;
        ritual.SupporterPosition = Transform(supporter).Coordinates;
        ritual.VictimPosition = Transform(ritual.Victim).Coordinates;
        ritual.ExpiresAt = _timing.CurTime + group.Comp.SacrificeDelay + group.Comp.InvitationTimeout;
        var ev = new CultistSacrificeDoAfterEvent { Group = GetNetEntity(group), RitualId = ritual.Id };
        if (!_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ritual.Initiator, group.Comp.SacrificeDelay, ev,
                ritual.Initiator, ritual.Victim)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
                DamageThreshold = 0,
                MovementThreshold = Comp<CultistComponent>(ritual.Initiator).MovementThreshold,
                DistanceThreshold = group.Comp.RitualRange,
                AttemptFrequency = AttemptFrequency.EveryTick,
                CancelDuplicate = false,
                ForceVisible = true,
            }, out var id))
        {
            CancelSacrifice(group);
            return;
        }

        if (ev.Handled)
            return;
        ritual.DoAfter = id;
        StartRiteVisuals(ritual.Effects, group.Comp.SacrificeEffects, ritual.Victim, group.Comp.SacrificeDelay);
        StartRiteVisuals(ritual.Effects, group.Comp.SacrificeParticipantEffects,
            ritual.Initiator, group.Comp.SacrificeDelay);
        StartRiteVisuals(ritual.Effects, group.Comp.SacrificeParticipantEffects, supporter, group.Comp.SacrificeDelay);
        PulseSacrifice(group, ritual);
        _audio.PlayPvs(group.Comp.RitualSound, ritual.Initiator);
        _popup.PopupEntity(Loc.GetString("rmc-cultist-sacrifice-started", ("first", ritual.Initiator),
            ("second", supporter), ("victim", ritual.Victim),
            ("seconds", group.Comp.SacrificeDelay.TotalSeconds)), ritual.Victim);
    }

    private bool ValidSacrifice(Entity<CultistDevotionComponent> group, CultistSacrifice ritual)
    {
        if (!Ready(ritual.Initiator, group) || !Comp<CultistComponent>(ritual.Initiator).Channeling ||
            !CanSacrificeVictim(ritual.Victim, group) || !InRitualRange(ritual.Initiator, ritual.Victim, group))
            return false;
        if (!ritual.Started)
            return true;

        return ritual.Supporter is { } supporter && supporter != ritual.Initiator && Ready(supporter, group) &&
               Comp<CultistComponent>(supporter).Channeling && InRitualRange(supporter, ritual.Victim, group) &&
               ritual.SupporterPosition.TryDistance(EntityManager, Transform(supporter).Coordinates,
                   out var helperMovement) &&
               helperMovement <= Comp<CultistComponent>(supporter).MovementThreshold &&
               ritual.VictimPosition.TryDistance(EntityManager, Transform(ritual.Victim).Coordinates,
                   out var victimMovement) &&
               victimMovement <= Comp<CultistComponent>(ritual.Initiator).MovementThreshold;
    }

    private void OnSacrificeAttempt(Entity<CultistComponent> ent,
        ref DoAfterAttemptEvent<CultistSacrificeDoAfterEvent> args)
    {
        if (_net.IsClient)
            return;
        if (!TryComp<CultistDevotionComponent>(GetEntity(args.Event.Group), out var group) ||
            group.Sacrifice is not { Started: true } ritual || ritual.Id != args.Event.RitualId ||
            ritual.Initiator != ent.Owner || !ValidSacrifice((GetEntity(args.Event.Group), group), ritual))
            args.Cancel();
    }

    private void OnSacrificeDoAfter(Entity<CultistComponent> ent, ref CultistSacrificeDoAfterEvent args)
    {
        if (_net.IsClient || args.Handled || !TryComp<CultistDevotionComponent>(GetEntity(args.Group), out var comp) ||
            comp.Sacrifice is not { Started: true } ritual || ritual.Id != args.RitualId ||
            ritual.Initiator != ent.Owner || args.Target != ritual.Victim ||
            ritual.DoAfter is { } id && id != args.DoAfter.Id)
            return;

        args.Handled = true;
        ritual.DoAfter = null;
        var group = new Entity<CultistDevotionComponent>(GetEntity(args.Group), comp);
        if (args.Cancelled || !ValidSacrifice(group, ritual) || !TryComp<BodyComponent>(ritual.Victim, out var body))
        {
            CancelSacrifice(group);
            return;
        }

        // Clear the reservation before gib events; duplicate completions cannot consume the same victim twice.
        ClearSacrifice(group, ritual, completed: true);
        _rmcGib.ScatterInventoryItems(ritual.Victim);
        _body.GibBody(ritual.Victim, true, body);
        if (!EntityManager.IsQueuedForDeletion(ritual.Victim))
            return;
        Award(ent, ritual.Victim, CultistDevotionSource.Sacrifice);
        _popup.PopupEntity(Loc.GetString("rmc-cultist-sacrifice-completed", ("victim", ritual.Victim)), ent);
        _adminLog.Add(LogType.Mind, LogImpact.High,
            $"{ToPrettyString(ent):actor} and {ToPrettyString(ritual.Supporter):actor} " +
            $"sacrificed {ToPrettyString(ritual.Victim):target}");
    }

    private void CancelSacrifice(Entity<CultistDevotionComponent> group)
    {
        if (group.Comp.Sacrifice is not { } ritual)
            return;
        ClearSacrifice(group, ritual);
        if (ritual.DoAfter is { } id && _doAfter.IsRunning(id))
            _doAfter.Cancel(id);
        if (!TerminatingOrDeleted(ritual.Initiator))
            _popup.PopupEntity(Loc.GetString("rmc-cultist-sacrifice-interrupted"), ritual.Initiator);
    }

    private void ClearSacrifice(Entity<CultistDevotionComponent> group, CultistSacrifice ritual,
        bool completed = false)
    {
        group.Comp.Sacrifice = null;
        EndRiteVisuals(ritual.Effects, completed);
        if (TryComp<CultistComponent>(ritual.Initiator, out var initiator))
            initiator.Channeling = false;
        if (ritual.Supporter is { } supporter && TryComp<CultistComponent>(supporter, out var helper))
            helper.Channeling = false;
    }

    private void UpdateSacrifice(Entity<CultistDevotionComponent> group)
    {
        if (group.Comp.Sacrifice is not { } ritual)
            return;
        if (ritual.ExpiresAt <= _timing.CurTime || !ValidSacrifice(group, ritual))
        {
            CancelSacrifice(group);
            return;
        }
        if (ritual.Started)
            PulseSacrifice(group, ritual);
    }

    private void PulseSacrifice(Entity<CultistDevotionComponent> group, CultistSacrifice ritual)
    {
        // Short pulses expire after cancellation without removing jitter caused by another mechanic.
        _jitter.DoJitter(ritual.Victim, group.Comp.SacrificeJitterTime, true,
            group.Comp.SacrificeJitterAmplitude, group.Comp.SacrificeJitterFrequency);
    }
}
