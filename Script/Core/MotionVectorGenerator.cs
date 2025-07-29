using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace PhotonSystem {
    public class MotionVectorGenerator
    {
        private Matrix4x4 prevVP = Matrix4x4.identity;
        private bool hasPrev = false;
        public RenderTexture GenerateMotionVector(Camera camera, RenderTexture depthRT, RenderTexture normalRT, RenderTexture historyNormalDepthRT, CommandBuffer cmd, int cascadeStride)
        {
            int screenWidth = historyNormalDepthRT.width;
            int screenHeight = historyNormalDepthRT.height;

 
            int outputWidth = screenWidth / cascadeStride;
            int outputHeight = screenHeight / cascadeStride;

            RenderTexture motionVectorRT = RTManager.Instance.GetAdjustableRT("motionVectorRT" + camera.GetInstanceID(), outputWidth, outputHeight, RenderTextureFormat.RGFloat);

            int cascadeStrideHalf = (int)Mathf.Floor(cascadeStride / 2f);
            ComputeShader motionCompute = ResourceManager.MotionVectorCompute;
            int kernel = motionCompute.FindKernel("GenerateMotionVector");


            cmd.SetComputeIntParam(motionCompute, "_CascadeStride", cascadeStride);
            cmd.SetComputeIntParam(motionCompute, "_CascadeStrideHalf", cascadeStrideHalf);

            float phiDepthStart = FilterManager.Instance.phiDepthStart;
            float phiDepthScale = FilterManager.Instance.phiDepthScale;
            float phiNormal = FilterManager.Instance.phiNormal;
            cmd.SetComputeFloatParam(motionCompute, "_PhiDepthStart", phiDepthStart);
            cmd.SetComputeFloatParam(motionCompute, "_PhiDepthScale", phiDepthScale);
            cmd.SetComputeFloatParam(motionCompute, "_PhiNormal", phiNormal);
            cmd.SetComputeFloatParam(motionCompute, "_ScreenWidth", screenWidth);
            cmd.SetComputeFloatParam(motionCompute, "_ScreenHeight", screenHeight);
            Matrix4x4 currVP = camera.projectionMatrix * camera.worldToCameraMatrix;
            Matrix4x4 currInvVP = currVP.inverse;

            Matrix4x4 prev = hasPrev ? prevVP : currVP;
            cmd.SetComputeMatrixParam(motionCompute, "_PrevVP", prev);
            cmd.SetComputeMatrixParam(motionCompute, "_CurrInvVP", currInvVP);

            Vector4 zBufferParams;
            float far = camera.farClipPlane;
            float near = camera.nearClipPlane;
            zBufferParams = new Vector4(-1 + far / near, 1, (-1 + far / near) / far, 1 / far);
            cmd.SetComputeVectorParam(motionCompute, "_ZBufferParams", zBufferParams);
            cmd.SetComputeTextureParam(motionCompute, kernel, "_HistoryNormalDepthRT", historyNormalDepthRT);
            cmd.SetComputeTextureParam(motionCompute, kernel, "_DepthRT", depthRT);
            cmd.SetComputeTextureParam(motionCompute, kernel, "_NormalRT", normalRT);
            cmd.SetComputeTextureParam(motionCompute, kernel, "_MotionVectorRT", motionVectorRT);

            int tgX = Mathf.CeilToInt((float)outputWidth / 8.0f);
            int tgY = Mathf.CeilToInt((float)outputHeight / 8.0f);
            cmd.DispatchCompute(motionCompute, kernel, tgX, tgY, 1);


            prevVP = currVP;
            hasPrev = true;
            
            return motionVectorRT;
        }
    }
}