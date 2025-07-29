using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotonSystem
{
    public class PhotonGIRenderPipeline : PhotonSingleton<PhotonGIRenderPipeline>
    {

        public void ProcessRenderPipeline(ScriptableRenderContext context, PhotonRenderingData photonRenderingData)
        {

        }
        public void ScenePreprocessing(ScriptableRenderContext context, Camera camera)//beginCameraRendering
        {
            CommandBuffer cmd = CommandBufferPool.Get("ScenePreprocessing");
            RadianceControl radianceControl = RadianceManager.Instance.GetRadianceControl(camera);
            radianceControl.GetGBuffer(context, camera, cmd);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += ScenePreprocessing;
        }
        public void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= ScenePreprocessing;
        }
    }
}
