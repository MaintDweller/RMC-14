using Content.Shared._RMC14.Cultist;
using Content.Shared._RMC14.Rules;
using Content.Shared._RMC14.Synth;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.GameTicking.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind.Components;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._RMC14.Cultist;

public sealed partial class CultistSystem
{
    private static readonly ProtoId<SpeciesPrototype> AdminCultistSpecies = "Human";
    private static readonly SpriteSpecifier.Rsi CultistVerbIcon =
        new(new ResPath("/Textures/_RMC14/Cultist/hud.rsi"), "cultist");
    private static readonly SpriteSpecifier.Rsi CultistLeaderVerbIcon =
        new(new ResPath("/Textures/_RMC14/Cultist/hud.rsi"), "leader");

    private void InitializeAdmin()
    {
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetAdminVerbs);
    }

    private void OnGetAdminVerbs(GetVerbsEvent<Verb> args)
    {
        if (!_admin.HasAdminFlag(args.User, AdminFlags.Fun) || !CanAdminMakeCultist(args.Target))
            return;

        var noHive = GetAdminCultistHive(args.Target) == null;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("rmc-cultist-admin-make-cultist"),
            Category = VerbCategory.Antag,
            Icon = CultistVerbIcon,
            Disabled = noHive,
            Message = Loc.GetString(noHive
                ? "rmc-cultist-admin-no-hive"
                : "rmc-cultist-admin-make-cultist-description"),
            Act = () => AdminMakeCultist(args.User, args.Target, false),
            Impact = LogImpact.High,
        });
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("rmc-cultist-admin-make-leader"),
            Category = VerbCategory.Antag,
            Icon = CultistLeaderVerbIcon,
            Disabled = noHive,
            Message = Loc.GetString(noHive
                ? "rmc-cultist-admin-no-hive"
                : "rmc-cultist-admin-make-leader-description"),
            Act = () => AdminMakeCultist(args.User, args.Target, true),
            Impact = LogImpact.High,
        });
    }

    private bool CanAdminMakeCultist(EntityUid target)
    {
        return !TerminatingOrDeleted(target) && !_mobState.IsDead(target) && !HasComp<SynthComponent>(target) &&
               HasComp<ActorComponent>(target) &&
               TryComp<MindContainerComponent>(target, out var mind) && mind.HasMind &&
               TryComp<HumanoidAppearanceComponent>(target, out var appearance) &&
               appearance.Species == AdminCultistSpecies;
    }

    private EntityUid? GetAdminCultistHive(EntityUid target)
    {
        if (_cultist.GetSupportedHive(target) is { } existing)
            return existing.Owner;

        var rules = EntityQueryEnumerator<CMDistressSignalRuleComponent, ActiveGameRuleComponent>();
        while (rules.MoveNext(out _, out var rule, out _))
        {
            if (!TerminatingOrDeleted(rule.Hive) && HasComp<HiveComponent>(rule.Hive))
                return rule.Hive;
        }

        EntityUid? hive = null;
        var hives = EntityQueryEnumerator<HiveComponent>();
        while (hives.MoveNext(out var uid, out _))
        {
            if (TerminatingOrDeleted(uid))
                continue;
            if (hive != null)
                return null;
            hive = uid;
        }

        return hive;
    }

    private void AdminMakeCultist(EntityUid admin, EntityUid target, bool leader)
    {
        if (!_admin.HasAdminFlag(admin, AdminFlags.Fun) || !CanAdminMakeCultist(target))
            return;

        if (GetAdminCultistHive(target) is not { } hive)
        {
            if (TryComp<ActorComponent>(admin, out var actor))
                _chat.DispatchServerMessage(actor.PlayerSession, Loc.GetString("rmc-cultist-admin-no-hive"));
            return;
        }

        _cultist.MakeCultist(target, hive, leader);
        ConfigureCultist((target, Comp<CultistComponent>(target)));
        _adminLog.Add(LogType.Mind, LogImpact.High,
            $"{ToPrettyString(admin):actor} administratively made {ToPrettyString(target):target} " +
            $"a cult {(leader ? "leader" : "member")} supporting {ToPrettyString(hive):hive}");
    }
}
