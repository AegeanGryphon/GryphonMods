using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace ScanIndicator
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log = null!;

        public override void Load()
        {
            Log = base.Log;
            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            Log.LogInfo("ScanIndicator loaded.");
        }
    }

    // ── Player portrait cards (bottom row) ─────────────────────────────────
    // SideDatabox is the portrait card for player-side animon.
    // Setup() fires when the card is bound to a battler.
    [HarmonyPatch(typeof(SideDatabox), nameof(SideDatabox.Setup))]
    static class Patch_SideDatabox_Setup
    {
        static void Postfix(SideDatabox __instance, NewBattleDataBox databox) =>
            PlayerDiamonds.Attach(__instance, databox);
    }

    // BattleUI.Appear() fires after the canvas is fully laid out.
    [HarmonyPatch(typeof(BattleUI), nameof(BattleUI.Appear))]
    static class Patch_BattleUI_Appear
    {
        static void Postfix(BattleUI __instance)
        {
            PlayerDiamonds.RepositionAll();

            // Single-enemy floating card — attach after full layout.
            if (__instance?.DataBoxes == null) return;
            int enemyCount = 0;
            NewBattleDataBox? enemyBox = null;
            foreach (var db in __instance.DataBoxes)
            {
                if (db == null) continue;
                if (db.LowerDatabox == null) { enemyCount++; enemyBox = db; }
            }
            if (enemyCount == 1 && enemyBox != null)
                FloatingEnemyDiamonds.Attach(enemyBox);
        }
    }

    // ── Enemy portrait cards (top-right condensed cards) ───────────────────
    // SpecificEnemyDatabox is the actual enemy portrait card component.
    // UpdateDatabox() fires whenever the card syncs with its NewBattleDataBox.
    [HarmonyPatch(typeof(SpecificEnemyDatabox), nameof(SpecificEnemyDatabox.UpdateDatabox),
        new[] { typeof(NewBattleDataBox), typeof(bool) })]
    static class Patch_SpecificEnemyDatabox_UpdateDatabox
    {
        static void Postfix(SpecificEnemyDatabox __instance, NewBattleDataBox target) =>
            EnemyDiamonds.Attach(__instance, target);
    }

    // ── Scan event ─────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(AniWikiInfo), nameof(AniWikiInfo.Scan))]
    static class Patch_AniWikiInfo_Scan
    {
        static void Postfix(Animon animon)
        {
            PlayerDiamonds.RefreshForAnimon(animon);
            EnemyDiamonds.RefreshForAnimon(animon);
            FloatingEnemyDiamonds.RefreshForAnimon(animon);
        }
    }

    // ── Shared helpers ─────────────────────────────────────────────────────
    static class DiamondHelper
    {
        public const float Size   = 8f;
        public const float Gap    = 11f;
        public const float Margin = 2f;
        public static readonly Color Gray = new(0.25f, 0.25f, 0.25f, 0.85f);

        static Sprite? _sprite;

        public static Sprite GetSprite()
        {
            if (_sprite != null) return _sprite;
            const int Res = 32;
            var tex   = new Texture2D(Res, Res, TextureFormat.RGBA32, false);
            float half = Res * 0.5f;
            float r    = half - 1.5f;
            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                float dx    = Mathf.Abs(x + 0.5f - half);
                float dy    = Mathf.Abs(y + 0.5f - half);
                float alpha = Mathf.Clamp01(r - (dx + dy) + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            tex.Apply();
            _sprite = Sprite.Create(tex, new Rect(0, 0, Res, Res), new Vector2(0.5f, 0.5f));
            return _sprite;
        }

        public static Image[] CreateDiamonds(Transform parent)
        {
            var diamonds = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var go  = new GameObject($"ScanDiamond_{i}");
                go.transform.SetParent(parent, false);
                var img            = go.AddComponent<Image>();
                img.sprite         = GetSprite();
                img.type           = Image.Type.Simple;
                img.preserveAspect = true;
                img.color          = Gray;
                img.rectTransform.sizeDelta = new Vector2(Size, Size);
                diamonds[i] = img;
            }
            return diamonds;
        }

        // xStep: pixels left/right per successive diamond (negative = left, for parallelogram slant).
        public static void Position(Image[] diamonds, RectTransform card, float xOffset = 0f, float yOffset = 0f, float xStep = 0f)
        {
            if (card == null) return;
            try
            {
                var rect   = card.rect;
                float x    = rect.xMin + Margin + Size * 0.5f + xOffset;
                float yTop = rect.yMax - Margin - Size * 0.5f + yOffset;
                for (int i = 0; i < 3; i++)
                    diamonds[i].rectTransform.anchoredPosition = new Vector2(x + i * xStep, yTop - i * Gap);
            }
            catch { }
        }

        // Absolute position variant — anchor is already in card's local space.
        public static void PositionAt(Image[] diamonds, float x, float yTop, float xStep = 0f)
        {
            for (int i = 0; i < 3; i++)
                diamonds[i].rectTransform.anchoredPosition = new Vector2(x + i * xStep, yTop - i * Gap);
        }

        public static void Color(Image[] diamonds, int level)
        {
            for (int i = 0; i < 3; i++)
                if (diamonds[i] != null)
                    diamonds[i].color = i < level ? UnityEngine.Color.white : Gray;
        }

        public static int KnowledgeLevel(NewBattleDataBox databox)
        {
            try
            {
                var animon   = databox?.Battler?.Animon;
                if (animon == null) return 0;
                var wikiInfo = Player.PlayerState?.AniWikiInfo;
                if (wikiInfo == null) return 0;
                return wikiInfo.GetEntry(animon.formData)?.KnowledgeLevel ?? 0;
            }
            catch { return 0; }
        }
    }

    // ── Player card diamonds ───────────────────────────────────────────────
    static class PlayerDiamonds
    {
        record Entry(Image[] Diamonds, NewBattleDataBox Databox, RectTransform CardRT);
        static readonly Dictionary<SideDatabox, Entry> _map = new();

        public static void Attach(SideDatabox box, NewBattleDataBox databox)
        {
            if (box == null || databox == null) return;

            // Use BG image RT — its rect matches the visible parallelogram card bounds.
            var cardRT = box.BG?.rectTransform ?? box.GetComponent<RectTransform>();
            if (cardRT == null) return;

            if (_map.TryGetValue(box, out var existing))
            {
                // Card reused for a different battler — just recolor.
                var updated = existing with { Databox = databox };
                _map[box] = updated;
                DiamondHelper.Color(updated.Diamonds, DiamondHelper.KnowledgeLevel(databox));
                return;
            }

            var diamonds = DiamondHelper.CreateDiamonds(cardRT);
            _map[box] = new Entry(diamonds, databox, cardRT);
            DiamondHelper.Position(diamonds, cardRT);
            DiamondHelper.Color(diamonds, DiamondHelper.KnowledgeLevel(databox));
            Plugin.Log.LogInfo($"PlayerDiamonds.Attach: cardRT.rect={cardRT.rect}");
        }

        public static void RepositionAll()
        {
            foreach (var (_, entry) in _map)
                DiamondHelper.Position(entry.Diamonds, entry.CardRT);
            Plugin.Log.LogInfo($"PlayerDiamonds.RepositionAll: {_map.Count} card(s)");
        }

        public static void RefreshForAnimon(Animon animon)
        {
            foreach (var (_, entry) in _map)
            {
                try
                {
                    if (entry.Databox?.Battler?.Animon == animon)
                    { DiamondHelper.Color(entry.Diamonds, DiamondHelper.KnowledgeLevel(entry.Databox)); return; }
                }
                catch { }
            }
        }
    }

    // ── Enemy card diamonds ────────────────────────────────────────────────
    static class EnemyDiamonds
    {
        record Entry(Image[] Diamonds, NewBattleDataBox Databox);
        static readonly Dictionary<SpecificEnemyDatabox, Entry> _map = new();

        public static void Attach(SpecificEnemyDatabox box, NewBattleDataBox databox)
        {
            if (box == null || databox == null) return;

            var cardRT = box.rect;
            if (cardRT == null) return;

            if (_map.TryGetValue(box, out var existing))
            {
                // Card refreshed — recolor only, no need to recreate.
                var updated = existing with { Databox = databox };
                _map[box] = updated;
                DiamondHelper.Color(updated.Diamonds, DiamondHelper.KnowledgeLevel(databox));
                return;
            }

            var diamonds = DiamondHelper.CreateDiamonds(cardRT);
            _map[box] = new Entry(diamonds, databox);

            // Compute top-left of the portrait icon in rootRT local space, then offset in.
            var iconRT  = box.AnimonIcon?.GetComponent<RectTransform>();
            if (iconRT != null)
            {
                var  iconPos = box.AnimonIcon.transform.localPosition;
                var  iconRect = iconRT.rect;
                // Use rootRT left edge — icon rect varies per animon size so don't use it.
                float x    = cardRT.rect.xMin + DiamondHelper.Margin + DiamondHelper.Size * 0.5f + 16f;
                float yTop = cardRT.rect.yMax - DiamondHelper.Margin - DiamondHelper.Size * 0.5f - 3f;
                // Clamp yTop to the rootRT card bounds so diamonds stay inside the visible strip.
                yTop = Mathf.Min(yTop, cardRT.rect.yMax - DiamondHelper.Margin - DiamondHelper.Size * 0.5f);
                DiamondHelper.PositionAt(diamonds, x, yTop, xStep: -3.5f);
                Plugin.Log.LogInfo($"EnemyDiamonds: iconPos={iconPos}, iconRect={iconRect}, x={x}, yTop={yTop}");
            }
            else
            {
                DiamondHelper.Position(diamonds, cardRT, xOffset: 0f, yOffset: 0f, xStep: -5f);
                Plugin.Log.LogInfo($"EnemyDiamonds: no iconRT, rootRT={cardRT.rect}");
            }
            DiamondHelper.Color(diamonds, DiamondHelper.KnowledgeLevel(databox));
        }

        public static void RefreshForAnimon(Animon animon)
        {
            foreach (var (_, entry) in _map)
            {
                try
                {
                    if (entry.Databox?.Battler?.Animon == animon)
                    { DiamondHelper.Color(entry.Diamonds, DiamondHelper.KnowledgeLevel(entry.Databox)); return; }
                }
                catch { }
            }
        }
    }

    // ── Floating single-enemy card diamonds ────────────────────────────────
    static class FloatingEnemyDiamonds
    {
        record Entry(Image[] Diamonds, NewBattleDataBox Box);
        static readonly Dictionary<NewBattleDataBox, Entry> _map = new();

        public static void Attach(NewBattleDataBox box)
        {
            if (box == null) return;

            // Parent diamonds to the CaughtIcon's parent so they sit in the same
            // coordinate space. Place them horizontally to the right of the icon slot.
            var iconRT = box.CaughtIcon?.rectTransform;
            var parent = iconRT ?? box.GetComponent<RectTransform>();
            if (parent == null) return;

            if (_map.TryGetValue(box, out var existing))
            {
                DiamondHelper.Color(existing.Diamonds, KnowledgeLevel(box));
                return;
            }

            var diamonds = DiamondHelper.CreateDiamonds(parent);
            _map[box] = new Entry(diamonds, box);

            // Position horizontally to the right of the icon slot.
            // Use the icon rect's right edge as the starting x, centred on its y.
            if (iconRT != null)
            {
                var r     = iconRT.rect;
                float startX = r.xMax + DiamondHelper.Gap;
                float y      = 0f; // centred on icon pivot
                for (int i = 0; i < 3; i++)
                    diamonds[i].rectTransform.anchoredPosition =
                        new Vector2(startX + i * DiamondHelper.Gap, y);
                Plugin.Log.LogInfo($"FloatingEnemyDiamonds.Attach: iconRT.rect={r}, startX={startX}");
            }
            else
            {
                // Fallback — top-left of card
                DiamondHelper.Position(diamonds, parent);
                Plugin.Log.LogInfo("FloatingEnemyDiamonds.Attach: no CaughtIcon, using card RT");
            }

            DiamondHelper.Color(diamonds, KnowledgeLevel(box));
        }

        public static void RefreshForAnimon(Animon animon)
        {
            foreach (var (_, entry) in _map)
            {
                try
                {
                    if (entry.Box?.Battler?.Animon == animon)
                    { DiamondHelper.Color(entry.Diamonds, KnowledgeLevel(entry.Box)); return; }
                }
                catch { }
            }
        }

        static int KnowledgeLevel(NewBattleDataBox box)
        {
            try
            {
                var animon   = box?.Battler?.Animon;
                if (animon == null) return 0;
                var wikiInfo = Player.PlayerState?.AniWikiInfo;
                if (wikiInfo == null) return 0;
                return wikiInfo.GetEntry(animon.formData)?.KnowledgeLevel ?? 0;
            }
            catch { return 0; }
        }
    }

}
