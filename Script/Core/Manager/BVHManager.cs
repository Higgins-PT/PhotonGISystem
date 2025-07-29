using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEngine;

namespace PhotonSystem
{
    public class BVHManager : PhotonSingleton<BVHManager>
    {
        /* -------- 可在 Inspector 调的调试字段 -------- */
        public bool gizmosDebugAncestors = false;   // 始终绘制而不必选中
        public bool gizmosDebugAlways = false;   // 始终绘制而不必选中
        public Color leafFaceAlpha = new Color(1, 1, 1, 0.2f); // 叶节点面的透明度
        public bool drawInternalFaces = false;   // 内部节点是否画实心面
        /* -------- 运行期数据 -------- */
        public readonly OctBVH tree = new OctBVH(); // 真正的 BVH
        public readonly List<PhotonObject> photonObjects = new(); // 仅做展示
        public int bvhCount;
        public int bvhDepth;
        private int bvhDepthLast;
        private List<BVH8Node> flatCache = new List<BVH8Node>();  // 展平的节点用于 Gizmos
        private bool bvhDirty = true;              // 结构是否改变
        private bool bvhDepthDirty = false;

        #region 对外 API -------------------------

        public void AddPhotonObject(PhotonObject obj)
        {
            tree.AddPhotonObject(obj);
            bvhDirty = true;
        }

        public void RemovePhotonObject(PhotonObject obj)
        {
            tree.RemovePhotonObject(obj);
            bvhDirty = true;
        }

        #endregion

        #region Unity 生命周期 -------------------

        /// <summary>
        /// 重置 BVH 树
        /// </summary>
        /// <param name="manager">目标 BVHManager</param>
        public void ResetBVH()
        {
            tree.Root = null;
            tree.nodes.Clear();
            tree.nodeMap.Clear();
            PhotonObject[] photonObjects = FindObjectsByType<PhotonObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (PhotonObject photonObject in photonObjects)
            {
                if (photonObject.gameObject.activeInHierarchy && photonObject.enabled == true)
                {
                    AddPhotonObject(photonObject);
                }
            }

        }
        public override void PhotonUpdate()
        {
            /* 每帧同步可视化/调试用数据 */
            photonObjects.Clear();
            photonObjects.AddRange(tree.nodeMap.Keys);
            if (bvhDepthDirty)
            {
                bvhDepthDirty = false;
                ResetBVH();
            }
            if (bvhDirty)
            {
                tree.RebuildNodesFromMap();

                bvhDirty = false;
                flatCache = tree.nodes;
                bvhCount = flatCache.Count;

                // 新增：计算最大深度
                bvhDepth = CalculateDepth();
                if (bvhDepthLast != bvhDepth)
                {
                    bvhDepthLast = bvhDepth;
                    bvhDepthDirty = true;
                }
            }

        }
        private int CalculateDepth()
        {
            int maxDepth = 0;
            foreach (var node in flatCache)
            {
                if (!node.IsLeaf) continue;

                int depth = 0;
                IBVHNode cur = node;
                while (cur != null)
                {
                    depth++;
                    cur = cur.parent;
                }
                if (depth > maxDepth) maxDepth = depth;
            }
            return maxDepth;
        }
        private void OnDrawGizmosSelected()
        {
            if (!gizmosDebugAlways) DrawGizmos();
        }

        private void OnDrawGizmos()
        {
            if (gizmosDebugAlways) DrawGizmos();
        }

        #endregion
        #region Ancestor Utilities -------------------------

        /// <summary>
        /// 获取从自身到根节点的完整祖先链（包含自身）
        /// </summary>
        /// <param name="node">起始节点</param>
        /// <returns>按从自身到根节点顺序排列的节点列表</returns>
        public List<IBVHNode> GetAncestorChain(IBVHNode node)
        {
            var chain = new List<IBVHNode>();
            var cur = node;
            while (cur != null)
            {
                chain.Add(cur);
                cur = cur.parent;
            }
            return chain;
        }

        /// <summary>
        /// 在 Gizmos 中绘制指定 PhotonObject 对应节点及其所有祖先节点的包围盒
        /// </summary>
        /// <param name="obj">目标 PhotonObject</param>
        public void DrawGizmosForObjectAncestors(PhotonObject obj)
        {
            if (obj == null) return;
            if (!tree.nodeMap.TryGetValue(obj, out BVH8Node node)) return;

            // 可选：根据需求设置 Gizmos 颜色/透明度
            Color originalColor = Gizmos.color;
            foreach (var anc in GetAncestorChain(node))
            {
                Bounds b = anc.Bounds;
                // 画半透明立方体
                Gizmos.color = new Color(1, 0, 0, 0.1f);
                Gizmos.DrawCube(b.center, b.size);
                // 画线框
                Gizmos.color = new Color(1, 0, 0, 1f);
                Gizmos.DrawWireCube(b.center, b.size);
            }
            Gizmos.color = originalColor;
        }

        #endregion

        #region Gizmos 绘制 ----------------------

        private void DrawGizmos()
        {
            if (flatCache == null || flatCache.Count == 0) return;

            for (int i = 0; i < flatCache.Count; ++i)
            {
                var node = flatCache[i];
                if (node == null) continue;

                Color c = ColorFromIndex(i);

                bool isLeaf = node.IsLeaf;
                Bounds b = node.Bounds;

                if (isLeaf)
                {
                    // 半透明面 + 实线框
                    Gizmos.color = new Color(c.r, c.g, c.b, leafFaceAlpha.a);
                    Gizmos.DrawCube(b.center, b.size);

                    Gizmos.color = new Color(c.r, c.g, c.b, 1f);
                    Gizmos.DrawWireCube(b.center, b.size);
                }
                else
                {
                    // 只画线框或可选画面
                    Gizmos.color = new Color(c.r, c.g, c.b, 1f);
                    Gizmos.DrawWireCube(b.center, b.size);

                    if (drawInternalFaces)
                    {
                        Gizmos.color = new Color(c.r, c.g, c.b, 0.05f);
                        Gizmos.DrawCube(b.center, b.size);
                    }
                }
            }
        }

        private static Color ColorFromIndex(int index)
        {
            UnityEngine.Random.InitState(index);
            return new Color(UnityEngine.Random.value,
                             UnityEngine.Random.value,
                             UnityEngine.Random.value, 0.5f);
        }

        #endregion
    }

    public interface IBVHNode
    {
        PhotonObject photonObject { get; set; }
        Bounds Bounds { get; set; }
        IBVHNode parent { get; set; }
        bool IsLeaf { get; }
    }
    public abstract class BVHTreeBase<TNode> where TNode : class, IBVHNode
    {
        public TNode Root { get; set; }

        [NonSerialized] public readonly List<TNode> nodes = new List<TNode>();
        [NonSerialized] public readonly Dictionary<PhotonObject, TNode> nodeMap = new Dictionary<PhotonObject, TNode>();
        public void RebuildNodesFromMap()
        {
            nodes.Clear();
            FlattenPreOrder(nodes, Root);
        }
        public void EnsureRootAtFront()
        {
            if (Root == null) return;

            int idx = nodes.IndexOf(Root);

            if (idx == 0) return;

            if (idx > 0)
            {
                nodes.RemoveAt(idx);
                nodes.Insert(0, Root);
            }
            else
            {
                nodes.Insert(0, Root);
            }
        }
        public void ModifyObjsNode(TNode node, PhotonObject photonObject)
        {
            nodeMap?.Remove(photonObject);
            nodeMap.Add(photonObject, node);
        }
        public void AddPhotonObject(PhotonObject obj)
        {
            if (obj == null) return;

            TNode newNode = CreateLeaf(obj);

            if (Root == null)
            {
                Root = newNode;
                UpdateNodeBounds(newNode);
            }
            else
            {
                InsertNode(Root, newNode);
            }
            nodeMap[obj] = newNode;
            OnTreeChanged();
        }

        public void RemovePhotonObject(PhotonObject obj)
        {
            if (obj == null || !nodeMap.TryGetValue(obj, out var target)) return;

            RemoveNode(target);

            nodeMap.Remove(obj);
            OnTreeChanged();
        }

        /* ---------- 抽象 / 可重写 ---------- */
        protected abstract TNode CreateLeaf(PhotonObject obj);

        protected abstract void UpdateNodeBounds(TNode node);

        protected abstract void InsertNode(TNode root, TNode newNode);

        protected abstract void RemoveNode(TNode targetNode);

        protected virtual void OnTreeChanged() { }

        /* ---------- 公共工具 ---------- */
        protected abstract IEnumerable<TNode> GetChildren(TNode node);
        public List<TNode> FlattenPreOrder(TNode root)
        {

            var result = new List<TNode>();
            var visited = new HashSet<TNode>();
            Traverse(root, result, visited, 0);
            return result;
        }
        public void FlattenPreOrder(List<TNode> list, TNode root)
        {
            var visited = new HashSet<TNode>();
            Traverse(root, list, visited, 0);
        }
        private void Traverse(TNode node, List<TNode> list, HashSet<TNode> visited, int depth)
        {
            if (node == null)
                return;
            if (depth >= 200)
                return;
            if (!visited.Add(node))
                return;
            list.Add(node);

            var children = GetChildren(node);
            if (children == null)
                return;

            foreach (var child in children)
            {
                Traverse(child, list, visited, depth + 1);
            }
        }

    }
}