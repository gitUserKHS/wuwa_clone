using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WuWa
{
    /// Session flow: title → new game / continue, cursor management, respawn,
    /// boss victory, ambience, and "back to title" (scene reload).
    public class GameDirector : MonoBehaviour
    {
        static GameDirector _inst;
        public static GameDirector I
        {
            get
            {
                if (_inst == null) _inst = Object.FindAnyObjectByType<GameDirector>();
                return _inst;
            }
        }

        public Vector3 respawnPoint = new Vector3(0f, 2f, 0f);
        public static bool CursorFree { get; private set; }
        public static bool MenuOpen;     // set by ScreenRouter; suppresses game input
        public static bool InTitle { get; private set; }

        PlayerController _player;
        AudioSource _wind;
        bool _busy;
        bool _introPlayed;
        bool _bossIntroPlayed;
        bool _returning;
        float _bossCheck;

        void Awake()
        {
            _inst = this;
            SettingsStore.Load();
            ResonanceTower.ActiveCount = 0;      // static — reset per session
            EnemyAI.ResetStatics();
            CameraShaker.Trauma = 0f;
            MenuOpen = false;
            InTitle = false;
            ThirdPersonCamera.TitleOrbit = false;
            MusicDirector.ForceCombat = false;
            MapSystem.ClearDynamic();
            MapSystem.ClearTracked();
            Inventory.Reset();
            BuffSystem.Clear();
            Codex.Reset();
            RegionCompletion.RiftRegionMask = 0;
            ContentStats.Reset();
            GameFlags.Clear();
            // statics that outlive a scene reload (back to title)
            MapDiscovery.Reset();
            MapPins.Clear();
            MapMarkers.InvalidateCaches();
            RegionCompletion.InvalidateCaches();
            BountyBoard.Import(-1, -1, null, null, null, null, null, null);
            ShopStock.Import(-1, new int[0]);
            DayNightCycle.DayIndex = 0;
            GatherNode.ResetBoot();
        }

        void Start()
        {
            _player = Object.FindAnyObjectByType<PlayerController>();
            if (_player != null) respawnPoint = _player.transform.position;
            LockCursor(true);
            SettingsAppliers.ApplyAll();

            _wind = gameObject.AddComponent<AudioSource>();
            _wind.clip = Sfx.WindLoop();
            _wind.loop = true;
            _wind.volume = 0.32f;
            _wind.spatialBlend = 0f;
            _wind.Play();

            if (SaveSystem.I != null && ScreenRouter.Get("Title") != null) EnterTitle();
            else
            {
                // no UI root in the scene: fall straight into a fresh session (editor convenience)
                if (SaveSystem.I != null) SaveSystem.I.NewGame();
                StartCoroutine(IntroRoutine());
            }
        }

        void Update()
        {
            if (MenuOpen) return;        // a screen owns input while open
            if (InputService.CursorFreeHeld != CursorFree) LockCursor(!InputService.CursorFreeHeld);   // Alt held = free cursor
            if (InputService.RespawnPressed) RespawnNow();
            // ESC / Start → ScreenRouter (PauseMenu) — see UIRoot.

            // kill plane: falling off the field teleports the party back
            if (_player != null && _player.transform.position.y < -40f)
                RespawnNow();

            // boss entrance cinematic on first approach
            _bossCheck -= Time.deltaTime;
            if (!_bossIntroPlayed && _bossCheck <= 0f && _player != null && !Cutscene.Active)
            {
                _bossCheck = 0.5f;
                for (int i = 0; i < EnemyAI.All.Count; i++)
                {
                    var e = EnemyAI.All[i];
                    if (e == null || !e.isBoss || e.Hp == null || !e.Hp.IsAlive || !e.gameObject.activeInHierarchy) continue;
                    if (WuWaUtil.Flat(e.transform.position - _player.transform.position).magnitude < 24f)
                    {
                        _bossIntroPlayed = true;
                        if (Cutscene.I != null) Cutscene.I.PlayBossIntro(e.transform);
                        break;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- title flow
        public void EnterTitle()
        {
            InTitle = true;
            ThirdPersonCamera.TitleOrbit = true;
            ScreenRouter.Push("Title");
        }

        void LeaveTitle()
        {
            InTitle = false;
            ThirdPersonCamera.TitleOrbit = false;
            ScreenRouter.CloseAll();
        }

        /// Title → 새로 시작: fresh session on the freshly loaded scene, then the chapter intro.
        public void BeginNewGame()
        {
            if (!InTitle || SaveSystem.I == null) return;
            LeaveTitle();
            SaveSystem.I.NewGame();
            StartCoroutine(IntroRoutine());
        }

        /// Title → 이어하기 / slot pick: load, then fade in without the intro.
        public void BeginContinue(int slot)
        {
            if (!InTitle || SaveSystem.I == null) return;
            if (!SaveSystem.I.LoadSlot(slot)) { HUDController.Toast("불러오기 실패 — " + SaveSystem.SlotName(slot)); return; }
            LeaveTitle();
            _introPlayed = true;
            StartCoroutine(FadeInRoutine());
        }

        IEnumerator IntroRoutine()
        {
            yield return new WaitForSecondsRealtime(0.3f);
            if (!_introPlayed && Cutscene.I != null)
            {
                _introPlayed = true;
                Cutscene.I.PlayIntro();
            }
        }

        IEnumerator FadeInRoutine()
        {
            HUDController.FadeScreen(1f, 0.01f);
            yield return new WaitForSecondsRealtime(0.08f);
            HUDController.FadeScreen(0f, 0.9f);
        }

        /// Pause → 타이틀로: optional quick save, fade, reload the field scene (title shows again).
        public void ReturnToTitle(bool save)
        {
            if (_returning) return;
            _returning = true;
            StartCoroutine(ReturnRoutine(save));
        }

        IEnumerator ReturnRoutine(bool save)
        {
            if (save && SaveSystem.I != null) SaveSystem.I.AutoSave("타이틀로 이동");
            ScreenRouter.CloseAll();
            HUDController.FadeScreen(1f, 0.45f);
            yield return new WaitForSecondsRealtime(0.6f);
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void LockCursor(bool locked)
        {
            CursorFree = !locked;
            CursorService.Apply(locked ? CursorService.Mode.Gameplay : CursorService.Mode.Free);
        }

        public void PlayerDown()
        {
            if (_busy) return;
            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            _busy = true;
            HUDController.Toast("팀 전멸... 재기동 중");
            HUDController.FadeScreen(1f, 0.9f);
            yield return new WaitForSeconds(1.6f);
            RespawnNow();
            HUDController.FadeScreen(0f, 0.8f);
            _busy = false;
        }

        void RespawnNow()
        {
            if (_player == null) return;
            Inventory.RefillFlask("리스폰");
            var cc = _player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _player.transform.position = respawnPoint + Vector3.up * 1.5f;
            if (cc != null) cc.enabled = true;
            var team = _player.GetComponent<TeamManager>();
            if (team != null) team.ReviveAll();
        }

        public void BossDefeated()
        {
            HUDController.Victory();
            Hitstop.I.SlowMo(0.25f, 1.6f, 0.6f);
            if (MusicDirector.I != null) MusicDirector.I.PlayVictory();
        }
    }
}
