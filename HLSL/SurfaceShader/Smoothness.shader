Shader"Photon/Smoothness"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0
        _SmoothnessMap("Smoothness Map", 2D) = "white" {}
        _SmoothnessMap_ST("Smoothness Map ST", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "UniversalMaterialType"="Lit"
        }

        Pass
        {
Name"SmoothnessPass"
            Tags
{"LightMode"="UniversalForward"
}

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

#include "UnityCG.cginc"

sampler2D _SmoothnessMap;
float4 _SmoothnessMap_ST;
float _Smoothness;
float _SmoothnessTextureChannel;
float4 _BaseMap_ST;
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

Varyings vert(Attributes IN)
{
    Varyings OUT;
    OUT.positionHCS = UnityObjectToClipPos(IN.positionOS);
                
                // Tiling / Offset
    OUT.uv = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    return OUT;
}

half4 frag(Varyings IN) : SV_Target
{
    half4 smVal = tex2D(_SmoothnessMap, IN.uv);
    half smoothnessFromMap;
    if (_SmoothnessTextureChannel < 0.5)
        smoothnessFromMap = smVal.a;
    else
        smoothnessFromMap = smVal.r;

    half finalSmoothness = smoothnessFromMap * _Smoothness;

    return half4(finalSmoothness, finalSmoothness, finalSmoothness, 1.0);
}
            ENDHLSL
        }
    }
}
