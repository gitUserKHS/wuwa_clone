using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    public interface IInteractable
    {
        Vector3 InteractPosition { get; }
        float InteractRange { get; }
        int InteractPriority { get; }
        string InteractLabel { get; }
        bool CanInteract { get; }
        void Interact();
    }

    /// One prompt, one key: picks the best interactable near the player each
    /// frame and routes the Interact action to it (design doc 7.11).
    public static class InteractionManager
    {
        static readonly List<IInteractable> _all = new List<IInteractable>();
        public static IInteractable Best { get; private set; }
        public static int NearbyCount { get; private set; }

        public static void Register(IInteractable i) { if (!_all.Contains(i)) _all.Add(i); }
        public static void Unregister(IInteractable i) { _all.Remove(i); }

        public static void Tick()
        {
            Best = null; NearbyCount = 0;
            if (Cutscene.Active || DialogueSystem.Active || GameDirector.MenuOpen || !InputService.GameplayActive) return;
            var pc = PlayerController.Instance;
            if (pc == null) return;
            Vector3 pp = pc.transform.position;
            float bestScore = float.MinValue;
            for (int i = 0; i < _all.Count; i++)
            {
                var it = _all[i];
                if (it == null || !it.CanInteract) continue;
                float d = WuWaUtil.Flat(it.InteractPosition - pp).magnitude;
                if (d > it.InteractRange) continue;
                NearbyCount++;
                float score = it.InteractPriority * 100f - d;
                if (score > bestScore) { bestScore = score; Best = it; }
            }
            if (Best == null) return;
            string label = Best.InteractLabel + (NearbyCount > 1 ? "  (+" + (NearbyCount - 1) + ")" : "");
            HUDController.SetInteractPrompt(Glyph.Prompt(label));
            if (InputService.InteractPressed) Best.Interact();
        }
    }
}
