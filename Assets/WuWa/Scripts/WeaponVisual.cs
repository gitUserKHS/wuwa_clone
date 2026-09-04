using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Builds a stylized sword mesh per tier and keeps it attached to each
    /// member's right hand. Rebuilds when the loadout or team changes.
    public class WeaponVisual : MonoBehaviour
    {
        TeamManager _team;
        readonly Dictionary<int, GameObject> _attached = new Dictionary<int, GameObject>();
        static WeaponVisual _inst;
        bool _hidden;

        /// Sheathes the visuals (WuWa hides weapons in the water).
        public static void SetHidden(bool hidden)
        {
            if (_inst == null) return;
            _inst._hidden = hidden;
            foreach (var kv in _inst._attached) if (kv.Value != null) kv.Value.SetActive(!hidden);
        }

        void Start()
        {
            _inst = this;
            _team = GetComponent<TeamManager>();
            if (_team != null) _team.OnTeamChanged += Rebuild;
            if (WeaponSystem.I != null) WeaponSystem.I.OnChanged += Rebuild;
            Rebuild();
        }

        void OnDestroy()
        {
            if (_inst == this) _inst = null;
            if (_team != null) _team.OnTeamChanged -= Rebuild;
            if (WeaponSystem.I != null) WeaponSystem.I.OnChanged -= Rebuild;
        }

        void Rebuild()
        {
            if (_team == null) return;
            for (int i = 0; i < _team.members.Length && i < 3; i++)
            {
                var m = _team.members[i];
                if (m == null) continue;
                GameObject cur;
                _attached.TryGetValue(i, out cur);
                if (cur != null) Destroy(cur);

                var def = WeaponSystem.I != null ? WeaponSystem.I.WeaponOf(i) : null;
                m.bonusAtk = WeaponSystem.I != null ? WeaponSystem.I.AtkFor(i) : 0f;   // levelled weapon ATK feeds EffAtk
                if (def == null) continue;
                var anim = m.Anim;
                if (anim == null || !anim.isHuman) continue;
                var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand == null) continue;

                var sword = BuildSword(def);
                sword.transform.SetParent(hand, false);
                Vector3 gripPos;
                Quaternion gripRot;
                if (GripPose(anim, hand, out gripPos, out gripRot))
                {
                    sword.transform.localPosition = gripPos;
                    sword.transform.localRotation = gripRot;
                }
                else
                {
                    // rig without finger bones: the fixed offset tuned on the old Meshy rigs
                    sword.transform.localPosition = new Vector3(-0.055f, 0.035f, -0.01f);
                    sword.transform.localRotation = Quaternion.Euler(0f, 90f, -90f);
                }
                sword.SetActive(!_hidden);
                _attached[i] = sword;
            }
        }

        // Where the grip centre sits in the fist, in hand space, measured from the wrist joint.
        // Unity-chan's knuckle row is 0.08 m out along the fingers; the handle rests just past the
        // knuckles and ~2.4 cm palm-ward of the joint centres (half the hand thickness + half the grip).
        public static float gripAlongFingers = 0.092f;
        public static float gripPalmOffset = 0.024f;
        // Handshake grip rather than a hammer grip: the handle runs diagonally across the palm, so
        // the blade tilts ~32 deg from the knuckle line toward the fingertips (about the sword's
        // own Z, the palm normal). That makes the blade continue the forearm line in the guard and
        // point forward-down instead of straight ahead when the arm hangs at rest.
        public static Vector3 gripTrimEuler = new Vector3(0f, 0f, -32f);
        const float GripCentreY = 0.02f;                       // sword-space y of the grip cylinder (BuildSword)

        /// Hammer grip derived from the hand's own finger geometry, so it holds on any humanoid rig
        /// instead of a per-rig Euler guess: the handle runs across the palm along the knuckle line
        /// and the blade leaves the fist on the thumb side, with the flat of the blade facing the
        /// palm. The finger proximal joints are direct children of the hand bone, so their
        /// hand-space positions are fixed whatever the animation is doing.
        public static bool GripPose(Animator anim, Transform hand, out Vector3 localPos, out Quaternion localRot)
        {
            localPos = Vector3.zero;
            localRot = Quaternion.identity;
            var index = anim.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var middle = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            var little = anim.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            var thumb = anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            if (index == null || middle == null || little == null || thumb == null) return false;
            Vector3 pI = hand.InverseTransformPoint(index.position);
            Vector3 pM = hand.InverseTransformPoint(middle.position);
            Vector3 pL = hand.InverseTransformPoint(little.position);
            Vector3 pT = hand.InverseTransformPoint(thumb.position);
            if (pM.sqrMagnitude < 1e-6f) return false;

            Vector3 f = pM.normalized;                              // wrist -> middle knuckle
            Vector3 k = pI - pL;                                    // little -> index, across the knuckles
            Vector3 across = k - f * Vector3.Dot(k, f);
            if (across.sqrMagnitude < 1e-8f) return false;
            across.Normalize();
            Vector3 n = Vector3.Cross(f, across);                   // palm normal, sign still open
            Vector3 t = pT - f * Vector3.Dot(pT, f) - across * Vector3.Dot(pT, across);
            if (Vector3.Dot(n, t) < 0f) n = -n;                    // the thumb root lies on the palm side

            // sword +Y (blade) along the knuckle line toward the thumb, +Z (flat) out of the palm
            localRot = Quaternion.LookRotation(n, across) * Quaternion.Euler(gripTrimEuler);
            Vector3 centre = f * gripAlongFingers + n * gripPalmOffset;
            localPos = centre - localRot * new Vector3(0f, GripCentreY, 0f);
            return true;
        }

        public static GameObject BuildSword(WeaponDef def)
        {
            var root = new GameObject("weapon_" + def.name);
            Color steel = def.tier == 1 ? new Color(0.75f, 0.77f, 0.80f)
                : def.tier == 2 ? new Color(0.72f, 0.90f, 0.95f)
                : new Color(1f, 0.90f, 0.55f);
            Color accent = def.Tint;

            var bladeMat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            bladeMat.SetColor("_BaseColor", steel);
            if (bladeMat.HasProperty("_Smoothness")) bladeMat.SetFloat("_Smoothness", 0.6f);
            if (def.tier >= 2)
            {
                bladeMat.EnableKeyword("_EMISSION");
                if (bladeMat.HasProperty("_EmissionColor"))
                    bladeMat.SetColor("_EmissionColor", accent * (def.tier == 3 ? 0.9f : 0.35f));
            }
            var gripMat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            gripMat.SetColor("_BaseColor", new Color(0.22f, 0.16f, 0.12f));
            var guardMat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            guardMat.SetColor("_BaseColor", Color.Lerp(accent, Color.black, 0.25f));

            System.Func<PrimitiveType, Vector3, Vector3, Material, GameObject> part = (type, pos, scale, mat) =>
            {
                var p = GameObject.CreatePrimitive(type);
                Object.Destroy(p.GetComponent<Collider>());
                p.transform.SetParent(root.transform, false);
                p.transform.localPosition = pos;
                p.transform.localScale = scale;
                p.GetComponent<MeshRenderer>().sharedMaterial = mat;
                return p;
            };

            // grip → guard → blade → tip, built along +Y
            part(PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(0.030f, 0.075f, 0.030f), gripMat);
            part(PrimitiveType.Sphere, new Vector3(0f, -0.055f, 0f), new Vector3(0.045f, 0.045f, 0.045f), guardMat);
            part(PrimitiveType.Cube, new Vector3(0f, 0.105f, 0f), new Vector3(0.13f, 0.028f, 0.045f), guardMat);
            part(PrimitiveType.Cube, new Vector3(0f, 0.46f, 0f), new Vector3(0.052f, 0.68f, 0.014f), bladeMat);
            var tip = part(PrimitiveType.Cube, new Vector3(0f, 0.83f, 0f), new Vector3(0.037f, 0.09f, 0.012f), bladeMat);
            tip.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            if (def.tier == 3)
            {
                var l = root.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = accent;
                l.intensity = 1.1f;
                l.range = 1.6f;
                l.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            }
            return root;
        }
    }
}
