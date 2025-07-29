using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityDenoiserPlugin;
using UnityEngine;
using UnityEngine.Device;
using UnityEngine.Rendering;
using UnityEngine.UIElements;


namespace PhotonSystem
{
    public class FilterManager : PhotonSingleton<FilterManager>
    {
        [Header("Compute Shader")]
        public ComputeShader filterCompute;
        public bool enableAdvanceDenoiser = false;
        public DenoiserType advanceDenoiserType = DenoiserType.OIDN;
        [Header("Temporal Filter Parameters")]
        public float clampSigma = 3.0f; 
        public float alphaColor = 0.2f;   
        public float alphaVar = 0.1f;    

        [Header("Atrous (Spatial) Filter Parameters")]
        public int stepSize = 1;           
        public int atrousIterations = 3;  
        public float phiNormal = 0.1f;    
        public float phiColor = 10.0f;   
        public float phiDepthStart = 0.1f;
        public float phiDepthScale = 0.04f;
        public Dictionary<Camera, MotionVectorGenerator> motionVectorGenerators = new Dictionary<Camera, MotionVectorGenerator>();
        public Dictionary<string, DenoiserPluginWrapper> denoiserPluginWrappers = new Dictionary<string, DenoiserPluginWrapper>();
        public enum FilterOptionType
        {
            ClampSigma,
            AlphaColor,
            AlphaVar,
            StepSize,
            AtrousIterations,
            PhiNormal,
            PhiColor,
            PhiDepthStart,
            PhiDepthScale,
        }
        public override void ReleaseSystem()
        {
            foreach (var d in denoiserPluginWrappers)
            {
                d.Value.Dispose();
            }
            denoiserPluginWrappers.Clear();
        }
        public readonly struct FilterOption
        {
            public readonly FilterOptionType type;
            public readonly float f;
            public FilterOption(FilterOptionType t, float value) { type = t; f = value; }
        }
        public static class FO
        {
            public static FilterOption ClampSigma(float v) => new(FilterOptionType.ClampSigma, v);
            public static FilterOption AlphaColor(float v) => new(FilterOptionType.AlphaColor, v);
            public static FilterOption AlphaVar(float v) => new(FilterOptionType.AlphaVar, v);
            public static FilterOption StepSize(int v) => new(FilterOptionType.StepSize, v);
            public static FilterOption AtrousIterations(int v) => new(FilterOptionType.AtrousIterations, v);
            public static FilterOption PhiNormal(float v) => new(FilterOptionType.PhiNormal, v);
            public static FilterOption PhiColor(float v) => new(FilterOptionType.PhiColor, v);
            public static FilterOption PhiDepthStart(float v) => new(FilterOptionType.PhiDepthStart, v);
            public static FilterOption PhiDepthScale(float v) => new(FilterOptionType.PhiDepthScale, v);
        }
        struct Local
        {
            public float clampSigma, alphaColor, alphaVar;
            public int stepSize, atrousIterations;
            public float phiNormal, phiColor, phiDepthStart, phiDepthScale;
        }
        void BuildLocalSettings(out Local s, FilterOption[] opts)
        {
            s = new Local
            {
                clampSigma = clampSigma,
                alphaColor = alphaColor,
                alphaVar = alphaVar,
                stepSize = stepSize,
                atrousIterations = atrousIterations,
                phiNormal = phiNormal,
                phiColor = phiColor,
                phiDepthStart = phiDepthStart,
                phiDepthScale = phiDepthScale
            };
            foreach (var o in opts)
            {
                switch (o.type)
                {
                    case FilterOptionType.ClampSigma: s.clampSigma = o.f; break;
                    case FilterOptionType.AlphaColor: s.alphaColor = o.f; break;
                    case FilterOptionType.AlphaVar: s.alphaVar = o.f; break;
                    case FilterOptionType.StepSize: s.stepSize = Mathf.RoundToInt(o.f); break;
                    case FilterOptionType.AtrousIterations: s.atrousIterations = Mathf.RoundToInt(o.f); break;
                    case FilterOptionType.PhiNormal: s.phiNormal = o.f; break;
                    case FilterOptionType.PhiColor: s.phiColor = o.f; break;
                    case FilterOptionType.PhiDepthStart: s.phiDepthStart = o.f; break;
                    case FilterOptionType.PhiDepthScale: s.phiDepthScale = o.f; break;
                }
            }
        }

        public MotionVectorGenerator GetMotionVectorGenerator(Camera camera)
        {
            if (motionVectorGenerators.ContainsKey(camera))
            {
                return motionVectorGenerators[camera];
            }
            MotionVectorGenerator motionVectorGenerator = new MotionVectorGenerator();
            motionVectorGenerators[camera] = motionVectorGenerator;
            return motionVectorGenerator;
        }
        public void ApplyAtrousFilter(
            PhotonRenderingData prd,
            RenderTexture radianceRT,
            RenderTexture aoRT,
            string key,
            bool feedbackToTime = true,
            params FilterOption[] opts)
        {
            BuildLocalSettings(out var P, opts);
            RenderTexture depthRT = prd.depthRT;
            RenderTexture normalRT = prd.normalRT;
            CommandBuffer cmd = prd.cmd;
            Camera cam = prd.camera;
            cmd.BeginSample("ApplyAtrousFilter");
            int2 screenPixel = new int2(radianceRT.width, radianceRT.height);

            RenderTexture historyColorRT = RTManager.Instance.GetAdjustableRT(
                cam.GetInstanceID() + "_HistoryColorRT" + key,
                screenPixel.x, screenPixel.y, RenderTextureFormat.ARGBFloat);
            RenderTexture varianceRT = RTManager.Instance.GetAdjustableRT(
                cam.GetInstanceID() + "_VarianceRT" + key,
                screenPixel.x, screenPixel.y, RenderTextureFormat.RFloat);

            RenderTexture tempRT = RTManager.Instance.GetRT(
                "_TempRTARGBFloat", screenPixel.x, screenPixel.y, RenderTextureFormat.ARGBFloat);
            cmd.Blit(radianceRT, tempRT);
            if (feedbackToTime) cmd.Blit(tempRT, historyColorRT);

            RenderTexture pingRT = tempRT;
            RenderTexture pongRT = RTManager.Instance.GetRT("_TempRT_Atrous",
                screenPixel.x, screenPixel.y, RenderTextureFormat.ARGBFloat);

            int kernelAtrous = filterCompute.FindKernel("AtrousFilter");
            Vector4 zBufferParams;
            float far = cam.farClipPlane;
            float near = cam.nearClipPlane;
            zBufferParams = new Vector4(-1 + far / near, 1, (-1 + far / near) / far, 1 / far);

            cmd.SetComputeIntParams(filterCompute, "_ScreenWidth", screenPixel.x);
            cmd.SetComputeIntParams(filterCompute, "_ScreenHeight", screenPixel.y);
            cmd.SetComputeVectorParam(filterCompute, "_ZBufferParams", zBufferParams);
            cmd.SetComputeTextureParam(filterCompute, kernelAtrous, "_DepthRT", depthRT);
            cmd.SetComputeTextureParam(filterCompute, kernelAtrous, "_NormalRT", normalRT);
            cmd.SetComputeTextureParam(filterCompute, kernelAtrous, "_VarianceRT", varianceRT);
            cmd.SetComputeVectorParam(filterCompute, "_MotionToRTScale", new Vector2(Mathf.CeilToInt((float)prd.motionVectorRT.width / (float)screenPixel.x), Mathf.CeilToInt((float)prd.motionVectorRT.height / (float)screenPixel.y)));
            if (aoRT != null)
            {
                cmd.SetComputeTextureParam(filterCompute, kernelAtrous, "_AORT", aoRT);
                cmd.SetComputeIntParams(filterCompute, "_AORTSize", new[] { aoRT.width, aoRT.height });
                cmd.SetComputeIntParam(filterCompute, "_EnableAO", 1);
            }
            else
            {
                cmd.SetComputeTextureParam(filterCompute, kernelAtrous, "_AORT", radianceRT);
                cmd.SetComputeIntParam(filterCompute, "_EnableAO", 0);
            }

            cmd.SetComputeFloatParam(filterCompute, "_PhiNormal", P.phiNormal);
            cmd.SetComputeFloatParam(filterCompute, "_PhiColor", P.phiColor);
            cmd.SetComputeFloatParam(filterCompute, "_PhiDepthStart", P.phiDepthStart);
            cmd.SetComputeFloatParam(filterCompute, "_PhiDepthScale", P.phiDepthScale);

            int tgX = Mathf.CeilToInt(screenPixel.x / 8.0f);
            int tgY = Mathf.CeilToInt(screenPixel.y / 8.0f);

            for (int i = 0; i < P.atrousIterations; i++)
            {
                int currentStep = 1 << i;
                cmd.SetComputeIntParam(filterCompute, "_StepSize", currentStep);
                cmd.SetComputeFloatParam(filterCompute, "_StepSizeWeight", 1f);

                cmd.SetComputeTextureParam(filterCompute, kernelAtrous, "_RadianceRT", pingRT);
                cmd.SetComputeTextureParam(filterCompute, kernelAtrous, "_TargetRT", pongRT);

                cmd.DispatchCompute(filterCompute, kernelAtrous, tgX, tgY, 1);

                (pingRT, pongRT) = (pongRT, pingRT);
            }

            cmd.Blit(pingRT, radianceRT);
            cmd.EndSample("ApplyAtrousFilter");
        }
        private void PingPongWALR(CommandBuffer cmd, ComputeShader cs, int kernel, ref bool pingpong, RenderTexture XX1S, RenderTexture XY1S, RenderTexture XX2S, RenderTexture XY2S, RenderTexture XX1L, RenderTexture XY1L, RenderTexture XX2L, RenderTexture XY2L)
        {
            if (pingpong)
            {
                cmd.SetComputeTextureParam(cs, kernel, "_XXBuffer1S", XX1L);
                cmd.SetComputeTextureParam(cs, kernel, "_XYBuffer1S", XY1L);
                cmd.SetComputeTextureParam(cs, kernel, "_XXBuffer2S", XX2L);
                cmd.SetComputeTextureParam(cs, kernel, "_XYBuffer2S", XY2L);

                cmd.SetComputeTextureParam(cs, kernel, "_XXBuffer1L", XX1S);
                cmd.SetComputeTextureParam(cs, kernel, "_XYBuffer1L", XY1S);
                cmd.SetComputeTextureParam(cs, kernel, "_XXBuffer2L", XX2S);
                cmd.SetComputeTextureParam(cs, kernel, "_XYBuffer2L", XY2S);
            }
            else
            {
                cmd.SetComputeTextureParam(cs, kernel, "_XXBuffer1S", XX1S);
                cmd.SetComputeTextureParam(cs, kernel, "_XYBuffer1S", XY1S);
                cmd.SetComputeTextureParam(cs, kernel, "_XXBuffer2S", XX2S);
                cmd.SetComputeTextureParam(cs, kernel, "_XYBuffer2S", XY2S);

                cmd.SetComputeTextureParam(cs, kernel, "_XXBuffer1L", XX1L);
                cmd.SetComputeTextureParam(cs, kernel, "_XYBuffer1L", XY1L);
                cmd.SetComputeTextureParam(cs, kernel, "_XXBuffer2L", XX2L);
                cmd.SetComputeTextureParam(cs, kernel, "_XYBuffer2L", XY2L);
            }
            pingpong = !pingpong;
        }
        public void ApplyWALR(
            PhotonRenderingData prd,
            RenderTexture radianceRT,
            string key,
            params FilterOption[] opts)
        {

            BuildLocalSettings(out var P, opts);
            var cam = prd.camera;
            var cmd = prd.cmd;
            cmd.BeginSample("WALR");
            var cs = filterCompute;
            int2 screen = new int2(radianceRT.width, radianceRT.height);
            int tgX = Mathf.CeilToInt(screen.x / 8.0f);
            int tgY = Mathf.CeilToInt(screen.y / 8.0f);

            int2 screenPixel = new int2(radianceRT.width, radianceRT.height);

            bool pingPong = false;
            RenderTexture XX1S = RTManager.Instance.GetRT("_XXBuffer1S" + key, screen.x, screen.y,
                                                          RenderTextureFormat.ARGBFloat);
            RenderTexture XX2S = RTManager.Instance.GetRT("_XXBuffer2S" + key, screen.x, screen.y,
                                                          RenderTextureFormat.ARGBFloat);
            RenderTexture XY1S = RTManager.Instance.GetRT("_XYBuffer1S" + key, screen.x, screen.y,
                                                          RenderTextureFormat.ARGBFloat);
            RenderTexture XY2S = RTManager.Instance.GetRT("_XYBuffer2S" + key, screen.x, screen.y,
                                                          RenderTextureFormat.ARGBFloat);

            RenderTexture XX1L = RTManager.Instance.GetRT("_XXBuffer1L" + key, screen.x, screen.y,
                                                          RenderTextureFormat.ARGBFloat);
            RenderTexture XX2L = RTManager.Instance.GetRT("_XXBuffer2L" + key, screen.x, screen.y,
                                                          RenderTextureFormat.ARGBFloat);
            RenderTexture XY1L = RTManager.Instance.GetRT("_XYBuffer1L" + key, screen.x, screen.y,
                                                          RenderTextureFormat.ARGBFloat);
            RenderTexture XY2L = RTManager.Instance.GetRT("_XYBuffer2L" + key, screen.x, screen.y,
                                                          RenderTextureFormat.ARGBFloat);
            RenderTexture tempRT = RTManager.Instance.GetRT("_TempRT_Atrous", radianceRT.width, radianceRT.height, RenderTextureFormat.ARGBFloat);

            int kInit = cs.FindKernel("WALR_Init");

            cmd.SetComputeTextureParam(cs, kInit, "_RadianceRT", radianceRT);
            cmd.SetComputeTextureParam(cs, kInit, "_NormalRT", prd.normalRT);
            cmd.SetComputeTextureParam(cs, kInit, "_TargetRT", tempRT);
            cmd.SetComputeTextureParam(cs, kInit, "_DepthRT", prd.depthRT);
            cmd.SetComputeVectorParam(filterCompute, "_MotionToRTScale", new Vector2(Mathf.CeilToInt((float)prd.motionVectorRT.width / (float)screenPixel.x), Mathf.CeilToInt((float)prd.motionVectorRT.height / (float)screenPixel.y)));
            cmd.SetComputeFloatParam(cs, "_PhiNormal", P.phiNormal);
            cmd.SetComputeFloatParam(cs, "_PhiColor", P.phiColor);
            cmd.SetComputeFloatParam(cs, "_PhiDepthStart", P.phiDepthStart);
            cmd.SetComputeFloatParam(cs, "_PhiDepthScale", P.phiDepthScale);
            PingPongWALR(cmd, cs, kInit, ref pingPong, XX1S, XY1S, XX2S, XY2S, XX1L, XY1L, XX2L, XY2L);

            cmd.SetComputeIntParams(cs, "_ScreenWidth", screen.x);
            cmd.SetComputeIntParams(cs, "_ScreenHeight", screen.y);
            cmd.DispatchCompute(cs, kInit, tgX, tgY, 1);

            int kAtrous = cs.FindKernel("WALR_Atrous");
            cmd.SetComputeTextureParam(cs, kAtrous, "_RadianceRT", radianceRT);
            cmd.SetComputeTextureParam(cs, kAtrous, "_DepthRT", prd.depthRT);
            cmd.SetComputeTextureParam(cs, kAtrous, "_NormalRT", prd.normalRT);


            for (int i = 0; i < P.atrousIterations; ++i)
            {
                int step = 1 << i;
                cmd.SetComputeIntParam(cs, "_StepSize", step);

                cmd.SetComputeIntParams(cs, "_ScreenSize", new[] { screen.x, screen.y });
                PingPongWALR(cmd, cs, kAtrous, ref pingPong, XX1S, XY1S, XX2S, XY2S, XX1L, XY1L, XX2L, XY2L);
                cmd.DispatchCompute(cs, kAtrous, tgX, tgY, 1);

            }

            int kSolve = cs.FindKernel("WALR_Solve");
            cmd.SetComputeTextureParam(cs, kSolve, "_RadianceRT", radianceRT);
            cmd.SetComputeTextureParam(cs, kSolve, "_TargetRT", tempRT);
            cmd.SetComputeTextureParam(cs, kSolve, "_NormalRT", prd.normalRT);
            cmd.SetComputeTextureParam(cs, kSolve, "_DepthRT", prd.depthRT);
            PingPongWALR(cmd, cs, kSolve, ref pingPong, XX1S, XY1S, XX2S, XY2S, XX1L, XY1L, XX2L, XY2L);
            cmd.SetComputeIntParams(cs, "_ScreenSize", new[] { screen.x, screen.y });
            cmd.DispatchCompute(cs, kSolve, tgX, tgY, 1);
            cmd.Blit(tempRT, radianceRT);
            cmd.EndSample("WALR");
        }

        public void ApplySVGF(
            PhotonRenderingData prd,
            RenderTexture radianceRT,
            RenderTexture aoRT,
            string key,
            params FilterOption[] opts)
        {
            BuildLocalSettings(out var P, opts);
            RenderTexture depthRT = prd.depthRT;
            RenderTexture normalRT = prd.normalRT;
            CommandBuffer cmd = prd.cmd;
            Camera cam = prd.camera;
            cmd.BeginSample("SVGF");
            int2 screen = new int2(radianceRT.width, radianceRT.height);

            RenderTexture historyColorRT = RTManager.Instance.GetAdjustableRT(
                cam.GetInstanceID() + "_HistoryColorRT" + key,
                screen.x, screen.y, RenderTextureFormat.ARGBFloat);
            RenderTexture historyND_RT = RTManager.Instance.GetAdjustableRT(
                cam.GetInstanceID() + "_HistoryNormalDepthRT" + key,
                screen.x, screen.y, RenderTextureFormat.ARGBFloat);
            RenderTexture varianceRT = RTManager.Instance.GetAdjustableRT(
                cam.GetInstanceID() + "_VarianceRT" + key,
                screen.x, screen.y, RenderTextureFormat.RFloat);

            RenderTexture tempRT = RTManager.Instance.GetRT(
                "_TempRTARGBFloat", screen.x, screen.y, RenderTextureFormat.ARGBFloat);

            int kTemporal = filterCompute.FindKernel("TemporalFilter");
            cmd.SetComputeIntParams(filterCompute, "_ScreenWidth", screen.x);
            cmd.SetComputeIntParams(filterCompute, "_ScreenHeight", screen.y);
            cmd.SetComputeTextureParam(filterCompute, kTemporal, "_HistoryNormalDepthRT", historyND_RT);
            cmd.SetComputeTextureParam(filterCompute, kTemporal, "_HistoryColorRT", historyColorRT);
            cmd.SetComputeTextureParam(filterCompute, kTemporal, "_VarianceRT", varianceRT);
            cmd.SetComputeTextureParam(filterCompute, kTemporal, "_RadianceRT", radianceRT);
            cmd.SetComputeTextureParam(filterCompute, kTemporal, "_MotionVectorRT", prd.motionVectorRT);
            cmd.SetComputeTextureParam(filterCompute, kTemporal, "_TargetRT", tempRT);
            cmd.SetComputeTextureParam(filterCompute, kTemporal, "_DepthRT", depthRT);
            cmd.SetComputeTextureParam(filterCompute, kTemporal, "_NormalRT", normalRT);

            cmd.SetComputeFloatParam(filterCompute, "_ClampSigma", P.clampSigma);
            cmd.SetComputeFloatParam(filterCompute, "_AlphaColor", P.alphaColor);
            cmd.SetComputeFloatParam(filterCompute, "_AlphaVar", P.alphaVar);
            cmd.SetComputeIntParam(filterCompute, "_StepSize", P.stepSize);
            cmd.SetComputeIntParam(filterCompute, "_ScreenWidth", screen.x);
            cmd.SetComputeIntParam(filterCompute, "_ScreenHeight", screen.y);
            cmd.SetComputeFloatParam(filterCompute, "_PhiNormal", P.phiNormal);
            cmd.SetComputeFloatParam(filterCompute, "_PhiColor", P.phiColor);
            cmd.SetComputeMatrixParam(filterCompute, "_ProjectionMatrixInverse", prd.camera.projectionMatrix.inverse);
            cmd.SetComputeMatrixParam(filterCompute, "_ViewMatrixInverse", prd.camera.worldToCameraMatrix.inverse);
            cmd.SetComputeVectorParam(filterCompute, "_MotionToRTScale", new Vector2(Mathf.CeilToInt((float)prd.motionVectorRT.width / (float)screen.x), Mathf.CeilToInt((float)prd.motionVectorRT.height / (float)screen.y)));
            CommandBufferHelper.DispatchCompute_RT(cmd, filterCompute, radianceRT, kTemporal, 8);
            cmd.Blit(tempRT, historyColorRT);

            // ---------- Atrous Pass ----------
            RenderTexture pingRT = tempRT;
            RenderTexture pongRT = RTManager.Instance.GetRT("_TempRT_Atrous",
                screen.x, screen.y, RenderTextureFormat.ARGBFloat);

            int kAtrous = filterCompute.FindKernel("AtrousFilter");
            Vector4 zBuf = new(-1 + cam.farClipPlane / cam.nearClipPlane, 1,
                               (-1 + cam.farClipPlane / cam.nearClipPlane) / cam.farClipPlane,
                               1 / cam.farClipPlane);

            cmd.SetComputeVectorParam(filterCompute, "_ZBufferParams", zBuf);
            cmd.SetComputeTextureParam(filterCompute, kAtrous, "_DepthRT", depthRT);
            cmd.SetComputeTextureParam(filterCompute, kAtrous, "_NormalRT", normalRT);
            cmd.SetComputeTextureParam(filterCompute, kAtrous, "_VarianceRT", varianceRT);

            if (aoRT != null)
            {
                cmd.SetComputeTextureParam(filterCompute, kAtrous, "_AORT", aoRT);
                cmd.SetComputeIntParams(filterCompute, "_AORTSize", new int[] { aoRT.width, aoRT.height });
                cmd.SetComputeIntParam(filterCompute, "_EnableAO", 1);
            }
            else
            {
                cmd.SetComputeTextureParam(filterCompute, kAtrous, "_AORT", radianceRT);
                cmd.SetComputeIntParam(filterCompute, "_EnableAO", 0);
            }

            cmd.SetComputeFloatParam(filterCompute, "_PhiNormal", P.phiNormal);
            cmd.SetComputeFloatParam(filterCompute, "_PhiColor", P.phiColor);
            cmd.SetComputeFloatParam(filterCompute, "_PhiDepthStart", P.phiDepthStart);
            cmd.SetComputeFloatParam(filterCompute, "_PhiDepthScale", P.phiDepthScale);

            int tgX = Mathf.CeilToInt(screen.x / 8.0f);
            int tgY = Mathf.CeilToInt(screen.y / 8.0f);

            for (int i = 0; i < P.atrousIterations; i++)
            {
                int curStep = 1 << i;
                cmd.SetComputeIntParam(filterCompute, "_StepSize", curStep);
                cmd.SetComputeFloatParam(filterCompute, "_StepSizeWeight", 1f);

                cmd.SetComputeTextureParam(filterCompute, kAtrous, "_RadianceRT", pingRT);
                cmd.SetComputeTextureParam(filterCompute, kAtrous, "_TargetRT", pongRT);

                cmd.DispatchCompute(filterCompute, kAtrous, tgX, tgY, 1);
                (pingRT, pongRT) = (pongRT, pingRT);
            }

            cmd.Blit(pingRT, radianceRT);
            cmd.EndSample("SVGF");
        }
        public GraphicsBuffer GetGBFromRT2_2(CommandBuffer cmd, RenderTexture source, string key, RenderTexture targetSizeRT)
        {
            int width = targetSizeRT.width;
            int height = targetSizeRT.height;
            int count = width * height;
            int stride = sizeof(float) * 3;

            float scale = targetSizeRT.width / source.width;
            GraphicsBuffer gb = RTManager.Instance.GetAdjustableGB(
                                    key, count, stride,
                                    GraphicsBuffer.Target.Structured |
                                    GraphicsBuffer.Target.Raw);

            int kernelCopyRT = filterCompute.FindKernel("CopyRT_Float2ToFloat2");

            cmd.SetComputeTextureParam(filterCompute, kernelCopyRT, "_SrcRT2", source);
            cmd.SetComputeBufferParam(filterCompute, kernelCopyRT, "_DstBuf2", gb);
            cmd.SetComputeFloatParam(filterCompute, "_CopyScale", scale);
            cmd.SetComputeIntParam(filterCompute, "_ScreenWidth", width);


            int gx = Mathf.CeilToInt(width / 8f);
            int gy = Mathf.CeilToInt(height / 8f);
            cmd.DispatchCompute(filterCompute, kernelCopyRT, gx, gy, 1);

            return gb;
        }
        public GraphicsBuffer GetGBFromRT4_3(CommandBuffer cmd, RenderTexture source, string key, RenderTexture targetSizeRT)
        {
            int width = targetSizeRT.width;
            int height = targetSizeRT.height;
            int count = width * height;   
            int stride = sizeof(float) * 3;   

            float scale = targetSizeRT.width / source.width;
            GraphicsBuffer gb = RTManager.Instance.GetAdjustableGB(
                                    key, count, stride,
                                    GraphicsBuffer.Target.Structured |
                                    GraphicsBuffer.Target.Raw);  

            int kernelCopyRT = filterCompute.FindKernel("CopyRT_Float4ToFloat3");

            cmd.SetComputeTextureParam(filterCompute, kernelCopyRT, "_SrcRT", source);
            cmd.SetComputeBufferParam(filterCompute, kernelCopyRT, "_DstBuf", gb);
            cmd.SetComputeFloatParam(filterCompute, "_CopyScale", scale);
            cmd.SetComputeIntParam(filterCompute, "_ScreenWidth", width);  


            int gx = Mathf.CeilToInt(width / 8f);
            int gy = Mathf.CeilToInt(height / 8f);
            cmd.DispatchCompute(filterCompute, kernelCopyRT, gx, gy, 1);

            return gb;
        }
        public void GetRTFromGB3_4(
            CommandBuffer cmd,
            GraphicsBuffer srcBuffer,
            RenderTexture targetRT)
        {


            int kernelCopyBuff = filterCompute.FindKernel("CopyBuff_Float3ToFloat4");

            cmd.SetComputeIntParam(filterCompute, "_ScreenHeight", targetRT.height);
            cmd.SetComputeIntParam(filterCompute, "_ScreenWidth", targetRT.width);
            cmd.SetComputeBufferParam(filterCompute, kernelCopyBuff, "_DstBuf", srcBuffer); 
            cmd.SetComputeTextureParam(filterCompute, kernelCopyBuff, "_SrcRTRW", targetRT); 

            int groups = Mathf.CeilToInt(srcBuffer.count / 64f); 
            cmd.DispatchCompute(filterCompute, kernelCopyBuff, groups, 1, 1);

        }
        public DenoiserPluginWrapper GetDenoiser(string key, DenoiserType type, DenoiserConfig cfg)
        {
            if (denoiserPluginWrappers.TryGetValue(key, out var wrapper))
            {
                if (wrapper.Config.Equals(cfg) && wrapper.Type == type)
                    return wrapper;
                wrapper.Dispose();
                denoiserPluginWrappers.Remove(key);
            }

            var newWrapper = new DenoiserPluginWrapper(type, cfg);
            denoiserPluginWrappers[key] = newWrapper;
            return newWrapper;
        }

        public void ApplyDenoiser(PhotonRenderingData prd, RenderTexture radianceRT, string key)
        {
            CommandBuffer cmd = prd.cmd;

            RenderTexture normalRT = prd.normalRT;
            Camera cam = prd.camera;
            int w = radianceRT.width;
            int h = radianceRT.height;
            int pixelCount = w * h;

            GraphicsBuffer flowBuf = GetGBFromRT2_2(cmd, prd.motionVectorRT, $"{cam.GetInstanceID()}_{key}_Flow", radianceRT);
            GraphicsBuffer radBuf = GetGBFromRT4_3(cmd, radianceRT, $"{cam.GetInstanceID()}_{key}_Rad", radianceRT);
            GraphicsBuffer nrmBuf = GetGBFromRT4_3(cmd, normalRT, $"{cam.GetInstanceID()}_{key}_Normal", radianceRT);
            GraphicsBuffer albedoBuf = GetGBFromRT4_3(cmd, prd.albedoRT, $"{cam.GetInstanceID()}_{key}_Albedo", radianceRT);
            GraphicsBuffer outBuf = RTManager.Instance.GetAdjustableGB($"{cam.GetInstanceID()}_{key}_Out", pixelCount, sizeof(float) * 3, GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Raw);
            var cfg = new DenoiserConfig
            {
                imageWidth = w,
                imageHeight = h,
                guideAlbedo = 1,
                guideNormal = 1,
                temporalMode = 0,
                cleanAux = 1,
                prefilterAux = 0
            };
            var fence = cmd.CreateAsyncGraphicsFence(SynchronisationStage.PixelProcessing);
            DenoiserPluginWrapper denoiserPluginWrapper = GetDenoiser($"{cam.GetInstanceID()}_{key}_F", advanceDenoiserType, cfg);
            denoiserPluginWrapper.Render(cmd, radBuf, outBuf, albedoBuf, nrmBuf, null);

            cmd.WaitOnAsyncGraphicsFence(fence);


            GetRTFromGB3_4(cmd, outBuf, radianceRT);

        }
    }
}