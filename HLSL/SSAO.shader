Shader"Photon/SSAO"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

#include "UnityCG.cginc"
float3 UVToWorld(float2 uv, float depth, float4x4 inverseView, float4x4 Projection)
{
    float2 p11_22 = float2(Projection._11, Projection._22);
    float3 vpos = float3((uv * 2 - 1) / p11_22, -1) * depth;
    float4 wposVP = mul(inverseView, float4(vpos, 1));
    return wposVP.xyz;
}

inline float3 GenerateRandomPointInSphere(float radius, sampler3D randomTex, float3 uv)
{
    float3 pointE = tex3Dlod(randomTex, float4(uv, 0)).xyz * radius;
    float distance = length(pointE);

    float clampedDistance = clamp(distance, 0.0, radius);
    pointE *= clampedDistance / distance;

    return pointE;
}

float3 GenerateRandomPointInHemisphere(float3 normal, float radius, sampler3D randomTex, float3 uv)
{

    float3 pointE = GenerateRandomPointInSphere(radius, randomTex, uv);
    
    //pointE *= sign(dot(pointE, normal));
    return pointE;
}
struct appdata
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
};

struct v2f
{
    float2 uv : TEXCOORD0;
    float4 vertex : SV_POSITION;
};

sampler2D _MainTex;
float4 _MainTex_ST;

float _SSAOShadowMin;
sampler2D _DepthTex;
sampler2D _NormalTex;
sampler3D _RandomDirTex;
float2 _NoiseScale;

float4x4 _ViewMatrix;
float4x4 _ProjectionMatrix;
float4x4 _InverViewMatrix;
float4x4 _InverProjectionMatrix;
float _SSAORadiusMin;
float _SSAORadiusMax;
float _SSAODistance;

float _SSAOIntensity;
int _SamplePointsCount;
float _SSAOIntensityFactor;

v2f vert(appdata v)
{
    v2f o;
    o.vertex = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv;
    return o;
}
int _Size;
float3 GetNormal(float2 uv)
{
    return tex2D(_NormalTex, uv);

}
float GetDepth(float2 uv)
{
    return tex2D(_DepthTex, uv);

}
float frag(v2f i) : SV_Target
{

    float3 initNormal = normalize(GetNormal(i.uv));
    float initDepth = GetDepth(i.uv);
    float3 initPos = UVToWorld(i.uv, initDepth, _InverViewMatrix, _ProjectionMatrix) ;
    float shadow = 0;

    float depthFactor = clamp(initDepth / _SSAODistance, 0, 1);
    float range = lerp(_SSAORadiusMin, _SSAORadiusMax, depthFactor);
    //initPos += initNormal * 0.01;
    int samplePoints = _SamplePointsCount;
    float deltaShadow = 1 / (float) samplePoints;
    float3 uv = float3(i.uv / _NoiseScale, 0);
    UNITY_LOOP

    for (int j = 0; j < samplePoints; j++)
    {
        uv.z = ((float) j / ((float) samplePoints - 1));
        float3 randomPos = initPos + GenerateRandomPointInHemisphere(initNormal.xyz, range, _RandomDirTex, uv);
        float4 viewPos = mul(_ViewMatrix, float4(randomPos, 1.0));
        float4 clipPos = mul(_ProjectionMatrix, viewPos);
        float2 ndcPos = clipPos.xy / clipPos.w;
        ndcPos.x = (ndcPos.x + 1.0) * 0.5;
        ndcPos.y = (ndcPos.y + 1.0) * 0.5;

        //float3 testNormal = GetNormal(ndcPos);
        float testDepth = GetDepth(ndcPos);
        if (!((testDepth > -viewPos.z) && abs(testDepth - (-viewPos.z)) < range))
        {
            shadow += deltaShadow;

        }
        

    }
    float factor = _SSAOIntensityFactor;
    shadow = clamp(pow(shadow * factor, _SSAOIntensity) / factor + _SSAOShadowMin, 0, 1);
    return shadow;
}
            ENDCG
        }
    }
}