using Content.Shared.Interaction.Components;
using Content.Shared.Mobs;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistRitesSystem
{
    private void InitializeRobes()
    {
        SubscribeLocalEvent<CultistComponent, MobStateChangedEvent>(OnCultistRevived);
    }

    private void OnCultistRevived(Entity<CultistComponent> ent, ref MobStateChangedEvent args)
    {
        if (_net.IsServer && args.OldMobState == MobState.Dead && args.NewMobState != MobState.Dead &&
            GetGroup(ent) is { Comp.Revealed: true } group)
            EquipRobes(ent, group);
    }

    private void EquipRobes(Entity<CultistComponent> ent, Entity<CultistDevotionComponent> group)
    {
        ent.Comp.Robe = EquipCultClothing(ent, group.Comp.Robe, "outerClothing");
        ent.Comp.Hood = EquipCultClothing(ent, group.Comp.Hood, "head");
        _audio.PlayEntity(group.Comp.RitualSound, ent, ent);
        _popup.PopupEntity(Loc.GetString("rmc-cultist-revelation-robes", ("cultist", ent.Owner)), ent);
    }

    private EntityUid? EquipCultClothing(EntityUid cultist, EntProtoId prototype, string slot)
    {
        if (_inventory.TryGetSlotEntity(cultist, slot, out var existing) &&
            MetaData(existing.Value).EntityPrototype?.ID == prototype.Id)
        {
            EnsureComp<UnremoveableComponent>(existing.Value);
            return existing;
        }

        _inventory.TryUnequip(cultist, slot, silent: true, force: true);
        var clothing = Spawn(prototype, Transform(cultist).Coordinates);
        if (!_inventory.TryEquip(cultist, clothing, slot, silent: true, force: true))
        {
            QueueDel(clothing);
            return null;
        }
        EnsureComp<UnremoveableComponent>(clothing);
        return clothing;
    }

    private void ReleaseRobes(Entity<CultistComponent> ent)
    {
        if (ent.Comp.Robe is { } robe && !TerminatingOrDeleted(robe))
            RemComp<UnremoveableComponent>(robe);
        if (ent.Comp.Hood is { } hood && !TerminatingOrDeleted(hood))
            RemComp<UnremoveableComponent>(hood);
        ent.Comp.Robe = null;
        ent.Comp.Hood = null;
    }
}
