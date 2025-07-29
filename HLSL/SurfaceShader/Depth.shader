Shader"Photon/Depth"
{
    Properties
    {

    }
    SubShader
    {
        Tags { "RenderType"="Opaque"}

        Pass
        {
Name"DepthPass"


            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
#include "UnityCG.cginc"
struct Attributes
{
    float4 positionOS : POSITION;
};

struct Varyings
{
    float4 positionHCS : SV_POSITION;
    float4 worldPos : TEXCOORD0;
};
    float4x4 _ProjectionMatrix;
    Varyings vert(Attributes IN)
    {
        Varyings OUT;
        OUT.positionHCS = UnityObjectToClipPos(IN.positionOS);
        OUT.worldPos = mul(unity_ObjectToWorld, IN.positionOS);
        return OUT;
    }


    half4 frag(Varyings IN) : SV_Target
    {
        float4 clipPos = mul(_ProjectionMatrix, IN.worldPos);
        float depth01 = (clipPos.z / clipPos.w) * 0.5 + 0.5;
        return half4(depth01, depth01, depth01, 1);
    }
            ENDHLSL
}
    }
}
