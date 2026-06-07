using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WeatherWheel
{
    public class WeatherWheelUI : MonoBehaviour
    {
        public WeatherWheelUI(IntPtr ptr) : base(ptr) { }

        // ── Constants ─────────────────────────────────────────────────────────

        private const int   SliceCount    = 10;
        private const float SliceDeg      = 360f / SliceCount;
        private const float StartDeg      = -90f;
        private const float LayoutRadius  = 168f;
        private const int   PinDuration   = 999_999;
        private const float StickDeadZone = 0.30f;
        private const float MouseDeadZone = 50f;

        // ── Singleton ─────────────────────────────────────────────────────────

        public static WeatherWheelUI? Instance { get; private set; }

        void Awake() => Instance = this;

        // ── State ─────────────────────────────────────────────────────────────

        private static readonly HashSet<string> _modPinnedMaps = new();

        private bool _isOpen;
        private int  _highlighted = -1;
        private int  _activeSlice = 0;

        // ── UI references ─────────────────────────────────────────────────────

        private GameObject? _canvasGO;
        private GameObject? _wheelRoot;
        private Font?       _font;
        private Sprite?     _pill;   // procedural rounded-rect sprite

        private readonly List<Image>    _pillBgs   = new();
        private readonly List<Text>     _pillTexts = new();
        private readonly List<Weathers> _sliceTypes = new();

        private Text? _statusText;

        // ── Colours ───────────────────────────────────────────────────────────

        private static readonly Color ColPillDefault  = new(0.04f, 0.02f, 0.14f, 0.78f);
        private static readonly Color ColPillHover    = new(0.40f, 0.24f, 0.80f, 0.95f);
        private static readonly Color ColPillActive   = new(0.20f, 0.10f, 0.46f, 0.90f);
        private static readonly Color ColPillLocked   = new(0.18f, 0.18f, 0.18f, 0.58f);
        private static readonly Color ColLabelDefault = new(0.88f, 0.86f, 1.00f, 0.95f);
        private static readonly Color ColLabelHover   = new(1.00f, 1.00f, 1.00f, 1.00f);
        private static readonly Color ColLabelLocked  = new(0.55f, 0.55f, 0.55f, 0.80f);
        private static readonly Color ColHintBar      = new(0.04f, 0.02f, 0.14f, 0.90f);

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick()
        {
            HandleOpenClose();
            if (_isOpen)
            {
                UpdateHighlight();
                HandleSelect();
            }
        }

        // ── Input ─────────────────────────────────────────────────────────────

        void HandleOpenClose()
        {
            var gp = Gamepad.current;

            bool openPressed =
                Input.GetKeyDown(KeyCode.C) ||
                (gp != null && gp.rightStickButton.wasPressedThisFrame);
            if (openPressed && CanOpen()) { Toggle(); return; }

            bool closePressed =
                Input.GetKeyDown(KeyCode.Escape)   ||
                Input.GetMouseButtonDown(1)         ||
                (gp != null && gp.buttonEast.wasPressedThisFrame);
            if (closePressed && _isOpen) Close();
        }

        void UpdateHighlight()
        {
            int next = (int)Weathers.Clear;   // default to Clear when nothing is aimed at

            var gp    = Gamepad.current;
            Vector2 stick = gp != null ? gp.rightStick.ReadValue() : Vector2.zero;

            if (stick.magnitude >= StickDeadZone)
            {
                // Controller: angle from stick direction
                next = AngleToSlice(Mathf.Atan2(stick.y, stick.x) * Mathf.Rad2Deg);
            }
            else
            {
                // Mouse: must be physically over an icon — no angle inference
                float scale      = Screen.height / 1080f;
                float hoverRadSq = (44f * scale) * (44f * scale);
                Vector2 centre   = WheelScreenCentre();
                Vector2 mouse    = Input.mousePosition;

                for (int i = 0; i < SliceCount; i++)
                {
                    float ang     = SliceDeg * i + StartDeg;
                    float rad     = ang * Mathf.Deg2Rad;
                    Vector2 ipos  = centre + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad))
                                           * LayoutRadius * scale;
                    if ((mouse - ipos).sqrMagnitude <= hoverRadSq)
                    {
                        next = i;
                        break;
                    }
                }
            }

            if (next != _highlighted)
            {
                _highlighted = next;
                ApplyHighlightColours();
            }
        }

        void HandleSelect()
        {
            var gp = Gamepad.current;
            bool pressed =
                Input.GetMouseButtonDown(0) ||
                (gp != null && gp.buttonSouth.wasPressedThisFrame);
            if (!pressed) return;
            if (IsStoryWeather(CurrentMapGuid())) return;
            if (_highlighted < 0 || _highlighted >= _sliceTypes.Count) return;
            if (_sliceTypes[_highlighted] == Weathers.Clear)
                ApplyAndUnpin();
            else
                ApplyWeather(_sliceTypes[_highlighted]);
        }

        // ── Open / close ──────────────────────────────────────────────────────

        bool CanOpen()
            => WeatherController.Instance != null &&
               !WeatherController.Instance.IsInsideOrAnispace;

        void Toggle() { if (_isOpen) Close(); else Open(); }

        void Open()
        {
            EnsureBuilt();
            if (_wheelRoot == null) return;
            _wheelRoot.SetActive(true);
            _isOpen      = true;
            _highlighted = (int)Weathers.Clear;
            SyncActiveIndicator();
            ApplyHighlightColours();
            RefreshLockState();
            SetHolokenSwitchEnabled(false);
        }

        void Close()
        {
            _wheelRoot?.SetActive(false);
            _isOpen = false;
            SetHolokenSwitchEnabled(true);
        }

        // Disable / re-enable the Q/E holoken-mode-switch actions while wheel is open.
        static void SetHolokenSwitchEnabled(bool enabled)
        {
            var ctrl = UnityEngine.Object.FindObjectOfType<TreyGameplayController>();
            if (ctrl == null) return;
            if (enabled)
            {
                ctrl.MoveHUDLeft.Enable();
                ctrl.MoveHUDRight.Enable();
            }
            else
            {
                ctrl.MoveHUDLeft.Disable();
                ctrl.MoveHUDRight.Disable();
            }
        }

        // ── UI construction ───────────────────────────────────────────────────

        void EnsureBuilt()
        {
            if (_wheelRoot != null) return;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _pill = MakeRoundedRectSprite(64, 20, 10);

            // Canvas
            _canvasGO = new GameObject("WeatherWheelCanvas");
            UnityEngine.Object.DontDestroyOnLoad(_canvasGO);
            var canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;
            var scaler = _canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            _canvasGO.AddComponent<GraphicRaycaster>();

            // Root — transparent, full screen
            _wheelRoot = new GameObject("WheelRoot");
            _wheelRoot.transform.SetParent(_canvasGO.transform, false);
            var rootRT = _wheelRoot.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;
            _wheelRoot.AddComponent<Image>().color = Color.clear;

            // Wheel pivot
            var wheel = new GameObject("Wheel");
            wheel.transform.SetParent(_wheelRoot.transform, false);
            var wheelRT = wheel.AddComponent<RectTransform>();
            wheelRT.anchoredPosition = Vector2.zero;
            wheelRT.sizeDelta        = new Vector2(460, 460);

            // 10 weather slices
            var sprites = MiniMap.Instance?.WeatherSprites;
            for (int i = 0; i < SliceCount; i++)
            {
                var w     = (Weathers)i;
                float ang = SliceDeg * i + StartDeg;
                float rad = ang * Mathf.Deg2Rad;
                var pos   = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * LayoutRadius;
                Sprite? sp = sprites != null && sprites.Length > i ? sprites[i] : null;

                BuildSlice(wheel.transform, w, pos, sp);
                _sliceTypes.Add(w);
            }

            // Story-weather warning
            var statusGO = new GameObject("Status");
            statusGO.transform.SetParent(_wheelRoot.transform, false);
            var sRT = statusGO.AddComponent<RectTransform>();
            sRT.anchoredPosition = new Vector2(0, -228f);
            sRT.sizeDelta        = new Vector2(600, 24);
            _statusText = statusGO.AddComponent<Text>();
            _statusText.font      = _font;
            _statusText.alignment = TextAnchor.MiddleCenter;
            _statusText.color     = new Color(1f, 0.68f, 0.24f, 0.95f);
            _statusText.fontSize  = 13;
            _statusText.fontStyle = FontStyle.Italic;

            // Bottom keybind bar
            var hintBar = new GameObject("HintBar");
            hintBar.transform.SetParent(_wheelRoot.transform, false);
            var hintBarSprite = MakeRoundedRectSprite(256, 40, 12);
            var hRT = hintBar.AddComponent<RectTransform>();
            hRT.anchorMin        = new Vector2(0.5f, 0f);
            hRT.anchorMax        = new Vector2(0.5f, 0f);
            hRT.pivot            = new Vector2(0.5f, 0f);
            hRT.anchoredPosition = new Vector2(0f, 14f);
            hRT.sizeDelta        = new Vector2(560f, 34f);
            var hBg = hintBar.AddComponent<Image>();
            hBg.sprite = hintBarSprite;
            hBg.type   = Image.Type.Sliced;
            hBg.color  = ColHintBar;
            MakeLabel(hintBar.transform, "Hint",
                "LMB / A  —  Select     RMB / B / ESC  —  Close     C / RS  —  Open",
                Vector2.zero, Vector2.zero,
                new Color(0.70f, 0.68f, 0.90f, 0.85f), 12, FontStyle.Normal,
                anchorFill: true);
        }

        void BuildSlice(Transform parent, Weathers weather, Vector2 pos, Sprite? sprite)
        {
            var go = new GameObject($"Slice_{weather}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta        = new Vector2(72, 72);

            // Icon — no background
            if (sprite != null)
            {
                var iconGO  = new GameObject("Icon");
                iconGO.transform.SetParent(go.transform, false);
                var iconRT  = iconGO.AddComponent<RectTransform>();
                iconRT.anchoredPosition = Vector2.zero;
                iconRT.sizeDelta        = new Vector2(56, 56);
                var iconImg = iconGO.AddComponent<Image>();
                iconImg.sprite         = sprite;
                iconImg.preserveAspect = true;
            }

            // Rounded name pill
            var pillGO = new GameObject("Pill");
            pillGO.transform.SetParent(go.transform, false);
            var pillRT = pillGO.AddComponent<RectTransform>();
            pillRT.anchoredPosition = new Vector2(0, -50f);
            pillRT.sizeDelta        = new Vector2(92, 22);
            var pillBg = pillGO.AddComponent<Image>();
            pillBg.sprite = _pill;
            pillBg.type   = Image.Type.Sliced;
            pillBg.color  = ColPillDefault;
            _pillBgs.Add(pillBg);

            var pillText = MakeLabel(pillGO.transform, "Text",
                WeatherName(weather),
                Vector2.zero, Vector2.zero,
                ColLabelDefault, 11, FontStyle.Normal,
                anchorFill: true);
            _pillTexts.Add(pillText);
        }

        // ── Procedural rounded-rect sprite ────────────────────────────────────

        // Creates a white rounded-rect texture with a 9-slice border so it can
        // scale to any size while keeping crisp corners.
        static Sprite MakeRoundedRectSprite(int w, int h, int radius)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[w * h];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = InsideRoundedRect(x, y, w, h, radius)
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(0, 0, 0, 0);

            tex.SetPixels32(px);
            tex.Apply();

            float r = radius;
            return Sprite.Create(tex,
                new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(r, r, r, r));   // left, bottom, right, top border
        }

        static bool InsideRoundedRect(int x, int y, int w, int h, int r)
        {
            // Fast-path: interior strip (no corner check needed)
            if (x >= r && x < w - r) return true;
            if (y >= r && y < h - r) return true;

            // Corner distances
            int dx = x < r ? r - x : x >= w - r ? x - (w - r - 1) : 0;
            int dy = y < r ? r - y : y >= h - r ? y - (h - r - 1) : 0;
            return dx * dx + dy * dy <= r * r;
        }

        // ── Visual state ──────────────────────────────────────────────────────

        void ApplyHighlightColours()
        {
            bool locked = IsStoryWeather(CurrentMapGuid());

            for (int i = 0; i < _pillBgs.Count; i++)
            {
                Color bg; Color txt;
                if (locked)
                {
                    bg = ColPillLocked;  txt = ColLabelLocked;
                }
                else if (i == _highlighted)
                {
                    bg = ColPillHover;   txt = ColLabelHover;
                }
                else if (i == _activeSlice)
                {
                    bg = ColPillActive;  txt = ColLabelHover;
                }
                else
                {
                    bg = ColPillDefault; txt = ColLabelDefault;
                }

                _pillBgs[i].color  = bg;
                _pillTexts[i].color = txt;
            }
        }

        void SyncActiveIndicator()
        {
            var current = WeatherController.Instance?.CurrentWeather ?? Weathers.Clear;
            _activeSlice = (int)current;
            ApplyHighlightColours();
        }

        void RefreshLockState()
        {
            bool locked = IsStoryWeather(CurrentMapGuid());
            if (_statusText != null)
                _statusText.text = locked
                    ? "Story weather is active — cannot be changed here."
                    : string.Empty;
            ApplyHighlightColours();
        }

        // ── Actions ───────────────────────────────────────────────────────────

        void ApplyWeather(Weathers weather)
        {
            var mapGuid = CurrentMapGuid();
            if (mapGuid == null) return;

            WeatherController.ChangeWeather(weather);
            Player.PlayerState?.MapsWeather?.AddWeather(mapGuid, weather, PinDuration);
            _modPinnedMaps.Add(mapGuid);
            _activeSlice = (int)weather;

            Plugin.Log.LogInfo($"WeatherWheel: pinned {weather} on map {mapGuid}.");
            Close();
        }

        void ApplyAndUnpin()
        {
            var mapGuid = CurrentMapGuid();
            if (mapGuid == null) return;

            WeatherController.ChangeWeather(Weathers.Clear);
            Player.PlayerState?.MapsWeather?.RemoveWeather(mapGuid);
            _modPinnedMaps.Remove(mapGuid);
            _activeSlice = (int)Weathers.Clear;

            Plugin.Log.LogInfo($"WeatherWheel: set Clear and unpinned on map {mapGuid}.");
            Close();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static int AngleToSlice(float angleDeg)
        {
            float shifted = (angleDeg - StartDeg + 360f + SliceDeg * 0.5f) % 360f;
            return Mathf.FloorToInt(shifted / SliceDeg) % SliceCount;
        }

        static Vector2 WheelScreenCentre()
            => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        bool IsStoryWeather(string? mapGuid)
        {
            if (mapGuid == null) return false;
            if (_modPinnedMaps.Contains(mapGuid)) return false;
            var mw = Player.PlayerState?.MapsWeather;
            if (mw == null || !mw.HasWeather(mapGuid)) return false;
            var info = mw.GetWeather(mapGuid);
            return info != null && info.Duration == -1;
        }

        public bool IsOpen => _isOpen;

        string? CurrentMapGuid()
        {
            try { return PlayerController.Instance?.CurrentMap?.MapGUID; }
            catch { return null; }
        }

        Text MakeLabel(Transform parent, string name, string text,
            Vector2 pos, Vector2 size, Color color, int fontSize, FontStyle style,
            bool anchorFill = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            if (anchorFill)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchoredPosition = pos;
                rt.sizeDelta        = size;
            }
            var t = go.AddComponent<Text>();
            t.font      = _font;
            t.text      = text;
            t.alignment = TextAnchor.MiddleCenter;
            t.color     = color;
            t.fontSize  = fontSize;
            t.fontStyle = style;
            return t;
        }

        static string WeatherName(Weathers w) => w switch
        {
            Weathers.Clear      => "Clear",
            Weathers.HarshSun   => "Harsh Sun",
            Weathers.Rain       => "Rain",
            Weathers.HeavyRain  => "Heavy Rain",
            Weathers.SandStorm  => "Sandstorm",
            Weathers.Hail       => "Hail",
            Weathers.Rainbow    => "Rainbow",
            Weathers.Fog        => "Fog",
            Weathers.Snow       => "Snow",
            Weathers.AshesRain  => "Ashes Rain",
            _                   => w.ToString()
        };
    }
}
