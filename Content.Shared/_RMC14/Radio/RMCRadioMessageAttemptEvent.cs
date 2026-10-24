using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Radio;

[ByRefEvent]
public record struct RMCRadioMessageAttemptEvent
{
    public readonly ProtoId<RadioChannelPrototype> Channel;
    public bool Cancelled;

    public RMCRadioMessageAttemptEvent(ProtoId<RadioChannelPrototype> channel)
    {
        Channel = channel;
    }
}
