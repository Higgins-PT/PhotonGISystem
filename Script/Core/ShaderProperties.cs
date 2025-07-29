using UnityEngine;
namespace PhotonSystem
{
    public static class ShaderProperties
    {
        /// <summary>
        /// 在 Copy3D.compute 中定义的 RWTexture3D<float4> _Source;
        /// </summary>
        public const string SourceTexture = "_Source";

        /// <summary>
        /// 在 Copy3D.compute 中定义的 RWTexture3D<float4> _Destination;
        /// </summary>
        public const string DestinationTexture = "_Destination";
        public const string Depth = "_Depth";
        public const string SDFShape = "_SDFShape";
        public const string ClipResult = "_ClipResult";

    }

}