using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static PhotonSystem.VoxelBlockUpdateTrackerWithFace;
namespace PhotonSystem
{
    public static class VoxelBlockUpdateTrackerWithFace
    {
        public enum FaceAxis { X, Y, Z }

        public struct VoxelBlockFace
        {
            public FaceAxis Axis;
            public int StartIndex;
            public int Depth;
            public VoxelBlockFace(FaceAxis axis, int startIndex, int depth)
            {
                Axis = axis;
                StartIndex = startIndex;
                Depth = depth;
            }
        }

        public struct VoxelBlock
        {
            public Vector3Int Index;
            public bool Dirty;
            public VoxelBlock(Vector3Int idx, bool dirty)
            {
                Index = idx;
                Dirty = dirty;
            }
        }

        public struct DirtyBlockRequest
        {
            public Vector3Int BlockIndex;
            public Vector3 BlockWorldPos;
            public float BlockWorldSize;
            public Quaternion CameraRotation;
            public Vector3 CameraPosition;
            public float FarClipPlane;
            public float OrthographicSize;

            public void ConfigureCamera(Camera cam)
            {
                cam.transform.rotation = CameraRotation;
                cam.transform.position = CameraPosition;
                cam.orthographic = true;
                cam.nearClipPlane = 0f;
                cam.farClipPlane = FarClipPlane;
                cam.orthographicSize = OrthographicSize;
            }

            public void ConfigureViewPoints(GameObject leftViewPoint, GameObject topViewPoint)
            {
                float half = BlockWorldSize * 0.5f;
                leftViewPoint.transform.position = BlockWorldPos - Vector3.right * half;
                leftViewPoint.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                topViewPoint.transform.position = BlockWorldPos + Vector3.up * half;
                topViewPoint.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        public class Tracker
        {
            private readonly int blocksPerAxis;
            private readonly int totalCount;
            private readonly bool[] flags;
            private readonly Vector3Int[] indexTable;
            private readonly List<Vector3Int> dirtyList;
            private readonly List<VoxelBlock> resultList;
            public Vector3 moveMotion;
            private Vector3 worldPos;    // 当前网格原点（可变）
            private readonly float worldSize;
            private readonly float blockSize;

            /// <summary>
            /// 构造：传入 blocksPerAxis, 初始 worldPos, worldSize
            /// </summary>
            public Tracker(int blocksPerAxis, Vector3 initialWorldPos, float worldSize)
            {
                if (blocksPerAxis <= 0) throw new ArgumentException("blocksPerAxis must be positive.");
                this.blocksPerAxis = blocksPerAxis;
                this.worldPos = initialWorldPos;
                this.worldSize = worldSize;
                this.blockSize = worldSize / blocksPerAxis;

                totalCount = blocksPerAxis * blocksPerAxis * blocksPerAxis;
                flags = new bool[totalCount];
                indexTable = new Vector3Int[totalCount];
                dirtyList = new List<Vector3Int>(totalCount);
                resultList = new List<VoxelBlock>(totalCount);

                for (int i = 0; i < totalCount; i++)
                {
                    int z = i / (blocksPerAxis * blocksPerAxis);
                    int rem = i - z * blocksPerAxis * blocksPerAxis;
                    int y = rem / blocksPerAxis;
                    int x = rem - y * blocksPerAxis;
                    indexTable[i] = new Vector3Int(x, y, z);
                }
                // 初始全脏
                MarkFaceDirty(new VoxelBlockFace(FaceAxis.X, 0, blocksPerAxis));
            }
            public void MarkBoundsDirty(Bounds worldBounds)
            {
                Vector3 gridMin = worldPos - Vector3.one * (worldSize * 0.5f);
                Vector3 gridMax = worldPos + Vector3.one * (worldSize * 0.5f);

                if (worldBounds.max.x < gridMin.x || worldBounds.min.x > gridMax.x ||
                    worldBounds.max.y < gridMin.y || worldBounds.min.y > gridMax.y ||
                    worldBounds.max.z < gridMin.z || worldBounds.min.z > gridMax.z)
                    return;

                Vector3 clippedMin = Vector3.Max(worldBounds.min, gridMin);
                Vector3 clippedMax = Vector3.Min(worldBounds.max, gridMax);

                Vector3Int minId = new Vector3Int(
                    Mathf.Clamp(Mathf.FloorToInt((clippedMin.x - gridMin.x) / blockSize), 0, blocksPerAxis - 1),
                    Mathf.Clamp(Mathf.FloorToInt((clippedMin.y - gridMin.y) / blockSize), 0, blocksPerAxis - 1),
                    Mathf.Clamp(Mathf.FloorToInt((clippedMin.z - gridMin.z) / blockSize), 0, blocksPerAxis - 1)
                );

                Vector3Int maxId = new Vector3Int(
                    Mathf.Clamp(Mathf.CeilToInt((clippedMax.x - gridMin.x) / blockSize), 0, blocksPerAxis - 1),
                    Mathf.Clamp(Mathf.CeilToInt((clippedMax.y - gridMin.y) / blockSize), 0, blocksPerAxis - 1),
                    Mathf.Clamp(Mathf.CeilToInt((clippedMax.z - gridMin.z) / blockSize), 0, blocksPerAxis - 1)
                );

                for (int z = minId.z; z <= maxId.z; z++)
                    for (int y = minId.y; y <= maxId.y; y++)
                        for (int x = minId.x; x <= maxId.x; x++)
                            EnqueueDirty(x, y, z);
            }
            private int ToFlat(int x, int y, int z)
                => x + y * blocksPerAxis + z * blocksPerAxis * blocksPerAxis;

            public void MarkFaceDirty(VoxelBlockFace face)
            {
                for (int d = 0; d < face.Depth; d++)
                {
                    int idx = face.StartIndex + d;
                    switch (face.Axis)
                    {
                        case FaceAxis.X:
                            for (int y = 0; y < blocksPerAxis; y++)
                                for (int z = 0; z < blocksPerAxis; z++)
                                    EnqueueDirty(idx, y, z);
                            break;
                        case FaceAxis.Y:
                            for (int x = 0; x < blocksPerAxis; x++)
                                for (int z = 0; z < blocksPerAxis; z++)
                                    EnqueueDirty(x, idx, z);
                            break;
                        case FaceAxis.Z:
                            for (int x = 0; x < blocksPerAxis; x++)
                                for (int y = 0; y < blocksPerAxis; y++)
                                    EnqueueDirty(x, y, idx);
                            break;
                    }
                }
            }

            public void ShiftAndMark(Vector3Int offset)
            {
                int dx = offset.x, dy = offset.y, dz = offset.z;
                int absX = Math.Abs(dx), absY = Math.Abs(dy), absZ = Math.Abs(dz);
                if (dx != 0) MarkFaceDirty(new VoxelBlockFace(FaceAxis.X, dx < 0 ? 0 : blocksPerAxis - absX, absX));
                if (dy != 0) MarkFaceDirty(new VoxelBlockFace(FaceAxis.Y, dy < 0 ? 0 : blocksPerAxis - absY, absY));
                if (dz != 0) MarkFaceDirty(new VoxelBlockFace(FaceAxis.Z, dz < 0 ? 0 : blocksPerAxis - absZ, absZ));
            }

            private void EnqueueDirty(int x, int y, int z)
            {
                try
                {
                    int flat = Math.Clamp(ToFlat(x, y, z), 0, blocksPerAxis * blocksPerAxis * blocksPerAxis);
                    if (!flags[flat])
                    {
                        flags[flat] = true;
                        dirtyList.Add(indexTable[flat]);
                    }
                }
                catch
                {

                }

            }

            public VoxelBlock[] GetDirtyBlocks()
            {
                resultList.Clear();
                foreach (var pos in dirtyList)
                    resultList.Add(new VoxelBlock(pos, true));
                return resultList.ToArray();
            }

            public void ClearAllDirty()
            {
                Array.Clear(flags, 0, totalCount);
                dirtyList.Clear();
            }

            /// <summary>
            /// 将网格原点移动到 newWorldPos，自动对齐到 blockSize 网格，计算 ShiftAndMark 并应用
            /// </summary>
            public void MoveToPos(Vector3 newWorldPos)
            {
                // 对齐到 blockSize 网格
                float blockSizeWorld = worldSize / blocksPerAxis;
                Vector3 aligned = new Vector3(
                    Mathf.Round(newWorldPos.x / blockSizeWorld) * blockSizeWorld,
                    Mathf.Round(newWorldPos.y / blockSizeWorld) * blockSizeWorld,
                    Mathf.Round(newWorldPos.z / blockSizeWorld) * blockSizeWorld);
                // 计算块偏移
                Vector3 delta = aligned - worldPos;
                Vector3Int blockOffset = new Vector3Int(
                    Mathf.RoundToInt(delta.x / blockSizeWorld),
                    Mathf.RoundToInt(delta.y / blockSizeWorld),
                    Mathf.RoundToInt(delta.z / blockSizeWorld));

                moveMotion = blockOffset * GlobalVoxelManager.Instance.VoxelBlockLength;
                // 应用位移更新
                ShiftAndMark(blockOffset);
                // 更新原点
                worldPos = aligned;
            }

            public DirtyBlockRequest[] GetUpdateRequests()
            {
                var blocks = GetDirtyBlocks();
                var requests = new DirtyBlockRequest[blocks.Length];
                for (int i = 0; i < blocks.Length; i++)
                {
                    var b = blocks[i];
                    Vector3Int idx = b.Index;
                    Vector3 center = worldPos + new Vector3(
                        (b.Index.x - blocksPerAxis * 0.5f + 0.5f) * blockSize,
                        (b.Index.y - blocksPerAxis * 0.5f + 0.5f) * blockSize,
                        (b.Index.z - blocksPerAxis * 0.5f + 0.5f) * blockSize);
                    requests[i] = new DirtyBlockRequest
                    {
                        BlockIndex = idx,
                        BlockWorldPos = center,
                        BlockWorldSize = blockSize,
                        CameraRotation = Quaternion.identity,
                        CameraPosition = center - Vector3.forward * (blockSize * 0.5f),
                        FarClipPlane = blockSize,
                        OrthographicSize = blockSize * 0.5f
                    };
                }
                return requests;
            }
        }
    }
    [Serializable]
    public struct GlobalRadiacneProbes
    {
        public float3 c0;
        public float3 c1;
        public float3 c2;
        public float3 c3;
        public float3 c4;
        public float3 c5;
        public float3 c6;
        public float3 c7;
        public float3 c8;
        public float3 worldPos;
        public int enable;
    }

    [Serializable]
    public struct GlobalVoxel
    {
        public uint AlbedoFront;
        public uint AlbedoBack;
        public uint Normal;
        public uint RadiosityAtlas;
        public uint FinalRadiosityAtlas;
        public uint FullVoxel;
    }
    public enum VoxelSize : int
    {
        Size_32 = 32,
        Size_64 = 64,
        Size_128 = 128
    }
    public enum VoxelBlockSize : int
    {
        Size_4 = 4,
        Size_8 = 8,
        Size_16 = 16,
    }
    public class GlobalVoxelManager : PhotonSingleton<GlobalVoxelManager>, IRadianceFeature
    {
        public int MipCount
        {
            get
            {
                return (int)Mathf.Log(VoxelLength, 2);
            }
        }
        public bool cameraRecordDebug = false;
        public ComputeShader globalVoxelCompute;
        public Shader voxelShader;
        public Shader sunDepthShader;
        private Material voxelMaterial;
        public float voxelSpaceLevel0Size = 20f;
        public bool stopInitVoxel;
        [Range(4, 12)]
        public int maxLevel = 8;
        [Range(1,8)]
        public int shadowCamIncreaseLevel = 5;
        public VoxelSize voxelLength = VoxelSize.Size_32;
        public Tracker[] trackers;
        public int VoxelLength
        {
            get
            {
                return (int)voxelLength;
            }
        }
        public VoxelBlockSize voxelBlockSize = VoxelBlockSize.Size_8;
        public int VoxelBlockLength
        {
            get
            {
                return (int)voxelBlockSize;
            }
        }
        public int BlocksPerAxis
        {
            get
            {
                return VoxelLength / VoxelBlockLength;
            }
        }

        [Range(1, 20)]
        public int iterationCount = 9;
        [Range(0.0f, 4.0f)]
        public float secondaryBounceGain = 1.0f;

        [Range(0, 2)]
        public int innerOcclusionLayers = 1;
        [Range(1,30)]
        public int coneCount = 16;
        [Range(1, 30)]
        public int coneSteps = 20;
        [Range(1, 60)]
        public int coneAngle = 20;
        public bool infiniteBounces = true;

        public LayerMask giCullingMask = 2147483647;
        private ComputeBuffer globalVoxelBuffer;
        private Camera voxelCamera;
        private Camera shadowCam;
        private RenderTexture tempSliceRT;
        private RenderTexture tempVoxelRTFront;
        private RenderTexture tempVoxelRTBack;
        private RenderTexture tempVoxelRTNormal;
        private RenderTexture tempVoxelRTRadiosityAtlas;

        private RenderTexture[] tempVoxelSecRTs;
        private RenderTexture voxelAlbedoRT;
        private RenderTexture sunDepthTexture;
        public Light sun;
        public Vector3 centerPos;
        Quaternion rotationFront = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);
        Quaternion rotationLeft = new Quaternion(0.0f, 0.7f, 0.0f, 0.7f);
        Quaternion rotationTop = new Quaternion(0.7f, 0.0f, 0.0f, 0.7f);
        int sunShadowResolution = 256;
        public Transform followTransform;
        public ComputeBuffer GlobalVoxelBuffer { get { return globalVoxelBuffer; } }
        public float[] levelOffsets;
        public float[] mipOffsets;
        public float[] voxelSpaceOriginFloats;
        private Vector3[] previousVoxelSpaceOrigins;
        private Vector3[] voxelSpaceOrigins;
        private Vector3[] voxelSpaceOriginDeltas;
        //---------------------------------RadianceProbes---------------------------------
        public ComputeBuffer globalRadiacneProbes;
        private ComputeBuffer globalRadiacneProbesTemp;

        private struct CameraInfo
        {
            public Vector3 position;
            public Quaternion rotation;
            public float orthographicSize;
            public float nearClip;
            public float farClip;
            public string name;         
        }
        private List<CameraInfo> recordedCameras = new List<CameraInfo>();
        private int GetGlobalProbesBufferCount()
        {
            return BlocksPerAxis * BlocksPerAxis * BlocksPerAxis * maxLevel;
        }
        private void InitGlobalProbes()
        {
            globalRadiacneProbes?.Release();
            globalRadiacneProbes?.Dispose();
            globalRadiacneProbes = new ComputeBuffer(GetGlobalProbesBufferCount(), Marshal.SizeOf<GlobalRadiacneProbes>());
            globalRadiacneProbesTemp?.Release();
            globalRadiacneProbesTemp?.Dispose();
            globalRadiacneProbesTemp = new ComputeBuffer(GetGlobalProbesBufferCount(), Marshal.SizeOf<GlobalRadiacneProbes>());
            
        }
        public void MarkBoundsDirty(Bounds worldBounds)
        {
            foreach (var tracker in trackers)
            {
                tracker.MarkBoundsDirty(worldBounds);
            }
        }
        public float GetVoxelSpaceSize(int level)
        {
            return voxelSpaceLevel0Size * Mathf.Pow(2, level);
        }
        public void OnEnable()
        {
            Init();
        }
        public void SetCSData(CommandBuffer cmd, ComputeShader computeShader, int kernel, Camera camera)
        {
            cmd.SetComputeBufferParam(computeShader, kernel, "_VoxelBuffer", globalVoxelBuffer);
            cmd.SetComputeBufferParam(computeShader, kernel, "_GlobalRadiacneProbes", globalRadiacneProbes);
            SetInputData(cmd, computeShader, kernel);
            cmd.SetComputeIntParam(computeShader, "_VoxelLength", VoxelLength);
            cmd.SetComputeIntParam(computeShader, "_MaxLevel", maxLevel);
            cmd.SetComputeIntParam(computeShader, "_ConeCount", coneCount);
            cmd.SetComputeIntParam(computeShader, "_ConeSteps", coneSteps);
            cmd.SetComputeIntParam(computeShader, "_BlocksPerAxis", BlocksPerAxis);
            cmd.SetComputeIntParam(computeShader, "_BlockSize", VoxelBlockLength);
            cmd.SetComputeIntParam(computeShader, "_BlockLodLevel", (int)Mathf.Log(BlocksPerAxis, 2));
            cmd.SetComputeFloatParams(computeShader, "_ConeAngle", coneAngle * Mathf.PI / 180);
            cmd.SetComputeFloatParams(computeShader, "_LevelOffsets", levelOffsets);
            cmd.SetComputeFloatParams(computeShader, "_VoxelScaleFactor", VoxelLength / 64f);
            cmd.SetComputeFloatParams(computeShader, "_MipOffsets", mipOffsets);
            cmd.SetComputeFloatParams(computeShader, "_VoxelSpaceOriginFloats", voxelSpaceOriginFloats);
            float voxelBaseDeltaSize = voxelSpaceLevel0Size / BlocksPerAxis;
            cmd.SetComputeFloatParam(computeShader, "_VoxelSafeSize", voxelSpaceLevel0Size - 2 * voxelBaseDeltaSize);
            cmd.SetComputeFloatParam(computeShader, "_VoxelBaseDeltaSize", voxelBaseDeltaSize);
            cmd.SetComputeFloatParam(computeShader, "_VoxelSize", voxelSpaceLevel0Size);
            cmd.SetComputeFloatParam(computeShader, "_TimeSeed", Time.time);
            cmd.SetComputeVectorParam(computeShader, "_CenterPosition", centerPos);
            cmd.SetComputeVectorParam(computeShader, "_CameraDirection", camera.transform.forward);

        }
        private void SetInputData(CommandBuffer cmd, ComputeShader computeShader, int kernel)
        {
            cmd.SetComputeTextureParam(computeShader, kernel, "_VoxelAlbedoRT", voxelAlbedoRT);
        }
        public static RenderTexture CreateVoxelRT(
            int voxelLength,
            RenderTextureFormat format = RenderTextureFormat.RInt,
            RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear,
            FilterMode filterMode = FilterMode.Point,
            bool enableRandomWrite = true,
            HideFlags hideFlags = HideFlags.HideAndDontSave)
        {
            RenderTexture voxelRT = new RenderTexture(voxelLength, voxelLength, 0, format, readWrite)
            {
                dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
                volumeDepth = voxelLength,
                enableRandomWrite = enableRandomWrite,
                filterMode = filterMode,
                hideFlags = hideFlags
            };

            voxelRT.Create();
            return voxelRT;
        }
        public static RenderTexture CreateVoxelRT(
            int3 voxelSize,
            RenderTextureFormat format = RenderTextureFormat.RInt,
            RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear,
            FilterMode filterMode = FilterMode.Point,
            bool enableRandomWrite = true,
            HideFlags hideFlags = HideFlags.HideAndDontSave,
            bool useMipMap = false,
            bool autoGenerateMips = false
            )
        {
            RenderTexture voxelRT = new RenderTexture(voxelSize.x, voxelSize.y, 0, format, readWrite)
            {
                dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
                volumeDepth = voxelSize.z,
                enableRandomWrite = enableRandomWrite,
                filterMode = filterMode,
                hideFlags = hideFlags,
                useMipMap = useMipMap,
                autoGenerateMips = autoGenerateMips,
            };

            voxelRT.Create();
            return voxelRT;
        }
        public void Init()
        {
            // 创建或获取体素相机
            if (voxelCamera == null)
            {
                voxelCamera = new GameObject("VoxelCamera").AddComponent<Camera>();
                float initSize = GetVoxelSpaceSize(0);
                voxelCamera.transform.parent = transform;
                voxelCamera.enabled = false;
                voxelCamera.orthographic = true;
                voxelCamera.orthographicSize = initSize * 0.5f;
                voxelCamera.aspect = 1f;
                voxelCamera.nearClipPlane = 0f;
                voxelCamera.farClipPlane = initSize;
                voxelCamera.clearFlags = CameraClearFlags.Color;
                voxelCamera.backgroundColor = Color.black;
                voxelCamera.useOcclusionCulling = false;
                voxelCamera.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
            // 创建或获取阴影相机
            if (shadowCam == null)
            {
                shadowCam = new GameObject("ShadowCam").AddComponent<Camera>();
                float initSize = GetVoxelSpaceSize(0);
                shadowCam.transform.parent = transform;
                shadowCam.enabled = false;
                shadowCam.orthographic = true;
                shadowCam.orthographicSize = initSize * 0.5f;
                shadowCam.aspect = 1f;
                shadowCam.nearClipPlane = 0f;
                shadowCam.farClipPlane = initSize;
                shadowCam.clearFlags = CameraClearFlags.Color;
                shadowCam.backgroundColor = Color.black;
                shadowCam.useOcclusionCulling = false;
                shadowCam.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }

            // 预分配临时 RT 和 Buffer
            tempVoxelSecRTs = new RenderTexture[maxLevel];
            int totalVoxelCount = PrecomputeOffsets();
            globalVoxelBuffer = new ComputeBuffer(totalVoxelCount, Marshal.SizeOf<GlobalVoxel>(), ComputeBufferType.Structured);
            tempSliceRT = new RenderTexture(VoxelBlockLength, VoxelBlockLength, 0, RenderTextureFormat.ARGB32)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            tempSliceRT.Create();

            tempVoxelRTFront = CreateVoxelRT(VoxelLength);
            tempVoxelRTBack = CreateVoxelRT(VoxelLength);
            tempVoxelRTNormal = CreateVoxelRT(VoxelLength);
            tempVoxelRTRadiosityAtlas = CreateVoxelRT(VoxelLength);
            for (int i = 0; i < maxLevel; i++)
                tempVoxelSecRTs[i] = CreateVoxelRT(VoxelLength, format: RenderTextureFormat.ARGBHalf, filterMode: FilterMode.Trilinear);

            voxelAlbedoRT = CreateVoxelRT(new int3(VoxelLength, VoxelLength, VoxelLength * maxLevel),
                                          format: RenderTextureFormat.ARGBHalf, filterMode: FilterMode.Trilinear,
                                          useMipMap: true);
            sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            sunDepthTexture.Create();

            // 准备体素空间起点数据
            previousVoxelSpaceOrigins = new Vector3[maxLevel];
            voxelSpaceOrigins = new Vector3[maxLevel];
            voxelSpaceOriginDeltas = new Vector3[maxLevel];
            voxelMaterial = new Material(voxelShader);

            // 初始化 Tracker 数组
            trackers = new VoxelBlockUpdateTrackerWithFace.Tracker[maxLevel];
            // 计算初始体素空间原点
            InputVoxelSpaceOriginDeltas();
            for (int level = 0; level < maxLevel; level++)
            {
                float size = GetVoxelSpaceSize(level);
                Vector3 origin = voxelSpaceOrigins[level];
                trackers[level] = new VoxelBlockUpdateTrackerWithFace.Tracker(BlocksPerAxis, origin, size);
            }
            InitGlobalProbes();
            RenderPipelineManager.beginCameraRendering += RenderingVoxel;
        }

        private void InputVoxelSpaceOriginDeltas()
        {
            voxelSpaceOriginFloats = new float[48];
            Array.Fill(voxelSpaceOriginFloats, 0);
            for (int level = 0; level < maxLevel; level++)
            {
                Vector3 origin;
                if (followTransform)
                {
                    origin = followTransform.position;
                }
                else
                {
                    origin = centerPos;
                }
                float voxelSpaceSize = GetVoxelSpaceSize(level);
                float deltaLength = voxelSpaceSize * VoxelBlockLength / VoxelLength;
                voxelSpaceOrigins[level] = new Vector3(Mathf.Floor(origin.x / deltaLength) * deltaLength, Mathf.Floor(origin.y / deltaLength) * deltaLength, Mathf.Floor(origin.z / deltaLength) * deltaLength);
                //voxelSpaceOrigins[level] = origin;
                voxelSpaceOriginDeltas[level] = voxelSpaceOrigins[level] - previousVoxelSpaceOrigins[level];
            }
            for (int m = 0; m < maxLevel; m++)
            {
                voxelSpaceOriginFloats[m * 4] = voxelSpaceOrigins[m].x;
                voxelSpaceOriginFloats[m * 4 + 1] = voxelSpaceOrigins[m].y;
                voxelSpaceOriginFloats[m * 4 + 2] = voxelSpaceOrigins[m].z;
            }
        }
        private int PrecomputeOffsets()
        {
            int mipCount = MipCount;
            mipOffsets = new float[48];
            Array.Fill(mipOffsets, 0);
            int running = 0;
            for (int m = 0; m < mipCount; m++)
            {
                mipOffsets[m * 4] = running;
                int dim = VoxelLength >> m;
                running += (dim * dim * dim);
            }
            int totalPerLevel = running;
            levelOffsets = new float[48];
            Array.Fill(levelOffsets, 0);
            for (int lv = 0; lv < maxLevel; lv++)
            {
                levelOffsets[lv * 4] = lv * totalPerLevel;
            }
            return totalPerLevel * (maxLevel);
        }
        Matrix4x4 TransformViewMatrix(Matrix4x4 mat)
        {

            if (SystemInfo.usesReversedZBuffer)
            {
                mat[2, 0] = -mat[2, 0];
                mat[2, 1] = -mat[2, 1];
                mat[2, 2] = -mat[2, 2];
                mat[2, 3] = -mat[2, 3];
            }
            return mat;
        }

        public void RenderingVoxel(ScriptableRenderContext context, Camera camera)
        {
            if (camera != PhotonGISystem.Instance.MainCamera || voxelShader == null || stopInitVoxel)
                return;
 
            CommandBuffer cmd = CommandBufferPool.Get("VoxelRender");
            cmd.SetComputeFloatParams(globalVoxelCompute, "_LevelOffsets", levelOffsets);
            cmd.SetComputeFloatParams(globalVoxelCompute, "_MipOffsets", mipOffsets);
            centerPos = camera.transform.position;

            GameObject leftViewPoint = new GameObject("LeftViewPoint");
            GameObject topViewPoint = new GameObject("TopViewPoint");
            leftViewPoint.hideFlags = HideFlags.HideAndDontSave;
            topViewPoint.hideFlags = HideFlags.HideAndDontSave;
            if (!cameraRecordDebug)
            {
                recordedCameras.Clear();
            }
            int groupSize = Mathf.CeilToInt(VoxelLength / 8.0f);
            InputVoxelSpaceOriginDeltas();
            for (int level = 0; level < maxLevel; level++)
            {


                float voxelSpaceSize = GetVoxelSpaceSize(level);

                //----------------------------------------------------------delta

                /*
                float shadowSpaceMaxSize = Mathf.Sqrt(3) * GetVoxelSpaceSize(level + shadowCamIncreaseLevel);
                float shadowSpaceMaxSizeHalf = shadowSpaceMaxSize / 2;

                //------------------------------RenderDepth
                
                float shadowSpaceSize = Mathf.Sqrt(3) * voxelSpaceSize;

                shadowCam.cullingMask = giCullingMask;
                float depthDelta = (shadowSpaceSize / (float)VoxelLength) / shadowSpaceMaxSize;
                Vector3 shadowCamPosition = voxelSpaceOrigins[level] + Vector3.Normalize(-sun.transform.forward) * (shadowSpaceSize * 0.5f + shadowSpaceMaxSizeHalf);

                shadowCam.transform.position = shadowCamPosition;
                shadowCam.transform.LookAt(voxelSpaceOrigins[level], Vector3.up);

                shadowCam.renderingPath = RenderingPath.Forward;
                shadowCam.depthTextureMode |= DepthTextureMode.None;

                shadowCam.orthographicSize = shadowSpaceSize * 0.5f;
                shadowCam.farClipPlane = shadowSpaceSize + shadowSpaceMaxSize;
                Matrix4x4 viewMatrixShadow = shadowCam.worldToCameraMatrix;
                Matrix4x4 projMatrixShadow = shadowCam.projectionMatrix;
                cmd.SetViewMatrix(viewMatrixShadow);
                cmd.SetProjectionMatrix(projMatrixShadow);

                cmd.SetRenderTarget(sunDepthTexture);
                cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                CommandBufferHelper.RenderWithShader(cmd, context, shadowCam, sunDepthShader, RenderQueueRange.all);

                cmd.SetGlobalTexture("SEGISunDepth", sunDepthTexture);
                cmd.SetGlobalFloat("DepthDelta", depthDelta);
                */
                //------------------ClearTempVoxelRT Here
                trackers[level].MoveToPos(voxelSpaceOrigins[level]);
                int kernelVoxelDataCopy = globalVoxelCompute.FindKernel("VoxelDataCopy");
                int totalVoxels = VoxelLength * VoxelLength * VoxelLength;

                cmd.SetComputeTextureParam(globalVoxelCompute, kernelVoxelDataCopy, "_TempVoxelRTFront", tempVoxelRTFront);
                cmd.SetComputeTextureParam(globalVoxelCompute, kernelVoxelDataCopy, "_TempVoxelRTBack", tempVoxelRTBack);
                cmd.SetComputeTextureParam(globalVoxelCompute, kernelVoxelDataCopy, "_TempVoxelRTNormal", tempVoxelRTNormal);
                cmd.SetComputeTextureParam(globalVoxelCompute, kernelVoxelDataCopy, "_TempVoxelRTRadiosityAtlas", tempVoxelRTRadiosityAtlas);
                cmd.SetComputeBufferParam(globalVoxelCompute, kernelVoxelDataCopy, "_VoxelBuffer", globalVoxelBuffer);
                cmd.SetComputeVectorParam(globalVoxelCompute, "_MoveMotionVector", trackers[level].moveMotion);

                //------------Data
                cmd.SetComputeIntParam(globalVoxelCompute, "_VoxelLength", VoxelLength);    
                cmd.SetComputeIntParam(globalVoxelCompute, "_CurrentLevel", level);
                cmd.SetComputeIntParam(globalVoxelCompute, "_IterationCount", iterationCount);
                cmd.SetComputeVectorParam(globalVoxelCompute, "_CenterPosition", centerPos);

                int copyGroups = Mathf.CeilToInt(VoxelLength / 8f);

                cmd.DispatchCompute(globalVoxelCompute, kernelVoxelDataCopy, copyGroups, copyGroups, copyGroups);
                //------------------Render
                cmd.BeginSample("Test");
                var updateRequests = trackers[level].GetUpdateRequests();
                foreach (var req in updateRequests)
                {
                   
                    int blockWorldLength = (int)req.BlockWorldSize;
                    int sliceResolution = VoxelBlockLength;
                    RenderTexture sliceRT = RTManager.Instance.GetRT(
                        "TempSliceRT",
                        sliceResolution,
                        format: RenderTextureFormat.ARGB32,
                        wrapMode: TextureWrapMode.Clamp,
                        filterMode: FilterMode.Point,
                        enableRandomWrite: true
                    );
                    req.ConfigureCamera(voxelCamera);
                    req.ConfigureViewPoints(leftViewPoint, topViewPoint);
                    RecordCameraInfo(voxelCamera, "FrontCamera");
                    //------------------CleanBlock
                    int kernelVoxelDataReset = globalVoxelCompute.FindKernel("VoxelDataReset");
                    cmd.SetComputeTextureParam(globalVoxelCompute, kernelVoxelDataReset, "_TempVoxelRTFront", tempVoxelRTFront);
                    cmd.SetComputeTextureParam(globalVoxelCompute, kernelVoxelDataReset, "_TempVoxelRTBack", tempVoxelRTBack);
                    cmd.SetComputeTextureParam(globalVoxelCompute, kernelVoxelDataReset, "_TempVoxelRTNormal", tempVoxelRTNormal);
                    cmd.SetComputeTextureParam(globalVoxelCompute, kernelVoxelDataReset, "_TempVoxelRTRadiosityAtlas", tempVoxelRTRadiosityAtlas);
                    Vector3 offest = req.BlockIndex * VoxelBlockLength;
                    cmd.SetComputeVectorParam(globalVoxelCompute, "_Offest", offest);
                    int resetGroups = Mathf.CeilToInt(VoxelBlockLength / 8f);
                    cmd.DispatchCompute(globalVoxelCompute, kernelVoxelDataReset, resetGroups, resetGroups, resetGroups);

                    //------------------InputData
                    Matrix4x4 viewMatrix = voxelCamera.worldToCameraMatrix;
                    Matrix4x4 projMatrix = voxelCamera.projectionMatrix;
                    cmd.SetViewMatrix(viewMatrix);
                    cmd.SetProjectionMatrix(projMatrix);
                    cmd.SetGlobalMatrix("SEGIVoxelViewFront", TransformViewMatrix(voxelCamera.transform.worldToLocalMatrix));
                    cmd.SetGlobalMatrix("SEGIVoxelViewLeft", TransformViewMatrix(leftViewPoint.transform.worldToLocalMatrix));
                    cmd.SetGlobalMatrix("SEGIVoxelViewTop", TransformViewMatrix(topViewPoint.transform.worldToLocalMatrix));
                    cmd.SetRandomWriteTarget(1, tempVoxelRTFront);
                    cmd.SetRandomWriteTarget(2, tempVoxelRTBack);
                    cmd.SetRandomWriteTarget(3, tempVoxelRTNormal);
                    
                    cmd.SetGlobalTexture("SEGIVolumeTexture1", tempVoxelSecRTs[level]);
                    cmd.SetGlobalInt("SEGIVoxelResolution", VoxelBlockLength);
                    cmd.SetGlobalFloat("_VoxelSize", voxelSpaceSize);

                    cmd.SetGlobalFloat("SEGISecondaryBounceGain", infiniteBounces ? secondaryBounceGain : 0);
                    cmd.SetGlobalMatrix("SEGIVoxelToGIProjection", TransformViewMatrix(topViewPoint.transform.worldToLocalMatrix));
                    Matrix4x4 voxelToGIProjection = (shadowCam.projectionMatrix) * (shadowCam.worldToCameraMatrix) * (voxelCamera.cameraToWorldMatrix);
                    cmd.SetGlobalMatrix("SEGIVoxelToGIProjection", voxelToGIProjection);
                    cmd.SetGlobalMatrix("SEGIVoxelProjection", voxelCamera.projectionMatrix);
                    cmd.SetGlobalMatrix("SEGIVoxelProjectionInverse", voxelCamera.projectionMatrix.inverse);
                    cmd.SetGlobalVector("SEGISunlightVector", sun ? Vector3.Normalize(sun.transform.forward) : Vector3.up);
                    cmd.SetGlobalColor("GISunColor", sun == null ? Color.black : new Color(Mathf.Pow(sun.color.r, 2.2f), Mathf.Pow(sun.color.g, 2.2f), Mathf.Pow(sun.color.b, 2.2f), Mathf.Pow(sun.intensity, 2.2f)));
                    cmd.SetGlobalColor("LevelColor", RandomColor(level));
                    cmd.SetGlobalVector("SEGISunlightVector", sun ? Vector3.Normalize(sun.transform.forward) : Vector3.up);
                    cmd.SetGlobalVector("_Offest", offest);
                    cmd.SetRenderTarget(tempSliceRT);
                    cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                    CommandBufferHelper.RenderWithShader(cmd, context, voxelCamera, voxelShader, RenderQueueRange.all);
                }

                cmd.EndSample("Test");
                //------------InputVoxel
                
                int kernelInput = globalVoxelCompute.FindKernel("InputVoxel");
                SetCSData(cmd, globalVoxelCompute, kernelInput, voxelCamera);
                cmd.SetComputeTextureParam(globalVoxelCompute, kernelInput, "_TempVoxelRTFront", tempVoxelRTFront);
                cmd.SetComputeTextureParam(globalVoxelCompute, kernelInput, "_TempVoxelRTBack", tempVoxelRTBack);
                cmd.SetComputeTextureParam(globalVoxelCompute, kernelInput, "_TempVoxelRTNormal", tempVoxelRTNormal);
                cmd.SetComputeTextureParam(globalVoxelCompute, kernelInput, "_TempVoxelRTRadiosityAtlas", tempVoxelRTRadiosityAtlas);
                cmd.DispatchCompute(globalVoxelCompute, kernelInput, groupSize, groupSize, groupSize);



                trackers[level].ClearAllDirty();
            }
            int kernel_CopyBufferToRT = globalVoxelCompute.FindKernel("CopyBufferToRT");
            SetCSData(cmd, globalVoxelCompute, kernel_CopyBufferToRT, voxelCamera);
            cmd.SetComputeTextureParam(globalVoxelCompute, kernel_CopyBufferToRT, "_VoxelAlbedoFrontRWRT", voxelAlbedoRT);
            cmd.DispatchCompute(globalVoxelCompute, kernel_CopyBufferToRT, groupSize, groupSize, groupSize * maxLevel);
            //--------------------------------------------UpLod------------------------------------
            /*
            for(int i = 0; i < maxLevel - 1; i++)
            {
                int kernel_PropagateUpLODData = globalVoxelCompute.FindKernel("PropagateUpLODData");

                SetCSData(cmd, globalVoxelCompute, kernel_PropagateUpLODData, voxelCamera);
                cmd.SetComputeIntParam(globalVoxelCompute, "_VoxelLength", VoxelLength);
                cmd.SetComputeIntParam(globalVoxelCompute, "_CurrentLevel", i);
                cmd.SetGlobalVector("_Offest", new Vector3(VoxelBlockLength, VoxelBlockLength, VoxelBlockLength));
                int upGroup = Mathf.CeilToInt((VoxelLength - 2 * VoxelBlockLength) / 8f / 2f);
                cmd.DispatchCompute(globalVoxelCompute, kernel_PropagateUpLODData, upGroup, upGroup, upGroup);
            }
            */

            cmd.GenerateMips(voxelAlbedoRT);
            GenerateMips(cmd, context);

            groupSize = Mathf.CeilToInt(BlocksPerAxis / 8.0f);
            int kernel_GenerateGlobalProbe = globalVoxelCompute.FindKernel("GenerateGlobalProbe");
            SetCSData(cmd, globalVoxelCompute, kernel_GenerateGlobalProbe, voxelCamera);
            cmd.SetComputeBufferParam(globalVoxelCompute, kernel_GenerateGlobalProbe, "_GlobalRadiacneProbesTemp", globalRadiacneProbesTemp);
            cmd.DispatchCompute(globalVoxelCompute, kernel_GenerateGlobalProbe, groupSize, groupSize, groupSize * maxLevel);

            int kernel_PropagateGlobalProbe = globalVoxelCompute.FindKernel("PropagateGlobalProbe");
            SetCSData(cmd, globalVoxelCompute, kernel_PropagateGlobalProbe, voxelCamera);
            cmd.SetComputeBufferParam(globalVoxelCompute, kernel_PropagateGlobalProbe, "_GlobalRadiacneProbesTemp", globalRadiacneProbesTemp);
            cmd.DispatchCompute(globalVoxelCompute, kernel_PropagateGlobalProbe, groupSize, groupSize, groupSize * maxLevel);


            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
        public static Color RandomColor(int seed)
        {
            // 用 seed 初始化 RNG
            var rng = new System.Random(seed);
            // 生成 0~1 范围内的浮点数
            float r = (float)rng.NextDouble();
            float g = (float)rng.NextDouble();
            float b = (float)rng.NextDouble();
            // alpha 固定为 1
            return new Color(r, g, b, 1f);
        }
        private void RecordCameraInfo(Camera cam, string nameTag)
        {
            if (cameraRecordDebug)
            {
                recordedCameras.Add(new CameraInfo
                {
                    position = cam.transform.position,
                    rotation = cam.transform.rotation,
                    orthographicSize = cam.orthographicSize,
                    nearClip = cam.nearClipPlane,
                    farClip = cam.farClipPlane,
                    name = nameTag
                });
            }
        }
        private void GenerateMips(CommandBuffer cmd, ScriptableRenderContext context)
        {
            int kernel = globalVoxelCompute.FindKernel("CSGenerateMips");
    
            for (int level = 0; level < maxLevel; level++)
            {
                for (int srcMip = 0; srcMip < MipCount - 1; srcMip++)
                {
                    int dstMip = srcMip + 1;
                    int dimDst = VoxelLength >> dstMip;
                    int threadGroups = Mathf.CeilToInt(dimDst / 8.0f);

                    cmd.SetComputeIntParam(globalVoxelCompute, "_SourceMip", srcMip);
                    cmd.SetComputeIntParam(globalVoxelCompute, "_DestMip", dstMip);
                    cmd.SetComputeIntParam(globalVoxelCompute, "_CurrentLevel", level);
                    cmd.SetComputeBufferParam(globalVoxelCompute, kernel, "_VoxelBuffer", globalVoxelBuffer);
                    cmd.DispatchCompute(globalVoxelCompute, kernel, threadGroups, threadGroups, threadGroups);
                }
            }

        }
        private void OnDrawGizmos()
        {
            if (recordedCameras == null || recordedCameras.Count == 0)
                return;

            Color wireColor = new Color(1f, 0f, 0f, 0.8f);
            Color faceColor = new Color(1f, 0f, 0f, 0.2f);

            foreach (var info in recordedCameras)
            {

                Gizmos.color = wireColor;
                Gizmos.DrawSphere(info.position, 0.02f);

                Matrix4x4 oldMatrix = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(info.position, info.rotation, Vector3.one);

                float w = info.orthographicSize * 2f;
                float h = info.orthographicSize * 2f;
                float nz = info.nearClip;
                float fz = info.farClip;
                float depth = fz - nz;

                Vector3 boxCenter = new Vector3(0, 0, nz + depth * 0.5f);
                Vector3 boxSize = new Vector3(w, h, depth);

                Gizmos.color = wireColor;
                Gizmos.DrawWireCube(boxCenter, boxSize);
                Gizmos.color = faceColor;
                //Gizmos.DrawCube(boxCenter, boxSize);

                Gizmos.matrix = oldMatrix;
            }
        }
        public void UpdateVoxel(CommandBuffer cmd)
        {

        }
        public void Release(RenderTexture renderTexture)
        {
            if (renderTexture)
            {
                renderTexture.DiscardContents();
                renderTexture.Release();
                DestroyImmediate(renderTexture);
            }
        }
        public void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= RenderingVoxel;
            globalVoxelBuffer?.Release();
            tempVoxelRTFront?.Release();
            tempVoxelRTNormal?.Release();
            tempVoxelRTBack?.Release();
            tempVoxelRTRadiosityAtlas?.Release();
            tempSliceRT?.Release();
            Release(sunDepthTexture);
            for (int i = 0; i < maxLevel; i++)
            {
                Release(tempVoxelSecRTs[i]);

            }

            DestroyImmediate(voxelCamera.gameObject);
        }

        public void GetRadianceSample(RadianceControl radianceControl, PhotonRenderingData photonRenderingData)
        {
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            int kernelGlobalVoxelDiffuse = radianceCompute.FindKernel("GlobalVoxelDiffuse");
            Camera camera = photonRenderingData.camera;
            radianceControl.traceBlendMaskD.SetBlendMask(photonRenderingData.cmd, radianceCompute, kernelGlobalVoxelDiffuse);
            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, radianceCompute, kernelGlobalVoxelDiffuse, camera);
            radianceControl.SetRadianceCascadesCSData(photonRenderingData, kernelGlobalVoxelDiffuse);
            RadianceManager.Instance.ExecutePersistentThreads(photonRenderingData, radianceCompute, photonRenderingData.IndirectScaleDownResolution.x, photonRenderingData.IndirectScaleDownResolution.y, kernelGlobalVoxelDiffuse);
        }

        public void RadainceFeedback(RadianceControl radianceControl, PhotonRenderingData photonRenderingData)
        {

        }
    }
}