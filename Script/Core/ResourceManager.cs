using UnityEngine;
using UnityEditor; 
namespace PhotonSystem
{

    public class ResourceManager : PhotonSingleton<ResourceManager>
    {

        public ComputeShader _blendMaskCompute;
        public static ComputeShader BlendMaskCompute
        {
            get
            {
                if (Instance._blendMaskCompute == null)
                {
                    Instance._blendMaskCompute = AttemptLoad<ComputeShader>("Assets/PhotonGiSystem/HLSL/BlendMaskCompute.compute");
                }
                return Instance._blendMaskCompute;
            }
        }

        public ComputeShader _copy3DCompute;
        public static ComputeShader Copy3DCompute
        {
            get
            {
                if (Instance._copy3DCompute == null)
                {
                    Instance._copy3DCompute = AttemptLoad<ComputeShader>("Assets/PhotonGiSystem/HLSL/Copy3D.compute");
                }
                return Instance._copy3DCompute;
            }
        }

        public ComputeShader _sdfMeshCompute;
        public static ComputeShader SdfMeshCompute
        {
            get
            {
                if (Instance._sdfMeshCompute == null)
                {
                    Instance._sdfMeshCompute = AttemptLoad<ComputeShader>("Assets/PhotonGiSystem/SDF/Runtime/MeshToSDF.compute");
                }
                return Instance._sdfMeshCompute;
            }
        }


        public ComputeShader _motionVectorCompute;
        public static ComputeShader MotionVectorCompute
        {
            get
            {
                if (Instance._motionVectorCompute == null)
                {
                    Instance._motionVectorCompute = AttemptLoad<ComputeShader>("Assets/PhotonGiSystem/HLSL/MotionVector.compute");
                }
                return Instance._motionVectorCompute;
            }
        }



        private static T AttemptLoad<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                Debug.LogError($"ResourceManager: 无法在路径 {path} 加载类型 {typeof(T).Name} 的资源！");
            }
            return asset;
#else
        return null;
#endif
        }
    }
}