#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
namespace PhotonSystem
{
    [CustomEditor(typeof(BVHManager))]
    public class BVHManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // 绘制默认的 Inspector
            DrawDefaultInspector();

            // 获取目标对象
            BVHManager manager = (BVHManager)target;

            // 添加一个分隔线
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("BVH Manager Controls", EditorStyles.boldLabel);

            // 重置按钮
            if (GUILayout.Button("Reset BVH"))
            {
                manager.ResetBVH();
            }
        }

    }
}
#endif
