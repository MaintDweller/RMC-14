using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Cultist;

[ByRefEvent]
public readonly record struct CultistLanguageSetupEvent;

public sealed partial class CultistFriendlyFireActionEvent : InstantActionEvent;

public sealed partial class CultistConvertActionEvent : EntityTargetActionEvent;

public sealed partial class CultistStunActionEvent : EntityTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class CultistConvertDoAfterEvent : CultistTargetDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CultistStunDoAfterEvent : CultistTargetDoAfterEvent
{
    public TimeSpan RootExpiresAt;
}

[Serializable, NetSerializable]
public abstract partial class CultistTargetDoAfterEvent : DoAfterEvent
{
    public NetCoordinates TargetPosition;
    public FixedPoint2 TargetDamage;

    public override DoAfterEvent Clone() => (CultistTargetDoAfterEvent) MemberwiseClone();
}

[ByRefEvent]
public readonly record struct CultistConvertedEvent(EntityUid Cultist, EntityUid Leader);
