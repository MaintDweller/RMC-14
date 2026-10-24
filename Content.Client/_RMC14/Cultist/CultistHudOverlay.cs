using System.Numerics;
using Content.Client._RMC14.NightVision;
using Content.Shared._RMC14.Cultist;
using Content.Shared._RMC14.Mobs;
using Content.Shared._RMC14.Xenonids;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._RMC14.Cultist;

public sealed class CultistHudOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private readonly ContainerSystem _container;
    private readonly EntityLookupSystem _lookup;
    private readonly MobStateSystem _mobState;
    private readonly SpriteSystem _sprite;
    private readonly TransformSystem _transform;
    private readonly HashSet<EntityUid> _entities = new();
    private readonly ShaderInstance _shader;
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly SpriteSpecifier.Rsi CultistIcon =
        new(new ResPath("/Textures/_RMC14/Cultist/hud.rsi"), "cultist");
    private static readonly SpriteSpecifier.Rsi LeaderIcon =
        new(new ResPath("/Textures/_RMC14/Cultist/hud.rsi"), "leader");

    public override OverlaySpace Space => _overlays.HasOverlay<NightVisionOverlay>()
        ? OverlaySpace.WorldSpace
        : OverlaySpace.WorldSpaceBelowFOV;

    public CultistHudOverlay()
    {
        IoCManager.InjectDependencies(this);
        _container = _entity.System<ContainerSystem>();
        _lookup = _entity.System<EntityLookupSystem>();
        _mobState = _entity.System<MobStateSystem>();
        _sprite = _entity.System<SpriteSystem>();
        _transform = _entity.System<TransformSystem>();
        _shader = _prototypes.Index(UnshadedShader).Instance();
        ZIndex = 1;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_entity.HasComponent<XenoComponent>(_player.LocalEntity) &&
            !_entity.HasComponent<CultistComponent>(_player.LocalEntity) &&
            !_entity.HasComponent<CMGhostXenoHudComponent>(_player.LocalEntity) &&
            !(_entity.TryGetComponent<GhostComponent>(_player.LocalEntity, out var ghost) && ghost.CanGhostInteract))
            return;

        _entities.Clear();
        _lookup.GetEntitiesIntersecting(args.MapId, args.WorldAABB, _entities,
            LookupFlags.Dynamic | LookupFlags.Uncontained);
        var handle = args.WorldHandle;
        var rotation = Matrix3Helpers.CreateRotation(-(args.Viewport.Eye?.Rotation ?? Angle.Zero));
        handle.UseShader(_shader);
        foreach (var uid in _entities)
        {
            if (!_entity.TryGetComponent<CultistComponent>(uid, out var cultist) || _mobState.IsDead(uid) ||
                !_entity.TryGetComponent<TransformComponent>(uid, out var xform) ||
                _container.IsEntityOrParentInContainer(uid, xform: xform))
                continue;

            var position = _transform.GetWorldPosition(xform);
            handle.SetTransform(rotation * Matrix3x2.CreateTranslation(position));
            handle.DrawTexture(_sprite.Frame0(cultist.Leader ? LeaderIcon : CultistIcon), new Vector2(-0.5f, -0.5f));
        }

        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }
}
