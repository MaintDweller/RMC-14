using Content.Shared.DoAfter;
using Robust.Shared.Map;

namespace Content.Shared._RMC14.Cultist;

public sealed class CultistSacrifice
{
    public required int Id;
    public required EntityUid Initiator;
    public required EntityUid Victim;
    public required TimeSpan ExpiresAt;
    public EntityUid? Supporter;
    public DoAfterId? DoAfter;
    public bool Started;
    public EntityCoordinates VictimPosition;
    public EntityCoordinates SupporterPosition;
}
