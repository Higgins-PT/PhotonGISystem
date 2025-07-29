
using System;
using System.Collections.Generic;
using UnityEngine;
namespace PhotonSystem
{
    public static class AABBUtil
    {
        public static Bounds FromCenterSize(Vector3 c, Vector3 s) => new Bounds(c, s);
        public static Bounds Union(Bounds a, Bounds b)
        {
            var min = Vector3.Min(a.min, b.min);
            var max = Vector3.Max(a.max, b.max);
            var r = new Bounds();
            r.SetMinMax(min, max);
            return r;
        }
        public static float SurfaceArea(ref Bounds b)
        {
            var s = b.size;
            return 2f * (s.x * s.y + s.y * s.z + s.z * s.x);
        }
    }

    [System.Serializable]
    public class BVH8Node : IBVHNode
    {
        public Vector3 center;
        public Vector3 size;
        public IBVHNode parent { get; set; }
        public BVH8Node[] children;
        public PhotonObject photonObject { get; set; }

        public bool IsLeaf => photonObject != null;
        public bool isFull = false;
        public int depth = 0;
        public void RefeshDepth(BVH8Node root)
        {
            depth = GetDepth(root, 0);
        }
        public int GetDepth(BVH8Node root, int depth)
        {
            BVH8Node parent = this.parent as BVH8Node;
            if (parent != root && depth < OctBVH.bvhStackSize - 1)
            {
                return parent.GetDepth(root, depth + 1);
            }
            else
            {
                return depth + 1;
            }
        }
        public Bounds Bounds
        {
            get => AABBUtil.FromCenterSize(center, size);
            set
            {
                center = value.center;
                size = value.size;
            }
        }

        public BVH8Node(PhotonObject p)
        {
            photonObject = p;
            children = null;
            Bounds = p.bvhBounds.ToBounds;
        }

    }
    public class OctBVH : BVHTreeBase<BVH8Node>
    {
        public const int bvhStackSize = 15;
        protected override BVH8Node CreateLeaf(PhotonObject obj) => new BVH8Node(obj);

        protected override IEnumerable<BVH8Node> GetChildren(BVH8Node n)
        {
            var arr = n.children;
            if (arr == null) yield break;
            for (int i = 0; i < arr.Length; ++i)
                if (arr[i] != null) yield return arr[i];
        }
        private static void CheckNodeFullState(BVH8Node node, BVH8Node root)
        {
            bool allFull = true;
            if (node.IsLeaf)
                return;
            for (int i = 0; i < 8; ++i)
            {
                if (node.children[i] != null)
                {
                    if (!node.children[i].isFull)
                    {
                        allFull = false;
                    }

                }
                else
                {
                    allFull = false;
                }
            }
            node.isFull = allFull;
            if (node.parent != root && node.isFull == true)
            {
                CheckNodeFullState(node.parent as BVH8Node, root);
            }
        }
        private void AttachChild(BVH8Node parent, int index, BVH8Node child)
        {
            if (parent.children == null)
                parent.children = new BVH8Node[8];
            parent.children[index] = child;
            child.parent = parent;
            child.RefeshDepth(Root);
            if (child.depth >= bvhStackSize - 1)
            {
                child.isFull = true;
            }

            CheckNodeFullState(parent, Root);
        }

        private int FindFirstEmptySlot(BVH8Node parent)
        {
            if (parent.children == null) return 0;
            for (int i = 0; i < 8; ++i)
                if (parent.children[i] == null) return i;
            return -1;
        }
        private void SplitLeafAndReinsert(BVH8Node current, BVH8Node newLeaf)
        {
            var oldObj = current.photonObject;
            current.photonObject = null;

            var oldLeaf = new BVH8Node(oldObj);
            ModifyObjsNode(oldLeaf, oldObj);
            int idx = FindFirstEmptySlot(current);
            AttachChild(current, idx, oldLeaf);

            InsertNode(current, newLeaf);
            UpdateNodeBounds(current);
        }
        private static float CalculateSurfaceArea(BVH8Node node, BVH8Node insert)
        {
            Bounds merged = node.Bounds;
            merged.Encapsulate(insert.Bounds);
            return merged.size.x * merged.size.y * 2f +
                   merged.size.y * merged.size.z * 2f +
                   merged.size.z * merged.size.x * 2f;
        }
        private static bool IsCompletelyEnclosed(BVH8Node parent, BVH8Node child)
        {
            Vector3 pMin = parent.center - parent.size * 0.5f;
            Vector3 pMax = parent.center + parent.size * 0.5f;
            Vector3 cMin = child.center - child.size * 0.5f;
            Vector3 cMax = child.center + child.size * 0.5f;

            return pMin.x <= cMin.x && pMin.y <= cMin.y && pMin.z <= cMin.z &&
                   pMax.x >= cMax.x && pMax.y >= cMax.y && pMax.z >= cMax.z;
        }
        protected override void InsertNode(BVH8Node current, BVH8Node newNode)
        {
            if (current.children == null)
                current.children = new BVH8Node[8];

            if (current.photonObject != null)
            {
                SplitLeafAndReinsert(current, newNode);
                return;
            }
            int empty = FindFirstEmptySlot(current);
            if (empty != -1)
            {
                AttachChild(current, empty, newNode);
                UpdateNodeBounds(current);
                return;
            }

            BVH8Node enclChild = null;
            foreach (var child in current.children)
            {
                if (child == null || child.isFull) continue;
                if (IsCompletelyEnclosed(child, newNode))
                {
                    enclChild = child;
                    break;
                }
            }
            if (enclChild != null)
            {
                InsertNode(enclChild, newNode);
                UpdateNodeBounds(current);
                return;
            }

            float minInc = float.MaxValue;
            BVH8Node bestChild = null;
            foreach (var child in current.children)
            {
                if (child == null || child.isFull) continue;
                float inc = CalculateSurfaceArea(child, newNode);
                if (inc < minInc)
                {
                    minInc = inc;
                    bestChild = child;
                }
            }

            InsertNode(bestChild, newNode);
            UpdateNodeBounds(current);
        }
        protected override void RemoveNode(BVH8Node target)
        {
            if (target == Root) { Root = null; return; }

            var parent = target.parent as BVH8Node;
            if (parent == null) return;

            int idx = Array.IndexOf(parent.children, target);
            if (idx >= 0) parent.children[idx] = null;

            int childCount = 0;
            BVH8Node survivor = null;
            foreach (var c in parent.children)
                if (c != null) { childCount++; survivor = c; }

            if (childCount >= 2 || parent == Root)
            {
                UpdateAncestorsBounds(parent);
                CheckNodeFullState(parent, Root);
                return;
            }

            var grand = parent.parent as BVH8Node;
            survivor.parent = grand;

            if (grand != null)
            {
                int pIdx = Array.IndexOf(grand.children, parent);
                grand.children[pIdx] = survivor;
                UpdateAncestorsBounds(survivor);
                CheckNodeFullState(survivor, Root);
            }
            else
            {
                Root = survivor;
            }
        }
        protected void UpdateAncestorsBounds(BVH8Node node)
        {
            if(node.parent != Root)
            {
                BVH8Node bvh8Node = node.parent as BVH8Node;
                UpdateNodeBounds(bvh8Node);
                UpdateAncestorsBounds(bvh8Node);
            }

        }

        protected override void OnTreeChanged() { }

        protected override void UpdateNodeBounds(BVH8Node node)
        {
            if (node.children == null) return;
            Bounds b = new Bounds();
            bool first = true;
            foreach (var c in node.children)
            {
                if (c == null) continue;
                if (first) { b = c.Bounds; first = false; }
                else b.Encapsulate(c.Bounds);
            }
            node.Bounds = b;
        }
    }
}