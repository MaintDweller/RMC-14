using Content.Shared._RMC14.Marines.Skills;
using Content.Shared._RMC14.Slow;
using Content.Shared._RMC14.Stun;
using Content.Shared._RMC14.Synth;
using Content.Shared._RMC14.Xenonids;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly CultistInteractionSystem _rules = default!;
    [Dependency] private readonly RMCSizeStunSystem _sizeStun = default!;
    [Dependency] private readonly SkillsSystem _skills = default!;
    [Dependency] private readonly RMCSlowSystem _slow = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly ProtoId<SpeciesPrototype> HumanSpecies = "Human";
    private static readonly EntProtoId CultistEffects = "RMCCultistEffects";
    private static readonly EntProtoId LeaderEffects = "RMCCultistLeaderEffects";

    public override void Initialize()
    {
        SubscribeLocalEvent<CultistComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CultistComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<CultistComponent, CultistConvertActionEvent>(OnConvertAction);
        SubscribeLocalEvent<CultistComponent, CultistConvertDoAfterEvent>(OnConvertDoAfter);
        SubscribeLocalEvent<CultistComponent, DoAfterAttemptEvent<CultistConvertDoAfterEvent>>(OnConvertAttempt);
        SubscribeLocalEvent<CultistComponent, CultistStunActionEvent>(OnStunAction);
        SubscribeLocalEvent<CultistComponent, CultistStunDoAfterEvent>(OnStunDoAfter);
        SubscribeLocalEvent<CultistComponent, DoAfterAttemptEvent<CultistStunDoAfterEvent>>(OnStunAttempt);
        InitializeCombat();
        InitializeEnvironment();
        InitializeLanguage();
    }

    private void OnStartup(Entity<CultistComponent> ent, ref ComponentStartup args)
    {
        if (_net.IsClient)
            return;

        EntityManager.AddComponents(ent, ent.Comp.Components);
        _actions.AddAction(ent, ref ent.Comp.FriendlyFireActionEntity, ent.Comp.FriendlyFireAction);
        _actions.AddAction(ent, ref ent.Comp.RitesActionEntity, ent.Comp.RitesAction);
        _actions.SetToggled(ent.Comp.FriendlyFireActionEntity, ent.Comp.AllowFriendlyFire);
        if (ent.Comp.Leader)
            AddLeaderActions(ent);

        _movementSpeed.RefreshMovementSpeedModifiers(ent);
        Dirty(ent);
    }

    private void OnShutdown(Entity<CultistComponent> ent, ref ComponentShutdown args)
    {
        if (_net.IsClient)
            return;

        var leaving = new CultistLeftEvent();
        RaiseLocalEvent(ent, ref leaving);
        _actions.RemoveAction(ent.Comp.ConvertActionEntity);
        _actions.RemoveAction(ent.Comp.StunActionEntity);
        _actions.RemoveAction(ent.Comp.FriendlyFireActionEntity);
        _actions.RemoveAction(ent.Comp.RitesActionEntity);
        if (ent.Comp.ParasiteRitual is { } parasiteRitual)
            _doAfter.Cancel(parasiteRitual);
        RemComp<CultistLanguageComponent>(ent);
    }

    public void MakeCultist(EntityUid uid, EntityUid hive, bool leader = false)
    {
        _hive.SetHive(uid, hive);
        EnsureComp<XenoFriendlyComponent>(uid);
        if (!HasComp<CultistComponent>(uid))
            EntityManager.AddComponents(uid, _prototypes.Index(leader ? LeaderEffects : CultistEffects));

        var cultist = Comp<CultistComponent>(uid);
        Dirty(uid, cultist);
        var language = new CultistLanguageSetupEvent();
        RaiseLocalEvent(uid, ref language);
        var joined = new CultistJoinedEvent();
        RaiseLocalEvent(uid, ref joined);
    }

    public void SetLeader(Entity<CultistComponent> ent, bool leader)
    {
        if (_net.IsClient || ent.Comp.Leader == leader)
            return;

        ent.Comp.Leader = leader;
        if (leader)
            AddLeaderActions(ent);
        else
        {
            _actions.RemoveAction(ent.Comp.ConvertActionEntity);
            _actions.RemoveAction(ent.Comp.StunActionEntity);
            ent.Comp.ConvertActionEntity = null;
            ent.Comp.StunActionEntity = null;
        }
        Dirty(ent);
    }

    private void AddLeaderActions(Entity<CultistComponent> ent)
    {
        _actions.AddAction(ent, ref ent.Comp.ConvertActionEntity, ent.Comp.ConvertAction);
        _actions.AddAction(ent, ref ent.Comp.StunActionEntity, ent.Comp.StunAction);
        if (_hive.GetHive(ent.Owner) is not { } hive || !TryComp<CultistDevotionComponent>(hive, out var devotion))
            return;

        if (devotion.ConversionReadyAt > _timing.CurTime)
            _actions.SetCooldown(ent.Comp.ConvertActionEntity, devotion.ConversionReadyAt - _timing.CurTime);
        if (devotion.StunReadyAt > _timing.CurTime)
            _actions.SetCooldown(ent.Comp.StunActionEntity, devotion.StunReadyAt - _timing.CurTime);
    }

    public bool CanUse(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) && _mobState.IsAlive(uid) &&
               !HasComp<RMCUnconsciousComponent>(uid) && !HasComp<RMCDazedComponent>(uid) &&
               _actionBlocker.CanInteract(uid, null);
    }

    public bool CanTarget(Entity<CultistComponent> cultist, EntityUid target, bool popup = false)
    {
        if (!cultist.Comp.Leader || !CanUse(cultist) || TerminatingOrDeleted(target) ||
            !_mobState.IsAlive(target) || HasComp<RMCUnconsciousComponent>(target))
            return false;

        if (HasComp<SynthComponent>(target) ||
            !TryComp<HumanoidAppearanceComponent>(target, out var appearance) || appearance.Species != HumanSpecies)
            return false;

        if (_hive.GetHive(cultist.Owner) is not { } hive ||
            hive.Comp.CurrentQueen is not { } queen || !_mobState.IsAlive(queen))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("rmc-cultist-no-queen"), cultist, cultist);
            return false;
        }

        if (_hive.FromSameHive(cultist.Owner, target) ||
            !_interaction.InRangeUnobstructed(cultist.Owner, target, cultist.Comp.AbilityRange))
            return false;

        if (_skills.HasAnySkills(target, cultist.Comp.ImmuneSkills))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("rmc-cultist-strong-mind"), cultist, cultist);
            return false;
        }

        return true;
    }
}
