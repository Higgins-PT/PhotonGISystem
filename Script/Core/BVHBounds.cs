using UnityEngine;
using UnityEngine.UIElements;

namespace PhotonSystem
{
    [System.Serializable]
    public class BVHBounds
    {
        public Vector3 center; // 包围盒中心
        public Vector3 size;   // 包围盒尺寸

        public void UpdateFromBounds(Bounds bounds)
        {
            center = bounds.center;
            size = bounds.size + new Vector3(0.01f, 0.01f, 0.01f);
        }

        public Vector3 Min => center - size * 0.5f;
        public Vector3 Max => center + size * 0.5f;
        public Bounds ToBounds => new Bounds(center, size);
        public Bounds GetBounds()
        {
            return new Bounds(center, size);
        }

    }
    [System.Serializable]
    public class BVHNode
    {
        public Vector3 center; // 包围盒中心
        public Vector3 size;   // 包围盒尺寸
        public BVHNode parent;
        public BVHNode leftChild;
        public BVHNode rightChild;
        public PhotonObject photonObject; 

    }
}
