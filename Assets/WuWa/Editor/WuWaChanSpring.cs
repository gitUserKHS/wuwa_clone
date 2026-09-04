using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityChan;

namespace WuWa.EditorTools
{
    /// Gives the rebuilt party-member prefabs Unity-chan's secondary motion: the twin-tails, the
    /// front/side hair, the head ribbons, the chest bones and the whole skirt cage.
    ///
    /// Nothing here is authored. The entire configuration is READ OFF the package's own
    /// unitychan_dynamic prefab at run time -- every SpringBone parameter, every SpringCollider
    /// radius, the SpringManager's curves, and above all the ORDER of SpringManager.springBones,
    /// because the manager walks that array in sequence and a parent bone must settle before its
    /// child reads its position. Copying by hand would be 40 bones x 10 fields of transcription
    /// risk for no gain.
    ///
    /// The mapping is by transform PATH below the root. WuWaChanSwap re-parented the FBX's children
    /// straight under the member root, so "Character1_Reference/..." resolves identically in both
    /// prefabs -- verified, not assumed: the only paths the reference needs that the FBX does not
    /// ship are the three Locator_* nodes (Head_Above, Left/RightUpLeg_Middle), which exist only in
    /// the dynamic prefab as hand-placed collider anchors. Those are recreated with the reference's
    /// exact local TRS; everything else is found, never built.
    ///
    /// Note the reference sets isUseEachBoneForceSettings on all 40 bones, so SpringManager's
    /// stiffness/drag curves never actually override the per-bone values. They are copied anyway
    /// so the component reads the same in the inspector.
    public static class WuWaChanSpring
    {
        const string RefPrefab = "Assets/unity-chan!/Unity-chan! Model/Prefabs/unitychan_dynamic.prefab";
        const int Members = 3;

        [MenuItem("WuWa/Add Unity-chan spring bones to all members")]
        public static void AddAllMenu() { Debug.Log(AddAll()); }

        public static string AddAll()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Members; i++) sb.Append("[" + i + "] " + AddSpringToMember(i) + "\n");
            return sb.ToString();
        }

        public static string AddSpringToMember(int memberIndex)
        {
            if (memberIndex < 0 || memberIndex >= Members) return "bad member index";
            string prefabPath = "Assets/WuWa/Prefabs/Member" + memberIndex + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                return "member prefab missing at " + prefabPath;

            var refRoot = AssetDatabase.LoadAssetAtPath<GameObject>(RefPrefab);
            if (refRoot == null) return "reference prefab missing at " + RefPrefab;
            var refMgr = refRoot.GetComponent<SpringManager>();
            if (refMgr == null) return "reference prefab has no SpringManager on its root";
            var refCols = refRoot.GetComponentsInChildren<SpringCollider>(true);
            var refBones = refMgr.springBones;
            if (refBones == null || refBones.Length == 0)
                return "reference SpringManager has an empty springBones list";
            int refBoneTotal = refRoot.GetComponentsInChildren<SpringBone>(true).Length;

            int locators = 0, cols = 0, bones = 0, colRefs = 0;
            var problems = new List<string>();
            var sb = new System.Text.StringBuilder();

            using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                var root = scope.prefabContentsRoot;

                // re-runnable: clear any earlier spring pass and touch nothing else on the root
                foreach (var old in root.GetComponentsInChildren<SpringBone>(true)) Object.DestroyImmediate(old);
                foreach (var old in root.GetComponentsInChildren<SpringCollider>(true)) Object.DestroyImmediate(old);
                foreach (var old in root.GetComponentsInChildren<SpringManager>(true)) Object.DestroyImmediate(old);

                // colliders first -- the bones hold references to them
                var colByPath = new Dictionary<string, SpringCollider>();
                foreach (var rc in refCols)
                {
                    if (rc == null) continue;
                    string path = PathOf(rc.transform, refRoot.transform);
                    if (path == null) { problems.Add("colliderOutsideReferenceRoot"); continue; }
                    var t = Resolve(root.transform, refRoot.transform, path, ref locators);
                    if (t == null) { problems.Add("noCollTarget:" + path); continue; }
                    var c = t.gameObject.AddComponent<SpringCollider>();
                    c.radius = rc.radius;
                    c.enabled = rc.enabled;
                    colByPath[path] = c;
                    cols++;
                }

                // bones in the manager's own order
                var chain = new List<SpringBone>();
                foreach (var rb in refBones)
                {
                    if (rb == null) { problems.Add("nullBoneInReferenceList"); continue; }
                    string path = PathOf(rb.transform, refRoot.transform);
                    var t = path == null ? null : root.transform.Find(path);
                    if (t == null) { problems.Add("noBone:" + path); continue; }

                    var b = t.gameObject.AddComponent<SpringBone>();
                    b.enabled = rb.enabled;
                    b.boneAxis = rb.boneAxis;
                    b.radius = rb.radius;
                    b.isUseEachBoneForceSettings = rb.isUseEachBoneForceSettings;
                    b.stiffnessForce = rb.stiffnessForce;
                    b.dragForce = rb.dragForce;
                    b.springForce = rb.springForce;
                    // the reference ships debug on, which draws a wire sphere per bone: 40 per
                    // member, 120 across the party, all over the Scene view. Editor-only noise.
                    b.debug = false;
                    b.threshold = rb.threshold;

                    // child is the tip the verlet solver chases; a null one is a Start() null-ref
                    string childPath = rb.child == null ? null : PathOf(rb.child, refRoot.transform);
                    b.child = childPath == null ? null : root.transform.Find(childPath);
                    if (b.child == null) problems.Add("noChild:" + path);

                    var wired = new List<SpringCollider>();
                    if (rb.colliders != null)
                    {
                        foreach (var rc in rb.colliders)
                        {
                            if (rc == null) continue;
                            string cp = PathOf(rc.transform, refRoot.transform);
                            SpringCollider c;
                            if (cp != null && colByPath.TryGetValue(cp, out c)) { wired.Add(c); colRefs++; }
                            else problems.Add("noCollRef:" + cp);
                        }
                    }
                    b.colliders = wired.ToArray();

                    chain.Add(b);
                    bones++;
                }

                var mgr = root.AddComponent<SpringManager>();
                mgr.dynamicRatio = refMgr.dynamicRatio;
                mgr.stiffnessForce = refMgr.stiffnessForce;
                mgr.dragForce = refMgr.dragForce;
                mgr.stiffnessCurve = CopyCurve(refMgr.stiffnessCurve);
                mgr.dragCurve = CopyCurve(refMgr.dragCurve);
                mgr.springBones = chain.ToArray();

                sb.Append("built bones=" + bones + "/" + refBones.Length);
                sb.Append(" colliders=" + cols + "/" + refCols.Length);
                sb.Append(" locatorsMade=" + locators + " collRefs=" + colRefs);
            }
            AssetDatabase.SaveAssets();

            // read the SAVED asset back rather than trusting the numbers above
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var vMgr = saved == null ? null : saved.GetComponent<SpringManager>();
            int vBones = saved == null ? -1 : saved.GetComponentsInChildren<SpringBone>(true).Length;
            int vCols = saved == null ? -1 : saved.GetComponentsInChildren<SpringCollider>(true).Length;
            int vList = vMgr == null || vMgr.springBones == null ? -1 : vMgr.springBones.Length;
            int vNullEntries = 0, vNullChild = 0, vColRefs = 0;
            if (vMgr != null && vMgr.springBones != null)
            {
                foreach (var b in vMgr.springBones)
                {
                    if (b == null) { vNullEntries++; continue; }
                    if (b.child == null) vNullChild++;
                    if (b.colliders != null) vColRefs += b.colliders.Length;
                }
            }
            sb.Append(" | VERIFY onAsset bones=" + vBones + " (reference has " + refBoneTotal + ")");
            sb.Append(" colliders=" + vCols + " mgrList=" + vList);
            sb.Append(" nullEntries=" + vNullEntries + " nullChild=" + vNullChild + " collRefs=" + vColRefs);
            if (vMgr != null)
                sb.Append(" mgr(dynamicRatio=" + vMgr.dynamicRatio + " stiff=" + vMgr.stiffnessForce +
                          " drag=" + vMgr.dragForce + " stiffKeys=" + vMgr.stiffnessCurve.length +
                          " dragKeys=" + vMgr.dragCurve.length + ")");
            else sb.Append(" mgr=MISSING");
            if (problems.Count > 0) sb.Append(" PROBLEMS=" + string.Join("|", problems.ToArray()));
            return sb.ToString();
        }

        /// Slash path of `t` below `root`, or null when `t` is not under `root`.
        static string PathOf(Transform t, Transform root)
        {
            if (t == null) return null;
            if (t == root) return "";
            string p = t.name;
            var cur = t.parent;
            while (cur != null && cur != root) { p = cur.name + "/" + p; cur = cur.parent; }
            return cur == root ? p : null;
        }

        /// Find `path` under `root`; if only its LEAF is missing, recreate it from the reference's
        /// local TRS. This exists solely for the three Locator_* collider anchors, which live in the
        /// dynamic prefab but not in unitychan.fbx. A missing PARENT is a real hierarchy mismatch,
        /// so it is reported instead of being papered over with a new empty.
        static Transform Resolve(Transform root, Transform refRoot, string path, ref int created)
        {
            var t = root.Find(path);
            if (t != null) return t;
            var refT = refRoot.Find(path);
            if (refT == null) return null;
            int cut = path.LastIndexOf('/');
            if (cut < 0) return null;
            var parent = root.Find(path.Substring(0, cut));
            if (parent == null) return null;
            var go = new GameObject(path.Substring(cut + 1));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = refT.localPosition;
            go.transform.localRotation = refT.localRotation;
            go.transform.localScale = refT.localScale;
            go.layer = parent.gameObject.layer;
            created++;
            return go.transform;
        }

        static AnimationCurve CopyCurve(AnimationCurve src)
        {
            if (src == null) return new AnimationCurve();
            var c = new AnimationCurve(src.keys);
            c.preWrapMode = src.preWrapMode;
            c.postWrapMode = src.postWrapMode;
            return c;
        }
    }
}
