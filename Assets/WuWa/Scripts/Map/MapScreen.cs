using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WuWa
{
    /// Full-screen world map (router screen "Map"): pan/zoom viewport over the
    /// 4096 bake, fog overlay, categorised markers with LOD + filters, virtual
    /// cursor for the pad, detail card with warp/track/pin, pins, region stats.
    public class MapScreen : UIScreen
    {
        public override string Id { get { return "Map"; } }
        public override string Title { get { return "지도"; } }
        public override bool IsHubTab { get { return true; } }
        public override bool UsesMapContext { get { return true; } }
        public override Transform FocusRoot { get { return null; } }     // virtual cursor instead of focus nav

        const float BasePx = 1000f;            // px across the world at zoom 1
        const float ZoomMin = 0.5f, ZoomMax = 4f;

        RectTransform _viewport, _content, _overlay;
        RawImage _mapImg, _fogImg;
        ScreenRouter.HubHeader _header;
        Text _status, _bottom, _hoverText, _regionTitle, _regionBody, _detailName, _detailBody, _detailHint, _panelTitle;
        Image _hoverBg, _cursorImg;
        RectTransform _playerRt, _coneRt, _questRt;
        Image _questImg;
        UILine _route;
        readonly List<Vector2> _routePts = new List<Vector2>();
        GameObject _detailRoot, _listRoot;
        Button _btnWarp, _btnTrack, _btnPin;
        readonly List<Text> _regionLabels = new List<Text>();
        readonly List<Vector2> _regionCenters = new List<Vector2>();

        class Node { public RectTransform rt; public Image img; public Text label; public MapMarker m; }
        readonly List<Node> _pool = new List<Node>();
        int _used;
        class Row { public GameObject go; public Image icon; public Text label; public Text state; public Image bg; }
        readonly List<Row> _rows = new List<Row>();

        // view state
        float _zoom = 1.6f, _zoomT = 1.6f;
        Vector2 _pan, _panT, _zoomAnchor;
        bool _panAnim; float _panAnimT;
        Vector2 _cursor; bool _padCursor;
        Vector2 _dragVel; bool _dragging; float _dragTime;
        MapMarker _hover, _selected;
        int _cycleIdx = -1;
        bool _filterOpen; int _filterRow; float _navRepeat; Vector2 _navLast;
        float _pinDown = -1f; MapPins.Pin _pinTarget; bool _pinConsumed;
        bool _hasView;
        int _lastRegion = -1;
        /// Set before Push("Map") to open centred on a world point (quest log / codex).
        public static Vector3? PendingFocus;

        static readonly string[] RegionNames = { "녹야 평원", "속삭임 숲", "노을빛 언덕", "거울 호수", "잿빛 황무지", "서리 고원", "노래잃은 도시", "메아리 마을" };
        static readonly Vector2[] RegionCenters = { new Vector2(0, -40), new Vector2(-60, 210), new Vector2(340, 330), new Vector2(390, -100), new Vector2(-360, -80), new Vector2(-190, 500), new Vector2(90, -360), new Vector2(-215, -165) };

        Vector2 ViewSize { get { return _viewport.rect.size; } }
        int Tier { get { return _zoom < 1f ? 0 : _zoom < 2.2f ? 1 : 2; } }

        // ================================================================ build
        protected override void Build()
        {
            _header = ScreenRouter.BuildHubHeader(Root, "지도", Id);
            _status = UIKit.Txt("status", Root, new Vector2(0f, 1f), new Vector2(24f, -96f), new Vector2(1000f, 26f), "", 16, UIKit.Theme.TextHi, TextAnchor.MiddleLeft);
            _status.rectTransform.pivot = new Vector2(0f, 1f);
            var hints = UIKit.Txt("hints", Root, new Vector2(0f, 1f), new Vector2(1500f, -96f), new Vector2(700f, 26f), "", 14, UIKit.Theme.TextLo, TextAnchor.MiddleRight);
            hints.rectTransform.pivot = new Vector2(1f, 1f);
            hints.name = "mapHints";

            // viewport
            var vpImg = UIKit.Img("viewport", Root, new Color(0.06f, 0.07f, 0.075f, 1f), null, true);
            _viewport = vpImg.rectTransform;
            _viewport.anchorMin = _viewport.anchorMax = new Vector2(0f, 1f);
            _viewport.pivot = new Vector2(0.5f, 0.5f);
            _viewport.anchoredPosition = new Vector2(20f + 740f, -124f - 458f);
            _viewport.sizeDelta = new Vector2(1480f, 916f);
            vpImg.gameObject.AddComponent<RectMask2D>();
            var vi = vpImg.gameObject.AddComponent<ViewportInput>();
            vi.screen = this;

            _content = UIKit.Rect("content", _viewport, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BasePx, BasePx));
            var mapGo = new GameObject("map");
            mapGo.transform.SetParent(_content, false);
            _mapImg = mapGo.AddComponent<RawImage>();
            _mapImg.texture = MapSystem.I != null ? MapSystem.I.worldMap : null;
            _mapImg.raycastTarget = false;
            UIKit.Stretch(_mapImg.rectTransform);
            var fogGo = new GameObject("fog");
            fogGo.transform.SetParent(_content, false);
            _fogImg = fogGo.AddComponent<RawImage>();
            _fogImg.texture = MapDiscovery.Texture;
            _fogImg.color = new Color(0.03f, 0.04f, 0.06f, 0.86f);
            _fogImg.raycastTarget = false;
            UIKit.Stretch(_fogImg.rectTransform);

            _overlay = UIKit.Rect("overlay", _viewport, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var routeGo = new GameObject("route");
            routeGo.transform.SetParent(_overlay, false);
            routeGo.AddComponent<RectTransform>();
            routeGo.AddComponent<CanvasRenderer>();
            _route = routeGo.AddComponent<UILine>();
            _route.color = new Color(1f, 0.92f, 0.5f, 0.75f);
            _route.raycastTarget = false;

            for (int i = 0; i < RegionNames.Length; i++)
            {
                var t = UIKit.Txt("rg" + i, _overlay, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 30f), RegionNames[i], 18, new Color(1f, 0.96f, 0.85f, 0.9f), TextAnchor.MiddleCenter, true, true);
                _regionLabels.Add(t);
                _regionCenters.Add(RegionCenters[i]);
            }

            _questImg = UIKit.Img("questRing", _overlay, MapMarkers.QuestC, MapIcons.Get("ring"));
            _questRt = _questImg.rectTransform;
            _questRt.sizeDelta = new Vector2(44f, 44f);
            _questImg.gameObject.SetActive(false);

            var cone = UIKit.Img("cone", _overlay, new Color(0.6f, 0.95f, 1f, 0.4f), MapIcons.Get("cone"));
            cone.rectTransform.sizeDelta = new Vector2(90f, 90f);
            _coneRt = cone.rectTransform;
            var player = UIKit.Img("player", _overlay, MapMarkers.PlayerC, MapIcons.Get("player"));
            player.rectTransform.sizeDelta = new Vector2(28f, 28f);
            player.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            _playerRt = player.rectTransform;

            _hoverBg = UIKit.Img("hoverBg", _overlay, new Color(0.04f, 0.05f, 0.08f, 0.92f), UIKit.Rounded);
            _hoverBg.rectTransform.pivot = new Vector2(0f, 0f);
            _hoverBg.rectTransform.sizeDelta = new Vector2(240f, 30f);
            _hoverText = UIKit.Txt("t", _hoverBg.transform, new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(230f, 28f), "", 14, UIKit.Theme.TextHi, TextAnchor.MiddleLeft);
            _hoverText.rectTransform.pivot = new Vector2(0f, 0.5f);
            _hoverBg.gameObject.SetActive(false);

            _cursorImg = UIKit.Img("cursor", _overlay, new Color(1f, 0.95f, 0.7f, 0.95f), MapIcons.Get("cursor"));
            _cursorImg.rectTransform.sizeDelta = new Vector2(36f, 36f);
            _cursorImg.gameObject.SetActive(false);

            BuildPanel();

            _bottom = UIKit.Txt("bottom", Root, new Vector2(0f, 0f), new Vector2(24f, 10f), new Vector2(1480f, 26f), "", 15, UIKit.Theme.TextHi, TextAnchor.MiddleLeft);
            _bottom.rectTransform.pivot = new Vector2(0f, 0f);
        }

        void BuildPanel()
        {
            var panel = UIKit.Panel("panel", Root, new Color(1f, 1f, 1f, 0.05f), new Vector2(0f, 1f), new Vector2(1520f, -124f), new Vector2(380f, 916f));
            var pr = panel.transform;
            _regionTitle = UIKit.Txt("regionTitle", pr, new Vector2(0f, 1f), new Vector2(20f, -16f), new Vector2(340f, 30f), "", 20, UIKit.Theme.Accent, TextAnchor.MiddleLeft, true);
            _regionTitle.rectTransform.pivot = new Vector2(0f, 1f);
            _regionBody = UIKit.Txt("regionBody", pr, new Vector2(0f, 1f), new Vector2(20f, -52f), new Vector2(340f, 80f), "", 15, UIKit.Theme.TextHi, TextAnchor.UpperLeft);
            _regionBody.rectTransform.pivot = new Vector2(0f, 1f);

            var sep = UIKit.Img("sep", pr, new Color(1f, 1f, 1f, 0.12f));
            var srt = sep.rectTransform; srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(1f, 1f); srt.pivot = new Vector2(0.5f, 1f); srt.anchoredPosition = new Vector2(0f, -140f); srt.sizeDelta = new Vector2(-32f, 1f);

            _detailRoot = new GameObject("detail");
            _detailRoot.transform.SetParent(pr, false);
            var drt = _detailRoot.AddComponent<RectTransform>();
            drt.anchorMin = new Vector2(0f, 1f); drt.anchorMax = new Vector2(1f, 1f); drt.pivot = new Vector2(0.5f, 1f);
            drt.anchoredPosition = new Vector2(0f, -150f); drt.sizeDelta = new Vector2(0f, 190f);
            _detailName = UIKit.Txt("name", drt, new Vector2(0f, 1f), new Vector2(20f, -6f), new Vector2(340f, 30f), "", 19, UIKit.Theme.TextHi, TextAnchor.MiddleLeft, true);
            _detailName.rectTransform.pivot = new Vector2(0f, 1f);
            _detailBody = UIKit.Txt("body", drt, new Vector2(0f, 1f), new Vector2(20f, -40f), new Vector2(340f, 60f), "", 14, UIKit.Theme.TextLo, TextAnchor.UpperLeft);
            _detailBody.rectTransform.pivot = new Vector2(0f, 1f);
            _btnWarp = UIKit.Btn("warp", drt, new Vector2(0f, 1f), new Vector2(20f, -110f), new Vector2(104f, 40f), "워프", UIKit.Theme.Confirm, () => { if (_selected != null) WarpConfirm(_selected); }, 15);
            _btnWarp.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            _btnTrack = UIKit.Btn("track", drt, new Vector2(0f, 1f), new Vector2(138f, -110f), new Vector2(104f, 40f), "추적", UIKit.Theme.Button, () => { if (_selected != null) ToggleTrack(_selected); }, 15);
            _btnTrack.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            _btnPin = UIKit.Btn("pin", drt, new Vector2(0f, 1f), new Vector2(256f, -110f), new Vector2(104f, 40f), "핀", UIKit.Theme.Button, () => { if (_selected == null) return; if (_selected.cat == MapCategory.Pin) { MapPins.Remove(_selected.source as MapPins.Pin); HUDController.Toast("핀 삭제"); _selected = null; RefreshDetail(); } else { MapPins.Add(_selected.pos); RefreshDetail(); } }, 15);
            _btnPin.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            _detailHint = UIKit.Txt("hint", drt, new Vector2(0f, 1f), new Vector2(20f, -158f), new Vector2(340f, 24f), "", 12, UIKit.Theme.TextLo, TextAnchor.MiddleLeft);
            _detailHint.rectTransform.pivot = new Vector2(0f, 1f);
            _detailRoot.SetActive(false);

            var sep2 = UIKit.Img("sep2", pr, new Color(1f, 1f, 1f, 0.12f));
            var s2 = sep2.rectTransform; s2.anchorMin = new Vector2(0f, 1f); s2.anchorMax = new Vector2(1f, 1f); s2.pivot = new Vector2(0.5f, 1f); s2.anchoredPosition = new Vector2(0f, -350f); s2.sizeDelta = new Vector2(-32f, 1f);

            _panelTitle = UIKit.Txt("listTitle", pr, new Vector2(0f, 1f), new Vector2(20f, -362f), new Vector2(340f, 26f), "", 15, UIKit.Theme.Accent, TextAnchor.MiddleLeft, true);
            _panelTitle.rectTransform.pivot = new Vector2(0f, 1f);
            _listRoot = new GameObject("list");
            _listRoot.transform.SetParent(pr, false);
            var lrt = _listRoot.AddComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 1f); lrt.anchorMax = new Vector2(1f, 1f); lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = new Vector2(0f, -394f); lrt.sizeDelta = new Vector2(0f, 500f);
            int rows = MapMarkers.Filterable.Length + 2;
            for (int i = 0; i < rows; i++)
            {
                int idx = i;
                var r = new Row();
                r.go = new GameObject("row" + i);
                r.go.transform.SetParent(lrt, false);
                var rrt = r.go.AddComponent<RectTransform>();
                rrt.anchorMin = new Vector2(0f, 1f); rrt.anchorMax = new Vector2(1f, 1f); rrt.pivot = new Vector2(0.5f, 1f);
                rrt.anchoredPosition = new Vector2(0f, -i * 40f); rrt.sizeDelta = new Vector2(-24f, 36f);
                r.bg = r.go.AddComponent<Image>(); r.bg.sprite = UIKit.Rounded; r.bg.type = Image.Type.Sliced; r.bg.color = new Color(1f, 1f, 1f, 0.03f);
                var b = r.go.AddComponent<Button>(); b.targetGraphic = r.bg;
                var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
                b.onClick.AddListener(() => RowClick(idx));
                if (i < MapMarkers.Filterable.Length)
                {
                    r.icon = UIKit.Img("icon", r.go.transform, Color.white, MapIcons.Get(MapMarkers.FilterIcons[i]));
                    var irt = r.icon.rectTransform; irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f); irt.pivot = new Vector2(0f, 0.5f); irt.anchoredPosition = new Vector2(10f, 0f); irt.sizeDelta = new Vector2(20f, 20f);
                }
                r.label = UIKit.Txt("label", r.go.transform, new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(220f, 30f), "", 14, UIKit.Theme.TextHi, TextAnchor.MiddleLeft);
                r.label.rectTransform.pivot = new Vector2(0f, 0.5f);
                r.state = UIKit.Txt("state", r.go.transform, new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(90f, 30f), "", 14, UIKit.Theme.TextLo, TextAnchor.MiddleRight);
                r.state.rectTransform.pivot = new Vector2(1f, 0.5f);
                _rows.Add(r);
            }
        }

        // ================================================================ open / close
        public override void OnOpen(object args)
        {
            ScreenRouter.RefreshHubHeader(_header);
            if (_mapImg.texture == null && MapSystem.I != null) _mapImg.texture = MapSystem.I.worldMap;
            _fogImg.texture = MapDiscovery.Texture;
            MapMarkers.InvalidateCaches();
            MapMarkers.Collect(true);
            _selected = null; _hover = null; _filterOpen = false; _dragVel = Vector2.zero; _panAnim = false;
            if (!_hasView) { CenterOnPlayer(1.6f, false); _hasView = true; }
            if (PendingFocus.HasValue)
            {
                var f = PendingFocus.Value; PendingFocus = null;
                _zoom = _zoomT = 2.4f;
                _pan = ClampPan(-new Vector2(f.x, f.z) / MapSystem.WorldSize * BasePx * _zoom);
                _panAnim = false; _dragVel = Vector2.zero; _hasView = true;
            }
            _cursor = Vector2.zero;
            _padCursor = InputService.GamepadActive;
            RefreshPanel();
            RefreshDetail();
            Layout();
        }

        public override void OnClose() { _dragging = false; _pinDown = -1f; }

        public override bool OnBack()
        {
            if (_filterOpen) { _filterOpen = false; RefreshPanel(); return true; }
            if (_selected != null) { _selected = null; RefreshDetail(); return true; }
            return false;
        }

        public override void OnTab(int dir) { CycleWarp(dir); }

        // ================================================================ coordinates
        Vector2 View(Vector3 world)
        {
            return _pan + new Vector2(world.x, world.z) / MapSystem.WorldSize * BasePx * _zoom;
        }

        Vector3 WorldAt(Vector2 view)
        {
            Vector2 w = (view - _pan) / (BasePx * _zoom) * MapSystem.WorldSize;
            return new Vector3(Mathf.Clamp(w.x, -WorldRegions.WorldHalf, WorldRegions.WorldHalf), 0f, Mathf.Clamp(w.y, -WorldRegions.WorldHalf, WorldRegions.WorldHalf));
        }

        float PxToMetres(float px) { return px / (BasePx * _zoom) * MapSystem.WorldSize; }

        void CenterOnPlayer(float zoom, bool animate)
        {
            var pc = PlayerController.Instance;
            Vector3 p = pc != null ? pc.transform.position : Vector3.zero;
            _zoomT = zoom;
            Vector2 target = -new Vector2(p.x, p.z) / MapSystem.WorldSize * BasePx * zoom;
            if (animate) { _panT = target; _panAnim = true; _panAnimT = 0f; }
            else { _zoom = zoom; _pan = target; }
            _zoomAnchor = Vector2.zero;
            _dragVel = Vector2.zero;
        }

        Vector2 ClampPan(Vector2 pan)
        {
            float s = BasePx * _zoom;
            Vector2 v = ViewSize;
            float lx = Mathf.Max(0f, s * 0.5f - v.x * 0.5f), ly = Mathf.Max(0f, s * 0.5f - v.y * 0.5f);
            return new Vector2(Mathf.Clamp(pan.x, -lx, lx), Mathf.Clamp(pan.y, -ly, ly));
        }

        // ================================================================ per frame
        public override void OnTick()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool pad = InputService.GamepadActive;
            _padCursor = pad;

            // ---- cursor
            if (!pad)
            {
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    Vector2 local;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, mouse.position.ReadValue(), null, out local))
                    {
                        var half = ViewSize * 0.5f;
                        if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y) _cursor = local;
                    }
                }
            }
            else if (!_filterOpen)
            {
                Vector2 c = InputService.MapCursor;
                if (c.sqrMagnitude > 0.01f)
                {
                    _cursor += c * 1100f * dt;
                    var half = ViewSize * 0.5f;
                    _cursor = new Vector2(Mathf.Clamp(_cursor.x, -half.x, half.x), Mathf.Clamp(_cursor.y, -half.y, half.y));
                    // edge auto-pan
                    Vector2 push = Vector2.zero;
                    if (_cursor.x > half.x - 60f) push.x = -1f; else if (_cursor.x < -half.x + 60f) push.x = 1f;
                    if (_cursor.y > half.y - 60f) push.y = -1f; else if (_cursor.y < -half.y + 60f) push.y = 1f;
                    if (push != Vector2.zero) { _pan += push * 600f * dt; _panAnim = false; }
                }
            }

            // ---- zoom
            float z = InputService.MapZoom;
            if (Mathf.Abs(z) > 0.01f)
            {
                if (Mathf.Abs(z) > 5f)
                {
                    // wheel: at least one notch per event (high-resolution wheels report small deltas), ×1.45 per notch
                    float notches = Mathf.Sign(z) * Mathf.Max(1f, Mathf.Abs(z) / 120f);
                    _zoomT *= Mathf.Pow(1.45f, notches);
                    _zoomAnchor = _cursor;
                }
                else { _zoomT *= Mathf.Pow(3f, z * dt); _zoomAnchor = pad ? Vector2.zero : _cursor; }      // analog triggers / −= keys: 3x per second
                _zoomT = Mathf.Clamp(_zoomT, ZoomMin, ZoomMax);
                _panAnim = false;
            }
            float nz = Mathf.Lerp(_zoom, _zoomT, 1f - Mathf.Exp(-18f * dt));
            if (Mathf.Abs(nz - _zoom) > 0.0001f)
            {
                _pan = _zoomAnchor - (_zoomAnchor - _pan) * (nz / _zoom);
                _zoom = nz;
            }

            // ---- pan
            Vector2 p = InputService.MapPan;
            if (p.sqrMagnitude > 0.01f && !(pad && _filterOpen))
            {
                float speed = pad ? 900f * p.magnitude : 900f;
                _pan -= p.normalized * speed * dt;
                _panAnim = false; _dragVel = Vector2.zero;
            }
            if (_panAnim)
            {
                _panAnimT += dt / 0.35f;
                _pan = Vector2.Lerp(_pan, _panT, 1f - Mathf.Exp(-14f * dt));
                if (_panAnimT >= 1f || (_pan - _panT).sqrMagnitude < 1f) { _pan = _panT; _panAnim = false; }
            }
            if (!_dragging && _dragVel.sqrMagnitude > 4f)
            {
                _pan += _dragVel * dt;
                _dragVel *= Mathf.Exp(-6f * dt);
            }
            var clamped = ClampPan(_pan);
            if ((clamped - _pan).sqrMagnitude > 0.01f) _pan = Vector2.Lerp(_pan, clamped, _dragging ? 0.15f : 1f - Mathf.Exp(-10f * dt));

            // ---- buttons
            if (InputService.MapCenterPressed) CenterOnPlayer(1.6f, true);
            if (InputService.MapFilterPressed) { _filterOpen = !_filterOpen; _filterRow = 0; RefreshPanel(); }
            if (_filterOpen && pad) FilterNav(dt);
            else if (InputService.MapWarpPressed) Select();
            HandlePin();

            MapMarkers.Collect();
            Layout();
            UpdateTexts();
        }

        void FilterNav(float dt)
        {
            Vector2 nav = InputService.UINavigate;
            int rows = _rows.Count;
            if (Mathf.Abs(nav.y) > 0.5f)
            {
                bool fresh = Mathf.Sign(nav.y) != Mathf.Sign(_navLast.y) || _navLast.sqrMagnitude < 0.25f;
                if (fresh || Time.unscaledTime >= _navRepeat)
                {
                    _filterRow = (_filterRow + (nav.y > 0f ? -1 : 1) + rows) % rows;
                    _navRepeat = Time.unscaledTime + (fresh ? 0.4f : 0.12f);
                    RefreshPanel();
                }
            }
            _navLast = nav;
            if (InputService.MapWarpPressed) RowClick(_filterRow);
        }

        void RowClick(int idx)
        {
            if (!_filterOpen) { _filterOpen = true; RefreshPanel(); return; }
            int n = MapMarkers.Filterable.Length;
            if (idx < n) MapMarkers.SetEnabled(MapMarkers.Filterable[idx], !MapMarkers.Enabled(MapMarkers.Filterable[idx]));
            else MapMarkers.SetAll(idx == n);
            RefreshPanel();
        }

        // ================================================================ layout
        void Layout()
        {
            _content.localScale = new Vector3(_zoom, _zoom, 1f);
            _content.anchoredPosition = _pan;
            var half = ViewSize * 0.5f + new Vector2(60f, 60f);
            int tier = Tier;

            for (int i = 0; i < _regionLabels.Count; i++)
            {
                var v = View(new Vector3(_regionCenters[i].x, 0f, _regionCenters[i].y));
                var t = _regionLabels[i];
                bool show = Mathf.Abs(v.x) < half.x && Mathf.Abs(v.y) < half.y;
                if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
                if (!show) continue;
                t.rectTransform.anchoredPosition = v + new Vector2(0f, tier == 0 ? 16f : 30f);   // clear of the region's tower marker
                t.fontSize = tier == 0 ? 15 : tier == 1 ? 18 : 22;
                bool disc = MapDiscovery.RegionDiscovered(i);
                t.color = new Color(1f, 0.96f, 0.85f, disc ? 0.92f : 0.45f);
            }

            _used = 0;
            var pc = PlayerController.Instance;
            Vector3 pp = pc != null ? pc.transform.position : Vector3.zero;
            MapMarker best = null; float bestD = _padCursor ? 20f : 14f;
            foreach (var m in MapMarkers.List)
            {
                if (m.cat == MapCategory.Player || !MapMarkers.Enabled(m.cat) || m.lod > tier) continue;
                var v = View(m.pos);
                if (Mathf.Abs(v.x) > half.x || Mathf.Abs(v.y) > half.y) continue;
                var n = GetNode(_used++);
                n.m = m;
                n.rt.anchoredPosition = v;
                n.img.sprite = MapIcons.Get(m.icon);
                bool hl = m == _hover || m == _selected || (_selected != null && SameMarker(m, _selected));
                float size = m.size * (hl ? 1.3f : 1f);
                n.rt.sizeDelta = new Vector2(size, size);
                n.img.color = m.color;
                bool label = tier >= 1 && (m.cat == MapCategory.Tower || m.cat == MapCategory.Waystone || m.cat == MapCategory.Boss || m.cat == MapCategory.Arena || m.cat == MapCategory.Rift || (m.cat == MapCategory.Village && m.icon == "house"));
                if (n.label.gameObject.activeSelf != label) n.label.gameObject.SetActive(label);
                if (label) { n.label.text = m.name; n.label.rectTransform.anchoredPosition = new Vector2(0f, -size * 0.5f - 2f); }
                float d = Vector2.Distance(v, _cursor);
                if (d < bestD) { bestD = d; best = m; }
            }
            for (int i = _used; i < _pool.Count; i++) if (_pool[i].rt.gameObject.activeSelf) _pool[i].rt.gameObject.SetActive(false);
            _hover = best;

            // player + cone
            var pv = View(pp);
            _playerRt.anchoredPosition = pv;
            _playerRt.localRotation = Quaternion.Euler(0f, 0f, pc != null ? -pc.transform.eulerAngles.y : 0f);
            _coneRt.anchoredPosition = pv;
            _coneRt.localRotation = Quaternion.Euler(0f, 0f, Camera.main != null ? -Camera.main.transform.eulerAngles.y : 0f);

            // objective ring + route
            Vector3 op; string on;
            bool hasObj = MapSystem.Objective(out op, out on);
            if (_questImg.gameObject.activeSelf != hasObj) _questImg.gameObject.SetActive(hasObj);
            if (hasObj)
            {
                var ov = View(op);
                float s = 40f + Mathf.Sin(Time.unscaledTime * 4f) * 6f;
                _questRt.anchoredPosition = ov;
                _questRt.sizeDelta = new Vector2(s, s);
                _questImg.color = MapSystem.HasTracked ? new Color(0.6f, 0.9f, 1f) : MapMarkers.QuestC;
                var wp = RoadRouter.Route(pp, op);
                _routePts.Clear();
                for (int i = 0; i < wp.Count; i++) _routePts.Add(View(new Vector3(wp[i].x, 0f, wp[i].y)));
                if (_routePts.Count >= 2) { _routePts[0] = pv; _routePts[_routePts.Count - 1] = ov; }
                _route.SetPolyline(_routePts);
            }
            else _route.Clear();

            // cursor + hover label
            if (_cursorImg.gameObject.activeSelf != _padCursor) _cursorImg.gameObject.SetActive(_padCursor);
            _cursorImg.rectTransform.anchoredPosition = _cursor;
            bool showHover = _hover != null;
            if (_hoverBg.gameObject.activeSelf != showHover) _hoverBg.gameObject.SetActive(showHover);
            if (showHover)
            {
                float dist = WuWaUtil.Flat(_hover.pos - pp).magnitude;
                _hoverText.text = _hover.name + "  ·  " + Mathf.RoundToInt(dist) + " m" + (string.IsNullOrEmpty(_hover.status) ? "" : "  ·  " + _hover.status);
                float w = Mathf.Max(120f, _hoverText.preferredWidth + 20f);
                _hoverBg.rectTransform.sizeDelta = new Vector2(w, 30f);
                var pos = View(_hover.pos) + new Vector2(14f, 12f);
                var hv = ViewSize * 0.5f;
                if (pos.x + w > hv.x) pos.x = View(_hover.pos).x - w - 14f;
                if (pos.y + 30f > hv.y) pos.y = View(_hover.pos).y - 42f;
                _hoverBg.rectTransform.anchoredPosition = pos;
            }
        }

        static bool SameMarker(MapMarker a, MapMarker b)
        {
            return a.cat == b.cat && (a.pos - b.pos).sqrMagnitude < 0.01f;
        }

        Node GetNode(int idx)
        {
            while (_pool.Count <= idx)
            {
                var img = UIKit.Img("mk", _overlay, Color.white, MapIcons.Get("dot"));
                img.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.55f);
                var n = new Node { rt = img.rectTransform, img = img };
                n.label = UIKit.Txt("label", img.transform, new Vector2(0.5f, 0f), new Vector2(0f, -8f), new Vector2(180f, 20f), "", 13, new Color(1f, 1f, 1f, 0.92f), TextAnchor.UpperCenter, false, true);
                n.label.rectTransform.pivot = new Vector2(0.5f, 1f);
                _pool.Add(n);
            }
            var node = _pool[idx];
            if (!node.rt.gameObject.activeSelf) node.rt.gameObject.SetActive(true);
            _cursorImg.transform.SetAsLastSibling();
            _hoverBg.transform.SetAsLastSibling();
            return node;
        }

        // ================================================================ texts / panel
        void UpdateTexts()
        {
            var cw = WorldAt(_cursor);
            int region = WorldRegions.RegionAt(cw.x, cw.z);
            float pct = MapDiscovery.RevealAll ? 100f : MapDiscovery.RevealedCells * 100f / (MapDiscovery.N * MapDiscovery.N);
            _status.text = "⌖ " + WorldRegions.RegionName(region) + "  (" + Mathf.RoundToInt(cw.x) + ", " + Mathf.RoundToInt(cw.z) + ")     줌 " + _zoom.ToString("0.0") + "x     탐색 " + pct.ToString("0") + "%";
            var hints = Root.Find("mapHints").GetComponent<Text>();
            hints.text = Glyph.Key("Map/Filter", "Tab") + " 필터 · " + Glyph.Key("Map/Center", "Space") + " 내 위치 · " + Glyph.Key("Map/Pin", "RMB") + " 핀 · " + Glyph.Key("UI/TabPrev", "Q") + "/" + Glyph.Key("UI/TabNext", "E") + " 워프 지점 · " + Glyph.Key("Map/Close", "M") + " 닫기";
            if (region != _lastRegion) { _lastRegion = region; RefreshRegionCard(region); }

            Vector3 op; string on;
            var pc = PlayerController.Instance;
            if (MapSystem.Objective(out op, out on) && pc != null)
            {
                var q = QuestSystem.I != null ? QuestSystem.I.Current : null;
                Vector3 qp = Vector3.zero; string qn = null, qobj = null;
                if (QuestSystem.I != null) QuestSystem.I.TrackedTarget(out qp, out qn, out qobj);
                string line = MapSystem.HasTracked ? "◇ 추적 · " + on : (qn != null ? "◇ " + qn + " · " + qobj : "");
                _bottom.text = line + "  ·  " + Mathf.RoundToInt(WuWaUtil.Flat(op - pc.transform.position).magnitude) + " m";
            }
            else _bottom.text = "◇ 추적 중인 목표 없음";
        }

        void RefreshRegionCard(int region)
        {
            var st = MapMarkers.Stats(region);
            _regionTitle.text = "▣ " + WorldRegions.RegionName(region);
            string body = "상자 " + st.chestsOpened + "/" + st.chests;
            body += "  ·  탑 " + (st.hasTower ? (st.towerOn ? "✓" : "미해방") : "-");
            body += "  ·  표석 " + (st.hasStone ? (st.stoneOn ? "✓" : "미조율") : "-");
            body += "\nNPC " + st.npcs + "  ·  갈고리 " + st.grapples + "  ·  " + (MapDiscovery.RegionDiscovered(region) ? "탐색함" : "미탐색");
            if (st.hasTower && !st.towerOn) body += "\n탑을 해방하면 이 지역의 상자가 모두 표시됩니다";
            body += "\n" + RegionCompletion.Summary(region);
            _regionBody.text = body;
        }

        void RefreshDetail()
        {
            bool show = _selected != null;
            if (_detailRoot.activeSelf != show) _detailRoot.SetActive(show);
            if (!show) return;
            var m = _selected;
            var pc = PlayerController.Instance;
            float dist = pc != null ? WuWaUtil.Flat(m.pos - pc.transform.position).magnitude : 0f;
            int region = WorldRegions.RegionAt(m.pos.x, m.pos.z);
            _detailName.text = m.name;
            _detailBody.text = WorldRegions.RegionName(region) + "  ·  " + Mathf.RoundToInt(dist) + " m" + (string.IsNullOrEmpty(m.status) ? "" : "\n" + m.status);
            _btnWarp.interactable = m.warpable;
            bool tracked = MapSystem.HasTracked && (MapSystem.TrackedPos - m.pos).sqrMagnitude < 0.01f;
            _btnTrack.GetComponentInChildren<Text>().text = tracked ? "추적 해제" : "추적";
            _btnPin.interactable = true;
            _btnPin.GetComponentInChildren<Text>().text = m.cat == MapCategory.Pin ? "핀 삭제" : "핀";
            _detailHint.text = m.warpable ? Glyph.Key("Map/Warp", "F") + " 워프  ·  " + Glyph.Key("Map/Pin", "RMB") + " 핀" : Glyph.Key("Map/Warp", "F") + " 추적  ·  " + Glyph.Key("Map/Pin", "RMB") + " 핀";
        }

        void RefreshPanel()
        {
            _panelTitle.text = _filterOpen ? "필터  (" + Glyph.Key("Map/Filter", "Tab") + " 닫기)" : "범례  (" + Glyph.Key("Map/Filter", "Tab") + " 필터)";
            int n = MapMarkers.Filterable.Length;
            var counts = new int[n];
            foreach (var m in MapMarkers.List) { int i = System.Array.IndexOf(MapMarkers.Filterable, m.cat); if (i >= 0) counts[i]++; }
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                bool isCat = i < n;
                bool extra = !isCat;
                if (r.go.activeSelf != (isCat || _filterOpen)) r.go.SetActive(isCat || _filterOpen);
                if (isCat)
                {
                    bool on = MapMarkers.Enabled(MapMarkers.Filterable[i]);
                    r.label.text = MapMarkers.FilterLabels[i];
                    r.state.text = _filterOpen ? (on ? "☑" : "☐") : counts[i].ToString();
                    r.icon.color = on ? Color.white : new Color(1f, 1f, 1f, 0.3f);
                    r.label.color = on ? UIKit.Theme.TextHi : UIKit.Theme.TextLo;
                }
                else
                {
                    r.label.text = i == n ? "전체 켜기" : "전체 끄기";
                    r.state.text = "";
                }
                r.bg.color = (_filterOpen && _padCursor && i == _filterRow) ? UIKit.Theme.Selected : new Color(1f, 1f, 1f, 0.03f);
            }
        }

        // ================================================================ actions
        void Select()
        {
            if (_hover != null)
            {
                if (_selected != null && SameMarker(_selected, _hover)) Primary(_selected);
                else { _selected = _hover; RefreshDetail(); UIKit.Sfx(2.2f, 0.15f); }
            }
            else if (_selected != null) { _selected = null; RefreshDetail(); }
        }

        internal void ClickAt(Vector2 local)
        {
            _cursor = local;
            Layout();
            Select();
        }

        void Primary(MapMarker m)
        {
            if (m.warpable) WarpConfirm(m);
            else if (m.cat == MapCategory.Pin)
            {
                var pin = m.source as MapPins.Pin;
                Modal.Choice("핀 · " + MapPins.ColorNames[pin != null ? pin.color : 0], "이 핀을 어떻게 할까요?", new[] { "색 변경", "삭제", "취소" }, k =>
                {
                    if (k == 0) MapPins.Cycle(pin);
                    else if (k == 1) { MapPins.Remove(pin); HUDController.Toast("핀 삭제"); if (_selected != null && _selected.cat == MapCategory.Pin) { _selected = null; RefreshDetail(); } }
                }, 2);
            }
            else ToggleTrack(m);
        }

        void WarpConfirm(MapMarker m)
        {
            string why;
            if (MapSystem.WarpBlocked(out why)) { HUDController.Toast(why); return; }
            var pc = PlayerController.Instance;
            float dist = pc != null ? WuWaUtil.Flat(m.pos - pc.transform.position).magnitude : 0f;
            Vector3 dest = m.pos; string name = m.name;
            Modal.Confirm("워프", name + "(으)로 워프 — " + Mathf.RoundToInt(dist) + " m", "워프", "취소", false, () => { if (MapSystem.I != null) MapSystem.I.Warp(dest, name); });
        }

        void ToggleTrack(MapMarker m)
        {
            if (MapSystem.HasTracked && (MapSystem.TrackedPos - m.pos).sqrMagnitude < 0.01f) MapSystem.ClearTracked();
            else MapSystem.SetTracked(m.pos, m.name);
            RefreshDetail();
        }

        void CycleWarp(int dir)
        {
            var list = new List<MapMarker>();
            foreach (var m in MapMarkers.List) if (m.warpable) list.Add(m);
            if (list.Count == 0) { HUDController.Toast("조율된 워프 지점이 없습니다"); return; }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            _cycleIdx = ((_cycleIdx + dir) % list.Count + list.Count) % list.Count;
            var target = list[_cycleIdx];
            _selected = target;
            _pan = -new Vector2(target.pos.x, target.pos.z) / MapSystem.WorldSize * BasePx * _zoom;
            _pan = ClampPan(_pan);
            _cursor = View(target.pos);
            _panAnim = false; _dragVel = Vector2.zero;
            RefreshDetail();
            UIKit.Sfx(2.4f, 0.1f);
        }

        void HandlePin()
        {
            if (InputService.MapPinPressed && !_filterOpen)
            {
                _pinDown = Time.unscaledTime;
                _pinTarget = MapPins.Nearest(WorldAt(_cursor), PxToMetres(14f));
                _pinConsumed = false;
            }
            if (_pinDown >= 0f)
            {
                if (!_pinConsumed && _pinTarget != null && InputService.MapPinHeld && Time.unscaledTime - _pinDown >= 0.5f)
                {
                    MapPins.Remove(_pinTarget); _pinConsumed = true;
                    HUDController.Toast("핀 삭제");
                    if (_selected != null && _selected.cat == MapCategory.Pin) { _selected = null; RefreshDetail(); }
                }
                if (!InputService.MapPinHeld)
                {
                    if (!_pinConsumed)
                    {
                        if (_pinTarget != null) MapPins.Cycle(_pinTarget);
                        else if (MapPins.Add(WorldAt(_cursor)) != null) UIKit.Sfx(2.6f, 0.15f);
                    }
                    _pinDown = -1f; _pinTarget = null;
                }
            }
        }

        // ================================================================ mouse drag / click on the viewport
        internal void DragBegin() { _dragging = true; _dragVel = Vector2.zero; _panAnim = false; }
        internal void Drag(Vector2 screenDelta)
        {
            float scale = Root.lossyScale.x > 0.0001f ? Root.lossyScale.x : 1f;
            var d = screenDelta / scale;
            _pan += d;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            _dragVel = Vector2.Lerp(_dragVel, d / dt, 0.5f);
            _dragTime = Time.unscaledTime;
        }
        internal void DragEnd() { _dragging = false; if (Time.unscaledTime - _dragTime > 0.08f) _dragVel = Vector2.zero; }

        public class ViewportInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public MapScreen screen;
            bool _dragged;
            public void OnPointerDown(PointerEventData e) { _dragged = false; }
            public void OnBeginDrag(PointerEventData e) { _dragged = true; screen.DragBegin(); }
            public void OnDrag(PointerEventData e) { screen.Drag(e.delta); }
            public void OnEndDrag(PointerEventData e) { screen.DragEnd(); }
            public void OnPointerUp(PointerEventData e)
            {
                if (_dragged || e.button != PointerEventData.InputButton.Left) return;
                Vector2 local;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(transform as RectTransform, e.position, null, out local)) screen.ClickAt(local);
            }
        }
    }
}
