using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Shared._RMC14.Rules;
using Content.Shared._RMC14.TacticalMap;
using Content.Shared.Coordinates;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._RMC14.Rules.DistressSignal;

public sealed partial class CMDistressSignalRuleSystem
{
    /// <summary>
    /// Main survivor spawning handler. Delegates to helper methods for job collection,
    /// candidate assignment, and individual survivor spawning.
    /// </summary>
    /// <param name="comp">The distress signal rule component.</param>
    /// <param name="ev">The rule player spawning event.</param>
    /// <param name="initialPlayerCount">Player count before any players were removed from the pool (for correct totalSurvivors calculation).</param>
    private List<(EntityUid Mob, ProtoId<JobPrototype> Job)> SpawnSurvivors(
        CMDistressSignalRuleComponent comp, RulePlayerSpawningEvent ev, int initialPlayerCount)
    {
        var survivors = new List<(EntityUid Mob, ProtoId<JobPrototype> Job)>();
        if (!comp.SpawnSurvivors || comp.SurvivorJobs.Count == 0)
            return survivors;

        if (ActiveNightmareScenario == null)
        {
            var compCopy = comp;
            IEnumerable<(ProtoId<JobPrototype> Job, int Amount)> jobs = comp.SurvivorJobs
                .Where(entry => entry.Job != compCopy.CivilianSurvivorJob)
                .OrderBy(_ => _random.Next());

            if (comp.SurvivorJobs.TryFirstOrNull(entry => entry.Job == compCopy.CivilianSurvivorJob, out var civJob))
                jobs = jobs.Append(civJob.Value);

            comp.SurvivorJobs = jobs.ToList();
        }

        var possibleJobs = CollectPossibleSurvivorJobs(comp);
        var (spawners, spawnersLeft) = FindSurvivorSpawners(possibleJobs);
        var candidates = CollectSurvivorCandidates(comp, ev);

        SpawnSurvivorsFromCandidates(comp, ev, candidates, spawners, spawnersLeft, initialPlayerCount, survivors);
        return survivors;
    }

    private List<ProtoId<JobPrototype>> CollectPossibleSurvivorJobs(CMDistressSignalRuleComponent comp)
    {
        var jobs = new List<ProtoId<JobPrototype>>();
        jobs.AddRange(comp.SurvivorJobs.Select(x => x.Job));
        if (comp.SurvivorJobOverrides != null)
            jobs.AddRange(comp.SurvivorJobOverrides.Values);
        if (comp.SurvivorJobVariants != null)
            jobs.AddRange(comp.SurvivorJobVariants.Values.SelectMany(x => x).Select(x => x.Variant));
        if (comp.SurvivorJobVariantScenarios != null)
            jobs.AddRange(comp.SurvivorJobVariantScenarios.Values.SelectMany(x => x).Select(x => x.Special));
        return jobs;
    }

    private (Dictionary<ProtoId<JobPrototype>, List<EntityUid>> Spawners,
             Dictionary<ProtoId<JobPrototype>, List<EntityUid>> SpawnersLeft)
        FindSurvivorSpawners(List<ProtoId<JobPrototype>> possibleJobs)
    {
        var spawners = new Dictionary<ProtoId<JobPrototype>, List<EntityUid>>();
        var spawnerQuery = EntityQueryEnumerator<SpawnPointComponent>();
        while (spawnerQuery.MoveNext(out var spawnId, out var spawnComp))
        {
            if (spawnComp.Job is { } job && possibleJobs.Contains(job))
                spawners.GetOrNew(job).Add(spawnId);
        }

        var spawnersLeft = spawners.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
        return (spawners, spawnersLeft);
    }

    private Dictionary<ProtoId<JobPrototype>, List<NetUserId>[]> CollectSurvivorCandidates(
        CMDistressSignalRuleComponent comp,
        RulePlayerSpawningEvent ev)
    {
        var priorities = Enum.GetValues<JobPriority>().Length;
        var candidates = new Dictionary<ProtoId<JobPrototype>, List<NetUserId>[]>();

        foreach (var job in comp.SurvivorJobs)
        {
            var jobList = new List<NetUserId>[priorities];
            for (var i = 0; i < jobList.Length; i++)
            {
                jobList[i] = [];
            }
            candidates[job.Job] = jobList;
        }

        foreach (var player in ev.PlayerPool)
        {
            foreach (var (job, players) in candidates)
            {
                TryAddSurvivorCandidate(player.UserId, job, players, comp, ev);
            }
        }

        return candidates;
    }

    private void TryAddSurvivorCandidate(
        NetUserId id,
        ProtoId<JobPrototype> job,
        List<NetUserId>[] players,
        CMDistressSignalRuleComponent comp,
        RulePlayerSpawningEvent ev)
    {
        if (!IsJobAllowed(id, comp.CivilianSurvivorJob) || !IsJobAllowed(id, job))
            return;

        if (!ev.Profiles.TryGetValue(id, out var profile))
            return;

        if (comp.SurvivorJobOverrides != null)
        {
            foreach (var (originalJob, overrideJob) in comp.SurvivorJobOverrides)
            {
                if (profile.JobPriorities.TryGetValue(originalJob, out var overridePrio) &&
                    overridePrio > JobPriority.Never && overrideJob == job)
                {
                    players[(int)overridePrio].Add(id);
                    return;
                }
            }
        }

        if (profile.JobPriorities.TryGetValue(job, out var prio) && prio > JobPriority.Never)
        {
            players[(int)prio].Add(id);
        }
    }

    private void SpawnSurvivorsFromCandidates(
        CMDistressSignalRuleComponent comp,
        RulePlayerSpawningEvent ev,
        Dictionary<ProtoId<JobPrototype>, List<NetUserId>[]> candidates,
        Dictionary<ProtoId<JobPrototype>, List<EntityUid>> spawners,
        Dictionary<ProtoId<JobPrototype>, List<EntityUid>> spawnersLeft,
        int initialPlayerCount,
        List<(EntityUid Mob, ProtoId<JobPrototype> Job)> survivors)
    {
        var priorities = Enum.GetValues<JobPriority>().Length;
        var totalSurvivors = (int)Math.Clamp((int)Math.Round(initialPlayerCount / _marinesPerSurvivor), _minimumSurvivors, _maximumSurvivors);
        var selected = 0;

        for (var i = priorities - 1; i >= 0; i--)
        {
            foreach (var (job, players) in candidates)
            {
                var ignoreLimit = comp.IgnoreMaximumSurvivorJobs.Contains(job);
                var playerNames = players[i].Select(p => ev.Profiles.TryGetValue(p, out var prof) ? prof.Name : p.ToString());
                Log.Info($"Rolling survivor job {job} with priority {i} and players {string.Join(", ", playerNames)}");
                while (players[i].Count > 0 && (ignoreLimit || selected < totalSurvivors))
                {
                    if (TrySpawnSingleSurvivor(job, players[i], comp, ev, spawnersLeft, spawners, survivors,
                            out var playerId))
                    {
                        var name = ev.Profiles.TryGetValue(playerId, out var prof) ? prof.Name : playerId.ToString();
                        Log.Info($"Spawned survivor job {job} with name/id {name}, ignore limit: {ignoreLimit}");
                        RemoveCandidateFromAllLists(candidates, playerId);
                        if (!ignoreLimit)
                            selected++;
                    }
                    else
                    {
                        Log.Info($"Stopped rolling survivor job {job}");
                        break;
                    }
                }
            }
        }
    }

    private void RemoveCandidateFromAllLists(
        Dictionary<ProtoId<JobPrototype>, List<NetUserId>[]> candidates,
        NetUserId playerId)
    {
        foreach (var (_, priorityLists) in candidates)
        {
            foreach (var list in priorityLists)
            {
                list.Remove(playerId);
            }
        }
    }

    private bool TrySpawnSingleSurvivor(
        ProtoId<JobPrototype> job,
        List<NetUserId> list,
        CMDistressSignalRuleComponent comp,
        RulePlayerSpawningEvent ev,
        Dictionary<ProtoId<JobPrototype>, List<EntityUid>> spawnersLeft,
        Dictionary<ProtoId<JobPrototype>, List<EntityUid>> spawners,
        List<(EntityUid Mob, ProtoId<JobPrototype> Job)> survivors,
        out NetUserId spawnedId)
    {
        spawnedId = default;
        var remaining = list.ToList();
        while (remaining.Count > 0)
        {
            var playerId = _random.PickAndTake(remaining);
            if (!_player.TryGetSessionById(playerId, out var player) ||
                !ev.Profiles.TryGetValue(playerId, out var selectionProfile))
            {
                list.Remove(playerId);
                continue;
            }

            var spawnAsJob = DetermineSurvivorJob(job, playerId, selectionProfile, comp,
                out var stop);
            if (stop || !_cultists.CanSelectSurvivor(selectionProfile, spawnAsJob))
                continue;

            if (!DecrementOriginalJobSlot(job, comp))
                return false;

            var spawner = FindSurvivorSpawner(spawnAsJob, comp, spawnersLeft, spawners, out stop);
            if (stop)
                return false;

            list.Remove(playerId);
            ev.PlayerPool.Remove(player);
            GameTicker.PlayerJoinGame(player);

            var profile = GameTicker.GetPlayerProfile(player);
            var coordinates = _transform.GetMoverCoordinates(spawner);
            var survivorMob = _stationSpawning.SpawnPlayerMob(coordinates, spawnAsJob, profile, null);

            if (!_mind.TryGetMind(playerId, out var mind))
                mind = _mind.CreateMind(playerId);

            RemCompDeferred<TacticalMapUserComponent>(survivorMob);
            _mind.TransferTo(mind.Value, survivorMob);
            _roles.MindAddJobRole(mind.Value, jobPrototype: spawnAsJob);
            _playTime.PlayerRolesChanged(player);

            RaiseLocalEvent(survivorMob, new PlayerSpawnCompleteEvent(survivorMob, player, spawnAsJob, false, true, 0, default, profile), true);

            survivors.Add((survivorMob, spawnAsJob));
            spawnedId = playerId;
            return true;
        }

        return false;
    }

    private ProtoId<JobPrototype> DetermineSurvivorJob(
        ProtoId<JobPrototype> job,
        NetUserId playerId,
        HumanoidCharacterProfile profile,
        CMDistressSignalRuleComponent comp,
        out bool stop)
    {
        stop = false;
        var spawnAsJob = job;

        var scenarioSuccess = TryGetScenarioJob(job, playerId, profile, comp, ref spawnAsJob, ref stop);
        if (stop)
            return spawnAsJob;

        if (!scenarioSuccess)
        {
            CheckVariantJob(job, playerId, profile, comp, ref spawnAsJob, ref stop);
        }

        return spawnAsJob;
    }

    private bool TryGetScenarioJob(
        ProtoId<JobPrototype> job,
        NetUserId playerId,
        HumanoidCharacterProfile profile,
        CMDistressSignalRuleComponent comp,
        ref ProtoId<JobPrototype> spawnAsJob,
        ref bool stop)
    {
        if (comp.SurvivorJobVariantScenarios == null ||
            !comp.SurvivorJobVariantScenarios.TryGetValue(job, out var scenarioJobsList))
        {
            return false;
        }

        stop = !TrySelectSurvivorVariant(scenarioJobsList, playerId, profile, false, out spawnAsJob);
        return true;
    }

    /// <summary>
    /// Uses the planet's random variant selection or its ordered variant slots.
    /// </summary>
    private void CheckVariantJob(
        ProtoId<JobPrototype> job,
        NetUserId playerId,
        HumanoidCharacterProfile profile,
        CMDistressSignalRuleComponent comp,
        ref ProtoId<JobPrototype> spawnAsJob,
        ref bool stop)
    {
        if (comp.SurvivorJobVariants == null ||
            !comp.SurvivorJobVariants.TryGetValue(job, out var variants))
        {
            return;
        }

        var randomize = SelectedPlanetMap?.Comp.SelectRandomSurvivorVariant ?? false;
        stop = !TrySelectSurvivorVariant(variants, playerId, profile, randomize, out spawnAsJob);
    }

    private bool TrySelectSurvivorVariant(List<(ProtoId<JobPrototype> Job, int Amount)> variants,
        NetUserId playerId, HumanoidCharacterProfile profile, bool randomize, out ProtoId<JobPrototype> selectedJob)
    {
        selectedJob = default;
        if (randomize)
        {
            var eligible = variants.Where(variant => IsJobAllowed(playerId, variant.Job) &&
                _cultists.CanSelectSurvivor(profile, variant.Job)).ToList();
            if (eligible.Count == 0)
                return false;

            selectedJob = _random.Pick(eligible).Job;
            return true;
        }

        var selected = -1;
        for (var i = 0; i < variants.Count; i++)
        {
            var (job, amount) = variants[i];
            if (amount != -1 && amount <= 0 || !IsJobAllowed(playerId, job) ||
                !_cultists.CanSelectSurvivor(profile, job))
                continue;

            if (selected == -1)
                selected = i;
            if (!_cultists.IsCultistCandidate(profile, job))
                continue;

            selected = i;
            break;
        }

        if (selected == -1)
            return false;

        var (selectedVariant, remaining) = variants[selected];
        selectedJob = selectedVariant;
        if (remaining > 0)
            variants[selected] = (selectedVariant, remaining - 1);
        return true;
    }

    /// <summary>
    /// Decrements the base job's slot count regardless of the assigned variant.
    /// </summary>
    private bool DecrementOriginalJobSlot(ProtoId<JobPrototype> job, CMDistressSignalRuleComponent comp)
    {
        for (var i = 0; i < comp.SurvivorJobs.Count; i++)
        {
            var (survJob, amount) = comp.SurvivorJobs[i];
            if (survJob != job)
                continue;

            if (amount == -1)
                return true;

            if (amount <= 0)
                return false;

            comp.SurvivorJobs[i] = (survJob, amount - 1);
        }
        return true;
    }

    private EntityUid FindSurvivorSpawner(
        ProtoId<JobPrototype> spawnAsJob,
        CMDistressSignalRuleComponent comp,
        Dictionary<ProtoId<JobPrototype>, List<EntityUid>> spawnersLeft,
        Dictionary<ProtoId<JobPrototype>, List<EntityUid>> spawners,
        out bool stop)
    {
        stop = false;

        if (!spawnersLeft.TryGetValue(spawnAsJob, out var jobSpawners) &&
            !spawnersLeft.TryGetValue(comp.CivilianSurvivorJob, out jobSpawners))
        {
            stop = true;
            return default;
        }

        if (jobSpawners.Count == 0)
        {
            if (spawners.TryGetValue(comp.CivilianSurvivorJob, out var fallbackSpawners))
            {
                jobSpawners.Clear();
                jobSpawners.AddRange(fallbackSpawners);
            }

            if (jobSpawners.Count == 0)
            {
                stop = true;
                return default;
            }
        }

        return _random.PickAndTake(jobSpawners);
    }
}
