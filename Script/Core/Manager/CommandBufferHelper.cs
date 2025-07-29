using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using static Unity.Burst.Intrinsics.X86.Avx;

namespace PhotonSystem
{
    public static class CommandBufferHelper
    {
        public static void DispatchCompute_RT(CommandBuffer cmd, ComputeShader computeShader, RenderTexture source, int kernel, int stride)
        {
            int threadGroupX = Mathf.CeilToInt(source.width / (float)stride);
            int threadGroupY = Mathf.CeilToInt(source.height / (float)stride);
            cmd.DispatchCompute(computeShader, kernel, threadGroupX, threadGroupY, 1);
        }
        public static void DispatchCompute_RT(CommandBuffer cmd, ComputeShader computeShader, RenderTexture source, string kernel, int stride)
        {
            int threadGroupX = Mathf.CeilToInt(source.width / (float)stride);
            int threadGroupY = Mathf.CeilToInt(source.height / (float)stride);
            cmd.DispatchCompute(computeShader, computeShader.FindKernel(kernel), threadGroupX, threadGroupY, 1);
        }
        public static void ExecuteComputeWithCmd(Action<CommandBuffer> action)
        {
            CommandBuffer cmd = new CommandBuffer();
            action.Invoke(cmd);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }
        public static void RenderWithShader(CommandBuffer cmd, ScriptableRenderContext context, Camera camera, Shader shader, RenderQueueRange renderQueueRange)
        {
            camera.TryGetCullingParameters(out ScriptableCullingParameters cullParams);
            CullingResults cullResults = context.Cull(ref cullParams);
            ShaderTagId[] shaderTags = new ShaderTagId[]
{
                        new ShaderTagId("UniversalForward"),
                        new ShaderTagId("SRPDefaultUnlit")
};
            RendererListDesc rendererListDesc = new RendererListDesc(shaderTags, cullResults, camera)
            {
                sortingCriteria = SortingCriteria.CommonOpaque,
                renderQueueRange = renderQueueRange,
                overrideShader = shader,
                rendererConfiguration = PerObjectData.None,
            };
            RendererList rendererList = context.CreateRendererList(rendererListDesc);
            cmd.DrawRendererList(rendererList);
        }

    }
}