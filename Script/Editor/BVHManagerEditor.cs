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
            // ����Ĭ�ϵ� Inspector
            DrawDefaultInspector();

            // ��ȡĿ�����
            BVHManager manager = (BVHManager)target;

            // ���һ���ָ���
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("BVH Manager Controls", EditorStyles.boldLabel);

            // ���ð�ť
            if (GUILayout.Button("Reset BVH"))
            {
                manager.ResetBVH();
            }
        }

    }
}
#endif
