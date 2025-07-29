using UnityEngine;
using UnityEngine.Rendering;
namespace PhotonSystem
{
    public class TraceBlendMask
    {
        public RenderTexture blendMaskRTPing;//RHalf
        public RenderTexture blendMaskRTPong;//RHalf
        public bool pingpong = false;
        private string TBMKey = "";
        public (RenderTexture input, RenderTexture output) GetPingPongRT()
        {
            pingpong = !pingpong;
            if (pingpong)
            {
                return (blendMaskRTPing, blendMaskRTPong);
            }
            else
            {
                return (blendMaskRTPong, blendMaskRTPing);
            }
        }
        public RenderTexture OutputRT { get { return pingpong ? blendMaskRTPong : blendMaskRTPing; } }
        public TraceBlendMask(PhotonRenderingData photonRenderingData, string name)
        {
            TBMKey = photonRenderingData.camera.GetInstanceID() + name;
        }
        public void SmoothBlendMask(PhotonRenderingData photonRenderingData, RenderTexture renderTexture, int mipLevel)
        {
            CommandBuffer cmd = photonRenderingData.cmd;
            ComputeShader computeShader = ResourceManager.BlendMaskCompute;
            string key = photonRenderingData.camera.GetInstanceID() + TBMKey;
            blendMaskRTPing = RTManager.Instance.GetAdjustableRT(key + "input", renderTexture.width, renderTexture.height, RenderTextureFormat.RHalf, useMipMap: true);
            blendMaskRTPong = RTManager.Instance.GetAdjustableRT(key + "output", renderTexture.width, renderTexture.height, RenderTextureFormat.RHalf, useMipMap: true);
            int kernelMaskSet = computeShader.FindKernel("MaskSet");
            int kernelSmoothBlendMask = computeShader.FindKernel("SmoothBlendMask");
            var pingpongRT = GetPingPongRT();
            cmd.SetComputeTextureParam(computeShader, kernelMaskSet, "_SetData", renderTexture);
            cmd.SetComputeTextureParam(computeShader, kernelMaskSet, "_MaskOutput", pingpongRT.output);
            CommandBufferHelper.DispatchCompute_RT(cmd, computeShader, pingpongRT.output, kernelMaskSet, 8);
            pingpongRT = GetPingPongRT();
            cmd.GenerateMips(pingpongRT.input);
            cmd.SetComputeTextureParam(computeShader, kernelSmoothBlendMask, "_MaskInput", pingpongRT.input);
            Vector2 size = new Vector2(pingpongRT.input.width, pingpongRT.input.height);
            cmd.SetComputeTextureParam(computeShader, kernelSmoothBlendMask, "_MaskOutput", pingpongRT.output);
            cmd.SetComputeVectorParam(computeShader, "_InputSize", size);
            cmd.SetComputeIntParam(computeShader, "_MipLevel", mipLevel);
            CommandBufferHelper.DispatchCompute_RT(cmd, computeShader, pingpongRT.output, kernelSmoothBlendMask, 8);
        }
        public void SetBlendMask(CommandBuffer cmd, ComputeShader computeShader, int kernel)
        {
            cmd.SetComputeTextureParam(computeShader, kernel, "_BlendMaskRT", OutputRT);
        }
    }
}