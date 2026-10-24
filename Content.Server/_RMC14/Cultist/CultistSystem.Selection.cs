using System.Linq;
using Content.Server.Jobs;
using Content.Shared._RMC14.Cultist;
using Content.Shared._RMC14.Marines.Skills;
using Content.Shared._RMC14.Rules;
using Content.Shared._RMC14.Synth;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._RMC14.Cultist;

public sealed partial class CultistSystem
{
    private static readonly EntProtoId<CultistComponent> LeaderEffects = "RMCCultistLeaderEffects";
    private static readonly ProtoId<JobPrototype> SyntheticJob = "RMCJobSynthetic";
    private Entity<CultistRuleComponent>? _round;

    public void RollRoundstart(EntityUid rule, CMDistressSignalRuleComponent survivors)
    {
        if (!TryComp<CultistRuleComponent>(rule, out var settings) || settings.Rolled)
            return;

        _round = (rule, settings);
        settings.Rolled = true;
        foreach (var (job, amount) in survivors.SurvivorJobs)
        {
            if (amount == 0)
                continue;

            var variants = survivors.SurvivorJobVariantScenarios?.GetValueOrDefault(job)
                ?? survivors.SurvivorJobVariants?.GetValueOrDefault(job);
            var available = variants == null
                ? IsCultistJob(job, settings)
                : variants.Any(variant => variant.Item2 != 0 && IsCultistJob(variant.Item1, settings));
            if (!available)
                continue;

            settings.Active = _random.Prob(settings.Probability);
            return;
        }
    }

    public bool CanSelectSurvivor(HumanoidCharacterProfile profile, ProtoId<JobPrototype> job)
    {
        return _round is not { } round || TerminatingOrDeleted(round) || !round.Comp.Active ||
               !IsCultistJob(job, round.Comp) ||
               profile.AntagPreferences.Contains(round.Comp.Preference);
    }

    public bool IsCultistCandidate(HumanoidCharacterProfile profile, ProtoId<JobPrototype> job)
    {
        return _round is { } round && !TerminatingOrDeleted(round) && round.Comp.Active &&
               profile.AntagPreferences.Contains(round.Comp.Preference) && IsCultistJob(job, round.Comp);
    }

    private bool IsCultistJob(ProtoId<JobPrototype> job, CultistRuleComponent settings)
    {
        var prototype = _prototypes.Index(job);
        if (!prototype.CanBeCultist ||
            !settings.AllowSyntheticCultists && prototype.WhitelistParent == SyntheticJob)
            return false;

        SkillsComponent? skills = null;
        foreach (var special in prototype.Special)
        {
            if (special is not AddComponentSpecial addComponents)
                continue;

            foreach (var entry in addComponents.Components.Values)
            {
                if (entry.Component is BypassSkillChecksComponent)
                    return false;

                if (entry.Component is SkillsComponent addedSkills && (skills == null || addComponents.RemoveExisting))
                    skills = addedSkills;
            }
        }

        if (skills == null)
            return true;

        var levels = skills.Skills;
        if (skills.Preset is { } preset && preset.TryGet(out var skillPreset, _prototypes, _compFactory))
            levels = skillPreset.Skills;

        return !LeaderEffects.Get(_prototypes, _compFactory).ImmuneSkills.Any(immune =>
            immune.Value > 0 && levels.GetValueOrDefault(immune.Key) >= immune.Value);
    }

    public int SelectRoundstart(EntityUid rule, IReadOnlyDictionary<NetUserId, HumanoidCharacterProfile> profiles,
        IReadOnlyList<(EntityUid Mob, ProtoId<JobPrototype> Job)> survivors, EntityUid? hive)
    {
        if (!TryComp<CultistRuleComponent>(rule, out var settings) || !settings.Active || settings.SelectionComplete)
            return 0;

        settings.SelectionComplete = true;
        if (hive is not { } hiveUid || !HasComp<HiveComponent>(hiveUid))
            return 0;

        var candidates = new List<EntityUid>();
        var leaders = new List<EntityUid>();
        var immuneSkills = LeaderEffects.Get(_prototypes, _compFactory).ImmuneSkills;
        foreach (var (mob, job) in survivors)
        {
            if (TerminatingOrDeleted(mob) || !_mobState.IsAlive(mob) || HasComp<CultistComponent>(mob) ||
                !IsCultistJob(job, settings) ||
                _skills.HasAnySkills(mob, immuneSkills) ||
                HasComp<SynthComponent>(mob) && !settings.AllowSyntheticCultists ||
                !TryComp<ActorComponent>(mob, out var actor) ||
                !profiles.TryGetValue(actor.PlayerSession.UserId, out var profile) ||
                !profile.AntagPreferences.Contains(settings.Preference))
                continue;

            candidates.Add(mob);
            if (!HasComp<SynthComponent>(mob))
                leaders.Add(mob);
        }

        if (leaders.Count == 0)
            return 0;

        var leader = _random.Pick(leaders);
        foreach (var mob in candidates)
        {
            _cultist.MakeCultist(mob, hiveUid, mob == leader);
            ConfigureCultist((mob, Comp<CultistComponent>(mob)));
        }

        return candidates.Count;
    }
}
