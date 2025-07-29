Shader"Photon/WorldProbesShader"
{
    Properties
    {
        _MainTex    ("Texture"      , 2D   ) = "white" {}
        _ProbeRadius("Probe Radius" , Float) = 0.2   
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
LOD 100

        Pass
        {
            HLSLPROGRAM
            // -------------------------------------------------------
            #pragma target 4.5
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing  
            // -------------------------------------------------------
            #include "UnityCG.cginc"


sampler2D _MainTex;
float4 _MainTex_ST;
float _ProbeRadius; // ÓÉ Properties ±©Â¶
struct GlobalRadiacneProbes
{
    float3 c0;
    float3 c1;
    float3 c2;
    float3 c3;
    float3 c4;
    float3 c5;
    float3 c6;
    float3 c7;
    float3 c8;
    float3 worldPos;
    int enable;
};
StructuredBuffer<GlobalRadiacneProbes> _GlobalRadiacneProbes;
struct appdata
{
    float3 vertex : POSITION;
    float2 uv : TEXCOORD0;
};

struct v2f
{
    float2 uv : TEXCOORD0;
    float4 vertex : SV_POSITION;
};

            // --------------------------------- Vertex ---------------------------------
v2f vert(appdata IN, uint instID : SV_InstanceID)
{
    GlobalRadiacneProbes probe = _GlobalRadiacneProbes[instID];

    float3 worldOffset = (probe.enable != 0) ? probe.worldPos : float3(1e6, 1e6, 1e6);
    float3 worldPos = IN.vertex * _ProbeRadius + worldOffset;

    v2f o;
    o.vertex = UnityWorldToClipPos(float4(worldPos, 1.0));
    o.uv = TRANSFORM_TEX(IN.uv, _MainTex);
    return o;
}

            // -------------------------------- Fragment --------------------------------
fixed4 frag(v2f IN) : SV_Target
{
    return float4(1, 1, 1, 1);
}
            ENDHLSL
        }
    }
}
