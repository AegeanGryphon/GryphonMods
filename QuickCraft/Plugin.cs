using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace QuickCraft
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log = null!;

        public override void Load()
        {
            Log = base.Log;
            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            Log.LogInfo("QuickCraft loaded.");
        }
    }

    // The fountain crafting menu requires holding the craft button for
    // TotalTimeForCraft seconds per item (roughly 1 second by default).
    // Crafting hundreds of items therefore takes several minutes of holding.
    //
    // Fix: override TotalTimeForCraft to a small value each time
    // HandleCrafting() runs, so the hold threshold is met almost immediately.
    // The progress bar still fills (just very quickly), and all success/fail
    // logic runs unchanged.

    [HarmonyPatch(typeof(CraftingMenu), nameof(CraftingMenu.HandleCrafting))]
    static class Patch_HandleCrafting
    {
        // Short enough to feel instant, non-zero to avoid any divide-by-zero
        // in progress bar fill calculations (bar = holdTime / totalTime).
        const float CraftTime = 0.1f;

        static void Prefix(CraftingMenu __instance)
        {
            __instance.TotalTimeForCraft = CraftTime;
        }
    }
}
