using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Cultist;

/// <summary>
/// Roundstart cult selection settings for a distress signal rule.
/// </summary>
[RegisterComponent]
public sealed partial class CultistRuleComponent : Component
{
    [DataField]
    public float Probability = 0.1f;

    [DataField]
    public ProtoId<AntagPrototype> Preference = "RMCCultist";

    [DataField]
    public bool AllowSyntheticCultists;

    public bool Rolled;
    public bool Active;
    public bool SelectionComplete;
}
