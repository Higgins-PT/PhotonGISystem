using UnityEngine;
namespace PhotonSystem
{
    public class SkyBoxManager : PhotonSingleton<SkyBoxManager>
    {
        public Cubemap cubemap;
        public int cubemapSize = 128;
        public bool updateSkyBox = false;
        private Camera cam;
        private GameObject camGameObject;
        void RenderSkyBoxToCube()
        {
            if(cam == null)
            {
                camGameObject = new GameObject("CubemapCamera");
                cam = camGameObject.AddComponent<Camera>();
                cam.enabled = false;
                camGameObject.hideFlags = HideFlags.HideAndDontSave;
                cam.transform.position = transform.position;
                cam.transform.rotation = Quaternion.identity;
                cam.cullingMask = 0;
            }

            if (cubemap == null)
            {
                cubemap = new Cubemap(cubemapSize, TextureFormat.RGBAFloat, false);
            }

            bool success = cam.RenderToCubemap(cubemap);
            if (!success)
            {
                Debug.LogError("‰÷»æ ß∞‹");
            }

        }
        void Start()
        {
            Invoke(nameof(RenderSkyBoxToCube), 0.1f);
        }

        public override void DestroySystem()
        {
            DestroyImmediate(camGameObject);
        }
        private void Update()
        {
            if (updateSkyBox)
            {
                RenderSkyBoxToCube();
            }
        }
    }
}