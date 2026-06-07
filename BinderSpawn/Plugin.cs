using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.Collections.Generic;
using Il2CppCollections = Il2CppSystem.Collections.Generic;

namespace BinderSpawn
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log = null!;

        public override void Load()
        {
            Log = base.Log;
            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            Log.LogInfo("BinderSpawn loaded.");
        }
    }

    // Filters overworld spawns based on cards placed in the player's manual
    // card binder. When at least one card is slotted in the manual binder:
    //   - SpawnAreas whose encounter list contains a matching animon are
    //     filtered to only spawn that animon.
    //   - SpawnAreas with no matching animon are suppressed entirely.
    //
    // When the manual binder is empty, all spawning behaves normally.
    //
    // We patch SpawnArea.Spawn (the synchronous entry point) and temporarily
    // swap m_Encounters for a filtered copy, restoring it in Postfix so the
    // area's encounter data is never permanently modified.
    static class BinderFilter
    {
        internal static HashSet<string> GetBinderFormGuids()
        {
            var result = new HashSet<string>();

            var album = Player.CardAlbum;
            if (album == null) return result;

            var manualInfos = album.ManualViewInfos;
            if (manualInfos == null || manualInfos.Count == 0) return result;

            var cardDb = GameMaster.CardDatabase;
            if (cardDb == null) return result;

            foreach (var viewInfo in manualInfos)
            {
                if (string.IsNullOrEmpty(viewInfo.CardGUID)) continue;
                var cardData = cardDb.GetDataByGUID(viewInfo.CardGUID);
                if (cardData == null) continue;
                var formGuid = cardData.FormData?.FormGUID;
                if (!string.IsNullOrEmpty(formGuid))
                    result.Add(formGuid);
            }

            return result;
        }

        // Returns a filtered encounter list when the binder is active, or null
        // when the binder is empty (caller leaves m_Encounters unchanged).
        // An empty returned list means the binder is active but this SpawnArea
        // has no matching animon — suppress spawning here.
        internal static Il2CppCollections.List<OverworldEncounterData>? BuildFilteredEncounters(
            Il2CppCollections.List<OverworldEncounterData> source)
        {
            var binderGuids = GetBinderFormGuids();
            if (binderGuids.Count == 0) return null;

            var matched = new Il2CppCollections.List<OverworldEncounterData>();
            foreach (var encounter in source)
            {
                if (encounter == null) continue;
                var formGuid = encounter.BaseEncounter?.Animon?.FormGUID;
                if (formGuid != null && binderGuids.Contains(formGuid))
                    matched.Add(encounter);
            }

            return matched;
        }
    }

    [HarmonyPatch(typeof(SpawnArea), nameof(SpawnArea.Spawn))]
    static class Patch_SpawnArea_Spawn
    {
        static readonly Dictionary<int, Il2CppCollections.List<OverworldEncounterData>> _saved = new();

        static void Prefix(SpawnArea __instance)
        {
            var filtered = BinderFilter.BuildFilteredEncounters(__instance.m_Encounters);
            if (filtered == null) return;

            _saved[__instance.GetHashCode()] = __instance.m_Encounters;
            __instance.m_Encounters = filtered;
        }

        static void Postfix(SpawnArea __instance)
        {
            int key = __instance.GetHashCode();
            if (!_saved.TryGetValue(key, out var original)) return;
            __instance.m_Encounters = original;
            _saved.Remove(key);
        }
    }

    // AnimonSpawner overrides Spawn() so it needs its own patch.
    [HarmonyPatch(typeof(AnimonSpawner), nameof(AnimonSpawner.Spawn))]
    static class Patch_AnimonSpawner_Spawn
    {
        static readonly Dictionary<int, Il2CppCollections.List<OverworldEncounterData>> _saved = new();

        static void Prefix(AnimonSpawner __instance)
        {
            var filtered = BinderFilter.BuildFilteredEncounters(__instance.m_Encounters);
            if (filtered == null) return;

            _saved[__instance.GetHashCode()] = __instance.m_Encounters;
            __instance.m_Encounters = filtered;
        }

        static void Postfix(AnimonSpawner __instance)
        {
            int key = __instance.GetHashCode();
            if (!_saved.TryGetValue(key, out var original)) return;
            __instance.m_Encounters = original;
            _saved.Remove(key);
        }
    }
}
