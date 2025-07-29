#ifndef DIRECTLIGHT
#define DIRECTLIGHT

struct LightBufferData
{
    float3 positionWS;
    float invRange;
    float range;
    float3 directionWS;
    float spotCosOuter;
    float3 radiance;
    float3 rightWS;
    uint flags;
    float spotCosInner;
    float2 areaSize;
    float pad;
};
Texture2D<float2> _HZBDepth;
float2 _HZBDepthSize;
uint _HZBMipCount;
RWStructuredBuffer<LightBufferData> _OriginLightsBuffer;
RWStructuredBuffer<LightBufferData> _LightsBuffer;
RWStructuredBuffer<uint> _VisibleLightCounter;
StructuredBuffer<uint> _SceneLightCounter; //feedback
float3 _CameraPosition_DL;
float3 _CameraDirection_DL;
float4x4 _ProjectionMatrix_DL;
float4x4 _ViewMatrix_DL;
float4x4 _ProjectionMatrixInverse_DL;
float4x4 _ViewMatrixInverse_DL;
uint _SceneLightCount;
float4 _ZBufferParamsL;
float3 _CamRightWS;
float3 _CamUpWS;
float3 _CamForwardWS;
float2 _ScaleOtoH;
uint SelectMip(float radiusUV,
               uint2 hzbSize,
               uint mipCount)
{
    float rPix0 = radiusUV * (float) hzbSize.x;
    uint mip = (uint) ceil(log2(max(rPix0, 1.0)));
    return clamp(mip, 0, mipCount - 1);
}


uint2 CalcTexel(float2 uvCenter,
                uint mip,
                uint2 hzbSize)
{
    float scale = (float) (1 << (int)mip);
    float2 texelF = uvCenter * float2(hzbSize) / float2(scale, scale);
    uint2 texel = uint2(round(texelF));

    uint2 dim = uint2((float) hzbSize.x / (float) scale, (float) hzbSize.y / (float) scale);
    texel.x = clamp(texel.x, 0, dim.x - 1);
    texel.y = clamp(texel.y, 0, dim.y - 1);
    return texel;
}

void GetLightTexelAndMip(
    float3 lightPosWS,
    float range,
    out int2 texel,
    out int mipLevel)
{
    float4 clipCenter = mul(_ProjectionMatrix_DL, mul(_ViewMatrix_DL, float4(lightPosWS, 1.0)));
    float2 uvCenter = (clipCenter.xy / clipCenter.w) * 0.5 + 0.5;

    float maxRadUV = 0.0;

    [unroll]
    for (int i = 0; i < 3; ++i)
    {
        float3 dirWS = (i == 0) ? _CamRightWS :
                       (i == 1) ? _CamUpWS :
                                  _CamForwardWS;

        float3 edgePosWS = lightPosWS + dirWS * range;
        float4 clipEdge = mul(_ProjectionMatrix_DL, mul(_ViewMatrix_DL, float4(edgePosWS, 1.0)));
        float2 uvEdge = (clipEdge.xy / clipEdge.w) * 0.5 + 0.5;

        maxRadUV = max(maxRadUV, length(uvEdge - uvCenter));
    }
    uvCenter *= _ScaleOtoH;
    maxRadUV *= _ScaleOtoH.x;
    mipLevel = (int)SelectMip(maxRadUV, _HZBDepthSize, _HZBMipCount);
    texel = int2(CalcTexel(uvCenter, (uint) mipLevel, _HZBDepthSize));
}


inline bool IsSphereBehindCameraWS(float3 centerWS, float radius)
{
    float dist = dot(centerWS - _CameraPosition_DL, _CameraDirection_DL);
    return (dist + radius) < 0.0;
}
uint GetSceneLightCount()
{
    return _SceneLightCounter[0];

}
bool IsSphereInsideFrustumWS(float3 centerWS, float radius)
{
    float4x4 viewMatrix = _ViewMatrix_DL;
    float4x4 projMatrix = _ProjectionMatrix_DL;
    float4x4 VP = mul(projMatrix, viewMatrix);
    float4 row0 = VP[0];
    float4 row1 = VP[1];
    float4 row2 = VP[2];
    float4 row3 = VP[3];

    float4 planes[6];
    planes[0] = row3 + row0;
    planes[1] = row3 - row0;
    planes[2] = row3 + row1;
    planes[3] = row3 - row1;
    planes[4] = row3 + row2;
    planes[5] = row3 - row2;
    [unroll]
    for (uint i = 0; i < 6; ++i)
    {
        float3 n = planes[i].xyz;
        float d = planes[i].w;
        float len = length(n) + 1e-6;
        float dist = (dot(n, centerWS) + d) / len;

        if (dist < -radius)
            return false;
    }

    return true;
}
inline float LinearEyeDepthL(float z)
{
    float nearClip = _ZBufferParamsL.x;
    float farClip = _ZBufferParamsL.y;
    
    return nearClip * farClip / (nearClip + (farClip - nearClip) * z);
}

[numthreads(64, 1, 1)]
void CullLights(uint id : SV_DispatchThreadID)
{
    if (id.x >= _SceneLightCount)
        return;
    LightBufferData L = _OriginLightsBuffer[id];
    float4 posVS = mul(_ViewMatrix_DL, float4(L.positionWS, 1));
    float3 viewPos = posVS.xyz;
    float z = viewPos.z;

    if (IsSphereBehindCameraWS(L.positionWS, L.range))
        return;

    if (!IsSphereInsideFrustumWS(L.positionWS, L.range))
        return;
    int2 texel = int2(0, 0);
    int mipLevel = 0;
    GetLightTexelAndMip(L.positionWS, L.range, texel, mipLevel);
    float3 LDir = _CameraPosition_DL - L.positionWS;
    if (dot(-normalize(LDir), _CameraDirection_DL) > 0)
    {
        float maxDepth = _HZBDepth.Load(int3(texel, mipLevel)).y;
        maxDepth = max(maxDepth, _HZBDepth.Load(int3(clamp(texel + int2(1, 1), 0, _HZBDepthSize - 1), mipLevel)).y);
        maxDepth = max(maxDepth, _HZBDepth.Load(int3(clamp(texel + int2(0, 1), 0, _HZBDepthSize - 1), mipLevel)).y);
        maxDepth = max(maxDepth, _HZBDepth.Load(int3(clamp(texel + int2(-1, 1), 0, _HZBDepthSize - 1), mipLevel)).y);
        maxDepth = max(maxDepth, _HZBDepth.Load(int3(clamp(texel + int2(1, 0), 0, _HZBDepthSize - 1), mipLevel)).y);
        maxDepth = max(maxDepth, _HZBDepth.Load(int3(clamp(texel + int2(-1, 0), 0, _HZBDepthSize - 1), mipLevel)).y);
        maxDepth = max(maxDepth, _HZBDepth.Load(int3(clamp(texel + int2(1, -1), 0, _HZBDepthSize - 1), mipLevel)).y);
        maxDepth = max(maxDepth, _HZBDepth.Load(int3(clamp(texel + int2(0, -1), 0, _HZBDepthSize - 1), mipLevel)).y);
        maxDepth = max(maxDepth, _HZBDepth.Load(int3(clamp(texel + int2(-1, -1), 0, _HZBDepthSize - 1), mipLevel)).y);
        float lD = length(LDir);
        if (L.range + (maxDepth - lD) < 0)
        {
            return;
        }
    }

    uint dstIdx = _VisibleLightCounter.IncrementCounter();
    _LightsBuffer[dstIdx] = L;
    
}
#endif 