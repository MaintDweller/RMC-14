using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class CultistRitesActionEvent : InstantActionEvent;

[ByRefEvent]
public readonly record struct CultistJoinedEvent;

[ByRefEvent]
public readonly record struct CultistLeftEvent;

[Serializable, NetSerializable]
public sealed class CultistRiteChoiceEvent(NetEntity group, int ritualId, CultistRiteChoice choice) : EntityEventArgs
{
    public NetEntity Group = group;
    public int RitualId = ritualId;
    public CultistRiteChoice Choice = choice;
}

[Serializable, NetSerializable]
public enum CultistRiteChoice : byte
{
    Nominate,
    Support,
    Cancel,
    Help,
    Back,
    Parasite,
    Sacrifice,
    Revelation,
    JoinRevelation,
    CancelRevelation,
}

[Serializable, NetSerializable]
public sealed partial class CultistRevelationDoAfterEvent : SimpleDoAfterEvent
{
    public NetEntity Group;
    public int RitualId;
}

[Serializable, NetSerializable]
public sealed partial class CultistSuccessionDoAfterEvent : SimpleDoAfterEvent
{
    public NetEntity Group;
    public int RitualId;
}

[Serializable, NetSerializable]
public sealed partial class CultistParasiteRiteDoAfterEvent : SimpleDoAfterEvent
{
    public NetEntity Group;
}

[Serializable, NetSerializable]
public sealed class CultistSacrificeChoiceEvent(NetEntity group, NetEntity victim, int ritualId,
    CultistSacrificeChoice choice) : EntityEventArgs
{
    public NetEntity Group = group;
    public NetEntity Victim = victim;
    public int RitualId = ritualId;
    public CultistSacrificeChoice Choice = choice;
}

[Serializable, NetSerializable]
public enum CultistSacrificeChoice : byte
{
    Offer,
    Join,
    Cancel,
}

[Serializable, NetSerializable]
public sealed partial class CultistSacrificeDoAfterEvent : SimpleDoAfterEvent
{
    public NetEntity Group;
    public int RitualId;
}

[ByRefEvent]
public readonly record struct CultistTrapPreparedEvent(EntityUid User);

[ByRefEvent]
public readonly record struct CultistParasiteImplantedEvent(EntityUid? User, EntityUid Parasite);
