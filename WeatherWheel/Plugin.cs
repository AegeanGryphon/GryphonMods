using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace WeatherWheel
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log = null!;

        public override void Load()
        {
            Log = base.Log;
            ClassInjector.RegisterTypeInIl2Cpp<WeatherWheelUI>();

            var go = new GameObject("WeatherWheelHost");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<WeatherWheelUI>();

            Harmony.CreateAndPatchAll(typeof(Patches));

            Log.LogInfo("WeatherWheel loaded.");
        }
    }

    internal static class Patches
    {
        // Drive WeatherWheelUI.Tick() from WeatherController's own Update loop
        // so we don't rely on Unity calling Update() on our injected MonoBehaviour.
        [HarmonyPatch(typeof(WeatherController), "Update")]
        [HarmonyPostfix]
        static void WeatherController_Update_Postfix() => WeatherWheelUI.Instance?.Tick();

        // Block holoken throw while the wheel is open so left-click selects a
        // weather slice instead of throwing the holoken.
        [HarmonyPatch(typeof(TreyGameplayController), "StartPreparing")]
        [HarmonyPrefix]
        static bool TreyGameplayController_StartPreparing_Prefix()
            => !(WeatherWheelUI.Instance?.IsOpen ?? false);

        // Block the pause/map menu from opening while the wheel is open.
        // Escape is handled by the wheel itself (closes the wheel).
        [HarmonyPatch(typeof(GameMaster), "OpenMenu")]
        [HarmonyPrefix]
        static bool GameMaster_OpenMenu_Prefix()
            => !(WeatherWheelUI.Instance?.IsOpen ?? false);

        [HarmonyPatch(typeof(GameMaster), "OpenMapMenu")]
        [HarmonyPrefix]
        static bool GameMaster_OpenMapMenu_Prefix()
            => !(WeatherWheelUI.Instance?.IsOpen ?? false);
    }
}
