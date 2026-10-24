using Content.Shared._RMC14.Xenonids.Parasite;
using Content.Shared.Database;
using Content.Shared.DoAfter;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistRitesSystem
{
    private const string ParasiteSlot = "parasite";

    private void InitializeParasiteRite()
    {
        SubscribeLocalEvent<CultistComponent, CultistParasiteRiteDoAfterEvent>(OnParasiteRiteDoAfter);
        SubscribeLocalEvent<CultistComponent, DoAfterAttemptEvent<CultistParasiteRiteDoAfterEvent>>(
            OnParasiteRiteAttempt);
    }

    private bool StartParasiteRite(Entity<CultistComponent> ent, Entity<CultistDevotionComponent> group)
    {
        if (ent.Comp.Channeling || ent.Comp.ParasiteRitual != null || !Ready(ent, group) ||
            group.Comp.Devotion < group.Comp.ParasiteCost ||
            group.Comp.Ritual is { } succession &&
            (succession.Candidate == ent.Owner || succession.Supporter == ent.Owner))
            return false;

        var ev = new CultistParasiteRiteDoAfterEvent { Group = GetNetEntity(group) };
        ent.Comp.Channeling = true;
        if (!_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ent, group.Comp.ParasiteRitualDelay, ev, ent)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
                AttemptFrequency = AttemptFrequency.EveryTick,
                CancelDuplicate = false,
            }, out var id))
        {
            ent.Comp.Channeling = false;
            return false;
        }

        if (ev.Handled)
            return true;

        ent.Comp.ParasiteRitual = id;
        _audio.PlayPvs(group.Comp.RitualSound, ent);
        _popup.PopupEntity(Loc.GetString("rmc-cultist-parasite-rite-started",
            ("seconds", group.Comp.ParasiteRitualDelay.TotalSeconds)), ent);
        return true;
    }

    private bool ValidParasiteRite(Entity<CultistComponent> ent, NetEntity hive,
        out Entity<CultistDevotionComponent> group)
    {
        group = default;
        if (GetGroup(ent) is not { } current || GetNetEntity(current) != hive || !Ready(ent, current) ||
            current.Comp.Devotion < current.Comp.ParasiteCost)
            return false;

        group = current;
        return true;
    }

    private void OnParasiteRiteAttempt(Entity<CultistComponent> ent,
        ref DoAfterAttemptEvent<CultistParasiteRiteDoAfterEvent> args)
    {
        if (_net.IsClient)
            return;

        if (!ent.Comp.Channeling ||
            ent.Comp.ParasiteRitual is { } ritual && ritual != args.DoAfter.Id ||
            !ValidParasiteRite(ent, args.Event.Group, out _))
            args.Cancel();
    }

    private void OnParasiteRiteDoAfter(Entity<CultistComponent> ent, ref CultistParasiteRiteDoAfterEvent args)
    {
        if (_net.IsClient || args.Handled || !ent.Comp.Channeling ||
            ent.Comp.ParasiteRitual is { } ritual && ritual != args.DoAfter.Id)
            return;

        args.Handled = true;
        ent.Comp.ParasiteRitual = null;
        ent.Comp.Channeling = false;
        if (args.Cancelled || !ValidParasiteRite(ent, args.Group, out var group))
        {
            _popup.PopupEntity(Loc.GetString("rmc-cultist-parasite-rite-interrupted"), ent, ent);
            return;
        }

        var capsule = Spawn(group.Comp.ParasiteContainer, Transform(ent).Coordinates);
        if (_itemSlots.GetItemOrNull(capsule, ParasiteSlot) is not { } parasite ||
            !TryComp<XenoParasiteComponent>(parasite, out var parasiteComp))
        {
            QueueDel(capsule);
            Log.Error($"Cultist parasite container {group.Comp.ParasiteContainer} has no parasite in {ParasiteSlot}.");
            return;
        }

        _hive.SetHive(parasite, group.Owner);
        _parasites.SetInfector((parasite, parasiteComp), ent);
        group.Comp.Devotion -= group.Comp.ParasiteCost;
        Dirty(group);
        _hands.PickupOrDrop(ent, capsule);
        _popup.PopupEntity(Loc.GetString("rmc-cultist-parasite-rite-completed",
            ("cost", group.Comp.ParasiteCost), ("balance", group.Comp.Devotion)), ent, ent);
        _adminLog.Add(LogType.Mind, LogImpact.Medium,
            $"{ToPrettyString(ent):actor} spent {group.Comp.ParasiteCost} cult devotion to create " +
            $"{ToPrettyString(capsule):container} with {ToPrettyString(parasite):parasite}");
    }
}
