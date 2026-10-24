using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Projectiles;

[ByRefEvent]
public readonly record struct RMCProjectileTargetModifiedEvent(EntityUid Projectile, ComponentRegistry Components);
