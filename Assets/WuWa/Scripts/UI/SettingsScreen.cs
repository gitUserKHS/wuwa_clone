using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Hub tab "설정": hub header + the registry-driven options body + rebinding rows.
    public class SettingsScreen : UIScreen
    {
        public override string Id { get { return "Settings"; } }
        public override string Title { get { return "설정"; } }
        public override bool IsHubTab { get { return true; } }
        ScreenRouter.HubHeader _header;

        protected override void Build()
        {
            _header = ScreenRouter.BuildHubHeader(Root, "설정", Id);
            OptionsPanel.ExtraBuilder = RebindUI.BuildRows;
            OptionsPanel.Build(Root, UIKit.Font, UIKit.White, UIKit.Dot, () => ScreenRouter.Back());
        }

        public override void OnOpen(object args)
        {
            ScreenRouter.RefreshHubHeader(_header);
            OptionsPanel.Refresh();
        }

        public override Selectable DefaultFocus { get { return OptionsPanel.FirstTab; } }

        public override void OnTab(int dir) { OptionsPanel.CycleTab(dir); }
        public override bool OnBack() { return RebindUI.CancelListening(); }
    }
}
