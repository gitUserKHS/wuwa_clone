using System;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Player-placed map pins (max 10, six colours), saved with the game.
    public static class MapPins
    {
        public class Pin { public Vector3 pos; public int color; }
        public const int Max = 10;
        public static readonly List<Pin> All = new List<Pin>();
        public static readonly Color[] Colors =
        {
            new Color(1f, 0.45f, 0.35f), new Color(1f, 0.8f, 0.3f), new Color(0.5f, 0.95f, 0.5f),
            new Color(0.45f, 0.8f, 1f), new Color(0.75f, 0.55f, 1f), new Color(1f, 1f, 1f),
        };
        public static readonly string[] ColorNames = { "빨강", "노랑", "초록", "파랑", "보라", "하양" };
        public static event Action Changed;

        public static Pin Add(Vector3 pos)
        {
            if (All.Count >= Max) { HUDController.Toast("핀은 최대 " + Max + "개까지 놓을 수 있습니다"); return null; }
            var p = new Pin { pos = pos, color = All.Count % Colors.Length };
            All.Add(p);
            Notify();
            return p;
        }

        public static void Remove(Pin p) { if (All.Remove(p)) Notify(); }
        public static void Cycle(Pin p) { if (p == null) return; p.color = (p.color + 1) % Colors.Length; Notify(); }
        public static void Clear() { All.Clear(); Notify(); }

        public static Pin Nearest(Vector3 world, float maxDist)
        {
            Pin best = null; float bd = maxDist;
            foreach (var p in All)
            {
                float d = WuWaUtil.Flat(p.pos - world).magnitude;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        public static void Export(out float[] xz, out int[] colors)
        {
            xz = new float[All.Count * 2]; colors = new int[All.Count];
            for (int i = 0; i < All.Count; i++) { xz[i * 2] = All[i].pos.x; xz[i * 2 + 1] = All[i].pos.z; colors[i] = All[i].color; }
        }

        public static void Import(float[] xz, int[] colors)
        {
            All.Clear();
            if (xz != null)
                for (int i = 0; i + 1 < xz.Length && i / 2 < Max; i += 2)
                    All.Add(new Pin { pos = new Vector3(xz[i], 0f, xz[i + 1]), color = colors != null && i / 2 < colors.Length ? colors[i / 2] : 0 });
            Notify();
        }

        static void Notify() { if (Changed != null) Changed(); }
    }
}
