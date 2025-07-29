#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class ReadWriteEnabler : EditorWindow
{
    [MenuItem("Tools/Enable Read/Write On All Models")]
    static void EnableReadWriteOnAllModels()
    {
        // 查找所有模型资源的 GUID
        string[] modelGuids = AssetDatabase.FindAssets("t:ModelImporter", new[] { "Assets" });
        int count = 0;

        foreach (var guid in modelGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                Debug.Log($"✅ 已开启 Model Read/Write: {path}");
                count++;
            }
        }

        if (count == 0)
            Debug.Log("🔍 未发现需要开启 Read/Write 的模型资源。");
        else
            Debug.Log($"🎉 共处理 {count} 个模型资源。");
    }
}

#endif