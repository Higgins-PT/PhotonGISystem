using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
namespace PhotonSystem
{
    public class MeshManager : PhotonSingleton<MeshManager>
    {
        public class SDFAndCount
        {
            public SDFMeshTexture sdf;
            public int count;
            public SDFAndCount(SDFMeshTexture sdf, int count)
            {
                this.sdf = sdf;
                this.count = count;
            }
        }
        public int sdfCount = 0;
        private Dictionary<Mesh, SDFAndCount> meshSdfTextures = new Dictionary<Mesh, SDFAndCount>();

        public SDFMeshTexture GetSDFMeshTexture(PhotonObject photonObject)
        {
            if (meshSdfTextures.TryGetValue(photonObject.mesh, out SDFAndCount sdfAndCount))
            {
                return sdfAndCount.sdf;
            }
            return null;
        }
        public void UpdateSDFCount()
        {
            sdfCount = meshSdfTextures.Count;
        }
        public void AddMesh(PhotonObject photonObject)
        {

            Mesh mesh = photonObject.mesh;

            if (mesh == null)
            {
                Debug.LogWarning("No Mesh found in the provided PhotonObject.");
                return;
            }

            // Check if the mesh is already processed
            if (meshSdfTextures.ContainsKey(mesh))
            {
                SDFAndCount value = meshSdfTextures[mesh];
                value.count++;
                photonObject.SDFMeshTexture = value.sdf;
                return;
            }

            SDFMeshTexture newTexture = CreateSDFMeshTexture(mesh);
            SDFAndCount sDFAndCount = new SDFAndCount(newTexture, 1);
            meshSdfTextures[mesh] = sDFAndCount;
            photonObject.sdfTexSize = new int3(newTexture.sdfTexture.width, newTexture.sdfTexture.height, newTexture.sdfTexture.volumeDepth);
            photonObject.SDFMeshTexture = newTexture;
            UpdateSDFCount();

        }

        public void RemoveMesh(PhotonObject photonObject)
        {
            Mesh mesh = photonObject.mesh;
            if (mesh == null)
            {
                Debug.LogWarning("No Mesh found in the provided PhotonObject.");
                return;
            }

            if (meshSdfTextures.ContainsKey(mesh))
            {
                SDFAndCount value = meshSdfTextures[mesh];
                value.count--;
                if (value.count <= 0)
                {
                    value.sdf.Release();
                    meshSdfTextures.Remove(mesh);
                    LocalSDFManager.Instance.RemoveMeshToSdfDictionary(photonObject);
                    UpdateSDFCount();
                }
                return;
            }

        }

        private SDFMeshTexture CreateSDFMeshTexture(Mesh mesh)
        {

            SDFMeshTexture newTexture = ScriptableObject.CreateInstance<SDFMeshTexture>();

            newTexture.mesh = mesh;
            newTexture.Initialize(mesh);

            return newTexture;
        }
    }
}