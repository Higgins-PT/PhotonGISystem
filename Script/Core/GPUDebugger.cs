using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotonSystem
{
    public static class GPUDebugger
    {
        public static int GetFieldOffset<T>(string fieldName) where T : struct
            => (int)Marshal.OffsetOf(typeof(T), fieldName);

        public static int GetFieldCount<T>(string fieldName) where T : struct
        {
            FieldInfo fi = typeof(T).GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi == null)
                throw new ArgumentException($"Type {typeof(T)} Ã»ÓÐ×Ö¶Î {fieldName}");
            int byteSize = Marshal.SizeOf(fi.FieldType);
            return byteSize / 4;
        }
        public static RenderTexture DebugField<T>(
            CommandBuffer cmd,
            ComputeBuffer buffer,
            string fieldName,
            int width
        ) where T : struct
        {
            int stride = Marshal.SizeOf<T>();
            int offset = GetFieldOffset<T>(fieldName);
            int count = buffer.count;
            int fieldCount = GetFieldCount<T>(fieldName);
            int height = Mathf.CeilToInt(count / (float)width);

  
            var rt = RTManager.Instance.GetRT(
                buffer.GetHashCode() + "_ComputeBufferDebug",
                width, height,
                RenderTextureFormat.ARGBFloat
            );
            rt.enableRandomWrite = true;
            rt.Create();

            var debugCS = DebugManager.Instance.debugCS;
            int kernel = debugCS.FindKernel("CopyBuffer");
            cmd.SetComputeBufferParam(debugCS, kernel, "InputData", buffer);
            cmd.SetComputeTextureParam(debugCS, kernel, "Result", rt);

            cmd.SetComputeIntParam(debugCS, "Stride", stride);
            cmd.SetComputeIntParam(debugCS, "FieldOffset", offset);
            cmd.SetComputeIntParam(debugCS, "FieldCount", fieldCount);
            cmd.SetComputeIntParam(debugCS, "Count", count);
            cmd.SetComputeIntParam(debugCS, "Width", width);
            int gx = Mathf.CeilToInt(width / 8f);
            int gy = Mathf.CeilToInt(height / 8f);
            cmd.DispatchCompute(debugCS, kernel, gx, gy, 1);

            return rt;
        }
    }

}