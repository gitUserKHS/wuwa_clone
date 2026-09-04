using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Dashed polyline drawn as UI geometry (route hints on the map).
    [RequireComponent(typeof(CanvasRenderer))]
    public class UILine : MaskableGraphic
    {
        public readonly List<Vector2> points = new List<Vector2>();
        public float thickness = 3f;
        public float dash = 12f;
        public float gap = 9f;

        public void SetPoints(Vector2 a, Vector2 b)
        {
            points.Clear();
            points.Add(a); points.Add(b);
            SetVerticesDirty();
        }

        public void Clear() { points.Clear(); SetVerticesDirty(); }

        public void SetPolyline(List<Vector2> pts)
        {
            points.Clear();
            if (pts != null) points.AddRange(pts);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points.Count < 2) return;
            var col = color;
            float phase = 0f;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a = points[i], b = points[i + 1];
                Vector2 dir = b - a;
                float len = dir.magnitude;
                if (len < 0.01f) continue;
                dir /= len;
                Vector2 nrm = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);
                float t = -phase;
                while (t < len)
                {
                    float s0 = Mathf.Max(t, 0f), s1 = Mathf.Min(t + dash, len);
                    if (s1 > s0)
                    {
                        Vector2 p0 = a + dir * s0, p1 = a + dir * s1;
                        int v = vh.currentVertCount;
                        vh.AddVert(p0 - nrm, col, Vector2.zero);
                        vh.AddVert(p0 + nrm, col, Vector2.zero);
                        vh.AddVert(p1 + nrm, col, Vector2.zero);
                        vh.AddVert(p1 - nrm, col, Vector2.zero);
                        vh.AddTriangle(v, v + 1, v + 2);
                        vh.AddTriangle(v, v + 2, v + 3);
                    }
                    t += dash + gap;
                }
                phase = (len + phase) % (dash + gap);
            }
        }
    }
}
