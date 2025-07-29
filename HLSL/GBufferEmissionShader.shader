Shader"Photon/GBufferEmissionShader"
{
    Properties
    {	_Color ("Main Color", Color) = (1,1,1,1)
	_MainTex ("Base (RGB)", 2D) = "white" {}
        [HDR] _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}
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
sampler2D _EmissionMap;
float4 _EmissionMap_ST;
float4 _EmissionColor;
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
    
    float4 col = float4(tex2D(_EmissionMap, i.uv * _EmissionMap_ST.xy + _EmissionMap_ST.zw).xyz * _EmissionColor.xyz, 1);
    return col;
}
            ENDCG
        }
    }
}
