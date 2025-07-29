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

    // -- 缓存一个 Unity 内置的 1x1x1 Cube Mesh（或自行定义）
    private static Mesh s_CubeMesh;
    private static Mesh GetCubeMesh()
    {
        // 如果想使用 Unity 内置 Primitive，可尝试：
        //   s_CubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        // 但某些版本/管线下可能并不存在该资源
        // 这里做一个简单检查
        if (s_CubeMesh == null)
        {
            // 如果找不到内置资源，你可以改用一个自制的 1x1x1 立方体 Mesh
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
        // 初始化 PreviewRenderUtility
        previewUtility = new PreviewRenderUtility();
        previewUtility.cameraFieldOfView = 60f;

        // 尝试加载我们自定义的 RaymarchSDF Shader
        Shader raymarchShader = Shader.Find("PhotonSystem/RaymarchSDF");
        if (raymarchShader == null)
        {
            Debug.LogWarning("RaymarchSDF shader not found. Please check your Shader path.");
            return;
        }

        // 创建材质，用于渲染
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
        // 只有当存在 mesh 且存在 SDF 纹理时才显示预览
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
            // 拖拽旋转
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
    /// 在预览窗口中使用 PreviewRenderUtility 渲染一个立方体（Cube），
    /// 并使用 RaymarchSDF Shader 来可视化 SDF。
    /// </summary>
    private void RenderPreviewMesh(SDFMeshTexture sdfMeshTexture, Rect rect, GUIStyle background)
    {
        if (previewUtility == null || previewMaterial == null)
            return;

        // 1) 开始预览
        previewUtility.BeginPreview(rect, background);

        // 2) 相机背景 / 灯光
        previewUtility.camera.backgroundColor = Color.gray;
        previewUtility.lights[0].intensity = 1.3f;
        previewUtility.lights[0].transform.rotation = Quaternion.Euler(30f, 30f, 0f);
        previewUtility.lights[1].intensity = 1.3f;

        // 3) 计算包围盒 => 用来定位相机、以及构建世界->SDF 矩阵
        Mesh mesh = sdfMeshTexture.Mesh;
        Bounds bounds = mesh.bounds;
        float maxExtent = bounds.extents.magnitude;
        float distance = maxExtent * 2.5f;

        Quaternion rotation = Quaternion.Euler(previewAngles.y, previewAngles.x, 0f);
        Vector3 camPos = rotation * (Vector3.back * distance);

        // 设置相机
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

        // 5) 设置 Raymarch 材质参数
        previewMaterial.SetMatrix("_WorldToSDF", worldToSDF);
        previewMaterial.SetVector("_CameraWorldPos", camPos);
        previewMaterial.SetVector("_LightDir", new Vector4(0.2f, 0.5f, -0.2f, 0).normalized);
        previewMaterial.SetColor("_BaseColor", Color.white);

        // 你可以在这里根据需要给 _MaxSteps, _Eps, _StepSize 设置默认值
        previewMaterial.SetInt("_MaxSteps", 64);
        previewMaterial.SetFloat("_Eps", 0.01f);
        previewMaterial.SetFloat("_StepSize", 0.01f);

        // 最重要：把生成好的 SDF (Texture3D) 传给着色器
        previewMaterial.SetTexture("_SDF", sdfMeshTexture.SDFTexture);

        // 6) 绘制一个 cube，其大小正好覆盖 mesh.bounds
        //    先让它的中心对齐 bounds.center => TRS(-bounds.center, ...)，
        //    再让 scale = bounds.size => 让 1x1x1 立方体变为与 bounds 等同的大小
        Matrix4x4 cubeMatrix = Matrix4x4.TRS(-bounds.center, Quaternion.identity, bounds.size);

        Mesh cube = GetCubeMesh();
        if (cube != null)
        {
            // 用 Raymarch 材质来 DrawMesh
            previewUtility.DrawMesh(cube, cubeMatrix, previewMaterial, 0);
        }
        else
        {
            // 如果找不到 Cube mesh，就提示一下
            Debug.LogWarning("Could not get a valid cube mesh to draw!");
        }

        // 7) 执行渲染
        previewUtility.camera.Render();

        // 8) 拿到结果
        previewTextureCache = previewUtility.EndPreview();
    }

}
#endif
*/