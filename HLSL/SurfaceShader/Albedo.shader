Shader"Photon/Albedo"
{
    Properties
    {

        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor]   _BaseColor("Color", Color) = (1,1,1,1)

        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "UniversalMaterialType"="Lit" }
        
        Pass
        {
Name"AlbedoPass"
            Tags
{"LightMode"="UniversalForward"
}

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

#include "UnityCG.cginc"

sampler2D _BaseMap;
float4 _BaseColor;
float4 _BaseMap_ST;
float _Cutoff;

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
    half4 baseCol = tex2D(_BaseMap, IN.uv) * _BaseColor;
                
    return baseCol;
}
            ENDHLSL
        }
    }
}
