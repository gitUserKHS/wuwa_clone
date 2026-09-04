using UnityEngine;

namespace WuWa
{
    /// Region banner + quest hook when the player crosses into an area.
    public class RegionTrigger : MonoBehaviour
    {
        public int regionId = 1;
        public string regionName = "속삭임 숲";
        public float checkRadius = 20f;

        Transform _player;
        bool _fired;
        bool _inside;

        void Start()
        {
            var p = Object.FindAnyObjectByType<PlayerController>();
            if (p != null) _player = p.transform;
        }

        void Update()
        {
            if (_player == null) return;
            bool now = WuWaUtil.Flat(_player.position - transform.position).magnitude < checkRadius;
            if (now && !_inside)
            {
                if (!_fired)
                {
                    _fired = true;
                    HUDController.Toast("—  " + regionName + "  —");
                    AudioMan.I.Play2D(Sfx.Swap(), 0.5f, 0.7f);
                }
                // the quest chain may revisit this region later — notify on every entry
                if (QuestSystem.I != null) QuestSystem.I.Notify(QuestEvent.Reach, regionId);
            }
            _inside = now;
        }
    }
}
