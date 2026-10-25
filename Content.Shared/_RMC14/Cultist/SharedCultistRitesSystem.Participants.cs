using Content.Shared.Damage;
using Robust.Shared.Player;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistRitesSystem
{
    private void InitializeRiteParticipants()
    {
        SubscribeLocalEvent<CultistComponent, DamageChangedEvent>(OnRiteParticipantDamaged);
        SubscribeLocalEvent<CultistComponent, CultistLeftEvent>(OnRiteParticipantLeft);
        SubscribeLocalEvent<CultistComponent, PlayerDetachedEvent>(OnRiteParticipantDetached);
        SubscribeLocalEvent<CultistDevotionComponent, ComponentShutdown>(OnRiteGroupShutdown);
    }

    private void OnRiteParticipantDamaged(Entity<CultistComponent> ent, ref DamageChangedEvent args)
    {
        if (_net.IsServer && args.DamageIncreased)
            CancelRitesByParticipant(ent);
    }

    private void OnRiteParticipantLeft(Entity<CultistComponent> ent, ref CultistLeftEvent args)
    {
        if (_net.IsClient)
            return;
        CancelRitesByParticipant(ent, leaving: true);
        ReleaseRobes(ent);
    }

    private void OnRiteParticipantDetached(Entity<CultistComponent> ent, ref PlayerDetachedEvent args)
    {
        if (_net.IsServer)
            CancelRitesByParticipant(ent);
    }

    private void OnRiteGroupShutdown(Entity<CultistDevotionComponent> ent, ref ComponentShutdown args)
    {
        if (_net.IsClient)
            return;
        CancelSacrifice(ent);
        CancelRevelation(ent);
    }

    private void CancelRitesByParticipant(EntityUid participant, bool leaving = false)
    {
        var query = EntityQueryEnumerator<CultistDevotionComponent>();
        while (query.MoveNext(out var uid, out var group))
        {
            if (leaving)
                group.Members.Remove(participant);
            if (group.Sacrifice is { } sacrifice &&
                (sacrifice.Initiator == participant || sacrifice.Supporter == participant))
                CancelSacrifice((uid, group));
            if (group.Revelation is { } revelation && revelation.Participants.ContainsKey(participant))
                CancelRevelation((uid, group));
        }
    }

    private bool InRitualRange(EntityUid participant, EntityUid target, Entity<CultistDevotionComponent> group)
    {
        return !EntityManager.IsQueuedForDeletion(participant) && !_container.IsEntityInContainer(participant) &&
               _interaction.InRangeUnobstructed(participant, target, group.Comp.RitualRange);
    }
}
