using Content.Shared._RMC14.Synth;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistRitesSystem
{
    public bool Nominate(Entity<CultistComponent> candidate)
    {
        if (_net.IsClient || candidate.Comp.Leader || candidate.Comp.Channeling ||
            HasComp<SynthComponent>(candidate) || _cultist.GetGroup(candidate.AsNullable()) is not { } group ||
            group.Comp.Ritual != null || group.Comp.Revelation != null || !Ready(candidate, group))
            return false;

        var ritual = new CultistSuccession
        {
            Id = ++group.Comp.NextRitualId,
            Candidate = candidate,
            ExpiresAt = _timing.CurTime + group.Comp.InvitationTimeout,
        };
        group.Comp.Ritual = ritual;
        if (!HasActiveLeader(group) && LivingMembers(group) == 1)
            return StartRitual(group);

        _popup.PopupEntity(Loc.GetString("rmc-cultist-rite-invitation", ("candidate", candidate.Owner)), candidate);
        return true;
    }

    public bool Support(Entity<CultistComponent> supporter)
    {
        if (_net.IsClient || supporter.Comp.Channeling || _cultist.GetGroup(supporter.AsNullable()) is not { } group ||
            group.Comp.Ritual is not { Started: false } ritual || ritual.ExpiresAt <= _timing.CurTime ||
            ritual.Candidate == supporter.Owner || !Ready(supporter, group) || !Ready(ritual.Candidate, group) ||
            !_interaction.InRangeUnobstructed(supporter.Owner, ritual.Candidate, group.Comp.RitualRange))
            return false;

        ritual.Supporter = supporter;
        return StartRitual(group);
    }

    private bool StartRitual(Entity<CultistDevotionComponent> group)
    {
        var ritual = group.Comp.Ritual!;
        var vacant = !HasActiveLeader(group);
        var voluntary = !vacant && ritual.Supporter == group.Comp.Leader;
        var cost = vacant || voluntary ? 0 : group.Comp.UsurpationCost;
        if (group.Comp.Devotion < cost || cost > 0 && group.Comp.UsurpationReadyAt > _timing.CurTime)
        {
            _popup.PopupEntity(Loc.GetString("rmc-cultist-rite-unavailable"), ritual.Candidate, ritual.Candidate);
            CancelRitual(group);
            return false;
        }

        var delay = voluntary ? group.Comp.TransferDelay : !vacant ? group.Comp.UsurpationDelay :
            ritual.Supporter == null ? group.Comp.SolitaryDelay : group.Comp.SuccessionDelay;
        ritual.PreviousLeader = group.Comp.Leader;
        ritual.LeaderVacant = vacant;
        ritual.Cost = cost;
        ritual.ExpiresAt = _timing.CurTime + delay + TimeSpan.FromSeconds(5);
        if (ritual.Supporter is { } supporter)
        {
            ritual.SupporterPosition = Transform(supporter).Coordinates;
            ritual.SupporterDamage = CompOrNull<DamageableComponent>(supporter)?.TotalDamage ?? FixedPoint2.Zero;
        }

        var ev = new CultistSuccessionDoAfterEvent { Group = GetNetEntity(group), RitualId = ritual.Id };
        if (!_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ritual.Candidate, delay, ev,
                ritual.Candidate, ritual.Supporter)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
                DistanceThreshold = group.Comp.RitualRange,
                AttemptFrequency = AttemptFrequency.EveryTick,
                CancelDuplicate = false,
            }))
        {
            CancelRitual(group);
            return false;
        }

        ritual.Started = true;
        Comp<CultistComponent>(ritual.Candidate).Channeling = true;
        if (ritual.Supporter is { } helper)
            Comp<CultistComponent>(helper).Channeling = true;
        if (!ev.Handled)
        {
            StartRiteVisuals(ritual.Effects, group.Comp.SuccessionEffects, ritual.Candidate, delay);
            if (ritual.Supporter is { } visualSupporter)
                StartRiteVisuals(ritual.Effects, group.Comp.SuccessionParticipantEffects, visualSupporter, delay);
        }
        _audio.PlayPvs(group.Comp.SuccessionSound, ritual.Candidate);
        _popup.PopupEntity(Loc.GetString("rmc-cultist-rite-started", ("candidate", ritual.Candidate),
            ("seconds", (int) delay.TotalSeconds)), ritual.Candidate);
        return true;
    }

    private bool ValidRitual(Entity<CultistDevotionComponent> group, CultistSuccession ritual)
    {
        if (!Ready(ritual.Candidate, group) || HasComp<SynthComponent>(ritual.Candidate) ||
            Comp<CultistComponent>(ritual.Candidate).Leader || group.Comp.Leader != ritual.PreviousLeader ||
            HasActiveLeader(group) == ritual.LeaderVacant || group.Comp.Devotion < ritual.Cost)
            return false;

        if (ritual.Supporter is not { } supporter)
            return ritual.LeaderVacant && LivingMembers(group) == 1;

        return Ready(supporter, group) &&
               _interaction.InRangeUnobstructed(ritual.Candidate, supporter, group.Comp.RitualRange) &&
               ritual.SupporterPosition.TryDistance(EntityManager, Transform(supporter).Coordinates,
                   out var distance) &&
               distance <= Comp<CultistComponent>(ritual.Candidate).MovementThreshold &&
               (CompOrNull<DamageableComponent>(supporter)?.TotalDamage ?? FixedPoint2.Zero) <= ritual.SupporterDamage;
    }

    private void OnSuccessionAttempt(Entity<CultistComponent> ent,
        ref DoAfterAttemptEvent<CultistSuccessionDoAfterEvent> args)
    {
        if (!TryComp<CultistDevotionComponent>(GetEntity(args.Event.Group), out var group) ||
            group.Ritual is not { } ritual || ritual.Id != args.Event.RitualId ||
            !ValidRitual((GetEntity(args.Event.Group), group), ritual))
            args.Cancel();
    }

    private void OnSuccessionDoAfter(Entity<CultistComponent> ent, ref CultistSuccessionDoAfterEvent args)
    {
        if (args.Handled || _net.IsClient || !TryComp<CultistDevotionComponent>(GetEntity(args.Group), out var comp) ||
            comp.Ritual is not { } ritual || ritual.Id != args.RitualId)
            return;

        args.Handled = true;
        var group = new Entity<CultistDevotionComponent>(GetEntity(args.Group), comp);
        if (args.Cancelled || !ValidRitual(group, ritual))
        {
            CancelRitual(group);
            return;
        }

        // Invalidate the rite before changing actions; a second completion cannot spend or promote twice.
        comp.Ritual = null;
        EndRiteVisuals(ritual.Effects, completed: true);
        comp.Devotion -= ritual.Cost;
        if (ritual.Cost > 0)
            comp.UsurpationReadyAt = _timing.CurTime + comp.UsurpationCooldown;
        if (comp.Leader is { } oldLeader && TryComp<CultistComponent>(oldLeader, out var old))
            _cultist.SetLeader((oldLeader, old), false);
        comp.Leader = ent;
        comp.LeaderAbsentSince = null;
        _cultist.SetLeader(ent, true);
        ent.Comp.Channeling = false;
        if (ritual.Supporter is { } supporter && TryComp<CultistComponent>(supporter, out var helper))
            helper.Channeling = false;
        Dirty(group);
        _audio.PlayPvs(comp.SuccessionSound, ent);
        _popup.PopupEntity(Loc.GetString("rmc-cultist-rite-completed", ("candidate", ent.Owner)), ent);
        _adminLog.Add(LogType.Mind, LogImpact.High,
            $"{ToPrettyString(ent):actor} became cult leader, " +
            $"replacing {ToPrettyString(ritual.PreviousLeader):target} for {ritual.Cost} devotion");
    }
}
