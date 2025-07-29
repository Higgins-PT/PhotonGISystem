using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotonSystem
{
    public class RTHelper : PhotonSingleton<RTHelper>
    {
        public ComputeShader copyShader;  

        private static readonly RenderTextureFormat[] SupportedFormats =
        {
            RenderTextureFormat.ARGB32,
            RenderTextureFormat.ARGBHalf,
            RenderTextureFormat.RHalf,
            RenderTextureFormat.R16
        };
        /// <summary>
        /// 将目标 RenderTexture 填充为同一种颜色。
        /// 支持 ARGB32 / ARGBHalf / RHalf / R16。
        /// 通过 CommandBuffer 调用 ComputeShader。
        /// </summary>
        public void FillWithColor(
            CommandBuffer cmd,
            RenderTexture targetRT,
            Color color
        )
        {
            if (copyShader == null)
            {
                Debug.LogError("[RTHelper] copyShader is null! Please assign a valid ComputeShader.");
                return;
            }

            if (!SupportedFormats.Contains(targetRT.format))
            {
                Debug.LogError($"[RTHelper] Target format '{targetRT.format}' is not supported!");
                return;
            }

            // 根据具体 format 选择对应的 kernel
            int kernel = -1;
            string kernelName = "";
            switch (targetRT.format)
            {
                case RenderTextureFormat.ARGB32:
                    kernelName = "KFill_ARGB32";
                    break;
                case RenderTextureFormat.ARGBHalf:
                    kernelName = "KFill_ARGBHalf";
                    break;
                case RenderTextureFormat.RHalf:
                    kernelName = "KFill_RHalf";
                    break;
                case RenderTextureFormat.R16:
                    kernelName = "KFill_R16";
                    break;
            }

            kernel = copyShader.FindKernel(kernelName);
            if (kernel < 0)
            {
                Debug.LogError($"[RTHelper] Could not find kernel '{kernelName}' in computeShader.");
                return;
            }

            // 设置纹理参数
            switch (kernelName)
            {
                case "KFill_ARGB32":
                    cmd.SetComputeTextureParam(copyShader, kernel, "_TargetARGB32", targetRT);
                    // 这里我们用 float4 传递颜色
                    Vector4 c4_32 = new Vector4(color.r, color.g, color.b, color.a);
                    cmd.SetComputeVectorParam(copyShader, "_FillColorARGB32", c4_32);
                    break;

                case "KFill_ARGBHalf":
                    cmd.SetComputeTextureParam(copyShader, kernel, "_TargetARGBHalf", targetRT);
                    // 同样用 float4，computeShader 端以 half4 接收
                    Vector4 c4_half = new Vector4(color.r, color.g, color.b, color.a);
                    cmd.SetComputeVectorParam(copyShader, "_FillColorARGBHalf", c4_half);
                    break;

                case "KFill_RHalf":
                    cmd.SetComputeTextureParam(copyShader, kernel, "_TargetRHalf", targetRT);
                    // 仅用到 color.r，余量丢弃
                    cmd.SetComputeFloatParam(copyShader, "_FillColorRHalf", color.r);
                    break;

                case "KFill_R16":
                    cmd.SetComputeTextureParam(copyShader, kernel, "_TargetR16", targetRT);
                    // 仅用到 color.r
                    cmd.SetComputeFloatParam(copyShader, "_FillColorR16", color.r);
                    break;
            }

            // 计算 Dispatch 大小（假设 #numthreads(8,8,1)）
            int threadGroupX = Mathf.CeilToInt(targetRT.width / 8f);
            int threadGroupY = Mathf.CeilToInt(targetRT.height / 8f);

            cmd.DispatchCompute(copyShader, kernel, threadGroupX, threadGroupY, 1);
        }
        public void CopyWithOffset(
            CommandBuffer cmd,
            RenderTexture sourceRT,
            RenderTexture destRT,
            Vector2Int offset
        )
        {
            if (copyShader == null)
            {
                Debug.LogError("[RTHelper] copyShader is null! Please assign a valid ComputeShader.");
                return;
            }
            if (!SupportedFormats.Contains(sourceRT.format))
            {
                Debug.LogError($"[RTHelper] Source format '{sourceRT.format}' is not supported!");
                return;
            }
            if (!SupportedFormats.Contains(destRT.format))
            {
                Debug.LogError($"[RTHelper] Dest format '{destRT.format}' is not supported!");
                return;
            }
            if (sourceRT.format != destRT.format)
            {
                Debug.LogError($"[RTHelper] Source format '{sourceRT.format}' != Dest format '{destRT.format}'. Must match!");
                return;
            }

            if (offset.x < 0 || offset.y < 0)
            {
                Debug.LogError($"[RTHelper] Offset must be non-negative, but got {offset}.");
                return;
            }
            if (offset.x + sourceRT.width > destRT.width ||
                offset.y + sourceRT.height > destRT.height)
            {
                Debug.LogError(
                    $"[RTHelper] Source + offset goes out of Dest bounds! " +
                    $"(srcSize=({sourceRT.width},{sourceRT.height}), offset=({offset.x},{offset.y}), " +
                    $"destSize=({destRT.width},{destRT.height}))");
                return;
            }

            int kernel = -1;
            string kernelName = "";
            switch (sourceRT.format)
            {
                case RenderTextureFormat.ARGB32:
                    kernelName = "KCopy_ARGB32";
                    break;
                case RenderTextureFormat.ARGBHalf:
                    kernelName = "KCopy_ARGBHalf";
                    break;
                case RenderTextureFormat.RHalf:
                    kernelName = "KCopy_RHalf";
                    break;
                case RenderTextureFormat.R16:
                    kernelName = "KCopy_R16";
                    break;
            }

            kernel = copyShader.FindKernel(kernelName);
            if (kernel < 0)
            {
                Debug.LogError($"[RTHelper] Could not find kernel '{kernelName}' in computeShader.");
                return;
            }

            switch (kernelName)
            {
                case "KCopy_ARGB32":
                    cmd.SetComputeTextureParam(copyShader, kernel, "_SourceARGB32", sourceRT);
                    cmd.SetComputeTextureParam(copyShader, kernel, "_DestinationARGB32", destRT);
                    break;
                case "KCopy_ARGBHalf":
                    cmd.SetComputeTextureParam(copyShader, kernel, "_SourceARGBHalf", sourceRT);
                    cmd.SetComputeTextureParam(copyShader, kernel, "_DestinationARGBHalf", destRT);
                    break;
                case "KCopy_RHalf":
                    cmd.SetComputeTextureParam(copyShader, kernel, "_SourceRHalf", sourceRT);
                    cmd.SetComputeTextureParam(copyShader, kernel, "_DestinationRHalf", destRT);
                    break;
                case "KCopy_R16":
                    cmd.SetComputeTextureParam(copyShader, kernel, "_SourceR16", sourceRT);
                    cmd.SetComputeTextureParam(copyShader, kernel, "_DestinationR16", destRT);
                    break;
            }

            cmd.SetComputeIntParam(copyShader, "_OffsetX", offset.x);
            cmd.SetComputeIntParam(copyShader, "_OffsetY", offset.y);

            int threadGroupX = Mathf.CeilToInt(sourceRT.width / 8f);
            int threadGroupY = Mathf.CeilToInt(sourceRT.height / 8f);

            cmd.DispatchCompute(copyShader, kernel, threadGroupX, threadGroupY, 1);
        }
        public void UpdateRadiosityAtlas(CommandBuffer cmd, ComputeBuffer surfaceCache, int totalCount, int offest)
        {
            int kernelCopyBufferRadiosityAtlas = copyShader.FindKernel("KCopyBufferRadiosityAtlas");
            cmd.SetComputeBufferParam(copyShader, kernelCopyBufferRadiosityAtlas, "_SurfaceCache", surfaceCache);
            int cacheCount = surfaceCache.count / totalCount;
            int cacheIndexOffest = cacheCount * offest;
            cmd.SetComputeIntParam(copyShader, "_SurfaceCacheCount", cacheCount);
            cmd.SetComputeIntParam(copyShader, "_Offest", cacheIndexOffest);
            cmd.SetComputeFloatParam(copyShader, "_SurfaceCacheDecay", SurfaceCacheManager.Instance.surfaceCacheDecay);
            
            int threadGroupX = Mathf.CeilToInt(cacheCount / 512f);

            cmd.DispatchCompute(copyShader, kernelCopyBufferRadiosityAtlas, threadGroupX, 1, 1);
        }
        public void FillSurfaceCache(
    CommandBuffer cmd,
    ComputeBuffer surfaceCache,
    float4 fillAlbedo,
    float3 fillNormal,
    float4 fillEmissive,
    float fillDepth
)
        {
            int kernelFillBuffer = copyShader.FindKernel("KFillBuffer");
            cmd.SetComputeBufferParam(copyShader, kernelFillBuffer, "_SurfaceCache", surfaceCache);
            int cacheCount = surfaceCache.count;
            cmd.SetComputeIntParam(copyShader, "_SurfaceCacheCount", cacheCount);

            cmd.SetComputeVectorParam(copyShader, "_FillColorAlbedo", fillAlbedo);
            cmd.SetComputeVectorParam(copyShader, "_FillColorEmissive", fillEmissive);
            cmd.SetComputeVectorParam(copyShader, "_FillColorNormal", new Vector4(fillNormal.x, fillNormal.y, fillNormal.z, 0));
            cmd.SetComputeFloatParam(copyShader, "_FillColorDepth", fillDepth);

            int threadGroupX = Mathf.CeilToInt(cacheCount / 512f);

            cmd.DispatchCompute(copyShader, kernelFillBuffer, threadGroupX, 1, 1);
        }

        public void CopyRTToSurfaceCacheWithOffset(
      CommandBuffer cmd,
      RenderTexture sourceRT,
      ComputeBuffer surfaceCacheBuffer,
      Vector2Int offset,
      int surfaceCacheWidth,
      SurfaceCacheWriteType writeType
  )
        {
            // 1) 基础校验
            if (copyShader == null)
            {
                Debug.LogError("[RTHelper] copyShader is null! Please assign a valid ComputeShader.");
                return;
            }
            if (sourceRT == null)
            {
                Debug.LogError("[RTHelper] sourceRT is null!");
                return;
            }
            if (!SupportedFormats.Contains(sourceRT.format))
            {
                Debug.LogError($"[RTHelper] Source format '{sourceRT.format}' is not supported!");
                return;
            }
            // 偏移是否合法
            if (offset.x < 0 || offset.y < 0)
            {
                Debug.LogError($"[RTHelper] Offset must be non-negative, but got {offset}.");
                return;
            }
            if (offset.x + sourceRT.width > surfaceCacheWidth)
            {
                Debug.LogError(
                    $"[RTHelper] Source + offset goes out of surfaceCacheWidth bounds! " +
                    $"(srcWidth={sourceRT.width}, offsetX={offset.x}, total={offset.x + sourceRT.width}, " +
                    $"cacheWidth={surfaceCacheWidth})");
                return;
            }

            string kernelName = GetWriteSurfaceCacheKernelName(writeType, sourceRT.format);
            if (string.IsNullOrEmpty(kernelName))
            {
                Debug.LogError($"[RTHelper] Write type '{writeType}' + format '{sourceRT.format}' not supported or mismatch!");
                return;
            }

            int kernel = copyShader.FindKernel(kernelName);
            if (kernel < 0)
            {
                Debug.LogError($"[RTHelper] Could not find kernel '{kernelName}' in computeShader.");
                return;
            }

            switch (sourceRT.format)
            {
                case RenderTextureFormat.ARGB32:
                    cmd.SetComputeTextureParam(copyShader, kernel, "_SourceARGB32", sourceRT);
                    break;
                case RenderTextureFormat.ARGBHalf:
                    cmd.SetComputeTextureParam(copyShader, kernel, "_SourceARGBHalf", sourceRT);
                    break;
                case RenderTextureFormat.RHalf:
                    cmd.SetComputeTextureParam(copyShader, kernel, "_SourceRHalf", sourceRT);
                    break;
                case RenderTextureFormat.R16:
                    cmd.SetComputeTextureParam(copyShader, kernel, "_SourceR16", sourceRT);
                    break;
            }
            cmd.SetComputeBufferParam(copyShader, kernel, "_SurfaceCache", surfaceCacheBuffer);

            cmd.SetComputeIntParam(copyShader, "_OffsetX", offset.x);
            cmd.SetComputeIntParam(copyShader, "_OffsetY", offset.y);
            cmd.SetComputeIntParam(copyShader, "_SurfaceCacheWidth", surfaceCacheWidth);

            int threadGroupX = Mathf.CeilToInt(sourceRT.width / 8f);
            int threadGroupY = Mathf.CeilToInt(sourceRT.height / 8f);

            cmd.DispatchCompute(copyShader, kernel, threadGroupX, threadGroupY, 1);
        }
        private string GetWriteSurfaceCacheKernelName(SurfaceCacheWriteType type, RenderTextureFormat format)
        {
            switch (type)
            {
                case SurfaceCacheWriteType.Albedo:
                    {
                        if (format == RenderTextureFormat.ARGB32) return "KWriteBuffer_ARGB32_Albedo";
                        if (format == RenderTextureFormat.ARGBHalf) return "KWriteBuffer_ARGBHalf_Albedo";
                        return "";
                    }
                case SurfaceCacheWriteType.Normal:
                    {
                        if (format == RenderTextureFormat.ARGB32) return "KWriteBuffer_ARGB32_Normal";
                        if (format == RenderTextureFormat.ARGBHalf) return "KWriteBuffer_ARGBHalf_Normal";
                        return "";
                    }
                case SurfaceCacheWriteType.Emissive:
                    {
                        if (format == RenderTextureFormat.ARGB32) return "KWriteBuffer_ARGB32_Emissive";
                        if (format == RenderTextureFormat.ARGBHalf) return "KWriteBuffer_ARGBHalf_Emissive";
                        return "";
                    }

                case SurfaceCacheWriteType.Depth:
                    {
                        if (format == RenderTextureFormat.RHalf) return "KWriteBuffer_RHalf_Depth";
                        if (format == RenderTextureFormat.R16) return "KWriteBuffer_R16_Depth";
                        return "";
                    }
                case SurfaceCacheWriteType.Metallic:
                    {
                        if (format == RenderTextureFormat.ARGB32) return "KWriteBuffer_ARGB32_Metallic";
                        if (format == RenderTextureFormat.ARGBHalf) return "KWriteBuffer_ARGBHalf_Metallic";
                        if (format == RenderTextureFormat.RHalf) return "KWriteBuffer_RHalf_Metallic";
                        if (format == RenderTextureFormat.R16) return "KWriteBuffer_R16_Metallic";
                        return "";
                    }
                case SurfaceCacheWriteType.Smoothness:
                    {
                        if (format == RenderTextureFormat.ARGB32) return "KWriteBuffer_ARGB32_Smoothness";
                        if (format == RenderTextureFormat.ARGBHalf) return "KWriteBuffer_ARGBHalf_Smoothness";
                        if (format == RenderTextureFormat.RHalf) return "KWriteBuffer_RHalf_Smoothness";
                        if (format == RenderTextureFormat.R16) return "KWriteBuffer_R16_Smoothness";
                        return "";
                    }
                case SurfaceCacheWriteType.RadiosityAtlas:
                    {
                        return "KWriteBuffer_Clean_RadiosityAtlas";
                    }
            }
            return "";
        }
    }
    public enum SurfaceCacheWriteType
    {
        Albedo,
        Normal,
        Emissive,
        Depth,
        Metallic,
        Smoothness,
        RadiosityAtlas
    }


}
