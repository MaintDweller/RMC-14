using Content.Shared._RMC14.Marines.Skills;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Cultist;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedCultistSystem), typeof(SharedCultistRitesSystem))]
public sealed partial class CultistComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Leader;

    [DataField, AutoNetworkedField]
    public bool AllowFriendlyFire;

    public bool Channeling;

    public DoAfterId? ParasiteRitual;

    [DataField]
    public ComponentRegistry Components = new();

    [DataField, AutoNetworkedField]
    public EntProtoId ConvertAction = "RMCActionCultistConvert";

    [DataField, AutoNetworkedField]
    public EntProtoId StunAction = "RMCActionCultistStun";

    [DataField, AutoNetworkedField]
    public EntProtoId FriendlyFireAction = "RMCActionCultistFriendlyFire";

    [DataField, AutoNetworkedField]
    public EntProtoId RitesAction = "RMCActionCultistRites";

    [AutoNetworkedField]
    public EntityUid? RitesActionEntity;

    [AutoNetworkedField]
    public EntityUid? ConvertActionEntity;

    [AutoNetworkedField]
    public EntityUid? StunActionEntity;

    [AutoNetworkedField]
    public EntityUid? FriendlyFireActionEntity;

    [DataField]
    public TimeSpan ConversionDelay = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan ConversionCooldown = TimeSpan.FromMinutes(3);

    [DataField]
    public TimeSpan ConversionKnockout = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan StunDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan StunDuration = TimeSpan.FromSeconds(20);

    [DataField]
    public TimeSpan StunCooldown = TimeSpan.FromMinutes(1);

    [DataField]
    public TimeSpan InterruptedStunCooldown = TimeSpan.FromSeconds(5);

    [DataField]
    public float AbilityRange = 1.5f;

    [DataField]
    public float MovementThreshold = 0.3f;

    [DataField]
    public Dictionary<EntProtoId<SkillDefinitionComponent>, int> ImmuneSkills = new()
    {
        ["RMCSkillLeadership"] = 1,
        ["RMCSkillPolice"] = 1,
    };

    [DataField]
    public SoundSpecifier ConversionSound = new SoundPathSpecifier("/Audio/_RMC14/Xeno/xeno_newlarva.ogg");

    [DataField]
    public SoundSpecifier StunSound = new SoundPathSpecifier("/Audio/_RMC14/Cultist/stun.ogg");
}
