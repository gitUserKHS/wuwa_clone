using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WuWa
{
    /// WuWa-style traversal: run/sprint, double jump, glide, wall run, grapple hook,
    /// dash-dodge with i-frames and perfect-dodge counter windows.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour, IDamageable
    {
        [Header("Movement")]
        public float runSpeed = 4.6f;
        public float sprintSpeed = 7.2f;
        public float combatRunSpeed = 4.2f;      // deliberate pace while enemies are engaged
        public float combatSprintSpeed = 5.5f;
        public float accel = 20f;
        public float airControl = 0.55f;
        public float turnSharpness = 12f;
        public float gravity = -24f;
        public float terminalFall = -30f;

        [Header("Jump / Glide")]
        public float jumpVel = 8.6f;
        public float doubleJumpVel = 8.0f;
        public float glideFallSpeed = -1.7f;
        public float glideMoveSpeed = 6.5f;
        public float coyoteTime = 0.12f;

        [Header("Dodge")]
        public float dodgeSpeed = 13.5f;
        public float dodgeDuration = 0.42f;
        public float dodgeCooldown = 0.5f;

        [Header("Stamina (WuWa)")]
        public float staminaMax = 240f;
        public float staminaRegen = 40f;
        public float sprintDrain = 11f;
        public float glideDrain = 8f;
        public float wallRunDrain = 16f;
        public float dodgeCost = 20f;
        public float doubleJumpCost = 10f;

        public float Stamina { get; private set; } = -1f;
        public bool StaminaExhausted { get; private set; }
        float _staminaDelay;

        [Header("Swim (WuWa)")]
        public float swimSpeed = 3.3f;
        public float swimSprintSpeed = 5.1f;
        public float swimDrain = 4f;
        public float swimSprintDrain = 9f;

        public bool IsSwimming { get; private set; }
        Vector3 _lastDryPos;
        float _swimFxTimer;
        public Vector2 iFrameWindow = new Vector2(0.02f, 0.26f);   // i-frames cover the first ~60% of the dodge

        [Header("Wall Run")]
        public float wallRunClimbSpeed = 5.6f;
        public float wallRunMaxTime = 2.2f;
        public float wallKickVel = 7.5f;

        [Header("Grapple")]
        public float grappleSpeed = 24f;

        // debug hooks for CLI-driven testing (no real input injection needed)
        public static Vector2 DbgMove { get { return InputService.DbgMove; } set { InputService.DbgMove = value; } }
        public static bool DbgSprint { get { return InputService.DbgSprint; } set { InputService.DbgSprint = value; } }
        public static bool DbgJumpHeld { get { return InputService.DbgJumpHeld; } set { InputService.DbgJumpHeld = value; } }

        CharacterController _cc;
        TeamManager _team;
        PlayerCombat _combat;
        Animator _anim;

        Vector3 _velocity;
        Vector3 _planarVel;
        Vector3 _impulse;
        float _lastGroundedTime;
        int _jumpsUsed;
        bool _gliding;
        bool _dodging;
        float _dodgeEndTime;
        float _nextDodgeTime;
        float _iFrameStart, _iFrameEnd;
        float _dodgeStartTime = -99f;
        bool _wasGrounded;
        float _regenDelay;

        // wall run
        bool _wallRunning;
        Vector3 _wallNormal;
        float _wallRunT;
        float _wallRunLockout;

        // grapple
        bool _grappling;
        Coroutine _grappleRoutine;

        // plunge (combat-driven)
        bool _plunging;

        public bool IsGrounded { get; private set; }
        public bool IsDodging { get { return _dodging; } }
        public bool IsGliding { get { return _gliding; } }
        public bool IsWallRunning { get { return _wallRunning; } }
        public bool IsGrappling { get { return _grappling; } }
        public bool IsPlunging { get { return _plunging; } }
        public float DodgeEndTime { get { return _dodgeEndTime; } }
        public float LastPerfectDodge { get; private set; }
        public GrapplePoint GrappleCandidate { get; private set; }
        public bool Invulnerable { get { return Time.time >= _iFrameStart && Time.time <= _iFrameEnd; } }
        public float MovementLock { get; set; }
        public float PlanarSpeed { get { return new Vector2(_planarVel.x, _planarVel.z).magnitude; } }
        public bool SprintHeld { get; private set; }
        public Vector2 MoveInput { get; private set; }
        public LockOnSystem LockOn { get; private set; }
        public bool InCombat { get; private set; }
        float _combatCheckTimer;

        public bool IsAlive { get { return _team == null || _team.AnyAlive; } }
        public static PlayerController Instance { get; private set; }
        public Transform Root { get { return transform; } }

        void Awake()
        {
            Instance = this;
            DbgMove = Vector2.zero; DbgSprint = false; DbgJumpHeld = false;   // statics survive scene reloads
            _cc = GetComponent<CharacterController>();
            _team = GetComponent<TeamManager>();
            _combat = GetComponent<PlayerCombat>();
            LockOn = GetComponent<LockOnSystem>();
            LastPerfectDodge = -999f;
        }

        public void BindAnimator(Animator anim)
        {
            _anim = anim;
            _hasCombatParam = false;
            if (_anim != null)
            {
                _anim.applyRootMotion = false;
                foreach (var p in _anim.parameters) if (p.nameHash == CombatHash) _hasCombatParam = true;
                WuWaUtil.Fade(_anim, "Loco", 0.05f);
            }
        }

        public Animator Anim { get { return _anim; } }

        void Update()
        {
            if (Time.timeScale <= 0.001f) return;
            if (Cutscene.Active || DialogueSystem.Active)
            {
                MoveInput = Vector2.zero;
                if (_anim != null) _anim.SetFloat(SpeedHash, 0f, 0.1f, Time.deltaTime);
                return;
            }
            float dt = Time.deltaTime;
            ReadMoveInput();
            TickItems();
            UpdateCombatState(dt);

            IsGrounded = CheckGrounded();
            if (IsGrounded) { _lastGroundedTime = Time.time; _jumpsUsed = 0; }

            bool busy = _combat != null && _combat.IsBusy;

            UpdateSwimState();
            UpdateGrappleScan();
            if (!_grappling && !_plunging && !IsSwimming)
            {
                HandleGrappleInput(busy);
                HandleDodge(busy);
            }

            if (_grappling || _plunging)
            {
                // routines drive velocity
            }
            else if (IsSwimming)
            {
                SwimMove(dt);
            }
            else if (_wallRunning)
            {
                WallRunMove(dt);
            }
            else if (_dodging)
            {
                DodgeMove(dt);
            }
            else
            {
                TryStartWallRun(busy);
                if (!_wallRunning)
                {
                    HandleJumpGlide(busy);
                    HandleMove(dt, busy);
                }
            }

            if (!_grappling && !IsSwimming) ApplyGravity(dt);

            Vector3 motion = (_planarVel + _impulse) * dt + Vector3.up * _velocity.y * dt;
            _cc.Move(motion);
            _impulse = Vector3.MoveTowards(_impulse, Vector3.zero, 18f * dt);

            if (!_wasGrounded && IsGrounded)
            {
                AudioMan.I.Play(Sfx.Land(), transform.position, 0.5f);
                _gliding = false;
                if (!busy && !_plunging && !IsSwimming) WuWaUtil.Fade(_anim, "Loco", 0.08f);
            }
            _wasGrounded = IsGrounded;

            DriveAnimator(dt, busy);
            PassiveRegen(dt);
            TickStamina(dt);
        }

        // ------------------------------------------------------------------ swim
        void UpdateSwimState()
        {
            float groundH = WorldRegions.HeightAt(transform.position.x, transform.position.z);
            bool deepWater = groundH < WorldRegions.WaterY - 1.05f
                             && transform.position.y < WorldRegions.WaterY + 0.45f;

            if (!IsSwimming && IsGrounded && groundH > WorldRegions.WaterY + 0.15f)
                _lastDryPos = transform.position;                 // last safe shore

            if (!IsSwimming && deepWater)
            {
                IsSwimming = true;
                _gliding = false;
                _dodging = false;
                if (_wallRunning) EndWallRun(0.2f);
                _velocity.y = 0f;
                _jumpsUsed = 1;
                WuWaUtil.Fade(_anim, "Glide", 0.25f);             // floating pose
                VFXLibrary.SpawnNova(new Vector3(transform.position.x, WorldRegions.WaterY, transform.position.z),
                    new Color(0.6f, 0.9f, 1f), 2.2f);
                AudioMan.I.Play(Sfx.Land(), transform.position, 0.6f, 0.7f);
            }
            else if (IsSwimming && !deepWater)
            {
                IsSwimming = false;
                _velocity.y = 0.5f;
                WuWaUtil.Fade(_anim, IsGrounded ? "Loco" : "Fall", 0.15f);
            }
        }

        void SwimMove(float dt)
        {
            Vector3 wish = CamRelativeInput();
            bool moving = wish.sqrMagnitude > 0.01f;
            bool sprint = SprintHeld && moving;
            float target = sprint ? swimSprintSpeed : swimSpeed;
            _planarVel = Vector3.MoveTowards(_planarVel, wish.normalized * (moving ? target : 0f), 12f * dt);
            if (moving) FaceInstant(wish);

            // bob on the surface, chest-deep
            float surfY = WorldRegions.WaterY - 0.85f + Mathf.Sin(Time.time * 1.7f) * 0.05f;
            _velocity.y = Mathf.Clamp((surfY - transform.position.y) * 5f, -4f, 4f);

            UseStamina((sprint ? swimSprintDrain : swimDrain) * dt);

            _swimFxTimer -= dt;
            if (_swimFxTimer <= 0f && moving)
            {
                _swimFxTimer = 0.5f;
                VFXLibrary.Flash(new Vector3(transform.position.x, WorldRegions.WaterY + 0.05f, transform.position.z),
                    new Color(0.7f, 0.95f, 1f), 1.1f, 0.35f);
            }

            // out of breath — wash back to the last dry ground
            if (StaminaExhausted && Stamina <= 0.01f)
            {
                IsSwimming = false;
                var m = _team != null ? _team.Active : null;
                if (m != null) m.hp = Mathf.Max(1f, m.hp - m.maxHp * 0.15f);
                var cc = GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                transform.position = _lastDryPos + Vector3.up * 0.5f;
                if (cc != null) cc.enabled = true;
                _velocity = Vector3.zero;
                _planarVel = Vector3.zero;
                WuWaUtil.Fade(_anim, "Loco", 0.2f);
                HUDController.Toast("숨이 다했다… 물가로 밀려났다");
                HUDController.NotifyResources();
            }
        }

        // ------------------------------------------------------------------ items (S4)
        float _flaskCast = -1f;

        void TickItems()
        {
            if (Cutscene.Active || DialogueSystem.Active || GameDirector.MenuOpen) return;
            if (InputService.QuickItemPressed) Inventory.UseQuick();
            if (InputService.FlaskPressed && _flaskCast < 0f)
            {
                if (Inventory.FlaskCharges <= 0) HUDController.Toast("공명의 물약이 비었습니다 — 표석·공명탑에서 충전됩니다");
                else if (_team != null && _team.Active != null && _team.Active.hp >= _team.Active.maxHp - 0.5f) HUDController.Toast("HP가 가득 찼습니다");
                else { _flaskCast = Time.time; HUDController.Toast("공명의 물약 시전 중… (1.2초)"); AudioMan.I.Play2D(Sfx.Absorb(), 0.4f, 1.5f); }
            }
            if (_flaskCast >= 0f && Time.time - _flaskCast >= 1.2f)
            {
                _flaskCast = -1f;
                if (Inventory.ConsumeFlask() && _team != null)
                {
                    _team.HealSplit(0.35f, 0.10f);
                    VFXLibrary.SpawnNova(transform.position, new Color(0.55f, 0.95f, 1f), 3f);
                    AudioMan.I.Play2D(Sfx.Absorb(), 0.7f, 1.2f);
                    HUDController.Toast("공명의 물약 — HP 회복 (남은 충전 " + Inventory.FlaskCharges + "/" + Inventory.FlaskMax + ")");
                }
            }
        }

        // ------------------------------------------------------------------ stamina
        bool HasStamina(float amount) { return !StaminaExhausted && Stamina >= amount; }

        void UseStamina(float amount)
        {
            Stamina = Mathf.Max(0f, Stamina - amount * BuffSystem.StaminaDrainMul);
            _staminaDelay = 0.75f;
            if (Stamina <= 0.01f) StaminaExhausted = true;
        }

        void TickStamina(float dt)
        {
            if (Stamina < 0f) { Stamina = staminaMax; _lastDryPos = transform.position; }

            bool draining = IsSwimming;                  // SwimMove drains; no regen in water
            bool sprinting = !IsSwimming && SprintHeld && IsGrounded && !_dodging && !_wallRunning && PlanarSpeed > 3.6f;
            if (sprinting) { UseStamina(sprintDrain * dt); draining = true; }
            if (_gliding)
            {
                UseStamina(glideDrain * dt);
                draining = true;
                if (StaminaExhausted) _gliding = false;      // arms give out
            }
            if (_wallRunning)
            {
                UseStamina(wallRunDrain * dt);
                draining = true;
                if (StaminaExhausted) EndWallRun(0.4f);
            }

            if (!draining)
            {
                _staminaDelay -= dt;
                if (_staminaDelay <= 0f)
                    Stamina = Mathf.Min(staminaMax, Stamina + staminaRegen * BuffSystem.StaminaRegenMul * dt);
            }
            if (StaminaExhausted && Stamina >= staminaMax * 0.25f) StaminaExhausted = false;

            HUDController.SetStamina(Stamina / staminaMax, StaminaExhausted);
        }

        // ------------------------------------------------------------------ input
        void ReadMoveInput()
        {
            Vector2 mv = InputService.Move;
            // WuWa: tap the dodge key = dodge, keep holding = sprint (hold mode); toggle mode uses the sprint key
            if (InputService.DodgePressed) _shiftDownTime = Time.time;
            bool sprint;
            if (SprintMode == 1)
            {
                if (InputService.SprintPressed || InputService.DodgeHeld && Time.time - _shiftDownTime > sprintHoldDelay && !_sprintToggle) _sprintToggle = true;
                else if (InputService.SprintPressed) _sprintToggle = false;
                if (mv.sqrMagnitude < 0.05f || InCombat) _sprintToggle = false;
                sprint = _sprintToggle;
            }
            else if (SprintMode == 2) sprint = false;
            else sprint = (InputService.DodgeHeld && Time.time - _shiftDownTime > sprintHoldDelay) || InputService.SprintHeld;
            MoveInput = Vector2.ClampMagnitude(mv, 1f);
            SprintHeld = (sprint || _autoSprint) && !StaminaExhausted;
            UpdateAutoSprint();
        }

        public static int SprintMode = 0;            // 0 hold, 1 toggle, 2 auto only
        public static float AutoSprintDelay = 3.5f;  // < 0 = off
        public static int TimingAssist = 0;          // 0 off, 1 light, 2 strong (accessibility)
        public float sprintHoldDelay = 0.27f;
        bool _sprintToggle;

        bool _autoSprint;
        float _fullMoveTime;
        float _shiftDownTime = -10f;
        void UpdateAutoSprint()
        {
            if (InCombat) { _autoSprint = false; _fullMoveTime = 0f; return; }
            if (MoveInput.sqrMagnitude > 0.92f && IsGrounded && !_dodging)
            {
                _fullMoveTime += Time.deltaTime;
                if (AutoSprintDelay >= 0f && _fullMoveTime > AutoSprintDelay) _autoSprint = true;
            }
            else if (MoveInput.sqrMagnitude < 0.1f)
            {
                _fullMoveTime = 0f;
                _autoSprint = false;
            }
        }

        void UpdateCombatState(float dt)
        {
            _combatCheckTimer -= dt;
            if (_combatCheckTimer > 0f) return;
            _combatCheckTimer = 0.3f;
            bool combat = false;
            for (int i = 0; i < EnemyAI.All.Count; i++)
            {
                var e = EnemyAI.All[i];
                if (e == null || e.Hp == null || !e.Hp.IsAlive || !e.gameObject.activeInHierarchy) continue;
                if (!e.IsAggro) continue;
                if (WuWaUtil.Flat(e.transform.position - transform.position).sqrMagnitude < 15f * 15f)
                {
                    combat = true;
                    break;
                }
            }
            InCombat = combat;
        }

        bool JumpPressed() { return InputService.JumpPressed; }
        bool JumpHeld() { return InputService.JumpHeld; }
        bool DodgePressed() { return InputService.DodgePressed; }
        // grapple key, or the interact key when nothing is prompting (WuWa: F doubles as the hook)
        bool GrapplePressed() { return InputService.GrapplePressed || (InputService.InteractPressed && !HUDController.InteractPromptActive); }

        // ------------------------------------------------------------------ move
        Vector3 CamRelativeInput()
        {
            var cam = CamCache.Main;
            Vector3 fwd = cam != null ? WuWaUtil.Flat(cam.transform.forward).normalized : Vector3.forward;
            Vector3 right = cam != null ? WuWaUtil.Flat(cam.transform.right).normalized : Vector3.right;
            return fwd * MoveInput.y + right * MoveInput.x;
        }

        void HandleMove(float dt, bool busy)
        {
            Vector3 wish = CamRelativeInput();
            float target = SprintHeld && IsGrounded && wish.sqrMagnitude > 0.01f ? sprintSpeed : runSpeed;
            if (InCombat && IsGrounded)
                target = Mathf.Min(target, SprintHeld ? combatSprintSpeed : combatRunSpeed);
            if (_gliding) target = glideMoveSpeed;
            float control = IsGrounded ? 1f : airControl;
            float lockMul = 1f - Mathf.Clamp01(MovementLock);
            if (busy) target *= 0.0f;

            if (EchoSystem.I != null && _team != null) target *= EchoSystem.I.MoveSpeedMulFor(_team.ActiveIndex);
            Vector3 desired = wish * target * lockMul;
            _planarVel = Vector3.MoveTowards(_planarVel, desired, accel * control * dt);

            Vector3 face = busy && LockOn != null && LockOn.Target != null
                ? WuWaUtil.Flat(LockOn.Target.position - transform.position)
                : (wish.sqrMagnitude > 0.01f && lockMul > 0.1f ? wish : Vector3.zero);
            if (face.sqrMagnitude > 0.005f)
            {
                Quaternion targetRot = Quaternion.LookRotation(face.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-turnSharpness * dt));
            }
        }

        public void FaceInstant(Vector3 dir)
        {
            dir = WuWaUtil.Flat(dir);
            if (dir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        void HandleJumpGlide(bool busy)
        {
            if (busy) return;
            if (JumpPressed())
            {
                bool canGround = Time.time - _lastGroundedTime <= coyoteTime && _jumpsUsed == 0;
                if ((canGround || _jumpsUsed < 2) && (canGround || HasStamina(doubleJumpCost)))
                {
                    if (!canGround) UseStamina(doubleJumpCost);
                    _velocity.y = canGround ? jumpVel : doubleJumpVel;
                    _jumpsUsed = canGround ? 1 : 2;
                    _gliding = false;
                    AudioMan.I.Play(Sfx.Jump(), transform.position, 0.45f, _jumpsUsed == 2 ? 1.2f : 1f);
                    WuWaUtil.Fade(_anim, "Jump", 0.05f);
                    if (_jumpsUsed == 2) VFXLibrary.SpawnJumpPuff(transform.position, ActiveColor());
                }
            }

            if (!IsGrounded && _velocity.y < -1.5f && JumpHeld() && _jumpsUsed >= 1)
            {
                if (!_gliding && HasStamina(4f))
                {
                    _gliding = true;
                    WuWaUtil.Fade(_anim, "Glide", 0.25f);
                }
            }
            else if (_gliding && (IsGrounded || !JumpHeld()))
            {
                _gliding = false;
                if (!IsGrounded) WuWaUtil.Fade(_anim, "Fall", 0.2f);
            }
        }

        // ------------------------------------------------------------------ wall run
        void TryStartWallRun(bool busy)
        {
            if (busy || _gliding || _dodging || Time.time < _wallRunLockout) return;
            if (!HasStamina(24f)) return;
            if (_velocity.y > 9f) return;
            Vector3 wish = CamRelativeInput();
            if (wish.sqrMagnitude < 0.2f) return;
            // from the ground you must actually be running into the wall (WuWa auto-climb)
            if (IsGrounded && PlanarSpeed < 3.2f) return;

            RaycastHit hit;
            Vector3 origin = transform.position + Vector3.up * 1.1f;
            int mask = ~(Layers.PlayerMask | Layers.EnemyMask | (1 << Layers.Pickup));
            if (!Physics.SphereCast(origin, 0.3f, wish.normalized, out hit, 1.1f, mask, QueryTriggerInteraction.Ignore))
            {
                // already hugging the wall? probe from slightly behind
                if (!Physics.Raycast(origin - wish.normalized * 0.4f, wish.normalized, out hit, 1.5f, mask, QueryTriggerInteraction.Ignore))
                    return;
            }
            if (Vector3.Angle(hit.normal, Vector3.up) < 72f) return;              // needs a steep wall
            if (Vector3.Dot(wish.normalized, -WuWaUtil.Flat(hit.normal).normalized) < 0.45f) return;

            _wallRunning = true;
            _wallNormal = hit.normal;
            _wallRunT = 0f;
            _jumpsUsed = 1;
            _planarVel = Vector3.zero;
            _velocity.y = Mathf.Max(_velocity.y, wallRunClimbSpeed);
            FaceInstant(-_wallNormal);
            WuWaUtil.Fade(_anim, "WallRun", 0.08f);
            AudioMan.I.Play(Sfx.Dash(), transform.position, 0.35f, 1.3f);
        }

        void WallRunMove(float dt)
        {
            _wallRunT += dt;
            bool wantsOff = !JumpHeldOrMovingIn();
            RaycastHit hit;
            int mask = ~(Layers.PlayerMask | Layers.EnemyMask | (1 << Layers.Pickup));
            bool wallStill = Physics.Raycast(transform.position + Vector3.up * 1.1f, -_wallNormal, out hit, 1.3f, mask, QueryTriggerInteraction.Ignore)
                             && Vector3.Angle(hit.normal, Vector3.up) >= 65f;
            if (wallStill) _wallNormal = hit.normal;

            if (JumpPressed())
            {
                // wall kick
                EndWallRun(0.35f);
                _velocity.y = wallKickVel;
                _planarVel = _wallNormal * wallKickVel * 0.9f;
                _jumpsUsed = 1;
                WuWaUtil.Fade(_anim, "Jump", 0.05f);
                VFXLibrary.SpawnJumpPuff(transform.position, ActiveColor());
                AudioMan.I.Play(Sfx.Jump(), transform.position, 0.5f, 1.1f);
                return;
            }

            if (_wallRunT > wallRunMaxTime || !wallStill || (IsGrounded && _velocity.y <= 0.5f && _wallRunT > 0.4f) || wantsOff)
            {
                EndWallRun(0.3f);
                return;
            }

            float climb = Mathf.Lerp(wallRunClimbSpeed, 1.2f, _wallRunT / wallRunMaxTime);
            _velocity.y = climb;
            _planarVel = -_wallNormal * 1.6f;   // stick to the wall
            FaceInstant(-_wallNormal);
        }

        bool JumpHeldOrMovingIn()
        {
            Vector3 wish = CamRelativeInput();
            return wish.sqrMagnitude > 0.15f && Vector3.Dot(wish.normalized, -WuWaUtil.Flat(_wallNormal).normalized) > 0.15f;
        }

        void EndWallRun(float lockout)
        {
            _wallRunning = false;
            _wallRunLockout = Time.time + lockout;
            if (!IsGrounded) WuWaUtil.Fade(_anim, "Fall", 0.2f);
        }

        // ------------------------------------------------------------------ grapple
        void UpdateGrappleScan()
        {
            GrappleCandidate = (_grappling || _plunging) ? null : GrapplePoint.Best(transform.position, CamCache.Main);
        }

        void HandleGrappleInput(bool busy)
        {
            if (busy || _wallRunning || GrappleCandidate == null) return;
            if (!GrapplePressed()) return;
            if (_combat != null) _combat.CancelAttack();
            if (_grappleRoutine != null) StopCoroutine(_grappleRoutine);
            _grappleRoutine = StartCoroutine(GrappleRoutine(GrappleCandidate.transform.position));
        }

        IEnumerator GrappleRoutine(Vector3 target)
        {
            _grappling = true;
            _gliding = false;
            _dodging = false;
            _velocity = Vector3.zero;
            _planarVel = Vector3.zero;

            AudioMan.I.Play2D(Sfx.Swap(), 0.5f, 1.4f);
            WuWaUtil.Fade(_anim, "Jump", 0.08f);
            VFXLibrary.GrappleLine(transform, target, 0.9f, new Color(0.5f, 1f, 0.85f));
            ThirdPersonCamera.PunchFov(5f, 0.4f);

            Vector3 start = transform.position;
            float dist = Vector3.Distance(start, target);
            float dur = Mathf.Max(0.18f, dist / grappleSpeed);
            Vector3 mid = (start + target) * 0.5f + Vector3.up * Mathf.Min(3.5f, dist * 0.16f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                Vector3 a = Vector3.Lerp(start, mid, k);
                Vector3 b = Vector3.Lerp(mid, target, k);
                Vector3 pos = Vector3.Lerp(a, b, k);
                Vector3 delta = pos - transform.position;
                _cc.Move(delta);
                Vector3 faceDir = WuWaUtil.Flat(target - transform.position);
                if (faceDir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(faceDir.normalized), 0.3f);
                yield return null;
            }

            // release with momentum
            Vector3 exitDir = WuWaUtil.Flat(target - start).normalized;
            _velocity.y = 7.5f;
            _planarVel = exitDir * 6.5f;
            _jumpsUsed = 1;
            _grappling = false;
            WuWaUtil.Fade(_anim, "Jump", 0.1f);
            AudioMan.I.Play(Sfx.Jump(), transform.position, 0.4f, 0.9f);
        }

        public void CancelGrapple()
        {
            if (!_grappling) return;
            if (_grappleRoutine != null) StopCoroutine(_grappleRoutine);
            _grappling = false;
        }

        // ------------------------------------------------------------------ plunge (driven by combat)
        public void BeginPlunge()
        {
            _plunging = true;
            _gliding = false;
            _dodging = false;
            _wallRunning = false;
            _planarVel *= 0.15f;
        }

        public void PlungeFall(float dt)
        {
            _velocity.y = -30f;
        }

        public void EndPlunge()
        {
            _plunging = false;
        }

        // ------------------------------------------------------------------ dodge
        void HandleDodge(bool busy)
        {
            if (_dodging && Time.time >= _dodgeEndTime)
            {
                _dodging = false;
                WuWaUtil.Fade(_anim, IsGrounded ? "Loco" : "Fall", 0.12f);
            }
            if (Time.time < _nextDodgeTime || _dodging || _wallRunning) return;
            if (!DodgePressed()) return;
            if (!HasStamina(dodgeCost)) return;               // too winded to dodge
            UseStamina(dodgeCost);

            if (_combat != null) _combat.CancelAttack();

            Vector3 dir = CamRelativeInput();
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir = dir.normalized;

            _dodging = true;
            _gliding = false;
            _dodgeStartTime = Time.time;
            _dodgeEndTime = Time.time + dodgeDuration;
            _nextDodgeTime = _dodgeEndTime + dodgeCooldown;
            _iFrameStart = Time.time + iFrameWindow.x;
            _iFrameEnd = Time.time + (TimingAssist == 0 ? iFrameWindow.y : TimingAssist == 1 ? 0.30f : 0.34f);
            _dodgeDir = dir;
            FaceInstant(dir);
            _velocity.y = Mathf.Max(_velocity.y, -0.5f);

            AudioMan.I.Play(Sfx.Dash(), transform.position, 0.55f);
            HapticsService.Dash();
            WuWaUtil.Fade(_anim, "Dodge", 0.04f);
            VFXLibrary.SpawnDashGhost(this, ActiveColor());
        }

        Vector3 _dodgeDir;
        void DodgeMove(float dt)
        {
            float t = 1f - Mathf.Clamp01((_dodgeEndTime - Time.time) / dodgeDuration);
            float speed = Mathf.Lerp(dodgeSpeed, runSpeed * 0.8f, t * t);
            _planarVel = _dodgeDir * speed;
        }

        void ApplyGravity(float dt)
        {
            if (_wallRunning || _plunging) return;   // states own vertical velocity
            if (IsGrounded && _velocity.y < 0f) _velocity.y = -3f;
            else
            {
                _velocity.y += gravity * dt;
                if (_gliding) _velocity.y = Mathf.Max(_velocity.y, glideFallSpeed);
                _velocity.y = Mathf.Max(_velocity.y, terminalFall);
            }
        }

        bool CheckGrounded()
        {
            if (_cc.isGrounded) return true;
            Vector3 origin = transform.position + Vector3.up * 0.3f;
            return Physics.SphereCast(origin, _cc.radius * 0.9f, Vector3.down, out _, 0.42f,
                ~(Layers.PlayerMask | Layers.EnemyMask | (1 << Layers.Pickup)), QueryTriggerInteraction.Ignore);
        }

        public float HeightAboveGround()
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out hit, 80f,
                    ~(Layers.PlayerMask | Layers.EnemyMask | (1 << Layers.Pickup)), QueryTriggerInteraction.Ignore))
                return hit.distance - 0.2f;
            return 99f;
        }

        public void AddImpulse(Vector3 v) { _impulse += v; }
        public void ForceVertical(float v) { _velocity.y = v; }
        public void ConsumeCounter() { LastPerfectDodge = -999f; }

        /// WuWa-style attack magnetism: glide toward the current target while swinging.
        public void AttackTrack(Vector3 worldTarget, float dt)
        {
            Vector3 to = WuWaUtil.Flat(worldTarget - transform.position);
            float d = to.magnitude;
            if (d < 0.05f) return;
            Vector3 dir = to / d;
            _cc.Move(dir * Mathf.Min(15f * dt, d));
            FaceInstant(dir);
        }

        // ------------------------------------------------------------------ anim
        void DriveAnimator(float dt, bool busy)
        {
            if (_anim == null) return;
            float speedNorm = PlanarSpeed <= runSpeed
                ? Mathf.Clamp01(PlanarSpeed / runSpeed) * 0.72f
                : Mathf.Lerp(0.72f, 1f, Mathf.Clamp01((PlanarSpeed - runSpeed) / (sprintSpeed - runSpeed)));
            if (SpeedPoseOverride >= 0f) speedNorm = SpeedPoseOverride;
            _anim.SetFloat(SpeedHash, speedNorm, 0.08f, dt);
            // Loco stance: relaxed sword carry out of combat, one-handed guard while enemies are engaged
            if (_hasCombatParam)
            {
                float stance = CombatPoseOverride >= 0 ? CombatPoseOverride : (InCombat ? 1f : 0f);
                _anim.SetFloat(CombatHash, stance, 0.2f, dt);
            }

            if (!busy && !_dodging && !_wallRunning && !_grappling && !_plunging && !IsSwimming)
            {
                var info = _anim.GetCurrentAnimatorStateInfo(0);
                if (!IsGrounded && !_gliding && _velocity.y < -2f && !info.IsName("Fall") && !info.IsName("Jump") && !info.IsName("Glide"))
                    WuWaUtil.Fade(_anim, "Fall", 0.2f);
                // grounded watchdog: whatever airborne/one-shot state we landed in
                // (Jump, Fall, Glide, WallRun, or a finished clip holding its last
                // frame) must hand control back to locomotion immediately
                else if (IsGrounded && !_anim.IsInTransition(0) && !info.IsName("Loco") &&
                         (info.IsName("Jump") || info.IsName("Fall") || info.IsName("Glide") ||
                          info.IsName("WallRun") || info.normalizedTime > 1.05f))
                    WuWaUtil.Fade(_anim, "Loco", 0.1f);
            }
        }

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int CombatHash = Animator.StringToHash("Combat");
        bool _hasCombatParam;
        /// -1 follows InCombat; 0 or 1 pins the Loco stance (previews, capture harness).
        public static int CombatPoseOverride = -1;
        /// -1 follows the real velocity; 0..1 pins the Loco Speed blend (capture harness).
        public static float SpeedPoseOverride = -1f;

        Color ActiveColor()
        {
            var m = _team != null ? _team.Active : null;
            return m != null ? m.themeColor : Color.white;
        }

        // ------------------------------------------------------------------ damage
        public void TakeDamage(DamageInfo info)
        {
            if (_team == null || !_team.AnyAlive) return;

            if (Invulnerable)
            {
                // only a dodge started right before the hit is PERFECT; the rest is a plain i-frame
                if (Time.time - _dodgeStartTime < (TimingAssist == 0 ? 0.2f : TimingAssist == 1 ? 0.26f : 0.32f))
                {
                    // Perfect dodge: slow the world and open a counterattack window.
                    LastPerfectDodge = Time.time; ContentStats.PerfectDodges++;
                    var am = _team.Active;
                    if (am != null) am.GainConcerto(15f);
                    Hitstop.I.SlowMo(0.22f, 1.05f, 0.3f);
                    AudioMan.I.Play2D(Sfx.PerfectDodge(), 0.8f);
                    VFXLibrary.SpawnPerfectDodge(transform.position + Vector3.up, ActiveColor());
                    HUDController.Toast("완벽 회피! → 반격 기회");
                    HapticsService.PerfectDodge();
                    HUDController.NotifyResources();
                }
                return;
            }

            if (_grappling) CancelGrapple();

            var m = _team.Active;
            if (m == null) return;
            if (EchoSystem.I != null) info.amount *= EchoSystem.I.DamageTakenMulFor(_team.ActiveIndex);   // guard echoes
            info.amount *= BuffSystem.DamageTakenMul;                                                       // 강철껍질 조림
            if (_flaskCast >= 0f) { _flaskCast = -1f; HUDController.Toast("물약 시전이 끊겼습니다"); }
            m.hp = Mathf.Max(0f, m.hp - info.amount);
            CombatScore.NotifyHitTaken();
            _regenDelay = 5f;
            CameraShaker.Add(0.35f);
            HapticsService.Hurt(info.amount / Mathf.Max(1f, m.maxHp));
            AudioMan.I.Play(Sfx.Hurt(), transform.position, 0.7f);
            VFXLibrary.SpawnHitSpark(transform.position + Vector3.up * 1.1f, new Color(1f, 0.3f, 0.3f), 0.7f);
            if (info.knockback > 0.01f)
            {
                Vector3 dir = WuWaUtil.Flat(transform.position - info.sourcePos).normalized;
                AddImpulse(dir * info.knockback);
            }
            _team.NotifyHpChanged();

            if (m.hp <= 0f)
            {
                if (!_team.SwapToNextAlive())
                    GameDirector.I.PlayerDown();
            }
            else if (!(_combat != null && _combat.IsBusy) && !_wallRunning && !_plunging && !_dodging)
            {
                WuWaUtil.Fade(_anim, "Hit", 0.06f);
                StartCoroutine(BackToLoco(0.45f));
            }
        }

        IEnumerator BackToLoco(float t)
        {
            yield return new WaitForSeconds(t);
            if (!(_combat != null && _combat.IsBusy) && !_dodging && IsGrounded && !_wallRunning && !_plunging)
                WuWaUtil.Fade(_anim, "Loco", 0.15f);
        }

        void PassiveRegen(float dt)
        {
            if (_team == null) return;
            if (_regenDelay > 0f) { _regenDelay -= dt; return; }
            var m = _team.Active;
            if (m != null && m.hp > 0f && m.hp < m.maxHp)
            {
                m.hp = Mathf.Min(m.maxHp, m.hp + m.maxHp * 0.02f * dt);
                _team.NotifyHpChanged();
            }
        }
    }
}
