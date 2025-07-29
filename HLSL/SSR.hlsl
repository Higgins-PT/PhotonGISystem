#ifndef SSR
#define SSR
Texture2D<float2> _HZBDepth_SSR;
Texture2D<float4> _HZBColor_SSR;
Texture2D<float4> _ScreenNormal_SSR;
Texture2D<float4> _NoiseTex_SSR;
Texture2D<float4> _MetallicRT_SSR;
float3 _CameraPosition_SSR;
float2 _HZBDepthSize_SSR;
float2 _ScreenSize_SSR;
float2 _NoiseSize_SSR;
float2 _ScaleOtoH_SSR;
float4x4 _ProjectionMatrix_SSR;
float4x4 _ViewMatrix_SSR;
float4x4 _ProjectionMatrixInverse_SSR;
float4x4 _ViewMatrixInverse_SSR;
float _MaxDistance;
float _Stride;
float _JitterStrength;
float _Thickness;
int _MaxStep;
int _MaxMipLevel;
int _DownSample;
uint _HZBMipCount_SSR;
inline float2 GetNoise_SSR(int2 index)
{
    float2 noise = _NoiseTex_SSR[index.xy % int2(_NoiseSize_SSR)].xy;
    
    return noise;
}

inline float2 ScreenToHZBUV(float2 screenUV)
{
    return screenUV * _ScaleOtoH_SSR;

}
inline float3 UVDepthToViewPos(float2 screenUV, float depth)
{
    float2 ndc;
    ndc.x = screenUV.x * 2.0f - 1.0f;
    ndc.y = screenUV.y * 2.0f - 1.0f;
    float fx = _ProjectionMatrix_SSR._11;
    float fy = _ProjectionMatrix_SSR._22;
    float viewX = ndc.x * (depth / fx);
    float viewY = ndc.y * (depth / fy);
    return float3(viewX, viewY, depth);
}

inline float4 ViewPosToHPos(float3 vpos)
{
    vpos.z = -vpos.z;
    float4 clipPos = mul(_ProjectionMatrix_SSR, float4(vpos, 1.0));
    return clipPos;
}
inline float2 HPosToScreenIndex(float4 clipPos)
{
    float2 ndc = clipPos.xy / abs(clipPos.w);

    float2 uv = ndc * 0.5 + 0.5;
    return uv * _ScreenSize_SSR;
}
float3 ReconstructWorldPos(float2 uv, float linearEyeDepth)
{
    float2 p11_22 = float2(_ProjectionMatrix_SSR._11, _ProjectionMatrix_SSR._22);
    float3 vpos = float3((uv * 2 - 1) / p11_22, -1) * linearEyeDepth;
    float4 wposVP = mul(_ViewMatrixInverse_SSR, float4(vpos, 1));
    return wposVP.xyz;
}
float ComputeSphereUVRadius(float3 startWorld, float3 endWorld, float coneAngle)
{
    float dist = length(endWorld - startWorld);
    float sphereR = clamp(tan(coneAngle) * dist, 0, 10000);
    float4 viewPosH = mul(_ViewMatrix_SSR, float4(endWorld, 1.0));
    float viewZ = -viewPosH.z;
    float proj00 = _ProjectionMatrix_SSR._11;
    float proj11 = _ProjectionMatrix_SSR._22;

    float ndcR_x = sphereR * proj00 / viewZ;
    float ndcR_y = sphereR * proj11 / viewZ;
    return max(ndcR_x, ndcR_y) * 0.5;
}



inline float4 GetMipColorFromScreenUV(float2 uv, int mipLevel)//ScreenUV to HZBIndex
{
    return _HZBColor_SSR.Load(int3(ceil(uv * _ScaleOtoH_SSR * _HZBDepthSize_SSR / exp2(mipLevel)), mipLevel));
   
}
inline float4 GetColorWithConeTrace(float2 uv, float coneAngle, float3 startPosWS, float3 endPosWS)
{
    float l = ComputeSphereUVRadius(startPosWS, endPosWS, coneAngle);
    l *= _ScaleOtoH_SSR.x;
    return GetMipColorFromScreenUV(uv, clamp(ceil(log2(l * _HZBDepthSize_SSR.x)), 0, _HZBMipCount_SSR - 1));

}
inline float GetSmoothness(float2 uv)
{
    return _MetallicRT_SSR[uv * _ScreenSize_SSR].w;

}
inline float GetHZBDepthFromScreenUV(float2 uv, int mipLevel)//ScreenUV to HZBIndex
{
    return _HZBDepth_SSR.Load(int3(ceil(uv * _ScaleOtoH_SSR * _HZBDepthSize_SSR / exp2(mipLevel)), mipLevel)).x;

}
inline float2 GetHZBDepthFromScreenUVFar(float2 uv, int mipLevel)//ScreenUV to HZBIndex
{
    return _HZBDepth_SSR.Load(int3(ceil(uv * _ScaleOtoH_SSR * _HZBDepthSize_SSR / exp2(mipLevel)), mipLevel));

}
inline float3 WorldToViewDir(float3 worldDir)
{
    float3 viewDir = mul((float3x3) _ViewMatrix_SSR, worldDir);
    return normalize(viewDir);
}

inline float3 ViewToWorldDir(float3 viewDir)
{
    float3 worldDir = mul((float3x3) _ViewMatrixInverse_SSR, viewDir);
    return normalize(worldDir);
}
inline float3 ScreenToDirection_SSR(float2 uv, float4x4 inverseProjectionMatrix, float4x4 inverseViewMatrix)
{

    float3 ndc;
    ndc.x = (uv.x) * 2.0 - 1.0;
    ndc.y = (uv.y) * 2.0 - 1.0;
    ndc.z = 1.0;

    float4 clipSpacePos = float4(ndc.xy, -1.0, 1.0);


    float4 viewSpacePos = mul(inverseProjectionMatrix, clipSpacePos);
    viewSpacePos /= viewSpacePos.w;
    

    float4 worldSpacePos = mul(inverseViewMatrix, viewSpacePos);


    float3 rayOrigin = mul(inverseViewMatrix, float4(0, 0, 0, 1)).xyz;
    float3 rayDirection = normalize(worldSpacePos.xyz - rayOrigin);
    return rayDirection;
}
inline float3 ScreenToNormal_SSR(float2 uv)
{
    float3 normal = _ScreenNormal_SSR[uv * _ScreenSize_SSR].xyz;
    return normal;
}


float4 SSRCalculate(float2 screenUV, float3 rayDirection, inout float3 hitPos)
{
    float2 hzbUV = ScreenToHZBUV(screenUV);
    int2 index = screenUV * _ScreenSize_SSR;
    int2 hzbIndex = hzbUV * _HZBDepthSize_SSR;
    float linearDepth = _HZBDepth_SSR[hzbIndex].x;
    float3 worldPos = ReconstructWorldPos(screenUV, linearDepth);
    float3 viewPos = mul(_ViewMatrix_SSR, float4(worldPos, 1)).xyz;
    viewPos.z = -viewPos.z;
    float3 normal = _ScreenNormal_SSR[index].xyz;
    float roughness = 1 - GetSmoothness(screenUV);
    float coneAngle = atan(roughness * roughness);
    coneAngle = 0;
    

    float3 endViewPos = mul(_ViewMatrix_SSR, float4(worldPos + _MaxDistance * rayDirection, 1)).xyz;
    

    endViewPos.z = -endViewPos.z;
    if (endViewPos.z < 0)
    {
        float dist = -endViewPos.z + 0.1;
        float3 viewDir = normalize(viewPos - endViewPos);
        dist = dist / viewDir.z;
        endViewPos = endViewPos + viewDir * dist;

    }
    float4 startHPos = ViewPosToHPos(viewPos);
    float4 endHPos = ViewPosToHPos(endViewPos);

    float2 p0 = HPosToScreenIndex(startHPos);
    float2 p1 = HPosToScreenIndex(endHPos);
    
    float k0 = 1 / startHPos.w;
    float k1 = 1 / endHPos.w;
    
    float3 q0 = viewPos * k0;
    float3 q1 = endViewPos * abs(k1);
    float2 delta = p1 - p0;
    bool permute = false;
    if (abs(delta.x) < abs(delta.y))
    {
        permute = true;
        delta = delta.yx;
        p0 = p0.yx;
        p1 = p1.yx;
    }
    float dir = sign(delta.x);
    float idx = dir / delta.x;
    float2 dp = float2(dir, idx * delta.y);
    float3 dq = (q1 - q0) * idx;
    float dk = (k1 - k0) * idx;
    float stride = _Stride * exp2(_DownSample);
    dp *= stride;
    dq *= stride;
    dk *= stride;
    float2 P = p0;
    float3 Q = q0;
    float K = k0;
    float noise = clamp(GetNoise_SSR(index).x, 0, 1);
    float jitter = lerp(1, noise, _JitterStrength);
    P += dp * jitter;
    Q += dq * jitter;
    K += dk * jitter;
    float rayDepthLast = viewPos.z;
    float rayDepthNow = viewPos.z;
    float lastZ = viewPos.z;
    int mipLevel = 0;
    float3 startPosWS = ReconstructWorldPos(screenUV, viewPos.z);
    float thickness;
    float lastDepth = 0;
    for (int i = 0; i < _MaxStep; i++)
    {
        float mipStepSize = exp2(mipLevel);
        P += dp * mipStepSize;
        Q += dq * mipStepSize;
        K += dk * mipStepSize;
        rayDepthLast = lastZ;
        rayDepthNow = (dq.z * mipStepSize * 0.5 + Q.z) / (dk * mipStepSize * 0.5 + K);
        lastZ = rayDepthNow;
        if (rayDepthLast < rayDepthNow)
        {
            float a = rayDepthNow;
            rayDepthNow = rayDepthLast;
            rayDepthLast = a;
        }
        float2 hitUV = permute ? P.yx : P;
        hitUV /= _ScreenSize_SSR.xy;
        if (any(hitUV < 0) || any(hitUV > 1))
        {
            if (mipLevel == 0)
            {
                return float4(0, 0, 0, 0);
            }
            else
            {
                P -= dp * mipStepSize;
                Q -= dq * mipStepSize;
                K -= dk * mipStepSize;
                lastZ = Q.z / K;
                mipLevel--;
                break;
            }
        }
        thickness = _Thickness * log10(lastDepth + 10);
        float hzbDepth = GetHZBDepthFromScreenUV(hitUV, mipLevel) + thickness;

        lastDepth = hzbDepth - thickness;
        bool isBehind = rayDepthLast + 0.01 >= hzbDepth;
        if (!isBehind)
        {
            mipLevel = min(mipLevel + 1, _MaxMipLevel - 1);
        }
        else
        {
            if (mipLevel == 0)
            {
                if (abs(hzbDepth - rayDepthNow) < thickness)
                {
                    hitPos = ReconstructWorldPos(hitUV, rayDepthNow);
                    return GetColorWithConeTrace(hitUV, coneAngle, startPosWS, hitPos);

                }

            }
            else
            {
                mipStepSize = exp2(mipLevel);
                P -= dp * mipStepSize;
                Q -= dq * mipStepSize;
                K -= dk * mipStepSize;
                lastZ = Q.z / K;
                mipLevel--;
            }
        }
    }
    return float4(0, 0, 0, 0);
}
float4 SSRCalculate(float2 screenUV, float3 rayDirection)
{
    float3 hitPos;
    return SSRCalculate(screenUV, rayDirection, hitPos);

}
#endif 