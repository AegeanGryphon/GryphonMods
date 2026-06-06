using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace HoloboardHoloken
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log = null!;

        public override void Load()
        {
            Log = base.Log;
            ClassInjector.RegisterTypeInIl2Cpp<HolokenFlightTracker>();
            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            Log.LogInfo("HoloboardHoloken loaded.");
        }
    }

    // Polls the holoken's position every 100 ms during flight.
    //
    // Attack mode (SlowTimeHit): triggered immediately mid-flight when the animon
    // comes within TriggerRadius — matches OnTriggerEnter timing for land animon.
    //
    // Bilia mode (SlowTimeNew): triggered mid-flight; rb.velocity zeroed to freeze
    // the holoken in place, then SlowTimeNew runs the slow-time QTE and handles
    // holoken return internally.
    //
    // After a bilia trigger, the tracker polls renderers around the target animon
    // for 2 seconds to catch any circle/shadow objects spawned by SlowTimeNew.
    class HolokenFlightTracker : MonoBehaviour
    {
        internal static HolokenFlightTracker? Instance;
        internal static bool HandledThisThrow = false;

        private TreyGameplayController? _tgc;
        private float _nextSample;
        private const float SampleInterval   = 0.1f;
        private const float TriggerRadius    = 4.5f;
        private const float TriggerRadiusSq  = TriggerRadius * TriggerRadius;

        // After a bilia hit, keep re-fixing renderers near the target for this window.
        private float   _biliaFixUntil = 0f;
        private Vector3 _biliaFixOrigin;
        private const float BiliaFixDuration  = 2f;
        private const float BiliaFixRadiusSq  = 100f;  // 10 units

        internal static void StartTracking(TreyGameplayController tgc)
        {
            if (Instance == null)
            {
                var go = new GameObject("HolokenFlightTracker");
                GameObject.DontDestroyOnLoad(go);
                Instance = go.AddComponent<HolokenFlightTracker>();
            }
            Instance._tgc        = tgc;
            Instance._nextSample = Time.time + 0.3f;  // grace period — lets any battle-start cancel resolve
            HandledThisThrow     = false;
        }

        internal static void StopTracking()
        {
            if (Instance != null) Instance._tgc = null;
        }

        public void Update()
        {
            // Keep re-fixing bilia renderers for a window after the encounter triggers.
            if (Time.time < _biliaFixUntil)
                RenderingFixes.FixRenderersNear(_biliaFixOrigin, BiliaFixRadiusSq);

            if (_tgc == null || HandledThisThrow) return;
            if (Time.time < _nextSample) return;
            _nextSample = Time.time + SampleInterval;

            var collider = _tgc.Collider;
            if (collider == null || !collider.gameObject.activeSelf)
            {
                _tgc = null;  // holoken not in flight — stop tracking
                return;
            }

            Vector3 pos = collider.transform.position;

            WildAnimonController? closest = null;
            float closestSq = TriggerRadiusSq;

            foreach (var wac in FindObjectsOfType<WildAnimonController>())
            {
                if (!wac.IsWaterAnimon) continue;
                float dsq = (wac.transform.position - pos).sqrMagnitude;
                if (dsq < closestSq)
                {
                    closestSq = dsq;
                    closest   = wac;
                }
            }

            if (closest == null) return;

            bool isBilia = _tgc.CurrentHolokenMode ==
                           TreyGameplayController.TreyHolokenMode.Bilia;

            var tgc = _tgc;
            _tgc             = null;
            HandledThisThrow = true;

            if (isBilia)
                TriggerBiliaEncounter(tgc, closest, pos);
            else
                TriggerAttackEncounter(tgc, closest, pos);
        }

        static unsafe void TriggerAttackEncounter(TreyGameplayController tgc,
                                                   WildAnimonController target,
                                                   Vector3 hitPos)
        {
            var pc = tgc.PlayerController;
            bool wasLockSpecial = pc != null && pc.LockSpecial;
            bool wasOnHoloboard = pc != null && pc.OnHoloboard;
            if (pc != null) pc.LockSpecial = false;
            if (wasOnHoloboard)
                *(bool*)((nint)pc!.Pointer + 0x418) = false;

            target.IsWaterAnimon = false;
            try
            {
                tgc.SlowTimeHit(hitPos, target.transform, target);
            }
            finally
            {
                target.IsWaterAnimon = true;
                if (pc != null) pc.LockSpecial = wasLockSpecial;
                if (wasOnHoloboard)
                    *(bool*)((nint)pc!.Pointer + 0x418) = true;
            }
        }

        internal unsafe void TriggerBiliaEncounter(TreyGameplayController tgc,
                                                    WildAnimonController target,
                                                    Vector3 hitPos)
        {
            var pc = tgc.PlayerController;
            bool wasLockSpecial = pc != null && pc.LockSpecial;
            bool wasOnHoloboard = pc != null && pc.OnHoloboard;
            if (pc != null) pc.LockSpecial = false;
            if (wasOnHoloboard)
                *(bool*)((nint)pc!.Pointer + 0x418) = false;

            // Start polling renderer fixes around the target animon so that
            // any circle/shadow objects spawned by SlowTimeNew get bumped above water.
            _biliaFixOrigin = target.transform.position;
            _biliaFixUntil  = Time.time + BiliaFixDuration;

            target.IsWaterAnimon = false;
            try
            {
                // Zero the holoken's Rigidbody velocity to freeze it in place.
                // Do NOT call StopHoloken() — that cancels the flight coroutine
                // that SlowTimeNew relies on to return the holoken after the QTE.
                var rb = tgc.Collider?.gameObject.GetComponent<Rigidbody>();
                if (rb != null) rb.velocity = Vector3.zero;

                // SlowTimeNew is a coroutine — must be started via StartCoroutine.
                tgc.StartCoroutine(tgc.SlowTimeNew(target));
            }
            finally
            {
                target.IsWaterAnimon = true;
                if (pc != null) pc.LockSpecial = wasLockSpecial;
                if (wasOnHoloboard)
                    *(bool*)((nint)pc!.Pointer + 0x418) = true;
            }
        }
    }

    // Allow the holoken throw (left-click) at all times while in the overworld,
    // even when the player is riding the holoboard.
    //
    // StartPreparing = mouse-down (begin charging the throw).
    // StopPreparing  = mouse-up   (release / launch the throw).
    //
    // Both methods check PlayerController.LockSpecial and the private
    // _onHoloboard field (offset 0x418) before allowing the throw.
    // We clear those flags for the duration of each call, then restore them
    // so the player stays visually and physically on the holoboard with
    // full holoboard movement intact.

    [HarmonyPatch(typeof(TreyGameplayController), nameof(TreyGameplayController.StartPreparing))]
    static class Patch_StartPreparing
    {
        static bool _wasLockSpecial;
        static bool _wasOnHoloboard;

        static unsafe void Prefix(TreyGameplayController __instance)
        {
            var pc = __instance.PlayerController;
            if (pc == null) return;

            _wasLockSpecial = pc.LockSpecial;
            _wasOnHoloboard = pc.OnHoloboard;

            pc.LockSpecial = false;

            if (_wasOnHoloboard)
                *(bool*)((nint)pc.Pointer + 0x418) = false;
        }

        static unsafe void Postfix(TreyGameplayController __instance)
        {
            var pc = __instance.PlayerController;
            if (pc == null) return;

            pc.LockSpecial = _wasLockSpecial;

            if (_wasOnHoloboard)
            {
                *(bool*)((nint)pc.Pointer + 0x418) = true;
                RenderingFixes.FixAimLine(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(TreyGameplayController), nameof(TreyGameplayController.StopPreparing))]
    static class Patch_StopPreparing
    {
        static bool _wasLockSpecial;
        static bool _wasOnHoloboard;

        static unsafe void Prefix(TreyGameplayController __instance)
        {
            var pc = __instance.PlayerController;
            if (pc == null) return;

            _wasLockSpecial = pc.LockSpecial;
            _wasOnHoloboard = pc.OnHoloboard;

            pc.LockSpecial = false;

            if (_wasOnHoloboard)
                *(bool*)((nint)pc.Pointer + 0x418) = false;
        }

        static unsafe void Postfix(TreyGameplayController __instance)
        {
            var pc = __instance.PlayerController;
            if (pc == null) return;

            pc.LockSpecial = _wasLockSpecial;

            if (_wasOnHoloboard)
            {
                *(bool*)((nint)pc.Pointer + 0x418) = true;
                HolokenFlightTracker.StartTracking(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(TreyGameplayController), nameof(TreyGameplayController.RunCatchSequence))]
    static class Patch_RunCatchSequence
    {
        static void Postfix(TreyGameplayController __instance)
        {
            RenderingFixes.FixBiliaCatchAnimations();
        }
    }

    [HarmonyPatch(typeof(TreyGameplayController), "FinishLaunch")]
    static class Patch_FinishLaunch
    {
        static void Prefix(TreyGameplayController __instance)
        {
            // Stop proximity tracking — all encounters are handled mid-flight.
            HolokenFlightTracker.StopTracking();
        }
    }

    // Bumps the holoken aim line and bilia catch animation renderers to
    // sortingOrder=10 so they draw above the water surface mesh (order=0).
    static class RenderingFixes
    {
        internal const int OverWaterOrder = 10;
        static bool _aimLineFixed = false;

        internal static void FixAimLine(TreyGameplayController tgc)
        {
            if (_aimLineFixed) return;
            _aimLineFixed = true;

            if (tgc.AimLineComponent != null)
                tgc.AimLineComponent.sortingOrder = OverWaterOrder;

            if (tgc.Line != null)
                tgc.Line.sortingOrder = OverWaterOrder;

        }

        // Called every frame for BiliaFixDuration seconds after a bilia encounter
        // triggers. Bumps any non-water renderer within radius to OverWaterOrder,
        // catching circle/shadow objects spawned by SlowTimeNew after trigger time.
        internal static void FixRenderersNear(Vector3 origin, float radiusSq)
        {
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r.sortingOrder >= OverWaterOrder) continue;
                if (r.gameObject.name.ToLower().Contains("water")) continue;
                float dsq = (r.transform.position - origin).sqrMagnitude;
                if (dsq > radiusSq) continue;
                r.sortingOrder = OverWaterOrder;
            }
        }


        internal static void FixBiliaCatchAnimations()
        {
            foreach (var bca in UnityEngine.Object.FindObjectsOfType<BiliaCatchAnimation>())
            {
                foreach (var ps in new[] { bca.VFX, bca.ExtraCatchVFX, bca.CatchSuccessVFX, bca.BreakVFX, bca.RestoreVFX, bca.SuccessRateVFX, bca.HighSuccessTicksVFX, bca.NormalTicksVFX, bca.ShineVFX, bca.FailVFX })
                {
                    if (ps == null) continue;
                    var psr = ps.GetComponent<ParticleSystemRenderer>();
                    if (psr != null) psr.sortingOrder = OverWaterOrder;
                }

                foreach (var mr in bca.GetComponentsInChildren<MeshRenderer>(true))
                    mr.sortingOrder = OverWaterOrder;
            }

            foreach (var s in UnityEngine.Object.FindObjectsOfType<BiliaSendOut>())
            {
                if (s.MeshRenderer != null) s.MeshRenderer.sortingOrder = OverWaterOrder;
                foreach (var mr in s.GetComponentsInChildren<MeshRenderer>(true))
                    mr.sortingOrder = OverWaterOrder;
            }

            foreach (var s in UnityEngine.Object.FindObjectsOfType<SendOutAnimation>())
            {
                if (s.Bilia != null) s.Bilia.sortingOrder = OverWaterOrder;
                foreach (var mr in s.GetComponentsInChildren<MeshRenderer>(true))
                    mr.sortingOrder = OverWaterOrder;
            }

        }
    }
}
