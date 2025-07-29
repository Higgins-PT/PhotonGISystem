using UnityEngine;

namespace PhotonSystem
{
    [System.Serializable]
    public class BVH2Node : IBVHNode
    {
        public PhotonObject photonObject { get; set; }
        public IBVHNode parent { get; set; }

        public Bounds Bounds
        {
            get => new Bounds(center, size);
            set { center = value.center; size = value.size; }
        }

        public bool IsLeaf => photonObject != null;
        public BVH2Node leftChild;
        public BVH2Node rightChild;

        public Vector3 center; 
        public Vector3 size;
        public BVH2Node() { }
        public BVH2Node(PhotonObject p)
        {
            photonObject = p;
            Bounds = p.bvhBounds.ToBounds;
        }
    }
}
