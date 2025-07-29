using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotonSystem
{
    public class HZBManager : PhotonSingleton<HZBManager>, IRadianceFeature
    {
        
        public int downSample = 0;
        public Dictionary<string, SSGIData> ssgiDatas = new Dictionary<string, SSGIData>();
        [Range(0, 10000)]
        public float maxDistance = 1000;
        public float stride = 32f;
        [Range(0, 1)]
        public float jitterStrength = 0.5f;
        public float thickness = 1f;
        public int maxStep = 50;
        [Range(1, 10)]
        public int maxMipLevel = 4;
        public ComputeShader hzbCS;
        public Texture2D noise;
        public class SSGIData
        {
            public Camera cam;
            public RenderTexture hzb;
            public RenderTexture mipColor;
            public int2 hzbSize;
            public int2 hzbSizeOrigin;
            public bool onlyDepth;
            public int mipCount;
            public void Release()
            {
                hzb?.Release();
                mipColor?.Release();
            }
        }
        public static void GetLightTexelAndMip(
            Vector3 lightPosWS,
            float range,
            Camera cam,
            int2 hzbSize,
            int mipCount,
            out int2 texel,
            out int mipLevel)
        {
            Matrix4x4 view = cam.worldToCameraMatrix;
            Matrix4x4 proj = cam.projectionMatrix;

            Transform tf = cam.transform;
            Vector3[] dirs =
            {
                tf.right,
                tf.up,    
                tf.forward 
            };

            Vector3 posVS = view.MultiplyPoint(lightPosWS);
            Vector4 clipCenter = proj * new Vector4(posVS.x, posVS.y, posVS.z, 1f);

            Vector2 ndcCenter = new Vector2(clipCenter.x, clipCenter.y) / clipCenter.w;
            Vector2 uvCenter = ndcCenter * 0.5f + Vector2.one * 0.5f; 

            float maxRadiusUV = 0f;

            foreach (Vector3 d in dirs)
            {
                Vector3 edgePosWS = lightPosWS + d * range;
                Vector3 edgePosVS = view.MultiplyPoint(edgePosWS);
                Vector4 clipEdge = proj * new Vector4(edgePosVS.x, edgePosVS.y, edgePosVS.z, 1f);

                Vector2 ndcEdge = new Vector2(clipEdge.x, clipEdge.y) / clipEdge.w;
                Vector2 uvEdge = ndcEdge * 0.5f + Vector2.one * 0.5f;

                float r = (uvEdge - uvCenter).magnitude;
                maxRadiusUV = Mathf.Max(maxRadiusUV, r);
            }

            float rPix0 = maxRadiusUV * Mathf.Max(hzbSize.x, hzbSize.y); 
            int mipSel = Mathf.Clamp(
                               Mathf.CeilToInt(Mathf.Log(Mathf.Max(rPix0, 1f), 2f)),
                               0, mipCount - 1);

            int scale = 1 << mipSel;        
            Vector2 uvClamped = new Vector2(
                Mathf.Clamp01(uvCenter.x),
                Mathf.Clamp01(uvCenter.y));

            Vector2 pixelF = new Vector2(
                                uvClamped.x * hzbSize.x / scale,
                                uvClamped.y * hzbSize.y / scale);

            texel = new int2(
                        Mathf.Clamp((int)pixelF.x, 0, (hzbSize.x >> mipSel) - 1),
                        Mathf.Clamp((int)pixelF.y, 0, (hzbSize.y >> mipSel) - 1));

            mipLevel = mipSel;
        }
        public static void DrawHZBPixelRect(int2 index, int mipLevel, int2 hzbBaseSize,
                                        Camera cam, Color color)
        {
            if (cam == null) return;

            int2 mipRes = new int2(
                Mathf.Max(1, hzbBaseSize.x >> mipLevel),
                Mathf.Max(1, hzbBaseSize.y >> mipLevel));

            // index.y = mipRes.y - 1 - index.y;
            Vector2 uvMin = new Vector2(
                (float)index.x / mipRes.x,
                (float)index.y / mipRes.y);

            Vector2 uvMax = new Vector2(
                (float)(index.x + 1) / mipRes.x,
                (float)(index.y + 1) / mipRes.y);

            float z = 0.1f;
            Vector3 p0 = cam.ViewportToWorldPoint(new Vector3(uvMin.x, uvMin.y, z)); 
            Vector3 p1 = cam.ViewportToWorldPoint(new Vector3(uvMax.x, uvMin.y, z)); 
            Vector3 p2 = cam.ViewportToWorldPoint(new Vector3(uvMax.x, uvMax.y, z)); 
            Vector3 p3 = cam.ViewportToWorldPoint(new Vector3(uvMin.x, uvMax.y, z)); 

#if UNITY_EDITOR
            Handles.color = color;
            Handles.DrawAAPolyLine(2,
                new Vector3[] { p0, p1, p2, p3, p0 });
#else
        Gizmos.color = color;
        Gizmos.DrawLine(p0, p1);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p0);
#endif
        }
        public string GetSSGIDataKey(PhotonRenderingData photonRenderingData)
        {
            return photonRenderingData.camera.GetInstanceID() + "SSGIKey";
        }
        public void GenerateColorMip(PhotonRenderingData photonRenderingData, RenderTexture rt, SSGIData ssgiData)
        {
            CommandBuffer cmd = photonRenderingData.cmd;
            RenderTexture color = rt;
            int kernelColorInput = hzbCS.FindKernel("InputBufferColorData");
            cmd.SetComputeTextureParam(hzbCS, kernelColorInput, "_InputColor", color);
            int2 hzbSize = ssgiData.hzbSize;
            int mipCount = ssgiData.mipCount;
            RenderTexture mipColor = RTManager.Instance.GetAdjustableRT("ColorHBuffer" + photonRenderingData.camera, hzbSize.x, hzbSize.y, RenderTextureFormat.ARGBFloat, useMipMap: true, mipCount: mipCount);
            cmd.SetComputeTextureParam(hzbCS, kernelColorInput, "_Color", mipColor, 0);

            int groupsX0 = Mathf.CeilToInt(hzbSize.x / 8f);
            int groupsY0 = Mathf.CeilToInt(hzbSize.y / 8f);
            cmd.DispatchCompute(hzbCS, kernelColorInput, groupsX0, groupsY0, 1);
            cmd.GenerateMips(mipColor);
        }
        public SSGIData GenerateHZB(PhotonRenderingData photonRenderingData, RenderTexture renderTexture, bool onlyDepth = false)
        {

            CommandBuffer cmd = photonRenderingData.cmd;
            RenderTexture depth = photonRenderingData.depthRT;
            RenderTexture color = renderTexture;
            int maxSize = Mathf.Max(depth.width, depth.height);
            int size = Mathf.NextPowerOfTwo(maxSize);
            int dsFactor = Mathf.Max(0, downSample + 1);
            dsFactor = Mathf.ClosestPowerOfTwo(dsFactor);
            int2 hzbSizeOrigin = new int2(Mathf.Max(1, size), Mathf.Max(1, size));
            int2 hzbSize = new int2(Mathf.Max(1, size / dsFactor), Mathf.Max(1, size / dsFactor));
            int mipCount = Mathf.FloorToInt(Mathf.Log(hzbSize.x, 2f)) + 1;

            RenderTexture hzb = RTManager.Instance.GetAdjustableRT("HizBuffer" + photonRenderingData.camera, hzbSize.x, hzbSize.y, RenderTextureFormat.RGFloat, useMipMap: true, mipCount: mipCount);
            RenderTexture mipColor;
            if (onlyDepth)
            {
                mipColor = RTManager.Instance.GetAdjustableRT("ColorHBuffer" + photonRenderingData.camera, hzbSize.x, hzbSize.y, RenderTextureFormat.ARGBFloat, useMipMap: true, mipCount: mipCount);
            }
            else
            {
                mipColor = RTManager.Instance.GetAdjustableRT("ColorHBuffer" + photonRenderingData.camera, hzbSize.x, hzbSize.y, RenderTextureFormat.ARGBFloat, useMipMap: true, mipCount: mipCount);
            }

            SSGIData ssgiData;
            if (ssgiDatas.TryGetValue(GetSSGIDataKey(photonRenderingData), out var data))
            {
                data.hzb = hzb;
                data.mipColor = mipColor;
                data.hzbSize = hzbSize;
                data.hzbSizeOrigin = hzbSizeOrigin;
                data.mipCount = mipCount;
                data.onlyDepth = onlyDepth;
                ssgiData = data;
            }
            else
            {
                ssgiData = new SSGIData { hzb = hzb, mipColor = mipColor, hzbSize = hzbSize, mipCount = mipCount, cam = photonRenderingData.camera, hzbSizeOrigin = hzbSizeOrigin, onlyDepth = onlyDepth };
                ssgiDatas.Add(GetSSGIDataKey(photonRenderingData), ssgiData);
            }
            int mipW = hzb.width, mipH = hzb.height;
            int kernelHZBG = hzbCS.FindKernel("HZBMipGenerate");
            int kernelInput = hzbCS.FindKernel("InputBufferData");
            int kernelDepthInput = hzbCS.FindKernel("InputBufferDepthData");
            cmd.SetComputeIntParam(hzbCS, "_DownLevel", downSample);
            cmd.SetComputeVectorParam(hzbCS, "_OriginSize",
                new Vector4(depth.width, depth.height, 0, 0));

            float near = photonRenderingData.camera.nearClipPlane;
            float far = photonRenderingData.camera.farClipPlane;
            Vector4 zBufferParams;
            zBufferParams = new Vector4(near, far, 1, 1);
            cmd.SetComputeVectorParam(hzbCS, "_ZBufferParamsL", zBufferParams);
            int groupsX0 = Mathf.CeilToInt(hzbSize.x / 8f);
            int groupsY0 = Mathf.CeilToInt(hzbSize.y / 8f);
            if (color != null || onlyDepth)
            {
                cmd.SetComputeTextureParam(hzbCS, kernelInput, "_Depth", depth);
                cmd.SetComputeTextureParam(hzbCS, kernelInput, "_Dst", hzb, 0);
                cmd.SetComputeTextureParam(hzbCS, kernelInput, "_InputColor", color);
                cmd.SetComputeTextureParam(hzbCS, kernelInput, "_Color", mipColor, 0);
                cmd.DispatchCompute(hzbCS, kernelInput, groupsX0, groupsY0, 1);
                cmd.GenerateMips(mipColor);
            }
            else
            {
                cmd.SetComputeTextureParam(hzbCS, kernelDepthInput, "_Depth", depth);
                cmd.SetComputeTextureParam(hzbCS, kernelDepthInput, "_Dst", hzb, 0);
                cmd.DispatchCompute(hzbCS, kernelDepthInput, groupsX0, groupsY0, 1);
            }

            for (int i = 0; i < mipCount - 1; i++)
            {
                cmd.SetComputeTextureParam(hzbCS, kernelHZBG, "_Src", hzb);
                cmd.SetComputeTextureParam(hzbCS, kernelHZBG, "_Dst", hzb, i + 1);
                cmd.SetComputeIntParam(hzbCS, "_MipLevel", i);
                int groupsX = Mathf.CeilToInt(mipW / 2f / 8);
                int groupsY = Mathf.CeilToInt(mipH / 2f / 8);
                cmd.DispatchCompute(hzbCS, kernelHZBG, groupsX, groupsY, 1);
                mipW = Mathf.Max(1, mipW >> 1);
                mipH = Mathf.Max(1, mipH >> 1);
            }
            return ssgiData;
        }
        public void SetScreenTraceData(PhotonRenderingData photonRenderingData, ComputeShader computeShader, int kernel, Camera camera)
        {
            CommandBuffer cmd = photonRenderingData.cmd;
            SSGIData ssgiData = ssgiDatas[GetSSGIDataKey(photonRenderingData)];
            cmd.SetComputeTextureParam(computeShader, kernel, "_HZBDepth_SSR", ssgiData.hzb);
            if (!ssgiData.onlyDepth)
            {
                cmd.SetComputeTextureParam(computeShader, kernel, "_HZBColor_SSR", ssgiData.mipColor);
            }

            cmd.SetComputeTextureParam(computeShader, kernel, "_ScreenNormal_SSR", photonRenderingData.normalRT);
            cmd.SetComputeTextureParam(computeShader, kernel, "_NoiseTex_SSR", noise);
            cmd.SetComputeTextureParam(computeShader, kernel, "_MetallicRT_SSR", photonRenderingData.metallicRT);
            cmd.SetComputeVectorParam(computeShader, "_HZBDepthSize_SSR", new Vector2(ssgiData.hzbSize.x, ssgiData.hzbSize.y));
            cmd.SetComputeVectorParam(computeShader, "_ScreenSize_SSR", new Vector2(photonRenderingData.activeRT.width, photonRenderingData.activeRT.height));
            cmd.SetComputeVectorParam(computeShader, "_NoiseSize_SSR", new Vector2(noise.width, noise.height));
            int2 screenSize = new int2(photonRenderingData.activeRT.width, photonRenderingData.activeRT.height);

            int2 hzbSizeOrigin = ssgiData.hzbSizeOrigin;
            Vector2 scaleOtoH = new Vector2(
                (float)screenSize.x / hzbSizeOrigin.x,
                (float)screenSize.y / hzbSizeOrigin.y);
            cmd.SetComputeVectorParam(computeShader, "_ScaleOtoH_SSR", scaleOtoH);
            Matrix4x4 proj = camera.projectionMatrix;
            Matrix4x4 view = camera.worldToCameraMatrix;
            cmd.SetComputeMatrixParam(computeShader, "_ProjectionMatrix_SSR", proj);
            cmd.SetComputeMatrixParam(computeShader, "_ViewMatrix_SSR", view);
            cmd.SetComputeMatrixParam(computeShader, "_ProjectionMatrixInverse_SSR", proj.inverse);
            cmd.SetComputeMatrixParam(computeShader, "_ViewMatrixInverse_SSR", view.inverse);
            cmd.SetComputeFloatParam(computeShader, "_MaxDistance", maxDistance);
            cmd.SetComputeFloatParam(computeShader, "_Stride", stride);
            cmd.SetComputeFloatParam(computeShader, "_JitterStrength", jitterStrength);
            cmd.SetComputeFloatParam(computeShader, "_Thickness", thickness);
            cmd.SetComputeIntParam(computeShader, "_MaxStep", maxStep);
            cmd.SetComputeIntParam(computeShader, "_HZBMipCount_SSR", ssgiData.mipCount);
            cmd.SetComputeIntParam(computeShader, "_MaxMipLevel", maxMipLevel);
            cmd.SetComputeIntParam(computeShader, "_DownSample", downSample);
            cmd.SetComputeVectorParam(computeShader, "_CameraPosition_SSR", camera.transform.position);
            cmd.SetComputeVectorParam(computeShader, "_CameraDirection_SSR", camera.transform.forward);
            


        }
        public override void ReleaseSystem()
        {
            base.ReleaseSystem();
            foreach (var data in ssgiDatas)
            {
                data.Value.Release();
            }
            ssgiDatas.Clear();
        }

        public void GetRadianceSample(RadianceControl radianceControl, PhotonRenderingData photonRenderingData)
        {
        }

        public void RadainceFeedback(RadianceControl radianceControl, PhotonRenderingData photonRenderingData)
        {
            if (ssgiDatas.TryGetValue(GetSSGIDataKey(photonRenderingData), out SSGIData data))
            {
                GenerateColorMip(photonRenderingData, photonRenderingData.targetRT, data);
            }
        }
    }

}
