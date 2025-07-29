using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotonSystem
{
    public enum FloodMode
    {
        Linear,
        Jump
    }

    public enum FloodFillQuality
    {
        Normal,
        Ultra
    }

    public enum DistanceMode
    {
        Signed,
        Unsigned
    }
    public static class MeshSDFGenerator
    {

        static class Uniforms
        {
            internal static int _SDF = Shader.PropertyToID("_SDF");
            internal static int _SDFBuffer = Shader.PropertyToID("_SDFBuffer");
            internal static int _SDFBufferRW = Shader.PropertyToID("_SDFBufferRW");
            internal static int _JumpBuffer = Shader.PropertyToID("_JumpBuffer");
            internal static int _JumpBufferRW = Shader.PropertyToID("_JumpBufferRW");
            internal static int _VoxelResolution = Shader.PropertyToID("_VoxelResolution");
            internal static int _MaxDistance = Shader.PropertyToID("_MaxDistance");
            internal static int INITIAL_DISTANCE = Shader.PropertyToID("INITIAL_DISTANCE");
            internal static int _WorldToLocal = Shader.PropertyToID("_WorldToLocal");
            internal static int _Offset = Shader.PropertyToID("_Offset");
            internal static int g_SignedDistanceField = Shader.PropertyToID("g_SignedDistanceField");
            internal static int g_NumCellsX = Shader.PropertyToID("g_NumCellsX");
            internal static int g_NumCellsY = Shader.PropertyToID("g_NumCellsY");
            internal static int g_NumCellsZ = Shader.PropertyToID("g_NumCellsZ");
            internal static int g_Origin = Shader.PropertyToID("g_Origin");
            internal static int g_CellSize = Shader.PropertyToID("g_CellSize");
            internal static int _VertexBuffer = Shader.PropertyToID("_VertexBuffer");
            internal static int _IndexBuffer = Shader.PropertyToID("_IndexBuffer");
            internal static int _IndexFormat16bit = Shader.PropertyToID("_IndexFormat16bit");
            internal static int _VertexBufferStride = Shader.PropertyToID("_VertexBufferStride");
            internal static int _VertexBufferPosAttributeOffset = Shader.PropertyToID("_VertexBufferPosAttributeOffset");
            internal static int _JumpOffset = Shader.PropertyToID("_JumpOffset");
            internal static int _JumpOffsetInterleaved = Shader.PropertyToID("_JumpOffsetInterleaved");
            internal static int _DispatchSizeX = Shader.PropertyToID("_DispatchSizeX");
        }

        static class Labels
        {
            internal static string MeshToSDF = "MeshToSDF";
            internal static string Initialize = "Initialize";
            internal static string SplatTriangleDistances = "SplatTriangleDistances";
            internal static string SplatTriangleDistancesSigned = "SplatTriangleDistancesSigned";
            internal static string SplatTriangleDistancesUnsigned = "SplatTriangleDistancesUnsigned";
            internal static string Finalize = "Finalize";
            internal static string LinearFloodStep = "LinearFloodStep";
            internal static string LinearFloodStepUltraQuality = "LinearFloodStepUltraQuality";
            internal static string JumpFloodInitialize = "JumpFloodInitialize";
            internal static string JumpFloodStep = "JumpFloodStep";
            internal static string JumpFloodStepUltraQuality = "JumpFloodStepUltraQuality";
            internal static string JumpFloodFinalize = "JumpFloodFinalize";
            internal static string BufferToTexture = "BufferToTexture";
        }
        const int kThreadCount = 64;
        const int kMaxThreadGroupCount = 65535;
        public static (Vector3Int resolution, float voxelSize) CalculateResolutionFromBounds(Bounds bounds, int maxResolution)
        {
            Vector3 size = bounds.size;
            float maxEdge = Mathf.Max(size.x, size.y, size.z);

            float scaleX = size.x / maxEdge;
            float scaleY = size.y / maxEdge;
            float scaleZ = size.z / maxEdge;
            int resX = Mathf.Max(Mathf.CeilToInt(scaleX * maxResolution), 1);
            int resY = Mathf.Max( Mathf.CeilToInt(scaleY * maxResolution),1);
            int resZ = Mathf.Max(Mathf.CeilToInt(scaleZ * maxResolution), 1);
            return (new Vector3Int(resX, resY, resZ), maxEdge / maxResolution);
        }
        public static RenderTexture GenerateSDF(
            Mesh mesh,
            int maxResolution,
            FloodMode floodMode,
            FloodFillQuality floodQuality,
            int floodIterations,
            DistanceMode distanceMode,
            float offset)
        {



            ComputeShader m_Compute = ResourceManager.SdfMeshCompute;

            ComputeBuffer m_SDFBuffer = null;
            ComputeBuffer m_SDFBufferBis = null;
            ComputeBuffer m_JumpBuffer = null;
            ComputeBuffer m_JumpBufferBis = null;

            GraphicsBuffer m_VertexBuffer = null;
            GraphicsBuffer m_IndexBuffer = null;
            int m_VertexBufferStride = 1;
            int m_VertexBufferPosAttributeOffset = 1;
            IndexFormat m_IndexFormat = IndexFormat.UInt32;

            CommandBuffer cmd = CommandBufferPool.Get(Labels.MeshToSDF);

            int m_ThreadGroupCountTriangles = 1;


            if (mesh == null)
            {

                Debug.LogError("GenerateSDF: 输入 mesh 为 null。");
                return null;
            }

            Bounds bounds = mesh.bounds;

            //bounds.size *= 1.08f;
            var result = CalculateResolutionFromBounds(bounds, maxResolution);

            float voxelSize = result.voxelSize;
            Vector3Int resolution = result.resolution;


            RenderTextureDescriptor desc = new RenderTextureDescriptor(
                resolution.x, resolution.y,
                RenderTextureFormat.RHalf,
                0)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = resolution.z,
                enableRandomWrite = true
            };
            RenderTexture sdfRT = new RenderTexture(desc);
            sdfRT.wrapMode = TextureWrapMode.Clamp;
            sdfRT.Create();


            Vector3Int voxelResolution = resolution;
            int voxelCount = voxelResolution.x * voxelResolution.y * voxelResolution.z;
            Bounds voxelBounds = bounds;
            int threadGroupCountVoxels = (int)Mathf.Ceil((float)voxelCount / (float)kThreadCount);

            int dispatchSizeX = threadGroupCountVoxels;
            int dispatchSizeY = 1;
            // Dispatch size in any dimension can't exceed kMaxThreadGroupCount, so when we're above that limit
            // start dispatching groups in two dimensions.
            if (threadGroupCountVoxels > kMaxThreadGroupCount)
            {
                // Make it roughly square-ish as a heuristic to avoid too many unused at the end
                dispatchSizeX = Mathf.CeilToInt(Mathf.Sqrt(threadGroupCountVoxels));
                dispatchSizeY = Mathf.CeilToInt((float)threadGroupCountVoxels / dispatchSizeX);
            }

            CreateComputeBuffer(ref m_SDFBuffer, voxelCount, sizeof(float));
            CreateComputeBuffer(ref m_SDFBufferBis, voxelCount, sizeof(float));
            if (floodMode == FloodMode.Jump)
            {
                CreateComputeBuffer(ref m_JumpBuffer, voxelCount, sizeof(int));
                CreateComputeBuffer(ref m_JumpBufferBis, voxelCount, sizeof(int));
            }
            else
            {
                ReleaseComputeBuffer(ref m_JumpBuffer);
                ReleaseComputeBuffer(ref m_JumpBufferBis);
            }

            //init
            int m_InitializeKernel = m_Compute.FindKernel(Labels.Initialize);
            int m_SplatTriangleDistancesSignedKernel = m_Compute.FindKernel(Labels.SplatTriangleDistancesSigned);
            int m_SplatTriangleDistancesUnsignedKernel = m_Compute.FindKernel(Labels.SplatTriangleDistancesUnsigned);
            int m_FinalizeKernel = m_Compute.FindKernel(Labels.Finalize);
            int m_LinearFloodStepKernel = m_Compute.FindKernel(Labels.LinearFloodStep);
            int m_LinearFloodStepUltraQualityKernel = m_Compute.FindKernel(Labels.LinearFloodStepUltraQuality);
            int m_JumpFloodInitialize = m_Compute.FindKernel(Labels.JumpFloodInitialize);
            int m_JumpFloodStep = m_Compute.FindKernel(Labels.JumpFloodStep);
            int m_JumpFloodStepUltraQuality = m_Compute.FindKernel(Labels.JumpFloodStepUltraQuality);
            int m_JumpFloodFinalize = m_Compute.FindKernel(Labels.JumpFloodFinalize);

            int m_BufferToTextureCalcGradient = m_Compute.FindKernel(Labels.BufferToTexture);

                if (!LoadMeshToComputeBuffers(mesh, ref m_VertexBuffer, ref m_IndexBuffer, ref m_VertexBufferStride, ref m_VertexBufferPosAttributeOffset, ref m_IndexFormat, ref m_ThreadGroupCountTriangles))
                {
                    ReleaseGraphicsBuffer(ref m_VertexBuffer);
                    ReleaseGraphicsBuffer(ref m_IndexBuffer);
                    return null;
                }
            try
            {
                cmd.SetComputeIntParam(m_Compute, Uniforms._DispatchSizeX, dispatchSizeX);
                cmd.SetComputeVectorParam(m_Compute, Uniforms.g_Origin, voxelBounds.center - voxelBounds.extents);
                cmd.SetComputeFloatParam(m_Compute, Uniforms.g_CellSize, voxelSize);
                cmd.SetComputeIntParam(m_Compute, Uniforms.g_NumCellsX, voxelResolution.x);
                cmd.SetComputeIntParam(m_Compute, Uniforms.g_NumCellsY, voxelResolution.y);
                cmd.SetComputeIntParam(m_Compute, Uniforms.g_NumCellsZ, voxelResolution.z);
                int[] voxelResolutionArray = { voxelResolution.x, voxelResolution.y, voxelResolution.z, voxelCount };
                cmd.SetComputeIntParams(m_Compute, Uniforms._VoxelResolution, voxelResolutionArray);
                float maxDistance = voxelBounds.size.magnitude;
                cmd.SetComputeFloatParam(m_Compute, Uniforms._MaxDistance, maxDistance);
                cmd.SetComputeFloatParam(m_Compute, Uniforms.INITIAL_DISTANCE, maxDistance * 1.01f);
                cmd.SetComputeMatrixParam(m_Compute, Uniforms._WorldToLocal, Matrix4x4.identity);

                // Last FloodStep should finish writing into m_SDFBufferBis, so that we always end up
                // writing to m_SDFBuffer in FinalizeFlood
                ComputeBuffer bufferPing = m_SDFBufferBis;
                ComputeBuffer bufferPong = m_SDFBuffer;
                if (floodIterations % 2 == 0 && floodMode == FloodMode.Linear)
                {
                    bufferPing = m_SDFBuffer;
                    bufferPong = m_SDFBufferBis;
                }

                cmd.BeginSample(Labels.Initialize);
                int kernel = m_InitializeKernel;
                cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms.g_SignedDistanceField, bufferPing);
                cmd.DispatchCompute(m_Compute, kernel, dispatchSizeX, dispatchSizeY, 1);
                cmd.EndSample(Labels.Initialize);

                cmd.BeginSample(Labels.SplatTriangleDistances);
                kernel = distanceMode == DistanceMode.Signed && floodMode == FloodMode.Linear ? m_SplatTriangleDistancesSignedKernel : m_SplatTriangleDistancesUnsignedKernel;

                cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._VertexBuffer, m_VertexBuffer);
                cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._IndexBuffer, m_IndexBuffer);
                cmd.SetComputeIntParam(m_Compute, Uniforms._IndexFormat16bit, m_IndexFormat == IndexFormat.UInt16 ? 1 : 0);
                cmd.SetComputeIntParam(m_Compute, Uniforms._VertexBufferStride, m_VertexBufferStride);
                cmd.SetComputeIntParam(m_Compute, Uniforms._VertexBufferPosAttributeOffset, m_VertexBufferPosAttributeOffset);

                cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms.g_SignedDistanceField, bufferPing);
                cmd.DispatchCompute(m_Compute, kernel, m_ThreadGroupCountTriangles, 1, 1);
                cmd.EndSample(Labels.SplatTriangleDistances);

                cmd.BeginSample(Labels.Finalize);
                kernel = m_FinalizeKernel;
                cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms.g_SignedDistanceField, bufferPing);
                cmd.DispatchCompute(m_Compute, kernel, dispatchSizeX, dispatchSizeY, 1);
                cmd.EndSample(Labels.Finalize);

                if (floodMode == FloodMode.Linear)
                {
                    cmd.BeginSample(Labels.LinearFloodStep);
                    kernel = floodQuality == FloodFillQuality.Normal ? m_LinearFloodStepKernel : m_LinearFloodStepUltraQualityKernel;
                    for (int i = 0; i < floodIterations; i++)
                    {
                        cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._SDFBuffer, i % 2 == 0 ? bufferPing : bufferPong);
                        cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._SDFBufferRW, i % 2 == 0 ? bufferPong : bufferPing);
                        cmd.DispatchCompute(m_Compute, kernel, dispatchSizeX, dispatchSizeY, 1);
                    }
                    cmd.EndSample(Labels.LinearFloodStep);
                }
                else
                {
                    cmd.BeginSample(Labels.JumpFloodInitialize);
                    kernel = m_JumpFloodInitialize;
                    cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._SDFBuffer, bufferPing);
                    cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._JumpBufferRW, m_JumpBuffer);
                    cmd.DispatchCompute(m_Compute, kernel, dispatchSizeX, dispatchSizeY, 1);
                    cmd.EndSample(Labels.JumpFloodInitialize);

                    int maxDim = Mathf.Max(Mathf.Max(voxelResolution.x, voxelResolution.y), voxelResolution.z);
                    int jumpFloodStepCount = Mathf.FloorToInt(Mathf.Log(maxDim, 2)) - 1;

                    cmd.BeginSample(Labels.JumpFloodStep);
                    bool bufferFlip = true;
                    int[] jumpOffsetInterleaved = new int[3];
                    for (int i = 0; i < jumpFloodStepCount; i++)
                    {
                        int jumpOffset = Mathf.FloorToInt(Mathf.Pow(2, jumpFloodStepCount - 1 - i) + 0.5f);
                        if (floodQuality == FloodFillQuality.Normal)
                        {
                            kernel = m_JumpFloodStep;
                            for (int j = 0; j < 3; j++)
                            {
                                jumpOffsetInterleaved[j] = jumpOffset;
                                jumpOffsetInterleaved[(j + 1) % 3] = jumpOffsetInterleaved[(j + 2) % 3] = 0;
                                cmd.SetComputeIntParams(m_Compute, Uniforms._JumpOffsetInterleaved, jumpOffsetInterleaved);
                                cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._JumpBuffer, bufferFlip ? m_JumpBuffer : m_JumpBufferBis);
                                cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._JumpBufferRW, bufferFlip ? m_JumpBufferBis : m_JumpBuffer);
                                cmd.DispatchCompute(m_Compute, kernel, dispatchSizeX, dispatchSizeY, 1);
                                bufferFlip = !bufferFlip;
                            }
                        }
                        else
                        {
                            kernel = m_JumpFloodStepUltraQuality;
                            cmd.SetComputeIntParam(m_Compute, Uniforms._JumpOffset, jumpOffset);
                            cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._JumpBuffer, bufferFlip ? m_JumpBuffer : m_JumpBufferBis);
                            cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._JumpBufferRW, bufferFlip ? m_JumpBufferBis : m_JumpBuffer);
                            cmd.DispatchCompute(m_Compute, kernel, dispatchSizeX, dispatchSizeY, 1);
                            bufferFlip = !bufferFlip;
                        }
                    }
                    cmd.EndSample(Labels.JumpFloodStep);

                    cmd.BeginSample(Labels.JumpFloodFinalize);
                    kernel = m_JumpFloodFinalize;
                    cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._JumpBuffer, bufferFlip ? m_JumpBuffer : m_JumpBufferBis);
                    cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._SDFBuffer, m_SDFBufferBis);
                    cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._SDFBufferRW, m_SDFBuffer);
                    cmd.SetComputeFloatParam(m_Compute, Uniforms.g_CellSize, voxelSize);
                    cmd.DispatchCompute(m_Compute, kernel, dispatchSizeX, dispatchSizeY, 1);
                    cmd.EndSample(Labels.JumpFloodFinalize);
                }

                cmd.BeginSample(Labels.BufferToTexture);
                kernel = m_BufferToTextureCalcGradient;
                cmd.SetComputeBufferParam(m_Compute, kernel, Uniforms._SDFBuffer, m_SDFBuffer);
                cmd.SetComputeTextureParam(m_Compute, kernel, Uniforms._SDF, sdfRT);
                cmd.SetComputeFloatParam(m_Compute, Uniforms._Offset, distanceMode == DistanceMode.Signed && floodMode != FloodMode.Jump ? offset : 0);
                cmd.DispatchCompute(m_Compute, kernel, dispatchSizeX, dispatchSizeY, 1);
                cmd.EndSample(Labels.BufferToTexture);

                Graphics.ExecuteCommandBuffer(cmd);

                ReleaseGraphicsBuffer(ref m_VertexBuffer);
                ReleaseGraphicsBuffer(ref m_IndexBuffer);
                m_SDFBuffer?.Release();
                m_SDFBufferBis?.Release();
                m_JumpBuffer?.Release();
                m_JumpBufferBis?.Release();

                CommandBufferPool.Release(cmd);

            }
            catch
            {
                Vector3 pos = DebugManager.Instance.debugObject.transform.position;
                pos.y = 2;
                DebugManager.Instance.debugObject.transform.position = pos;
            }
            return sdfRT;
        }
        public static bool LoadMeshToComputeBuffers(
    Mesh mesh,
    ref GraphicsBuffer vertexBuffer,
    ref GraphicsBuffer indexBuffer,
    ref int vertexBufferStride,
    ref int vertexBufferPosAttributeOffset,
    ref IndexFormat indexFormat,
    ref int threadGroupCountTriangles
)
        {

            if (mesh == null)
            {
                Debug.LogError("LoadMeshToComputeBuffers: input Mesh is null.");
                return false;
            }

            if (mesh.GetTopology(0) != MeshTopology.Triangles)
            {
                Debug.LogError($"LoadMeshToComputeBuffers: The mesh '{mesh.name}' is not using Triangles topology.");
                return false;
            }

            int stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
            if (stream < 0)
            {
                Debug.LogError($"LoadMeshToComputeBuffers: The mesh '{mesh.name}' has no VertexAttribute.Position, aborting.");
                return false;
            }

            ReleaseGraphicsBuffer(ref vertexBuffer);
            ReleaseGraphicsBuffer(ref indexBuffer);

            mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;

            indexFormat = mesh.indexFormat;                         // 可能是 UInt16 或 UInt32
            vertexBufferStride = mesh.GetVertexBufferStride(stream);
            vertexBufferPosAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);

            vertexBuffer = mesh.GetVertexBuffer(stream);
            indexBuffer = mesh.GetIndexBuffer();

            int triangleCount = indexBuffer.count / 3;
            threadGroupCountTriangles = Mathf.CeilToInt((float)triangleCount / kThreadCount);
            bool success = (vertexBuffer != null && indexBuffer != null);
            if (!success)
            {
                Debug.LogError($"LoadMeshToComputeBuffers: Failed to get valid GPU buffers for mesh '{mesh.name}'.");
            }

            return success;

        }
        static void CreateComputeBuffer(ref ComputeBuffer cb, int length, int stride)
        {
            if (cb != null && cb.count == length && cb.stride == stride)
                return;

            ReleaseComputeBuffer(ref cb);
            cb = new ComputeBuffer(length, stride);
        }

        static void ReleaseComputeBuffer(ref ComputeBuffer buffer)
        {
            if (buffer != null)
                buffer.Release();
            buffer = null;
        }

        static void ReleaseGraphicsBuffer(ref GraphicsBuffer buffer)
        {
            if (buffer != null)
                buffer.Release();
            buffer = null;
        }
    }
}