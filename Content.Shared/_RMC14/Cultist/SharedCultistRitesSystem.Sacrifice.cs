using Content.Shared._RMC14.Dialog;
using Content.Shared._RMC14.Synth;
using Content.Shared._RMC14.Xenonids;
using Content.Shared._RMC14.Xenonids.Parasite;
using Content.Shared.Body.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistRitesSystem
{
    private static readonly ProtoId<SpeciesPrototype> SacrificeSpecies = "Human";
    private readonly HashSet<Entity<HumanoidAppearanceComponent>> _sacrificeTargets = new();

    private void OpenSacrificeMenu(Entity<CultistComponent> ent, Entity<CultistDevotionComponent> group)
    {
        if (group.Comp.Sacrifice is { } pending &&
            (pending.ExpiresAt <= _timing.CurTime || !ValidSacrifice(group, pending)))
            CancelSacrifice(group);
        var options = new List<DialogOption>();
        if (group.Comp.Sacrifice is { } ritual)
        {
            if (!ritual.Started && ritual.Initiator != ent.Owner)
                options.Add(new DialogOption(Loc.GetString("rmc-cultist-sacrifice-join", ("victim", ritual.Victim)),
                    new CultistSacrificeChoiceEvent(GetNetEntity(group), GetNetEntity(ritual.Victim), ritual.Id,
                        CultistSacrificeChoice.Join)));
            if (ritual.Initiator == ent.Owner || ritual.Supporter == ent.Owner)
                options.Add(new DialogOption(Loc.GetString("rmc-cultist-sacrifice-cancel"),
                    new CultistSacrificeChoiceEvent(GetNetEntity(group), GetNetEntity(ritual.Victim), ritual.Id,
                        CultistSacrificeChoice.Cancel)));
        }
        else
        {
            _sacrificeTargets.Clear();
            _lookup.GetEntitiesInRange(Transform(ent).Coordinates, group.Comp.RitualRange, _sacrificeTargets);
            foreach (var target in _sacrificeTargets)
            {
                if (!CanSacrificeVictim(target, group) || !InRitualRange(ent, target, group) ||
                    IsSacrificeReserved(target))
                    continue;
                options.Add(new DialogOption(Loc.GetString("rmc-cultist-sacrifice-offer", ("victim", target.Owner)),
                    new CultistSacrificeChoiceEvent(GetNetEntity(group), GetNetEntity(target),
                        group.Comp.NextSacrificeId, CultistSacrificeChoice.Offer)));
            }
        }
        options.Add(new DialogOption(Loc.GetString("rmc-cultist-rites-back"),
            new CultistRiteChoiceEvent(GetNetEntity(group), 0, CultistRiteChoice.Back)));
        _dialog.OpenOptions(ent, Loc.GetString("rmc-cultist-rite-sacrifice"), options,
            Loc.GetString("rmc-cultist-sacrifice-description", ("seconds", group.Comp.SacrificeDelay.TotalSeconds),
                ("reward", group.Comp.Rewards.GetValueOrDefault(CultistDevotionSource.Sacrifice))),
            enableSearch: false);
    }

    private void OnSacrificeChoice(Entity<CultistComponent> ent, ref CultistSacrificeChoiceEvent args)
    {
        if (_net.IsClient || _cultist.GetGroup(ent.AsNullable()) is not { } group ||
            GetEntity(args.Group) != group.Owner ||
            !Ready(ent, group))
            return;

        if (!TryGetEntity(args.Victim, out var victimUid) || victimUid is not { } victim)
            return;
        if (args.Choice == CultistSacrificeChoice.Offer)
        {
            if (args.RitualId != group.Comp.NextSacrificeId || !OfferSacrifice(ent, group, victim))
                SacrificeUnavailable(ent);
            else
                OpenSacrificeMenu(ent, group);
            return;
        }

        if (group.Comp.Sacrifice is not { } ritual || ritual.Id != args.RitualId || ritual.Victim != victim)
            return;
        if (args.Choice == CultistSacrificeChoice.Join)
        {
            if (!CanBeginSacrifice(ent, group) || ritual.Initiator == ent.Owner || ritual.Started ||
                ritual.ExpiresAt <= _timing.CurTime || !ValidSacrifice(group, ritual) ||
                !InRitualRange(ent, victim, group))
            {
                SacrificeUnavailable(ent);
                return;
            }
            ritual.Supporter = ent;
            StartSacrifice(group, ritual);
        }
        else if (args.Choice == CultistSacrificeChoice.Cancel &&
                 (ritual.Initiator == ent.Owner || ritual.Supporter == ent.Owner))
            CancelSacrifice(group);
    }

    private bool OfferSacrifice(Entity<CultistComponent> ent, Entity<CultistDevotionComponent> group, EntityUid victim)
    {
        if (!CanBeginSacrifice(ent, group) || group.Comp.Sacrifice != null ||
            !CanSacrificeVictim(victim, group) || !InRitualRange(ent, victim, group) || IsSacrificeReserved(victim))
            return false;

        group.Comp.Sacrifice = new CultistSacrifice
        {
            Id = ++group.Comp.NextSacrificeId,
            Initiator = ent,
            Victim = victim,
            ExpiresAt = _timing.CurTime + group.Comp.InvitationTimeout,
        };
        ent.Comp.Channeling = true;
        _popup.PopupEntity(Loc.GetString("rmc-cultist-sacrifice-invitation", ("cultist", ent.Owner),
            ("victim", victim)), victim);
        return true;
    }

    private bool CanBeginSacrifice(Entity<CultistComponent> ent, Entity<CultistDevotionComponent> group)
    {
        return !ent.Comp.Channeling && Ready(ent, group) && !_container.IsEntityInContainer(ent) &&
               (group.Comp.Ritual is not { } succession ||
                succession.Candidate != ent.Owner && succession.Supporter != ent.Owner);
    }

    private bool CanSacrificeVictim(EntityUid victim, Entity<CultistDevotionComponent> group)
    {
        return !TerminatingOrDeleted(victim) && !EntityManager.IsQueuedForDeletion(victim) &&
               _mobState.IsAlive(victim) && _standing.IsDown(victim) && HasComp<BodyComponent>(victim) &&
               TryComp<HumanoidAppearanceComponent>(victim, out var appearance) &&
               appearance.Species == SacrificeSpecies && !HasComp<SynthComponent>(victim) &&
               !HasComp<XenoComponent>(victim) && !HasComp<CultistComponent>(victim) &&
               !HasComp<GodmodeComponent>(victim) && !_hive.IsMember(victim, group.Comp.Hive) &&
               !_container.IsEntityInContainer(victim) && !HasComp<VictimInfectedComponent>(victim) &&
               (!TryComp<InfectableComponent>(victim, out var infectable) || !infectable.BeingInfected);
    }

    private bool IsSacrificeReserved(EntityUid victim)
    {
        var query = EntityQueryEnumerator<CultistDevotionComponent>();
        while (query.MoveNext(out _, out var group))
        {
            if (group.Sacrifice?.Victim == victim)
                return true;
        }
        return false;
    }

    private void SacrificeUnavailable(EntityUid participant)
    {
        _popup.PopupEntity(Loc.GetString("rmc-cultist-sacrifice-unavailable"), participant, participant);
    }
}
