Shader"Photon/GBufferMetallicShader"
{
    Properties
    {	_Color ("Main Color", Color) = (1,1,1,1)
	    _MainTex ("Base (RGB)", 2D) = "white" {}

        _WorkflowMode("WorkflowMode", Float) = 1.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5

        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}

        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        _SpecGlossMap("Specular", 2D) = "white" {}
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
			#pragma target 5.0
#include "UnityCG.cginc"
CBUFFER_START(UnityPerMaterial)

float _WorkflowMode;

float _Metallic;
float4 _MetallicGlossMap_ST;
float _Smoothness;

float4 _SpecColor;
float4 _SpecGlossMap_ST;;
sampler2D _MetallicGlossMap;
sampler2D _SpecGlossMap;
CBUFFER_END
sampler2D _MainTex;
float4 _MainTex_ST;
struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 normal : TEXCOORD1;
    half4 color : COLOR;
};
			
			
v2f vert(appdata_full v)
{
    v2f o;
				
    o.pos = UnityObjectToClipPos(v.vertex);
				
    float3 pos = o.pos;
				
    o.pos.xy = (o.pos.xy);
				
				
    o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
    o.normal = UnityObjectToWorldNormal(v.normal);
				
    o.color = v.color;
				
    return o;
}
			
float4 frag(v2f i) : SV_Target
{
    float4 metalSample = tex2D(_MetallicGlossMap, i.uv * _MetallicGlossMap_ST.xy + _MetallicGlossMap_ST.zw);
    float smoothMetalA = metalSample.a;

    float4 specSample = tex2D(_SpecGlossMap, i.uv * _SpecGlossMap_ST.xy + _SpecGlossMap_ST.zw);
    float3 specColorTex = specSample.rgb;
    float smoothSpecA = specSample.a;
    float smoothness = _Smoothness;
    float3 F0;
    if (_WorkflowMode >= 0.5)
    {
        smoothness = _Smoothness * smoothMetalA;
        F0 = metalSample.xyz * _Metallic;
    }
    else
    {
        smoothness = _Smoothness * smoothSpecA;
        F0 = specColorTex * _SpecColor.rgb;
    }



    return float4(F0, smoothness);
}
            ENDCG
        }
    }
}
