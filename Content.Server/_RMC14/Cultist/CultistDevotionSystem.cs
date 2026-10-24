using Content.Server.Destructible;
using Content.Shared._RMC14.Cultist;
using Content.Shared._RMC14.Xenonids.Construction.ResinHole;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Parasite;
using Content.Shared._RMC14.Xenonids.Weeds;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Events;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._RMC14.Cultist;

public sealed partial class CultistDevotionSystem : EntitySystem
{
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedCultistRitesSystem _rites = default!;
    [Dependency] private readonly MobThresholdSystem _threshold = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedXenoWeedsSystem _weeds = default!;

    private readonly Dictionary<EntityUid, Sabotage> _sabotage = new();
    private readonly Dictionary<EntityUid, EntityUid> _towerAttackers = new();
    private readonly Dictionary<EntityUid, Rescue> _rescues = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoResinHoleComponent, CultistTrapPreparedEvent>(OnTrapPrepared);
        SubscribeLocalEvent<InfectableComponent, CultistParasiteImplantedEvent>(OnImplanted);
        InitializeSabotage();
        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>(OnRescueDamage,
            before: new[] { typeof(DestructibleSystem) });
        SubscribeLocalEvent<CultistComponent, PullStartedMessage>(OnRescuePullStarted);
        SubscribeLocalEvent<CultistComponent, PullStoppedMessage>(OnRescuePullStopped);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnTrapPrepared(Entity<XenoResinHoleComponent> ent, ref CultistTrapPreparedEvent args)
    {
        if (_hive.FromSameHive(args.User, ent.Owner))
            _rites.Award(args.User, ent, CultistDevotionSource.Trap);
    }

    private void OnImplanted(Entity<InfectableComponent> ent, ref CultistParasiteImplantedEvent args)
    {
        if (args.User is { } user && !HasComp<CultistComponent>(ent) &&
            !_mobState.IsDead(ent) && _hive.FromSameHive(user, args.Parasite) && HasComp<VictimInfectedComponent>(ent))
            _rites.Award(user, ent, CultistDevotionSource.Infection);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _sabotage.Clear();
        _towerAttackers.Clear();
        _rescues.Clear();
        _nextUpdate = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        foreach (var (target, sabotage) in _sabotage)
        {
            if (TerminatingOrDeleted(target) || EntityManager.IsQueuedForDeletion(target) ||
                Transform(target).MapUid == null || !IsSabotaged(target, sabotage.Source) ||
                _rites.GetGroup(sabotage.Actor)?.Owner != sabotage.Group)
            {
                _sabotage.Remove(target);
                continue;
            }
            if (_timing.CurTime < sabotage.FinishesAt)
                continue;
            _rites.Award(sabotage.Actor, target, sabotage.Source);
            _sabotage.Remove(target);
        }
        UpdateRescues();
    }

    private readonly record struct Sabotage(
        EntityUid Actor,
        EntityUid Group,
        TimeSpan FinishesAt,
        CultistDevotionSource Source);

    private sealed class Rescue(EntityUid group, EntityCoordinates position, TimeSpan expiresAt)
    {
        public readonly EntityUid Group = group;
        public readonly EntityCoordinates Position = position;
        public readonly TimeSpan ExpiresAt = expiresAt;
        public EntityUid? Rescuer;
        public TimeSpan? SafeSince;
        public FixedPoint2 EnemyDamage;
        public bool Eligible;
        public bool Evacuated;
    }
}
