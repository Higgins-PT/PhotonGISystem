using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.UI;
using UnityEngine.Rendering.RendererUtils;
using Unity.Mathematics;

namespace PhotonSystem {
    public class PhotonRendererFeature : ScriptableRendererFeature
    {
        class PhotonRenderPass : ScriptableRenderPass
        {
            public static Material motionVectorMat;
            private class PassData
            {
                public TextureHandle renderingTexture;
                public TextureHandle activeColorTexture;
                public TextureHandle normalTexture;
                public TextureHandle depthTexture;
                public Camera camera;
                public RenderGraph renderGraph;
                public ContextContainer frameData;
            }
            public static bool IsSceneCamera(Camera cam)
            {
                if (cam == null) return false;
#if UNITY_EDITOR
                switch (cam.cameraType)
                {
                    case CameraType.Preview:
                    case CameraType.Reflection:
                    case CameraType.VR:
                        return false;
                }
#endif
                if (cam.cameraType == CameraType.SceneView && DebugManager.Instance.enableSceneViewCameraGI)
                    return true;

                if (cam.cameraType == CameraType.Game)
                    return true;

                return false;
            }
            static void ExecutePass(PassData data, UnsafeGraphContext context)
            {
                if (IsSceneCamera(data.camera))
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    cmd.Blit(data.activeColorTexture, data.renderingTexture);
                    RenderTexture renderingTexture = ((RenderTexture)data.renderingTexture);
                    if (data.camera == null)
                    {
                        return;
                    }
                    RenderTexture motionVectorRT = RTManager.Instance.GetRT("MotionVectorRT" + data.camera.GetInstanceID(), renderingTexture.width, renderingTexture.height, RenderTextureFormat.RGFloat);
                    cmd.Blit(null, motionVectorRT, motionVectorMat);
                    PhotonRenderingData photonRenderingData = new PhotonRenderingData(data.renderingTexture, data.normalTexture, data.depthTexture, data.activeColorTexture, motionVectorRT, cmd, data.camera, 1 / (float)DebugManager.Instance.resolution, 1 / (float)DebugManager.Instance.specularResolution, 1 / (float)DebugManager.Instance.indirectResolution);
                    DebugManager.Instance.DebugOutput(photonRenderingData);
                    cmd.Blit(data.renderingTexture, data.activeColorTexture);
                }
            }
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                const string passName = "Photon Render Pass";
                using (var builder = renderGraph.AddUnsafePass<PassData>(passName, out var passData))
                {
                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                    TextureDesc rgDesc = new TextureDesc(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height);
                    rgDesc.name = "_CameraDepthTexture";
                    rgDesc.dimension = cameraData.cameraTargetDescriptor.dimension;
                    rgDesc.clearBuffer = false;
                    rgDesc.autoGenerateMips = true;
                    rgDesc.useMipMap = true;
                    rgDesc.msaaSamples = MSAASamples.None;
                    rgDesc.filterMode = FilterMode.Bilinear;
                    rgDesc.wrapMode = TextureWrapMode.Clamp;
                    rgDesc.enableRandomWrite = true;
                    rgDesc.bindTextureMS = cameraData.cameraTargetDescriptor.bindMS;
                    rgDesc.colorFormat = cameraData.cameraTargetDescriptor.graphicsFormat;
                    rgDesc.depthBufferBits = 0;
                    rgDesc.isShadowMap = false;
                    passData.renderingTexture = renderGraph.CreateTexture(rgDesc);
                    passData.activeColorTexture = resourceData.activeColorTexture;
                    passData.camera = cameraData.camera;
                    passData.normalTexture = resourceData.cameraNormalsTexture;
                    passData.depthTexture = resourceData.cameraDepthTexture;
                    passData.renderGraph = renderGraph;
                    passData.frameData = frameData;
                    builder.UseTexture(passData.activeColorTexture, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.renderingTexture, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.normalTexture, AccessFlags.Read);
                    builder.UseTexture(passData.depthTexture, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    ConfigureInput(ScriptableRenderPassInput.Motion | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
                    builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
                }
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
            }
        }

        PhotonRenderPass m_ScriptablePass;
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        /// <inheritdoc/>
        public override void Create()
        {
            m_ScriptablePass = new PhotonRenderPass();
            PhotonRenderPass.motionVectorMat = new Material(Shader.Find("PhotonSystem/MotionVector"));
            m_ScriptablePass.renderPassEvent = renderPassEvent;
        }


        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(m_ScriptablePass);
        }
    }
    public class PhotonRenderingData
    {
        public PhotonRenderingData(RenderTexture targetRT, RenderTexture normalRT, RenderTexture depthRT, RenderTexture activeRT, RenderTexture motionVectorRT, CommandBuffer cmd, Camera camera, float scale, float specularScale, float indirectScale)
        {
            this.targetRT = targetRT;
            this.normalRT = normalRT;
            this.depthRT = depthRT;
            this.cmd = cmd;
            this.camera = camera;
            this.motionVectorRT = motionVectorRT;
            this.camera = camera;
            this.activeRT = activeRT;
            this.scale = scale;
            this.specularScale = specularScale;
            this.indirectScale = indirectScale;
        }
        public RenderTexture targetRT;
        public RenderTexture normalRT;
        public RenderTexture depthRT;
        public RenderTexture activeRT;
        public RenderTexture motionVectorRT;
        public RenderTexture albedoRT;
        public RenderTexture metallicRT;
        public RenderTexture sparseLightLevelRT;
        public Vector2Int DownResolution { get { return new Vector2Int(Mathf.CeilToInt(targetRT.width * scale), Mathf.CeilToInt(targetRT.height * scale)); } }
        public Vector2Int SpecularDownResolution { get { return new Vector2Int(Mathf.CeilToInt(targetRT.width * specularScale), Mathf.CeilToInt(targetRT.height * specularScale)); } }

        public Vector2Int IndirectScaleDownResolution { get { return new Vector2Int(Mathf.CeilToInt(targetRT.width * indirectScale), Mathf.CeilToInt(targetRT.height * indirectScale)); } }
        public HZBManager.SSGIData ssgiData;
        public CommandBuffer cmd;
        public Camera camera;
        public float scale;
        public float specularScale;
        public float indirectScale;
    }
}