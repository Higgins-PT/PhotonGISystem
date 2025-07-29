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
            // ��ȡĿ�����
            PhotonObject photonObject = (PhotonObject)target;

            // ��ʾĬ�ϵ� Inspector ����
            base.OnInspectorGUI();

            // ����Ƿ��� Renderer
            if (photonObject.renderer == null)
            {
                EditorGUILayout.HelpBox("Renderer is not assigned! Please assign a Renderer component.", MessageType.Warning);

                // �ṩһ���Զ����ҵİ�ť
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