#ifndef RAYTRACE
#define RAYTRACE

#include "SH_Lite.hlsl"
#include "BRDF.hlsl"
struct ObjectData
{
    int sdfDataOffset;
    int directLightSurfaceCacheIndex;
    int3 sdfDataSize;
    float4x4 localTo01AffineMatrix;
    float4x4 worldToLocalAffineMatrix;
    float4x4 localToWorldAffineMatrix;
    float deltaSize;
    float2 t;
};
/*
struct BVHNodeInfo
{
    int leftNodeIndex;
    int rightNodeIndex;
    int objectDataIndex;
    float3 minPoint;
    float3 maxPoint;
};*/
struct BVHNodeInfo
{
    int nodeIndices[8];
    int objectDataIndex;
    float3 minPoint;
    float3 maxPoint;
    int t;
};
struct SurfaceCacheInfo
{
    int meshCardIndex;
    int meshCardCount;
};

struct MeshCardInfo
{
    int4 rect;
    float4x4 sdfToLocalMatrix;
    float4x4 sdfToProjectionMatrix;
    float deltaDepth;
};

struct SurfaceCacheData
{
    half4 albedo;
    half4 normal;
    half4 emissive;
    half4 radiosityAtlas;
    half4 finalRadiosityAtlas;
    float depth;
    float metallic;
    float smoothness;

};
struct GlobalVoxel
{
    uint AlbedoFront;
    uint AlbedoBack;
    uint Normal;
    uint RadiosityAtlas;
    uint FinalRadiosityAtlas;
    uint FullVoxel;
};
struct RayMarchResult
{
    bool hit;
    float3 hitPosition;
    float3 normal;
    int step;
};
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

RWStructuredBuffer<float> _SDFDataBuffer;
float _SdfHitThreshold;
RWStructuredBuffer<ObjectData> _ObjectBuffer;
RWStructuredBuffer<BVHNodeInfo> _BVHBuffer;
RWStructuredBuffer<SurfaceCacheInfo> _SurfaceCacheInfo;
RWStructuredBuffer<MeshCardInfo> _MeshCardInfo;
RWStructuredBuffer<SurfaceCacheData> _SurfaceCache;
RWStructuredBuffer<GlobalRadiacneProbes> _GlobalRadiacneProbes;
TextureCube<float4> _EnvironmentCube;
SamplerState sampler_EnvironmentCube;

int _SurfaceCacheSize;
uint _BVHCount;
float4x4 _ProjectionMatrix;
float4x4 _ViewMatrix;
float4x4 _ProjectionMatrixInverse;
float4x4 _ViewMatrixInverse;
float4 _ZBufferParams;
float3 _CameraPosition;
float _FarClipPlane;

//---------------------GlobalSDF

float3 _CenterPosition;
float3 _CameraDirection;
uint _VoxelLength;
float _VoxelSize;
float _VoxelSafeSize;
float _VoxelBaseDeltaSize;
uint _MaxLevel;
float4 _LevelOffsets[12];
float4 _MipOffsets[12];
float4 _VoxelSpaceOriginFloats[12];
float _VoxelScaleFactor;
int _ConeCount;
int _ConeSteps;
float _ConeAngle;
float _TimeSeed;

RWStructuredBuffer<GlobalVoxel> _VoxelBuffer;
Texture3D<uint4> _VoxelDataRT; //x = front y = back z = normal w = FinalRadiosityAtlas
SamplerState _LinearClamp;
float _EnvironmentLightIntensity;

int _BlocksPerAxis;
int _BlockSize;
int _BlockLodLevel;
//-------------------------------------------------------------------------------------------------------------------------------------------
//                                                             Encode/Decode
//-------------------------------------------------------------------------------------------------------------------------------------------

GlobalRadiacneProbes L2_RGB_To_GlobalRadiacneProbes(
    L2_RGB rgb,
    float3 worldPos,
    int enable
)
{
    GlobalRadiacneProbes p;
    
    p.c0 = rgb.C[0];
    p.c1 = rgb.C[1];
    p.c2 = rgb.C[2];
    p.c3 = rgb.C[3];
    p.c4 = rgb.C[4];
    p.c5 = rgb.C[5];
    p.c6 = rgb.C[6];
    p.c7 = rgb.C[7];
    p.c8 = rgb.C[8];

    p.worldPos = worldPos;
    p.enable = enable;
    
    return p;
}


L2_RGB GlobalRadiacneProbes_To_L2_RGB(
    GlobalRadiacneProbes p
)
{
    L2_RGB rgb = L2_RGB::Zero();

    rgb.C[0] = p.c0;
    rgb.C[1] = p.c1;
    rgb.C[2] = p.c2;
    rgb.C[3] = p.c3;
    rgb.C[4] = p.c4;
    rgb.C[5] = p.c5;
    rgb.C[6] = p.c6;
    rgb.C[7] = p.c7;
    rgb.C[8] = p.c8;

    return rgb;
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

//-------------------------------------------------------------------------------------------------------------------------------------------
//                                                             
//-------------------------------------------------------------------------------------------------------------------------------------------
int GetGlobalRadiacneProbeIndex(int3 coord, int level)
{
    return coord.x + coord.y * _BlocksPerAxis + coord.z * _BlocksPerAxis * _BlocksPerAxis + level * _BlocksPerAxis * _BlocksPerAxis * _BlocksPerAxis;

}

inline float4 SampleEnvironment(float3 direction)
{
    return _EnvironmentCube.SampleLevel(sampler_EnvironmentCube, normalize(direction), 0);
}
uint GetVoxelLevel(float3 position)
{
    float3 distB = abs(position - _CenterPosition);
    
    float dist = max(distB.x, max(distB.y, distB.z));
    dist = max(dist, 1e-6);
    float levelFloat = floor(log2(dist / ((float) _VoxelSafeSize / 4)));
    levelFloat = clamp(levelFloat, 0, (float) (_MaxLevel - 1));

    return (uint) levelFloat;
}

//------------------------------------BufferPos
float FastHash(float4 seed)
{
    float4 p = frac(seed * 0.1031);
    p += dot(p, p.zwyx + 31.32);
    return frac((p.x + p.y) * (p.z + p.w));
}

float3 GetGlobalPosition(uint3 coord, uint level, uint mip)
{
    float voxelRealSize = _VoxelSize * pow(2, (float)level);
    int dim = max(1, (int)(_VoxelLength >> mip));
    float3 halfExtent = 0.5 * float3(voxelRealSize, voxelRealSize, voxelRealSize);
    
    float3 position = (_VoxelSpaceOriginFloats[level].xyz - halfExtent) + ((float3) coord + 0.5) * (voxelRealSize / (float) dim);
    
    return position;
}

int GetVoxelIndex(int level, int mip, int3 coord)
{
    int levelOffset = (int) _LevelOffsets[level].x;
    int mipOffset = (int) _MipOffsets[mip].x;
    int dim = ((int)_VoxelLength >> mip);
    int3 size = int3(dim, dim, dim);

    int localIndex = coord.x + coord.y * size.x + coord.z * size.x * size.y;
    return levelOffset + mipOffset + localIndex;
}
uint3 GetCoordFromGlobalPosition(float3 position, uint level, uint mip)
{
    float voxelRealSize = _VoxelSize * pow(2, (float) level);
    int dim = max(1, (int)(_VoxelLength >> mip));
    float3 halfExtent = 0.5 * float3(voxelRealSize, voxelRealSize, voxelRealSize);
    float3 A = _VoxelSpaceOriginFloats[level].xyz - halfExtent;
    float step = voxelRealSize / (float) dim;
    float3 coordF = (position - A) / step - 0.5;
    uint3 coord = uint3(round(coordF));
    
    return coord;
}
//-------------------------------------RWPos
float3 GetVoxelRTUVW(float3 worldPos){
    uint level = GetVoxelLevel(worldPos);
    float zOffest = level * _VoxelLength;
    
    float voxelRealSize = _VoxelSize * pow(2, level);
    float3 halfExtent = 0.5 * float3(voxelRealSize, voxelRealSize, voxelRealSize);
    float3 localPos = worldPos - (_VoxelSpaceOriginFloats[level].xyz - halfExtent);
    localPos = (localPos / voxelRealSize) * (float) _VoxelLength;
    localPos = clamp(localPos, 0, _VoxelLength);
    localPos.z += zOffest;
    float3 uvw = localPos / float3(_VoxelLength, _VoxelLength, _VoxelLength * _MaxLevel);
    return uvw;
}
float3 GetVoxelRTUVW(float3 worldPos, uint level)
{
    float zOffest = level * _VoxelLength;
    
    float voxelRealSize = _VoxelSize * pow(2, level);
    float3 halfExtent = 0.5 * float3(voxelRealSize, voxelRealSize, voxelRealSize);
    float3 localPos = worldPos - (_VoxelSpaceOriginFloats[level].xyz - halfExtent);
    localPos = (localPos / voxelRealSize) * (float) _VoxelLength;
    localPos = clamp(localPos, 0, _VoxelLength);
    localPos.z += zOffest;
    float3 uvw = localPos / float3(_VoxelLength, _VoxelLength, _VoxelLength * _MaxLevel);
    return uvw;
}
//-------------------------------------

GlobalRadiacneProbes GetGlobalProbe(float3 position)
{

    uint level = GetVoxelLevel(position);
    float voxelRealSize = _VoxelSize * pow(2, level);
    int dim = max(1, (int) (_BlocksPerAxis));

    float3 halfExtent = 0.5 * float3(voxelRealSize, voxelRealSize, voxelRealSize);
    float3 localPos = position - (_VoxelSpaceOriginFloats[level].xyz - halfExtent);
    localPos = (localPos / voxelRealSize) * (float) dim;
    int3 coord = int3(floor(localPos.x), floor(localPos.y), floor(localPos.z));

    coord.x = clamp(coord.x, 0, dim - 1);
    coord.y = clamp(coord.y, 0, dim - 1);
    coord.z = clamp(coord.z, 0, dim - 1);
    int index = GetGlobalRadiacneProbeIndex(coord, level);

    return _GlobalRadiacneProbes[index];
}

GlobalVoxel GetGlobalVoxel(float3 position, uint mip)
{

    uint level = GetVoxelLevel(position);
    float voxelRealSize = _VoxelSize * pow(2, level);
    int dim = max(1, (int)(_VoxelLength >> mip));

    float3 halfExtent = 0.5 * float3(voxelRealSize, voxelRealSize, voxelRealSize);
    float3 localPos = position - (_VoxelSpaceOriginFloats[level].xyz - halfExtent);
    localPos = (localPos / voxelRealSize) * (float) dim;
    int3 coord = int3(floor(localPos.x), floor(localPos.y), floor(localPos.z));

    coord.x = clamp(coord.x, 0, dim - 1);
    coord.y = clamp(coord.y, 0, dim - 1);
    coord.z = clamp(coord.z, 0, dim - 1);
    int index = GetVoxelIndex(level, mip, coord);

    return _VoxelBuffer[index];
}
void SetGlobalVoxel(float3 position, float3 radiosityAtlas)
{

    uint level = GetVoxelLevel(position);
    float voxelRealSize = _VoxelSize * pow(2, level);
    float3 halfExtent = 0.5 * (float) voxelRealSize;
    float3 localPos = position - (_VoxelSpaceOriginFloats[level].xyz - halfExtent);
    localPos = (localPos / voxelRealSize) * (float) _VoxelLength;
    int3 coord = int3(floor(localPos.x), floor(localPos.y), floor(localPos.z));

    coord.x = clamp(coord.x, 0, _VoxelLength - 1);
    coord.y = clamp(coord.y, 0, _VoxelLength - 1);
    coord.z = clamp(coord.z, 0, _VoxelLength - 1);

    int index = GetVoxelIndex(level, 0, coord);

    _VoxelBuffer[index].RadiosityAtlas = EncodeRGBAuint(float4(radiosityAtlas, 0));

}
L2_RGB SampleGlobalProbes_Trilinear(float3 worldPos)
{
    uint level = GetVoxelLevel(worldPos);
    float vSize = _VoxelSize * pow(2, level);
    float blockSize = vSize / (float) _BlocksPerAxis;

    float3 pos000 = worldPos + float3(0, 0, 0) * blockSize;
    float3 pos100 = worldPos + float3(1, 0, 0) * blockSize;
    float3 pos010 = worldPos + float3(0, 1, 0) * blockSize;
    float3 pos110 = worldPos + float3(1, 1, 0) * blockSize;
    float3 pos001 = worldPos + float3(0, 0, 1) * blockSize;
    float3 pos101 = worldPos + float3(1, 0, 1) * blockSize;
    float3 pos011 = worldPos + float3(0, 1, 1) * blockSize;
    float3 pos111 = worldPos + float3(1, 1, 1) * blockSize;



    GlobalRadiacneProbes p000 = GetGlobalProbe(pos000);
    GlobalRadiacneProbes p100 = GetGlobalProbe(pos100);
    GlobalRadiacneProbes p010 = GetGlobalProbe(pos010);
    GlobalRadiacneProbes p110 = GetGlobalProbe(pos110);
    GlobalRadiacneProbes p001 = GetGlobalProbe(pos001);
    GlobalRadiacneProbes p101 = GetGlobalProbe(pos101);
    GlobalRadiacneProbes p011 = GetGlobalProbe(pos011);
    GlobalRadiacneProbes p111 = GetGlobalProbe(pos111);

    float tx = saturate((worldPos.x - p000.worldPos.x) / (p100.worldPos.x - p000.worldPos.x));
    float ty = saturate((worldPos.y - p000.worldPos.y) / (p010.worldPos.y - p000.worldPos.y));
    float tz = saturate((worldPos.z - p000.worldPos.z) / (p001.worldPos.z - p000.worldPos.z));

    float w000 = (p000.enable != 0) ? (1 - tx) * (1 - ty) * (1 - tz) : 0;
    float w100 = (p100.enable != 0) ? (tx) * (1 - ty) * (1 - tz) : 0;
    float w010 = (p010.enable != 0) ? (1 - tx) * (ty) * (1 - tz) : 0;
    float w110 = (p110.enable != 0) ? (tx) * (ty) * (1 - tz) : 0;
    float w001 = (p001.enable != 0) ? (1 - tx) * (1 - ty) * (tz) : 0;
    float w101 = (p101.enable != 0) ? (tx) * (1 - ty) * (tz) : 0;
    float w011 = (p011.enable != 0) ? (1 - tx) * (ty) * (tz) : 0;
    float w111 = (p111.enable != 0) ? (tx) * (ty) * (tz) : 0;

    float wSum = w000 + w100 + w010 + w110
               + w001 + w101 + w011 + w111 + 1e-5;

    L2_RGB c000 = GlobalRadiacneProbes_To_L2_RGB(p000);
    L2_RGB c100 = GlobalRadiacneProbes_To_L2_RGB(p100);
    L2_RGB c010 = GlobalRadiacneProbes_To_L2_RGB(p010);
    L2_RGB c110 = GlobalRadiacneProbes_To_L2_RGB(p110);
    L2_RGB c001 = GlobalRadiacneProbes_To_L2_RGB(p001);
    L2_RGB c101 = GlobalRadiacneProbes_To_L2_RGB(p101);
    L2_RGB c011 = GlobalRadiacneProbes_To_L2_RGB(p011);
    L2_RGB c111 = GlobalRadiacneProbes_To_L2_RGB(p111);

    float invW = 1.0f / (w000 + w100 + w010 + w110 + w001 + w101 + w011 + w111 + 1e-5);
    
    L2_RGB sum0 = Add(Multiply(c000, w000), Multiply(c100, w100));
    L2_RGB sum1 = Add(Multiply(c010, w010), Multiply(c110, w110));
    L2_RGB sum2 = Add(Multiply(c001, w001), Multiply(c101, w101));
    L2_RGB sum3 = Add(Multiply(c011, w011), Multiply(c111, w111));

    L2_RGB acc0 = Add(sum0, sum1);
    L2_RGB acc1 = Add(sum2, sum3);
    L2_RGB total = Add(acc0, acc1);

    return Multiply(total, invW);
}
float3 SampleRadianceWithGlobalProbes(float3 worldPos, float3 normal)
{
    
    L2_RGB sh = SampleGlobalProbes_Trilinear(worldPos);
    
    return CalculateIrradiance(sh, normal);
}

float3 GetNormalizedPositionInCell(
    float3 position,
    uint level,
    uint mip)
{
    float voxelRealSize = _VoxelSize * pow(2.0, (float) level);
    int dim = max(1, (int)(_VoxelLength >> mip));
    float3 halfExtent = 0.5 * float3(voxelRealSize, voxelRealSize, voxelRealSize);
    float3 A = _VoxelSpaceOriginFloats[level].xyz - halfExtent;

    float step = voxelRealSize / (float) dim;
    float3 cellF = (position - A) / step; 
    float3 coordF = round(cellF - 0.5);
    float3 frac = cellF - coordF; 

    return frac;
}

uint GetMipCount()
{
    return (uint) floor(log2((float) _VoxelLength));
}
struct RayTraceResult
{
    bool hit;
    float3 hitPosition;
    float3 normal;
};


float EqualNonZero(float a, float b)//return 1 if a == b
{
    return 1 - sign(abs(a - b));
}
float3 GetNextRayStep(float3 position, float3 rayDirection, int mip)
{
    uint level = GetVoxelLevel(position);
    float voxelRealSize = _VoxelSize * pow(2, level);
    uint voxelMipLength = _VoxelLength >> mip;
    float voxelRealDeltaSize = (1 / (float) voxelMipLength) * voxelRealSize;
    float3 pos = _VoxelSpaceOriginFloats[level].xyz;
    float3 deltaPosition = position - pos;
    
    
    int3 stepSign = sign(rayDirection);
    
    float3 alignPosFloor = floor(deltaPosition / voxelRealDeltaSize) * voxelRealDeltaSize + pos + float3(1, 1, 1) * 0.0001;
    float3 alignPosCeil = ceil(deltaPosition / voxelRealDeltaSize) * voxelRealDeltaSize + pos + float3(1, 1, 1) * 0.0001;
    
    float3 nextStepPos = (alignPosFloor * (float3) (-(stepSign - 1) / 2) + alignPosCeil * (float3) ((stepSign + 1) / 2)) * abs(stepSign);
    float3 hCoord = min(abs((nextStepPos - position) / rayDirection), 100000);
    hCoord = (hCoord + (float3(1, 1, 1) * 100000) * (-abs(stepSign) + int3(1, 1, 1)));
    float minLength = min(min(hCoord.x, hCoord.y), hCoord.z);
    nextStepPos = position + minLength * rayDirection;
    return nextStepPos + rayDirection * voxelRealDeltaSize / 1000;

}


float3
    GlobalVoxelConeTrace(
    float3 worldRayOrigin, float3 worldRayDir, float3 worldNormal, float angle, uint rayStep, float maxDistance
)
{
    worldRayDir = normalize(worldRayDir);
    float stepSize = _VoxelBaseDeltaSize * exp2(3);
    float3 tracePosition = worldRayOrigin + worldRayDir * stepSize;
    float alphaAccum = 0;
    float distance = stepSize;
    float3 indirectColor = float3(0, 0, 0);
    [loop]
    for (uint i = 0; i < rayStep && length(tracePosition - worldRayOrigin) < maxDistance && alphaAccum < 0.99; i++)
    {
        float coneRadius = tan(angle) * distance;
        
        tracePosition = worldRayOrigin + worldRayDir * distance;
        float level = GetVoxelLevel(tracePosition);
        float minSize = _VoxelBaseDeltaSize * exp2(level);
        int mipLevel = max(log2(ceil(coneRadius / minSize)), 0);

        int levelOffest = clamp(mipLevel, 0, _MaxLevel - 1);
        level += levelOffest;
        mipLevel -= levelOffest;
        float3 uvw = GetVoxelRTUVW(tracePosition, (uint)level);
        float4 color = DecodeRGBAuint(_VoxelDataRT.SampleLevel(_LinearClamp, uvw, mipLevel).x);
        float weight = 1.0 - alphaAccum;
        weight = weight * weight;
        indirectColor += weight * color.xyz * color.a * (coneRadius + 1);
        alphaAccum += color.a;
        
        distance += coneRadius;

    }
    indirectColor *= saturate(dot(worldNormal, worldRayDir));
    return indirectColor;
}
RayTraceResult
    GlobalVoxelRayTrace(
    float3 worldRayOrigin,
    float3 worldRayDir,
    float maxDistance,
    uint maxStep,
    inout GlobalVoxel result
)
{
    worldRayDir = normalize(worldRayDir);
    RayTraceResult rr;
    rr.hit = false;
    rr.hitPosition = float3(0, 0, 0);
    rr.normal = float3(0, 0, 0);
    GlobalVoxel emptyVoxel;
    emptyVoxel.AlbedoFront = 0;
    emptyVoxel.RadiosityAtlas = 0;
    emptyVoxel.FinalRadiosityAtlas = 0;
    uint mipCount = GetMipCount();
    uint mip = (mipCount > 0) ? (mipCount - 1) : 0;
    float3 halfMaxExtent = 0.5 * (float) _VoxelLength * pow(2, _MaxLevel - 1);
    float3 minBB = _CenterPosition - halfMaxExtent;
    float3 maxBB = _CenterPosition + halfMaxExtent;
    float3 pos = worldRayOrigin;
    float3 nextPos = worldRayOrigin;
    float3 lastPos = pos;
    bool selfIntersection = true;
    [loop]
    for (uint i = 0; i < maxStep && length(nextPos - pos) < maxDistance; i++)
    {
        pos = nextPos;

        GlobalVoxel voxel = GetGlobalVoxel(pos, mip);

        [branch]
        if (voxel.Normal != 0)
        {
            [branch]
            if (mip > 0)
            {
                mip--;
            }
            else
            {
                if (selfIntersection)
                {
                    nextPos = GetNextRayStep(pos, worldRayDir, mip);
                    continue;
                }
                rr.hit = true;
                rr.hitPosition = pos;
                if (dot(worldRayDir, DecodeOct16(voxel.Normal)) > 0)
                {
                    voxel.AlbedoFront = voxel.AlbedoBack;

                }
                result = voxel;
                return rr;
            }
        }
        else
        {
            selfIntersection = false;
            nextPos = GetNextRayStep(pos, worldRayDir, mip);
            lastPos = pos;
            mip += 1;
            mip = clamp(mip, 0, (mipCount > 0) ? (mipCount - 1) : 0);
            
            [branch]
            if (pos.x < minBB.x || pos.y < minBB.y || pos.z < minBB.z ||
            pos.x > maxBB.x || pos.y > maxBB.y || pos.z > maxBB.z)
            {
                result = emptyVoxel;
                return rr;
            }
        }
    }

    result = emptyVoxel;
    return rr;
}
/*
float3 GlobalVoxelRadianceCalculate(float3 worldPos, float3 worldNormal, float coneAngle, uint count, uint rayStep)
{
    const float gAngle = 5.0832036899;
    float3 color = float3(0, 0, 0);
    for (uint i = 0; i < count; i++)
    {
        float random = FastHash(float4(worldPos, count));
        float fi = (float) i + random;
        float fiN = fi / count;
        float longitude = gAngle * fi;
        float latitude = asin(fiN * 2.0 - 1.0);
                    
        float3 kernel;
        kernel.x = cos(latitude) * cos(longitude);
        kernel.z = cos(latitude) * sin(longitude);
        kernel.y = sin(latitude);
                    
        kernel = normalize(kernel + worldNormal.xyz * 1.0); 
        color += GlobalVoxelConeTrace(worldPos, kernel, worldNormal, coneAngle, rayStep, 10000);

    }
    color /= count;

    return color;

}
*/
float3 RandomSample(float3 normal, float3 worldPos, float count, float i, float gAngle)
{
    float random = FastHash(float4(worldPos, count + _TimeSeed));
    float fi = (float) i + random;
    float fiN = fi / count;
    float longitude = gAngle * fi;
    float latitude = asin(fiN * 2.0 - 1.0);
                    
    float3 kernel;
    kernel.x = cos(latitude) * cos(longitude);
    kernel.z = cos(latitude) * sin(longitude);
    kernel.y = sin(latitude);
    return kernel;
}
uint HashUint(uint x)
{
    x += (x << 10);
    x ^= (x >> 6);
    x += (x << 3);
    x ^= (x >> 11);
    x += (x << 15);
    return x;
}

float Rand(float seed)
{
    const uint MANTISSA_MASK = 0x007FFFFF;
    const uint ONE_FLOAT = 0x3F800000; 
    uint bits = asuint(seed); 
    uint hash = HashUint(bits);
    hash = (hash & MANTISSA_MASK) | ONE_FLOAT; 
    return asfloat(hash) - 1.0;
}
float3 SampleUniformSphere(float2 rand2)
{
    float z = rand2.x * 2.0f - 1.0f;
    float phi = 2.0f * 3.1415926 * rand2.y;
    float r = sqrt(max(0.0f, 1.0f - z * z));
    return float3(r * cos(phi),
                  r * sin(phi),
                  z);
}
float3 SampleCosineHemisphere(float3 normal, float2 rand2, out float pdf)
{
    float phi = 2.0f * 3.1415926f * rand2.x;
    float cosTheta = sqrt(1.0f - rand2.y);
    float sinTheta = sqrt(rand2.y); 
    float3 localDir;
    localDir.x = cos(phi) * sinTheta;
    localDir.y = sin(phi) * sinTheta;
    localDir.z = cosTheta;
    float3 tangent = normalize(abs(normal.z) < 0.999f ? cross(normal, float3(0, 0, 1))
                                                     : cross(normal, float3(1, 0, 0)));
    float3 bitangent = normalize(cross(normal, tangent));
    float3 worldDir = localDir.x * tangent + localDir.y * bitangent + localDir.z * normal;
    worldDir = normalize(worldDir);
    pdf = cosTheta / 3.1415926f;
    return worldDir;
}
float3 GlobalVoxelRadianceCalculate(float3 worldPos, float3 worldNormal, float coneAngle, uint count, uint rayStep)
{
    const float gAngle = 5.0832036899;
    float3 color = float3(0, 0, 0);
    for (uint i = 0; i < count; i++)
    {
        float3 kernel = RandomSample(worldNormal, worldPos, count, i, gAngle);
        kernel = normalize(kernel + worldNormal.xyz * 1.0);
        GlobalVoxel voxel;
        RayTraceResult hitResult = GlobalVoxelRayTrace(worldPos, kernel, 10000, 100, voxel);
        color += hitResult.hit ? DecodeRGBAuint(voxel.AlbedoFront).xyz : float3(0, 0, 0);


    }
    color /= count;

    return color;

}

struct Ray
{
    float3 origin;
    float3 direction;
};
float3 SchlickFresnel(float3 F0, float cosTheta)
{
    return F0 + (1 - F0) * pow(1 - cosTheta, 5);
}

float G1_SmithGGX(float3 N, float3 X, float alpha)
{
    float NdotX = max(dot(N, X), 0.0f);
    float k = alpha * 0.5f;
    return NdotX / (NdotX * (1.0f - k) + k);
}

float D_GGX(float3 N, float3 H, float alpha)
{
    float NdotH = max(dot(N, H), 0.0f);
    float alpha2 = alpha * alpha;
    float denom = (NdotH * NdotH) * (alpha2 - 1.0f) + 1.0f;
    return alpha2 / (3.1415926 * denom * denom);
}
float3 LambertDiffuse(float3 normal, float3 lightDir, float3 lightRadiance)
{
    float cosTheta = saturate(dot(normal, lightDir));
    float3 diffuse = lightRadiance * cosTheta;
    return diffuse;
}
float3 LambertDiffuse(float3 albedo, float3 normal, float3 lightDir, float3 lightRadiance)
{

    float cosTheta = saturate(dot(normal, lightDir));
    float3 diffuse = (albedo / 3.14159265) * lightRadiance * cosTheta;
    return diffuse;
}


inline float LinearEyeDepth(float z)
{
    return 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w);
}

float2 NextRand(inout uint seed)
{
    float x = frac(sin(seed * 12.9898) * 43758.5453);
    float y = frac(sin(seed * 12.9898) * 43758.5453);
    return float2(x, y);
}
inline Ray ScreenToRay(float2 screenPos, float2 screneSize, float4x4 inverseProjectionMatrix, float4x4 inverseViewMatrix)
{

    float3 ndc;
    ndc.x = (screenPos.x / screneSize.x) * 2.0 - 1.0;
    ndc.y = (screenPos.y / screneSize.y) * 2.0 - 1.0;
    ndc.z = 1.0;

    float4 clipSpacePos = float4(ndc.xy, -1.0, 1.0);


    float4 viewSpacePos = mul(inverseProjectionMatrix, clipSpacePos);
    viewSpacePos /= viewSpacePos.w;
    

    float4 worldSpacePos = mul(inverseViewMatrix, viewSpacePos);


    float3 rayOrigin = mul(inverseViewMatrix, float4(0, 0, 0, 1)).xyz;
    float3 rayDirection = normalize(worldSpacePos.xyz - rayOrigin);


    Ray result;
    result.origin = rayOrigin;
    result.direction = rayDirection;
    return result;
}
float3 UVToWorld(float2 uv, float depth)
{
    float2 p11_22 = float2(_ProjectionMatrix._11, _ProjectionMatrix._22);
    float3 vpos = float3((uv * 2 - 1) / p11_22, -1) * depth;
    float4 wposVP = mul(_ViewMatrixInverse, float4(vpos, 1));
    return wposVP.xyz;
}
inline int GetSurfaceCacheIndex(int2 index)
{
    return index.x + _SurfaceCacheSize * index.y;
}
bool SetSurfaceCache(
    ObjectData objectData,
    float3 sdfPos,
    float3 r)
{
    SurfaceCacheInfo surfaceCache = _SurfaceCacheInfo[objectData.directLightSurfaceCacheIndex];
    for (int i = 0; i < surfaceCache.meshCardCount; i++)
    {
        int meshCardIndex = surfaceCache.meshCardIndex + i;
        MeshCardInfo meshCard = _MeshCardInfo[meshCardIndex];
        
        float4 clipPos = mul(meshCard.sdfToProjectionMatrix, float4(sdfPos, 1.0));
        float2 ndc = clipPos.xy / clipPos.w;
        float2 uv01 = ndc * 0.5 + 0.5;
        
        int2 rectXY = meshCard.rect.xy;
        int2 rectSize = meshCard.rect.zw - meshCard.rect.xy;
        
        int2 pixelCoord = clamp(int2(floor(uv01 * rectSize)), 0, rectSize);
        int2 finalCoord = rectXY + pixelCoord;
        
        float depth01 = (clipPos.z / clipPos.w) * 0.5 + 0.5;
        
        uint cacheIndex = GetSurfaceCacheIndex(finalCoord);
        SurfaceCacheData data = _SurfaceCache[cacheIndex];
        
        if ((depth01 - data.depth) <= meshCard.deltaDepth)
        {
            _SurfaceCache[cacheIndex].radiosityAtlas = float4(r, 1.0);
            return true;
        }
    }
    return false;
}

bool GetSurfaceCacheData(
    ObjectData objectData,
    float3 sdfPos,
    inout float3 outAlbedo,
    inout float3 outNormal,
    inout half3 outEmissive,
    inout float outMetallic,
    inout float outSmoothness,
    inout half4 outRadiosityAtlas
)
{
        

    SurfaceCacheInfo surfaceCache = _SurfaceCacheInfo[objectData.directLightSurfaceCacheIndex];
    for (int i = 0; i < surfaceCache.meshCardCount; i++)
    {
        int index = surfaceCache.meshCardIndex + i;
        MeshCardInfo meshCard = _MeshCardInfo[index];

        float4 clipPos = mul(meshCard.sdfToProjectionMatrix, float4(sdfPos, 1.0));
        float2 ndc = clipPos.xy / clipPos.w;
        float2 uv01 = ndc * 0.5 + 0.5;
        int2 rectXY = meshCard.rect.xy;
        int2 rectSize = meshCard.rect.zw - meshCard.rect.xy;

        int2 pixelCoord = int2(floor(uv01 * rectSize));
        pixelCoord.x = clamp(pixelCoord.x, 0, rectSize.x - 1);
        pixelCoord.y = clamp(pixelCoord.y, 0, rectSize.y - 1);
        int2 finalCoord = rectXY + pixelCoord;
        float depth01 = (clipPos.z / clipPos.w) * 0.5 + 0.5;
        SurfaceCacheData data = _SurfaceCache[GetSurfaceCacheIndex(finalCoord)];
        float depthVal = (float) data.depth;
        
        if ((depth01 - depthVal) <= meshCard.deltaDepth)
        {
            
            outAlbedo = (float3) data.albedo.xyz;
            outNormal = (float3) data.normal.xyz;
            outEmissive = (float3) data.emissive.xyz;
            outMetallic = data.metallic;
            outSmoothness = data.smoothness;
            outRadiosityAtlas = data.finalRadiosityAtlas;
            return true;
        }
    }
    return false;

}
bool GetSurfaceCacheData(
    ObjectData objectData,
    float3 sdfPos,
    inout float3 outAlbedo,
    inout float3 outNormal,
    inout half3 outEmissive,
    inout float outMetallic,
    inout float outSmoothness
)
{
        

    SurfaceCacheInfo surfaceCache = _SurfaceCacheInfo[objectData.directLightSurfaceCacheIndex];
    for (int i = 0; i < surfaceCache.meshCardCount; i++)
    {
        int index = surfaceCache.meshCardIndex + i;
        MeshCardInfo meshCard = _MeshCardInfo[index];

        float4 clipPos = mul(meshCard.sdfToProjectionMatrix, float4(sdfPos, 1.0));
        float2 ndc = clipPos.xy / clipPos.w;
        float2 uv01 = ndc * 0.5 + 0.5;
        int2 rectXY = meshCard.rect.xy;
        int2 rectSize = meshCard.rect.zw - meshCard.rect.xy;

        int2 pixelCoord = clamp(int2(floor(uv01 * rectSize)), 0, rectSize);
        int2 finalCoord = rectXY + pixelCoord;
        float depth01 = (clipPos.z / clipPos.w) * 0.5 + 0.5;
        SurfaceCacheData data = _SurfaceCache[GetSurfaceCacheIndex(finalCoord)];
        float depthVal = (float)data.depth;
        
        if (abs(depth01 - depthVal) <= meshCard.deltaDepth)
        {
            
            outAlbedo = (float3) data.albedo.xyz;
            outNormal = (float3) data.normal.xyz;
            outEmissive = (float3) data.emissive.xyz;
            outMetallic = data.metallic;
            outSmoothness = data.smoothness;
            return true;
        }
    }
    return false;

}

bool IntersectAABB(
    float3 rayOrigin,
    float3 rayDir,
    float3 minBox,
    float3 maxBox,
    out float tNear,    
    out float tFar,
    out float3 hitPoint
)
{
    const float EPS = 1e-8;
    float3 safeDir = sign(rayDir) * max(abs(rayDir), float3(EPS, EPS, EPS));

    float3 t1 = (minBox - rayOrigin) / safeDir;
    float3 t2 = (maxBox - rayOrigin) / safeDir;
    
    float3 tMin = min(t1, t2);
    float3 tMax = max(t1, t2);
    
    float largest_tmin = max(tMin.x, max(tMin.y, tMin.z));
    float smallest_tmax = min(tMax.x, min(tMax.y, tMax.z));
    
    if ((smallest_tmax < largest_tmin) || smallest_tmax < 0.0)
    {
        tNear = 0.0;
        tFar = 0.0;
        return false;
    }


    tNear = largest_tmin;
    tFar = smallest_tmax;
    hitPoint = rayOrigin + tNear * rayDir * sign(clamp(largest_tmin, 0, 1));
    return true;
}
float SampleSDF(uint3 pos, ObjectData info)
{
    uint3 sdfSize = (uint3) info.sdfDataSize;
    uint3 clampPos = clamp(pos, uint3(0, 0, 0), sdfSize - uint3(1, 1, 1));
    uint offset = info.sdfDataOffset;

    uint linearIndex = clampPos.x + clampPos.y * sdfSize.x + clampPos.z * (sdfSize.x * sdfSize.y);
    uint indexOffset = offset + linearIndex;

    return _SDFDataBuffer[indexOffset];
}
float SampleSDF(int3 pos, ObjectData info, out bool outOfRange, bool littleSize)
{
    int3 sdfSize = (int3) info.sdfDataSize;
    int3 minBound = int3(0, 0, 0);
    int3 maxBound = sdfSize - int3(1, 1, 1);
    int3 clampPos = clamp(pos, minBound, maxBound);

    int offset = info.sdfDataOffset;

    int linearIndex = clampPos.x + clampPos.y * sdfSize.x + clampPos.z * (sdfSize.x * sdfSize.y);
    int indexOffset = offset + linearIndex;
    if (!littleSize)
    {
        outOfRange = any(pos != clampPos);
    }
    else
    {
        minBound = int3(-1, -1, -1);
        maxBound = sdfSize + int3(1, 1, 1);
        clampPos = clamp(pos, minBound, maxBound);
        outOfRange = any(pos != clampPos);
        outOfRange = false;
    }

    return _SDFDataBuffer[indexOffset];
}



bool InBounds(float3 pos, float3 min, float3 max)
{
    return all(pos >= min) && all(pos <= max);
}
float3 LocalToSDF(float3 rayPos, float4x4 localTo01)
{
    return mul(localTo01, float4(rayPos, 1)).xyz + float3(0.5, 0.5, 0.5);//TODO Debug
} 

float3 SafeNormalize(float3 v)
{
    float len = length(v);
    return (len > 1e-6) ? v / len : float3(0, 0, 1);
}

bool RayIntersectUnitBox(float3 ro, float3 rd, out float3 hitPos)
{
    const float EPS = 1e-6;
    float3 safeDir = sign(rd) * max(abs(rd), float3(EPS, EPS, EPS));
    float3 invDir = 1.0 / safeDir;
    float3 t0 = (float3(0, 0, 0) - ro) * invDir;
    float3 t1 = (float3(1, 1, 1) - ro) * invDir;
    float3 tMin = min(t0, t1);
    float3 tMax = max(t0, t1);

    float tNear = max(max(tMin.x, tMin.y), tMin.z);
    float tFar = min(min(tMax.x, tMax.y), tMax.z);

    if (tNear > tFar || tFar < 0.0)
    {
        return false;
    }
    float tHit = tNear >= 0.0 ? tNear : tFar;

    hitPos = ro + rd * tHit;
    return true;
}

RayMarchResult RayMarchSDF(
    float3 rayOrigin,
    float3 rayDir,
    uint maxSteps,
    float3 minPoint,
    float3 maxPoint,
    ObjectData objectData
)
{
    // Initialize result
    RayMarchResult result;
    result.hit = false;
    result.hitPosition = float3(0, 0, 0);
    result.normal = float3(0, 0, 0);
    result.step = 0;
    // Transform ray into local SDF space
    float3 localOrigin = mul(objectData.worldToLocalAffineMatrix, float4(rayOrigin, 1)).xyz;
    float3 localDir = normalize(mul(objectData.worldToLocalAffineMatrix, float4(rayOrigin + rayDir, 1)).xyz - localOrigin);
    
    float3 sdfPos0 = LocalToSDF(localOrigin, objectData.localTo01AffineMatrix);
    float3 sdfDir = normalize(LocalToSDF(float3(localOrigin + localDir), objectData.localTo01AffineMatrix).xyz - sdfPos0);
    
    float3 hitPos;
    bool hitSDF = RayIntersectUnitBox(sdfPos0, sdfDir, hitPos);

    if (hitSDF)
    {
        float3 rayPos = localOrigin.xyz;
        float3 sizeFloat = (float3) objectData.sdfDataSize;

        float distanceTravelled = 0.001;
        bool selfIsect = true;
        bool littleSize = min(sizeFloat.x, min(sizeFloat.z, sizeFloat.y)) == 1;
        if (littleSize)
        {
            selfIsect = false;

        }
        float sdfHitThreshold = littleSize ? _SdfHitThreshold + 0.05 : _SdfHitThreshold;
        sdfHitThreshold *= objectData.deltaSize;
        for (uint step = 0; step < maxSteps; step++)
        {
            result.step++;
            float3 localPos = rayPos + localDir * distanceTravelled;
            float3 sdfPos = LocalToSDF(localPos, objectData.localTo01AffineMatrix);
            float3 worldPos = mul(objectData.localToWorldAffineMatrix, float4(localPos, 1)).xyz;
        // Check if position is outside AABB
            if (!InBounds(worldPos, minPoint, maxPoint))
            {
                break;
            }

        // Check if position is valid
            int3 gridPos = int3(floor(sdfPos * objectData.sdfDataSize));
        // Sample SDF value
            bool outRange = false;
            float sdfValue = SampleSDF(gridPos, objectData, outRange, littleSize);
            if (sdfValue < sdfHitThreshold && !outRange)
            { // Hit threshold
                if (!selfIsect)
                {
                    result.hit = true;
                    result.hitPosition = worldPos;

            // Compute normal via central difference gradient
                    float3 normal;
                    float delta = 0.01;
            
                    normal.x = SampleSDF(gridPos + uint3(1, 0, 0), objectData) - SampleSDF(gridPos - uint3(1, 0, 0), objectData);
                    normal.y = SampleSDF(gridPos + uint3(0, 1, 0), objectData) - SampleSDF(gridPos - uint3(0, 1, 0), objectData);
                    normal.z = SampleSDF(gridPos + uint3(0, 0, 1), objectData) - SampleSDF(gridPos - uint3(0, 0, 1), objectData);
                    result.normal = normalize(mul(objectData.localToWorldAffineMatrix, float4(localPos + normalize(normal), 1)).xyz - result.hitPosition);

                    return result;
                }

            }
            else
            {
                selfIsect = false;

            }

        // Increment distance
            distanceTravelled += max(abs(sdfValue), objectData.deltaSize);
        }
    }


    return result; // No hit
}
/*
struct StackEntry
{
    int nodeIndex;
    float tNear;
    float tFar;
};
#define BVH_STACK_SIZE 32
void PushStack(
    inout StackEntry stack[BVH_STACK_SIZE],
    inout int stackPtr,
    int nodeIndex,
    float tNear,
    float tFar)
{
    if (stackPtr < BVH_STACK_SIZE - 1)
    {
        stackPtr++;
    }
    stack[stackPtr].nodeIndex = nodeIndex;
    stack[stackPtr].tNear = tNear;
    stack[stackPtr].tFar = tFar;
}

bool PopStack(
    inout StackEntry stack[BVH_STACK_SIZE],
    inout int stackPtr,
    out StackEntry entry)
{
    entry = (StackEntry) 0;
    if (stackPtr < 0)
    {
        return false;
    }
    entry = stack[stackPtr];
    stackPtr--;
    return true;
}*/
struct StackEntry
{
    int nodeIndex;
    uint childMask;
};
#define BVH_STACK_SIZE 17
void PushStack(
    inout StackEntry stack[BVH_STACK_SIZE],
    inout int stackPtr,
    StackEntry entry)
{
    if (stackPtr < BVH_STACK_SIZE - 1)
    {
        stackPtr++;
    }
    stack[stackPtr] = entry;
}
void PushStack(
    inout StackEntry stack[BVH_STACK_SIZE],
    inout int stackPtr,
    int nodeIndex,
    uint childMask)
{
    if (stackPtr < BVH_STACK_SIZE - 1)
    {
        stackPtr++;
    }
    StackEntry stackNew;
    stackNew.nodeIndex = nodeIndex;
    stackNew.childMask = nodeIndex;
    stack[stackPtr] = stackNew;
}

bool PopStack(
    inout StackEntry stack[BVH_STACK_SIZE],
    inout int stackPtr,
    out StackEntry entry)
{
    entry = (StackEntry) 0;
    if (stackPtr < 0)
    {
        return false;
    }
    entry = stack[stackPtr];
    stackPtr--;
    return true;
}


RayMarchResult NoHit()
{
    RayMarchResult r;
    r.hit = false;
    r.hitPosition = float3(0, 0, 0);
    r.normal = float3(0, 0, 0);
    return r;
}
bool IsBitSet(uint mask, uint index)
{
    return (mask & (1u << index)) != 0;
}
int FindLowestZeroBit8(uint mask)
{
    uint inv = (~mask) & 0xFFu; 
    return (inv == 0) ? -1
                      : firstbitlow(inv); 
}
void WriteBit(inout StackEntry stack, uint index, bool on)
{
    uint bit = (1u << index);
    stack.childMask = on ? (stack.childMask | bit)
              : (stack.childMask & ~bit);
}
void WriteBit(inout uint mask, uint index, bool on)
{
    uint bit = (1u << index);
    mask = on ? (mask | bit) 
              : (mask & ~bit); 
}
RayMarchResult RayMarchSDFWithBVH(
    float3 worldRayOrigin,
    float3 worldRayDir,
    float maxDistance,
    uint maxMarchSteps,
    out ObjectData object
)
{
    RayMarchResult finalRes = NoHit();
    finalRes.hit = false;
    
    float closestHitDist = maxDistance;
    int nodeIndex = 0;
    /*-------------------ROOT*/
    BVHNodeInfo root = _BVHBuffer[0];
    float rtNear, rtFar;
    float3 hitPoint;
    bool rootHit = IntersectAABB(
            worldRayOrigin,
            worldRayDir,
            root.minPoint,
            root.maxPoint,
            rtNear,
            rtFar,
            hitPoint
        );
    if (!rootHit)
    {
        return finalRes;
    }
    if (root.objectDataIndex >= 0)
    {
        ObjectData rootObj = _ObjectBuffer[root.objectDataIndex];

        RayMarchResult r = RayMarchSDF(

                hitPoint,
                worldRayDir,
                maxMarchSteps,
                root.minPoint,
                root.maxPoint,
                rootObj
            );
            
        if (r.hit)
        {
            object = rootObj;
            float dist = distance(worldRayOrigin, r.hitPosition);
            if (dist < closestHitDist)
            {
                closestHitDist = dist;
                finalRes = r;
                finalRes.hit = true;

            }

        }
        return finalRes;
    }
    /*-----------------------*/
    StackEntry stack[BVH_STACK_SIZE];
    for (int i = 0; i < BVH_STACK_SIZE; i++)
    {
        stack[i].nodeIndex = -1;
        stack[i].childMask = 0;
    }
    int stackPtr = -1;
    BVHNodeInfo node = _BVHBuffer[nodeIndex];

    PushStack(stack, stackPtr, nodeIndex, 0);
    nodeIndex = -1;
    finalRes.step = 0;
    //-------------------BVH8Trace
    while (finalRes.step < 100)
    {
        finalRes.step += 1;
        StackEntry entry;
        if (!PopStack(stack, stackPtr, entry))
        {
            break;
        }

        node = _BVHBuffer[entry.nodeIndex];
        int childIndex = FindLowestZeroBit8(entry.childMask);
        nodeIndex = node.nodeIndices[childIndex];
        WriteBit(entry, childIndex, true);
        if (nodeIndex >= 0)
        {
            BVHNodeInfo childNode = _BVHBuffer[nodeIndex];
            float tNear, tFar;
            float3 hitPoint;
            bool intersected = IntersectAABB(worldRayOrigin, worldRayDir, childNode.minPoint, childNode.maxPoint, tNear, tFar, hitPoint);
            
            if (!intersected || tNear > closestHitDist || childNode.objectDataIndex < 0)//Not hit AABB
            {


            }
            else //hit
            {

                ObjectData obj = _ObjectBuffer[childNode.objectDataIndex];

                RayMarchResult r = RayMarchSDF(

                hitPoint,
                worldRayDir,
                maxMarchSteps,
                childNode.minPoint,
                childNode.maxPoint,
                obj);
                
                if (r.hit)
                {
                    object = obj;
                    float dist = distance(worldRayOrigin, r.hitPosition);
                    if (dist < closestHitDist)
                    {
                        closestHitDist = dist;
                        finalRes = r;
                        finalRes.hit = true;

                    }

                }
            }

            if (childNode.objectDataIndex < 0 && intersected)
            {
                if (childIndex < 7)
                {
                    PushStack(stack, stackPtr, entry);
                }
                StackEntry newStack;
                newStack.nodeIndex = nodeIndex;
                newStack.childMask = 0;
                PushStack(stack, stackPtr, newStack);
            }
            else
            {
                if (childIndex < 7)
                {
                    PushStack(stack, stackPtr, entry);
                }
            }

        }
        else
        {
            if (childIndex < 7)
            {
                PushStack(stack, stackPtr, entry);
            }
        }



    }
    return finalRes;

}

/* BVH2RayTrace
RayMarchResult RayMarchSDFWithBVH(
    float3 worldRayOrigin,
    float3 worldRayDir,
    float maxDistance,
    uint maxMarchSteps,
    out ObjectData object
)
{
    RayMarchResult finalRes = NoHit();
    finalRes.hit = false;

    float closestHitDist = maxDistance;

    int nodeIndex = 0;

    StackEntry stack[BVH_STACK_SIZE];
    int stackPtr = -1;

    finalRes.step = 0;
    while (true)
    {
        finalRes.step += 1;
        if (nodeIndex < 0)
        {
            StackEntry entry;
            if (!PopStack(stack, stackPtr, entry))
            {
                break;
            }
            nodeIndex = entry.nodeIndex;
        }

        if (nodeIndex < 0 || nodeIndex >= (int)_BVHCount)
        {
            nodeIndex = -1;
            continue;
        }

        BVHNodeInfo node = _BVHBuffer[nodeIndex];

        float tNear, tFar;
        float3 hitPoint;
        bool intersected = IntersectAABB(
            worldRayOrigin,
            worldRayDir,
            node.minPoint,
            node.maxPoint,
            tNear,
            tFar,
            hitPoint
        );

        if (!intersected || tNear > closestHitDist)
        {
            nodeIndex = -1;
            continue;
        }

        bool isLeaf = (node.objectDataIndex >= 0);
        if (isLeaf)
        {
            ObjectData obj = _ObjectBuffer[node.objectDataIndex];

            RayMarchResult r = RayMarchSDF(

                hitPoint,
                worldRayDir,
                maxMarchSteps,
                node.minPoint,
                node.maxPoint,
                obj
            );
            
            if (r.hit)
            {
                object = obj;
                float dist = distance(worldRayOrigin, r.hitPosition);
                if (dist < closestHitDist)
                {
                    closestHitDist = dist;
                    finalRes = r;
                    finalRes.hit = true;
                    //finalRes.step = r.step;

                }

            }

            nodeIndex = -1;
        }
        else
        {
            int leftIdx = node.leftNodeIndex;
            int rightIdx = node.rightNodeIndex;

            float tNearLeft = 0, tFarLeft = 0;
            float3 hpLeft = (float3) 0;
            bool leftHit = false;
            if (leftIdx >= 0)
            {
                BVHNodeInfo leftNode = _BVHBuffer[leftIdx];
                leftHit = IntersectAABB(
                    worldRayOrigin, worldRayDir,
                    leftNode.minPoint, leftNode.maxPoint,
                    tNearLeft, tFarLeft, hpLeft
                );
            }

            float tNearRight = 0, tFarRight = 0;
            float3 hpRight = (float3) 0;
            bool rightHit = false;
            if (rightIdx >= 0)
            {
                BVHNodeInfo rightNode = _BVHBuffer[rightIdx];
                rightHit = IntersectAABB(
                    worldRayOrigin, worldRayDir,
                    rightNode.minPoint, rightNode.maxPoint,
                    tNearRight, tFarRight, hpRight
                );
            }

            nodeIndex = -1;

            if (!leftHit && !rightHit)
            {
                continue;
            }

            if (leftHit && !rightHit && tNearLeft <= closestHitDist)
            {
                nodeIndex = leftIdx;
                continue;
            }
            if (rightHit && !leftHit && tNearRight <= closestHitDist)
            {
                nodeIndex = rightIdx;
                continue;
            }

            if (leftHit && rightHit)
            {
                if (tNearLeft < tNearRight)
                {
                    if (tNearRight <= closestHitDist)
                    {
                        PushStack(stack, stackPtr, rightIdx, tNearRight, tFarRight);
                    }
                    nodeIndex = leftIdx;
                }
                else
                {
                    if (tNearLeft <= closestHitDist)
                    {
                        PushStack(stack, stackPtr, leftIdx, tNearLeft, tFarLeft);
                    }
                    nodeIndex = rightIdx;
                }
            }
        }
    }

    return finalRes;
}*/
    float3 PathTrace(
    float3 startPos,
    float3 dir,
    float3 startDir,
    float3 startNormal,
    uint2 pixelCoord,
    uint sampleIndex,
    uint maxBounces
)
{
    float3 radiance = float3(0, 0, 0);
    float3 throughput = float3(1, 1, 1);

    Ray ray;
    ray.origin = startPos;
    ray.direction = dir;

    float3 baseColor = float3(0, 0, 0);
    half3 emissive = half3(0, 0, 0);
    float smoothness = 0.5;
    float metallic = 0;
    float3 directBRDFDir = startDir;
    half4 outRadiosityAtlas = half4(0, 0, 0, 0);
    float3 normal = startNormal;
    ObjectData selfObj;

    normal = startNormal;
    
    ObjectData hitObj;
    ObjectData nullObj;
    RayMarchResult hitResult = RayMarchSDFWithBVH(ray.origin, ray.direction, 5000.0, 128, hitObj);
    float3 hitPos = hitResult.hitPosition;
    if (hitResult.hit)
    {
        GetSurfaceCacheData(hitObj, mul(hitObj.worldToLocalAffineMatrix, float4(hitPos, 1)).xyz, baseColor, normal, emissive, metallic, smoothness, outRadiosityAtlas);

        float3 V = -directBRDFDir;
        float3 directBRDF = CookTorranceBRDF(
                    normal,
                    V,
                    -V,
                    baseColor,
                    metallic,
                    smoothness
                );
                
        radiance += ((float3) emissive + outRadiosityAtlas.xyz) * throughput;

    }
    else
    {
        radiance += SampleEnvironment(ray.direction).xyz * throughput * _EnvironmentLightIntensity;
    }

    return radiance;
}


//WaveIntrinsics    
uint _TotalRays;
RWStructuredBuffer<int> _GlobalCounter;
#define PERSIST_BATCH_SIZE 64
#define GROUP_THREADS 64

groupshared int localStart;
groupshared int popIdx;
groupshared uint exitFlag;
//must use uint3 gtID : SV_GroupThreadID
#define PERSIST_PRE(gtID) \
    if (gtID.x == 0) \
        localStart = 0; \
        popIdx     = 0; \
        exitFlag = 0; \
    GroupMemoryBarrierWithGroupSync(); \
    bool shouldExitLocal = false;
    int safeValue = 0;
#define PERSIST_INIT(gtID) \
    if (gtID.x == 0) { \
       int oldValue; \
       InterlockedAdd(_GlobalCounter[0], PERSIST_BATCH_SIZE, oldValue); \
       localStart = oldValue; \
       popIdx     = 0; \
    } \
    GroupMemoryBarrierWithGroupSync(); \
    safeValue++;

#define PERSIST_POP(pixelCoord) \
    int _ps_idx = 0; \
    InterlockedAdd(popIdx, 1, _ps_idx); \
    int rayIdxVarRaw = localStart + _ps_idx; \
    int rayIdxVar = clamp(rayIdxVarRaw, localStart, localStart + PERSIST_BATCH_SIZE); \
    int2 pixelCoord = int2((uint)rayIdxVar % (uint)_ScreenWidth, (uint)rayIdxVar / (uint)_ScreenWidth);
#define PERSIST_CHECK(gtID) \
    if(rayIdxVarRaw > (int)_TotalRays || safeValue > 1000000){ \
       shouldExitLocal = true; \
    } \
    if (gtID.x == 0) { \
        exitFlag = 0; \
    } \
    GroupMemoryBarrierWithGroupSync(); \
    if (shouldExitLocal) { \
        uint oldValue; \
        InterlockedOr(exitFlag, 1, oldValue); \
    } \
    GroupMemoryBarrierWithGroupSync(); \
    if (exitFlag != 0) { \
        GroupMemoryBarrierWithGroupSync(); \
        break; \
    }
/*
PERSIST_PRE(gtID)
while (true) {  
    PERSIST_INIT(gtID);
    PERSIST_POP(index);
        // —— RayTrace ——  
    PERSIST_CHECK(gtID)
}

*/
#endif 