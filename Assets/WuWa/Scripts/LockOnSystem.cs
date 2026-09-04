using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Press = lock the best shadow (or cycle to the next one while locked);
    /// hold = release. With nothing to lock, the press recenters the camera.
    public class LockOnSystem : MonoBehaviour
    {
        public float maxDistance = 30f;

        public Transform Target { get; private set; }
        Health _targetHealth;

        float _recenterPending = -1f;
        bool _zoomUsed;

        void Update()
        {
            if (InputService.LockOnHoldPerformed)
            {
                if (Target != null) Clear();
            }
            else if (InputService.LockOnPressed && !GameDirector.CursorFree && !GameDirector.MenuOpen)
            {
                if (Target != null)
                {
                    var prev = Target;
                    Acquire(prev);                 // cycle to the next shadow; nothing else = keep
                    if (Target == prev) Clear();
                }
                else if (!Acquire()) { _recenterPending = Time.unscaledTime; _zoomUsed = false; }   // recenter on a short tap only (R3 hold = camera zoom)
            }

            if (_recenterPending >= 0f)
            {
                if (InputService.ZoomModifierHeld && Mathf.Abs(InputService.Zoom) > 0.0001f) _zoomUsed = true;
                float held = Time.unscaledTime - _recenterPending;
                if (!InputService.LockOnHeld)
                {
                    if (!_zoomUsed && held < 0.35f) ThirdPersonCamera.RecenterRequest();
                    _recenterPending = -1f;
                }
                else if (held >= 0.35f) _recenterPending = -1f;
            }

            if (Target != null)
            {
                bool dead = _targetHealth == null || !_targetHealth.IsAlive || !Target.gameObject.activeInHierarchy;
                bool far = (Target.position - transform.position).sqrMagnitude > maxDistance * maxDistance * 1.44f;
                if (dead || far)
                {
                    Clear();
                    if (dead) Acquire();   // chain to the next enemy mid-fight
                }
            }
        }

        bool Acquire(Transform exclude = null)
        {
            var cam = CamCache.Main;
            Vector3 fwd = cam != null ? cam.transform.forward : transform.forward;
            float best = float.MaxValue;
            Health bestH = null;

            var all = EnemyAI.All;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null || e.Hp == null || !e.Hp.IsAlive || !e.gameObject.activeInHierarchy || e.transform == exclude) continue;
                Vector3 to = e.transform.position - transform.position;
                float dist = to.magnitude;
                if (dist > maxDistance) continue;
                float ang = Vector3.Angle(WuWaUtil.Flat(fwd), WuWaUtil.Flat(to));
                float score = ang * 1.6f + dist;
                if (score < best) { best = score; bestH = e.Hp; }
            }

            if (bestH != null)
            {
                Target = bestH.transform;
                _targetHealth = bestH;
                HUDController.SetLockTarget(_targetHealth);
                return true;
            }
            return false;
        }

        void OnDisable() { Clear(); }

        void Clear()
        {
            Target = null;
            _targetHealth = null;
            HUDController.SetLockTarget(null);
        }
    }
}
