using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistRitesSystem
{
    private void StartRiteVisuals(List<EntityUid> effects, EntProtoId[] prototypes,
        EntityUid participant, TimeSpan duration, EntityUid? center = null)
    {
        foreach (var prototype in prototypes)
            AddRiteEffect(effects, prototype, participant, duration, center);
    }

    private void AddRiteEffect(List<EntityUid> effects, EntProtoId prototype, EntityUid participant,
        TimeSpan duration, EntityUid? center)
    {
        // Keep the effect on the ground when the victim is gibbed or a participant moves away.
        var effect = Spawn(prototype, Transform(participant).Coordinates);
        var visual = Comp<CultistRitualEffectComponent>(effect);
        visual.StartedAt = _timing.CurTime;
        visual.EndsAt = visual.StartedAt + duration;
        if (visual.TravelToCenter)
            visual.Focus = center;
        Comp<TimedDespawnComponent>(effect).Lifetime = (float) duration.TotalSeconds + visual.CompletionDuration;
        Dirty(effect, visual);
        effects.Add(effect);
    }

    private void EndRiteVisuals(List<EntityUid> effects, bool completed = false)
    {
        foreach (var effect in effects)
        {
            if (TerminatingOrDeleted(effect))
                continue;
            if (!completed)
            {
                QueueDel(effect);
                continue;
            }

            var visual = Comp<CultistRitualEffectComponent>(effect);
            visual.CompletedAt = _timing.CurTime;
            Comp<TimedDespawnComponent>(effect).Lifetime = visual.CompletionDuration;
            Dirty(effect, visual);
        }
        effects.Clear();
    }

    private void ClearRiteVisuals()
    {
        var query = EntityQueryEnumerator<CultistRitualEffectComponent>();
        while (query.MoveNext(out var uid, out _))
            QueueDel(uid);
    }
}
