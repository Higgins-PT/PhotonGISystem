Shader"Photon/ShadowFullScreen"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
LOD 100

        Pass
        {
Name"ShadowFullScreen"
            Tags
{"LightMode" = "UniversalForward"
}

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
};

struct Varyings
{
    float4 positionHCS : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float4x4 _ProjectionMatrix;
float4x4 _ViewMatrixInverse;
float4 _ZBufferParamsP;
sampler2D _DepthRT;
float3 UVToWorld(float2 uv, float depth)
{
    float2 p11_22 = float2(_ProjectionMatrix._11, _ProjectionMatrix._22);
    float3 vpos = float3((uv * 2 - 1) / p11_22, -1) * depth;
    float4 wposVP = mul(_ViewMatrixInverse, float4(vpos, 1));
    return wposVP.xyz;
}

Varyings vert(Attributes IN)
{
    Varyings OUT;
    OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
    OUT.uv = IN.uv;
    return OUT;
}
inline float LinearEyeDepthD(float z)
{
    return 1.0 / (_ZBufferParamsP.z * z + _ZBufferParamsP.w);
}

half4 frag(Varyings input) : SV_Target
{
    float3 worldPos = UVToWorld(input.uv, LinearEyeDepthD(tex2D(_DepthRT, input.uv).x));
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(worldPos));
    float shadow = mainLight.shadowAttenuation;
    return half4(shadow, 0, 0, 1);
}
            ENDHLSL
        }
    }
}
