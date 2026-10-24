using Content.Shared._RMC14.Language.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Cultist;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedCultistSystem))]
public sealed partial class CultistLanguageComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<LanguagePrototype> Language = "Xeno";

    [DataField]
    public EntProtoId LearningPrototype = "RMCCultistLanguageLearning";

    [DataField, AutoNetworkedField]
    public float MaximumComprehension = 0.5f;

    [DataField, AutoNetworkedField]
    public float SpeechThreshold = 0.25f;

    public bool LearningConfigured;
    public bool AddedLearning;
    public bool HadSpokenLanguage;
    public bool HadUnderstoodLanguage;
    public bool HadLearnableLanguage;
}
