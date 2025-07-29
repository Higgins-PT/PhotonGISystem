Shader"Photon/Normal"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Scale", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "UniversalMaterialType"="Lit" }

        Pass
        {
Name"NormalPass"
            Tags
{"LightMode"="UniversalForward"
}

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
#include "UnityCG.cginc"

sampler2D _BumpMap;
float _BumpScale;
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
    OUT.uv = IN.uv;
    return OUT;
}

half4 frag(Varyings IN) : SV_Target
{

    half4 normalMapSample = tex2D(_BumpMap, IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw);
    normalMapSample.a = _BumpScale;
    return normalMapSample;
}

            ENDHLSL
        }
    }
}
