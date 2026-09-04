using UnityEngine;

namespace WuWa
{
    /// Orbit camera with collision, lock-on framing, trauma shake and FOV kicks.
    /// Input comes from InputService; sensitivity/invert/distance/FOV/assist come
    /// from the settings store (design doc ch.9.6 / ch.10).
    public class ThirdPersonCamera : MonoBehaviour
    {
        public Transform target;
        public float pitchMin = -38f, pitchMax = 68f;
        public float minDistance = 2f, maxDistance = 7.5f;
        public float pivotHeight = 1.55f;

        // ---- settings (pushed by SettingsAppliers)
        public static float MouseSensX = 0.132f, MouseSensY = 0.132f;
        public static float PadYawRate = 130f, PadPitchRate = 100f;
        public static bool PadAccel = true;
        public static bool InvertX, InvertY;
        public static bool LockCamTrack = true;
        public static float LockAssist = 3.2f;
        public static bool MoveCamCorrect;
        public static float DistanceSetting = 4.8f;
        public static float CombatDistanceSetting = 5.6f;
        public static float FovBase = 55f;
        public static bool TitleOrbit;                 // title screen: slow orbit, no look input

        float _yaw, _pitch = 14f;
        float _distTarget;                  // user-controlled orbit distance
        float _curDistance;                 // after collision
        Camera _cam;
        PlayerController _player;
        static ThirdPersonCamera _inst;
        float _fovPunch, _fovPunchVel;
        float _fovPunchTime = 0.35f;
        float _lastPitchInput = -99f;
        float _lastLookInput = -99f;
        float _padFullTime;
        float _recenterUntil = -1f;
        Vector3 _shakeOffset;
        float _lastAppliedSetting = -1f;

        public static void PunchFov(float amount, float time)
        {
            if (_inst == null) return;
            _inst._fovPunch = amount;
            _inst._fovPunchTime = Mathf.Clamp(time, 0.12f, 1.2f);
        }

        /// Lock-on press with nothing to lock: swing the camera behind the player.
        public static void RecenterRequest()
        {
            if (_inst != null) _inst._recenterUntil = Time.unscaledTime + 0.45f;
        }

        public float Yaw { get { return _yaw; } }

        void Awake()
        {
            _inst = this;
            _cam = GetComponent<Camera>();
            _distTarget = DistanceSetting;
            _curDistance = _distTarget;
            _lastAppliedSetting = DistanceSetting;
        }

        void Start()
        {
            if (target == null)
            {
                var p = PlayerController.Instance != null ? PlayerController.Instance : Object.FindAnyObjectByType<PlayerController>();
                if (p != null) target = p.transform;
            }
            if (target != null)
            {
                _player = target.GetComponent<PlayerController>();
                _yaw = target.eulerAngles.y;
            }
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.unscaledDeltaTime;

            // the settings slider moves the orbit; the wheel/pad moves it too and writes back
            if (!Mathf.Approximately(_lastAppliedSetting, DistanceSetting)) { _distTarget = DistanceSetting; _lastAppliedSetting = DistanceSetting; }

            bool lookAllowed = !GameDirector.CursorFree && !GameDirector.MenuOpen && !Cutscene.Active && !DialogueSystem.Active;
            if (TitleOrbit) { lookAllowed = false; _yaw += 5f * dt; _pitch = Mathf.Lerp(_pitch, 9f, 1f - Mathf.Exp(-2f * dt)); }
            if (lookAllowed)
            {
                Vector2 m = InputService.LookMouse;
                if (m.sqrMagnitude > 0.0001f)
                {
                    _yaw += m.x * MouseSensX * (InvertX ? -1f : 1f);
                    _pitch -= m.y * MouseSensY * (InvertY ? -1f : 1f);
                    if (Mathf.Abs(m.y) > 0.6f) _lastPitchInput = Time.unscaledTime;
                    _lastLookInput = Time.unscaledTime;
                }
                Vector2 s = InputService.LookStick;
                if (s.sqrMagnitude > 0.0001f)
                {
                    float accel = 1f;
                    if (PadAccel)
                    {
                        _padFullTime = s.magnitude > 0.95f ? _padFullTime + dt : 0f;
                        accel = _padFullTime > 0.3f ? 1.5f : 1f;
                    }
                    _yaw += s.x * PadYawRate * accel * dt * (InvertX ? -1f : 1f);
                    _pitch -= s.y * PadPitchRate * accel * dt * (InvertY ? -1f : 1f);
                    if (Mathf.Abs(s.y) > 0.3f) _lastPitchInput = Time.unscaledTime;
                    _lastLookInput = Time.unscaledTime;
                }
                else _padFullTime = 0f;

                float zoom = InputService.Zoom;
                if (Mathf.Abs(zoom) > 0.001f)
                {
                    _distTarget = Mathf.Clamp(_distTarget * Mathf.Pow(1.12f, -zoom), minDistance, maxDistance);
                    SettingsStore.D.camDistance = _distTarget;
                    DistanceSetting = _distTarget; _lastAppliedSetting = _distTarget;
                    SettingsStore.MarkDirty();
                }
            }
            _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);

            // ---- recenter (lock-on press with no target, or moving-camera correction)
            bool recentering = Time.unscaledTime < _recenterUntil;
            if (recentering || (MoveCamCorrect && _player != null && _player.MoveInput.sqrMagnitude > 0.2f && Time.unscaledTime - _lastLookInput > 1.2f))
            {
                float wantYaw = target.eulerAngles.y;
                float rate = recentering ? 9f : 1.6f;
                _yaw = Mathf.LerpAngle(_yaw, wantYaw, 1f - Mathf.Exp(-rate * dt));
            }

            // ---- lock-on soft aim
            var lockOn = _player != null ? _player.LockOn : null;
            bool locked = lockOn != null && lockOn.Target != null;
            if (locked && LockCamTrack)
            {
                Vector3 to = lockOn.Target.position - target.position;
                float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                _yaw = Mathf.LerpAngle(_yaw, wantYaw, 1f - Mathf.Exp(-LockAssist * dt));
                if (Time.unscaledTime - _lastPitchInput > 1.5f)      // the player's own vertical look wins
                    _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(12f + to.magnitude * 0.35f, 8f, 24f), 1f - Mathf.Exp(-2f * dt));
            }

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = target.position + Vector3.up * pivotHeight;

            // ---- distance: combat pulls out a little, lock-on frames both actors
            float wantDist = _distTarget;
            if (TitleOrbit) wantDist = Mathf.Max(wantDist, 7.5f);
            if (_player != null && _player.InCombat) wantDist = Mathf.Max(wantDist, CombatDistanceSetting);
            if (locked) wantDist = Mathf.Clamp(wantDist + (lockOn.Target.position - target.position).magnitude * 0.06f, wantDist, maxDistance + 0.5f);
            if (_player != null && _player.IsGliding) wantDist += 1.2f;
            if (_player != null && _player.IsSwimming) wantDist -= 0.4f;

            // ---- collision
            Vector3 desired = pivot - rot * Vector3.forward * wantDist;
            float targetDist = wantDist;
            RaycastHit hit;
            Vector3 castDir = (desired - pivot).normalized;
            int mask = ~(Layers.PlayerMask | Layers.EnemyMask | (1 << Layers.Pickup));
            if (Physics.SphereCast(pivot, 0.28f, castDir, out hit, wantDist, mask, QueryTriggerInteraction.Ignore))
                targetDist = Mathf.Max(hit.distance - 0.1f, 0.5f);
            float rate2 = targetDist < _curDistance ? 12f : 3f;          // pull in fast, ease out slow
            _curDistance = Mathf.Lerp(_curDistance, targetDist, 1f - Mathf.Exp(-rate2 * dt));

            // ---- shake
            CameraShaker.Trauma = Mathf.Max(0f, CameraShaker.Trauma - dt * 1.3f);
            float tr = CameraShaker.Trauma * CameraShaker.Trauma;
            if (locked) tr *= 0.6f;
            if (GameDirector.MenuOpen) tr = 0f;
            float t = Time.unscaledTime * 26f;
            _shakeOffset = new Vector3(
                (Mathf.PerlinNoise(t, 0.5f) - 0.5f), (Mathf.PerlinNoise(0.5f, t) - 0.5f), 0f) * 0.55f * tr;
            Quaternion shakeRot = Quaternion.Euler(
                (Mathf.PerlinNoise(t, 9.7f) - 0.5f) * 4.5f * tr,
                (Mathf.PerlinNoise(7.3f, t) - 0.5f) * 4.5f * tr,
                (Mathf.PerlinNoise(t, 3.1f) - 0.5f) * 5.5f * tr);

            Vector3 pos = pivot - rot * Vector3.forward * _curDistance + rot * _shakeOffset;
            if (_player != null && _player.IsSwimming && !_player.IsDiving) pos.y = Mathf.Max(pos.y, WorldRegions.WaterY + 0.3f);
            transform.position = pos;
            transform.rotation = rot * shakeRot;

            // ---- fov
            if (_cam != null)
            {
                float want = FovBase;
                if (_player != null && _player.PlanarSpeed > _player.runSpeed + 0.5f) want = FovBase + 5f;
                if (_player != null && _player.IsGliding) want = FovBase + 7f;
                _fovPunch = Mathf.SmoothDamp(_fovPunch, 0f, ref _fovPunchVel, _fovPunchTime * 0.5f, 999f, dt);
                _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, want + _fovPunch, 1f - Mathf.Exp(-6f * dt));
            }
        }
    }
}
