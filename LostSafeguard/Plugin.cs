using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace LostSafeguard
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log = null!;

        public override void Load()
        {
            Log = base.Log;
            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            Log.LogInfo("LostSafeguard loaded.");
        }
    }

    // When a holoken attack triggers a battle, the game records pre-battle damage
    // for each animon in BattleEncounterInfo.StartBattleDamages before the battle
    // scene loads. If that damage >= the animon's current HP, it faints on the
    // opening screen with no warning and no way to prevent it.
    //
    // For Lost animon this is a particularly bad outcome — they're rare variants
    // the player may have been hunting, and losing one to incidental holoken
    // splash damage is frustrating.
    //
    // This patch intercepts the holoken battle start and caps the recorded damage
    // for any Lost animon so it always enters the battle with at least 1 HP.
    // The player still gets to fight it — they just can't accidentally KO it
    // before the battle even begins.
    [HarmonyPatch(typeof(GameMaster), nameof(GameMaster.InitiateBattleHoloken))]
    static class Patch_InitiateBattleHoloken
    {
        static void Prefix(BattleEncounterInfo encounterInfo)
        {
            if (encounterInfo?.StartBattleDamages == null) return;

            // Collect updates separately — modifying a dictionary while iterating throws.
            var updates = new System.Collections.Generic.Dictionary<Animon, int>();

            foreach (var kvp in encounterInfo.StartBattleDamages)
            {
                var animon = kvp.Key;
                if (animon == null || !animon.lost) continue;

                // If this damage would faint the Lost animon, cap it to leave 1 HP.
                if (kvp.Value >= animon.HP)
                    updates[animon] = animon.HP - 1;
            }

            foreach (var kv in updates)
                encounterInfo.StartBattleDamages[kv.Key] = kv.Value;
        }
    }
}
