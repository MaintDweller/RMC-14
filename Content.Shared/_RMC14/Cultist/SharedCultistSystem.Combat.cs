using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared.Damage;
using Content.Shared.Interaction.Events;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistSystem
{
    private void InitializeCombat()
    {
        SubscribeLocalEvent<CultistComponent, CultistFriendlyFireActionEvent>(OnFriendlyFireAction);
        SubscribeLocalEvent<CultistComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<HiveMemberComponent, BeforeDamageChangedEvent>(OnHiveBeforeDamage);
    }

    private void OnFriendlyFireAction(Entity<CultistComponent> ent, ref CultistFriendlyFireActionEvent args)
    {
        if (args.Handled || !CanUse(ent))
            return;

        args.Handled = true;
        ent.Comp.AllowFriendlyFire = !ent.Comp.AllowFriendlyFire;
        Dirty(ent);
        _actions.SetToggled(ent.Comp.FriendlyFireActionEntity, ent.Comp.AllowFriendlyFire);
        _movementSpeed.RefreshMovementSpeedModifiers(ent);
        _popup.PopupClient(Loc.GetString(ent.Comp.AllowFriendlyFire
            ? "rmc-cultist-friendly-fire-enabled"
            : "rmc-cultist-friendly-fire-disabled"), ent, ent);
    }

    private void OnAttackAttempt(Entity<CultistComponent> ent, ref AttackAttemptEvent args)
    {
        if (!args.Disarm && args.Target is { } target && _rules.BlocksCultistDamage(ent.AsNullable(), target))
            args.Cancel();
    }

    private void OnHiveBeforeDamage(Entity<HiveMemberComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Damage.GetTotal() <= 0 || args.Origin is not { } origin)
            return;

        if (_rules.BlocksCultistDamage(origin, ent.Owner))
            args.Cancelled = true;
    }
}
