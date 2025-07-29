// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader"PhotonSystem/RaymarchSDF"
{
    Properties
    {

    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
ZWrite Off

Blend SrcAlpha
OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
#include "UnityCG.cginc"
sampler3D _SDF; 
float4x4 _WorldToSDF; 
float4 _CameraWorldPos;
float4 _LightDir;
float4 _BaseColor; 
int _MaxSteps;
float _Eps; 
float _StepSize; 

struct appdata
{
    float4 vertex : POSITION;
};


struct v2f
{
    float4 pos : SV_POSITION;
    float3 worldPos : TEXCOORD1;
    float3 rayOrigin : TEXCOORD0; 
};


v2f vert(appdata v)
{
    v2f o;

    float4 worldPos4 = mul(unity_ObjectToWorld, v.vertex);
    float3 worldPos = worldPos4.xyz;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.worldPos = worldPos;
    o.rayOrigin = worldPos;
    return o;
}

float3 GetSDFGradient(float3 sdfUVW, float e, float lodLevel)
{
    float dx1 = tex3Dlod(_SDF, float4(sdfUVW + float3(e, 0, 0), lodLevel)).r;
    float dx2 = tex3Dlod(_SDF, float4(sdfUVW - float3(e, 0, 0), lodLevel)).r;
    float dy1 = tex3Dlod(_SDF, float4(sdfUVW + float3(0, e, 0), lodLevel)).r;
    float dy2 = tex3Dlod(_SDF, float4(sdfUVW - float3(0, e, 0), lodLevel)).r;
    float dz1 = tex3Dlod(_SDF, float4(sdfUVW + float3(0, 0, e), lodLevel)).r;
    float dz2 = tex3Dlod(_SDF, float4(sdfUVW - float3(0, 0, e), lodLevel)).r;

    return float3(dx1 - dx2, dy1 - dy2, dz1 - dz2);
}


bool InRange01(float3 uvw)
{
    return all(uvw >= 0) && all(uvw <= 1);
}

float4 frag(v2f i) : SV_Target
{
    float3 rayPos = i.rayOrigin;
    float3 camPos = _CameraWorldPos.xyz;
    float3 dir = normalize(i.worldPos - camPos);
    float3 rayDir = dir;
    rayPos += rayDir * 0.01;
    float4 finalColor = float4(0, 0, 0, 0);
    /*
    float3 sdfPos = mul(_WorldToSDF, float4(rayPos, 1.0)).xyz + float3(0.5, 0.5, 0.5);
    return float4(tex3D(_SDF, sdfPos).r, 0, 0, 1);*/
    UNITY_LOOP
    for (int step = 0; step < _MaxSteps; step++)
    {
        float3 sdfPos = mul(_WorldToSDF, float4(rayPos, 1.0)).xyz + float3(0.5, 0.5, 0.5);

        if (!InRange01(sdfPos))
            break;

        float dist = tex3Dlod(_SDF, float4(sdfPos, 0)).r;

        if (dist < 0)
        {
            float3 grad = GetSDFGradient(sdfPos, _Eps * 2.0, 0);
            float3 normal = normalize(grad);


            float3 L = normalize(_LightDir.xyz);
            float NdotL = saturate(dot(normal, L) * 0.5 + 0.5);
                        
            float3 baseRGB = _BaseColor.rgb;
            float3 shaded = baseRGB * NdotL;

            finalColor = float4(shaded, 1);
            break;
        }

        float stepLength = max(dist, _StepSize);
        rayPos += rayDir * stepLength;
    }
    return finalColor;
}
            ENDCG
        }
    }

FallBack Off
}
