using Content.Shared._RMC14.Xenonids.Construction.ResinWhisper;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Doors;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Cultist;

public sealed partial class SharedCultistSystem
{
    private static readonly ProtoId<AccessLevelPrototype> ResinDoorAccess = "CMAccessXeno";

    private void InitializeEnvironment()
    {
        SubscribeLocalEvent<CultistComponent, GetAccessTagsEvent>(OnGetAccessTags);
        SubscribeLocalEvent<ResinDoorComponent, BeforeDoorOpenedEvent>(OnResinDoorBeforeOpened);
    }

    private void OnGetAccessTags(Entity<CultistComponent> ent, ref GetAccessTagsEvent args)
    {
        if (_rules.HasHiveProtection(ent.AsNullable()))
            args.Tags.Add(ResinDoorAccess);
    }

    private void OnResinDoorBeforeOpened(Entity<ResinDoorComponent> ent, ref BeforeDoorOpenedEvent args)
    {
        if (args.User is { } user && _rules.CanHarmHive(user))
            args.Cancel();
    }
}
