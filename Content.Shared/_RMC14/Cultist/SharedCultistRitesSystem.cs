using Content.Shared._RMC14.Dialog;
using Content.Shared._RMC14.Gibbing;
using Content.Shared._RMC14.Synth;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Parasite;
using Content.Shared.Administration.Logs;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Jittering;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistRitesSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedCultistSystem _cultist = default!;
    [Dependency] private readonly DialogSystem _dialog = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedXenoParasiteSystem _parasites = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly RMCGibSystem _rmcGib = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private TimeSpan _nextUpdate;
    private static readonly EntProtoId DevotionSettings = "RMCCultistDevotion";

    public override void Initialize()
    {
        SubscribeLocalEvent<CultistComponent, CultistJoinedEvent>(OnJoined);
        SubscribeLocalEvent<CultistComponent, CultistRitesActionEvent>(OnRitesAction);
        SubscribeLocalEvent<CultistComponent, CultistRiteChoiceEvent>(OnRiteChoice);
        SubscribeLocalEvent<CultistComponent, CultistSuccessionDoAfterEvent>(OnSuccessionDoAfter);
        SubscribeLocalEvent<CultistComponent, DoAfterAttemptEvent<CultistSuccessionDoAfterEvent>>(OnSuccessionAttempt);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        InitializeParasiteRite();
        InitializeSacrificeRite();
        InitializeRevelationRite();
        InitializeRiteParticipants();
        InitializeRobes();
    }

    private void OnJoined(Entity<CultistComponent> ent, ref CultistJoinedEvent args)
    {
        if (_net.IsClient || _hive.GetHive(ent.Owner) is not { } hive)
            return;

        if (!HasComp<CultistDevotionComponent>(hive))
            EntityManager.AddComponents(hive, _prototypes.Index(DevotionSettings));
        var group = Comp<CultistDevotionComponent>(hive);
        group.Members.RemoveWhere(member => TerminatingOrDeleted(member) ||
            !HasComp<CultistComponent>(member) || _hive.GetHive(member)?.Owner != hive.Owner);
        group.Members.Add(ent);
        if (!group.Revealed && group.Members.Count >= group.ConcealedMemberLimit)
        {
            group.RevelationRequired = true;
            Dirty(hive, group);
        }
        if (group.Revealed && !_mobState.IsDead(ent))
            EquipRobes(ent, (hive, group));
        if (!ent.Comp.Leader)
            return;

        if (group.Leader is { } previous && previous != ent.Owner && TryComp<CultistComponent>(previous, out var old))
            _cultist.SetLeader((previous, old), false);
        group.Leader = ent;
        group.LeaderAbsentSince = null;
    }

    public Entity<CultistDevotionComponent>? GetGroup(EntityUid member)
    {
        if (_hive.GetHive(member) is { } hive && TryComp<CultistDevotionComponent>(hive, out var group))
            return (hive, group);
        return null;
    }

    public bool Award(EntityUid actor, EntityUid target, CultistDevotionSource source)
    {
        if (_net.IsClient || TerminatingOrDeleted(actor) || TerminatingOrDeleted(target) ||
            !HasComp<CultistComponent>(actor) ||
            GetGroup(actor) is not { } group || !group.Comp.Rewards.TryGetValue(source, out var amount) || amount <= 0)
            return false;

        var key = source is CultistDevotionSource.Trap or CultistDevotionSource.Sabotage or
            CultistDevotionSource.SensorSabotage
            ? target
            : CompOrNull<MindContainerComponent>(target)?.Mind ?? target;
        if (group.Comp.RewardReadyAt.TryGetValue((source, key), out var readyAt) && readyAt > _timing.CurTime)
            return false;

        var gained = Math.Min(amount, Math.Max(0, group.Comp.Capacity - group.Comp.Devotion));
        if (gained == 0)
            return false;

        var actorKey = CompOrNull<MindContainerComponent>(actor)?.Mind ?? actor;
        if (source == CultistDevotionSource.Trap)
        {
            if (group.Comp.TrapRewards.TryGetValue(actorKey, out var ready) && ready > _timing.CurTime)
                return false;
            group.Comp.TrapRewards[actorKey] = _timing.CurTime + group.Comp.TrapRewardCooldown;
        }

        group.Comp.RewardReadyAt[(source, key)] = _timing.CurTime + group.Comp.RewardCooldown;
        group.Comp.Devotion += gained;
        Dirty(group);
        _popup.PopupEntity(Loc.GetString("rmc-cultist-devotion-gained", ("amount", gained),
            ("balance", group.Comp.Devotion)), actor, actor);
        _adminLog.Add(LogType.Mind, LogImpact.Low,
            $"{ToPrettyString(actor):actor} earned {gained} cult devotion for {source} " +
            $"on {ToPrettyString(target):target}");
        return true;
    }

    private void OnRitesAction(Entity<CultistComponent> ent, ref CultistRitesActionEvent args)
    {
        if (args.Handled || _net.IsClient || !_cultist.CanUse(ent) || GetGroup(ent) is not { } group)
            return;

        args.Handled = true;
        OpenRitesMenu(ent, group);
    }

    private void OpenRitesMenu(Entity<CultistComponent> ent, Entity<CultistDevotionComponent> group,
        bool showHelp = false)
    {
        var options = new List<DialogOption>();
        var ritual = group.Comp.Ritual;
        var id = ritual?.Id ?? group.Comp.NextRitualId;
        if (ritual == null && !ent.Comp.Leader && !HasComp<SynthComponent>(ent))
            options.Add(new DialogOption(Loc.GetString("rmc-cultist-rite-nominate"),
                new CultistRiteChoiceEvent(GetNetEntity(group), id, CultistRiteChoice.Nominate)));
        if (ritual != null && !ritual.Started && ritual.Candidate != ent.Owner)
            options.Add(new DialogOption(Loc.GetString("rmc-cultist-rite-support", ("candidate", ritual.Candidate)),
                new CultistRiteChoiceEvent(GetNetEntity(group), id, CultistRiteChoice.Support)));
        if (ritual != null && (ritual.Candidate == ent.Owner || ritual.Supporter == ent.Owner))
            options.Add(new DialogOption(Loc.GetString("rmc-cultist-rite-cancel"),
                new CultistRiteChoiceEvent(GetNetEntity(group), id, CultistRiteChoice.Cancel)));

        options.Add(new DialogOption(Loc.GetString("rmc-cultist-rites-help-button"),
            new CultistRiteChoiceEvent(GetNetEntity(group), id, CultistRiteChoice.Help)));
        options.Add(new DialogOption(Loc.GetString("rmc-cultist-rite-parasite", ("cost", group.Comp.ParasiteCost)),
            new CultistRiteChoiceEvent(GetNetEntity(group), id, CultistRiteChoice.Parasite),
            description: Loc.GetString("rmc-cultist-rite-parasite-description",
                ("seconds", group.Comp.ParasiteRitualDelay.TotalSeconds))));
        options.Add(new DialogOption(Loc.GetString("rmc-cultist-rite-sacrifice"),
            new CultistRiteChoiceEvent(GetNetEntity(group), 0, CultistRiteChoice.Sacrifice)));
        AddRevelationOptions(ent, group, options);
        var settings = group.Comp;
        var message = ritual == null ? Loc.GetString("rmc-cultist-rites-idle") :
            Loc.GetString(ritual.Started ? "rmc-cultist-rites-channeling" : "rmc-cultist-rites-waiting",
                ("candidate", ritual.Candidate));
        if (showHelp)
            options = HelpOptions(group);
        else
            message += "\n" + RevelationStatus(group);
        _dialog.OpenOptions(ent, Loc.GetString("rmc-cultist-rites-title", ("balance", settings.Devotion),
            ("capacity", settings.Capacity)), options, showHelp ? "" : message, enableSearch: false);
    }

    private List<DialogOption> HelpOptions(Entity<CultistDevotionComponent> group)
    {
        var settings = group.Comp;
        var help = Loc.GetString("rmc-cultist-rites-help", ("cost", settings.UsurpationCost),
            ("transfer", settings.TransferDelay.TotalSeconds), ("absence", settings.AbsentLeaderDelay.TotalMinutes),
            ("succession", settings.SuccessionDelay.TotalSeconds), ("solitary", settings.SolitaryDelay.TotalSeconds),
            ("usurpation", settings.UsurpationDelay.TotalSeconds),
            ("cooldown", settings.UsurpationCooldown.TotalMinutes));
        var rewards = settings.Rewards;
        var rewardHelp = Loc.GetString("rmc-cultist-devotion-help",
            ("conversion", rewards.GetValueOrDefault(CultistDevotionSource.Conversion)),
            ("trap", rewards.GetValueOrDefault(CultistDevotionSource.Trap)),
            ("infection", rewards.GetValueOrDefault(CultistDevotionSource.Infection)),
            ("xeno", rewards.GetValueOrDefault(CultistDevotionSource.XenoRescue)),
            ("sabotage", rewards.GetValueOrDefault(CultistDevotionSource.Sabotage)),
            ("sensor", rewards.GetValueOrDefault(CultistDevotionSource.SensorSabotage)),
            ("sacrifice", rewards.GetValueOrDefault(CultistDevotionSource.Sacrifice)),
            ("sacrificeDelay", settings.SacrificeDelay.TotalSeconds),
            ("rewardCooldown", settings.RewardCooldown.TotalMinutes),
            ("sabotageDelay", settings.SabotageDelay.TotalSeconds),
            ("trapCooldown", settings.TrapRewardCooldown.TotalSeconds), ("damage", settings.RescueDamageFraction * 100),
            ("distance", settings.RescueDistance), ("safety", settings.RescueSafetyDelay.TotalSeconds));
        return
        [
            new DialogOption(Loc.GetString("rmc-cultist-rites-succession-help"), description: help),
            new DialogOption(Loc.GetString("rmc-cultist-rites-rewards-help"), description: rewardHelp),
            new DialogOption(Loc.GetString("rmc-cultist-rite-revelation"), description: RevelationDescription(group)),
            new DialogOption(Loc.GetString("rmc-cultist-rites-back"),
                new CultistRiteChoiceEvent(GetNetEntity(group), 0, CultistRiteChoice.Back)),
        ];
    }

    private void OnRiteChoice(Entity<CultistComponent> ent, ref CultistRiteChoiceEvent args)
    {
        if (_net.IsClient || !_cultist.CanUse(ent) || GetGroup(ent) is not { } group ||
            GetEntity(args.Group) != group.Owner)
            return;

        if (args.Choice is CultistRiteChoice.Help or CultistRiteChoice.Back)
        {
            OpenRitesMenu(ent, group, args.Choice == CultistRiteChoice.Help);
            return;
        }

        if (args.Choice == CultistRiteChoice.Parasite)
        {
            if (!StartParasiteRite(ent, group))
                _popup.PopupEntity(Loc.GetString("rmc-cultist-parasite-rite-unavailable",
                    ("cost", group.Comp.ParasiteCost)), ent, ent);
            return;
        }

        if (args.Choice == CultistRiteChoice.Sacrifice)
        {
            OpenSacrificeMenu(ent, group);
            return;
        }

        if (args.Choice is CultistRiteChoice.Revelation or CultistRiteChoice.JoinRevelation or
            CultistRiteChoice.CancelRevelation)
        {
            OnRevelationChoice(ent, group, args);
            return;
        }

        if (args.Choice == CultistRiteChoice.Nominate)
        {
            if (group.Comp.NextRitualId == args.RitualId && !Nominate(ent))
                _popup.PopupEntity(Loc.GetString("rmc-cultist-rite-requirements"), ent, ent);
            return;
        }

        if (group.Comp.Ritual is not { } ritual || ritual.Id != args.RitualId)
            return;
        if (args.Choice == CultistRiteChoice.Support)
        {
            if (!Support(ent))
                _popup.PopupEntity(Loc.GetString("rmc-cultist-rite-requirements"), ent, ent);
        }
        else if (args.Choice == CultistRiteChoice.Cancel &&
                 (ritual.Candidate == ent.Owner || ritual.Supporter == ent.Owner))
            CancelRitual(group);
    }

    private bool Ready(EntityUid uid, Entity<CultistDevotionComponent> group)
    {
        return !TerminatingOrDeleted(uid) && HasComp<CultistComponent>(uid) &&
               GetGroup(uid)?.Owner == group.Owner && _cultist.CanUse(uid) &&
               !HasComp<SleepingComponent>(uid);
    }

    private bool HasActiveLeader(Entity<CultistDevotionComponent> group)
    {
        return group.Comp.Leader is { } leader && !TerminatingOrDeleted(leader) &&
               HasComp<CultistComponent>(leader) && GetGroup(leader)?.Owner == group.Owner &&
               !_mobState.IsDead(leader) &&
               (group.Comp.LeaderAbsentSince == null ||
                group.Comp.LeaderAbsentSince + group.Comp.AbsentLeaderDelay > _timing.CurTime);
    }

    private int LivingMembers(Entity<CultistDevotionComponent> group)
    {
        var count = 0;
        foreach (var member in group.Comp.Members)
        {
            if (!TerminatingOrDeleted(member) && HasComp<CultistComponent>(member) &&
                GetGroup(member)?.Owner == group.Owner && !_mobState.IsDead(member) &&
                (member != group.Comp.Leader || HasActiveLeader(group)))
                count++;
        }
        return count;
    }

    private void CancelRitual(Entity<CultistDevotionComponent> group)
    {
        if (group.Comp.Ritual is not { } ritual)
            return;
        group.Comp.Ritual = null;
        if (TryComp<CultistComponent>(ritual.Candidate, out var candidate))
            candidate.Channeling = false;
        if (ritual.Supporter is { } supporter && TryComp<CultistComponent>(supporter, out var helper))
            helper.Channeling = false;
        if (!TerminatingOrDeleted(ritual.Candidate))
            _popup.PopupEntity(Loc.GetString("rmc-cultist-rite-interrupted"), ritual.Candidate, ritual.Candidate);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        if (_net.IsClient)
            return;
        var query = EntityQueryEnumerator<CultistDevotionComponent>();
        while (query.MoveNext(out var uid, out var group))
        {
            CancelRitual((uid, group));
            CancelSacrifice((uid, group));
            CancelRevelation((uid, group));
            RemCompDeferred<CultistDevotionComponent>(uid);
        }
        _sacrificeTargets.Clear();
        _nextUpdate = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient || _timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<CultistDevotionComponent>();
        while (query.MoveNext(out var uid, out var group))
        {
            UpdateSacrifice((uid, group));
            UpdateRevelation((uid, group));
            if (group.Leader is { } leader && !TerminatingOrDeleted(leader) && HasComp<ActorComponent>(leader))
                group.LeaderAbsentSince = null;
            else
                group.LeaderAbsentSince ??= _timing.CurTime;
            if (group.Ritual is { } ritual &&
                (ritual.ExpiresAt <= _timing.CurTime || !Ready(ritual.Candidate, (uid, group))))
                CancelRitual((uid, group));
        }
    }
}
