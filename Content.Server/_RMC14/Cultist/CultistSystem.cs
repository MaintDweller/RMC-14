using Content.Server._RMC14.Language.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Radio;
using Content.Server.Radio.EntitySystems;
using Content.Server.Roles;
using Content.Shared._RMC14.Cultist;
using Content.Shared._RMC14.Intel;
using Content.Shared._RMC14.Marines.Skills;
using Content.Shared._RMC14.Radio;
using Content.Shared._RMC14.Xenonids.Announce;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._RMC14.Cultist;

public sealed partial class CultistSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly SharedCultistSystem _cultist = default!;
    [Dependency] private readonly CultistInteractionSystem _cultistRules = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly LanguageLearningSystem _learning = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PlayTimeTrackingSystem _playTime = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedCultistRitesSystem _rites = default!;
    [Dependency] private readonly RoleSystem _roles = default!;
    [Dependency] private readonly SkillsSystem _skills = default!;
    [Dependency] private readonly SharedXenoAnnounceSystem _xenoAnnounce = default!;

    private static readonly EntProtoId MindRole = "RMCMindRoleCultist";

    public override void Initialize()
    {
        SubscribeLocalEvent<CultistComponent, CultistConvertedEvent>(OnConverted);
        SubscribeLocalEvent<CultistComponent, RMCRadioMessageAttemptEvent>(OnCultistRadioMessage);
        SubscribeLocalEvent<CultistComponent, XenoAnnouncementEvent>(OnCultistAnnouncement);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnRadioReceiveAttempt);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        InitializeLanguage();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        var query = EntityQueryEnumerator<CultistRuleComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            comp.Rolled = false;
            comp.Active = false;
            comp.SelectionComplete = false;
        }
        _round = null;
    }

    private void OnConverted(Entity<CultistComponent> ent, ref CultistConvertedEvent args)
    {
        ConfigureCultist(ent);
        _rites.Award(args.Leader, ent, CultistDevotionSource.Conversion);
        _xenoAnnounce.AnnounceSameHive(ent.Owner, Loc.GetString("rmc-cultist-converted", ("cultist", ent.Owner)));
        _adminLog.Add(LogType.Mind, LogImpact.High,
            $"{ToPrettyString(args.Leader):actor} converted {ToPrettyString(args.Cultist):target} to the xeno cult");
    }

    private void OnRadioReceiveAttempt(ref RadioReceiveAttemptEvent args)
    {
        if (args.Channel.ID != SharedChatSystem.HivemindChannel)
            return;

        if (_radio.ResolveRadioListener(args.RadioReceiver) is { } listener &&
            !_cultistRules.CanUseHiveCommunication(listener))
            args.Cancelled = true;
    }

    private void OnCultistRadioMessage(Entity<CultistComponent> ent, ref RMCRadioMessageAttemptEvent args)
    {
        if (args.Channel == SharedChatSystem.HivemindChannel && !_cultistRules.CanUseHiveCommunication(ent.Owner))
            args.Cancelled = true;
    }

    private void OnCultistAnnouncement(Entity<CultistComponent> ent, ref XenoAnnouncementEvent args)
    {
        args.Cancelled = !_cultistRules.CanUseHiveCommunication(ent.Owner);
    }

    private void ConfigureCultist(Entity<CultistComponent> ent)
    {
        var uid = ent.Owner;
        RemComp<IntelRescueSurvivorObjectiveComponent>(uid);

        if (!TryComp<MindContainerComponent>(uid, out var container) || container.Mind is not { } mind)
            return;

        if (!_roles.MindHasRole<CultistRoleComponent>(mind))
            _roles.MindAddRole(mind, MindRole);
        if (TryComp<ActorComponent>(uid, out var actor))
        {
            _playTime.PlayerRolesChanged(actor.PlayerSession);
            _chat.DispatchServerMessage(actor.PlayerSession, Loc.GetString(ent.Comp.Leader
                ? "rmc-cultist-leader-greeting"
                : "rmc-cultist-greeting"));
        }
    }
}
