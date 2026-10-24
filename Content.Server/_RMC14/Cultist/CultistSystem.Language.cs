using Content.Server._RMC14.Language.Systems;
using Content.Shared._RMC14.Cultist;
using Content.Shared._RMC14.Language;
using Content.Shared._RMC14.Language.Components;

namespace Content.Server._RMC14.Cultist;

public sealed partial class CultistSystem
{
    private void InitializeLanguage()
    {
        SubscribeLocalEvent<CultistComponent, CultistLanguageSetupEvent>(OnLanguageSetup);
        SubscribeLocalEvent<CultistLanguageComponent, DetermineEntityLanguagesEvent>(OnDetermineCultistLanguages,
            after: [typeof(LanguageLearningSystem)]);
        SubscribeLocalEvent<CultistLanguageComponent, LanguageLearningSpeechEvent>(OnLearningSpeech);
        SubscribeLocalEvent<CultistLanguageComponent, ComponentShutdown>(OnLanguageShutdown);
    }

    private void OnLanguageSetup(Entity<CultistComponent> ent, ref CultistLanguageSetupEvent args)
    {
        ConfigureLanguage(ent);
    }

    private void ConfigureLanguage(Entity<CultistComponent> ent)
    {
        var language = EnsureComp<CultistLanguageComponent>(ent);
        if (language.LearningConfigured)
            return;

        _cultist.InitializeLanguage((ent, language), !HasComp<LanguageLearningComponent>(ent),
            _language.CanSpeak(ent, language.Language), _language.CanUnderstand(ent, language.Language),
            _learning.CanLearnLanguage(ent, language.Language));
        if (language.AddedLearning)
            EntityManager.AddComponents(ent, _prototypes.Index(language.LearningPrototype));

        _learning.AddLearnableLanguage(ent, language.Language);
        var learning = Comp<LanguageLearningComponent>(ent);
        _learning.UpdateLanguageProgress((ent, learning), language.Language);
        Dirty(ent, learning);
        _language.RemoveLanguage(ent.Owner, language.Language);
    }

    private void OnLearningSpeech(Entity<CultistLanguageComponent> ent, ref LanguageLearningSpeechEvent args)
    {
        if (ent.Comp.LifeStage < ComponentLifeStage.Stopping && args.Language == ent.Comp.Language)
            args.CanSpeak = args.Progress >= ent.Comp.SpeechThreshold;
    }

    private void OnDetermineCultistLanguages(Entity<CultistLanguageComponent> ent,
        ref DetermineEntityLanguagesEvent args)
    {
        if (ent.Comp.LifeStage >= ComponentLifeStage.Stopping)
            return;

        args.UnderstoodLanguages.Remove(ent.Comp.Language);
        if (_learning.GetComprehensionLevel(ent, ent.Comp.Language) >= ent.Comp.SpeechThreshold)
            args.SpokenLanguages.Add(ent.Comp.Language);
        else
            args.SpokenLanguages.Remove(ent.Comp.Language);
    }

    private void OnLanguageShutdown(Entity<CultistLanguageComponent> ent, ref ComponentShutdown args)
    {
        if (!ent.Comp.LearningConfigured || TerminatingOrDeleted(ent))
            return;

        if (ent.Comp.AddedLearning)
            RemComp<LanguageLearningComponent>(ent);
        else if (!ent.Comp.HadLearnableLanguage)
            _learning.RemoveLearnableLanguage(ent, ent.Comp.Language);
        else if (TryComp<LanguageLearningComponent>(ent, out var learning))
        {
            _learning.UpdateLanguageProgress((ent, learning), ent.Comp.Language);
            Dirty(ent, learning);
        }

        _language.RemoveLanguage(ent.Owner, ent.Comp.Language);
        _language.AddLanguage(ent, ent.Comp.Language, ent.Comp.HadSpokenLanguage, ent.Comp.HadUnderstoodLanguage);
    }
}
