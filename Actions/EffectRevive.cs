using FortRise;
using HarmonyLib;
using System.Runtime.CompilerServices;
using TowerFall;
using static FortRise.VanillaConstants;

namespace CrowdControl.Actions;

public class EffectRevive : Effect, IHookable
{
    private static readonly ConditionalWeakTable<PlayerCorpse, TeamReviver> revivers = new();
    private static readonly AccessTools.FieldRef<LastManStandingRoundLogic, RoundEndCounter> roundEndCounter =
        AccessTools.FieldRefAccess<LastManStandingRoundLogic, RoundEndCounter>("roundEndCounter");

    public override string Code { get; } = "revive";

    public override string[] Mutex { get; } = { "dead" };

    public static void Hook(IHarmony harmony)
    {
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(Player), nameof(Player.Added)),
            postfix: new HarmonyMethod(Player_Added_Postfix)
        );
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(TeamReviver), nameof(TeamReviver.ReviveUpdate)),
            prefix: new HarmonyMethod(TeamReviver_ReviveUpdate_Prefix)
        );
    }

    public override void Start()
    {
        base.Start();
        if (!Active || Level == null || Players == null) { return; }

        for (int i = 0; i < TFGame.PlayerInputs.Length; i++)
        {
            if (Level.GetPlayerOrCorpse(i) is PlayerCorpse corpse && !revivers.TryGetValue(corpse, out _))
            {
                var reviver = new TeamReviver(corpse, TeamReviver.Modes.Quest) { AutoRevive = true };
                Level.Add(reviver);
                revivers.Add(corpse, reviver);
            }
        }
    }

    private static void Player_Added_Postfix(Player __instance)
    {
        var level = __instance.Level;
        if (!level.Ending)
        {
            return;
        }

        // If a player revives while the round is ending, cancel the round end and reset the counter.
        if (level.Session.RoundLogic is LastManStandingRoundLogic roundLogic && !roundLogic.FFACheckForAllButOneDead())
        {
            level.Ending = false;
            roundEndCounter(roundLogic).Reset();
        }
    }

    private static void TeamReviver_ReviveUpdate_Prefix(TeamReviver __instance)
    {
        var level = __instance.Level;
        if (__instance.Finished || !level.Ending)
        {
            return;
        }

        if (level.Session.RoundLogic is LastManStandingRoundLogic roundLogic)
        {
            roundEndCounter(roundLogic).Reset();
        }
    }
}