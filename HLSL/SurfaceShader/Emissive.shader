Shader"Photon/Emissive"
{
    Properties
    {   
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0,1)
        _EmissionMap("Emission Map", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "UniversalMaterialType"="Lit" }

        Pass
        {
Name"EmissivePass"
            Tags
{"LightMode"="UniversalForward"
}

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
#include "UnityCG.cginc"

sampler2D _EmissionMap;
float4 _EmissionMap_ST;
sampler2D _BaseMap;
float4 _BaseMap_ST;
float4 _EmissionColor; // HDR color

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
    OUT.uv = IN.uv;
    return OUT;
}

half4 frag(Varyings IN) : SV_Target
{
    half4 emissiveTex = tex2D(_EmissionMap, IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw);
    half4 finalColor = emissiveTex * _EmissionColor;
                // 这里可直接输出发光值
    return finalColor;
}
            ENDHLSL
        }
    }
}
