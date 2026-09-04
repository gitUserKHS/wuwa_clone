using System;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Every player-facing setting, registered once; the options screen renders
    /// this list and SettingsAppliers pushes values into the game. Design doc ch.8.
    public static class SettingsCatalog
    {
        public const string TabGraphics = "그래픽", TabAudio = "오디오", TabControls = "조작", TabGameplay = "게임플레이", TabAccess = "접근성", TabSave = "저장";
        public static readonly string[] Tabs = { TabGraphics, TabAudio, TabControls, TabGameplay, TabAccess, TabSave };
        public static readonly Dictionary<string, object> Defaults = new Dictionary<string, object>();
        static string[] _resolutionLabels;
        static Vector2Int[] _resolutions;

        static OptionsData D { get { return SettingsStore.D; } }

        static void Slider(string key, string tab, string label, float min, float max, float step, Func<float> get, Action<float> set, string tip = null)
        {
            SettingsStore.Defs.Add(new SettingDef { key = key, tab = tab, label = label, kind = SettingKind.Slider, min = min, max = max, step = step,
                get = () => get(), set = v => set(Convert.ToSingle(v)), tooltip = tip });
        }
        static void Toggle(string key, string tab, string label, Func<bool> get, Action<bool> set, string tip = null)
        {
            SettingsStore.Defs.Add(new SettingDef { key = key, tab = tab, label = label, kind = SettingKind.Toggle,
                get = () => get(), set = v => set(Convert.ToBoolean(v)), tooltip = tip });
        }
        static void Cycle(string key, string tab, string label, string[] options, Func<int> get, Action<int> set, string tip = null)
        {
            SettingsStore.Defs.Add(new SettingDef { key = key, tab = tab, label = label, kind = SettingKind.Cycle, options = options,
                get = () => get(), set = v => set(Mathf.Clamp(Convert.ToInt32(v), 0, options.Length - 1)), tooltip = tip });
        }
        static void Button(string key, string tab, string label, Action onClick, bool dangerous = false, string tip = null)
        {
            SettingsStore.Defs.Add(new SettingDef { key = key, tab = tab, label = label, kind = SettingKind.Button, onClick = onClick, dangerous = dangerous, tooltip = tip });
        }

        static void Custom() { D.quality = 3; }

        public static string[] ResolutionLabels { get { if (_resolutionLabels == null) BuildResolutions(); return _resolutionLabels; } }
        public static Vector2Int ResolutionAt(int index)
        {
            if (_resolutions == null) BuildResolutions();
            return index <= 0 || index >= _resolutions.Length ? Vector2Int.zero : _resolutions[index];
        }
        public static int ResolutionIndex(int w, int h)
        {
            if (_resolutions == null) BuildResolutions();
            for (int i = 1; i < _resolutions.Length; i++) if (_resolutions[i].x == w && _resolutions[i].y == h) return i;
            return 0;
        }
        static void BuildResolutions()
        {
            var list = new List<Vector2Int> { Vector2Int.zero };
            var labels = new List<string> { "데스크톱" };
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.x < 1024 || list.Contains(v)) continue;
                list.Add(v); labels.Add(v.x + " × " + v.y);
            }
            _resolutions = list.ToArray();
            _resolutionLabels = labels.ToArray();
        }

        public static void Register()
        {
            SettingsStore.Defs.Clear();
            // ---------------------------------------------------------------- graphics
            Cycle("gfx.displayMode", TabGraphics, "창 모드", new[] { "전체화면", "테두리 없는 창", "창" }, () => D.displayMode, v => D.displayMode = v);
            Cycle("gfx.resolution", TabGraphics, "해상도", ResolutionLabels, () => ResolutionIndex(D.resW, D.resH), v => { var r = ResolutionAt(v); D.resW = r.x; D.resH = r.y; });
            Toggle("gfx.vsync", TabGraphics, "수직 동기화", () => D.vsync, v => D.vsync = v, "켜면 프레임 상한은 무시됩니다");
            Cycle("gfx.frameCap", TabGraphics, "프레임 상한", new[] { "30", "60", "120", "144", "무제한" }, () => FrameCapIndex(D.frameCap), v => D.frameCap = FrameCapValue(v));
            Cycle("gfx.quality", TabGraphics, "품질 프리셋", new[] { "낮음", "보통", "높음", "사용자" }, () => D.quality, v => SettingsAppliers.ApplyPreset(v));
            Slider("gfx.renderScale", TabGraphics, "렌더 스케일", 0.5f, 1.5f, 0.05f, () => D.renderScale, v => { D.renderScale = v; Custom(); }, "URP 렌더 해상도 배율");
            Cycle("gfx.shadows", TabGraphics, "그림자 품질", new[] { "끔", "낮음", "보통", "높음" }, () => D.shadowQuality, v => { D.shadowQuality = v; Custom(); });
            Cycle("gfx.aa", TabGraphics, "안티에일리어싱", new[] { "없음", "FXAA", "SMAA" }, () => D.aa, v => { D.aa = v; Custom(); });
            Toggle("gfx.ssao", TabGraphics, "앰비언트 오클루전(SSAO)", () => D.ssao, v => { D.ssao = v; Custom(); });
            Toggle("gfx.bloom", TabGraphics, "블룸", () => D.bloom, v => D.bloom = v);
            Toggle("gfx.vignette", TabGraphics, "비네트", () => D.vignette, v => D.vignette = v);
            Toggle("gfx.lensFlare", TabGraphics, "렌즈 플레어", () => D.lensFlare, v => D.lensFlare = v);
            Cycle("gfx.grassDensity", TabGraphics, "풀 밀도", new[] { "끔", "낮음", "보통", "높음" }, () => D.grassDensity, v => { D.grassDensity = v; Custom(); });
            Cycle("gfx.grassDistance", TabGraphics, "풀 가시거리", new[] { "40m", "66m", "90m" }, () => D.grassDistance, v => { D.grassDistance = v; Custom(); });
            Cycle("gfx.decoDistance", TabGraphics, "장식 가시거리", new[] { "가까움", "보통", "멂" }, () => D.decoDistance, v => { D.decoDistance = v; Custom(); });
            Slider("gfx.brightness", TabGraphics, "밝기 (EV)", -1f, 1f, 0.1f, () => D.brightness, v => D.brightness = v);
            Slider("gfx.fov", TabGraphics, "시야각", 50f, 70f, 1f, () => D.fov, v => D.fov = v);
            Toggle("gfx.showFps", TabGraphics, "FPS 표시", () => D.showFps, v => D.showFps = v);
            // ---------------------------------------------------------------- audio
            Slider("audio.master", TabAudio, "마스터 볼륨", 0f, 1f, 0.05f, () => D.masterVol, v => D.masterVol = v);
            Slider("audio.bgm", TabAudio, "배경 음악", 0f, 1f, 0.05f, () => D.bgmVol, v => D.bgmVol = v);
            Slider("audio.sfx", TabAudio, "효과음", 0f, 1f, 0.05f, () => D.sfxVol, v => D.sfxVol = v);
            Toggle("audio.muteBg", TabAudio, "포커스 잃으면 음소거", () => D.muteInBackground, v => D.muteInBackground = v);
            // ---------------------------------------------------------------- controls
            Slider("ctl.mouseX", TabControls, "마우스 감도 (좌우)", 1f, 100f, 1f, () => D.mouseSensX, v => D.mouseSensX = v);
            Slider("ctl.mouseY", TabControls, "마우스 감도 (상하)", 1f, 100f, 1f, () => D.mouseSensY, v => D.mouseSensY = v);
            Slider("ctl.padX", TabControls, "패드 감도 (좌우)", 1f, 100f, 1f, () => D.padSensX, v => D.padSensX = v);
            Slider("ctl.padY", TabControls, "패드 감도 (상하)", 1f, 100f, 1f, () => D.padSensY, v => D.padSensY = v);
            Toggle("ctl.padAccel", TabControls, "패드 카메라 가속", () => D.padAccel, v => D.padAccel = v, "스틱을 끝까지 0.3초 이상 기울이면 1.5배");
            Toggle("ctl.invertX", TabControls, "좌우 반전", () => D.invertX, v => D.invertX = v);
            Toggle("ctl.invertY", TabControls, "상하 반전", () => D.invertY, v => D.invertY = v);
            Slider("ctl.deadzoneL", TabControls, "왼쪽 스틱 데드존", 0.05f, 0.35f, 0.01f, () => D.deadzoneL, v => D.deadzoneL = v);
            Slider("ctl.deadzoneR", TabControls, "오른쪽 스틱 데드존", 0.05f, 0.35f, 0.01f, () => D.deadzoneR, v => D.deadzoneR = v);
            Cycle("ctl.curve", TabControls, "스틱 응답 곡선", new[] { "선형", "부드럽게", "공격적" }, () => D.stickCurve, v => D.stickCurve = v);
            Slider("ctl.trigger", TabControls, "트리거 임계값", 0.1f, 0.9f, 0.05f, () => D.triggerThreshold, v => D.triggerThreshold = v);
            Slider("ctl.vibration", TabControls, "진동 강도", 0f, 1f, 0.05f, () => D.vibration, v => D.vibration = v);
            Toggle("ctl.vibCombat", TabControls, "진동 · 전투", () => D.vibCombat, v => D.vibCombat = v);
            Toggle("ctl.vibMove", TabControls, "진동 · 이동", () => D.vibMove, v => D.vibMove = v);
            Toggle("ctl.vibFx", TabControls, "진동 · 연출", () => D.vibFx, v => D.vibFx = v);
            Cycle("ctl.sprintMode", TabControls, "질주 방식", new[] { "홀드", "토글", "자동 질주만" }, () => D.sprintMode, v => D.sprintMode = v, "홀드: 회피 키를 누르고 있으면 질주");
            Cycle("ctl.autoSprint", TabControls, "자동 질주 지연", new[] { "꺼짐", "2초", "3.5초", "5초" }, () => D.autoSprintDelay, v => D.autoSprintDelay = v);
            Toggle("ctl.dodgeRmb", TabControls, "우클릭 회피", () => D.dodgeRmb, v => D.dodgeRmb = v);
            Toggle("ctl.lockCamTrack", TabControls, "락온 카메라 추적", () => D.lockCamTrack, v => D.lockCamTrack = v, "끄면 방향·공격만 락, 카메라는 자유");
            Cycle("ctl.lockAssist", TabControls, "락온 카메라 보정 강도", new[] { "약", "보통", "강" }, () => D.lockAssist, v => D.lockAssist = v);
            Toggle("ctl.moveCamCorrect", TabControls, "이동 시 카메라 자동 정렬", () => D.moveCamCorrect, v => D.moveCamCorrect = v);
            Slider("ctl.camDistance", TabControls, "카메라 거리", 2f, 7.5f, 0.1f, () => D.camDistance, v => D.camDistance = v);
            Slider("ctl.camCombatDistance", TabControls, "전투 카메라 거리", 2.5f, 8f, 0.1f, () => D.camCombatDistance, v => D.camCombatDistance = v);
            Cycle("ctl.glyph", TabControls, "버튼 표시 스타일", new[] { "자동", "키보드", "Xbox", "PlayStation" }, () => D.glyphStyle, v => D.glyphStyle = v);
            Button("ctl.resetBindings", TabControls, "조작 초기화 (키 · 패드 오버라이드)", () => { InputService.ResetOverrides(); HUDController.Toast("조작이 기본값으로 돌아갔습니다"); });
            // ---------------------------------------------------------------- gameplay
            Slider("play.shake", TabGameplay, "화면 흔들림", 0f, 2f, 0.1f, () => D.shakeMul, v => D.shakeMul = v);
            Slider("play.hitstop", TabGameplay, "타격 정지(히트스톱)", 0f, 2f, 0.1f, () => D.hitstopMul, v => D.hitstopMul = v);
            Slider("play.slowmo", TabGameplay, "슬로모 연출", 0f, 1f, 0.1f, () => D.slowMoMul, v => D.slowMoMul = v);
            Toggle("map.roadRoute", TabGameplay, "지도 경로 도로 추종", () => D.roadRoute, v => { D.roadRoute = v; RoadRouter.Invalidate(); }, "끄면 목표까지 직선 점선으로 표시합니다");
            Slider("play.flash", TabGameplay, "화면 섬광", 0f, 1f, 0.1f, () => D.flashMul, v => D.flashMul = v);
            Cycle("play.dmgNumbers", TabGameplay, "대미지 숫자", new[] { "끔", "크리티컬만", "상세" }, () => D.dmgNumbers, v => D.dmgNumbers = v);
            Slider("play.dmgScale", TabGameplay, "대미지 숫자 크기", 0.8f, 1.5f, 0.1f, () => D.dmgScale, v => D.dmgScale = v);
            Toggle("play.minimap", TabGameplay, "미니맵 표시", () => D.minimap, v => D.minimap = v);
            Cycle("play.minimapRadius", TabGameplay, "미니맵 범위", new[] { "70m", "120m", "200m" }, () => D.minimapRadius, v => D.minimapRadius = v);
            Cycle("play.minimapMode", TabGameplay, "미니맵 방향", new[] { "북쪽 고정", "카메라 회전" }, () => D.minimapMode, v => D.minimapMode = v, "카메라 회전: 보는 방향이 항상 위");
            Toggle("play.mapRevealAll", TabGameplay, "지도 안개 해제 (데모)", () => D.mapRevealAll, v => D.mapRevealAll = v, "탐색하지 않은 지역도 전부 표시합니다");
            Toggle("play.questTracker", TabGameplay, "퀘스트 트래커", () => D.questTracker, v => D.questTracker = v);
            Toggle("play.tutorials", TabGameplay, "조작 힌트 · 튜토리얼", () => D.tutorials, v => D.tutorials = v);
            Cycle("play.dialogueSpeed", TabGameplay, "대화 속도", new[] { "느림", "보통", "빠름", "즉시" }, () => D.dialogueSpeed, v => D.dialogueSpeed = v);
            Toggle("play.dialogueAuto", TabGameplay, "대화 자동 진행", () => D.dialogueAuto, v => D.dialogueAuto = v);
            Cycle("play.autosaveNotice", TabGameplay, "자동 저장 알림", new[] { "아이콘만", "아이콘 + 토스트", "끔" }, () => D.autosaveNotice, v => D.autosaveNotice = v);
            Slider("play.hudScale", TabGameplay, "HUD 크기", 0.8f, 1.2f, 0.05f, () => D.hudScale, v => D.hudScale = v);
            // ---------------------------------------------------------------- accessibility
            Cycle("acc.colorblind", TabAccess, "색각 보정 (예고 색상)", new[] { "없음", "적록", "청황", "고대비" }, () => D.colorblind, v => D.colorblind = v);
            Toggle("acc.reduceFlash", TabAccess, "섬광 줄이기", () => D.reduceFlash, v => D.reduceFlash = v);
            Toggle("acc.holdSprint", TabAccess, "홀드 → 토글: 질주", () => D.holdToggleSprint, v => { D.holdToggleSprint = v; if (v) D.sprintMode = 1; });
            Cycle("acc.timing", TabAccess, "타이밍 보조", new[] { "꺼짐", "약", "강" }, () => D.timingAssist, v => D.timingAssist = v, "완벽 회피·무적·패리 예고·선입력 창을 넓힙니다");
            Slider("acc.textScale", TabAccess, "대화 · 자막 크기", 0.9f, 1.3f, 0.05f, () => D.textScale, v => D.textScale = v);
            // ---------------------------------------------------------------- save
            Button("save.now", TabSave, "지금 저장 (F9)", () => { if (SaveSystem.I != null) SaveSystem.I.AutoSave("수동 저장"); });
            Cycle("save.interval", TabSave, "자동 저장 주기", new[] { "3분", "5분", "10분" }, () => IntervalIndex(SaveSystem.AutosaveInterval), v => SaveSystem.AutosaveInterval = new[] { 180f, 300f, 600f }[v]);
            Button("save.resetSettings", TabSave, "설정 초기화 (전체)", () => { foreach (var t in Tabs) SettingsStore.ResetTab(t); HUDController.Toast("설정이 초기화되었습니다"); });
            Button("save.delete", TabSave, "저장 데이터 삭제 (2초 홀드)", () => { if (SaveSystem.I != null) SaveSystem.I.DeleteSave(); }, true);
            Button("save.openFolder", TabSave, "저장 폴더 열기", () => Application.OpenURL("file:///" + Application.persistentDataPath));

            // defaults snapshot
            var live = SettingsStore.D;
            SettingsStore.D = new OptionsData();
            Defaults.Clear();
            foreach (var d in SettingsStore.Defs) if (d.get != null) Defaults[d.key] = d.get();
            SettingsStore.D = live;
        }

        static int FrameCapIndex(int cap) { return cap == 30 ? 0 : cap == 60 ? 1 : cap == 120 ? 2 : cap == 144 ? 3 : 4; }
        static int FrameCapValue(int i) { return new[] { 30, 60, 120, 144, -1 }[Mathf.Clamp(i, 0, 4)]; }
        static int IntervalIndex(float s) { return s <= 180f ? 0 : s <= 300f ? 1 : 2; }
    }
}
