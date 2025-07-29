using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotonSystem
{
    public class RayTraceManager : PhotonSingleton<RayTraceManager>
    {

        public void SetTraceCSData(PhotonRenderingData photonRenderingData, ComputeShader computeShader, int kernel, Camera camera)
        {
            CommandBuffer cmd = photonRenderingData.cmd;
            cmd.SetComputeTextureParam(computeShader, kernel, "_EnvironmentCube", SkyBoxManager.Instance.cubemap);
            HZBManager.Instance.SetScreenTraceData(photonRenderingData, computeShader, kernel, camera);
            LocalSDFManager.Instance.SetCSData(cmd, computeShader, kernel, camera);
            GlobalVoxelManager.Instance.SetCSData(cmd, computeShader, kernel, camera);
        }
    }
}