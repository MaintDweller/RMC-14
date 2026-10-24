using Content.Shared._RMC14.Language.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Language;

[ByRefEvent]
public record struct LanguageLearningProgressEvent(ProtoId<LanguagePrototype> Language, float Progress)
{
    public readonly ProtoId<LanguagePrototype> Language = Language;
    public float Progress = Progress;
}

[ByRefEvent]
public record struct LanguageLearningSpeechEvent(ProtoId<LanguagePrototype> Language, float Progress, bool CanSpeak)
{
    public readonly ProtoId<LanguagePrototype> Language = Language;
    public readonly float Progress = Progress;
    public bool CanSpeak = CanSpeak;
}

[ByRefEvent]
public record struct LanguageLearningWordEvent(ProtoId<LanguagePrototype> Language, float Comprehension)
{
    public readonly ProtoId<LanguagePrototype> Language = Language;
    public float Comprehension = Comprehension;
    public bool LetterByLetter;
}
