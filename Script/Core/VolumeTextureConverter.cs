using PhotonSystem;
using UnityEngine;
using UnityEngine.Rendering;
namespace PhotonSystem
{
    public static class VolumeTextureConverter
    {
        /// <summary>
        /// 将可读的 3D RenderTexture 直接逐层读回到 Texture3D。
        /// </summary>
        /// <param name="volumeRT">3D RenderTexture（dimension=Tex3D），可被 CPU ReadPixels。</param>
        /// <returns>生成的 Texture3D 对象</returns>
        public static Texture3D RenderTextureToTexture3D(RenderTexture volumeRT)
        {
            if (volumeRT == null)
            {
                Debug.LogError("RenderTextureToTexture3D failed: input RenderTexture is null.");
                return null;
            }

            if (volumeRT.dimension != TextureDimension.Tex3D)
            {
                Debug.LogError($"RenderTextureToTexture3D failed: RenderTexture dimension must be Tex3D. Current dimension: {volumeRT.dimension}");
                return null;
            }

            // 获取基本信息
            int width = volumeRT.width;
            int height = volumeRT.height;
            int depth = volumeRT.volumeDepth;

            // 创建目标 Texture3D（这里以 RGBAFloat 为例，你也可以根据需求修改）
            Texture3D texture3D = new Texture3D(width, height, depth, TextureFormat.RGBAFloat, false);

            // 备份当前激活的 RenderTexture
            RenderTexture prevRT = RenderTexture.active;

            // 逐层读取
            for (int z = 0; z < depth; z++)
            {
                // 将第 z 层设置为当前渲染目标
                Graphics.SetRenderTarget(volumeRT, 0, CubemapFace.Unknown, z);

                // 用一个临时 Texture2D 来读取像素
                Texture2D temp2D = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
                temp2D.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                temp2D.Apply();

                // 获取像素数组，写入到 Texture3D 的第 z 层
                Color[] colors = temp2D.GetPixels();
                texture3D.SetPixels(colors, z);

                Object.DestroyImmediate(temp2D);
            }

            // 应用所有更改
            texture3D.Apply();

            // 恢复之前的激活状态
            RenderTexture.active = prevRT;

            return texture3D;
        }
    }
}