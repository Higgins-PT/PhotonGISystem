using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.VirtualTexturing;
using Unity.Mathematics;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Debug = UnityEngine.Debug;

namespace PhotonSystem
{
    public class DebugManager : PhotonSingleton<DebugManager>
    {
        public enum Resolution : int
        {
            Scale1_1 = 1,
            Scale1_2 = 2,
            Scale1_4 = 4,
            Scale1_8 = 8,
            Scale1_16 = 16
        }
        public enum DebugType
        {
            LocalSDF,
            RadianceCascades,
            GlobalVoxel,
            GlobalVoxelGI,
            TestFilter,
            SSRTest,
            OfflineRenderer
        }
        public GameObject debugObject;
        public bool enableSceneViewCameraGI = true;
        public bool enableDebug;
        public DebugType debugType;
        public Resolution resolution = Resolution.Scale1_1;
        public Resolution indirectResolution = Resolution.Scale1_1;
        public Resolution specularResolution = Resolution.Scale1_1;

        public ComputeShader photonRendererCS;
        public ComputeShader debugCS;
        public Mesh sphereMesh;
        [Range(1, 16)]
        public int spp = 4;
        [Range(1, 16)]
        public int maxBounces = 1;
        [Range(0, 50)]
        public float coneAngle = 1;
        private int m_RenderLocalSDFDebug = -1;
        private int m_GlobalVoxelTest = -1;
        private int m_GlobalVoxelGI = -1;
        private int m_BlitToActive = -1;
        private int m_OutputColor = -1;
        private const int threadGroupSize = 8;

        public bool enableDebugPlane;
        public RectTransform debugPlane;
        public TMP_Text debugText;
        private bool debugPlaneState = false;
        public bool surfaceCacheDebug = false;
        public bool surfaceCacheCamDebug = false;
        public bool lightCullHZBDebug = false;
        public bool stopCullLight = false;
        public bool sparseLightDebug = false;
        public bool worldProbesDebug = false;
        [Range(0f, 2f)]
        public float probeRadius = 0.2f;
        private bool initFinish = false;
        private Material worldProbesMat;

        private void OnEnable()
        {
            m_RenderLocalSDFDebug = photonRendererCS.FindKernel("RenderLocalSDFDebug");
            m_BlitToActive = photonRendererCS.FindKernel("BlitToActive");
            m_GlobalVoxelTest = photonRendererCS.FindKernel("GlobalVoxelTest");
            m_GlobalVoxelGI = photonRendererCS.FindKernel("GlobalVoxelGI");
            m_OutputColor = photonRendererCS.FindKernel("OutputColor"); 
        }
        // Update is called once per frame
        void Update()
        {
            if (enableDebugPlane && debugPlaneState == false)
            {

                debugPlaneState = true;
                debugPlane.gameObject.SetActive(debugPlaneState);

            }
            /*
            if (enableDebugPlane)
            {
                albedoRTImage.texture = SurfaceCacheManager.Instance.albedoRT;
                normalRTImage.texture = SurfaceCacheManager.Instance.normalRT;
                emissiveRTImage.texture = SurfaceCacheManager.Instance.emissiveRT;
                depthRTImage.texture = SurfaceCacheManager.Instance.depthRT;
            }*/
            if (!enableDebugPlane && debugPlaneState == true)
            {
                debugPlaneState = false;
                debugPlane.gameObject.SetActive(debugPlaneState);
            }
            if (surfaceCacheDebug)
            {
                foreach (SurfaceCache surfaceCache in SurfaceCacheManager.Instance.directLightSurfaceHash.Values)
                {
                    surfaceCache.Refresh(surfaceCache.assignedSize);
                }
                
            }
 
        }

        struct Pending
        {
            public uint value;
            public bool ready;
        }
        readonly Dictionary<string, Pending> _pending = new();
        readonly Stack<ComputeBuffer> _pool = new();
        int _completedCount = 0;

        ComputeBuffer GetTempUint() =>
            _pool.Count > 0
                ? _pool.Pop()
                : new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Counter);

        void Recycle(ComputeBuffer buf)
        {
            if (buf != null) _pool.Push(buf);
        }

        public void RequestUint(CommandBuffer cmd,
                                ComputeBuffer counterBuf,
                                string key = "GPU-Value")
        {
            if (_pending.TryGetValue(key, out var p) && !p.ready)
                return;

            ComputeBuffer rb = GetTempUint();
            cmd.CopyCounterValue(counterBuf, rb, 0);

            _pending[key] = new Pending { value = p.value, ready = false };

            AsyncGPUReadback.Request(rb, req =>
            {
                Recycle(rb);

                if (req.hasError)
                {
                    Debug.LogWarning($"{key} readback failed");
                    return;
                }

                uint v = req.GetData<uint>()[0];
                _pending[key] = new Pending { value = v, ready = true };
            });
        }

        struct TimeEntry { public float time, delta; public TimeEntry(float t, float d) { time = t; delta = d; } }
        private Queue<TimeEntry> entries = new Queue<TimeEntry>();
        private float keepTime = 0.6f;  // 保持的时间窗口长度（秒）
        private float fps;
        void LateUpdate()
        {
            if (!enableDebugPlane) return;

            var sb = new System.Text.StringBuilder();

            foreach (var kv in _pending)
            {
                sb.AppendLine($"{kv.Key}: {kv.Value.value}");
            }
            entries.Enqueue(new TimeEntry(Time.time, Time.unscaledDeltaTime));

            float threshold = Time.time - keepTime;
            while (entries.Count > 0 && entries.Peek().time < threshold)
                entries.Dequeue();

            float accum = 0f;
            foreach (var e in entries) accum += e.delta;
            fps = entries.Count > 0 ? 1f / (accum / entries.Count) : 0f;
            debugText.text = sb.ToString() + "\n" + $"FPS: {fps:F1}";
        }
        private void OnDrawGizmos()
        {
            if (surfaceCacheCamDebug)
            {
                var mgr = SurfaceCacheManager.Instance;
                if (mgr == null || mgr.directLightSurfaceHash == null) return;

                foreach (SurfaceCache surfaceCache in mgr.directLightSurfaceHash.Values)
                {
                    Camera camera = mgr.bakeCamera;
                    if (camera == null) continue;

                    Bounds meshBounds = surfaceCache.photonObject.mesh.bounds;
                    Transform meshTransform = surfaceCache.photonObject.transform;

                    for (int i = 0; i < 6; i++)
                    {
                        surfaceCache.BakeCameraTransform(camera, meshBounds, meshTransform, i, out float depth);


                        Gizmos.color = Color.cyan;
                        Matrix4x4 oldMatrix = Gizmos.matrix;
                        Gizmos.matrix = camera.transform.localToWorldMatrix;

                        float nearZ = camera.nearClipPlane;
                        float farZ = camera.farClipPlane;
                        float halfH = camera.orthographicSize;
                        float halfW = halfH * camera.aspect;
                        Vector3 center = new Vector3(0, 0, (nearZ + farZ) * 0.5f);
                        Vector3 size = new Vector3(halfW * 2f, halfH * 2f, farZ - nearZ);
                        Gizmos.DrawWireCube(center, size);

                        Gizmos.color = new Color(1f, 0f, 0f, 0.3f); 
                        Vector3 nearCenter = new Vector3(0, 0, nearZ);
                        Vector3 nearSize = new Vector3(halfW * 2f, halfH * 2f, 0.01f);
                        Gizmos.DrawCube(nearCenter, nearSize);

                        Gizmos.matrix = oldMatrix;
                    }
                }
            }
            if (lightCullHZBDebug)
            {
                foreach (var data in HZBManager.Instance.ssgiDatas)
                {
                    if (data.Value.cam == Camera.main)
                    {
                        foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                        {
                            if (light.enabled && light.type != LightType.Directional)
                            {
                                HZBManager.GetLightTexelAndMip(light.transform.position, light.range, Camera.main, data.Value.hzbSize, data.Value.mipCount, out int2 texel, out int mipLevel);

                                HZBManager.DrawHZBPixelRect(texel, mipLevel, data.Value.hzbSize, Camera.main, Color.green);


                            }
                        }

                    }
                }

            }
        }


        public override void PhotonUpdate()
        {
            base.PhotonUpdate();
            //TODO Render SDF Debug

        }
        public void Start()
        {
            worldProbesMat = new Material(Shader.Find("Photon/WorldProbesShader"));
            worldProbesMat.enableInstancing = true;

            initFinish = true;
        }
        public void DebugOutput(PhotonRenderingData photonRenderingData)
        {
            if (!RadianceManager.Instance.initComplete)
            {
                return;
            }
            switch (debugType)
            {
                case DebugType.LocalSDF:
                    {
                        LocalSDFRendering(photonRenderingData);
                        break;
                    }
                case DebugType.RadianceCascades:
                    {
                        RadianceRendering(photonRenderingData);
                        break;
                    }
                case DebugType.GlobalVoxel:
                    {
                        GlobalVoxelRendering(photonRenderingData);
                        break;
                    }
                case DebugType.GlobalVoxelGI:
                    {
                        GlobalVoxelGIRendering(photonRenderingData);
                        break;
                    }
                case DebugType.TestFilter:
                    {
                        TestRendering(photonRenderingData);
                        break;
                    }
                case DebugType.SSRTest:
                    {
                        SSRTestRendering(photonRenderingData);
                        break;
                    }
                case DebugType.OfflineRenderer:
                    {
                        OfflineRendering(photonRenderingData);
                        break;
                    }
            }


        }
        private ComputeBuffer argsBuffer;
        private void WorldProbesDebug(PhotonRenderingData photonRenderingData)
        {
            if (!worldProbesDebug)
                return;

            ComputeBuffer probeBuffer = GlobalVoxelManager.Instance.globalRadiacneProbes;
            CommandBuffer cmd = photonRenderingData.cmd;
            worldProbesMat.SetBuffer("_GlobalRadiacneProbes", probeBuffer);
            worldProbesMat.SetFloat("_ProbeRadius", probeRadius);
            if (argsBuffer == null)
                argsBuffer = new ComputeBuffer(
                    1,
                    sizeof(uint) * 5,
                    ComputeBufferType.IndirectArguments
                );

            uint[] args = new uint[5] {
        (uint)sphereMesh.GetIndexCount(0),    // indexCountPerInstance
        (uint)probeBuffer.count,              // instanceCount
        (uint)sphereMesh.GetIndexStart(0),    // startIndexLocation
        (uint)sphereMesh.GetBaseVertex(0),    // baseVertexLocation
        0                                      // startInstanceLocation
    };
            argsBuffer.SetData(args);
            cmd.SetRenderTarget(photonRenderingData.targetRT);
            cmd.DrawMeshInstancedIndirect(
                sphereMesh,      // 要绘制的 Mesh
                0,               // 子网格索引
                worldProbesMat,  // 材质
                0,               // Shader Pass
                argsBuffer,      // args 缓冲
                0                // args 偏移（字节）
            );
        }
        public override void ReleaseSystem()
        {
            base.ReleaseSystem();
            if (argsBuffer != null)
            {
                argsBuffer.Release();
                argsBuffer = null;
            }
        }
        /// <summary>
        public void OutputColor(RenderTexture targetRT, RenderTexture lumResult, RenderTexture activeRT, CommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(photonRendererCS, m_OutputColor, "_LumResult", lumResult);
            cmd.SetComputeTextureParam(photonRendererCS, m_OutputColor, "_ResultTarget", targetRT);
            cmd.SetComputeTextureParam(photonRendererCS, m_OutputColor, "_ActiveTexture", activeRT);
            CommandBufferHelper.DispatchCompute_RT(cmd, photonRendererCS, targetRT, m_OutputColor, 8);
        }
        public void SetScreenCSData(RenderTexture targetRT, RenderTexture normalRT, RenderTexture depthRT, RenderTexture activeRT, RenderTexture tempRT, CommandBuffer cmd, int kernel)
        {
            cmd.SetComputeIntParam(photonRendererCS, "_ScreenWidth", targetRT.width);
            cmd.SetComputeIntParam(photonRendererCS, "_ScreenHeight", targetRT.height);
            cmd.SetComputeTextureParam(photonRendererCS, kernel, "_TempBuffer", tempRT);
            cmd.SetComputeTextureParam(photonRendererCS, kernel, "_ResultTarget", targetRT);
            cmd.SetComputeTextureParam(photonRendererCS, kernel, "_NormalTexture", normalRT);
            cmd.SetComputeTextureParam(photonRendererCS, kernel, "_DepthTexture", depthRT);
            cmd.SetComputeTextureParam(photonRendererCS, kernel, "_ActiveTexture", activeRT);
        }
        public void SSRTestRendering(PhotonRenderingData photonRenderingData)
        {
            if (!enableDebug || !initFinish) return;
            int kernelSSR = photonRendererCS.FindKernel("SSRTest");
            CommandBuffer cmd = photonRenderingData.cmd;
            RadianceControl radianceControl = RadianceManager.Instance.GetRadianceControl(photonRenderingData.camera);
            TraceQueueSet(radianceControl);
            radianceControl.ExecuteRadianceCascades(photonRenderingData);
            RenderTexture tempRT = RTManager.Instance.GetAdjustableRT("tempSSRRT", photonRenderingData.targetRT.width, photonRenderingData.targetRT.height, RenderTextureFormat.ARGBFloat);
            SetScreenCSData(photonRenderingData.targetRT, photonRenderingData.normalRT, photonRenderingData.depthRT, photonRenderingData.activeRT, tempRT, photonRenderingData.cmd, kernelSSR);
            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, photonRendererCS, kernelSSR, photonRenderingData.camera);
            cmd.BeginSample("SSR");
            CommandBufferHelper.DispatchCompute_RT(cmd, photonRendererCS, photonRenderingData.targetRT, kernelSSR, 8);
            cmd.EndSample("SSR");
        }
        public void TestRendering(PhotonRenderingData photonRenderingData)
        {
            if (!enableDebug || !initFinish) return;
            int kernelCustom = debugCS.FindKernel("Custom");
            CommandBuffer cmd = photonRenderingData.cmd;
            RadianceControl radianceControl = RadianceManager.Instance.GetRadianceControl(photonRenderingData.camera);
            TraceQueueSet(radianceControl);
            radianceControl.ExecuteRadianceCascades(photonRenderingData);
            RenderTexture radianceRT = RTManager.Instance.GetAdjustableRT("radianceRT", photonRenderingData.targetRT.width, photonRenderingData.targetRT.height, RenderTextureFormat.ARGBFloat);
            cmd.SetComputeTextureParam(debugCS, kernelCustom, "Result", radianceRT);
            cmd.SetComputeIntParam(debugCS, "_Width", radianceRT.width);
            cmd.SetComputeIntParam(debugCS, "_Height", radianceRT.height);
            CommandBufferHelper.DispatchCompute_RT(cmd, debugCS, radianceRT, kernelCustom, 8);
            FilterManager.Instance.ApplyWALR(photonRenderingData, radianceRT, "_Diffuse", FilterManager.FO.AtrousIterations(4));
            cmd.Blit(radianceRT, photonRenderingData.targetRT);
        }
        public void GlobalVoxelGIRendering(PhotonRenderingData photonRenderingData)
        {
            if (!enableDebug || !initFinish) return;
            RenderTexture tempRT = RTManager.Instance.GetAdjustableRT("tempLocalSDFRT", photonRenderingData.targetRT.width, photonRenderingData.targetRT.height, RenderTextureFormat.ARGBFloat);
            RenderTexture radianceRT = RTManager.Instance.GetAdjustableRT("radianceRT", photonRenderingData.targetRT.width, photonRenderingData.targetRT.height, RenderTextureFormat.ARGBFloat);
            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, photonRendererCS, m_GlobalVoxelGI, photonRenderingData.camera);
            SetScreenCSData(radianceRT, photonRenderingData.normalRT, photonRenderingData.depthRT, photonRenderingData.activeRT, tempRT, photonRenderingData.cmd, m_GlobalVoxelGI);
            photonRenderingData.cmd.DispatchCompute(photonRendererCS, m_GlobalVoxelGI, Mathf.CeilToInt((float)photonRenderingData.targetRT.width / threadGroupSize), Mathf.CeilToInt((float)photonRenderingData.targetRT.height / threadGroupSize), 1);
            FilterManager.Instance.ApplySVGF(photonRenderingData, radianceRT, null, "_Diffuse");
            OutputColor(photonRenderingData.targetRT, radianceRT, photonRenderingData.activeRT, photonRenderingData.cmd);
        }
        public void GlobalVoxelRendering(PhotonRenderingData photonRenderingData)
        {
            if (!enableDebug || !initFinish) return;
            RenderTexture tempRT = RTManager.Instance.GetAdjustableRT("tempLocalSDFRT", photonRenderingData.targetRT.width, photonRenderingData.targetRT.height, RenderTextureFormat.ARGBFloat);
            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, photonRendererCS, m_GlobalVoxelTest, photonRenderingData.camera);
            SetScreenCSData(photonRenderingData.targetRT, photonRenderingData.normalRT, photonRenderingData.depthRT, photonRenderingData.activeRT, tempRT, photonRenderingData.cmd, m_GlobalVoxelTest);
            photonRenderingData.cmd.DispatchCompute(photonRendererCS, m_GlobalVoxelTest, Mathf.CeilToInt((float)photonRenderingData.targetRT.width / threadGroupSize), Mathf.CeilToInt((float)photonRenderingData.targetRT.height / threadGroupSize), 1);
            WorldProbesDebug(photonRenderingData);
        }
        #region LocalSDFTest
        [StructLayout(LayoutKind.Sequential)]
        public struct ReservoirSample
        {
            public float3 radiance;
            public float pdf;
            public float3 pos;
            public float3 path;
            public float3 endNormal;
            public float weightSum;
            public float eWeight;
            public uint count;
        }
        int currendStartDepth;
        public void LocalSDFRendering(PhotonRenderingData photonRenderingData)
        {
            if (!enableDebug || !initFinish) return;
            const int maxHistoryCount = 1;

            RadianceControl radianceControl = RadianceManager.Instance.GetRadianceControl(photonRenderingData.camera);
            TraceQueueSet(radianceControl);
            radianceControl.ExecuteRadianceCascades(photonRenderingData);

            RenderTexture tempRT = RTManager.Instance.GetAdjustableRT("tempLocalSDFRT", photonRenderingData.targetRT.width, photonRenderingData.targetRT.height, RenderTextureFormat.ARGBFloat);
            photonRenderingData.cmd.SetRenderTarget(tempRT);
            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, photonRendererCS, m_RenderLocalSDFDebug, photonRenderingData.camera);
            SetScreenCSData(photonRenderingData.targetRT, photonRenderingData.normalRT, photonRenderingData.depthRT, photonRenderingData.activeRT, tempRT, photonRenderingData.cmd, m_RenderLocalSDFDebug);
            ComputeBuffer reservoirBuffer = RTManager.Instance.GetAdjustableCB("_ReservoirBuffer", photonRenderingData.targetRT.width * photonRenderingData.targetRT.height, Marshal.SizeOf<ReservoirSample>());
            ComputeBuffer historyBuffer = RTManager.Instance.GetAdjustableCB("_HistoryBuffer", photonRenderingData.targetRT.width * photonRenderingData.targetRT.height * maxHistoryCount, Marshal.SizeOf<ReservoirSample>());
            ComputeBuffer temporalReservoir = RTManager.Instance.GetAdjustableCB("_TemporalReservoir", photonRenderingData.targetRT.width * photonRenderingData.targetRT.height, Marshal.SizeOf<ReservoirSample>());

            photonRenderingData.cmd.BeginSample("RayTrace");
            photonRenderingData.cmd.SetComputeIntParam(photonRendererCS, "_SPP", spp);
            photonRenderingData.cmd.SetComputeFloatParam(photonRendererCS, "_ConeAngle", coneAngle);
            photonRenderingData.cmd.SetComputeIntParam(photonRendererCS, "_MaxBounces", maxBounces);
            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, m_RenderLocalSDFDebug, "_ReservoirBuffer", reservoirBuffer);
            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, m_RenderLocalSDFDebug, "_TemporalReservoir", temporalReservoir);
            photonRenderingData.cmd.SetComputeTextureParam(photonRendererCS, m_RenderLocalSDFDebug, "_NoiseTex", RadianceManager.Instance.noiseTex);
            photonRenderingData.cmd.DispatchCompute(photonRendererCS, m_RenderLocalSDFDebug, Mathf.CeilToInt((float)photonRenderingData.targetRT.width / threadGroupSize), Mathf.CeilToInt((float)photonRenderingData.targetRT.height / threadGroupSize), 1);
            photonRenderingData.cmd.EndSample("RayTrace");

            //--------------------------------------RS------------------------------------------
            int kernel_TemporalReuse = photonRendererCS.FindKernel("TemporalReuse");
            int kernel_HistoryFeedBack = photonRendererCS.FindKernel("HistoryFeedBack");
            int kernel_OutputReSTIR = photonRendererCS.FindKernel("OutputReSTIR");
            int kernel_SpatialReuse = photonRendererCS.FindKernel("SpatialReuse");


            photonRenderingData.cmd.BeginSample("ReSTIR");
            photonRenderingData.cmd.SetComputeTextureParam(photonRendererCS, kernel_TemporalReuse, "_NoiseTex", RadianceManager.Instance.noiseTex);
            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, kernel_TemporalReuse, "_ReservoirBuffer", reservoirBuffer);
            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, kernel_TemporalReuse, "_HistoryBuffer", historyBuffer);
            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, kernel_TemporalReuse, "_TemporalReservoir", temporalReservoir);
            photonRenderingData.cmd.SetComputeIntParam(photonRendererCS, "_HistoryDepth", maxHistoryCount);
            photonRenderingData.cmd.SetComputeIntParam(photonRendererCS, "_CurrendStartDepth", currendStartDepth);
            SetScreenCSData(photonRenderingData.targetRT, photonRenderingData.normalRT, photonRenderingData.depthRT, photonRenderingData.activeRT, tempRT, photonRenderingData.cmd, kernel_TemporalReuse);
            CommandBufferHelper.DispatchCompute_RT(photonRenderingData.cmd, photonRendererCS, photonRenderingData.targetRT, kernel_TemporalReuse, 8);

            photonRenderingData.cmd.SetComputeTextureParam(photonRendererCS, kernel_SpatialReuse, "_NoiseTex", RadianceManager.Instance.noiseTex);
            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, kernel_SpatialReuse, "_ReservoirBuffer", reservoirBuffer);
            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, kernel_SpatialReuse, "_HistoryBuffer", historyBuffer);
            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, kernel_SpatialReuse, "_TemporalReservoir", temporalReservoir);
            photonRenderingData.cmd.SetComputeIntParam(photonRendererCS, "_HistoryDepth", maxHistoryCount);
            photonRenderingData.cmd.SetComputeIntParam(photonRendererCS, "_CurrendStartDepth", currendStartDepth);
            SetScreenCSData(photonRenderingData.targetRT, photonRenderingData.normalRT, photonRenderingData.depthRT, photonRenderingData.activeRT, tempRT, photonRenderingData.cmd, kernel_SpatialReuse);
            CommandBufferHelper.DispatchCompute_RT(photonRenderingData.cmd, photonRendererCS, photonRenderingData.targetRT, kernel_SpatialReuse, 8);

            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, kernel_HistoryFeedBack, "_ReservoirBuffer", reservoirBuffer);
            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, kernel_HistoryFeedBack, "_TemporalReservoir", temporalReservoir);
            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, kernel_HistoryFeedBack, "_HistoryBuffer", historyBuffer);
            currendStartDepth = (currendStartDepth + 1) % maxHistoryCount;
            photonRenderingData.cmd.SetComputeIntParam(photonRendererCS, "_CurrendStartDepth", currendStartDepth);

            CommandBufferHelper.DispatchCompute_RT(photonRenderingData.cmd, photonRendererCS, photonRenderingData.targetRT, kernel_HistoryFeedBack, 8);

            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, kernel_OutputReSTIR, "_ReservoirBuffer", reservoirBuffer);
            photonRenderingData.cmd.SetComputeBufferParam(photonRendererCS, kernel_OutputReSTIR, "_TemporalReservoir", temporalReservoir);
            SetScreenCSData(photonRenderingData.targetRT, photonRenderingData.normalRT, photonRenderingData.depthRT, photonRenderingData.activeRT, tempRT, photonRenderingData.cmd, kernel_OutputReSTIR);
            CommandBufferHelper.DispatchCompute_RT(photonRenderingData.cmd, photonRendererCS, photonRenderingData.targetRT, kernel_OutputReSTIR, 8);
            photonRenderingData.cmd.EndSample("ReSTIR");
            //FilterManager.Instance.ApplySVGF(photonRenderingData, tempRT, null, "_Test", FilterManager.FO.AtrousIterations(5), FilterManager.FO.AlphaColor(0.3f));
            //FilterManager.Instance.ApplyWALR(photonRenderingData, tempRT, null, FilterManager.FO.AtrousIterations(3));
            //FilterManager.Instance.ApplyDenoiser(photonRenderingData, tempRT, "_Test");
            SetScreenCSData(photonRenderingData.targetRT, photonRenderingData.normalRT, photonRenderingData.depthRT, photonRenderingData.activeRT, tempRT, photonRenderingData.cmd, m_BlitToActive);
            photonRenderingData.cmd.DispatchCompute(photonRendererCS, m_BlitToActive, Mathf.CeilToInt((float)photonRenderingData.targetRT.width / threadGroupSize), Mathf.CeilToInt((float)photonRenderingData.targetRT.height / threadGroupSize), 1);
        }
        #endregion
        public void TraceQueueSet(RadianceControl radianceControl)
        {
            radianceControl.SetRadianceFeatures(HZBManager.Instance, LocalSDFManager.Instance, GlobalVoxelManager.Instance);
        }
        public void RadianceRendering(PhotonRenderingData photonRenderingData)
        {
            if (!enableDebug || !initFinish) return;
            RadianceControl radianceControl = RadianceManager.Instance.GetRadianceControl(photonRenderingData.camera);
            TraceQueueSet(radianceControl);
            radianceControl.ExecuteRadianceCascades(photonRenderingData);
            WorldProbesDebug(photonRenderingData);


            //RadianceManager.Instance.RadianceCascadesCaculate(photonRenderingData);

        }
        public void OfflineRendering(PhotonRenderingData photonRenderingData)
        {
            if (!enableDebug || !initFinish) return;
            RadianceControl radianceControl = RadianceManager.Instance.GetRadianceControl(photonRenderingData.camera);
            TraceQueueSet(radianceControl);
            radianceControl.ExecuteRadianceCascades(photonRenderingData);
            WorldProbesDebug(photonRenderingData);


            //RadianceManager.Instance.RadianceCascadesCaculate(photonRenderingData);

        }
    }
}