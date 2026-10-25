using Content.Shared._RMC14.Dialog;
using Content.Shared.Database;
using Content.Shared.DoAfter;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistRitesSystem
{
    private void InitializeRevelationRite()
    {
        SubscribeLocalEvent<CultistComponent, CultistRevelationDoAfterEvent>(OnRevelationDoAfter);
        SubscribeLocalEvent<CultistComponent, DoAfterAttemptEvent<CultistRevelationDoAfterEvent>>(
            OnRevelationAttempt);
    }

    private void OnRevelationAttempt(Entity<CultistComponent> ent,
        ref DoAfterAttemptEvent<CultistRevelationDoAfterEvent> args)
    {
        if (_net.IsClient)
            return;
        if (!TryComp<CultistDevotionComponent>(GetEntity(args.Event.Group), out var group) ||
            group.Revelation is not { Started: true } ritual || ritual.Id != args.Event.RitualId ||
            ritual.Leader != ent.Owner || !ValidRevelation((GetEntity(args.Event.Group), group), ritual))
            args.Cancel();
    }

    private void OnRevelationDoAfter(Entity<CultistComponent> ent, ref CultistRevelationDoAfterEvent args)
    {
        if (_net.IsClient || args.Handled || !TryComp<CultistDevotionComponent>(GetEntity(args.Group), out var comp) ||
            comp.Revelation is not { Started: true } ritual || ritual.Id != args.RitualId ||
            ritual.Leader != ent.Owner || ritual.DoAfter is { } id && id != args.DoAfter.Id)
            return;

        args.Handled = true;
        var group = new Entity<CultistDevotionComponent>(GetEntity(args.Group), comp);
        if (args.Cancelled || !ValidRevelation(group, ritual))
        {
            CancelRevelation(group);
            return;
        }
        ClearRevelation(group, ritual);
        comp.Devotion -= comp.RevelationCost;
        comp.Revealed = true;
        comp.RevelationRequired = false;
        Dirty(group);
        foreach (var member in comp.Members)
        {
            if (!TerminatingOrDeleted(member) && !_mobState.IsDead(member) &&
                GetGroup(member)?.Owner == group.Owner &&
                TryComp<CultistComponent>(member, out var cultist))
                EquipRobes((member, cultist), group);
        }
        _adminLog.Add(LogType.Mind, LogImpact.High,
            $"{ToPrettyString(ent):actor} revealed their cult with {ritual.Participants.Count} participants " +
            $"for {comp.RevelationCost} devotion");
    }

    private void OnRevelationChoice(Entity<CultistComponent> ent, Entity<CultistDevotionComponent> group,
        CultistRiteChoiceEvent args)
    {
        if (args.Choice == CultistRiteChoice.Revelation)
        {
            if (args.RitualId != group.Comp.NextRevelationId || !OfferRevelation(ent, group))
                _popup.PopupEntity(Loc.GetString("rmc-cultist-revelation-unavailable"), ent, ent);
            return;
        }

        if (group.Comp.Revelation is not { } ritual || ritual.Id != args.RitualId)
            return;
        if (args.Choice == CultistRiteChoice.CancelRevelation)
        {
            if (ritual.Participants.ContainsKey(ent))
                CancelRevelation(group);
            return;
        }

        if (ritual.Started || ritual.Participants.ContainsKey(ent) || !CanJoinRevelation(ent, group) ||
            !ValidRevelation(group, ritual) ||
            !_interaction.InRangeUnobstructed(ent.Owner, ritual.Leader, group.Comp.RevelationRange))
        {
            _popup.PopupEntity(Loc.GetString("rmc-cultist-revelation-unavailable"), ent, ent);
            return;
        }
        ritual.Participants.Add(ent, Transform(ent).Coordinates);
        ent.Comp.Channeling = true;
        UpdateRevelation(group);
        OpenRitesMenu(ent, group);
    }

    private string RevelationDescription(Entity<CultistDevotionComponent> group)
    {
        return Loc.GetString(group.Comp.Revealed ? "rmc-cultist-revelation-revealed" :
            "rmc-cultist-revelation-description", ("limit", group.Comp.ConcealedMemberLimit),
            ("seconds", group.Comp.RevelationDelay.TotalSeconds), ("cost", group.Comp.RevelationCost),
            ("range", group.Comp.RevelationRange));
    }

    private string RevelationStatus(Entity<CultistDevotionComponent> group)
    {
        if (group.Comp.Revealed)
            return Loc.GetString("rmc-cultist-revelation-revealed");
        if (group.Comp.Revelation is { } ritual)
            return Loc.GetString(ritual.Started ? "rmc-cultist-revelation-channeling" :
                "rmc-cultist-revelation-waiting", ("leader", ritual.Leader),
                ("joined", ritual.Participants.Count), ("total", RevelationMembers(group)));
        if (group.Comp.RevelationRequired && group.Comp.Leader is { } leader &&
            _cultist.CanRecruitCompanion(leader, group))
            return Loc.GetString("rmc-cultist-revelation-last-survivor");
        return Loc.GetString(group.Comp.RevelationRequired ? "rmc-cultist-recruitment-sealed" :
            "rmc-cultist-revelation-concealed", ("limit", group.Comp.ConcealedMemberLimit));
    }

    private void AddRevelationOptions(Entity<CultistComponent> ent, Entity<CultistDevotionComponent> group,
        List<DialogOption> options)
    {
        if (group.Comp.Revealed)
            return;
        var ritual = group.Comp.Revelation;
        if (ritual == null && ent.Comp.Leader && group.Comp.RevelationRequired)
            options.Add(new DialogOption(Loc.GetString("rmc-cultist-rite-revelation-cost",
                    ("cost", group.Comp.RevelationCost)),
                new CultistRiteChoiceEvent(GetNetEntity(group), group.Comp.NextRevelationId,
                    CultistRiteChoice.Revelation), description: RevelationDescription(group)));
        if (ritual is { Started: false } && !ritual.Participants.ContainsKey(ent))
            options.Add(new DialogOption(Loc.GetString("rmc-cultist-revelation-join", ("leader", ritual.Leader)),
                new CultistRiteChoiceEvent(GetNetEntity(group), ritual.Id, CultistRiteChoice.JoinRevelation),
                description: RevelationDescription(group)));
        if (ritual != null && ritual.Participants.ContainsKey(ent))
            options.Add(new DialogOption(Loc.GetString("rmc-cultist-rite-cancel"),
                new CultistRiteChoiceEvent(GetNetEntity(group), ritual.Id, CultistRiteChoice.CancelRevelation)));
    }

    private bool OfferRevelation(Entity<CultistComponent> ent, Entity<CultistDevotionComponent> group)
    {
        if (!ent.Comp.Leader || group.Comp.Leader != ent.Owner || !group.Comp.RevelationRequired ||
            group.Comp.Revealed || group.Comp.Revelation != null || group.Comp.Ritual != null ||
            group.Comp.Devotion < group.Comp.RevelationCost || !CanJoinRevelation(ent, group))
            return false;

        group.Comp.Revelation = new CultistRevelation
        {
            Id = ++group.Comp.NextRevelationId,
            Leader = ent,
            ExpiresAt = _timing.CurTime + group.Comp.InvitationTimeout,
        };
        group.Comp.Revelation.Participants.Add(ent, Transform(ent).Coordinates);
        ent.Comp.Channeling = true;
        _popup.PopupEntity(Loc.GetString("rmc-cultist-revelation-invitation", ("leader", ent.Owner)), ent);
        return true;
    }

    private bool CanJoinRevelation(Entity<CultistComponent> ent, Entity<CultistDevotionComponent> group)
    {
        return !ent.Comp.Channeling && !EntityManager.IsQueuedForDeletion(ent) && Ready(ent, group) &&
               !_standing.IsDown(ent) &&
               !_container.IsEntityInContainer(ent);
    }

    private int RevelationMembers(Entity<CultistDevotionComponent> group)
    {
        var count = 0;
        foreach (var member in group.Comp.Members)
        {
            if (!TerminatingOrDeleted(member) && !EntityManager.IsQueuedForDeletion(member) &&
                HasComp<CultistComponent>(member) && GetGroup(member)?.Owner == group.Owner &&
                !_mobState.IsDead(member))
                count++;
        }
        return count;
    }

    private void UpdateRevelation(Entity<CultistDevotionComponent> group)
    {
        if (group.Comp.Revelation is not { } ritual)
            return;
        if (!ValidRevelation(group, ritual))
        {
            CancelRevelation(group);
            return;
        }
        if (ritual.Started || ritual.Participants.Count < 2 ||
            ritual.Participants.Count != RevelationMembers(group))
            return;

        ritual.Started = true;
        ritual.ExpiresAt = _timing.CurTime + group.Comp.RevelationDelay + group.Comp.InvitationTimeout;
        var ev = new CultistRevelationDoAfterEvent { Group = GetNetEntity(group), RitualId = ritual.Id };
        if (!_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ritual.Leader, group.Comp.RevelationDelay,
                ev, ritual.Leader)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
                DamageThreshold = 0,
                MovementThreshold = Comp<CultistComponent>(ritual.Leader).MovementThreshold,
                AttemptFrequency = AttemptFrequency.EveryTick,
                CancelDuplicate = false,
                ForceVisible = true,
            }, out var id))
        {
            CancelRevelation(group);
            return;
        }
        if (ev.Handled)
            return;
        ritual.DoAfter = id;
        _audio.PlayPvs(group.Comp.RitualSound, ritual.Leader);
        _popup.PopupEntity(Loc.GetString("rmc-cultist-revelation-started", ("leader", ritual.Leader),
            ("count", ritual.Participants.Count), ("seconds", group.Comp.RevelationDelay.TotalSeconds)), ritual.Leader);
    }

    private bool ValidRevelation(Entity<CultistDevotionComponent> group, CultistRevelation ritual)
    {
        if (group.Comp.Revealed || !group.Comp.RevelationRequired || ritual.ExpiresAt <= _timing.CurTime ||
            group.Comp.Devotion < group.Comp.RevelationCost ||
            group.Comp.Leader != ritual.Leader || !TryComp<CultistComponent>(ritual.Leader, out var leader) ||
            !leader.Leader)
            return false;
        foreach (var (member, position) in ritual.Participants)
        {
            if (!Ready(member, group) || EntityManager.IsQueuedForDeletion(member) ||
                !Comp<CultistComponent>(member).Channeling || _standing.IsDown(member) ||
                _container.IsEntityInContainer(member) ||
                (member != ritual.Leader &&
                 !_interaction.InRangeUnobstructed(member, ritual.Leader, group.Comp.RevelationRange)) ||
                !position.TryDistance(EntityManager, Transform(member).Coordinates, out var moved) ||
                moved > Comp<CultistComponent>(member).MovementThreshold)
                return false;
        }
        return !ritual.Started || ritual.Participants.Count >= 2 &&
               ritual.Participants.Count == RevelationMembers(group);
    }

    private void CancelRevelation(Entity<CultistDevotionComponent> group)
    {
        if (group.Comp.Revelation is not { } ritual)
            return;
        ClearRevelation(group, ritual);
        if (ritual.DoAfter is { } id && _doAfter.IsRunning(id))
            _doAfter.Cancel(id);
        if (!TerminatingOrDeleted(ritual.Leader))
            _popup.PopupEntity(Loc.GetString("rmc-cultist-revelation-interrupted"), ritual.Leader, ritual.Leader);
    }

    private void ClearRevelation(Entity<CultistDevotionComponent> group, CultistRevelation ritual)
    {
        group.Comp.Revelation = null;
        foreach (var member in ritual.Participants.Keys)
        {
            if (TryComp<CultistComponent>(member, out var cultist))
                cultist.Channeling = false;
        }
    }
}
