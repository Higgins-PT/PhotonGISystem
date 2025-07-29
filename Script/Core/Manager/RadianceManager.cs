using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Rendering;
#endif
using static PhotonSystem.RadianceManager;
namespace PhotonSystem
{
    public class RadianceManager : PhotonSingleton<RadianceManager>
    {
        /*
        public enum TraceType
        {
            DetailTrace,
            RoughTrace
        }
        [Header("Trace Type")]
        public TraceType traceType = TraceType.DetailTrace;
        */
        [Min(1)]
        public int feedbackResolution = 4;
        [Header("Trace Range")]
        public float maxRayTracingDistance;
        public float ssrEnableDistance;
        public float localSDFEnableDistance;
        public float globalVoxelEnableDistance;

        public float worldProbeEnableDistance = 10f;
        public float probeLerpRange = 50f;
        [Header("World Probes")]
        [Range(0, 64)]
        public int worldPorbeSampleCount = 32;
        [Header("Indirect Light")]
        [Range(0f, 10f)]
        public float indirectIlluminationIntensity = 1f;
        [Range(0f, 10f)]
        public float localSDFGetIlluminationIntensity = 1f;
        [Range(0f, 30f)]
        public float emissionIntensity = 10f;
        //[Header("Sparse Light Probes")]
        [Header("Direct Light")]
        [Range(0.3f, 3f)]
        public float lightIntensity = 0.5f;
        [Range(10f,10000f)]
        public float lightCullDistance = 2000f;
        [Range(0.1f, 20f)]
        public float lightGlobalIntensity = 10f;
        [Range(1f, 100f)]
        public float lightDataRefreshCameraDistance = 10f;
        public bool refreshLightData = false;
        [Header("Sample")]
        public const int historyCount = 1;
        [Range(0, 4)]
        public int resolution = 2;
        public ComputeShader radianceCompute;
        [HideInInspector]
        public Shader emissionShader;
        [HideInInspector]
        public Shader metallicShader;
        [HideInInspector]
        public Shader albedoShader;
        [HideInInspector]
        public Shader shadowShader;
        [HideInInspector]
        public Material shadowMat;
        [Header("Environment")]
        [Range(0f, 10f)]
        public float environmentLightIntensity = 1f;
        public int cubeResolution = 256;
        public bool cubeContinuousUpdate = false;
        public Texture2D noiseTex;
        public ComputeBuffer globalCounter;
        const int GROUP_THREADS = 64;
        [HideInInspector]
        public bool initComplete = false;
        public void Start()
        {
            emissionShader = Shader.Find("Photon/GBufferEmissionShader");
            metallicShader = Shader.Find("Photon/GBufferMetallicShader");
            shadowShader = Shader.Find("Photon/ShadowFullScreen");
            albedoShader = Shader.Find("Photon/GBufferAlbedoShader");
            shadowMat = new Material(shadowShader);
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct Reservoir
        {
            public float3 radiance;
            public float pdf;
            public float3 pos;
            public float3 path;
            public float3 endNormal;
            public int hit;
            public float weightSum;
            public float eWeight;
            public uint count;
        }


        #region DirectLight
        public Dictionary<LightReporter, LightBufferData> lights = new Dictionary<LightReporter, LightBufferData>();
        public void RegisterLight(LightReporter l)
        {
            lights.Add(l, new LightBufferData(l._light));
        }
        public void UnregisterLight(LightReporter l)
        {
            lights.Remove(l);
        }
        public LightBufferData[] QueryLocalLights(Vector3 centerWS)
        {
            List<LightBufferData> result = new List<LightBufferData>();

            float maxSqr = lightCullDistance * lightCullDistance;
            foreach (var kv in lights)
            {

                LightReporter rep = kv.Key;
                if (rep == null || rep._light == null) continue;
                if (!rep._light.enabled) continue;


                if ((rep.transform.position - centerWS).sqrMagnitude <= maxSqr && kv.Key._light.type != LightType.Directional)
                {
                    result.Add(kv.Value);   
                }
            }

            return result.ToArray();   
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct LightBufferData
        {
            public Vector3 positionWS; public float invRange; public float range;
            public Vector3 directionWS; public float spotCosOuter;
            public Vector3 radiance; public float3 rightWS; public uint flags;
            public float spotCosInner; public Vector2 areaSize; public float pad;
            public LightBufferData(Light l)
            {
                this = FromUnityLight(l);
            }
            public static LightBufferData FromUnityLight(Light l)
            {
                LightBufferData d = default;
                d.positionWS = l.transform.position;
                d.invRange = (l.type == LightType.Directional) ? 0f :
                               (l.range > 0.001f ? 1f / l.range : 0f);
                d.range = (l.type == LightType.Directional) ? 0f : l.range;
                d.directionWS = (l.type == LightType.Spot || l.type == LightType.Disc ||
                                 l.type == LightType.Rectangle)
                                ? l.transform.forward
                                : Vector3.zero;
                d.rightWS = l.transform.right;
                if (l.type == LightType.Spot)
                {
                    float outer = l.spotAngle * 0.5f * Mathf.Deg2Rad;
                    d.spotCosOuter = Mathf.Cos(outer);

                    float inner = l.innerSpotAngle * 0.5f * Mathf.Deg2Rad;
                    d.spotCosInner = Mathf.Cos(inner);
                }
                else
                {
                    d.spotCosOuter = 1f;
                    d.spotCosInner = 1f;
                }
                Color baseColor = l.color;  
                if (l.useColorTemperature && l.colorTemperature > 0f) 
                {
                    Color tempRGB = Mathf.CorrelatedColorTemperatureToRGB(l.colorTemperature);
                    baseColor *= tempRGB;     
                }

                Color lin = baseColor * l.intensity * RadianceManager.Instance.lightGlobalIntensity * Instance.lightIntensity;

                d.radiance = new Vector3(lin.r, lin.g, lin.b);
                uint f = 0;
                switch (l.type)
                {
                    case LightType.Point: f = 0u; break;
                    case LightType.Spot: f = 1u; break;
                    case LightType.Rectangle: f = 2u; break;
                    case LightType.Disc: f = 3u; break;
                }
                if (l.shadows != LightShadows.None) f |= (1u << 2);
                if (l.cookie != null) f |= (1u << 3);
                d.flags = f;

                if (l.type == LightType.Rectangle || l.type == LightType.Disc)
                {
                    if (l.type == LightType.Disc)
                    {
                        d.areaSize = new Vector2(l.areaSize.x, 0);
                    }
                    else
                    {
                        d.areaSize = new Vector2(l.areaSize.x * 0.5f, l.areaSize.y * 0.5f);
                    }

                }
                else d.areaSize = Vector2.zero;

                d.pad = 0f;
                return d;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ReservoirLightSample
        {
            public Vector3 Le;
            public float pdf;

            public Vector3 L;
            public float dist;

            public uint index;
            public float weightSum;
            public uint count;      
        }
        private readonly List<(LightReporter rep, LightBufferData data)> _pendingUpdates = new List<(LightReporter Rep, LightBufferData)>();
        public void UpdateLight()
        {
            _pendingUpdates.Clear();

            foreach (var kvp in lights)
            {
                var rep = kvp.Key;
                if (rep != null && rep.NeedRefresh)
                {
                    _pendingUpdates.Add((
                        rep,
                        new LightBufferData(rep._light)  
                    ));
                }
            }

            foreach (var (rep, data) in _pendingUpdates)
                lights[rep] = data;    
        }
        #endregion
        private Dictionary<Camera, RadianceControl> radianceControls = new Dictionary<Camera, RadianceControl>();


        public RadianceControl GetRadianceControl(Camera camera)
        {
            RadianceControl result;

            if (radianceControls.TryGetValue(camera, out result))
            {

                return result;
            }
            else
            {
                result = new RadianceControl();
                radianceControls.Add(camera, result);
            }
            return result;
        }

        public void ExecutePersistentThreads(PhotonRenderingData photonRenderingData, ComputeShader computeShader, int width, int height, int kernel)
        {
            CommandBuffer cmd = photonRenderingData.cmd;
            if (globalCounter == null)
            {
                globalCounter = new ComputeBuffer(1, sizeof(int));
            }
            cmd.SetComputeIntParam(computeShader, "_ScreenWidth", width);
            cmd.SetComputeIntParam(computeShader, "_ScreenHeight", height);
            int rayCount = width * height;
            cmd.SetComputeIntParam(computeShader, "_TotalRays", rayCount);
            cmd.SetBufferData(globalCounter, new int[] { 0 });
            cmd.SetComputeBufferParam(computeShader, kernel, "_GlobalCounter", globalCounter);
            cmd.DispatchCompute(computeShader, kernel, (int)Mathf.Ceil(rayCount / (float)GROUP_THREADS / 4f), 1, 1);
        }
        public override void ReleaseSystem()
        {
            initComplete = false;
            base.ReleaseSystem();
            foreach (var dict in radianceControls)
            {
                dict.Value.Release();
            }
            globalCounter?.Release();
        }
        public override void PhotonUpdate()
        {
            initComplete = true;

            base.PhotonUpdate();
            UpdateLight();
        }
        void Update() { 

        }
    }
    public interface IRadianceFeature
    {
        public void GetRadianceSample(RadianceControl radianceControl, PhotonRenderingData photonRenderingData);

        public void RadainceFeedback(RadianceControl radianceControl, PhotonRenderingData photonRenderingData);
    }
    public class RadianceControl
    {
        public struct RadianceData
        {
            public PhotonRenderingData photonRenderingData;
            public int cascadeStride;
            public int cascadeStrideHalf;
            public int cascadeStrideSquare;
            public int2 radianceCascadesRTSize;
            public int2 indirectScaleRTSize;
            public int2 activeRTSize;
            public Vector2 mapScale;
            public RadianceData(PhotonRenderingData photonRenderingData)
            {
                this.photonRenderingData = photonRenderingData;
                int resolution = RadianceManager.Instance.resolution;
                RenderTexture activeRT = photonRenderingData.activeRT;
                cascadeStride = 1 << resolution;
                cascadeStrideHalf = cascadeStride / 2;
                cascadeStrideSquare = cascadeStride * cascadeStride;

                radianceCascadesRTSize = new int2(
                   photonRenderingData.IndirectScaleDownResolution.x,
                   photonRenderingData.IndirectScaleDownResolution.y
               );
                indirectScaleRTSize = new int2(
                   Mathf.CeilToInt(photonRenderingData.IndirectScaleDownResolution.x),
                   Mathf.CeilToInt(photonRenderingData.IndirectScaleDownResolution.y)
               );
                mapScale = new Vector2(
                   (float)photonRenderingData.DownResolution.x / radianceCascadesRTSize.x,
                   (float)photonRenderingData.DownResolution.y / radianceCascadesRTSize.y
               );

                activeRTSize = new int2(activeRT.width, activeRT.height);

            }
        }
        public bool RefreshBufferSizeNextTime;
        public List<IRadianceFeature> radianceFeatures = new List<IRadianceFeature>();
        public RadianceData radianceData;
        public ComputeBuffer reservoirBuffer;
        public ComputeBuffer temporalReservoir;
        public ComputeBuffer historyBuffer;//cascadesSize * z

        public ComputeBuffer reservoirDLBuffer;
        public ComputeBuffer temporalDLReservoir;
        public ComputeBuffer historyDLBuffer;


        public RenderTexture radianceCascadesRT;
        public RenderTexture confidenceRT;
        public RenderTexture radianceResult;
        public RenderTexture specularRT;
        public RenderTexture specularExtendRT;
        public RenderTexture directLightRT;
        public RenderTexture directLightExtendRT;
        public RenderTexture indirectExtendRT;
        
        public RenderTexture cascadesDirectionRT;
        public RenderTexture gbufferEmissionRT;
        public RenderTexture gbufferMetallicRT;
        public RenderTexture gbufferAlbedoRT;
        public RenderTexture shadowRT;
        public Vector3 lastRefeshLightCameraPosition = new Vector3(0, 0, 0);
        public LightBufferData[] lightData;
        public ComputeBuffer lightsBuffer;
        public ComputeBuffer cullLightsBuffer;
        public ComputeBuffer visibleLightCounter;
        public ComputeBuffer readbackLightCounter;

        public RenderTexture shBufferRT;
        public TraceBlendMask traceBlendMask;
        public TraceBlendMask traceBlendMaskD;
        private int lightCount = -1;
        private const int maxHistoryCount = 4;
        private int currendStartDepth = 0;
        public void SetRadianceFeatures(params IRadianceFeature[] radianceFeaturesIn)
        {
            radianceFeatures.Clear();
            radianceFeatures.AddRange(radianceFeaturesIn.ToList());
        }
        #region SetData
        public void CreateRadianceData(PhotonRenderingData photonRenderingData)
        {
            radianceData = new RadianceData(photonRenderingData);
        }
        public void SetRadianceCascadesCSData(PhotonRenderingData photonRenderingData, int kernel)
        {
            CommandBuffer cmd = photonRenderingData.cmd;
            RenderTexture targetRT = photonRenderingData.targetRT;
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            cmd.SetComputeFloatParam(radianceCompute, "_TimeSeed", Time.time);
            cmd.SetComputeIntParam(radianceCompute, "_SceneLightCount", lightCount);
            cmd.SetComputeIntParam(radianceCompute, "_ScreenWidth", targetRT.width);
            cmd.SetComputeIntParam(radianceCompute, "_ScreenHeight", targetRT.height);
            cmd.SetComputeIntParam(radianceCompute, "_CascadeStride", radianceData.cascadeStride);
            cmd.SetComputeIntParam(radianceCompute, "_CascadeStrideHalf", radianceData.cascadeStrideHalf);
            cmd.SetComputeIntParam(radianceCompute, "_CascadeStrideSquare", radianceData.cascadeStrideSquare);
            cmd.SetComputeIntParam(radianceCompute, "_HistoryCount", RadianceManager.historyCount);
            cmd.SetComputeIntParam(radianceCompute, "_CurrendStartDepth", currendStartDepth);

            cmd.SetComputeIntParams(radianceCompute, "_RadianceCascadesRTSize",
                new int[] { radianceData.radianceCascadesRTSize.x, radianceData.radianceCascadesRTSize.y }
            );
            cmd.SetComputeIntParams(radianceCompute, "_ActiveRTSize",
                new int[] { radianceData.activeRTSize.x, radianceData.activeRTSize.y }
            );

            cmd.SetComputeVectorParam(radianceCompute, "_MapScale", new Vector4(radianceData.mapScale.x, radianceData.mapScale.y, 0, 0));
            cmd.SetComputeVectorParam(radianceCompute, "_DownResolution", (Vector2)photonRenderingData.DownResolution);
            cmd.SetComputeVectorParam(radianceCompute, "_SpecularDownResolution", (Vector2)photonRenderingData.SpecularDownResolution);
            cmd.SetComputeVectorParam(radianceCompute, "_IndirectDownResolution", (Vector2)photonRenderingData.IndirectScaleDownResolution);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_DepthTexture", photonRenderingData.depthRT);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_NormalTexture", photonRenderingData.normalRT);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_ActiveTexture", photonRenderingData.activeRT);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_RadianceResult", radianceResult);
            cmd.SetComputeBufferParam(radianceCompute, kernel, "_HistoryBuffer", historyBuffer);
            cmd.SetComputeBufferParam(radianceCompute, kernel, "_ReservoirBuffer", reservoirBuffer);
            cmd.SetComputeBufferParam(radianceCompute, kernel, "_TemporalReservoir", temporalReservoir);

            cmd.SetComputeTextureParam(radianceCompute, kernel, "_SparseLightLevel", photonRenderingData.sparseLightLevelRT);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_GBufferMetallicRT", gbufferMetallicRT);
            cmd.SetComputeBufferParam(radianceCompute, kernel, "_DLHistoryBuffer", historyDLBuffer);
            cmd.SetComputeBufferParam(radianceCompute, kernel, "_DLReservoirBuffer", reservoirDLBuffer);
            cmd.SetComputeBufferParam(radianceCompute, kernel, "_DLTemporalReservoir", temporalDLReservoir);
            cmd.SetComputeBufferParam(radianceCompute, kernel, "_LightsBuffer", cullLightsBuffer);
            cmd.SetComputeBufferParam(radianceCompute, kernel, "_SceneLightCounter", readbackLightCounter);
            
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_CascadesDirectionRT", cascadesDirectionRT);

            cmd.SetComputeTextureParam(radianceCompute, kernel, "_NoiseTex", RadianceManager.Instance.noiseTex);
            cmd.SetComputeFloatParam(radianceCompute, "_NoiseSize_X", RadianceManager.Instance.noiseTex.width);
            cmd.SetComputeFloatParam(radianceCompute, "_NoiseSize_Y", RadianceManager.Instance.noiseTex.height);
            cmd.SetComputeFloatParam(radianceCompute, "_IndirectIlluminationIntensity", RadianceManager.Instance.indirectIlluminationIntensity);
            cmd.SetComputeFloatParam(radianceCompute, "_LocalSDFGetIlluminationIntensity", RadianceManager.Instance.localSDFGetIlluminationIntensity);
            
            cmd.SetComputeFloatParam(radianceCompute, "_EnvironmentLightIntensity", RadianceManager.Instance.environmentLightIntensity);
            cmd.SetComputeFloatParam(radianceCompute, "_FarClipPlane", photonRenderingData.camera.farClipPlane);
            cmd.SetComputeFloatParam(radianceCompute, "_EmissionIntensity", RadianceManager.Instance.emissionIntensity);
            
            cmd.SetComputeFloatParam(radianceCompute, "_ResolutionScale", photonRenderingData.scale);
            cmd.SetComputeFloatParam(radianceCompute, "_ResolutionScaleInv", 1 / photonRenderingData.scale);

            cmd.SetComputeFloatParam(radianceCompute, "_SpecularResolutionScale", photonRenderingData.specularScale);
            cmd.SetComputeFloatParam(radianceCompute, "_SpecularResolutionScaleInv", 1 / photonRenderingData.specularScale);


            cmd.SetComputeFloatParam(radianceCompute, "_IndirectResolutionScale", photonRenderingData.indirectScale);
            cmd.SetComputeFloatParam(radianceCompute, "_IndirectResolutionScaleInv", 1 / photonRenderingData.indirectScale);
            
        }
        #endregion
        #region Pass
        public void RadianceCascadesSampleGet(PhotonRenderingData photonRenderingData)
        {
            CommandBuffer cmd = photonRenderingData.cmd;
            RenderTexture targetRT = photonRenderingData.targetRT;
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            int kernelCascadesRTGet = radianceCompute.FindKernel("RadianceCascadesRTGet");
            Camera camera = photonRenderingData.camera;
            radianceCascadesRT = RTManager.Instance.GetAdjustableRT(
                camera.GetInstanceID() + "_RadianceCascadesRT",
                radianceData.radianceCascadesRTSize.x,
                radianceData.radianceCascadesRTSize.y,
                RenderTextureFormat.ARGBFloat
            );
            SetRadianceCascadesCSData(photonRenderingData, kernelCascadesRTGet);
            {
                cmd.SetComputeTextureParam(radianceCompute, kernelCascadesRTGet, "_RadianceResult", radianceResult);
                cmd.SetComputeTextureParam(radianceCompute, kernelCascadesRTGet, "_RadianceCascadesRT", radianceCascadesRT);

                cmd.SetComputeBufferParam(radianceCompute, kernelCascadesRTGet, "_ReservoirBuffer", reservoirBuffer);

                int threadX = Mathf.CeilToInt(radianceCascadesRT.width / 8f);
                int threadY = Mathf.CeilToInt(radianceCascadesRT.height / 8f);
                cmd.DispatchCompute(radianceCompute, kernelCascadesRTGet, threadX, threadY, 1);
            }
        }

        public void LightProbeSHGet(PhotonRenderingData photonRenderingData)
        {
            CommandBuffer cmd = photonRenderingData.cmd;
            RenderTexture targetRT = photonRenderingData.targetRT;
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            int kernelLightProbeSHGet = radianceCompute.FindKernel("LightProbeSHGet");
            Camera camera = photonRenderingData.camera;
            radianceCascadesRT = RTManager.Instance.GetAdjustableRT(
                camera.GetInstanceID() + "_RadianceCascadesRT",
                radianceData.radianceCascadesRTSize.x,
                radianceData.radianceCascadesRTSize.y,
                RenderTextureFormat.ARGBFloat
            );
            var cascades = radianceData.radianceCascadesRTSize;
            if (shBufferRT == null || shBufferRT.width != cascades.x || shBufferRT.height != cascades.y)
            {
                shBufferRT?.DiscardContents();
                shBufferRT?.Release();
                
                shBufferRT = new RenderTexture(cascades.x, cascades.y, 0, RenderTextureFormat.ARGBFloat)
                {
                    dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
                    volumeDepth = 9,
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                    useMipMap = false,
                    autoGenerateMips = false,
                };

                shBufferRT.Create();

            }


            SetRadianceCascadesCSData(photonRenderingData, kernelLightProbeSHGet);
            {
                cmd.SetComputeTextureParam(radianceCompute, kernelLightProbeSHGet, "_RadianceResult", radianceResult);
                cmd.SetComputeTextureParam(radianceCompute, kernelLightProbeSHGet, "_RadianceCascadesRT", radianceCascadesRT);
                cmd.SetComputeTextureParam(radianceCompute, kernelLightProbeSHGet, "_SHBufferRW", shBufferRT);

                int threadX = Mathf.CeilToInt(radianceCascadesRT.width / 8f);
                int threadY = Mathf.CeilToInt(radianceCascadesRT.height / 8f);
                cmd.DispatchCompute(radianceCompute, kernelLightProbeSHGet, threadX, threadY, 1);
            }
        }
        public void SampleGlobalProbes(PhotonRenderingData photonRenderingData)
        {

            CommandBuffer cmd = photonRenderingData.cmd;
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;

            int kernelSampleGlobalProbes = radianceCompute.FindKernel("SampleGlobalProbes");
            int groupSize = Mathf.CeilToInt(GlobalVoxelManager.Instance.BlocksPerAxis / 8.0f); 
            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, radianceCompute, kernelSampleGlobalProbes, photonRenderingData.camera);
            SetRadianceCascadesCSData(photonRenderingData, kernelSampleGlobalProbes);
            cmd.SetComputeIntParam(radianceCompute, "_SampleCount", RadianceManager.Instance.worldPorbeSampleCount);

            cmd.DispatchCompute(radianceCompute, kernelSampleGlobalProbes, groupSize, groupSize, groupSize * GlobalVoxelManager.Instance.maxLevel);

        }
        public void OutputLerpSH(PhotonRenderingData photonRenderingData)
        {
            CommandBuffer cmd = photonRenderingData.cmd;
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;

            int kernelOutputLerpSH = radianceCompute.FindKernel("OutputLerpSH");
            Camera camera = photonRenderingData.camera;
            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, radianceCompute, kernelOutputLerpSH, photonRenderingData.camera);
            {
                SetRadianceCascadesCSData(photonRenderingData, kernelOutputLerpSH);
                cmd.SetComputeTextureParam(radianceCompute, kernelOutputLerpSH, "_ResultTarget", radianceResult);
                cmd.SetComputeTextureParam(radianceCompute, kernelOutputLerpSH, "_MotionVectorRT", photonRenderingData.motionVectorRT);
                cmd.SetComputeTextureParam(radianceCompute, kernelOutputLerpSH, "_SHBufferRW", shBufferRT);
                cmd.SetComputeFloatParam(radianceCompute, "_WorldProbeEnableDistance", RadianceManager.Instance.worldProbeEnableDistance);
                cmd.SetComputeFloatParam(radianceCompute, "_ProbeLerpRange", RadianceManager.Instance.probeLerpRange);
                int threadX = Mathf.CeilToInt(photonRenderingData.DownResolution.x / 8f);
                int threadY = Mathf.CeilToInt(photonRenderingData.DownResolution.y / 8f);
                cmd.SetRenderTarget(radianceResult);
                cmd.DispatchCompute(radianceCompute, kernelOutputLerpSH, threadX, threadY, 1);
            }
        }
        public void HandleReSTIROutPutRT(PhotonRenderingData photonRenderingData)
        {
            CommandBuffer cmd = photonRenderingData.cmd;
            RenderTexture targetRT = photonRenderingData.targetRT;
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;

            int kernelOutputLerpLum = radianceCompute.FindKernel("OutputLum");
            RenderTexture activeRT = photonRenderingData.activeRT;
            Camera camera = photonRenderingData.camera;


            {
                cmd.SetComputeTextureParam(radianceCompute, kernelOutputLerpLum, "_RadianceCascadesRT", radianceCascadesRT);
                cmd.SetComputeTextureParam(radianceCompute, kernelOutputLerpLum, "_ResultTarget", radianceResult);
                cmd.SetComputeTextureParam(radianceCompute, kernelOutputLerpLum, "_Confidence", confidenceRT);
                cmd.SetComputeBufferParam(radianceCompute, kernelOutputLerpLum, "_ReservoirBuffer", reservoirBuffer);
                cmd.SetComputeBufferParam(radianceCompute, kernelOutputLerpLum, "_DLReservoirBuffer", reservoirDLBuffer);
                SetRadianceCascadesCSData(photonRenderingData, kernelOutputLerpLum);
                RayTraceManager.Instance.SetTraceCSData(photonRenderingData, radianceCompute, kernelOutputLerpLum, camera);
                int threadX = Mathf.CeilToInt(targetRT.width / 8f);
                int threadY = Mathf.CeilToInt(targetRT.height / 8f);
                if (FilterManager.Instance.enableAdvanceDenoiser)
                {
                    photonRenderingData.cmd.SetComputeFloatParam(radianceCompute, "_NoiseIntensity", 1);
                }
                else
                {
                    photonRenderingData.cmd.SetComputeFloatParam(radianceCompute, "_NoiseIntensity", 0);
                }
                cmd.DispatchCompute(radianceCompute, kernelOutputLerpLum, threadX, threadY, 1);
            }
        }
        public void Filter(PhotonRenderingData photonRenderingData)
        {
            //FilterManager.Instance.ApplyWALR(photonRenderingData, radianceResult, "_Diffuse", FilterManager.FO.AtrousIterations(6));
            //FilterManager.Instance.ApplyDenoiser(photonRenderingData, radianceResult, "_Diffuse");
            indirectExtendRT = RTManager.Instance.GetAdjustableRT("indirectExtendWRT" + photonRenderingData.camera.GetInstanceID(), photonRenderingData.activeRT.width, photonRenderingData.activeRT.height, RenderTextureFormat.ARGBFloat);
            photonRenderingData.cmd.SetRenderTarget(indirectExtendRT);
            /*
            if (FilterManager.Instance.enableAdvanceDenoiser)
            {
                //FilterManager.Instance.ApplyDenoiser(photonRenderingData, radianceResult, "_Diffuse");
                //FilterManager.Instance.ApplySVGF(photonRenderingData, radianceResult, null, "_Diffuse", FilterManager.FO.AtrousIterations(1), FilterManager.FO.AlphaColor(0.1f));
                ExtendIDRT(photonRenderingData, radianceResult, indirectExtendRT);
                FilterManager.Instance.ApplySVGF(photonRenderingData, indirectExtendRT, null, "_Diffuse", FilterManager.FO.AtrousIterations(6), FilterManager.FO.AlphaColor(0.1f));
                FilterManager.Instance.ApplyAtrousFilter(photonRenderingData, indirectExtendRT, null, "_Diffuse", true, FilterManager.FO.AtrousIterations(1));
            }
            else
            {
                FilterManager.Instance.ApplySVGF(photonRenderingData, radianceResult, null, "_Diffuse", FilterManager.FO.AtrousIterations(5), FilterManager.FO.AlphaColor(0.1f));
                FilterManager.Instance.ApplyAtrousFilter(photonRenderingData, radianceResult, null, "_Diffuse", true, FilterManager.FO.AtrousIterations(1));
                ExtendIDRT(photonRenderingData, radianceResult, indirectExtendRT);
            }*/
            FilterManager.Instance.ApplySVGF(photonRenderingData, radianceResult, null, "_Diffuse", FilterManager.FO.AtrousIterations(5), FilterManager.FO.AlphaColor(0.1f));
            FilterManager.Instance.ApplyAtrousFilter(photonRenderingData, radianceResult, null, "_Diffuse", true, FilterManager.FO.AtrousIterations(1));
            ExtendIDRT(photonRenderingData, radianceResult, indirectExtendRT);

        }
        public static Color ColorTemperatureToRGB(float kelvin)
        {
            kelvin = Mathf.Clamp(kelvin, 1000f, 40000f) / 100f;

            float r, g, b;

            // Red
            if (kelvin <= 66f)
                r = 1.0f;
            else
            {
                float t = kelvin - 60f;
                r = Mathf.Clamp01(1.292936186062745f * Mathf.Pow(t, -0.1332047592f));
            }

            // Green
            if (kelvin <= 66f)
            {
                float t = kelvin;
                g = Mathf.Clamp01(0.3900815787690196f * Mathf.Log(t) - 0.6318414437886275f);
            }
            else
            {
                float t = kelvin - 60f;
                g = Mathf.Clamp01(1.1298908608952941f * Mathf.Pow(t, -0.0755148492f));
            }

            // Blue
            if (kelvin >= 66f)
                b = 1.0f;
            else if (kelvin <= 19f)
                b = 0.0f;
            else
            {
                float t = kelvin - 10f;
                b = Mathf.Clamp01(0.5432067891101961f * Mathf.Log(t) - 1.19625408914f);
            }

            return new Color(r, g, b, 1f);
        }
        public void RadianceApply(PhotonRenderingData photonRenderingData)
        {
            RenderTexture targetRT = photonRenderingData.targetRT;
            RenderTexture activeRT = photonRenderingData.activeRT;
            CommandBuffer cmd = photonRenderingData.cmd;
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;

            int kernelOutputColor = radianceCompute.FindKernel("OutputColor");
            cmd.SetComputeTextureParam(radianceCompute, kernelOutputColor, "_DepthTexture", photonRenderingData.depthRT);
            cmd.SetComputeTextureParam(radianceCompute, kernelOutputColor, "_NormalTexture", photonRenderingData.normalRT);
            cmd.SetComputeTextureParam(radianceCompute, kernelOutputColor, "_ActiveTexture", photonRenderingData.activeRT);
            cmd.SetComputeTextureParam(radianceCompute, kernelOutputColor, "_RadianceResult", radianceResult);
            cmd.SetComputeTextureParam(radianceCompute, kernelOutputColor, "_Specular", specularExtendRT);
            cmd.SetComputeTextureParam(radianceCompute, kernelOutputColor, "_DirectLight", directLightExtendRT);

            cmd.SetComputeTextureParam(radianceCompute, kernelOutputColor, "_LumResult", indirectExtendRT);
            cmd.SetComputeTextureParam(radianceCompute, kernelOutputColor, "_ResultTarget", targetRT);
            cmd.SetComputeTextureParam(radianceCompute, kernelOutputColor, "_ActiveTexture", activeRT);
            cmd.SetComputeTextureParam(radianceCompute, kernelOutputColor, "_GBufferEmissionRT", gbufferEmissionRT);
            cmd.SetComputeTextureParam(radianceCompute, kernelOutputColor, "_GBufferMetallicRT", gbufferMetallicRT);
            cmd.SetComputeTextureParam(radianceCompute, kernelOutputColor, "_ShadowRT", shadowRT);
            cmd.SetComputeIntParam(radianceCompute, "_FeedBackResolution", RadianceManager.Instance.feedbackResolution);
            
            Light mainLight = RenderSettings.sun;
            if (mainLight != null)
            {
                Color finalColor = mainLight.color;

                if (mainLight.useColorTemperature)
                {
                    finalColor *= ColorTemperatureToRGB(mainLight.colorTemperature);
                }

                cmd.SetComputeVectorParam(radianceCompute, "_LightDirection", -mainLight.transform.forward);
                cmd.SetComputeVectorParam(radianceCompute, "_LightColor", finalColor);
            }
            else
            {
                cmd.SetComputeVectorParam(radianceCompute, "_LightDirection", new Vector4(0, 0, 0, 0));
                cmd.SetComputeVectorParam(radianceCompute, "_LightColor", new Vector4(0, 0, 0, 0));
            }


            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, radianceCompute, kernelOutputColor, photonRenderingData.camera);
            SetRadianceCascadesCSData(photonRenderingData, kernelOutputColor);

            int threadX = Mathf.CeilToInt(targetRT.width / 8f);
            int threadY = Mathf.CeilToInt(targetRT.height / 8f);
            cmd.DispatchCompute(radianceCompute, kernelOutputColor, threadX, threadY, 1);
        }
        public void RadianceFeedback(PhotonRenderingData photonRenderingData)
        {
            RenderTexture activeRT = photonRenderingData.activeRT;
            CommandBuffer cmd = photonRenderingData.cmd;
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            int kernelHistoryFeedBack = radianceCompute.FindKernel("HistoryFeedBack");
            currendStartDepth = (currendStartDepth + 1) % maxHistoryCount;
            SetRadianceCascadesCSData(photonRenderingData, kernelHistoryFeedBack);
            cmd.SetComputeTextureParam(radianceCompute, kernelHistoryFeedBack, "_MotionVectorRT", photonRenderingData.motionVectorRT);
            CommandBufferHelper.DispatchCompute_RT(cmd, radianceCompute, radianceCascadesRT, kernelHistoryFeedBack, 8);

        }
        public void SpawnRadianceCascadesDirection(PhotonRenderingData photonRenderingData)
        {

            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            int resolution = RadianceManager.Instance.resolution;
            RenderTexture targetRT = photonRenderingData.targetRT;
            CommandBuffer cmd = photonRenderingData.cmd;
            int kernelRadianceCascadesInit = radianceCompute.FindKernel("RadianceCascadesInit");
            int kernelSparseLightProbesLevelInit = radianceCompute.FindKernel("SparseLightProbesLevelInit");
            int kernelSparseLightProbesLevelHZB = radianceCompute.FindKernel("SparseLightProbesLevelHZB");
            int kernelSparseLightProbesLevelFini = radianceCompute.FindKernel("SparseLightProbesLevelFini");
            int kernelSparseLightProbesLevelDebug = radianceCompute.FindKernel("SparseLightProbesLevelDebug");
            Camera camera = photonRenderingData.camera;
            radianceResult = RTManager.Instance.GetAdjustableRT(
                camera.GetInstanceID() + "_RadianceResult",
                photonRenderingData.IndirectScaleDownResolution.x,
                photonRenderingData.IndirectScaleDownResolution.y,
                RenderTextureFormat.ARGBFloat
            );
            cascadesDirectionRT = RTManager.Instance.GetAdjustableRT(camera.GetInstanceID() + "_CascadesDirectionRT", photonRenderingData.IndirectScaleDownResolution.x, photonRenderingData.IndirectScaleDownResolution.y, RenderTextureFormat.ARGBFloat);
            int pixelCount = photonRenderingData.IndirectScaleDownResolution.x * photonRenderingData.IndirectScaleDownResolution.y;
            if (reservoirBuffer == null || reservoirBuffer.count != pixelCount)
            {
                reservoirBuffer?.Release();
                reservoirBuffer = new ComputeBuffer(
                pixelCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Reservoir)),
                ComputeBufferType.Default);
            }
            int historyBufferPixelCount = radianceData.radianceCascadesRTSize.x * radianceData.radianceCascadesRTSize.y * RadianceManager.historyCount;
            if (historyBuffer == null || historyBuffer.count != historyBufferPixelCount)
            {
                historyBuffer?.Release();
                historyBuffer = new ComputeBuffer(historyBufferPixelCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Reservoir)),
                ComputeBufferType.Default);
            }
            int temporalReservoirPixelCount = radianceData.radianceCascadesRTSize.x * radianceData.radianceCascadesRTSize.y;
            if (temporalReservoir == null || temporalReservoir.count != temporalReservoirPixelCount)
            {
                temporalReservoir?.Release();
                temporalReservoir = new ComputeBuffer(temporalReservoirPixelCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Reservoir)),
                ComputeBufferType.Raw);
            }
            radianceCascadesRT = RTManager.Instance.GetAdjustableRT(
                camera.GetInstanceID() + "_RadianceCascadesRT",
                radianceData.radianceCascadesRTSize.x,
                radianceData.radianceCascadesRTSize.y,
                RenderTextureFormat.ARGBFloat
            );
            const int maxMipLevel = 4;
            HZBManager.Instance.SetScreenTraceData(photonRenderingData, radianceCompute, kernelSparseLightProbesLevelInit, camera);
            cmd.SetComputeIntParam(radianceCompute, "_MinResolutionLevel", resolution);
            cmd.SetComputeIntParam(radianceCompute, "_MaxResolutionLevel", maxMipLevel);
            int maxDim = Mathf.Max(photonRenderingData.DownResolution.x, photonRenderingData.DownResolution.y);
            int mipCount = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(maxDim, 2f)), 0, maxMipLevel) + 1;
            #region SparseLight
            RenderTexture sparseLightLevelRTTemp = RTManager.Instance.GetAdjustableRT(camera.GetInstanceID() + "_SparseLightLevelTemp", photonRenderingData.DownResolution.x, photonRenderingData.DownResolution.y, RenderTextureFormat.RFloat, useMipMap: true, mipCount: mipCount);
            RenderTexture sparseLightLevelRT = RTManager.Instance.GetAdjustableRT(camera.GetInstanceID() + "_SparseLightLevel", photonRenderingData.DownResolution.x, photonRenderingData.DownResolution.y, RenderTextureFormat.RFloat, useMipMap: true, mipCount: mipCount);

            cmd.SetComputeTextureParam(radianceCompute, kernelSparseLightProbesLevelInit, "_SparseLightLevel", sparseLightLevelRTTemp, 0);
            CommandBufferHelper.DispatchCompute_RT(cmd, radianceCompute, radianceResult, kernelSparseLightProbesLevelInit, 8);
            for (int i = 1; i <= maxMipLevel; i++)
            {
                cmd.SetComputeTextureParam(radianceCompute, kernelSparseLightProbesLevelHZB, "_Src", sparseLightLevelRTTemp, i - 1);
                cmd.SetComputeTextureParam(radianceCompute, kernelSparseLightProbesLevelHZB, "_Dst", sparseLightLevelRTTemp, i);
                cmd.SetComputeIntParam(radianceCompute, "_MipLevel", i - 1);
                Vector2 size = GetMipSize(sparseLightLevelRTTemp, i);
                int threadGroupX = Mathf.CeilToInt(size.x / 8);
                int threadGroupY = Mathf.CeilToInt(size.y / 8);
                cmd.DispatchCompute(radianceCompute, kernelSparseLightProbesLevelHZB, threadGroupX, threadGroupY, 1);
            }

            cmd.SetComputeTextureParam(radianceCompute, kernelSparseLightProbesLevelFini, "_Src", sparseLightLevelRTTemp);
            cmd.SetComputeTextureParam(radianceCompute, kernelSparseLightProbesLevelFini, "_Dst", sparseLightLevelRT, 0);
            CommandBufferHelper.DispatchCompute_RT(cmd, radianceCompute, radianceResult, kernelSparseLightProbesLevelFini, 8);

            if (DebugManager.Instance.sparseLightDebug)
            {
                cmd.SetComputeTextureParam(radianceCompute, kernelSparseLightProbesLevelDebug, "_ResultTarget", targetRT);
                cmd.SetComputeTextureParam(radianceCompute, kernelSparseLightProbesLevelDebug, "_Src", sparseLightLevelRT);
                cmd.SetRenderTarget(targetRT);
                CommandBufferHelper.DispatchCompute_RT(cmd, radianceCompute, radianceResult, kernelSparseLightProbesLevelDebug, 8);
            }
            photonRenderingData.sparseLightLevelRT = sparseLightLevelRT;
            #endregion
            SetRadianceCascadesCSData(photonRenderingData, kernelRadianceCascadesInit);

            CommandBufferHelper.DispatchCompute_RT(cmd, radianceCompute, radianceResult, kernelRadianceCascadesInit, 8);
        }
        public static Vector2 GetMipSize(RenderTexture rt, int mipLevel)
        {
            mipLevel = Mathf.Clamp(mipLevel, 0, rt.mipmapCount - 1);
            int w = rt.width >> mipLevel;
            int h = rt.height >> mipLevel;
            w = Mathf.Max(1, w);
            h = Mathf.Max(1, h);

            return new Vector2(w, h);
        }
        public void SetScreenCSData(RenderTexture targetRT, RenderTexture normalRT, RenderTexture depthRT, RenderTexture activeRT, RenderTexture tempRT, CommandBuffer cmd, int kernel)
        {
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            cmd.SetComputeIntParam(radianceCompute, "_ScreenWidth", targetRT.width);
            cmd.SetComputeIntParam(radianceCompute, "_ScreenHeight", targetRT.height);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_SpecularRT", tempRT);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_ResultTarget", targetRT);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_NormalTexture", normalRT);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_DepthTexture", depthRT);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_ActiveTexture", activeRT);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_GBufferMetallicRT", gbufferMetallicRT);
        }
        public void CullLights(PhotonRenderingData photonRenderingData)
        {

            ComputeShader cs = RadianceManager.Instance.radianceCompute;
            HZBManager.SSGIData ssgiData = photonRenderingData.ssgiData;
            int kernCull = cs.FindKernel("CullLights");
            Camera cam = photonRenderingData.camera;
            CommandBuffer cmd = photonRenderingData.cmd;

            ComputeBuffer originBuf = lightsBuffer; 
            ComputeBuffer culledBuf = cullLightsBuffer;  
            ComputeBuffer counterBuf = visibleLightCounter;

            uint sceneLightCount = (uint)lightCount;

            cmd.SetComputeBufferParam(cs, kernCull, "_OriginLightsBuffer", originBuf);
            cmd.SetComputeBufferParam(cs, kernCull, "_LightsBuffer", culledBuf);
            cmd.SetComputeBufferParam(cs, kernCull, "_VisibleLightCounter", counterBuf);

            Matrix4x4 proj = cam.projectionMatrix;
            Matrix4x4 view = cam.worldToCameraMatrix;
            cmd.SetComputeMatrixParam(cs, "_ProjectionMatrix_DL", proj);
            cmd.SetComputeMatrixParam(cs, "_ViewMatrix_DL", view);
            cmd.SetComputeMatrixParam(cs, "_ProjectionMatrixInverse_DL", proj.inverse);
            cmd.SetComputeMatrixParam(cs, "_ViewMatrixInverse_DL", view.inverse);

            cmd.SetComputeVectorParam(cs, "_CameraPosition_DL", cam.transform.position);
            cmd.SetComputeVectorParam(cs, "_CameraDirection_DL", cam.transform.forward);


            cmd.SetComputeVectorParam(cs, "_CamRightWS", photonRenderingData.camera.transform.right);
            cmd.SetComputeVectorParam(cs, "_CamUpWS", photonRenderingData.camera.transform.up);
            cmd.SetComputeVectorParam(cs, "_CamForwardWS", photonRenderingData.camera.transform.forward);
            cmd.SetComputeIntParam(cs, "_HZBMipCount", ssgiData.mipCount);
            cmd.SetComputeTextureParam(cs, kernCull, "_HZBDepth", ssgiData.hzb);
            float near = photonRenderingData.camera.nearClipPlane;
            float far = photonRenderingData.camera.farClipPlane;
            Vector4 zBufferParams;
            zBufferParams = new Vector4(near, far, 1, 1);
            cmd.SetComputeVectorParam(cs, "_ZBufferParamsL", zBufferParams);

            cmd.SetComputeVectorParam(cs, "_HZBDepthSize", new Vector4(ssgiData.hzbSize.x, ssgiData.hzbSize.y, 0, 0));
            int2 screenSize = new int2(photonRenderingData.activeRT.width, photonRenderingData.activeRT.height);

            int2 hzbSizeOrigin = ssgiData.hzbSizeOrigin;
            Vector2 scaleOtoH = new Vector2(
                (float)screenSize.x / hzbSizeOrigin.x,
                (float)screenSize.y / hzbSizeOrigin.y);

            cmd.SetComputeVectorParam(cs, "_ScaleOtoH", scaleOtoH);
            cmd.SetComputeIntParam(cs, "_SceneLightCount", (int)sceneLightCount);

            cmd.SetBufferCounterValue(counterBuf, 0);

            int groups = Mathf.CeilToInt(sceneLightCount / 64f);
            cmd.DispatchCompute(cs, kernCull, groups, 1, 1);
            DebugManager.Instance.RequestUint(cmd, counterBuf, "CullLights");
            cmd.CopyCounterValue(counterBuf, readbackLightCounter, 0);

        }

        private int GetAtrousLevel(PhotonRenderingData photonRenderingData, int upLevel)
        {
            int foundationAtrousLevel = (int)Mathf.Floor(Mathf.Log(Mathf.Max(photonRenderingData.activeRT.width, photonRenderingData.activeRT.height, 2)));
            foundationAtrousLevel = Mathf.Max(foundationAtrousLevel - upLevel, 0);
            return foundationAtrousLevel;
        }
        public void DirectLight(PhotonRenderingData photonRenderingData)
        {

            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            RenderTexture activeRT = photonRenderingData.activeRT;
            directLightRT = RTManager.Instance.GetAdjustableRT("directLightRT" + photonRenderingData.camera.GetInstanceID(), photonRenderingData.DownResolution.x, photonRenderingData.DownResolution.y, RenderTextureFormat.ARGBFloat);

            photonRenderingData.cmd.BeginSample("DirectLight");
            int kernelDirectLight = radianceCompute.FindKernel("DirectLight");
            SetRadianceCascadesCSData(photonRenderingData, kernelDirectLight);

            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, radianceCompute, kernelDirectLight, photonRenderingData.camera);
            photonRenderingData.cmd.SetComputeTextureParam(radianceCompute, kernelDirectLight, "_DirectLightRT", directLightRT);
            if (FilterManager.Instance.enableAdvanceDenoiser)
            {
                photonRenderingData.cmd.SetComputeFloatParam(radianceCompute, "_NoiseIntensity", 1);
            }
            else
            {
                photonRenderingData.cmd.SetComputeFloatParam(radianceCompute, "_NoiseIntensity", 0);
            }
            CommandBufferHelper.DispatchCompute_RT(photonRenderingData.cmd, radianceCompute, directLightRT, kernelDirectLight, 8);
            photonRenderingData.cmd.EndSample("DirectLight");

            directLightExtendRT = RTManager.Instance.GetAdjustableRT("directLightExtendWRT" + photonRenderingData.camera.GetInstanceID(), photonRenderingData.activeRT.width, photonRenderingData.activeRT.height, RenderTextureFormat.ARGBFloat);
            //FilterManager.Instance.ApplyWALR(photonRenderingData, directLightRT, "_DirectLightRT", FilterManager.FO.AtrousIterations(4));
            //FilterManager.Instance.ApplySVGF(photonRenderingData, directLightRT, null, "_DirectLightRT", FilterManager.FO.AtrousIterations(Mathf.Max(FilterManager.Instance.atrousIterations - 3, 1)), FilterManager.FO.AlphaColor(0.1f));
            //FilterManager.Instance.ApplyAtrousFilter(photonRenderingData, directLightRT, null, "_DirectLightRT", true, FilterManager.FO.AtrousIterations(1));
            //FilterManager.Instance.ApplySVGF(photonRenderingData, directLightRT, null, "_DirectLightRT", FilterManager.FO.AtrousIterations(0), FilterManager.FO.AlphaColor(0.1f));
            if (FilterManager.Instance.enableAdvanceDenoiser)
            {
                
                FilterManager.Instance.ApplyDenoiser(photonRenderingData, directLightRT, "_DirectLightRT");
                ExtendRT(photonRenderingData, directLightRT, directLightExtendRT);
                //FilterManager.Instance.ApplyWALR(photonRenderingData, directLightExtendRT, "_DirectLightExtendRT", FilterManager.FO.AtrousIterations(2));
                FilterManager.Instance.ApplySVGF(photonRenderingData, directLightExtendRT, null, "_DirectLightExtendRT", FilterManager.FO.AtrousIterations(2), FilterManager.FO.AlphaColor(0.3f));
            }
            else
            {
                ExtendRT(photonRenderingData, directLightRT, directLightExtendRT);
                //FilterManager.Instance.ApplyWALR(photonRenderingData, directLightExtendRT, "_DirectLightExtendRT", FilterManager.FO.AtrousIterations(4));
                
                FilterManager.Instance.ApplySVGF(photonRenderingData, directLightExtendRT, null, "_DirectLightExtendRT", FilterManager.FO.AtrousIterations(GetAtrousLevel(photonRenderingData, 4)), FilterManager.FO.AlphaColor(0.1f));
                FilterManager.Instance.ApplyAtrousFilter(photonRenderingData, directLightExtendRT, null, "_DirectLightExtendRT", true, FilterManager.FO.AtrousIterations(1));
            }

        }
        public void Specular(PhotonRenderingData photonRenderingData)
        {

            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;

            int kernelSpecularSSR = radianceCompute.FindKernel("SpecularSSR");
            int kernelSpecular = radianceCompute.FindKernel("SpecularLS");
            int kernelSpecularGV = radianceCompute.FindKernel("SpecularGV");
            int kernelSpecularRoughMix = radianceCompute.FindKernel("SpecularRoughMix");
            specularRT = RTManager.Instance.GetAdjustableRT("specularRT" + photonRenderingData.camera.GetInstanceID(), photonRenderingData.SpecularDownResolution.x, photonRenderingData.SpecularDownResolution.y, RenderTextureFormat.ARGBFloat);
            specularExtendRT = RTManager.Instance.GetAdjustableRT("specularExtendRT" + photonRenderingData.camera.GetInstanceID(), photonRenderingData.activeRT.width, photonRenderingData.activeRT.height, RenderTextureFormat.ARGBFloat);
            RenderTexture specularRoughRT = RTManager.Instance.GetAdjustableRT("tempRT" + photonRenderingData.camera.GetInstanceID(), photonRenderingData.SpecularDownResolution.x, photonRenderingData.SpecularDownResolution.y, RenderTextureFormat.ARGBFloat);
            photonRenderingData.cmd.SetComputeFloatParam(radianceCompute, "_ConeAngle", 1f);

            photonRenderingData.cmd.BeginSample("SpecularSSR");
            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, radianceCompute, kernelSpecularSSR, photonRenderingData.camera);
            SetScreenCSData(photonRenderingData.targetRT, photonRenderingData.normalRT, photonRenderingData.depthRT, photonRenderingData.activeRT, specularRT, photonRenderingData.cmd, kernelSpecularSSR);
            SetRadianceCascadesCSData(photonRenderingData, kernelSpecularSSR);
            photonRenderingData.cmd.SetComputeTextureParam(radianceCompute, kernelSpecularSSR, "_GBufferMetallicRT", gbufferMetallicRT);
            RadianceManager.Instance.ExecutePersistentThreads(photonRenderingData, radianceCompute, photonRenderingData.SpecularDownResolution.x, photonRenderingData.SpecularDownResolution.y, kernelSpecularSSR);
            traceBlendMask.SmoothBlendMask(photonRenderingData, specularRT, 4);
            photonRenderingData.cmd.EndSample("SpecularSSR");

            photonRenderingData.cmd.BeginSample("SpecularLS");
            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, radianceCompute, kernelSpecular, photonRenderingData.camera);
            SetScreenCSData(photonRenderingData.targetRT, photonRenderingData.normalRT, photonRenderingData.depthRT, photonRenderingData.activeRT, specularRT, photonRenderingData.cmd, kernelSpecular);
            SetRadianceCascadesCSData(photonRenderingData, kernelSpecular);
            photonRenderingData.cmd.SetComputeTextureParam(radianceCompute, kernelSpecular, "_GBufferMetallicRT", gbufferMetallicRT);
            traceBlendMask.SetBlendMask(photonRenderingData.cmd, radianceCompute, kernelSpecular);
            RadianceManager.Instance.ExecutePersistentThreads(photonRenderingData, radianceCompute, photonRenderingData.SpecularDownResolution.x, photonRenderingData.SpecularDownResolution.y, kernelSpecular);
            photonRenderingData.cmd.EndSample("SpecularLS");

            photonRenderingData.cmd.BeginSample("SpecularGV");
            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, radianceCompute, kernelSpecularGV, photonRenderingData.camera);
            SetScreenCSData(photonRenderingData.targetRT, photonRenderingData.normalRT, photonRenderingData.depthRT, photonRenderingData.activeRT, specularRT, photonRenderingData.cmd, kernelSpecularGV);
            SetRadianceCascadesCSData(photonRenderingData, kernelSpecularGV);
            photonRenderingData.cmd.SetComputeTextureParam(radianceCompute, kernelSpecularGV, "_GBufferMetallicRT", gbufferMetallicRT);
            RadianceManager.Instance.ExecutePersistentThreads(photonRenderingData, radianceCompute, photonRenderingData.SpecularDownResolution.x, photonRenderingData.SpecularDownResolution.y, kernelSpecularGV);
            photonRenderingData.cmd.EndSample("SpecularGV");

            photonRenderingData.cmd.Blit(specularRT, specularRoughRT);
            FilterManager.Instance.ApplyAtrousFilter(photonRenderingData, specularRoughRT, null, "_SpecularRough", false, FilterManager.FO.AtrousIterations(Mathf.Max(FilterManager.Instance.atrousIterations - 2, 0)));

            photonRenderingData.cmd.SetComputeTextureParam(radianceCompute, kernelSpecularRoughMix, "_GBufferMetallicRT", gbufferMetallicRT);
            photonRenderingData.cmd.SetComputeTextureParam(radianceCompute, kernelSpecularRoughMix, "_SpecularRT", specularRT);
            photonRenderingData.cmd.SetComputeTextureParam(radianceCompute, kernelSpecularRoughMix, "_SpecularRoughRT", specularRoughRT);
            CommandBufferHelper.DispatchCompute_RT(photonRenderingData.cmd, radianceCompute, specularRT, kernelSpecularRoughMix, 8);
            ExtendSPRT(photonRenderingData, specularRT, specularExtendRT);
        }
        public void DLTemporalReuse(PhotonRenderingData photonRenderingData)
        {
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            CommandBuffer cmd = photonRenderingData.cmd;
            int kernelTemporalReuse = radianceCompute.FindKernel("DLTemporalReuse");
            SetRadianceCascadesCSData(photonRenderingData, kernelTemporalReuse);
            cmd.SetComputeTextureParam(radianceCompute, kernelTemporalReuse, "_MotionVectorRT", photonRenderingData.motionVectorRT);
           
            int threadX = Mathf.CeilToInt(photonRenderingData.DownResolution.x / 8f);
            int threadY = Mathf.CeilToInt(photonRenderingData.DownResolution.y / 8f);
            cmd.DispatchCompute(radianceCompute, kernelTemporalReuse, threadX, threadY, 1);
        }
        public void DLSpatialReuse(PhotonRenderingData photonRenderingData)
        {
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            CommandBuffer cmd = photonRenderingData.cmd;
            int kernelSpatialReuse = radianceCompute.FindKernel("DLSpatialReuse");
            SetRadianceCascadesCSData(photonRenderingData, kernelSpatialReuse);

            int threadX = Mathf.CeilToInt(photonRenderingData.DownResolution.x / 8f);
            int threadY = Mathf.CeilToInt(photonRenderingData.DownResolution.y / 8f);
            cmd.DispatchCompute(radianceCompute, kernelSpatialReuse, threadX, threadY, 1);
        }
        public void TemporalReuse(PhotonRenderingData photonRenderingData)
        {
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            CommandBuffer cmd = photonRenderingData.cmd;
            int kernelTemporalReuse = radianceCompute.FindKernel("TemporalReuse");

            SetRadianceCascadesCSData(photonRenderingData, kernelTemporalReuse);
            cmd.SetComputeTextureParam(radianceCompute, kernelTemporalReuse, "_MotionVectorRT", photonRenderingData.motionVectorRT);

            int threadX = Mathf.CeilToInt(radianceData.radianceCascadesRTSize.x / 8f);
            int threadY = Mathf.CeilToInt(radianceData.radianceCascadesRTSize.y / 8f);
            cmd.DispatchCompute(radianceCompute, kernelTemporalReuse, threadX, threadY, 1);
        }
        public void SpatialReuse(PhotonRenderingData photonRenderingData)
        {
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            CommandBuffer cmd = photonRenderingData.cmd;
            int kernelSpatialReuse = radianceCompute.FindKernel("SpatialReuse");
            SetRadianceCascadesCSData(photonRenderingData, kernelSpatialReuse);
            cmd.SetComputeBufferParam(radianceCompute, kernelSpatialReuse, "_ReservoirBuffer", reservoirBuffer);


            int threadX = Mathf.CeilToInt(radianceData.radianceCascadesRTSize.x / 8f);
            int threadY = Mathf.CeilToInt(radianceData.radianceCascadesRTSize.y / 8f);
            cmd.DispatchCompute(radianceCompute, kernelSpatialReuse, threadX, threadY, 1);
        }
        #endregion



        public void ForeachRadianceFeature(Action<IRadianceFeature> action)
        {
            foreach(IRadianceFeature radianceFeature in radianceFeatures)
            {
                action(radianceFeature);
            }
        }
        public void Release()
        {
            historyDLBuffer?.Release();
            reservoirDLBuffer?.Release();
            temporalDLReservoir?.Release();
            historyBuffer?.Release();
            reservoirBuffer?.Release();
            temporalReservoir?.Release();
            visibleLightCounter?.Release();
            lightsBuffer?.Release();
            cullLightsBuffer?.Release();
            readbackLightCounter?.Release();
            GameObject.DestroyImmediate(shBufferRT);
        }
        public void GetGBuffer(ScriptableRenderContext context, Camera camera, CommandBuffer cmd)
        {
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            int resolution = RadianceManager.Instance.resolution;
            int2 gbufferResolution = new int2(camera.pixelWidth, camera.pixelHeight);
            gbufferEmissionRT = RTManager.Instance.GetAdjustableRT("GBufferEmissionRT", gbufferResolution.x, gbufferResolution.y, RenderTextureFormat.ARGBFloat, TextureWrapMode.Clamp, FilterMode.Trilinear, depth: 16); // xyz = emission
            gbufferMetallicRT = RTManager.Instance.GetAdjustableRT("GBufferMetallicRT", gbufferResolution.x, gbufferResolution.y, RenderTextureFormat.ARGBFloat, TextureWrapMode.Clamp, FilterMode.Trilinear, depth: 16); // xyz = metallic w = smoothness
            gbufferAlbedoRT = RTManager.Instance.GetAdjustableRT("GBufferAlbedoRT", gbufferResolution.x, gbufferResolution.y, RenderTextureFormat.ARGBFloat, TextureWrapMode.Clamp, FilterMode.Trilinear, depth: 16); // xyzw = Albedo
            
            cmd.SetViewMatrix(camera.worldToCameraMatrix);
            cmd.SetProjectionMatrix(camera.projectionMatrix);

            cmd.SetRenderTarget(gbufferEmissionRT);
            cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
            CommandBufferHelper.RenderWithShader(cmd, context, camera, RadianceManager.Instance.emissionShader, RenderQueueRange.opaque);
            cmd.SetRenderTarget(gbufferMetallicRT);
            cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
            CommandBufferHelper.RenderWithShader(cmd, context, camera, RadianceManager.Instance.metallicShader, RenderQueueRange.opaque);
            
            cmd.SetRenderTarget(gbufferAlbedoRT);
            cmd.ClearRenderTarget(true, true, new Color(1, 1, 1, 1));
            //CommandBufferHelper.RenderWithShader(cmd, context, camera, RadianceManager.Instance.albedoShader, RenderQueueRange.opaque);
            
        }
        public void HandleLightData(PhotonRenderingData photonRenderingData)
        {
            lastRefeshLightCameraPosition = photonRenderingData.camera.transform.position;
            lightData = RadianceManager.Instance.QueryLocalLights(photonRenderingData.camera.transform.position);
            if (lightCount != lightData.Length || lightsBuffer == null)
            {
                lightCount = Mathf.Max(lightData.Length, 1);
                lightsBuffer?.Release();
                lightsBuffer = new ComputeBuffer(lightCount, Marshal.SizeOf<LightBufferData>());
                cullLightsBuffer?.Release();
                cullLightsBuffer = new ComputeBuffer(lightCount, Marshal.SizeOf<LightBufferData>());
                visibleLightCounter?.Release();
                visibleLightCounter = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Counter);
                readbackLightCounter?.Release();
                readbackLightCounter = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
            }
            lightsBuffer.SetData(lightData);
        }
        public void GenerateShadowRT(PhotonRenderingData photonRenderingData)
        {
            shadowRT = RTManager.Instance.GetAdjustableRT("ShadowRT", photonRenderingData.activeRT.width, photonRenderingData.activeRT.height, RenderTextureFormat.RFloat, TextureWrapMode.Clamp, FilterMode.Trilinear, depth: 16); // x = shadow

            CommandBuffer cmd = photonRenderingData.cmd;
            Camera camera = photonRenderingData.camera;
            float near = camera.nearClipPlane;
            float far = camera.farClipPlane;
            Vector4 zBufferParams;
            zBufferParams = new Vector4(-1 + far / near, 1, (-1 + far / near) / far, 1 / far);


            cmd.SetGlobalVector( "_CameraPosition", camera.transform.position);
            cmd.SetGlobalVector("_ZBufferParamsP", zBufferParams);
            cmd.SetGlobalMatrix("_ProjectionMatrixInverse", camera.projectionMatrix.inverse);
            cmd.SetGlobalMatrix("_ViewMatrixInverse", camera.cameraToWorldMatrix);
            cmd.SetGlobalMatrix("_ProjectionMatrix", camera.projectionMatrix);
            cmd.SetGlobalMatrix("_ViewMatrix", camera.worldToCameraMatrix);
            cmd.SetGlobalTexture("_DepthRT", photonRenderingData.depthRT);
            cmd.SetRenderTarget(shadowRT);
            cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
            cmd.Blit(null, shadowRT, RadianceManager.Instance.shadowMat);

        }
        public void ExtendSPRT(PhotonRenderingData photonRenderingData, RenderTexture origin, RenderTexture target)
        {

            CommandBuffer cmd = photonRenderingData.cmd;
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            int kernel = radianceCompute.FindKernel("ExtendSPRT");
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_ExtendOriginRT", target);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_OriginRT", origin);
            SetRadianceCascadesCSData(photonRenderingData, kernel);
            CommandBufferHelper.DispatchCompute_RT(cmd, radianceCompute, target, kernel, 8);

        }
        public void ExtendRT(PhotonRenderingData photonRenderingData, RenderTexture origin, RenderTexture target)
        {

            CommandBuffer cmd = photonRenderingData.cmd;
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            int kernel = radianceCompute.FindKernel("ExtendRT");
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_ExtendOriginRT", target);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_OriginRT", origin);
            SetRadianceCascadesCSData(photonRenderingData, kernel);
            CommandBufferHelper.DispatchCompute_RT(cmd, radianceCompute, target, kernel, 8);

        }
        public void ExtendIDRT(PhotonRenderingData photonRenderingData, RenderTexture origin, RenderTexture target)
        {

            CommandBuffer cmd = photonRenderingData.cmd;
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            int kernel = radianceCompute.FindKernel("ExtendIDRT");
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_ExtendOriginRT", target);
            cmd.SetComputeTextureParam(radianceCompute, kernel, "_OriginRT", origin);
            SetRadianceCascadesCSData(photonRenderingData, kernel);
            CommandBufferHelper.DispatchCompute_RT(cmd, radianceCompute, target, kernel, 8);

        }
        
        public void InitBuffer(PhotonRenderingData photonRenderingData)
        {
            CreateRadianceData(photonRenderingData);
            photonRenderingData.metallicRT = gbufferMetallicRT;
            //photonRenderingData.cmd.SetRenderTarget(gbufferAlbedoRT);
            //photonRenderingData.cmd.ClearRenderTarget(true, true, Color.white);
            traceBlendMask = new TraceBlendMask(photonRenderingData, "TraceBlendMask");
            traceBlendMaskD = new TraceBlendMask(photonRenderingData, "TraceBlendMaskD");
            photonRenderingData.albedoRT = gbufferAlbedoRT;
            int pixelCount = photonRenderingData.DownResolution.x * photonRenderingData.DownResolution.y;
            if (reservoirDLBuffer == null || reservoirDLBuffer.count != pixelCount)
            {
                reservoirDLBuffer?.Release();
                reservoirDLBuffer = new ComputeBuffer(
                pixelCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(ReservoirLightSample)),
                ComputeBufferType.Default);
            }
            int historyDLBufferPixelCount = photonRenderingData.DownResolution.x * photonRenderingData.DownResolution.y;
            if (historyDLBuffer == null || historyDLBuffer.count != historyDLBufferPixelCount)
            {
                historyDLBuffer?.Release();
                historyDLBuffer = new ComputeBuffer(historyDLBufferPixelCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(ReservoirLightSample)),
                ComputeBufferType.Default);
                int kernelFillDL = RadianceManager.Instance.radianceCompute.FindKernel("FillDLBuffer");
                photonRenderingData.cmd.SetComputeBufferParam(RadianceManager.Instance.radianceCompute, kernelFillDL, "_DLHistoryBuffer", historyDLBuffer);
                photonRenderingData.cmd.DispatchCompute(RadianceManager.Instance.radianceCompute, kernelFillDL, Mathf.CeilToInt(historyDLBufferPixelCount / 64f), 1, 1);
            }
            int temporalReservoirPixelCount = photonRenderingData.DownResolution.x * photonRenderingData.DownResolution.y;
            if (temporalDLReservoir == null || temporalDLReservoir.count != temporalReservoirPixelCount)
            {
                temporalDLReservoir?.Release();
                temporalDLReservoir = new ComputeBuffer(temporalReservoirPixelCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(ReservoirLightSample)),
                ComputeBufferType.Raw);
            }
        }
        private void ExecutePiplineFeatureWithProfile(PhotonRenderingData photonRenderingData, Action action, string name)
        {
            photonRenderingData.cmd.BeginSample(name);
            action?.Invoke();
            photonRenderingData.cmd.EndSample(name);
        }
        public void ExecuteRadianceCascades(PhotonRenderingData photonRenderingData)
        {

            InitBuffer(photonRenderingData);
            HandleLightData(photonRenderingData);


            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => photonRenderingData.ssgiData = HZBManager.Instance.GenerateHZB(photonRenderingData, null),
                "GenerateHZB");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => GenerateShadowRT(photonRenderingData),
                "GenerateShadowRT");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => SampleGlobalProbes(photonRenderingData),
                "SampleGlobalProbes");


            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => SpawnRadianceCascadesDirection(photonRenderingData),
                "SpawnRadianceCascadesDirection");
            if (!DebugManager.Instance.stopCullLight)
            {
                ExecutePiplineFeatureWithProfile(photonRenderingData,
                    () => CullLights(photonRenderingData),
                    "CullLights");
            }


            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => ForeachRadianceFeature(
                        rf => rf.GetRadianceSample(this, photonRenderingData)),
                "ForeachRadianceFeature_GetRadianceSample");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => TemporalReuse(photonRenderingData),
                "TemporalReuse");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => RadianceFeedback(photonRenderingData),
                "RadianceFeedback");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => SpatialReuse(photonRenderingData),
                "SpatialReuse");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => HandleReSTIROutPutRT(photonRenderingData),
                "HandleReSTIROutPutRT");
            /* ReSTIRGI 

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => TemporalReuse(photonRenderingData),
                "TemporalReuse");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => SpatialReuse(photonRenderingData),
                "SpatialReuse");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => RadianceFeedback(photonRenderingData),
                "RadianceFeedback");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => HandleCascadesRT(photonRenderingData),
                "HandleCascadesRT");
            */


            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => DLTemporalReuse(photonRenderingData),
                "DLTemporalReuse");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => DLSpatialReuse(photonRenderingData),
                "DLSpatialReuse");


            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => Filter(photonRenderingData),
                "Filter");



            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => Specular(photonRenderingData),
                "Specular");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => DirectLight(photonRenderingData),
                "DirectLight");


            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => RadianceApply(photonRenderingData),
                "RadianceApply");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => ForeachRadianceFeature(
                        rf => rf.RadainceFeedback(this, photonRenderingData)),
                "ForeachRadianceFeature_RadainceFeedback");


        }
    }


}
