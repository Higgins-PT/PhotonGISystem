Shader"Photon/Metallic"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        // Metallic
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic Map", 2D) = "white" {}
    }
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque"
            "UniversalMaterialType" = "Lit"
        }

        Pass
        {
Name"MetallicPass"
            Tags
{"LightMode" = "UniversalForward"
}

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

#include "UnityCG.cginc"

sampler2D _MetallicGlossMap;
float4 _MetallicGlossMap_ST;
float _Metallic;
sampler2D _BaseMap;
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
                
    OUT.uv = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    return OUT;
}

half4 frag(Varyings IN) : SV_Target
{
    half metallicMapVal = tex2D(_MetallicGlossMap, IN.uv).r;
                
    half metallicVal = metallicMapVal * _Metallic;

    return half4(metallicVal, metallicVal, metallicVal, 1.0);
}
            ENDHLSL
        }
    }
}
