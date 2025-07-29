using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotonSystem
{
    public class LocalSDFManager : PhotonSingleton<LocalSDFManager>, IRadianceFeature
    {

        public Dictionary<Mesh, SDFDataInfo> sdfDataDic = new Dictionary<Mesh, SDFDataInfo>();
        Dictionary<string, int> surfaceCacheIndex = new Dictionary<string, int>();
        public List<SDFDataInfo> sdfTextures = new List<SDFDataInfo>();
        private ObjectData[] objectDatas = null;
        private BVHNodeInfo[] bVHNodeInfo = null;
        private SurfaceCacheInfo[] surfaceCacheInfo = null;
        private MeshCardInfo[] meshCardInfo = null;
        [HideInInspector]
        public ComputeShader serializeTextureCompute;
        private ComputeBuffer sdfBuffer = null;
        private ComputeBuffer objectBuffer = null;
        private ComputeBuffer bvhBuffer = null;
        private ComputeBuffer surfaceCacheBuffer = null;
        private ComputeBuffer meshCardBuffer = null;
        public float sdfHitThreshold = 0.1f;
        public float localSDFStartOffest = 0.002f;
        int m_SerializeTextureToBuffer = -1;
        private void OnDisable()
        {
            TryReleaseBuffer(ref surfaceCacheBuffer);
            TryReleaseBuffer(ref meshCardBuffer);
            TryReleaseBuffer(ref sdfBuffer);
            TryReleaseBuffer(ref objectBuffer);
            TryReleaseBuffer(ref bvhBuffer);
        }
        public void SetCSData(CommandBuffer cmd, ComputeShader computeShader, int kernel, Camera camera)
        {
            cmd.SetComputeBufferParam(computeShader, kernel, "_SurfaceCacheInfo", surfaceCacheBuffer);
            cmd.SetComputeBufferParam(computeShader, kernel, "_MeshCardInfo", meshCardBuffer);
            cmd.SetComputeBufferParam(computeShader, kernel, "_SDFDataBuffer", sdfBuffer);
            cmd.SetComputeBufferParam(computeShader, kernel, "_ObjectBuffer", objectBuffer);
            cmd.SetComputeBufferParam(computeShader, kernel, "_BVHBuffer", bvhBuffer);

            var scMgr = SurfaceCacheManager.Instance;
            cmd.SetComputeBufferParam(computeShader, kernel, "_SurfaceCache", scMgr.surfaceCache);

            cmd.SetComputeFloatParam(computeShader, "_SdfHitThreshold", sdfHitThreshold);
            cmd.SetComputeFloatParam(computeShader, "_LocalSDFStartOffest", localSDFStartOffest);
            cmd.SetComputeIntParam(computeShader, "_BVHCount", bvhBuffer.count);

            cmd.SetComputeIntParam(computeShader, "_SurfaceCacheSize", SurfaceCacheManager.size);
            float near = camera.nearClipPlane;
            float far = camera.farClipPlane;
            Vector4 zBufferParams;
            zBufferParams = new Vector4(-1 + far / near, 1, (-1 + far / near) / far, 1 / far);

            if (SkyBoxManager.Instance.cubemap != null)
            {
                Cubemap cubemap = SkyBoxManager.Instance.cubemap;

                cmd.SetComputeTextureParam(computeShader, kernel, "_SkyBox", cubemap);
            }

            cmd.SetComputeVectorParam(computeShader, "_CameraPosition", camera.transform.position);
            cmd.SetComputeVectorParam(computeShader, "_ZBufferParams", zBufferParams);
            cmd.SetComputeMatrixParam(computeShader, "_ProjectionMatrixInverse", camera.projectionMatrix.inverse);
            cmd.SetComputeMatrixParam(computeShader, "_ViewMatrixInverse", camera.cameraToWorldMatrix);
            cmd.SetComputeMatrixParam(computeShader, "_ProjectionMatrix", camera.projectionMatrix);
            cmd.SetComputeMatrixParam(computeShader, "_ViewMatrix", camera.worldToCameraMatrix);
        }
        public void Awake()
        {
            m_SerializeTextureToBuffer = serializeTextureCompute.FindKernel("SerializeTextureToBuffer");
        }
        public SDFDataInfo AddMeshToSdfDictionary(SDFMeshTexture sdfMeshTexture)
        {

            Mesh mesh = sdfMeshTexture.Mesh;
            RenderTexture sdfTexture = sdfMeshTexture.SDFTexture;
            if (mesh == null || sdfTexture == null)
            {
                Debug.LogError("Mesh or SDF Texture is null. Cannot add to dictionary.");
                return new SDFDataInfo(null, 0, 0);
            }
            if (sdfDataDic.ContainsKey(mesh))
            {
                Debug.LogWarning($"Mesh {mesh.name} already exists in the dictionary.");
                return new SDFDataInfo(null, 0, 0);
            }
            int offset = 0;
            if (sdfTextures.Count != 0)
            {
                SDFDataInfo sDFDataInfo = sdfTextures[sdfTextures.Count - 1];
                offset = sDFDataInfo.offest + sDFDataInfo.length;
            }
            SDFDataInfo dataInfo = new SDFDataInfo(sdfTexture, offset, GetOffest(sdfTexture));
            sdfDataDic[mesh] = dataInfo;
            return dataInfo;
        }
        public int GetOffest(RenderTexture texture3D)
        {
            return texture3D.width * texture3D.height * texture3D.volumeDepth;
        }
        int lastSDFBufferSize;
        public void InputSDFToBuffer()
        {
            
            CommandBuffer cmd = new CommandBuffer();
            cmd.name = "Texture3dToBuffer";

            const int threadGroupSize = 8;
            int sdfBufferSize = sdfTextures[sdfTextures.Count - 1].offest + GetOffest(sdfTextures[sdfTextures.Count - 1].sdfTexture);

            if (lastSDFBufferSize != sdfBufferSize || sdfBuffer == null)
            {
                TryReleaseBuffer(ref sdfBuffer);
                sdfBuffer = new ComputeBuffer(sdfBufferSize, sizeof(float));
                lastSDFBufferSize = sdfBufferSize;
            }
            for (int i = 0; i < sdfTextures.Count; i++)
            {

                SDFDataInfo sDFDataInfo = sdfTextures[i];
                RenderTexture texture = sDFDataInfo.sdfTexture;

                cmd.SetComputeTextureParam(serializeTextureCompute, m_SerializeTextureToBuffer, "_SourceTexture", texture);
                cmd.SetComputeBufferParam(serializeTextureCompute, m_SerializeTextureToBuffer, "_TargetBuffer", sdfBuffer);
                cmd.SetComputeIntParam(serializeTextureCompute, "_Width", texture.width);
                cmd.SetComputeIntParam(serializeTextureCompute, "_Height", texture.height);
                cmd.SetComputeIntParam(serializeTextureCompute, "_Depth", texture.volumeDepth);
                cmd.SetComputeIntParam(serializeTextureCompute, "_Offest", sDFDataInfo.offest);

                int threadGroupsX = Mathf.CeilToInt((float)texture.width / threadGroupSize);
                int threadGroupsY = Mathf.CeilToInt((float)texture.height / threadGroupSize);
                int threadGroupsZ = Mathf.CeilToInt((float)texture.volumeDepth / threadGroupSize);

                cmd.DispatchCompute(serializeTextureCompute, m_SerializeTextureToBuffer, threadGroupsX, threadGroupsY, threadGroupsZ);
            }

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }
        public void RemoveMeshToSdfDictionary(PhotonObject photonObject)
        {
            SDFDataInfo sDFDataInfo;
            if (sdfDataDic.TryGetValue(photonObject.mesh, out sDFDataInfo))
            {
                sdfDataDic.Remove(photonObject.mesh);
            }
        }
        public ObjectData GetObjectDataFromPhotonObject(PhotonObject photonObject)
        {

            ObjectData objectData = new ObjectData();
            SDFDataInfo sDFDataInfo;
            if (!sdfDataDic.TryGetValue(photonObject.mesh, out sDFDataInfo))
            {
                if (photonObject.SDFMeshTexture == null)
                {
                    Debug.LogError($"Object {photonObject.gameObject.name} SDF is null");
                }
                sDFDataInfo = AddMeshToSdfDictionary(photonObject.SDFMeshTexture);
            }
            else
            {
                if (sDFDataInfo.sdfTexture != photonObject.SDFMeshTexture.sdfTexture)
                {
                    RemoveMeshToSdfDictionary(photonObject);
                    if (photonObject.SDFMeshTexture == null)
                    {
                        Debug.LogError($"Object {photonObject.gameObject.name} SDF is null");
                    }
                    sDFDataInfo = AddMeshToSdfDictionary(photonObject.SDFMeshTexture);
                }
            }


            objectData.sdfDataOffest = sDFDataInfo.offest;
            objectData.sdfDataSize = new int3(sDFDataInfo.sdfTexture.width, sDFDataInfo.sdfTexture.height, sDFDataInfo.sdfTexture.volumeDepth);
            objectData.worldToLocalAffineMatrix = photonObject.SDFMeshTexture.GetWorldToSDFLocalMatrix(photonObject.transform);
            objectData.localTo01AffineMatrix = photonObject.SDFMeshTexture.GetLocalTo01Matrix();
            objectData.localToWorldAffineMatrix = objectData.worldToLocalAffineMatrix.inverse;
            Vector3 meshSize = photonObject.mesh.bounds.size;
            int3 size = objectData.sdfDataSize;
            objectData.deltaSize = Mathf.Max(meshSize.x, Mathf.Max(meshSize.y, meshSize.z)) / Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            objectData.directLightSurfaceCacheIndex = -1;
            if (surfaceCacheIndex.TryGetValue(photonObject.GetObjectHash(), out int index))
            {
                objectData.directLightSurfaceCacheIndex = index;
            }

            return objectData;
        }   
        public void RefreshLocalSDFData()
        {


            int count = BVHManager.Instance.photonObjects.Count;
            objectDatas = new ObjectData[count];
            for (int i = 0; i < count; i++)
            {
                PhotonObject photonObject = BVHManager.Instance.photonObjects[i];
                objectDatas[i] = GetObjectDataFromPhotonObject(photonObject);
            }

            sdfTextures.Clear();
            foreach (var value in sdfDataDic)
            {
                SDFDataInfo modifyData = value.Value;
                if (sdfTextures.Count > 0)
                {
                    SDFDataInfo sDFDataInfo;
                    sDFDataInfo = sdfTextures[sdfTextures.Count - 1];

                    modifyData.offest = sDFDataInfo.length + sDFDataInfo.offest;
                }
                else
                {
                    modifyData.offest = 0;
                }

                sdfTextures.Add(modifyData);

            }



            BuildSurfaceCacheInfos(SurfaceCacheManager.Instance.directLightSurfaceHash, SurfaceCacheManager.Instance.directLightSurfaceObjectsHash);

            bVHNodeInfo = ConvertBVHNodeListToArray(BVHManager.Instance.tree.nodes);
            RefreshBufferData(ref surfaceCacheBuffer, surfaceCacheInfo);

            RefreshBufferData(ref meshCardBuffer, meshCardInfo);
            RefreshBufferData(ref bvhBuffer, bVHNodeInfo);
            RefreshBufferData(ref objectBuffer, objectDatas);//input objectdata to buffer
            InputSDFToBuffer(); //input sdf to buffer
        }
        public void RefreshBufferData<T>(ref ComputeBuffer computeBuffer, T[] values) where T : struct 
        {
            TryReleaseBuffer(ref computeBuffer);

            int objectDataStride = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            computeBuffer = new ComputeBuffer(values.Length, objectDataStride);
            computeBuffer.SetData(values);
        }

        public void TryReleaseBuffer(ref ComputeBuffer computeBuffer)
        {
            if (computeBuffer != null)
            {
                computeBuffer.Release();
                computeBuffer = null;
            }
        }
        public static Info[] ConvertArrayToInfo<Source, Info>(Source[] sources, Func<Source, IDictionary<Source, int>, Info> convertFunc)
        {
            Info[] nodeInfoArray = new Info[sources.Length];
            Dictionary<Source, int> sourcesMap = sources.Select((souce, index) => new { souce, index }).ToDictionary(x => x.souce, x => x.index);// Create dictionary map from sources (souce, index)
            for (int i = 0; i < sources.Length; i++)
            {
                nodeInfoArray[i] = convertFunc(sources[i], sourcesMap);
            }
            return nodeInfoArray;
        }
        public static Info[] ConvertListToInfo<Source, Info>(IList<Source> sources, Func<Source, IDictionary<Source, int>, Info> convertFunc)
        {
            Info[] nodeInfoArray = new Info[sources.Count];
            Dictionary<Source, int> sourcesMap = sources.Select((souce, index) => new { souce, index }).ToDictionary(x => x.souce, x => x.index);// Create dictionary map from sources (souce, index)
            for (int i = 0; i < sources.Count; i++)
            {
                nodeInfoArray[i] = convertFunc(sources[i], sourcesMap);
            }
            return nodeInfoArray;
        }
        public static IDictionary<Source, int> ConvertListToMap<Source>(IList<Source> sources)
        {
            Dictionary<Source, int> sourcesMap = sources.Select((souce, index) => new { souce, index }).ToDictionary(x => x.souce, x => x.index);// Create dictionary map from sources (souce, index)
            return sourcesMap;
        }

        public BVHNodeInfo[] ConvertBVHNodeListToArray(List<BVH8Node> nodes)
        {
            var objectMap = (Dictionary<PhotonObject, int>)ConvertListToMap(BVHManager.Instance.photonObjects);

            return ConvertListToInfo(nodes, (node, nodeIndexMap) =>
            {

                return new BVHNodeInfo
                {
                    node0 = (node.children != null && node.children[0] != null && nodeIndexMap.ContainsKey(node.children[0]))
                                ? nodeIndexMap[node.children[0]] : -1,
                    node1 = (node.children != null && node.children[1] != null && nodeIndexMap.ContainsKey(node.children[1]))
                                ? nodeIndexMap[node.children[1]] : -1,
                    node2 = (node.children != null && node.children[2] != null && nodeIndexMap.ContainsKey(node.children[2]))
                                ? nodeIndexMap[node.children[2]] : -1,
                    node3 = (node.children != null && node.children[3] != null && nodeIndexMap.ContainsKey(node.children[3]))
                                ? nodeIndexMap[node.children[3]] : -1,
                    node4 = (node.children != null && node.children[4] != null && nodeIndexMap.ContainsKey(node.children[4]))
                                ? nodeIndexMap[node.children[4]] : -1,
                    node5 = (node.children != null && node.children[5] != null && nodeIndexMap.ContainsKey(node.children[5]))
                                ? nodeIndexMap[node.children[5]] : -1,
                    node6 = (node.children != null && node.children[6] != null && nodeIndexMap.ContainsKey(node.children[6]))
                                ? nodeIndexMap[node.children[6]] : -1,
                    node7 = (node.children != null && node.children[7] != null && nodeIndexMap.ContainsKey(node.children[7]))
                                ? nodeIndexMap[node.children[7]] : -1,
                    objectDataIndex = (node.photonObject != null && objectMap.ContainsKey(node.photonObject))
                                        ? objectMap[node.photonObject] : -1,
                    minPoint = node.center - node.size * 0.5f,
                    maxPoint = node.center + node.size * 0.5f
                };
            });
        }

        /*
        public BVHNodeInfo[] ConvertBVHNodeListToArray(List<BVH2Node> nodes)
        {
            Dictionary<PhotonObject, int> objectMap = (Dictionary<PhotonObject, int>)ConvertListToMap(BVHManager.Instance.photonObjects);

            return ConvertListToInfo(nodes,
                (node, nodeIndexMap) => {
                    return new BVHNodeInfo
                    {
                        leftNodeIndex = node.leftChild != null && nodeIndexMap.ContainsKey(node.leftChild)
                    ? nodeIndexMap[node.leftChild]
                    : -1,
                        rightNodeIndex = node.rightChild != null && nodeIndexMap.ContainsKey(node.rightChild)
                    ? nodeIndexMap[node.rightChild]
                        : -1,
                        objectDataIndex = node.photonObject != null ? objectMap[node.photonObject] : -1,
                        minPoint = node.center - node.size * 0.5f,
                        maxPoint = node.center + node.size * 0.5f
                    };
                });

        }
        */
        public void BuildSurfaceCacheInfos(Dictionary<string, SurfaceCache> directLightSurfaceHash, Dictionary<string, List<PhotonObject>> directLightSurfaceObjectsHash)
        {
            surfaceCacheIndex.Clear();
            List<SurfaceCacheInfo> scInfoList = new List<SurfaceCacheInfo>();
            List<MeshCardInfo> mcInfoList = new List<MeshCardInfo>();
            foreach (var kvp in directLightSurfaceHash)
            {
                string hash = kvp.Key;
                SurfaceCache surfaceCache = kvp.Value;

                if (!directLightSurfaceObjectsHash.TryGetValue(hash, out List<PhotonObject> objList)
                    || objList == null || objList.Count == 0)
                {
                    continue;
                }
                
                int startIndex = mcInfoList.Count;
                int countCards = 0;

                // 遍历 surfaceCache 的 meshCards
                var cards = surfaceCache.meshCards;
                if (cards != null)
                {
                    for (int i = 0; i < cards.Length; i++)
                    {
                        MeshCard card = cards[i];

                        MeshCardInfo mcInfo = new MeshCardInfo();
                        mcInfo.rect = card.tileRect;
                        mcInfo.sdfToLocalMatrix = card.sdfToLocalMatrix;
                        mcInfo.sdfToProjectionMatrix = card.sdfToProjectionMatrix;
                        mcInfo.deltaDepth = card.deltaDepth;
                        // 加入列表
                        mcInfoList.Add(mcInfo);
                        countCards++;
                    }
                }

                SurfaceCacheInfo scInfo = new SurfaceCacheInfo();
                scInfo.meshCardIndex = startIndex;
                scInfo.meshCardCount = countCards;

                scInfoList.Add(scInfo);
                surfaceCacheIndex[hash] = scInfoList.Count - 1;
            }

            surfaceCacheInfo = scInfoList.ToArray();
            meshCardInfo = mcInfoList.ToArray();

        }
        public override void PhotonUpdate()
        {
            RefreshLocalSDFData();
        }

        #region RadianceControl
        public void GetRadianceSample(RadianceControl radianceControl, PhotonRenderingData photonRenderingData)
        {
            // === DIRECT BEGIN ==
            ComputeShader radianceCompute = RadianceManager.Instance.radianceCompute;
            CommandBuffer cmd = photonRenderingData.cmd;
            Camera camera = photonRenderingData.camera;
            int kernelLocalSDFDirectLightSample = radianceCompute.FindKernel("LocalSDFDirectLightSample");

            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, radianceCompute, kernelLocalSDFDirectLightSample, camera);
            radianceControl.SetRadianceCascadesCSData(photonRenderingData, kernelLocalSDFDirectLightSample);
            RadianceManager.Instance.ExecutePersistentThreads(photonRenderingData, radianceCompute, photonRenderingData.DownResolution.x, photonRenderingData.DownResolution.y, kernelLocalSDFDirectLightSample);
            //CommandBufferHelper.DispatchCompute_RT(cmd, radianceCompute, photonRenderingData.activeRT, kernelLocalSDFDirectLightSample, 8);
            // === DIRECT END ===

            // === INDIRECT BEGIN ===
            int kernelLocalSDFDiffuse = radianceCompute.FindKernel("LocalSDFDiffuse");
            cmd.SetRenderTarget(radianceControl.radianceResult);
            RayTraceManager.Instance.SetTraceCSData(photonRenderingData, radianceCompute, kernelLocalSDFDiffuse, camera);
            radianceControl.SetRadianceCascadesCSData(photonRenderingData, kernelLocalSDFDiffuse);
            RadianceManager.Instance.ExecutePersistentThreads(photonRenderingData, radianceCompute, photonRenderingData.IndirectScaleDownResolution.x, photonRenderingData.IndirectScaleDownResolution.y, kernelLocalSDFDiffuse);
            radianceControl.traceBlendMaskD.SmoothBlendMask(photonRenderingData, radianceControl.radianceResult, 4);
            //CommandBufferHelper.DispatchCompute_RT(cmd, radianceCompute, photonRenderingData.activeRT, kernelLocalSDFDiffuse, 8);
            // === INDIRECT END ===
        }
        const int updateCount = 4;
        int updateOffest;
        public void RadainceFeedback(RadianceControl radianceControl, PhotonRenderingData photonRenderingData)
        {
            updateOffest++;
            updateOffest = updateOffest % 4;
            RTHelper.Instance.UpdateRadiosityAtlas(photonRenderingData.cmd, SurfaceCacheManager.Instance.surfaceCache, updateCount, updateOffest);

        }
        #endregion
    }
    [System.Serializable]
    public class SDFDataInfo
    {
        public int offest;
        public int length;
        public RenderTexture sdfTexture;
        public SDFDataInfo(RenderTexture sdfTexture, int offest, int length)
        {
            this.sdfTexture = sdfTexture;
            this.offest = offest;
            this.length = length;
        }
    }
    [System.Serializable]
    public struct ObjectData
    {
        public int sdfDataOffest;
        public int directLightSurfaceCacheIndex;
        public int3 sdfDataSize;
        public Matrix4x4 localTo01AffineMatrix;
        public Matrix4x4 worldToLocalAffineMatrix;
        public Matrix4x4 localToWorldAffineMatrix;
        public float deltaSize;
        public Vector2 t;
    }
    /*
    [System.Serializable]
    public struct BVHNodeInfo//BVHNode in buffer
    {
        public int leftNodeIndex;
        public int rightNodeIndex;
        public int objectDataIndex;
        public Vector3 minPoint;
        public Vector3 maxPoint;

    }
    */
    [System.Serializable]
    public struct BVHNodeInfo//BVHNode in buffer
    {
        public int node0;
        public int node1;
        public int node2;
        public int node3;
        public int node4;
        public int node5;
        public int node6;
        public int node7;
        public int objectDataIndex;
        public Vector3 minPoint;
        public Vector3 maxPoint;
        public int t;
    }
    [System.Serializable]
    public struct SurfaceCacheInfo
    {
        public int meshCardIndex;
        public int meshCardCount;
    }
    [System.Serializable]
    public struct MeshCardInfo
    {
        public int4 rect;
        public Matrix4x4 sdfToLocalMatrix;
        public Matrix4x4 sdfToProjectionMatrix;
        public float deltaDepth;
    }
}