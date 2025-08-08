using UnityEngine;
namespace PhotonSystem
{
    public class OfflineManager : PhotonSingleton<OfflineManager>
    {
        [Range(1, 1000)]
        public int spp = 1;
        [Range(1, 10)]
        public int bounds = 2;
        public bool startToRendering = false;
    }
    public partial class RadianceControl
    {
        public void ExecuteOfflineRenderer(PhotonRenderingData photonRenderingData)
        {

            InitBuffer(photonRenderingData);
            HandleLightData(photonRenderingData);


            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => photonRenderingData.ssgiData = HZBManager.Instance.GenerateHZB(photonRenderingData, null),
                "GenerateHZB");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => GenerateShadowRT(photonRenderingData),
                "GenerateShadowRT");

            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => SampleGlobalProbes(photonRenderingData),
                "SampleGlobalProbes");


            ExecutePiplineFeatureWithProfile(
                photonRenderingData,
                () => SpawnRadianceCascadesDirection(photonRenderingData),
                "SpawnRadianceCascadesDirection");
            if (!DebugManager.Instance.stopCullLight)
            {
                ExecutePiplineFeatureWithProfile(photonRenderingData,
                    () => CullLights(photonRenderingData),
                    "CullLights");
            }
            if (!OfflineManager.Instance.startToRendering)
            {
                ExecutePiplineFeatureWithProfile(photonRenderingData,
                    () => ForeachRadianceFeature(rf => rf.GetRadianceSample(this, photonRenderingData)),
                    "ForeachRadianceFeature_GetRadianceSample");

                ExecutePiplineFeatureWithProfile(
                    photonRenderingData,
                    () => TemporalReuse(photonRenderingData),
                    "TemporalReuse");

                ExecutePiplineFeatureWithProfile(
                    photonRenderingData,
                    () => RadianceFeedback(photonRenderingData),
                    "RadianceFeedback");

                ExecutePiplineFeatureWithProfile(
                    photonRenderingData,
                    () => SpatialReuse(photonRenderingData),
                    "SpatialReuse");

                ExecutePiplineFeatureWithProfile(
                    photonRenderingData,
                    () => HandleReSTIROutPutRT(photonRenderingData),
                    "HandleReSTIROutPutRT");

                ExecutePiplineFeatureWithProfile(
                    photonRenderingData,
                    () => DLTemporalReuse(photonRenderingData),
                    "DLTemporalReuse");

                ExecutePiplineFeatureWithProfile(
                    photonRenderingData,
                    () => DLSpatialReuse(photonRenderingData),
                    "DLSpatialReuse");


                ExecutePiplineFeatureWithProfile(
                    photonRenderingData,
                    () => Filter(photonRenderingData),
                    "Filter");



                ExecutePiplineFeatureWithProfile(
                    photonRenderingData,
                    () => Specular(photonRenderingData),
                    "Specular");

                ExecutePiplineFeatureWithProfile(
                    photonRenderingData,
                    () => DirectLight(photonRenderingData),
                    "DirectLight");


                ExecutePiplineFeatureWithProfile(
                    photonRenderingData,
                    () => RadianceApply(photonRenderingData),
                    "RadianceApply");

                ExecutePiplineFeatureWithProfile(
                    photonRenderingData,
                    () => ForeachRadianceFeature(
                            rf => rf.RadainceFeedback(this, photonRenderingData)),
                    "ForeachRadianceFeature_RadainceFeedback");

            }
            else
            {

            }


        }
    }
}