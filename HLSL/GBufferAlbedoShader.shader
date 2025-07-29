Shader"Photon/GBufferAlbedoShader"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BaseMap   ("Base (RGB)", 2D)    = "white" {}
        _Cutoff    ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
LOD 100

        Pass
        {
CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   5.0

#include "UnityCG.cginc"


            CBUFFER_START(UnityPerMaterial)

float4 _BaseColor;
float4 _BaseMap_ST;
float _Cutoff;
            CBUFFER_END

sampler2D _BaseMap;

struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    half4 color : COLOR; 
};


v2f vert(appdata_full v)
{
    v2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = TRANSFORM_TEX(v.texcoord.xy, _BaseMap);
    o.color = v.color;
    return o;
}

float4 frag(v2f i) : SV_Target
{
    float4 baseSample = tex2D(_BaseMap, i.uv);
    float4 albedoRGBA = baseSample * _BaseColor * i.color;

    clip(albedoRGBA.a - _Cutoff);

    return float4(albedoRGBA.rgb, 1.0);
}
            ENDCG
        }
    }
}
