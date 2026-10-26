using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Cultist;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedCultistRitesSystem), typeof(SharedCultistSystem))]
public sealed partial class CultistDevotionComponent : Component
{
    [AutoNetworkedField]
    public EntityUid? Hive;

    [AutoNetworkedField]
    public int Devotion;

    [DataField]
    public int Capacity = 100;

    [DataField]
    public int ConcealedMemberLimit = 10;

    [AutoNetworkedField]
    public bool RevelationRequired;

    [AutoNetworkedField]
    public bool Revealed;

    [DataField]
    public TimeSpan RevelationDelay = TimeSpan.FromSeconds(20);

    [DataField]
    public int RevelationCost = 50;

    [DataField]
    public float RevelationRange = 8;

    [DataField]
    public EntProtoId Robe = "RMCCultistRobe";

    [DataField]
    public EntProtoId Hood = "RMCCultistHood";

    [DataField]
    public Dictionary<CultistDevotionSource, int> Rewards = new()
    {
        [CultistDevotionSource.Conversion] = 5,
        [CultistDevotionSource.Trap] = 2,
        [CultistDevotionSource.XenoRescue] = 6,
        [CultistDevotionSource.Infection] = 5,
        [CultistDevotionSource.Sabotage] = 8,
        [CultistDevotionSource.SensorSabotage] = 5,
        [CultistDevotionSource.Sacrifice] = 10,
    };

    [DataField]
    public TimeSpan RewardCooldown = TimeSpan.FromMinutes(5);

    [DataField]
    public TimeSpan TrapRewardCooldown = TimeSpan.FromMinutes(1);

    [DataField]
    public TimeSpan SabotageDelay = TimeSpan.FromMinutes(1);

    [DataField]
    public float RescueDamageFraction = 0.5f;

    [DataField]
    public float RescueDistance = 8;

    [DataField]
    public TimeSpan RescueSafetyDelay = TimeSpan.FromSeconds(20);

    [DataField]
    public TimeSpan RescueExpiry = TimeSpan.FromMinutes(5);

    [DataField]
    public int ParasiteCost = 35;

    [DataField]
    public TimeSpan ParasiteRitualDelay = TimeSpan.FromSeconds(15);

    [DataField]
    public EntProtoId ParasiteContainer = "RMCCultistParasiteContainer";

    [DataField]
    public TimeSpan SacrificeDelay = TimeSpan.FromSeconds(15);

    [DataField]
    public TimeSpan SacrificeJitterTime = TimeSpan.FromSeconds(1.5);

    [DataField]
    public float SacrificeJitterAmplitude = 14;

    [DataField]
    public float SacrificeJitterFrequency = 5;

    [DataField]
    public int UsurpationCost = 30;

    [DataField]
    public TimeSpan TransferDelay = TimeSpan.FromSeconds(20);

    [DataField]
    public TimeSpan SuccessionDelay = TimeSpan.FromSeconds(40);

    [DataField]
    public TimeSpan SolitaryDelay = TimeSpan.FromMinutes(1);

    [DataField]
    public TimeSpan UsurpationDelay = TimeSpan.FromMinutes(1);

    [DataField]
    public TimeSpan UsurpationCooldown = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan InvitationTimeout = TimeSpan.FromMinutes(1);

    [DataField]
    public TimeSpan AbsentLeaderDelay = TimeSpan.FromMinutes(2);

    [DataField]
    public float RitualRange = 1.5f;

    [DataField]
    public SoundSpecifier SuccessionSound = new SoundPathSpecifier("/Audio/_RMC14/Cultist/rite_succession.ogg");

    [DataField]
    public SoundSpecifier ParasiteRitualSound = new SoundPathSpecifier("/Audio/_RMC14/Cultist/rite_parasite.ogg");

    [DataField]
    public SoundSpecifier SacrificeSound = new SoundPathSpecifier("/Audio/_RMC14/Cultist/rite_sacrifice.ogg");

    [DataField]
    public SoundSpecifier RevelationSound = new SoundPathSpecifier("/Audio/_RMC14/Cultist/robes.ogg");

    [DataField]
    public TimeSpan RevelationSoundLeadTime = TimeSpan.FromSeconds(7);

    [DataField]
    public EntProtoId[] SuccessionEffects = ["RMCEffectCultistSuccessionSeal", "RMCEffectCultistSuccessionCrown"];

    [DataField]
    public EntProtoId[] SuccessionParticipantEffects = ["RMCEffectCultistSuccessionParticipant"];

    [DataField]
    public EntProtoId[] ParasiteRiteEffects = ["RMCEffectCultistParasiteSeal", "RMCEffectCultistParasiteEgg"];

    [DataField]
    public EntProtoId[] SacrificeEffects = ["RMCEffectCultistSacrificeSeal", "RMCEffectCultistSacrificeClaws"];

    [DataField]
    public EntProtoId[] SacrificeParticipantEffects = ["RMCEffectCultistSacrificeParticipant"];

    [DataField]
    public EntProtoId[] RevelationEffects = ["RMCEffectCultistRevelationSeal", "RMCEffectCultistRevelationHalo"];

    [DataField]
    public EntProtoId[] RevelationParticipantEffects =
    [
        "RMCEffectCultistRevelationParticipant",
        "RMCEffectCultistRevelationStream",
        "RMCEffectCultistRevelationStreamSecond",
        "RMCEffectCultistRevelationStreamThird",
    ];

    public readonly HashSet<EntityUid> Members = new();
    public readonly Dictionary<(CultistDevotionSource Source, EntityUid Target), TimeSpan> RewardReadyAt = new();
    public readonly Dictionary<EntityUid, TimeSpan> TrapRewards = new();
    public EntityUid? Leader;
    public TimeSpan? LeaderAbsentSince;
    public TimeSpan UsurpationReadyAt;
    public TimeSpan ConversionReadyAt;
    public TimeSpan StunReadyAt;
    public int NextRitualId;
    public CultistSuccession? Ritual;
    public int NextSacrificeId;
    public CultistSacrifice? Sacrifice;
    public int NextRevelationId;
    public CultistRevelation? Revelation;
}

public sealed class CultistRevelation
{
    public required int Id;
    public required EntityUid Leader;
    public required TimeSpan ExpiresAt;
    public readonly Dictionary<EntityUid, EntityCoordinates> Participants = new();
    public readonly List<EntityUid> Effects = new();
    public bool Started;
    public DoAfterId? DoAfter;
    public bool SoundPlayed;
    public EntityUid? Sound;
}

public enum CultistDevotionSource : byte
{
    Conversion,
    Trap,
    XenoRescue,
    Infection,
    Sabotage,
    SensorSabotage,
    Sacrifice,
}

public sealed class CultistSuccession
{
    public readonly List<EntityUid> Effects = new();

    public required int Id;
    public required EntityUid Candidate;
    public EntityUid? Supporter;
    public EntityUid? PreviousLeader;
    public required TimeSpan ExpiresAt;
    public bool Started;
    public bool LeaderVacant;
    public int Cost;
    public EntityCoordinates SupporterPosition;
    public FixedPoint2 SupporterDamage;
}
