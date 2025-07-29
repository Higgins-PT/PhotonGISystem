using PhotonSystem;
using UnityEngine;
using UnityEngine.Rendering;
namespace PhotonSystem
{
    public static class VolumeTextureConverter
    {
        /// <summary>
        /// ���ɶ��� 3D RenderTexture ֱ�������ص� Texture3D��
        /// </summary>
        /// <param name="volumeRT">3D RenderTexture��dimension=Tex3D�����ɱ� CPU ReadPixels��</param>
        /// <returns>���ɵ� Texture3D ����</returns>
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

            // ��ȡ������Ϣ
            int width = volumeRT.width;
            int height = volumeRT.height;
            int depth = volumeRT.volumeDepth;

            // ����Ŀ�� Texture3D�������� RGBAFloat Ϊ������Ҳ���Ը��������޸ģ�
            Texture3D texture3D = new Texture3D(width, height, depth, TextureFormat.RGBAFloat, false);

            // ���ݵ�ǰ����� RenderTexture
            RenderTexture prevRT = RenderTexture.active;

            // ����ȡ
            for (int z = 0; z < depth; z++)
            {
                // ���� z ������Ϊ��ǰ��ȾĿ��
                Graphics.SetRenderTarget(volumeRT, 0, CubemapFace.Unknown, z);

                // ��һ����ʱ Texture2D ����ȡ����
                Texture2D temp2D = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
                temp2D.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                temp2D.Apply();

                // ��ȡ�������飬д�뵽 Texture3D �ĵ� z ��
                Color[] colors = temp2D.GetPixels();
                texture3D.SetPixels(colors, z);

                Object.DestroyImmediate(temp2D);
            }

            // Ӧ�����и���
            texture3D.Apply();

            // �ָ�֮ǰ�ļ���״̬
            RenderTexture.active = prevRT;

            return texture3D;
        }
    }
}