using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace DualTypeHoloken
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log = null!;

        public override void Load()
        {
            Log = base.Log;
            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            Log.LogInfo("DualTypeHoloken loaded.");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static class HolokenHelper
    {
        /// <summary>
        /// If the active animon's HiddenType satisfies the object's AllowedTypeInteraction
        /// and the player has unlocked that power, returns the hidden EleType to use.
        /// Otherwise returns EleTypes.NONE (meaning: do nothing special).
        /// </summary>
        public static EleTypes GetHiddenTypeOverride(
            HolokenElementalObject target,
            TreyGameplayController ctrl)
        {
            // Primary type already satisfies the object — no override needed.
            if (ctrl.CurrentElement == target.AllowedTypeInteraction)
                return EleTypes.NONE;

            // Get the currently selected animon from the player's party.
            var party = Player.Party;
            if (party == null) return EleTypes.NONE;

            int idx = ctrl.CurrentPartyIndex;
            if (idx < 0 || idx >= party.Count) return EleTypes.NONE;

            var animon = party[idx];
            if (animon == null) return EleTypes.NONE;

            var hiddenType = animon.HiddenType;
            if (hiddenType == EleTypes.NONE) return EleTypes.NONE;

            // Hidden type must match what this object needs.
            if (hiddenType != target.AllowedTypeInteraction) return EleTypes.NONE;

            // Respect the game's unlock gate — player must have this power unlocked.
            if (!ctrl.CanUsePower(hiddenType)) return EleTypes.NONE;

            return hiddenType;
        }
    }

    // ── Patches ───────────────────────────────────────────────────────────────

    // Patch Interact() — the [ContextMenu] / primary public entry point.
    // If the primary type doesn't match but the hidden type does (and is unlocked),
    // we trigger the interaction coroutine directly with the hidden type and skip
    // the original method (which would silently do nothing for the wrong element).
    [HarmonyPatch(typeof(HolokenElementalObject), nameof(HolokenElementalObject.Interact))]
    static class Patch_Interact
    {
        static bool Prefix(HolokenElementalObject __instance)
        {
            var ctrl = Object.FindObjectOfType<TreyGameplayController>();
            if (ctrl == null) return true;

            var hidden = HolokenHelper.GetHiddenTypeOverride(__instance, ctrl);
            if (hidden == EleTypes.NONE) return true; // run original

            Plugin.Log.LogDebug($"DualTypeHoloken: substituting {hidden} for Interact()");
            __instance.StartCoroutine(__instance.InteractionCoroutine(hidden));
            return false; // skip original
        }
    }

    // Patch OnInteraction() — the collision-driven entry point (called when the
    // thrown holoken physically hits a HolokenElementalObject).
    [HarmonyPatch(typeof(HolokenElementalObject), nameof(HolokenElementalObject.OnInteraction))]
    static class Patch_OnInteraction
    {
        static bool Prefix(HolokenElementalObject __instance)
        {
            var ctrl = Object.FindObjectOfType<TreyGameplayController>();
            if (ctrl == null) return true;

            var hidden = HolokenHelper.GetHiddenTypeOverride(__instance, ctrl);
            if (hidden == EleTypes.NONE) return true; // run original

            Plugin.Log.LogDebug($"DualTypeHoloken: substituting {hidden} for OnInteraction()");
            __instance.StartCoroutine(__instance.InteractionCoroutine(hidden));
            return false; // skip original
        }
    }
}
