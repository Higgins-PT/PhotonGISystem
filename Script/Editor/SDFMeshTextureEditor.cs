/*
#if UNITY_EDITOR
using PhotonSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(SDFMeshTexture))]
public class SDFMeshTextureEditor : Editor
{
    private PreviewRenderUtility previewUtility;
    private Material previewMaterial;

    private Vector2 previewAngles = Vector2.zero;

    private Texture previewTextureCache;
    private bool needRefreshPreview = true;

    private Mesh lastMesh;

    // -- ����һ�� Unity ���õ� 1x1x1 Cube Mesh�������ж��壩
    private static Mesh s_CubeMesh;
    private static Mesh GetCubeMesh()
    {
        // �����ʹ�� Unity ���� Primitive���ɳ��ԣ�
        //   s_CubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        // ��ĳЩ�汾/�����¿��ܲ������ڸ���Դ
        // ������һ���򵥼��
        if (s_CubeMesh == null)
        {
            // ����Ҳ���������Դ������Ը���һ�����Ƶ� 1x1x1 ������ Mesh
            s_CubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (s_CubeMesh == null)
            {
                Debug.LogError("Builtin Cube.fbx not found! You may need to supply your own cube mesh.");
            }
        }
        return s_CubeMesh;
    }

    private void OnEnable()
    {
        // ��ʼ�� PreviewRenderUtility
        previewUtility = new PreviewRenderUtility();
        previewUtility.cameraFieldOfView = 60f;

        // ���Լ��������Զ���� RaymarchSDF Shader
        Shader raymarchShader = Shader.Find("PhotonSystem/RaymarchSDF");
        if (raymarchShader == null)
        {
            Debug.LogWarning("RaymarchSDF shader not found. Please check your Shader path.");
            return;
        }

        // �������ʣ�������Ⱦ
        previewMaterial = new Material(raymarchShader);
    }

    private void OnDisable()
    {
        if (previewUtility != null)
        {
            previewUtility.Cleanup();
            previewUtility = null;
        }

        if (previewMaterial != null)
        {
            DestroyImmediate(previewMaterial);
            previewMaterial = null;
        }
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUI.changed)
        {
            needRefreshPreview = true;
        }
    }

    public override bool HasPreviewGUI()
    {
        var sdfMeshTexture = target as SDFMeshTexture;
        // ֻ�е����� mesh �Ҵ��� SDF ����ʱ����ʾԤ��
        return (sdfMeshTexture != null && sdfMeshTexture.Mesh != null && sdfMeshTexture.SDFTexture != null);
    }

    public override GUIContent GetPreviewTitle()
    {
        return new GUIContent("SDF Raymarch Preview");
    }

    public override void OnPreviewGUI(Rect r, GUIStyle background)
    {
        var sdfMeshTexture = target as SDFMeshTexture;
        if (sdfMeshTexture == null || sdfMeshTexture.Mesh == null || sdfMeshTexture.SDFTexture == null)
            return;

        Event e = Event.current;
        if (e.type == EventType.MouseDrag && r.Contains(e.mousePosition))
        {
            // ��ק��ת
            previewAngles.x += e.delta.x * 0.5f;
            previewAngles.y += e.delta.y * 0.5f;
            needRefreshPreview = true;
            e.Use();
        }

        if (sdfMeshTexture.Mesh != lastMesh)
        {
            lastMesh = sdfMeshTexture.Mesh;
            needRefreshPreview = true;
        }

        if (needRefreshPreview)
        {
            RenderPreviewMesh(sdfMeshTexture, r, background);
            needRefreshPreview = false;
        }

        if (previewTextureCache != null)
        {
            GUI.DrawTexture(r, previewTextureCache, ScaleMode.StretchToFill, false);
        }
    }

    /// <summary>
    /// ��Ԥ��������ʹ�� PreviewRenderUtility ��Ⱦһ�������壨Cube����
    /// ��ʹ�� RaymarchSDF Shader �����ӻ� SDF��
    /// </summary>
    private void RenderPreviewMesh(SDFMeshTexture sdfMeshTexture, Rect rect, GUIStyle background)
    {
        if (previewUtility == null || previewMaterial == null)
            return;

        // 1) ��ʼԤ��
        previewUtility.BeginPreview(rect, background);

        // 2) ������� / �ƹ�
        previewUtility.camera.backgroundColor = Color.gray;
        previewUtility.lights[0].intensity = 1.3f;
        previewUtility.lights[0].transform.rotation = Quaternion.Euler(30f, 30f, 0f);
        previewUtility.lights[1].intensity = 1.3f;

        // 3) �����Χ�� => ������λ������Լ���������->SDF ����
        Mesh mesh = sdfMeshTexture.Mesh;
        Bounds bounds = mesh.bounds;
        float maxExtent = bounds.extents.magnitude;
        float distance = maxExtent * 2.5f;

        Quaternion rotation = Quaternion.Euler(previewAngles.y, previewAngles.x, 0f);
        Vector3 camPos = rotation * (Vector3.back * distance);

        // �������
        previewUtility.camera.transform.position = camPos;
        previewUtility.camera.transform.rotation = rotation;
        previewUtility.camera.transform.LookAt(Vector3.zero);
        previewUtility.camera.nearClipPlane = 0.01f;
        previewUtility.camera.farClipPlane = 1000f;

        Vector3 bMin = bounds.min;
        Vector3 bMax = bounds.max;
        Vector3 size = bMax - bMin;
        if (size.x < 1e-6f) size.x = 1e-6f;
        if (size.y < 1e-6f) size.y = 1e-6f;
        if (size.z < 1e-6f) size.z = 1e-6f;

        Matrix4x4 translate = Matrix4x4.Translate(bounds.center);
        Matrix4x4 scale = Matrix4x4.Scale(new Vector3(1f / size.x, 1f / size.y, 1f / size.z));
        Matrix4x4 worldToSDF = scale * translate;

        // 5) ���� Raymarch ���ʲ���
        previewMaterial.SetMatrix("_WorldToSDF", worldToSDF);
        previewMaterial.SetVector("_CameraWorldPos", camPos);
        previewMaterial.SetVector("_LightDir", new Vector4(0.2f, 0.5f, -0.2f, 0).normalized);
        previewMaterial.SetColor("_BaseColor", Color.white);

        // ����������������Ҫ�� _MaxSteps, _Eps, _StepSize ����Ĭ��ֵ
        previewMaterial.SetInt("_MaxSteps", 64);
        previewMaterial.SetFloat("_Eps", 0.01f);
        previewMaterial.SetFloat("_StepSize", 0.01f);

        // ����Ҫ�������ɺõ� SDF (Texture3D) ������ɫ��
        previewMaterial.SetTexture("_SDF", sdfMeshTexture.SDFTexture);

        // 6) ����һ�� cube�����С���ø��� mesh.bounds
        //    �����������Ķ��� bounds.center => TRS(-bounds.center, ...)��
        //    ���� scale = bounds.size => �� 1x1x1 �������Ϊ�� bounds ��ͬ�Ĵ�С
        Matrix4x4 cubeMatrix = Matrix4x4.TRS(-bounds.center, Quaternion.identity, bounds.size);

        Mesh cube = GetCubeMesh();
        if (cube != null)
        {
            // �� Raymarch ������ DrawMesh
            previewUtility.DrawMesh(cube, cubeMatrix, previewMaterial, 0);
        }
        else
        {
            // ����Ҳ��� Cube mesh������ʾһ��
            Debug.LogWarning("Could not get a valid cube mesh to draw!");
        }

        // 7) ִ����Ⱦ
        previewUtility.camera.Render();

        // 8) �õ����
        previewTextureCache = previewUtility.EndPreview();
    }

}
#endif
*/