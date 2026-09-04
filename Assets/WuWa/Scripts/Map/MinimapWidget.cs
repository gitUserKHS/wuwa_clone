using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace WuWa
{
    /// Circular minimap (top-left): rotate-with-camera or north-up, compass ring,
    /// shared marker atlas, aggro enemy dots, chest sparkle, off-screen indicators.
    public class MinimapWidget : MonoBehaviour
    {
        const float R = 98f;              // visible radius in px
        RectTransform _root, _mapRt, _fogRt, _markerParent, _arrow, _cone;
        RawImage _map, _fog;
        Text _regionText;
        readonly RectTransform[] _compass = new RectTransform[4];
        readonly RectTransform[] _off = new RectTransform[3];
        readonly Image[] _offImg = new Image[3];
        readonly Text[] _offText = new Text[3];
        class Node { public RectTransform rt; public Image img; public Text alt; }
        readonly List<Node> _pool = new List<Node>();
        readonly List<Node> _enemyPool = new List<Node>();
        float _next, _radiusCur = 120f;
        bool _visible = true;
        ResonanceTower[] _towers;
        Texture2D _tex;

        public void Build(Transform parent, Texture2D tex)
        {
            _tex = tex;
            _root = UIKit.Rect("minimap", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(122f, -148f), new Vector2(210f, 210f));

            var frame = UIKit.Img("frame", _root, new Color(0.04f, 0.05f, 0.07f, 0.78f), MapIcons.Get("disc"));
            frame.rectTransform.sizeDelta = new Vector2(214f, 214f);

            var maskImg = UIKit.Img("mask", _root, Color.white, MapIcons.Get("disc"));
            maskImg.rectTransform.sizeDelta = new Vector2(196f, 196f);
            var mask = maskImg.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var mapGo = new GameObject("map");
            mapGo.transform.SetParent(maskImg.transform, false);
            _mapRt = mapGo.AddComponent<RectTransform>();
            _mapRt.sizeDelta = new Vector2(284f, 284f);
            _map = mapGo.AddComponent<RawImage>();
            _map.texture = tex;
            _map.raycastTarget = false;

            var fogGo = new GameObject("fog");
            fogGo.transform.SetParent(maskImg.transform, false);
            _fogRt = fogGo.AddComponent<RectTransform>();
            _fogRt.sizeDelta = new Vector2(284f, 284f);
            _fog = fogGo.AddComponent<RawImage>();
            _fog.texture = MapDiscovery.Texture;
            _fog.color = new Color(0.03f, 0.04f, 0.06f, 0.8f);
            _fog.raycastTarget = false;

            var cone = UIKit.Img("cone", maskImg.transform, new Color(0.6f, 0.95f, 1f, 0.35f), MapIcons.Get("cone"));
            cone.rectTransform.sizeDelta = new Vector2(84f, 84f);
            cone.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _cone = cone.rectTransform;

            _markerParent = UIKit.Rect("markers", maskImg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var ring = UIKit.Img("ring", _root, new Color(1f, 0.85f, 0.45f, 0.6f), MapIcons.Get("ring"));
            ring.rectTransform.sizeDelta = new Vector2(206f, 206f);

            string[] letters = { "N", "E", "S", "W" };
            for (int i = 0; i < 4; i++)
            {
                var t = UIKit.Txt(letters[i], _root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 20f), letters[i], i == 0 ? 15 : 12,
                    i == 0 ? new Color(1f, 0.85f, 0.45f, 1f) : new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter, i == 0, true);
                _compass[i] = t.rectTransform;
            }

            var arrow = UIKit.Img("arrow", _root, new Color(1f, 0.92f, 0.55f), MapIcons.Get("player"));
            arrow.rectTransform.sizeDelta = new Vector2(24f, 24f);
            _arrow = arrow.rectTransform;

            string[] offIcons = { "quest", "rift", "tower" };
            for (int i = 0; i < 3; i++)
            {
                var img = UIKit.Img("off" + i, _root, Color.white, MapIcons.Get(offIcons[i]));
                img.rectTransform.sizeDelta = new Vector2(18f, 18f);
                _off[i] = img.rectTransform; _offImg[i] = img;
                _offText[i] = UIKit.Txt("d", img.transform, new Vector2(0.5f, 0f), new Vector2(0f, -2f), new Vector2(80f, 14f), "", 11, new Color(1f, 1f, 1f, 0.9f), TextAnchor.UpperCenter, false, true);
                _offText[i].rectTransform.pivot = new Vector2(0.5f, 1f);
                img.gameObject.SetActive(false);
            }

            _regionText = UIKit.Txt("region", _root, new Vector2(0.5f, 0f), new Vector2(0f, -6f), new Vector2(240f, 20f), "", 13, new Color(1f, 0.95f, 0.8f, 0.9f), TextAnchor.UpperCenter, false, true);
            _regionText.rectTransform.pivot = new Vector2(0.5f, 1f);
        }

        public void SetVisible(bool on)
        {
            _visible = on;
            if (_root != null && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }

        Node GetNode(List<Node> pool, int idx, bool alt)
        {
            while (pool.Count <= idx)
            {
                var img = UIKit.Img("mk", _markerParent, Color.white, MapIcons.Get("dot"));
                var n = new Node { rt = img.rectTransform, img = img };
                if (alt)
                {
                    n.alt = UIKit.Txt("alt", img.transform, new Vector2(1f, 0.5f), new Vector2(2f, 0f), new Vector2(14f, 14f), "", 10, Color.white, TextAnchor.MiddleLeft, true, true);
                    n.alt.rectTransform.pivot = new Vector2(0f, 0.5f);
                }
                pool.Add(n);
            }
            return pool[idx];
        }

        static Vector2 Rot(Vector2 d, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
        }

        void LateUpdate()
        {
            if (!_visible || _root == null) return;
            var pc = PlayerController.Instance;
            if (pc == null || _tex == null) return;
            float dt = Time.unscaledDeltaTime;
            Vector3 pp = pc.transform.position;
            float camYaw = Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f;
            float charYaw = pc.transform.eulerAngles.y;
            bool rotate = MapSystem.MinimapMode == 1;
            float rot = rotate ? camYaw : 0f;

            _radiusCur = Mathf.Lerp(_radiusCur, MapSystem.MinimapRadius, 1f - Mathf.Exp(-8f * dt));
            // free-cursor click on the minimap opens the full map centred on that point (design doc 10.6)
            if (GameDirector.CursorFree && !ScreenRouter.IsOpen && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (TryOpenMapAt(Mouse.current.position.ReadValue(), pp, rot)) return;
            }
            float w = _radiusCur * 2f * 1.4142f / MapSystem.WorldSize;
            var c01 = MapSystem.World01(pp);
            var uv = new Rect(c01.x - w * 0.5f, c01.y - w * 0.5f, w, w);
            _map.uvRect = uv; _fog.uvRect = uv;
            var q = Quaternion.Euler(0f, 0f, rot);
            _mapRt.localRotation = q; _fogRt.localRotation = q;
            _arrow.localRotation = Quaternion.Euler(0f, 0f, rotate ? -(charYaw - camYaw) : -charYaw);
            _cone.localRotation = Quaternion.Euler(0f, 0f, rotate ? 0f : -camYaw);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                _compass[i].anchoredPosition = Rot(new Vector2(Mathf.Sin(a), Mathf.Cos(a)), rot) * 90f;
            }

            _next -= dt;
            if (_next > 0f) return;
            _next = 0.1f;

            float pxPerM = R / _radiusCur;
            MapMarkers.Collect();
            int used = 0;
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 10f);
            foreach (var m in MapMarkers.List)
            {
                if (!m.onMinimap || m.cat == MapCategory.Player || !MapMarkers.Enabled(m.cat)) continue;
                if (m.cat == MapCategory.Grapple || m.cat == MapCategory.Camp) continue;
                Vector2 d = new Vector2(m.pos.x - pp.x, m.pos.z - pp.z);
                float dist = d.magnitude;
                if (dist > _radiusCur * 1.05f) continue;
                if (m.cat == MapCategory.Chest && dist > 70f) continue;
                Vector2 v = Rot(d, rot) * pxPerM;
                if (v.magnitude > R - 6f) continue;
                var n = GetNode(_pool, used++, true);
                n.rt.gameObject.SetActive(true);
                n.rt.anchoredPosition = v;
                n.img.sprite = MapIcons.Get(m.icon);
                float size = m.cat == MapCategory.Tower ? 17f : m.cat == MapCategory.Chest ? 10f : m.cat == MapCategory.Quest || m.cat == MapCategory.Tracked ? 16f : 13f;
                n.rt.sizeDelta = new Vector2(size, size);
                var col = m.color;
                if (m.cat == MapCategory.Chest) col.a = pulse;
                n.img.color = col;
                float dy = m.pos.y - pp.y;
                n.alt.text = dy > 8f ? "▲" : dy < -8f ? "▼" : "";
            }
            // aggro enemies
            int eu = 0;
            foreach (var e in EnemyAI.All)
            {
                if (eu >= 12) break;
                if (e == null || e.Hp == null || !e.Hp.IsAlive || !e.IsAggro) continue;
                Vector2 d = new Vector2(e.transform.position.x - pp.x, e.transform.position.z - pp.z);
                Vector2 v = Rot(d, rot) * pxPerM;
                if (v.magnitude > R - 4f) continue;
                var n = GetNode(_enemyPool, eu++, false);
                n.rt.gameObject.SetActive(true);
                n.rt.anchoredPosition = v;
                n.img.sprite = MapIcons.Get(e.isBoss ? "boss" : "dot");
                n.rt.sizeDelta = e.isBoss ? new Vector2(13f, 13f) : new Vector2(7f, 7f);
                n.img.color = new Color(1f, 0.3f, 0.25f, 0.95f);
            }
            for (int i = used; i < _pool.Count; i++) _pool[i].rt.gameObject.SetActive(false);
            for (int i = eu; i < _enemyPool.Count; i++) _enemyPool[i].rt.gameObject.SetActive(false);

            // off-screen indicators: objective, active rift, nearest inactive tower
            Vector3 op; string on;
            bool hasObj = MapSystem.Objective(out op, out on);
            PlaceOff(0, hasObj, op, pp, rot, MapSystem.HasTracked ? new Color(0.6f, 0.9f, 1f) : MapMarkers.QuestC);
            Vector3 riftPos = Vector3.zero; bool hasRift = false;
            foreach (var dp in MapSystem.Dynamic) if (dp.cat == MapCategory.Rift) { hasRift = true; riftPos = dp.pos; break; }
            PlaceOff(1, hasRift, riftPos, pp, rot, MapMarkers.RiftC);
            if (_towers == null || _towers.Length == 0) _towers = FindObjectsByType<ResonanceTower>(FindObjectsSortMode.None);
            ResonanceTower near = null; float nd = float.MaxValue;
            foreach (var t in _towers)
            {
                if (t == null || t.Activated) continue;
                float d = WuWaUtil.Flat(t.transform.position - pp).magnitude;
                if (d < nd) { nd = d; near = t; }
            }
            PlaceOff(2, near != null, near != null ? near.transform.position : Vector3.zero, pp, rot, MapMarkers.TowerOff);

            _regionText.text = WorldRegions.RegionName(WorldRegions.RegionAt(pp.x, pp.z));
        }

        /// Screen point inside the disc → world point → full map (2.4x) centred there.
        public bool TryOpenMapAt(Vector2 screenPos, Vector3 pp, float rot)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screenPos, null, out local) || local.magnitude > R) return false;
            Vector2 dm = Rot(local, -rot) * (_radiusCur / R);
            MapScreen.PendingFocus = new Vector3(pp.x + dm.x, pp.y, pp.z + dm.y);
            ScreenRouter.Push("Map");
            return true;
        }

        public RectTransform RootRect { get { return _root; } }

        void PlaceOff(int i, bool has, Vector3 target, Vector3 pp, float rot, Color col)
        {
            Vector2 d = new Vector2(target.x - pp.x, target.z - pp.z);
            float dist = d.magnitude;
            bool show = has && dist > _radiusCur;
            if (_off[i].gameObject.activeSelf != show) _off[i].gameObject.SetActive(show);
            if (!show) return;
            Vector2 dir = Rot(d.normalized, rot);
            _off[i].anchoredPosition = dir * 116f;
            _offImg[i].color = col;
            _offText[i].text = dist >= 1000f ? (dist / 1000f).ToString("0.0") + "km" : Mathf.RoundToInt(dist) + "m";
        }
    }
}
