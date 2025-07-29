using System.Collections.Generic;
using UnityEngine;

namespace PhotonSystem
{
    public class BVH2Tree : BVHTreeBase<BVH2Node>
    {

        protected override BVH2Node CreateLeaf(PhotonObject obj) => new BVH2Node(obj);
        protected override IEnumerable<BVH2Node> GetChildren(BVH2Node n)
        {
            if (n.leftChild != null) yield return n.leftChild;
            if (n.rightChild != null) yield return n.rightChild;
        }
        protected override void InsertNode(BVH2Node current, BVH2Node newNode)
        {
            if (current.leftChild == null)
            {
                AttachLeft(current, newNode);

                if (current.photonObject != null)
                    CreateSiblingAndDownward(current, newNode);

                UpdateNodeBounds(current);
                return;
            }

            if (current.rightChild == null)
            {
                AttachRight(current, newNode);

                UpdateNodeBounds(current);
                return;
            }

            if (IsCompletelyEnclosed(current.leftChild, newNode))
            {
                InsertNode(current.leftChild, newNode);
            }
            else if (IsCompletelyEnclosed(current.rightChild, newNode))
            {
                InsertNode(current.rightChild, newNode);
            }
            else
            {
                float incLeft = CalculateSurfaceArea(current.leftChild, newNode);
                float incRight = CalculateSurfaceArea(current.rightChild, newNode);
                if (incLeft < incRight) InsertNode(current.leftChild, newNode);
                else InsertNode(current.rightChild, newNode);
            }
        }
        private static bool IsCompletelyEnclosed(BVH2Node parent, BVH2Node child)
        {
            Vector3 pMin = parent.center - parent.size * 0.5f;
            Vector3 pMax = parent.center + parent.size * 0.5f;
            Vector3 cMin = child.center - child.size * 0.5f;
            Vector3 cMax = child.center + child.size * 0.5f;

            return pMin.x <= cMin.x && pMin.y <= cMin.y && pMin.z <= cMin.z &&
                   pMax.x >= cMax.x && pMax.y >= cMax.y && pMax.z >= cMax.z;
        }

        private static float CalculateSurfaceArea(BVH2Node a, BVH2Node b)
        {
            Vector3 min = Vector3.Min(a.center - a.size * 0.5f,
                                      b.center - b.size * 0.5f);
            Vector3 max = Vector3.Max(a.center + a.size * 0.5f,
                                      b.center + b.size * 0.5f);
            Vector3 s = max - min;
            return 2f * (s.x * s.y + s.y * s.z + s.z * s.x);
        }
        protected override void RemoveNode(BVH2Node target)
        {
            if (target == Root) { Root = null; return; }

            var parent = target.parent as BVH2Node;
            var sibling = parent.leftChild == target ? parent.rightChild : parent.leftChild;

            if (sibling == null)
            {
                Debug.LogWarning("Sibling node is null. Cannot replace parent node.");
                return;
            }
            sibling.parent = parent.parent as BVH2Node;

            if (parent.parent is BVH2Node grand)
            {
                if (grand.leftChild == parent) grand.leftChild = sibling;
                else grand.rightChild = sibling;
            }
            else  // parent ÊÇ¸ù
            {
                Root = sibling;
            }
            UpdateNodeBounds(sibling.parent as BVH2Node);
        }


        protected override void OnTreeChanged()
        {
            if (Root != null) RefitRecursive(Root);
        }


        private void AttachLeft(BVH2Node p, BVH2Node c) { p.leftChild = c; c.parent = p; }
        private void AttachRight(BVH2Node p, BVH2Node c) { p.rightChild = c; c.parent = p; }
        private void CreateSiblingAndDownward(BVH2Node current, BVH2Node newNode)
        {
            var sibling = new BVH2Node
            {
                photonObject = current.photonObject,
                center = current.center,
                size = current.size,
                parent = current
            };

            current.photonObject = null;
            current.rightChild = sibling;
            current.leftChild = newNode;
            newNode.parent = current;
            nodes.Add(sibling);
            nodeMap[sibling.photonObject] = sibling;
        }


        private static void RefitRecursive(BVH2Node n)
        {
            if (n.IsLeaf)
            {
                n.Bounds = n.photonObject.bvhBounds.ToBounds;
                return;
            }
            if (n.leftChild != null) RefitRecursive(n.leftChild);
            if (n.rightChild != null) RefitRecursive(n.rightChild);

            var b = n.leftChild.Bounds;
            if (n.rightChild != null) b.Encapsulate(n.rightChild.Bounds);
            n.Bounds = b;
        }

        protected override void UpdateNodeBounds(BVH2Node node)
        {
            for (var n = node; n != null; n = n.parent as BVH2Node)
            {
                if (n.leftChild == null && n.rightChild == null)
                {
                    if (n.photonObject != null)
                    {
                        n.center = n.photonObject.bvhBounds.center;
                        n.size = n.photonObject.bvhBounds.size;
                    }
                }
                else
                {
                    Vector3 min = Vector3.positiveInfinity;
                    Vector3 max = Vector3.negativeInfinity;

                    if (n.leftChild != null)
                    {
                        Vector3 lMin = n.leftChild.center - n.leftChild.size * 0.5f;
                        Vector3 lMax = n.leftChild.center + n.leftChild.size * 0.5f;
                        min = Vector3.Min(min, lMin);
                        max = Vector3.Max(max, lMax);
                    }

                    if (n.rightChild != null)
                    {
                        Vector3 rMin = n.rightChild.center - n.rightChild.size * 0.5f;
                        Vector3 rMax = n.rightChild.center + n.rightChild.size * 0.5f;
                        min = Vector3.Min(min, rMin);
                        max = Vector3.Max(max, rMax);
                    }

                    n.center = (min + max) * 0.5f;
                    n.size = max - min;
                }
            }
        }
    }
}
