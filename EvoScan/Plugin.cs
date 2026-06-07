using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace EvoScan
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log = null!;

        public override void Load()
        {
            Log = base.Log;
            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            Log.LogInfo("EvoScan loaded.");
        }
    }

    // When an animon evolves, automatically grant the equivalent of 3 battle
    // scans for the evolved form in the AniWiki, plus mark it as caught (since
    // the player already owns it). This pushes the entry to KnowledgeLevel 3
    // and triggers the Complete star immediately.
    //
    // Progression facts (from IL2CPP dump):
    //   KnowledgeLevel  = AniWikiEntry.ExtraLevel  (scan counter, 0–3)
    //   Complete (star) = Caught == true && KnowledgeLevel >= 3
    //   Each battle Scan press increments ExtraLevel by 1.
    //   CaughtLost / KnownTypes do NOT affect this progression.
    [HarmonyPatch(typeof(Animon), nameof(Animon.Evolve))]
    static class Patch_Animon_Evolve
    {
        static void Postfix(Animon __instance)
        {
            try
            {
                var playerState = Player.PlayerState;
                if (playerState == null)
                {
                    Plugin.Log.LogWarning("EvoScan: PlayerState is null, skipping.");
                    return;
                }

                var wiki = playerState.AniWikiInfo;
                if (wiki == null)
                {
                    Plugin.Log.LogWarning("EvoScan: AniWikiInfo is null, skipping.");
                    return;
                }

                var form = __instance.formData;
                if (form == null)
                {
                    Plugin.Log.LogWarning("EvoScan: formData is null after evolution, skipping.");
                    return;
                }

                // Set Seen and Caught — the player owns this animon since they evolved it.
                // triggerAchievements: true so any dex achievements fire legitimately.
                wiki.UpdateEntry(__instance, seen: true, caught: true, triggerAchievements: true);

                // Set ExtraLevel = 3, which is what KnowledgeLevel returns directly.
                // This is equivalent to having scanned in battle 3 times.
                // Combined with Caught = true, Complete (the star) will also be true.
                var entry = wiki.GetEntry(form);
                if (entry == null)
                {
                    Plugin.Log.LogWarning("EvoScan: No AniWikiEntry found for evolved form, skipping.");
                    return;
                }

                entry.ExtraLevel = 3;

                Plugin.Log.LogInfo($"EvoScan: Granted KnowledgeLevel 3 for evolved form (GUID: {entry.AnimonFormGUID}).");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"EvoScan: Exception in Evolve postfix: {ex}");
            }
        }
    }
}
