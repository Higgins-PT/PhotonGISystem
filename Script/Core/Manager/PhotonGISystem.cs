using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace PhotonSystem
{
    public class PhotonGISystem : PhotonSingleton<PhotonGISystem>
    {
        private Camera mainCamera;
        private bool mainCameraIsSet = false;
        public Camera MainCamera
        {
            get
            {
                if (mainCameraIsSet == false)
                {
                    mainCameraIsSet = true;
                    mainCamera = Camera.main;
                    return mainCamera;
                }
                else
                {
                    return mainCamera;
                }

            }
            set
            {
                mainCamera = value;
                mainCameraIsSet = true;
            }
        }

        public void Awake()
        {
            
        }
        public void Update()
        {
            PhotonUpdate();
        }
        public override void PhotonUpdate()
        {
            RadianceManager.Instance.PhotonUpdate();
            BVHManager.Instance.PhotonUpdate();
            LocalSDFManager.Instance.PhotonUpdate();

        }
    }
#if UNITY_EDITOR

    [CustomEditor(typeof(PhotonSystem.PhotonGISystem))]
    public class PhotonGISystemEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // Draw default fields from PhotonGISystem
            DrawDefaultInspector();

            // Check 1: Graphics API = Direct3D12 only
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows);
            bool isD12Only = apis.Length == 1 && apis[0] == GraphicsDeviceType.Direct3D12;

            // Check 2: PhotonRendererFeature present?
            bool hasPhotonFeat = false;
            var urpAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset != null)
            {
                foreach (var rd in urpAsset.rendererDataList)
                {
                    var sod = new SerializedObject(rd);
                    var feats = sod.FindProperty("m_RendererFeatures");
                    for (int i = 0; i < feats.arraySize; i++)
                    {
                        if (feats.GetArrayElementAtIndex(i)
                                 .objectReferenceValue is PhotonRendererFeature)
                        {
                            hasPhotonFeat = true;
                            break;
                        }
                    }
                }
            }

            // Render checks in a boxed area
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Configuration Checks", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Graphics API = Direct3D12 only", isD12Only ? "✔︎ OK" : "✖ Missing");
            EditorGUILayout.LabelField("PhotonRendererFeature present", hasPhotonFeat ? "✔︎ OK" : "✖ Missing");
            EditorGUILayout.EndVertical();

            // Fix button
            if (GUILayout.Button("Fix Issues"))
            {
                // Enforce Direct3D12
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows, false);
                PlayerSettings.SetGraphicsAPIs(
                    BuildTarget.StandaloneWindows,
                    new[] { GraphicsDeviceType.Direct3D12 }
                );

                // Add missing PhotonRendererFeature to each renderer
                if (urpAsset != null)
                {
                    foreach (var rd in urpAsset.rendererDataList)
                    {
                        var sod = new SerializedObject(rd);
                        var feats = sod.FindProperty("m_RendererFeatures");
                        bool found = false;
                        for (int i = 0; i < feats.arraySize; i++)
                        {
                            if (feats.GetArrayElementAtIndex(i)
                                     .objectReferenceValue is PhotonRendererFeature)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            // Create and attach new feature instance
                            var feat = CreateInstance<PhotonRendererFeature>();
                            AssetDatabase.AddObjectToAsset(feat, rd);
                            sod.Update();
                            feats.InsertArrayElementAtIndex(feats.arraySize);
                            feats.GetArrayElementAtIndex(feats.arraySize - 1)
                                 .objectReferenceValue = feat;
                            sod.ApplyModifiedProperties();
                        }
                    }
                    AssetDatabase.SaveAssets();
                    EditorUtility.DisplayDialog(
                        "Photon GISystem",
                        "Direct3D12 and PhotonRendererFeature enforced.",
                        "OK"
                    );
                }
            }
        }
    }
#endif
}