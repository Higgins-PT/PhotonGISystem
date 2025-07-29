
#ifndef BRDF_SAMPLER_HLSL
#define BRDF_SAMPLER_HLSL
#define PI 3.14159265
// ──────────────────────────────────────────────────────────────
// Math helpers
// ──────────────────────────────────────────────────────────────
float3 ToWorld(float3 v, float3 N)
{
    float3 B, T;
    if (abs(N.z) < 0.999f)
    {
        B = normalize(cross(float3(0, 0, 1), N));
    }
    else
    {
        B = normalize(cross(float3(0, 1, 0), N));
    }
    T = cross(B, N);
    return v.x * T + v.y * B + v.z * N;
}

float3 CosineSampleHemisphere(float2 u)
{
    float phi = 2.0 * PI * u.x;
    float cosT = sqrt(1.0 - u.y);
    float sinT = sqrt(u.y);
    return float3(cos(phi) * sinT, sin(phi) * sinT, cosT);
}

// Smith‑Schlick GGX  (single direction)
float SmithG1_GGX(float NdotV, float alpha)
{
    float a2 = alpha * alpha;
    float b = sqrt(a2 + (1.0 - a2) * NdotV * NdotV);
    return 2.0 * NdotV / (NdotV + b);
}

// Heitz 2018 – VNDF hemispherical GGX sampling
float3 SampleGGXVNDF(float3 V, float3 N, float alpha, float2 u)
{
    // stretch view
    float3 Vh = normalize(float3(alpha * V.x, alpha * V.y, V.z));
    // orthonormal basis
    float lensq = Vh.x * Vh.x + Vh.y * Vh.y;
    float3 T1 = lensq > 0 ? float3(-Vh.y, Vh.x, 0) / sqrt(lensq) : float3(1, 0, 0);
    float3 T2 = cross(Vh, T1);
    // sample polar coords
    float r = sqrt(u.x);
    float phi = 2.0 * PI * u.y;
    float t1 = r * cos(phi);
    float t2 = r * sin(phi);
    float s = 0.5 * (1.0 + Vh.z);
    t2 = (1.0 - s) * sqrt(1.0 - t1 * t1) + s * t2;
    // transform
    float3 Nh = t1 * T1 + t2 * T2 + sqrt(max(0.0, 1.0 - t1 * t1 - t2 * t2)) * Vh;
    // unstretch
    float3 H = normalize(float3(alpha * Nh.x, alpha * Nh.y, max(0.0, Nh.z)));
    return H;
}

// NDF: Trowbridge‑Reitz GGX
float D_GGX(float NdotH, float alpha)
{
    float a2 = alpha * alpha;
    float d = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d);
}

// Fresnel Schlick
float3 FresnelSchlick(float3 F0, float HdotV)
{
    return F0 + (1.0 - F0) * pow(1.0 - HdotV, 5.0);
}
float ComputeLuminanceBRDF(float3 color)
{
    float3 luminanceCoeff = float3(0.2126, 0.7152, 0.0722);
    float luminance = dot(color, luminanceCoeff);
    return luminance;
}
// Cook‑Torrance BRDF
float3 CookTorranceBRDF(
    float3 N, float3 V, float3 L,
    float3 baseColor, float3 metallicColor, float roughness)
{
    float3 H = normalize(V + L);
    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float VdotH = max(dot(V, H), 0.0);
    metallicColor = clamp(metallicColor, 0, 0.995);
    roughness = clamp(roughness, 0.05, 1);
    float3 F0 = lerp(0.04, baseColor.xyz, ComputeLuminanceBRDF(metallicColor.xyz).xxx);
    float3 F = FresnelSchlick(F0, VdotH);

    float alpha = roughness * roughness;
    float D = D_GGX(NdotH, alpha);
    float G = SmithG1_GGX(NdotV, alpha) * SmithG1_GGX(NdotL, alpha);

    float3 spec = (D * G * F) / max(4.0 * NdotV * NdotL, 1e-4);
    float3 diff = ((1.0 - metallicColor.xyz) * baseColor / PI);

    return (diff + spec);
}

// ──────────────────────────────────────────────────────────────
// BRDF importance sampler
// ──────────────────────────────────────────────────────────────
// Inputs:
//   N, V           ‑ shading normal & view dir (both normalized, V points OUT of surface)
//   baseColor      ‑ albedo in linear space [0,1]
//   metallic       ‑ [0,1]
//   roughness      ‑ perceptual roughness [0,1]
//   u1,u2,u3       ‑ three independent uniform random numbers in (0,1)
// Outputs (by reference):
//   L              ‑ sampled light direction, normalized
//   f              ‑ BRDF value for that (N,V,L)
//   pdf            ‑ combined pdf(N,V,L)
// Returns: true if sample is valid (N·L > 0)
bool SampleBRDF(
    float3 N, float3 V,
    float3 baseColor, float metallic, float roughness,
    float u1, float u2, float u3,
    out float3 L, out float3 f, out float pdf)
{
    metallic = clamp(metallic, 0.05, 0.95);
    roughness = clamp(roughness, 0.05, 1);
    // ---------- Branch selection ----------
    float pDiffuse = saturate(1.0 - metallic);
    bool chooseDiff = (u1 < pDiffuse);
    float xi1 = chooseDiff ? (u1 / max(pDiffuse, 1e-6)) : ((u1 - pDiffuse) / max(1.0 - pDiffuse, 1e-6));
    float2 rand2 = float2(xi1, u2);

    float pdf_diff = 0, pdf_spec = 0;

    if (chooseDiff)
    {
        // -------- Diffuse: cosine hemisphere --------
        float3 localDir = CosineSampleHemisphere(rand2);
        L = normalize(ToWorld(localDir, N));
        float NdotL = max(dot(N, L), 0.0);
        pdf_diff = NdotL / PI;
    }
    else
    {
        // -------- Specular: GGX VNDF --------
        float alpha = roughness * roughness;
        float3 H = SampleGGXVNDF(V, N, alpha, rand2);
        L = reflect(-V, H);
        if (dot(N, L) <= 0.0)
        {
            pdf = 0;
            f = 0;
            return false;
        }
        float NdotH = max(dot(N, H), 0.0);
        float VdotH = max(dot(V, H), 0.0);
        pdf_spec = D_GGX(NdotH, alpha) * NdotH / max(4.0 * VdotH, 1e-6);
    }

    // ---------- Combined pdf ----------
    pdf = pDiffuse * pdf_diff + (1.0 - pDiffuse) * pdf_spec;
    if (pdf <= 1e-6)
    {
        f = 0;
        return false;
    }
    f = CookTorranceBRDF(N, V, L, baseColor, metallic, roughness);

    return true;
}
// ──────────────────────────────────────────────────────────────

float EvaluateBRDFPdf(
    float3 N, float3 V, float3 L,
    float metallic,
    float roughness)
{
    float NdotL = dot(N, L);
    if (NdotL <= 0.0)
        return 0.0;

   
    float pdf_diff = NdotL / PI; 

    float3 H = normalize(V + L);
    float NdotH = max(dot(N, H), 0.0);
    float VdotH = max(dot(V, H), 0.0);
    float alpha = roughness * roughness;

    float a2 = alpha * alpha;
    float D = a2 / (PI * pow((NdotH * NdotH) * (a2 - 1.0) + 1.0, 2.0));

    float pdf_spec = D * NdotH / max(4.0 * VdotH, 1e-6);

    float pDiffuse = saturate(1.0 - metallic); 
    float pdf_mix = pDiffuse * pdf_diff 
                    + (1.0 - pDiffuse) * pdf_spec;

    return pdf_mix;
}
#endif 
