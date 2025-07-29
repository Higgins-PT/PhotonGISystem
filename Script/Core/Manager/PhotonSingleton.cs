#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
namespace PhotonSystem
{

    [ExecuteInEditMode]
    public class PhotonSingleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T _instance;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    // 查找实例，忽略 Prefab 物体
                    var allInstances = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (var instance in allInstances)
                    {
                        if (!IsPrefab(instance))
                        {
                            _instance = instance;
                            break;
                        }
                    }

                    // 如果没有找到实例，则创建一个新的
                    if (_instance == null)
                    {
#if UNITY_EDITOR
#else
                    // 在运行模式下创建新的实例
                    GameObject singletonObject = new GameObject(typeof(T).Name);
                    _instance = singletonObject.AddComponent<T>();
#endif
                    }
                }
                return _instance;
            }
        }

        protected PhotonSingleton() { }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                if (!IsPrefab(this))
                {
                    DestroyImmediate(gameObject);
                }
            }
            else
            {
                _instance = this as T;
            }
        }
        public virtual void PhotonUpdate()
        {

        }
        /// <summary>
        /// 检查对象是否是 Prefab（在编辑器模式下存在的 Prefab 实例）。
        /// </summary>
        private static bool IsPrefab(Object obj)
        {
#if UNITY_EDITOR
            return UnityEditor.PrefabUtility.IsPartOfPrefabAsset(obj);
#else
        return false;
#endif
        }
        public virtual void ResetSystem()
        {

        }

        public virtual void ReleaseSystem()
        {

        }
        public virtual void DestroySystem()
        {

        }
        public void Update()
        {

        }
        public void OnDestroy()
        {
            ReleaseSystem();
            DestroySystem();

        }
        void OnEnable()
        {
#if UNITY_EDITOR
            SceneVisibilityManager.instance.DisablePicking(gameObject, true);
#endif
        }
        public void OnDisable()
        {
#if UNITY_EDITOR
            SceneVisibilityManager.instance.EnablePicking(gameObject, true);
#endif
            ReleaseSystem();
        }
    }
}