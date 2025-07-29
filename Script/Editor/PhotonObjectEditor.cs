/*
#if UNITY_EDITOR
using PhotonSystem;
using UnityEditor;
using UnityEngine;
namespace PhotonSystem
{
    [CustomEditor(typeof(PhotonObject))]
    public class PhotonObjectEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // 获取目标对象
            PhotonObject photonObject = (PhotonObject)target;

            // 显示默认的 Inspector 属性
            base.OnInspectorGUI();

            // 检查是否有 Renderer
            if (photonObject.renderer == null)
            {
                EditorGUILayout.HelpBox("Renderer is not assigned! Please assign a Renderer component.", MessageType.Warning);

                // 提供一个自动查找的按钮
                if (GUILayout.Button("Auto-Assign Renderer"))
                {
                    photonObject.renderer = photonObject.GetComponent<Renderer>();

                    if (photonObject.renderer == null)
                    {
                        Debug.LogWarning("No Renderer found on this GameObject.");
                    }
                    else
                    {
                        Debug.Log("Renderer successfully assigned.");
                    }
                }
            }
        }
    }
}
#endif
*/