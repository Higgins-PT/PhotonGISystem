// LightReporter.cs
using PhotonSystem;
using UnityEngine;

/// <summary>
/// Registration + change-tracking helper for non-directional lights.
/// </summary>
[RequireComponent(typeof(Light))]
[ExecuteInEditMode]
public class LightReporter : MonoBehaviour
{
    // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ Public API ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤

    public enum RefreshMode
    {
        None,           // Never refresh
        Transform,      // Refresh on position / rotation change
        State,          // Refresh on light property change (intensity / color / range ¡­)
        Continuous      // Refresh every frame
    }

    [Tooltip("How should this light trigger a refresh event?")]
    public RefreshMode refreshMode = RefreshMode.State;

    /// <summary>Set to true only for *one* frame when a refresh is required.</summary>
    public bool NeedRefresh { get; private set; }

    // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ Internals ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤

    public Light _light;                        // cached reference

    // Cached transform state
    Vector3 lastPos;
    Quaternion lastRot;

    // Cached light state
    LightSnapshot lastSnap;

    struct LightSnapshot
    {
        public float intensity;
        public Color color;
        public float range;
        public float spotAngleInner;
        public float spotAngle;
        public bool enabled;
        public LightShadows shadows;
        public LightType lightType;
        public Vector2 rect;
        public float colorTemperature;
        public void Capture(Light l)
        {
            intensity = l.intensity;
            color = l.color;
            range = l.range;
            spotAngle = l.spotAngle;
            spotAngleInner = l.innerSpotAngle;
            enabled = l.enabled;
            shadows = l.shadows;
            lightType = l.type;
            rect = l.areaSize;
            colorTemperature = l.colorTemperature;
        }

        public bool Differs(Light l)
        {
            return Mathf.Abs(intensity - l.intensity) > 0.0001f ||
                   color != l.color ||
                   Mathf.Abs(range - l.range) > 0.0001f ||
                   Mathf.Abs(spotAngle - l.spotAngle) > 0.0001f ||
                   enabled != l.enabled ||
                   shadows != l.shadows || lightType != l.type || spotAngleInner != l.innerSpotAngle || rect != l.areaSize ||
                   colorTemperature != l.colorTemperature;
        }
    }

    // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ Unity events ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤

    void Awake()
    {
        _light = GetComponent<Light>();
        CacheTransform();
        lastSnap.Capture(_light);
    }

    void OnEnable()
    {
        if (_light != null)
        {
            RadianceManager.Instance.RegisterLight(this);
        }
    }

    void OnDisable()
    {

        if (_light != null) {

            RadianceManager.Instance.UnregisterLight(this); 
        }
    }


    void Update()
    {
        NeedRefresh = false; // reset each frame

        switch (refreshMode)
        {
            case RefreshMode.None:
                break;

            case RefreshMode.Transform:
                if (CheckTransformChanged())
                {
                    NeedRefresh = true;
                    CacheTransform();
                }
                break;

            case RefreshMode.State:
                if (lastSnap.Differs(_light) || CheckTransformChanged())
                {
                    NeedRefresh = true;
                    lastSnap.Capture(_light);
                    CacheTransform();
                }
                break;

            case RefreshMode.Continuous:
                NeedRefresh = true;
                break;
        }
    }

    // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ Helper methods ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤

    bool CheckTransformChanged() =>
        transform.position != lastPos || transform.rotation != lastRot;

    void CacheTransform()
    {
        lastPos = transform.position;
        lastRot = transform.rotation;
    }

#if UNITY_EDITOR   // keep caches in sync when editing
    void OnValidate()
    {
        CacheTransform();
        if (_light == null) _light = GetComponent<Light>();
        lastSnap.Capture(_light);
    }
#endif
}
