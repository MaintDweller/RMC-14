using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Cultist;

/// <summary>
/// Presentation of a cult rite. It does not grant vision or affect nearby entities.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SharedCultistRitesSystem))]
public sealed partial class CultistRitualEffectComponent : Component
{
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan StartedAt;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan EndsAt;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan? CompletedAt;

    [AutoNetworkedField]
    public EntityUid? Focus;

    [DataField]
    public Vector2 Scale = Vector2.One;

    [DataField]
    public Color Color = Color.White;

    [DataField]
    public Vector2 Offset = new(0, -0.3f);

    [DataField]
    public Vector2 Drift;

    [DataField]
    public float Sway;

    [DataField]
    public float Rocking;

    [DataField]
    public bool TravelToCenter;

    [DataField]
    public float PhaseOffset;

    [DataField]
    public float FadeIn = 0.3f;

    [DataField]
    public float PulsePeriod = 1.6f;

    [DataField]
    public float MinimumAlpha = 0.5f;

    [DataField]
    public float MaximumAlpha = 0.95f;

    [DataField]
    public float Expansion = 0.08f;

    [DataField]
    public float ProgressExpansion;

    [DataField]
    public float ProgressOpacity;

    [DataField]
    public float CompletionDuration = 0.8f;

    [DataField]
    public float CompletionScale = 1.4f;

    [DataField]
    public Vector2 CompletionDrift;
}
