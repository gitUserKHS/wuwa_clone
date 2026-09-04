using UnityEngine;

namespace WuWa
{
    /// Boots the UI framework: layer canvases, screen registry, and the per-frame
    /// services (router hotkeys, focus, tooltip, feed, rebind, interaction).
    [DefaultExecutionOrder(-100)]
    public class UIRoot : MonoBehaviour
    {
        public static UIRoot I { get; private set; }
        Canvas _screens, _popup, _modal, _system;

        void Awake()
        {
            I = this;
            UIKit.EnsureEventSystem();
            _screens = UIKit.MakeCanvas("UI.Screens", transform, 95, true);
            _popup = UIKit.MakeCanvas("UI.Popup", transform, 100, true);
            _modal = UIKit.MakeCanvas("UI.Modal", transform, 105, true);
            _system = UIKit.MakeCanvas("UI.System", transform, 110, false);
            ScreenRouter.ScreenLayer = _screens.transform;
            ScreenRouter.PopupLayer = _popup.transform;
            ScreenRouter.ModalLayer = _modal.transform;
            ScreenRouter.SystemLayer = _system.transform;
            FocusNavigator.Init(_popup.transform);
            Tooltip.Init(_popup.transform);
            NotificationFeed.Init(_system.transform);
            Tutorial.Init(_system.transform);

            ScreenRouter.Register(gameObject.AddComponent<PauseMenu>());
            ScreenRouter.Register(gameObject.AddComponent<SettingsScreen>());
            ScreenRouter.Register(gameObject.AddComponent<ConfirmScreen>());
            ScreenRouter.Register(gameObject.AddComponent<CharacterScreen>());
            ScreenRouter.Register(gameObject.AddComponent<InventoryScreen>());
            ScreenRouter.Register(gameObject.AddComponent<ShopScreen>());
            ScreenRouter.Register(gameObject.AddComponent<QuestLogScreen>());
            ScreenRouter.Register(gameObject.AddComponent<CodexScreen>());
            ScreenRouter.Register(gameObject.AddComponent<TutorialScreen>());
            ScreenRouter.Register(gameObject.AddComponent<MapScreen>());
            ScreenRouter.Register(gameObject.AddComponent<TitleScreen>());
            ScreenRouter.Register(gameObject.AddComponent<SlotListScreen>());
            ScreenRouter.Register(gameObject.AddComponent<ResultsScreen>());
            ScreenRouter.Register(gameObject.AddComponent<TrialScreen>());
        }

        void Start() { GatherNode.Bootstrap(); }

        void OnDestroy()
        {
            if (I == this) I = null;
            ScreenRouter.CloseAll();
        }

        void Update()
        {
            ScreenRouter.Tick();
            RebindUI.Tick();
            FocusNavigator.Tick();
            Tooltip.Tick();
            NotificationFeed.Tick();
            InteractionManager.Tick();
            SettingsStore.Tick();
            BuffSystem.Tick();
            ShopStock.Tick();
            if (SaveSystem.SessionStarted) { Tutorial.Tick(); BountyBoard.Tick(); RegionCompletion.Tick(); }
        }
    }
}
