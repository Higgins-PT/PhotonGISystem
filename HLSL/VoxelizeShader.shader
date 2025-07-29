/*
MIT License

Copyright (c) 2017 sonicether

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE. 
*/

Shader"Hidden/SEGIVoxelizeScene" {
	Properties
	{
		_BaseColor ("Main Color", Color) = (1,1,1,1)
		_BaseMap ("Base (RGB)", 2D) = "white" {}
		_EmissionColor("Color", Color) = (0,0,0)
		_EmissionMap("Emission", 2D) = "white" {}
		_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.333
		_BlockerValue ("Blocker Value", Range(0, 10)) = 0
	}
	SubShader 
	{
Cull Off

ZTest Always
		
		Pass
		{
			CGPROGRAM
			
				#pragma target 5.0
				#pragma vertex vert
				#pragma fragment frag
				#pragma geometry geom
#include "UnityCG.cginc"
RWTexture3D<uint> _NormalFront;
RWTexture3D<uint> _NormalBack;
RWTexture3D<uint> _Normal;
float4x4 SEGIVoxelViewFront;
float4x4 SEGIVoxelViewLeft;
float4x4 SEGIVoxelViewTop;
				
sampler2D _BaseMap;
sampler2D _EmissionMap;
float _Cutoff;
float4 _BaseMap_ST;
half4 _EmissionColor;
half4 _BaseColor;

float SEGISecondaryBounceGain;
				
float _BlockerValue;
				
int SEGIVoxelResolution;
				
float4x4 SEGIVoxelToGIProjection;
float4x4 SEGIVoxelProjectionInverse;
sampler2D SEGISunDepth; 
float4 SEGISunlightVector;
float4 GISunColor;
float4 SEGIVoxelSpaceOriginDelta;
float DepthDelta;
float4 LevelColor;

sampler3D SEGIVolumeTexture1;

int SEGIInnerOcclusionLayers;


#define VoxelResolution (SEGIVoxelResolution)



struct v2g
{
    float4 pos : SV_POSITION;
    half4 uv : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float angle : TEXCOORD2;
};
				
struct g2f
{
    float4 pos : SV_POSITION;
    half4 uv : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float angle : TEXCOORD2;
};
				
		
v2g vert(appdata_full v)
{
    v2g o;
					
    float4 vertex = v.vertex;
					
    o.normal = UnityObjectToWorldNormal(v.normal);
    float3 absNormal = abs(o.normal);
					
    o.pos = vertex;
					
    o.uv = float4(TRANSFORM_TEX(v.texcoord.xy, _BaseMap), 1.0, 1.0);
					
					
    return o;
}
				

[maxvertexcount(3)]
				void geom(triangle v2g input[3], inout TriangleStream<g2f> triStream)
{
    v2g p[3];
    for (int i = 0; i < 3; i++)
    {
        p[i] = input[i];
        p[i].pos = mul(unity_ObjectToWorld, p[i].pos);
    }
					

    float3 realNormal = float3(0.0, 0.0, 0.0);
					
    float3 V = p[1].pos.xyz - p[0].pos.xyz;
    float3 W = p[2].pos.xyz - p[0].pos.xyz;
					
    realNormal.x = (V.y * W.z) - (V.z * W.y);
    realNormal.y = (V.z * W.x) - (V.x * W.z);
    realNormal.z = (V.x * W.y) - (V.y * W.x);
					
    float3 absNormal = abs(realNormal);
					

					
    int angle = 0;
    if (absNormal.z > absNormal.y && absNormal.z > absNormal.x)
    {
        angle = 0;
    }
    else if (absNormal.x > absNormal.y && absNormal.x > absNormal.z)
    {
        angle = 1;
    }
    else if (absNormal.y > absNormal.x && absNormal.y > absNormal.z)
    {
        angle = 2;
    }
    else
    {
        angle = 0;
    }
					
    for (int i = 0; i < 3; i++)
    {
						///*
        if (angle == 0)
        {
            p[i].pos = mul(SEGIVoxelViewFront, p[i].pos);
        }
        else if (angle == 1)
        {
            p[i].pos = mul(SEGIVoxelViewLeft, p[i].pos);
        }
        else
        {
            p[i].pos = mul(SEGIVoxelViewTop, p[i].pos);
        }
						
        p[i].pos = mul(UNITY_MATRIX_P, p[i].pos);
						
#if defined(UNITY_REVERSED_Z)
						p[i].pos.z = 1.0 - p[i].pos.z;	
#else 
        p[i].pos.z *= -1.0;
#endif
						
        p[i].angle = (float) angle;
    }
					
    triStream.Append(p[0]);
    triStream.Append(p[1]);
    triStream.Append(p[2]);
}

float3 rgb2hsv(float3 c)
{
    float4 k = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, k.wz), float4(c.gb, k.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;

    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 hsv2rgb(float3 c)
{
    float4 k = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + k.xyz) * 6.0 - k.www);
    return c.z * lerp(k.xxx, saturate(p - k.xxx), c.y);
}

float4 DecodeRGBAuint(uint value)
{
    uint ai = value & 0x0000007F;
    uint vi = (value / 0x00000080) & 0x000007FF;
    uint si = (value / 0x00040000) & 0x0000007F;
    uint hi = value / 0x02000000;

    float h = float(hi) / 127.0;
    float s = float(si) / 127.0;
    float v = (float(vi) / 2047.0) * 10.0;
    float a = ai * 2.0;

    v = pow(v, 3.0);

    float3 color = hsv2rgb(float3(h, s, v));

    return float4(color.rgb, a);
}

uint EncodeRGBAuint(float4 color)
{
					//7[HHHHHHH] 7[SSSSSSS] 11[VVVVVVVVVVV] 7[AAAAAAAA]
    float3 hsv = rgb2hsv(color.rgb);
    hsv.z = pow(hsv.z, 1.0 / 3.0);

    uint result = 0;

    uint a = min(127, uint(color.a / 2.0));
    uint v = min(2047, uint((hsv.z / 10.0) * 2047));
    uint s = uint(hsv.y * 127);
    uint h = uint(hsv.x * 127);

    result += a;
    result += v * 0x00000080; // << 7
    result += s * 0x00040000; // << 18
    result += h * 0x02000000; // << 25

    return result;
}
uint EncodeOct16(float3 n) 
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    float2 oct = (n.z >= 0.0) ? n.xy
                 : (1.0 - abs(float2(n.y, n.x))) * sign(float2(n.x, n.y));
    int2 s = int2(round(saturate(oct) * 32767.0));
    return (uint(s.y & 0xFFFF) << 16) | (uint(s.x) & 0xFFFF);
}
float3 DecodeOct16(uint p)
{
    int2 s = int2(p & 0xFFFF, (p >> 16) & 0xFFFF);
    float2 f = clamp(float2(s) / 32767.0, -1.0, 1.0);
    float3 n = float3(f, 1.0 - abs(f.x) - abs(f.y));
    if (n.z < 0.0)
        n.xy = (1.0 - abs(float2(n.y, n.x))) * sign(float2(n.x, n.y));
    return normalize(n);
}
bool InterlockedInitOrFetchNormal(
    RWTexture3D<uint> destination,
    int3 coord,
    inout float3 normal)
{
    uint encodedNew = EncodeOct16(normal);
    uint compareValue = 0; 
    uint originalVal;

    InterlockedCompareExchange(destination[coord],
                               compareValue,
                               encodedNew,
                               originalVal);

    if (originalVal != 0)
    {
        normal = DecodeOct16(originalVal);
        return false;
    }
    else
    {
        return true;
    }

}
void interlockedAddFloat4(RWTexture3D<uint> destination, int3 coord, float4 value)
{
    uint writeValue = EncodeRGBAuint(value);
    uint compareValue = 0;
    uint originalValue;

					[allow_uav_condition]
    for (int i = 0; i < 1; i++)
    {
        InterlockedCompareExchange(destination[coord], compareValue, writeValue, originalValue);
        if (compareValue == originalValue)
            break;
        compareValue = originalValue;
        float4 originalValueFloats = DecodeRGBAuint(originalValue);
        writeValue = EncodeRGBAuint(originalValueFloats + value);
    }
}

void interlockedAddFloat4b(RWTexture3D<uint> destination, int3 coord, float4 value)
{
    uint writeValue = EncodeRGBAuint(value);
    uint compareValue = 0;
    uint originalValue;

					[allow_uav_condition]
    for (int i = 0; i < 1; i++)
    {
        InterlockedCompareExchange(destination[coord], compareValue, writeValue, originalValue);
        if (compareValue == originalValue)
            break;
        compareValue = originalValue;
        float4 originalValueFloats = DecodeRGBAuint(originalValue);
        writeValue = EncodeRGBAuint(originalValueFloats + value);
    }
}

float3 _Offest;
				
float4 frag(g2f input) : SV_TARGET
{
    int3 coord = int3((int) (input.pos.x), (int) (input.pos.y), (int) (input.pos.z * VoxelResolution));
					
    float3 absNormal = abs(input.normal);
					
    int angle = 0;
					
    angle = (int) input.angle;

    if (angle == 1)
    {
        coord.xyz = coord.zyx;
        coord.z = VoxelResolution - coord.z - 1;
    }
    else if (angle == 2)
    {
        coord.xyz = coord.xzy;
        coord.y = VoxelResolution - coord.y - 1;
    }
					
    float3 fcoord = (float3) (coord.xyz) / VoxelResolution;

    float4 shadowPos = mul(SEGIVoxelProjectionInverse, float4(fcoord * 2.0 - 1.0, 0.0));
    shadowPos = mul(SEGIVoxelToGIProjection, shadowPos);
    shadowPos.xyz = shadowPos.xyz * 0.5 + 0.5;
					
    float sunDepth = tex2Dlod(SEGISunDepth, float4(shadowPos.xy, 0, 0)).x;
#if defined(UNITY_REVERSED_Z)
					sunDepth = 1.0 - sunDepth;
#endif
					
    float sunVisibility = saturate(sign((sunDepth - shadowPos.z + DepthDelta * 2)));


    float sunNdotL = saturate(dot(input.normal, -SEGISunlightVector.xyz));
					
    float4 tex = tex2D(_BaseMap, input.uv.xy);
    float4 emissionTex = tex2D(_EmissionMap, input.uv.xy);
					
    float4 color = _BaseColor;

    if (length(_BaseColor.rgb) < 0.0001)
    {
        color.rgb = float3(1, 1, 1);
    }

					
    //float3 col = sunVisibility.xxx * sunNdotL * color.rgb * tex.rgb * GISunColor.rgb * GISunColor.a + _EmissionColor.rgb * 0.9 * emissionTex.rgb;
    float3 uvw = (float3) _Offest / (float3(VoxelResolution, VoxelResolution, VoxelResolution) * 8);
    float3 col = color.rgb * tex.rgb + _EmissionColor.rgb * 1 * emissionTex.rgb;

    float4 result = float4(col.rgb, 2.0);
    coord = clamp(coord, int3(0, 0, 0), int3(VoxelResolution - 1, VoxelResolution - 1, VoxelResolution - 1));
    coord += (int3) _Offest;
    float3 frontNormal = normalize(input.normal + float3(0.01, 0.01, 0.01));
    bool firstTime = InterlockedInitOrFetchNormal(_Normal, coord, frontNormal);
    
    if (firstTime)
    {
        interlockedAddFloat4(_NormalFront, coord, result);
        interlockedAddFloat4(_NormalBack, coord, result);
    }
    else
    {
        if (dot(frontNormal, input.normal) > 0)
        {
            interlockedAddFloat4(_NormalFront, coord, result);
        
        }
        else
        {
            interlockedAddFloat4(_NormalBack, coord, result);
        }
    }


					
    return float4(0.0, 1, 0.0, 0.0);
}
			
			ENDCG
		}
	} 
FallBack Off
}
