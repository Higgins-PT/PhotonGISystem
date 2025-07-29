using UnityEngine;
namespace PhotonSystem
{
    public static class ShaderProperties
    {
        /// <summary>
        /// �� Copy3D.compute �ж���� RWTexture3D<float4> _Source;
        /// </summary>
        public const string SourceTexture = "_Source";

        /// <summary>
        /// �� Copy3D.compute �ж���� RWTexture3D<float4> _Destination;
        /// </summary>
        public const string DestinationTexture = "_Destination";
        public const string Depth = "_Depth";
        public const string SDFShape = "_SDFShape";
        public const string ClipResult = "_ClipResult";

    }

}