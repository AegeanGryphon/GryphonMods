using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace QuestRewardFix
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log = null!;

        public override void Load()
        {
            Log = base.Log;
            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            Log.LogInfo("QuestRewardFix loaded.");
        }
    }

    // BUG: After the second hotfix patch, loading a save causes a completed "secret room"
    // side quest to re-complete four times, granting 250 EXP each time (1000 EXP total).
    //
    // Root cause: CheckForQuestCompletion() is called once per quest objective during
    // save deserialization. For a quest with multiple objectives that is already
    // Completed in the save data, each call sees all objectives done and fires
    // AssignRewards() again, re-granting the EXP reward.
    //
    // Fix: prefix CheckForQuestCompletion() — if the quest is already in the Completed
    // state, skip the entire method. There is no valid reason to re-check completion
    // (and re-assign rewards) for a quest that is already finished.

    [HarmonyPatch(typeof(Quest), nameof(Quest.CheckForQuestCompletion))]
    static class Patch_CheckForQuestCompletion
    {
        static bool Prefix(Quest __instance)
        {
            if (__instance.CurrentQuestState == Quest.QuestState.Completed)
            {
                Plugin.Log.LogInfo($"QuestRewardFix: blocked re-completion of already-finished quest '{__instance.Name}'.");
                return false; // skip original
            }

            return true; // quest is still running — proceed normally
        }
    }
}
