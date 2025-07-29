using System;
using System.Linq;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotonSystem
{
    [ExecuteInEditMode]
    public class PhotonObject : MonoBehaviour
    {
        public enum UpdateMode
        {
            OnChange,     // 变化更新
            Continuous,   // 持续更新
            Disabled      // 永不更新
        }
        public UpdateMode updateMode = UpdateMode.OnChange;
        #region SDF
        public bool enableContinuousSDFUpdate = false;
        public new Renderer renderer; // 直接引用 Renderer
        public MeshFilter meshFilter;
        public Mesh mesh;
        public Material[] materials;
        public BVHBounds bvhBounds; // 包围盒数据

        private Bounds lastBounds; // 缓存的实际包围盒
        private Bounds broadBounds; // 宽泛包围盒
        private Bounds narrowBounds; // 狭窄包围盒
        public float boundsScaleFactor = 1.01f; // 默认比例
        public SDFMeshTexture SDFMeshTexture { get { return sdfMeshTexture; } set { sdfMeshTexture = value; } }
        public int3 sdfTexSize;
        private SDFMeshTexture sdfMeshTexture;
        private bool refreshSurfaceCacheNextUpdate = false;
#if UNITY_EDITOR
        private void EnableReadWrite(Mesh mesh)
        {
            string path = AssetDatabase.GetAssetPath(mesh);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
        }
#endif
        private void Start()
        {

        }
        private void CheckHashChange()
        {
            if (mesh != meshFilter.sharedMesh || (materials.Length > 0 && !materials.SequenceEqual(renderer.sharedMaterials)) || refreshSurfaceCacheNextUpdate)
            {
                mesh = meshFilter.sharedMesh;
                materials = renderer.sharedMaterials;
                SurfaceCacheManager.Instance?.RemovePhotonObject(this);
                MeshManager.Instance.RemoveMesh(this);
                MeshManager.Instance.AddMesh(this);
                SurfaceCacheManager.Instance.AddPhotonObject(this);
                InitObjectHash();
            }
        }
        private void Update()
        {
            CheckHashChange();
            // 每帧检测包围盒是否发生变化
            if (renderer != null && renderer.bounds != lastBounds)
            {
                // 如果超出宽泛或小于狭窄包围盒
                if (!broadBounds.Contains(renderer.bounds.min) || !broadBounds.Contains(renderer.bounds.max) ||
                    narrowBounds.Contains(renderer.bounds.min) || narrowBounds.Contains(renderer.bounds.max))
                {
                    GlobalVoxelManager.Instance.MarkBoundsDirty(lastBounds);
                    // 更新宽泛和狭窄包围盒以及自身
                    UpdateBoundingBox();
                    GlobalVoxelManager.Instance.MarkBoundsDirty(lastBounds);
                    // 重新注册到 BVHManager
                    BVHManager.Instance.RemovePhotonObject(this);
                    BVHManager.Instance.AddPhotonObject(this);


                }
            }
            if (updateMode == UpdateMode.Continuous)
            {
                UpdateObject();
            }
            else if (updateMode == UpdateMode.OnChange)
            {

            }
            if (refreshSurfaceCacheNextUpdate)
            {
                refreshSurfaceCacheNextUpdate = false;
                UpdateObject();
            }
        }
        private void UpdateObject()
        {
            directLightSufaceCache.Refresh(directLightSufaceCache.assignedSize);
        }

        private void UpdateBoundingBox()
        {
            if (renderer != null)
            {
                Bounds bounds = renderer.bounds;

                bvhBounds.UpdateFromBounds(bounds);
                lastBounds = bounds; // 缓存当前包围盒

                // 初始化宽泛和狭窄包围盒
                UpdateBoundingBoxBounds(bounds);
            }
        }

        /// <summary>
        /// 根据当前包围盒更新宽泛和狭窄包围盒
        /// </summary>
        private void UpdateBoundingBoxBounds(Bounds currentBounds)
        {
            Vector3 size = currentBounds.size;
            Vector3 center = currentBounds.center;

            // 宽泛包围盒（扩大）
            broadBounds = new Bounds(center, size * boundsScaleFactor);

            // 狭窄包围盒（缩小）
            narrowBounds = new Bounds(center, size / boundsScaleFactor);
        }

        // 在选中物体时显示包围盒 Gizmos
        private void OnDrawGizmosSelected()
        {
            if (renderer != null && bvhBounds != null)
            {
                // 绘制实际包围盒
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(bvhBounds.center, bvhBounds.size);

                // 绘制宽泛包围盒
                Gizmos.color = Color.blue;
                Gizmos.DrawWireCube(broadBounds.center, broadBounds.size);

                // 绘制狭窄包围盒
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(narrowBounds.center, narrowBounds.size);
            }
            if (BVHManager.Instance.gizmosDebugAncestors)
            {
                BVHManager.Instance.DrawGizmosForObjectAncestors(this);
            }
        }
        private void Enable()
        {
            if (renderer == null)
            {
                renderer = GetTargetRenderer();
                materials = renderer.sharedMaterials;
            }
            if (meshFilter == null)
            {
                meshFilter = GetTargetMeshFilter();
                mesh = meshFilter.sharedMesh;
            }
            if (bvhBounds == null)
            {
                bvhBounds = new BVHBounds();
            }

#if UNITY_EDITOR
            if (AssetDatabase.Contains(mesh))
            {
                EnableReadWrite(mesh);
            }
#endif


            UpdateBoundingBox();
            InitObjectHash();

            MeshManager.Instance.AddMesh(this);
            BVHManager.Instance.AddPhotonObject(this);
            SurfaceCacheManager.Instance.AddPhotonObject(this);
        }
        private void OnEnable()
        {
            Enable();

        }
        private void Disable()
        {
            try
            {
                BVHManager.Instance?.RemovePhotonObject(this);

            }
            catch
            {

            }
            try
            {
                SurfaceCacheManager.Instance?.RemovePhotonObject(this);

            }
            catch
            {

            }
            try
            {
                MeshManager.Instance?.RemoveMesh(this);
            }
            catch
            {

            }
        }
        private void OnDisable()
        {
            Disable();

        }
        #endregion
        #region MeshCard
        public string objHashCode;
        private void InitObjectHash()
        {

            objHashCode = gameObject.GetInstanceID().ToString();
        }
        public string GetObjectHash()
        {
            return objHashCode;
        }

        public float GetMaxDimension()
        {
            return Mathf.Max(lastBounds.size.x, lastBounds.size.y, lastBounds.size.z);
        }

        public Vector3 GetBoundsCenter()
        {
            return lastBounds.center;
        }
        public void InitDirectLightSurfaceCacheIfNeeded()
        {
            if (directLightSufaceCache == null)
            {
                directLightSufaceCache = new SurfaceCache();
                directLightSufaceCache.Init(this);
                refreshSurfaceCacheNextUpdate = true;

            }
            directLightSufaceCache.Refresh(32);
        }
        public void RefreshObject()
        {
            refreshSurfaceCacheNextUpdate = true;
        }
        public SurfaceCache directLightSufaceCache;
        public SurfaceCache indirectLightSufaceCache;
        public Shader albedoShader;
        public Shader normalShader;
        public Shader emissiveShader;
        public Shader depthShader;
        public Shader metallicShader;
        public Shader smoothnessShader;
        public Renderer GetTargetRenderer()
        {
            return GetComponent<MeshRenderer>();
        }
        public MeshFilter GetTargetMeshFilter()
        {
            return GetComponent<MeshFilter>();
        }
        #endregion
    }

    public class SurfaceCache
    {
        // 6 MeshCards, typically for 6 faces or sub-surfaces
        public MeshCard[] meshCards = new MeshCard[6];

        // 与哪个 PhotonObject 关联
        public PhotonObject photonObject;

        // A stored "assigned size" that we update via ModifyNode
        public int assignedSize = 32;  // default to the minimum size

        /// <summary>
        /// 初始化：给 6 个 MeshCard 绑定父级引用
        /// </summary>
        public void Init(PhotonObject owner)
        {
            this.photonObject = owner;

            for (int i = 0; i < meshCards.Length; i++)
            {
                if (meshCards[i] == null)
                {
                    meshCards[i] = new MeshCard();
                }
                meshCards[i].Init(this);
            }
        }

        /// <summary>
        /// Refresh this SurfaceCache with the new size.
        /// 这里重点：为每个 MeshCard 分配4个RT，然后调用 DrawMeshCard 进行渲染
        /// </summary>
        public void Refresh(int newSize)
        {
            assignedSize = newSize;
            if (SurfaceCacheManager.Instance.surfaceCache == null)
            {
                SurfaceCacheManager.Instance.CreateSurfaceCacheBuffer();
            }
            // 拿到全局管理器
            var mgr = SurfaceCacheManager.Instance;
            if (mgr == null)
            {
                Debug.LogWarning("No SurfaceCacheManager found, cannot refresh properly.");
                return;
            }

            // 准备 Renderer
            Renderer targetRenderer = photonObject.renderer;
            if (targetRenderer == null)
            {
                Debug.LogWarning("PhotonObject has no valid Renderer to draw.");
                return;
            }

            // 拿到摄像机
            Camera bakeCam = mgr.bakeCamera;
            if (bakeCam == null)
            {
                Debug.LogWarning("No bakeCamera found in SurfaceCacheManager. Cannot draw mesh cards.");
                return;
            }
            Material originalMat = null;
            if (targetRenderer.sharedMaterials != null && targetRenderer.sharedMaterials.Length > 0)
            {
                originalMat = targetRenderer.sharedMaterials[0];
            }
            // 根据 PhotonObject 上的 Shader 优先使用，否则回退到 manager 默认
            Shader albedoShader = photonObject.albedoShader ? photonObject.albedoShader : mgr.defaultAlbedoShader;
            Shader normalShader = photonObject.normalShader ? photonObject.normalShader : mgr.defaultNormalShader;
            Shader emissiveShader = photonObject.emissiveShader ? photonObject.emissiveShader : mgr.defaultEmissiveShader;
            Shader depthShader = photonObject.depthShader ? photonObject.depthShader : mgr.defaultDepthShader;
            Shader smoothnessShader = photonObject.smoothnessShader ? photonObject.smoothnessShader : mgr.defaultSmoothnessShader;
            Shader metallicShader = photonObject.metallicShader ? photonObject.metallicShader : mgr.defaultMetallicShader;
            SurfaceMaterials surfaceMaterials = SurfaceCacheManager.Instance.GetSurfaceMaterials(
                photonObject,
                albedoShader,
                normalShader,
                emissiveShader,
                depthShader,
                smoothnessShader,
                metallicShader
            );

            // 分别创建材质
            Material albedoMat = surfaceMaterials.albedoMat;
            Material normalMat = surfaceMaterials.normalMat;
            Material emissiveMat = surfaceMaterials.emissiveMat;
            Material depthMat = surfaceMaterials.depthMat;
            Material smoothnessMat = surfaceMaterials.smoothnessMat;
            Material metallicMat = surfaceMaterials.metallicMat;
            // 生成一个cmd，让 6 个卡片的渲染都放在同一个 CommandBuffer
            CommandBuffer cmd = new CommandBuffer();
            cmd.name = "SurfaceCache_" + this.GetHashCode();

            if (originalMat != null)
            {
                mgr.ApplyMaterialMapping(originalMat, albedoMat);
                mgr.ApplyMaterialMapping(originalMat, normalMat);
                mgr.ApplyMaterialMapping(originalMat, emissiveMat);
                mgr.ApplyMaterialMapping(originalMat, depthMat);
                mgr.ApplyMaterialMapping(originalMat, smoothnessMat);
                mgr.ApplyMaterialMapping(originalMat, metallicMat);
            }
            Vector3 resolution = MeshSDFGenerator.CalculateResolutionFromBounds(photonObject.SDFMeshTexture.mesh.bounds, photonObject.SDFMeshTexture.maxResolution).resolution;
            Matrix4x4 sdfLocalToWorld = photonObject.SDFMeshTexture.GetWorldToSDFLocalMatrix(photonObject.transform).inverse;
            // 依次处理这 6 个 MeshCard
            for (int i = 0; i < meshCards.Length; i++)
            {
                MeshCard card = meshCards[i];
                card.AADirectionIndex = i;
                // 分配4个RT
                RenderTexture albedoRT = RTManager.Instance.GetRT(
                    $"AlbedoCardRT" + assignedSize,
                    assignedSize, assignedSize,
                    RenderTextureFormat.ARGBHalf
                );
                RenderTexture normalRT = RTManager.Instance.GetRT(
                    $"NormalCardRT" + assignedSize,
                    assignedSize, assignedSize,
                    RenderTextureFormat.ARGBHalf
                );
                RenderTexture emissiveRT = RTManager.Instance.GetRT(
                    $"EmissiveCardRT" + assignedSize,
                    assignedSize, assignedSize,
                    RenderTextureFormat.ARGBHalf
                );
                RenderTexture depthRT = RTManager.Instance.GetRT(
                    $"DepthCardRT" + assignedSize,
                    assignedSize, assignedSize,
                    RenderTextureFormat.RHalf
                );
                RenderTexture metallicRT = RTManager.Instance.GetRT(
                    $"MetallicCardRT" + assignedSize,
                    assignedSize, assignedSize,
                    RenderTextureFormat.RHalf
                );
                RenderTexture smoothnessRT = RTManager.Instance.GetRT(
                    $"SmoothnessCardRT" + assignedSize,
                    assignedSize, assignedSize,
                    RenderTextureFormat.RHalf
                );
                BakeCameraTransform(bakeCam, photonObject.mesh.bounds, photonObject.transform, card.AADirectionIndex, out float depth);
                Matrix4x4 viewMat = bakeCam.worldToCameraMatrix;
                Matrix4x4 sdfToLocalMatrix = viewMat * sdfLocalToWorld;
                Matrix4x4 sdfToProjectionMatrix = bakeCam.projectionMatrix * sdfToLocalMatrix;
                card.sdfToLocalMatrix = sdfToLocalMatrix;
                card.sdfToProjectionMatrix = sdfToProjectionMatrix;
                float deltaDepth = Mathf.Max(1f / Mathf.Abs(Vector3.Dot(GetDirection(card.AADirectionIndex), resolution)), 1f / assignedSize);

                card.deltaDepth = deltaDepth;
                cmd.SetGlobalMatrix("_ProjectionMatrix", sdfToProjectionMatrix * photonObject.transform.worldToLocalMatrix);
                // DrawMeshCard => 分别渲染4张
                if (albedoMat != null) DrawMeshCard(cmd, bakeCam, albedoMat, targetRenderer, albedoRT);
                if (normalMat != null) DrawMeshCard(cmd, bakeCam, normalMat, targetRenderer, normalRT);
                if (emissiveMat != null) DrawMeshCard(cmd, bakeCam, emissiveMat, targetRenderer, emissiveRT);
                if (depthMat != null) DrawMeshCard(cmd, bakeCam, depthMat, targetRenderer, depthRT);
                if (metallicMat != null) DrawMeshCard(cmd, bakeCam, metallicMat, targetRenderer, metallicRT);
                if (smoothnessMat != null) DrawMeshCard(cmd, bakeCam, smoothnessMat, targetRenderer, smoothnessRT);
                RTHelper.Instance.CopyRTToSurfaceCacheWithOffset(cmd, albedoRT, SurfaceCacheManager.Instance.surfaceCache, ToVector2Int(card.tileRect), SurfaceCacheManager.size, SurfaceCacheWriteType.Albedo);
                RTHelper.Instance.CopyRTToSurfaceCacheWithOffset(cmd, normalRT, SurfaceCacheManager.Instance.surfaceCache, ToVector2Int(card.tileRect), SurfaceCacheManager.size, SurfaceCacheWriteType.Normal);
                RTHelper.Instance.CopyRTToSurfaceCacheWithOffset(cmd, emissiveRT, SurfaceCacheManager.Instance.surfaceCache, ToVector2Int(card.tileRect), SurfaceCacheManager.size, SurfaceCacheWriteType.Emissive);
                RTHelper.Instance.CopyRTToSurfaceCacheWithOffset(cmd, depthRT, SurfaceCacheManager.Instance.surfaceCache, ToVector2Int(card.tileRect), SurfaceCacheManager.size, SurfaceCacheWriteType.Depth);
                RTHelper.Instance.CopyRTToSurfaceCacheWithOffset(cmd, metallicRT, SurfaceCacheManager.Instance.surfaceCache, ToVector2Int(card.tileRect), SurfaceCacheManager.size, SurfaceCacheWriteType.Metallic);
                RTHelper.Instance.CopyRTToSurfaceCacheWithOffset(cmd, smoothnessRT, SurfaceCacheManager.Instance.surfaceCache, ToVector2Int(card.tileRect), SurfaceCacheManager.size, SurfaceCacheWriteType.Smoothness);
                RTHelper.Instance.CopyRTToSurfaceCacheWithOffset(cmd, smoothnessRT, SurfaceCacheManager.Instance.surfaceCache, ToVector2Int(card.tileRect), SurfaceCacheManager.size, SurfaceCacheWriteType.RadiosityAtlas);
            }

            // 提交命令
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();


        }
        public static Vector2Int ToVector2Int(int4 value)
        {
            return new Vector2Int(value.x, value.y);
        }
        public Vector3 GetDirection(int directionIndex)
        {
            Vector3 lookDir;
            switch (directionIndex)
            {
                case 0: // +X
                    lookDir = Vector3.right;
                    break;
                case 1: // -X
                    lookDir = Vector3.left;

                    break;
                case 2: // +Y
                    lookDir = Vector3.up;

                    break;
                case 3: // -Y
                    lookDir = Vector3.down;

                    break;
                case 4: // +Z
                    lookDir = Vector3.forward;

                    break;
                case 5: // -Z
                    lookDir = Vector3.back;

                    break;
                default:
                    Debug.LogWarning($"Unexpected directionIndex={directionIndex}, defaulting to +Z");
                    lookDir = Vector3.forward;
                    break;
            }
            return lookDir;
        }
        public void BakeCameraTransform(Camera cam, Bounds localBounds, Transform meshTransform, int directionIndex, out float depth)
        {
            // 1) 根据包围盒信息，计算中心 & 半径
            Vector3 center = localBounds.center;
            Vector3 extents = localBounds.extents;
            float distance = 0;

            // 2) 确定相机朝向(lookDir)与上向量(upDir)
            Vector3 lookDir, upDir;
            switch (directionIndex)
            {
                case 0: // +X
                    lookDir = Vector3.right;
                    upDir = Vector3.up;
                    distance = localBounds.size.x / 2f;
                    break;
                case 1: // -X
                    lookDir = Vector3.left;
                    upDir = Vector3.up;
                    distance = localBounds.size.x / 2f;
                    break;
                case 2: // +Y
                    lookDir = Vector3.up;
                    upDir = Vector3.back;
                    distance = localBounds.size.y / 2f;
                    break;
                case 3: // -Y
                    lookDir = Vector3.down;
                    upDir = Vector3.forward;
                    distance = localBounds.size.y / 2f;
                    break;
                case 4: // +Z
                    lookDir = Vector3.forward;
                    upDir = Vector3.up;
                    distance = localBounds.size.z / 2f;
                    break;
                case 5: // -Z
                    lookDir = Vector3.back;
                    upDir = Vector3.up;
                    distance = localBounds.size.z / 2f;
                    break;
                default:
                    Debug.LogWarning($"Unexpected directionIndex={directionIndex}, defaulting to +Z");
                    lookDir = Vector3.forward;
                    upDir = Vector3.up;
                    break;
            }
            distance *= 1.0001f;//offest
            cam.transform.position = meshTransform.TransformPoint(center - lookDir * distance);
            cam.transform.rotation = Quaternion.LookRotation(
                meshTransform.TransformDirection(lookDir),
                meshTransform.TransformDirection(upDir)
            );
            depth = distance * 2;
            Vector3 farPoint = meshTransform.TransformPoint(center + lookDir * distance);
            cam.farClipPlane = (cam.transform.position - farPoint).magnitude;
            cam.nearClipPlane = 0;

            Vector3[] localCorners = new Vector3[8];
            localCorners[0] = center + new Vector3(+extents.x, +extents.y, +extents.z);
            localCorners[1] = center + new Vector3(+extents.x, +extents.y, -extents.z);
            localCorners[2] = center + new Vector3(+extents.x, -extents.y, +extents.z);
            localCorners[3] = center + new Vector3(+extents.x, -extents.y, -extents.z);
            localCorners[4] = center + new Vector3(-extents.x, +extents.y, +extents.z);
            localCorners[5] = center + new Vector3(-extents.x, +extents.y, -extents.z);
            localCorners[6] = center + new Vector3(-extents.x, -extents.y, +extents.z);
            localCorners[7] = center + new Vector3(-extents.x, -extents.y, -extents.z);


            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            Matrix4x4 w2c = cam.worldToCameraMatrix;

            for (int i = 0; i < 8; i++)
            {
                Vector3 worldPos = meshTransform.TransformPoint(localCorners[i]);
                Vector3 camPos = w2c.MultiplyPoint(worldPos);
                minX = Mathf.Min(camPos.x, minX);
                maxX = Mathf.Max(camPos.x, maxX);
                minY = Mathf.Min(camPos.y, minY);
                maxY = Mathf.Max(camPos.y, maxY);
            }

            float widthInCam = maxX - minX;
            float heightInCam = maxY - minY;
            float aspect = (heightInCam > 0.0001f) ? (widthInCam / heightInCam) : 1.0f;

            float sizeNeeded = heightInCam * 0.5f;

            cam.orthographic = true;
            cam.orthographicSize = sizeNeeded;

            cam.aspect = aspect;
        }
        /// <summary>
        /// 将 targetRenderer 渲染到 targetRT 的通用逻辑。由外部决定用什么材质、渲染到哪个RT。
        /// </summary>
        public void DrawMeshCard(CommandBuffer cmd, Camera bakeCam, Material mat, Renderer targetRenderer, RenderTexture targetRT)
        {
            // 切换RenderTarget
            cmd.SetRenderTarget(targetRT);
            cmd.ClearRenderTarget(true, true, Color.clear);

            // 设置视图投影矩阵
            cmd.SetViewProjectionMatrices(bakeCam.worldToCameraMatrix, bakeCam.projectionMatrix);

            // DrawRenderer: submesh=0, pass=-1(全部Pass) 或指定pass
            cmd.DrawRenderer(targetRenderer, mat, 0, -1);

            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        }
    }
    public class MeshCard
    {
        // 每个卡片都有方向索引 (0=+x,1=-x,2=+y,3=-y,4=+z,5=-z)
        public int AADirectionIndex = 0;
        public Matrix4x4 sdfToLocalMatrix;
        public Matrix4x4 sdfToProjectionMatrix;
        public float deltaDepth;


        // int4: (offsetX, offsetY, endX, endY) 如果有需要，也可以在分配时赋值
        public int4 tileRect;

        public SurfaceCacheQuadtreeNode parentNode;
        public SurfaceCache parentSurfaceCache;

        public void Init(SurfaceCache parent)
        {
            this.parentSurfaceCache = parent;
            // 如果需要更多初始化，写在这里
        }
    }
    public class SurfaceMaterials
    {
        public Material albedoMat;
        public Material normalMat;
        public Material emissiveMat;
        public Material depthMat;
        public Material smoothnessMat;
        public Material metallicMat;

        /// <summary>
        /// 根据各个 shader 创建材质实例（shader 不为 null 时创建材质，否则保持 null）
        /// </summary>
        public SurfaceMaterials(Shader albedoShader, Shader normalShader, Shader emissiveShader,
                                Shader depthShader, Shader smoothnessShader, Shader metallicShader)
        {
            albedoMat = albedoShader != null ? new Material(albedoShader) : null;
            normalMat = normalShader != null ? new Material(normalShader) : null;
            emissiveMat = emissiveShader != null ? new Material(emissiveShader) : null;
            depthMat = depthShader != null ? new Material(depthShader) : null;
            smoothnessMat = smoothnessShader != null ? new Material(smoothnessShader) : null;
            metallicMat = metallicShader != null ? new Material(metallicShader) : null;
        }
    }

}