
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotonSystem
{
    public class SurfaceCacheManager : PhotonSingleton<SurfaceCacheManager>
    {
        public ComputeBuffer surfaceCache;
        public const int size = 4096;
        [Range(0f, 1f)]
        public float surfaceCacheDecay = 0.95f;
        // Quadtree that holds all MeshCards
        public SurfaceCacheQuadtree directLightSurfaceCache = new SurfaceCacheQuadtree();

        // Keep track of all PhotonObjects and their SurfaceCaches
        public List<PhotonObject> allObjects = new List<PhotonObject>();
        public Dictionary<string, SurfaceCache> directLightSurfaceHash = new Dictionary<string, SurfaceCache>();
        public Dictionary<string, List<PhotonObject>> directLightSurfaceObjectsHash = new Dictionary<string, List<PhotonObject>>();
        private Dictionary<string, SurfaceMaterials> surfaceMaterialsCache = new Dictionary<string, SurfaceMaterials>();
        // 在此放一个常驻相机，用于正交渲染贴图
        public Camera bakeCamera;

        [Header("Default Shaders (Fallback)")]
        public Shader defaultAlbedoShader;
        public Shader defaultNormalShader;
        public Shader defaultEmissiveShader;
        public Shader defaultDepthShader;
        public Shader defaultMetallicShader;
        public Shader defaultSmoothnessShader;


        [Header("Material Property Mappings")]
        [SerializeField]
        private List<MaterialPropertyMapping> materialMappings = new List<MaterialPropertyMapping>();

        private List<int> validSizes;
        private bool initFinish = false;
        public SurfaceMaterials GetSurfaceMaterials(PhotonObject photonObject,
                                              Shader albedoShader,
                                              Shader normalShader,
                                              Shader emissiveShader,
                                              Shader depthShader,
                                              Shader smoothnessShader,
                                              Shader metallicShader)
        {
            string key = photonObject.GetObjectHash();
            if (!surfaceMaterialsCache.ContainsKey(key))
            {
                SurfaceMaterials newMaterials = new SurfaceMaterials(albedoShader, normalShader, emissiveShader,
                                                                        depthShader, smoothnessShader, metallicShader);
                surfaceMaterialsCache.Add(key, newMaterials);
            }

            return surfaceMaterialsCache[key];
        }
        public void ApplyMaterialMapping(Material srcMat, Material dstMat)
        {
            if (srcMat == null || dstMat == null) return;

            foreach (var mapping in materialMappings)
            {
                // 如果 sourceName 或 targetName 为空，就跳过
                if (string.IsNullOrEmpty(mapping.sourceName)) continue;
                if (string.IsNullOrEmpty(mapping.targetName)) continue;

                // 如果原始材质 srcMat 没有这个属性，或目标 dstMat 没有这个属性，也跳过
                if (!srcMat.HasProperty(mapping.sourceName)) continue;
                if (!dstMat.HasProperty(mapping.targetName)) continue;

                switch (mapping.paramType)
                {
                    case MaterialParamType.Float:
                        float fVal = srcMat.GetFloat(mapping.sourceName);
                        dstMat.SetFloat(mapping.targetName, fVal);
                        break;

                    case MaterialParamType.Color:
                        Color cVal = srcMat.GetColor(mapping.sourceName);
                        dstMat.SetColor(mapping.targetName, cVal);
                        break;

                    case MaterialParamType.Vector:
                        Vector4 vVal = srcMat.GetVector(mapping.sourceName);
                        dstMat.SetVector(mapping.targetName, vVal);
                        break;

                    case MaterialParamType.Texture:
                        Texture tex = srcMat.GetTexture(mapping.sourceName);
                        dstMat.SetTexture(mapping.targetName, tex);
                        Vector2 scale = srcMat.GetTextureScale(mapping.sourceName);
                        Vector2 offset = srcMat.GetTextureOffset(mapping.sourceName);
                        dstMat.SetTextureScale(mapping.targetName, scale);
                        dstMat.SetTextureOffset(mapping.targetName, offset);
                        break;

                    case MaterialParamType.Int:
                        // Unity 没有单独的 GetInt API，但 GetFloat 也能拿 int
                        int iVal = (int)srcMat.GetFloat(mapping.sourceName);
                        dstMat.SetFloat(mapping.targetName, iVal);
                        break;
                    
                }
            }
        }
        public void CreateBuffer<T>(ref ComputeBuffer computeBuffer, int size) where T : struct
        {
            if (computeBuffer != null) return;
            int objectDataStride = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            computeBuffer = new ComputeBuffer(size * size, objectDataStride);
        }
        public void CreateSurfaceCacheBuffer()
        {
            CreateBuffer<SurfaceCacheData>(ref surfaceCache, size); //new surface
            CommandBufferHelper.ExecuteComputeWithCmd((CommandBuffer cmd) => {
                RTHelper.Instance.FillSurfaceCache(cmd, surfaceCache, new float4(0, 0, 0, 1), new float3(0, 1, 0), new float4(0, 0, 0, 0), 0);
            });
        }
        private void Init()
        {
            initFinish = true;


            allObjects.Clear();
            directLightSurfaceHash.Clear();
            directLightSurfaceObjectsHash.Clear();

            CreateSurfaceCacheBuffer();
            validSizes = new List<int>();
            int sizeL = minSize;
            while (sizeL <= maxSize)
            {
                validSizes.Add(sizeL);
                sizeL <<= 1;
            }
            if (bakeCamera == null)
            {
                GameObject camGo = new GameObject("BakeCamera");
                bakeCamera = camGo.AddComponent<Camera>();
                bakeCamera.orthographic = true;
                bakeCamera.transform.parent = this.transform;
                camGo.SetActive(false);
            }

        }
        private void Awake()
        {
            if (!initFinish)
            {
                Init();
            }
        }
        private void OnEnable()
        {
            if (!initFinish)
            {
                Init();
            }
        }

        private void OnDisable()
        {
            surfaceCache?.Release();
        }
        private new void OnDestroy()
        {
            allObjects.Clear();
            directLightSurfaceHash.Clear();
            directLightSurfaceObjectsHash.Clear();

        }
        // The maximum and minimum node sizes we allow
        public const int maxSize = 256; 
        public const int minSize = 32;
        public void AddPhotonObject(PhotonObject photonObject)
        {
            if (!initFinish)
            {
                Init();
            }
            string hash = photonObject.GetObjectHash();
            if (!directLightSurfaceHash.ContainsKey(hash))
            {
                photonObject.InitDirectLightSurfaceCacheIfNeeded();

                directLightSurfaceHash[hash] = photonObject.directLightSufaceCache;
                directLightSurfaceObjectsHash[hash] = new List<PhotonObject>();
                directLightSurfaceCache.AddNode(photonObject.directLightSufaceCache, minSize);
            }
            allObjects.Add(photonObject);
            directLightSurfaceObjectsHash[hash].Add(photonObject);

        }

        public void RemovePhotonObject(PhotonObject photonObject)
        {
            string hash = photonObject.GetObjectHash();
            allObjects.Remove(photonObject);
            if (directLightSurfaceObjectsHash.TryGetValue(hash, out List<PhotonObject> photonObjectsList))
            {

                if (photonObjectsList.Count <= 1)
                {
                    directLightSurfaceCache.RemoveNode(photonObject.directLightSufaceCache);
                    directLightSurfaceHash.Remove(hash);
                    directLightSurfaceObjectsHash.Remove(hash);
                }
                else
                {
                    photonObjectsList.Remove(photonObject);
                    SurfaceCache surfaceCache = directLightSurfaceHash[hash];
                    if (surfaceCache.photonObject == photonObject)
                    {
                        surfaceCache.photonObject = photonObjectsList[0];
                    }
                }

            }
        }
        //TodoList: Make ComputeWorldSize influence by camera distances
        public void UpdateSurfaceCacheNodeSizesWorldSpace(int sampleCount)
        {
            // 1) 从 directLightSurfaceHash 里随机抽取 sampleCount 个 key
            List<string> allKeys = directLightSurfaceHash.Keys.ToList();
            if (allKeys.Count == 0) return;

            // 如果 sampleCount >= allKeys.Count，说明没有必要随机抽了；用全部即可
            if (sampleCount > allKeys.Count) sampleCount = allKeys.Count;
            List<string> sampledKeys = ShuffleHelper.ShuffleTake(allKeys, sampleCount);


            float minEst = float.MaxValue;
            float maxEst = float.MinValue;

            // 存一下：每个 SurfaceCache 的“估值”
            Dictionary<SurfaceCache, float> cacheEstimates = new Dictionary<SurfaceCache, float>();

            // 2) 遍历被抽到的 SurfaceCache
            foreach (var key in sampledKeys)
            {
                if (!directLightSurfaceHash.TryGetValue(key, out SurfaceCache sc))
                    continue;

                // 找到引用它的所有 PhotonObject
                if (!directLightSurfaceObjectsHash.TryGetValue(key, out List<PhotonObject> objs)
                    || objs == null || objs.Count == 0)
                    continue;

                // 计算“最大 Bounds 尺寸”作为它的估值
                float estVal = 0f;
                foreach (var obj in objs)
                {
                    float size = ComputeWorldSize(obj);
                    if (size > estVal)
                    {
                        estVal = size;
                    }
                }

                cacheEstimates[sc] = estVal;
                if (estVal < minEst) minEst = estVal;
                if (estVal > maxEst) maxEst = estVal;
            }

            // 如果都没有有效的对象
            if (cacheEstimates.Count == 0) return;
            foreach (var kvp in cacheEstimates)
            {
                SurfaceCache sc = kvp.Key;
                float estVal = kvp.Value;

                // 4) 算 log2
                float logVal = Mathf.Log(estVal, 2f);

                // clamp 到 [log2(32), log2(256)] = [5, 8]
                float clampedLog = Mathf.Clamp(logVal, Mathf.Log(minSize, 2f), Mathf.Log(maxSize, 2f));

                // 最终 size = 2^ round(clampedLog)
                int nearestExp = Mathf.RoundToInt(clampedLog);
                int finalSize = (int)Mathf.Pow(2f, nearestExp);

                // Double-check 32 <= finalSize <= 256
                if (finalSize < minSize) finalSize = minSize;
                if (finalSize > maxSize) finalSize = maxSize;

                // 5) 比较与当前 assignedSize，如不同则修改
                if (sc.assignedSize != finalSize)
                {
                    directLightSurfaceCache.ModifyNode(sc, finalSize);
                    sc.assignedSize = finalSize;

                    sc.Refresh(finalSize);
                }
            }
        }
        private float ComputeWorldSize(PhotonObject obj)
        {
            Bounds localBounds = obj.renderer.bounds;

            Vector3 size = localBounds.size;
            Vector3 transformSize = obj.transform.lossyScale;
            float distance = Mathf.Max((PhotonGISystem.Instance.MainCamera.transform.position - obj.transform.position).magnitude, 1);
            float maxDim = Mathf.Max(size.x * transformSize.x, size.y * transformSize.y, size.z * transformSize.z) / distance;
            return maxDim;
        }
        // Start() and Update() remain as is.
        void Start()
        {
        }

        void Update()
        {
            UpdateSurfaceCacheNodeSizesWorldSpace(3);
        }
    }
    public class SurfaceCacheQuadtree
    {
        public SurfaceCacheQuadtreeNode rootNode;

        public SurfaceCacheQuadtree()
        {
            // Initialize the root with size=4096, offsetX=0, offsetY=0
            rootNode = new SurfaceCacheQuadtreeNode(SurfaceCacheManager.size, 0, 0);
        }

        /// <summary>
        /// Add ALL 6 MeshCards of a SurfaceCache to the quadtree.
        /// If ANY card fails to add, we remove those that succeeded
        /// and return false.
        /// </summary>
        public bool AddNode(SurfaceCache surfaceCache, int targetSize)
        {

            // Keep track of cards that were successfully inserted
            var successfullyAddedCards = new List<MeshCard>();

            foreach (var meshCard in surfaceCache.meshCards)
            {
                // If for some reason the array is partially null, skip or handle
                if (meshCard == null)
                {
                    RemoveCards(successfullyAddedCards);
                    return false;
                }
                    
                bool success = rootNode.AddNode(meshCard, targetSize);
                if (!success)
                {
                    // If any fails, remove the ones that have already been added
                    RemoveCards(successfullyAddedCards);
                    return false;
                }
                else
                {
                    successfullyAddedCards.Add(meshCard);
                }
            }

            // If we get here, all 6 MeshCards were successfully added
            return true;
        }

        /// <summary>
        /// Removes ALL 6 MeshCards of a SurfaceCache from the quadtree.
        /// </summary>
        public void RemoveNode(SurfaceCache surfaceCache)
        {
            foreach (var meshCard in surfaceCache.meshCards)
            {
                if (meshCard == null) continue; // or handle differently
                if (meshCard.parentNode != null)
                {
                    meshCard.parentNode.RemoveNode(meshCard);
                }
            }
        }

        /// <summary>
        /// Modify the 'size' requirement for all 6 MeshCards in this SurfaceCache:
        /// 1) Remove them from the quadtree
        /// 2) Re-add them with the new size
        /// </summary>
        public bool ModifyNode(SurfaceCache surfaceCache, int newSize)
        {
            // First remove all 6
            RemoveNode(surfaceCache);

            // Then attempt to add them at the new size
            return AddNode(surfaceCache, newSize);
        }

        /// <summary>
        /// Convenience method to remove multiple MeshCards if something fails.
        /// </summary>
        private void RemoveCards(List<MeshCard> meshCards)
        {
            foreach (var card in meshCards)
            {
                if (card != null && card.parentNode != null)
                {
                    card.parentNode.RemoveNode(card);
                }
            }
        }
    }


    /// <summary>
    /// A single node in the quadtree now stores exactly ONE MeshCard if it is a leaf.
    /// We also track (offsetX, offsetY) so we know where this node starts in the 4096 texture.
    /// </summary>
    public class SurfaceCacheQuadtreeNode
    {
        public SurfaceCacheQuadtreeNode node_Left_UP;
        public SurfaceCacheQuadtreeNode node_Left_Down;
        public SurfaceCacheQuadtreeNode node_Right_Up;
        public SurfaceCacheQuadtreeNode node_Right_Down;

        // A reference to the parent node (for upward merging, if needed)
        public SurfaceCacheQuadtreeNode parent;
        // Indicates if this node currently holds an element (i.e. a MeshCard)
        public bool haveElement = false;
        // Indicates if this node is "full" (e.g., a leaf with an element, or all children are full)
        public bool full = false;
        // Indicates if this node is a leaf node
        public bool leafNode = false;
        // The capacity/size that this node can handle
        public int size;

        // The top-left offset (in pixels) of this node in the 4096×4096 texture
        public int offsetX;
        public int offsetY;

        // The MeshCard object stored in this node (if it is a leaf node)
        public MeshCard card;

        /// <summary>
        /// Main constructor for the quadtree node.
        /// </summary>
        public SurfaceCacheQuadtreeNode(int size, int offsetX, int offsetY)
        {
            this.size = size;
            this.offsetX = offsetX;
            this.offsetY = offsetY;

            // If size is the smallest possible (e.g. 32), we treat it as a leaf node
            if (size <= 32)
            {
                leafNode = true;
            }
        }

        /// <summary>
        /// Attempts to place a MeshCard into this node (recursively).
        /// </summary>
        /// <param name="meshCard">The MeshCard to place.</param>
        /// <param name="targetSize">The required size for the MeshCard.</param>
        /// <returns>True if placement is successful, otherwise false.</returns>
        public bool AddNode(MeshCard meshCard, int targetSize)
        {

            // Case 1: If this node is a leaf, unoccupied, and exactly matches target size
            if (this.size == targetSize && IsEmpty())
            {
                this.card = meshCard;
                this.haveElement = true;
                this.full = true;

                // 把起始与结束坐标直接记录到 tileRect 上
                // 假设我们以 offsetX, offsetY 为左上角，nodeSize = targetSize，所以右下角就是 (offsetX+targetSize, offsetY+targetSize)
                meshCard.tileRect = new int4(
                    offsetX,
                    offsetY,
                    offsetX + targetSize,
                    offsetY + targetSize
                );
                // 同样更新 SurfaceCache 大小
                meshCard.parentSurfaceCache.assignedSize = targetSize;

                // 绑定 MeshCard 的 parentNode
                meshCard.parentNode = this;
                return true;
            }

            // Case 2: If the targetSize is smaller than this node's size, descend into children
            if (targetSize < this.size)
            {
                CreateChildrenIfNeeded();

                if (!node_Left_UP.full && node_Left_UP.AddNode(meshCard, targetSize))
                {
                    UpdateFullStatus();
                    return true;
                }
                if (!node_Right_Up.full && node_Right_Up.AddNode(meshCard, targetSize))
                {
                    UpdateFullStatus();
                    return true;
                }
                if (!node_Left_Down.full && node_Left_Down.AddNode(meshCard, targetSize))
                {
                    UpdateFullStatus();
                    return true;
                }
                if (!node_Right_Down.full && node_Right_Down.AddNode(meshCard, targetSize))
                {
                    UpdateFullStatus();
                    return true;
                }

                UpdateFullStatus();
                return false;
            }

            // If none of the above conditions are met, placement fails
            return false;
        }


        /// <summary>
        /// Removes the specified MeshCard from the node that contains it.
        /// </summary>
        /// <param name="meshCard">The MeshCard to remove.</param>
        /// <returns>True if removal succeeds, otherwise false.</returns>
        public bool RemoveNode(MeshCard meshCard)
        {
            // If this node holds the meshCard, remove it
            if (this.card == meshCard)
            {
                this.card = null;
                this.haveElement = false;
                this.full = false;
                meshCard.parentNode = null;

                // Update ancestors
                PropagateRemovalUp();
                return true;
            }

            // If it isn't here, we do nothing
            // (we assume direct removal by known parentNode, so no recursion needed)
            return false;
        }

        /// <summary>
        /// After removal, update this node's 'full' status and 
        /// attempt to merge children if they are empty.
        /// </summary>
        private void PropagateRemovalUp()
        {
            UpdateFullStatus();
            TryCleanUpChildren();

            // Move upward if there is a parent
            if (this.parent != null)
            {
                this.parent.PropagateRemovalUp();
            }
        }

        /// <summary>
        /// Creates four child nodes if they don't exist yet.
        /// Also sets their parent references and their offsets.
        /// </summary>
        private void CreateChildrenIfNeeded()
        {
            if (node_Left_UP != null) return;

            int childSize = this.size / 2;

            // Top-left child
            node_Left_UP = new SurfaceCacheQuadtreeNode(childSize, offsetX, offsetY)
            {
                parent = this
            };

            // Top-right child
            node_Right_Up = new SurfaceCacheQuadtreeNode(childSize, offsetX + childSize, offsetY)
            {
                parent = this
            };

            // Bottom-left child
            node_Left_Down = new SurfaceCacheQuadtreeNode(childSize, offsetX, offsetY + childSize)
            {
                parent = this
            };

            // Bottom-right child
            node_Right_Down = new SurfaceCacheQuadtreeNode(childSize, offsetX + childSize, offsetY + childSize)
            {
                parent = this
            };
        }

        /// <summary>
        /// Updates whether this node is full, based on whether it is a leaf with a card 
        /// or if all children are full.
        /// </summary>
        private void UpdateFullStatus()
        {
            // If this is a leaf node that has a MeshCard, it's full
            if (leafNode && haveElement)
            {
                full = true;
                return;
            }

            // If there are 4 children and they are all full, this node is also full
            if (node_Left_UP != null && node_Left_Down != null
                && node_Right_Up != null && node_Right_Down != null)
            {
                full = (node_Left_UP.full
                     && node_Left_Down.full
                     && node_Right_Up.full
                     && node_Right_Down.full);
            }
            else
            {
                // Otherwise, full depends on whether it holds an element
                full = haveElement;
            }
        }

        /// <summary>
        /// If all 4 children are empty/not-full, remove them and make this node a leaf again.
        /// </summary>
        private void TryCleanUpChildren()
        {
            // If children do not exist, nothing to clean
            if (node_Left_UP == null) return;

            // Check if all children are empty
            bool allChildrenEmpty = node_Left_UP.IsEmpty()
                                 && node_Left_Down.IsEmpty()
                                 && node_Right_Up.IsEmpty()
                                 && node_Right_Down.IsEmpty();

            // Check that none of them are full
            bool allChildrenNotFull = !node_Left_UP.full
                                   && !node_Left_Down.full
                                   && !node_Right_Up.full
                                   && !node_Right_Down.full;

            // If they're all empty and not full, remove them
            if (allChildrenEmpty && allChildrenNotFull)
            {
                node_Left_UP = null;
                node_Left_Down = null;
                node_Right_Up = null;
                node_Right_Down = null;
                leafNode = true;
            }
        }

        /// <summary>
        /// Checks if this node is empty. A leaf is empty if it does not hold a card.
        /// A non-leaf is empty if all children are empty and this node has no card.
        /// </summary>
        private bool IsEmpty()
        {
            if (leafNode)
                return !haveElement;

            // For a non-leaf, check if children are empty
            bool childrenEmpty = true;

            if (node_Left_UP != null && !node_Left_UP.IsEmpty()) childrenEmpty = false;
            if (node_Left_Down != null && !node_Left_Down.IsEmpty()) childrenEmpty = false;
            if (node_Right_Up != null && !node_Right_Up.IsEmpty()) childrenEmpty = false;
            if (node_Right_Down != null && !node_Right_Down.IsEmpty()) childrenEmpty = false;

            return !haveElement && childrenEmpty;
        }

        /// <summary>
        /// Static helper to directly remove a MeshCard by using meshCard.parentNode.
        /// </summary>
        public static bool RemoveMeshCard(MeshCard meshCard)
        {
            if (meshCard.parentNode == null)
            {
                // The card is not in the tree
                return false;
            }
            return meshCard.parentNode.RemoveNode(meshCard);
        }

    }

    public enum MaterialParamType
    {
        Float,
        Color,
        Vector,
        Texture,
        Int
    }
    [Serializable]
    public class MaterialPropertyMapping
    {
        public string sourceName;      // 原始材质的属性名（可能为空）
        public string targetName;      // 新材质的属性名（可能为空）
        public MaterialParamType paramType;  // 映射类型
    }
    [Serializable]
    public struct SurfaceCacheData
    {
        public half4 albedo;
        public half4 normal;
        public half4 emissive;
        public half4 radiosityAtlas;
        public half4 finalRadiosityAtlas;
        public float depth;
        public float metallic;
        public float smoothness;
        public float3 t;
    }
}