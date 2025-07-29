using System;
using UnityEngine;
namespace PhotonSystem
{
    public class SDFMeshTexture : ScriptableObject
    {
        [SerializeField]
        public Mesh mesh; // 关联的网格

        [SerializeField]
        public RenderTexture sdfTexture; // SDF 数据的三维纹理

        public int maxResolution = 64;
        public FloodMode floodMode = FloodMode.Jump;
        public FloodFillQuality floodFillQuality = FloodFillQuality.Ultra;
        public int floodIterations = 64;
        public float offset = 0f;


        public Mesh Mesh
        {
            get => mesh;
            set => mesh = value;
        }

        public RenderTexture SDFTexture
        {
            get => sdfTexture;
            set => sdfTexture = value;
        }
        public Matrix4x4 GetLocalTo01Matrix()
        {
            Matrix4x4 translate = Matrix4x4.Translate(-mesh.bounds.center);
            Vector3 size = mesh.bounds.size;
            Matrix4x4 scale = Matrix4x4.Scale(new Vector3(1f / Mathf.Max(size.x, 0.01f), 1f / Mathf.Max(size.y, 0.01f), 1f / Mathf.Max(size.z, 0.01f)));
            Matrix4x4 worldToSDF = scale * translate;

            return worldToSDF;
        }
        public Matrix4x4 GetWorldToSDFLocalMatrix(Transform transform)
        {
            return transform.worldToLocalMatrix;
        }
        /// <summary>
        /// 初始化 SDF 数据
        /// </summary>
        /// <param name="sourceMesh">需要生成 SDF 的网格</param>
        /// <param name="maxResolution">最大的分辨率</param>
        public void Initialize(Mesh sourceMesh)
        {
            mesh = sourceMesh;
            
            if (mesh == null)
            {
                Debug.LogError("SDFMeshTexture initialization failed: Source mesh is null.");
                return;
            }

            GenerateSDF(mesh);
        }
        private void OnDestroy()
        {
        }
        public void Release()
        {
            sdfTexture?.Release();
            DestroyImmediate(sdfTexture);
            //DestroyImmediate(mesh);
        }
        /// <summary>
        /// 生成 SDF 数据
        /// </summary>
        /// <param name="sourceMesh">目标网格</param>
        private void GenerateSDF(Mesh sourceMesh)
        {
            RenderTexture renderTexture = MeshSDFGenerator.GenerateSDF(sourceMesh, maxResolution, floodMode, floodFillQuality, floodIterations, DistanceMode.Unsigned, offset);
            sdfTexture = renderTexture;
        }
        RenderTexture ClipTo2d(RenderTexture renderTexture3D, int depth)
        {
            int staide = 8;
            RenderTexture renderTexture = new RenderTexture(renderTexture3D.width, renderTexture3D.height, 0, RenderTextureFormat.RHalf)
            {
                enableRandomWrite = true
            };
            ComputeShader computeShader = ResourceManager.Copy3DCompute;
            int kernel = computeShader.FindKernel("ClipTex");
            computeShader.SetTexture(kernel, "_SDFShape", renderTexture3D);
            computeShader.SetTexture(kernel, "_ClipResult", renderTexture);

            computeShader.SetInt("_Depth", depth);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)renderTexture3D.width / staide), Mathf.CeilToInt((float)renderTexture3D.height / staide), 1);
            return renderTexture;
        }
        Texture3D ReadRenderTexture3D(RenderTexture renderTexture3D, TextureFormat textureFormat)
        {
            int width = renderTexture3D.width;
            int height = renderTexture3D.height;
            int depth = renderTexture3D.volumeDepth;

            // 创建一个3D纹理
            Texture3D texture3D = new Texture3D(width, height, depth, textureFormat, false);

            // 临时的Texture2D，用于读取每个切片
            Texture2D tempTexture2D = new Texture2D(width, height, textureFormat, false);
            // 保存当前的激活的RenderTexture
            RenderTexture previous = RenderTexture.active;
            Color[] texs = new Color[width * height * depth];
            for (int z = 0; z < depth; z++)
            {



                // 读取当前切片到Texture2D
                RenderTexture result = ClipTo2d(renderTexture3D, z);
                RenderTexture.active = result;
                tempTexture2D.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tempTexture2D.Apply();

                // 获取当前切片的像素数据
                Color[] pixels = tempTexture2D.GetPixels();
                Array.Copy(pixels, 0, texs, z * width * height, pixels.Length);
                RenderTexture.active = previous;
                result.Release();
            }

            // 设置到3D纹理的对应切片
            texture3D.SetPixels(texs);
            // 应用所有切片的像素数据到3D纹理
            texture3D.Apply();

            // 恢复之前的激活的RenderTexture
            RenderTexture.active = previous;
            return texture3D;
        }

        public void OnValidate()
        {
            Initialize(mesh);
        }
    }
}