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
        internal static (HashSet<string> formGuids, HashSet<string> speciesNames) GetBinderSets()
        {
            var formGuids = new HashSet<string>();
            var speciesNames = new HashSet<string>();

            var album = Player.CardAlbum;
            if (album == null) return (formGuids, speciesNames);

            var manualInfos = album.ManualViewInfos;
            if (manualInfos == null || manualInfos.Count == 0) return (formGuids, speciesNames);

            var cardDb = GameMaster.CardDatabase;
            if (cardDb == null) return (formGuids, speciesNames);

            var formDb = GameMaster.FormDatabase;

            foreach (var viewInfo in manualInfos)
            {
                if (string.IsNullOrEmpty(viewInfo.CardGUID)) continue;
                var cardData = cardDb.GetDataByGUID(viewInfo.CardGUID);
                if (cardData == null) continue;
                var formGuid = cardData.FormData?.FormGUID;
                if (string.IsNullOrEmpty(formGuid)) continue;

                formGuids.Add(formGuid);

                if (formDb != null)
                {
                    var formData = formDb.GetDataByGUID(formGuid);
                    var species = formData?.ParentData?.Species;
                    if (!string.IsNullOrEmpty(species))
                        speciesNames.Add(species);
                }
            }

            return (formGuids, speciesNames);
        }

        // Returns a filtered encounter list when the binder is active, or null
        // when the binder is empty (caller leaves m_Encounters unchanged).
        // An empty returned list means the binder is active but this SpawnArea
        // has no matching animon — suppress spawning here.
        // Matching is by exact FormGUID first, then by species name — so a single
        // card covers all regional/variant forms of the same animon (e.g. Minube).
        internal static Il2CppCollections.List<OverworldEncounterData>? BuildFilteredEncounters(
            Il2CppCollections.List<OverworldEncounterData> source, string areaName)
        {
            var (binderGuids, binderSpecies) = GetBinderSets();

            Plugin.Log.LogInfo($"[BinderSpawn] Area={areaName} | binderGuids=[{string.Join(", ", binderGuids)}] | binderSpecies=[{string.Join(", ", binderSpecies)}]");

            if (binderGuids.Count == 0 && binderSpecies.Count == 0)
            {
                Plugin.Log.LogInfo($"[BinderSpawn] Binder empty — no filtering.");
                return null;
            }

            var matched = new Il2CppCollections.List<OverworldEncounterData>();
            foreach (var encounter in source)
            {
                if (encounter == null) continue;
                var formGuid = encounter.BaseEncounter?.Animon?.FormGUID;
                var species = encounter.BaseEncounter?.Data?.Species;
                var formName = encounter.BaseEncounter?.FormData?.FormName;
                Plugin.Log.LogInfo($"[BinderSpawn]   encounter: formName={formName} species={species} guid={formGuid}");

                if (formGuid != null && binderGuids.Contains(formGuid))
                {
                    Plugin.Log.LogInfo($"[BinderSpawn]     -> GUID match");
                    matched.Add(encounter);
                    continue;
                }
                if (species != null && binderSpecies.Contains(species))
                {
                    Plugin.Log.LogInfo($"[BinderSpawn]     -> species match");
                    matched.Add(encounter);
                    continue;
                }
                Plugin.Log.LogInfo($"[BinderSpawn]     -> no match");
            }

            Plugin.Log.LogInfo($"[BinderSpawn] Result: {matched.Count}/{source.Count} encounters kept.");
            return matched;
        }
    }

    [HarmonyPatch(typeof(SpawnArea), nameof(SpawnArea.Spawn))]
    static class Patch_SpawnArea_Spawn
    {
        static readonly Dictionary<int, Il2CppCollections.List<OverworldEncounterData>> _saved = new();

        static void Prefix(SpawnArea __instance)
        {
            var filtered = BinderFilter.BuildFilteredEncounters(__instance.m_Encounters, __instance.gameObject.name);
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
            var filtered = BinderFilter.BuildFilteredEncounters(__instance.m_Encounters, __instance.gameObject.name);
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
