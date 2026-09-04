using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WuWa
{
    public enum NpcRole { Villager, Merchant, Keeper }

    /// Talkable character. Villagers gossip, the merchant opens the shop, the
    /// trial keeper explains the arena. Idle-turns to face the player nearby.
    public class NPC : MonoBehaviour, IInteractable
    {
        public static readonly List<NPC> All = new List<NPC>();

        public string npcName = "마을 사람";
        public NpcRole role = NpcRole.Villager;
        public int npcId;
        public float interactRange = 3.4f;

        Transform _player;
        Quaternion _restRot;
        Transform _marker;
        float _pulse;

        void OnEnable() { All.Add(this); InteractionManager.Register(this); }
        void OnDisable() { All.Remove(this); InteractionManager.Unregister(this); }

        void Start()
        {
            _restRot = transform.rotation;
            _marker = transform.Find("marker");
            _pulse = Random.value * 6f;
        }

        void Update()
        {
            if (_player == null)
            {
                var p = PlayerController.Instance;
                if (p == null) return;
                _player = p.transform;
            }
            if (_marker != null)
            {
                _pulse += Time.deltaTime * 2f;
                _marker.localPosition = new Vector3(0f, 2.35f + Mathf.Sin(_pulse) * 0.08f, 0f);
                _marker.Rotate(0f, 60f * Time.deltaTime, 0f, Space.World);
            }

            Vector3 to = WuWaUtil.Flat(_player.position - transform.position);
            float d = to.magnitude;
            // turn toward a nearby player, settle back afterwards
            Quaternion want = d < 6f && d > 0.05f ? Quaternion.LookRotation(to.normalized) : _restRot;
            transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-4f * Time.deltaTime));

        }

        // IInteractable (prompt + key are routed by InteractionManager)
        public Vector3 InteractPosition { get { return transform.position; } }
        public float InteractRange { get { return interactRange; } }
        public int InteractPriority { get { return 3; } }
        public string InteractLabel { get { return npcName + "와 대화"; } }
        public bool CanInteract { get { return !DialogueSystem.Active; } }
        public void Interact() { Talk(); }

        void Talk()
        {
            string[] lines = LinesFor();
            DialogueSystem.Show(npcName, lines, () =>
            {
                if (QuestSystem.I != null) QuestSystem.I.Notify(QuestEvent.Talk, npcId);
                GameFlags.Set("talked_" + npcId);
                if (role == NpcRole.Merchant) ScreenRouter.Push("Shop");
            });
        }

        string[] LinesFor()
        {
            bool first = !GameFlags.Has("talked_" + npcId);
            int rifts = ContentStats.RiftsClosed;
            int clears = ContentStats.ArenaClears;
            switch (role)
            {
                case NpcRole.Merchant:
                    return first
                        ? new[]
                        {
                            "오, 조율사님이시군요. 노래가 돌아온 뒤로 장사가 조금은 됩니다.",
                            "요즘 들판에 보랏빛 균열이 열린다는 소문, 들으셨나요? 그림자가 쏟아져 나온답니다.",
                            "조각소리만 있으면 무기든 에코든 구해 드리지요. 자, 구경하고 가세요.",
                        }
                        : rifts > 0
                            ? new[] { "균열을 " + rifts + "번이나 닫으셨다고요? 마을이 한결 조용해졌습니다.", "오늘도 좋은 물건 있습니다. 천천히 보세요." }
                            : new[] { "어서 오세요. 조각소리는 넉넉하신가요?" };
                case NpcRole.Keeper:
                    return first
                        ? new[]
                        {
                            "이 제단은 첫 노래가 끊기던 날 세워졌지. 그림자들이 파도처럼 몰려오는 곳이다.",
                            "가운데 결정에 손을 대면 시련이 시작된다. 다섯 파도를 버티면 제단이 보답할 것이야.",
                            "제단 밖으로 도망치면 시련은 무효다. 각오가 섰을 때 오게.",
                        }
                        : clears > 0
                            ? new[] { "벌써 " + clears + "번 완주했군. 파도는 매번 조금씩 거세진다 — 방심 말게." }
                            : new[] { "결정에 손을 대면 파도가 시작된다. 준비됐나?" };
                default:
                    {
                        var pool = new List<string>();
                        if (DayNightCycle.IsNight) pool.Add("밤에는 들판에 반딧불이 가득해요. 그런데 균열도 밤에 더 자주 열린대요…");
                        else pool.Add("날씨가 참 좋네요. 호수 쪽 다리 건너면 보물상자가 있다던데.");
                        pool.Add("현상 게시판은 퀘스트 창의 사이드 탭에서 볼 수 있어요. 매일 새 현상이 걸린답니다.");
                        if (rifts == 0) pool.Add("보랏빛 빛기둥을 보면 가까이 가지 마세요. 그림자가 나온답니다.");
                        else pool.Add("균열을 닫아 주셔서 고마워요. 요즘은 밤에도 잠이 옵니다.");
                        pool.Add("공명탑을 해방하면 세계의 노래가 한 겹씩 돌아온대요.");
                        return pool.ToArray();
                    }
            }
        }
    }
}
