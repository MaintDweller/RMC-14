using Content.Shared._RMC14.Language;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistSystem
{
    private void InitializeLanguage()
    {
        SubscribeLocalEvent<CultistLanguageComponent, LanguageLearningProgressEvent>(OnLanguageProgress);
        SubscribeLocalEvent<CultistLanguageComponent, LanguageLearningWordEvent>(OnLanguageWord);
    }

    public void InitializeLanguage(Entity<CultistLanguageComponent> ent, bool addedLearning, bool spoken,
        bool understood, bool learnable)
    {
        ent.Comp.AddedLearning = addedLearning;
        ent.Comp.HadSpokenLanguage = spoken;
        ent.Comp.HadUnderstoodLanguage = understood;
        ent.Comp.HadLearnableLanguage = learnable;
        ent.Comp.LearningConfigured = true;
    }

    private void OnLanguageProgress(Entity<CultistLanguageComponent> ent, ref LanguageLearningProgressEvent args)
    {
        if (ent.Comp.LifeStage < ComponentLifeStage.Stopping && args.Language == ent.Comp.Language)
            args.Progress = Math.Min(args.Progress, ent.Comp.MaximumComprehension);
    }

    private void OnLanguageWord(Entity<CultistLanguageComponent> ent, ref LanguageLearningWordEvent args)
    {
        if (ent.Comp.LifeStage >= ComponentLifeStage.Stopping || args.Language != ent.Comp.Language)
            return;

        args.Comprehension = Math.Min(args.Comprehension, ent.Comp.MaximumComprehension);
        args.LetterByLetter = true;
    }
}
