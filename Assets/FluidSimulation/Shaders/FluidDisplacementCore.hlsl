// ============================================================================
// Flexus Fluid Simulation - Displacement & Waves Core HLSL Module
// Handles vertex displacement and dynamic normal reconstruction from
// procedural noise and interactive heightfield simulation buffers.
// ============================================================================

#ifndef FLUID_DISPLACEMENT_CORE_INCLUDED
#define FLUID_DISPLACEMENT_CORE_INCLUDED

#include "FluidDisplacementInput.hlsl"

// Fallback cbuffer for SRP Batcher when included without a host shader cbuffer
#ifndef FLUID_DISPLACEMENT_UNIFORMS_DECLARED
CBUFFER_START(UnityPerMaterial)
    FLUID_DISPLACEMENT_UNIFORMS
CBUFFER_END
#endif

// ============================================================================
// 1. Data Contracts for Surface Evaluation
// ============================================================================
struct FluidNoiseSample
{
    float height;
    float2 gradient; // dH/d(posOS.x), dH/d(posOS.z) - surface slope vector
};

struct FluidSurfaceSample
{
    float height;
    float2 gradient; // dH/d(posOS.x), dH/d(posOS.z) - surface slope vector
};

// ============================================================================
// 2. 2D Simplex Noise with Analytical Derivative (Gustavson / Ashima algorithm)
// Computes height f(x) and its spatial gradient grad(f) in a single pass.
// This avoids re-evaluating the permutation hash table for finite difference taps.
// ============================================================================
float3 FluidMod289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float2 FluidMod289(float2 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float3 FluidPermute(float3 x) { return FluidMod289(((x * 34.0) + 1.0) * x); }

float FluidSimplex2D_Grad(float2 v, out float2 grad)
{
    const float4 C = float4(0.211324865405187,  // (3.0 - sqrt(3.0)) / 6.0
                            0.366025403784439,  // 0.5 * (sqrt(3.0) - 1.0)
                           -0.577350269189626,  // -1.0 + 2.0 * C.x
                            0.024390243902439); // 1.0 / 41.0

    // First corner
    float2 i  = floor(v + dot(v, C.yy));
    float2 x0 = v - i + dot(i, C.xx);

    // Other corners
    float2 i1 = (x0.x > x0.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);
    float4 x12 = x0.xyxy + C.xxzz;
    x12.xy -= i1;

    // Permutations
    i = FluidMod289(i);
    float3 p = FluidPermute(FluidPermute(i.y + float3(0.0, i1.y, 1.0))
                          + i.x + float3(0.0, i1.x, 1.0));

    float3 t = max(0.5 - float3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0);

    // Gradients
    float3 x = 2.0 * frac(p * C.www) - 1.0;
    float3 h = abs(x) - 0.5;
    float3 ox = floor(x + 0.5);
    float3 a0 = x - ox;

    float3 norm = 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);
    float2 g0 = float2(a0.x, h.x) * norm.x;
    float2 g1 = float2(a0.y, h.y) * norm.y;
    float2 g2 = float2(a0.z, h.z) * norm.z;

    float3 gdotx = float3(dot(g0, x0), dot(g1, x12.xy), dot(g2, x12.zw));

    // Analytical derivative of the implemented kernel f = t^4 * (g . x) with Taylor-normalized gradients:
    // grad(f) = t^3 * (t * g - 8.0 * x * (g . x))
    float3 t2 = t * t;
    float3 t3 = t2 * t;
    float3 t4 = t2 * t2;

    float n = 130.0 * dot(t4, gdotx);

    float2 grad0 = t3.x * (t.x * g0 - 8.0 * x0 * gdotx.x);
    float2 grad1 = t3.y * (t.y * g1 - 8.0 * x12.xy * gdotx.y);
    float2 grad2 = t3.z * (t.z * g2 - 8.0 * x12.zw * gdotx.z);
    grad = 130.0 * (grad0 + grad1 + grad2);

    return n;
}

// Lightweight height-only Simplex 2D (skips all gradient ALU)
float FluidSimplex2D(float2 v)
{
    const float4 C = float4(0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439);
    float2 i  = floor(v + dot(v, C.yy));
    float2 x0 = v - i + dot(i, C.xx);
    float2 i1 = (x0.x > x0.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);
    float4 x12 = x0.xyxy + C.xxzz;
    x12.xy -= i1;
    i = FluidMod289(i);
    float3 p = FluidPermute(FluidPermute(i.y + float3(0.0, i1.y, 1.0)) + i.x + float3(0.0, i1.x, 1.0));
    float3 m = max(0.5 - float3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0);
    m = m * m; m = m * m;
    float3 x = 2.0 * frac(p * C.www) - 1.0;
    float3 h = abs(x) - 0.5;
    float3 ox = floor(x + 0.5);
    float3 a0 = x - ox;
    m *= 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);
    float3 g;
    g.x  = a0.x  * x0.x  + h.x  * x0.y;
    g.yz = a0.yz * x12.xz + h.yz * x12.yw;
    return 130.0 * dot(m, g);
}

// ============================================================================
// 3. Animation Time & Domain Helpers
// ============================================================================
float GetFluidAnimationTime(float timeVal)
{
    float visc = saturate(_Viscosity);
    float speedFactor = lerp(1.30, 0.15, visc);
    return _NoiseTime > 0.0001 ? _NoiseTime : (timeVal * speedFactor);
}

float2 GetFluidDomainSize()
{
    return _FluidDomainSizeOS.xy > 0.01 ? _FluidDomainSizeOS.xy : float2(10.0, 10.0);
}

float2 GetFluidDomainInvSize()
{
    return _FluidDomainSizeOS.zw > 0.0001 ? _FluidDomainSizeOS.zw : (1.0 / GetFluidDomainSize());
}

// ============================================================================
// 4. Procedural Fluid Noise: Classic Simplex & Basin Slosh
// Both algorithms return height and analytical gradients in Object Space
// ============================================================================

// Mode 0: Classic Multi-Octave Simplex fBm (Full Sample: Height + Gradient)
FluidNoiseSample EvaluateClassicSimplex(float2 posOS_xz, float animTime)
{
    float2 uv1 = posOS_xz * _NoiseScale;
    float2 move = float2(_NoiseSpeed * 0.7, _NoiseSpeed * 0.5) * animTime;

    float2 g1;
    float h1 = FluidSimplex2D_Grad(uv1 + move, g1);
    float totalH = h1;
    float2 totalGrad = g1 * _NoiseScale;

    if (_NoiseOctaves > 1.5)
    {
        float scale2 = _NoiseScale * 2.05;
        float2 g2;
        float h2 = FluidSimplex2D_Grad(posOS_xz * scale2 - move * 1.3, g2);
        totalH += 0.5 * h2;
        totalGrad += 0.5 * g2 * scale2;
    }

    if (_NoiseOctaves > 2.5)
    {
        float scale3 = _NoiseScale * 4.12;
        float2 g3;
        float h3 = FluidSimplex2D_Grad(posOS_xz * scale3 + move * 1.7, g3);
        totalH += 0.25 * h3;
        totalGrad += 0.25 * g3 * scale3;
    }

    FluidNoiseSample sample;
    sample.height = totalH * _NoiseAmplitude;
    sample.gradient = totalGrad * _NoiseAmplitude;
    return sample;
}

// Mode 0: Height Only (Optimized for displacement only passes)
float SampleClassicSimplexHeightOnly(float2 posOS_xz, float animTime)
{
    float2 uv = posOS_xz * _NoiseScale;
    float2 move = float2(_NoiseSpeed * 0.7, _NoiseSpeed * 0.5) * animTime;

    float h = FluidSimplex2D(uv + move);
    if (_NoiseOctaves > 1.5)
    {
        h += 0.5 * FluidSimplex2D(uv * 2.05 - move * 1.3);
    }
    if (_NoiseOctaves > 2.5)
    {
        h += 0.25 * FluidSimplex2D(uv * 4.12 + move * 1.7);
    }

    return h * _NoiseAmplitude;
}

// Mode 1: Basin Sloshing (Standing Waves / Seiche)
// Enclosed-domain wave model: sums fundamental standing wave modes
// with boundary run-up weighting and counter-propagating Simplex noise.
FluidNoiseSample EvaluateBasinSlosh(float2 posOS_xz, float animTime)
{
    float2 invHalfSize = GetFluidDomainInvSize() * 2.0;
    float2 p = clamp(posOS_xz * invHalfSize, -1.2, 1.2);
    float distSq = dot(p, p);

    // 1. Boundary Run-Up Factor & derivative
    float wallBoost = 0.70 + 0.85 * saturate(distSq * 0.7);
    float2 dWallBoost_dp = (distSq * 0.7 < 1.0) ? (0.85 * 0.7 * 2.0 * p) : float2(0.0, 0.0);

    // 2. Fundamental Basin Slosh Modes & analytical trigonometric derivatives
    float sloshSpeed = _NoiseSpeed * 1.1;
    float theta = animTime * sloshSpeed;

    float surgeX = sin(p.x * 1.5708) * cos(theta);
    float dSurgeX_dx = cos(p.x * 1.5708) * 1.5708 * cos(theta);

    float surgeZ = sin(p.y * 1.5708) * sin(theta * 1.06 + 0.4);
    float dSurgeZ_dy = cos(p.y * 1.5708) * 1.5708 * sin(theta * 1.06 + 0.4);

    float surgeDiag = sin(p.x * 1.5708) * sin(p.y * 1.5708) * cos(theta * 1.32 + 0.8);
    float dSurgeDiag_dx = cos(p.x * 1.5708) * 1.5708 * sin(p.y * 1.5708) * cos(theta * 1.32 + 0.8);
    float dSurgeDiag_dy = sin(p.x * 1.5708) * cos(p.y * 1.5708) * 1.5708 * cos(theta * 1.32 + 0.8);

    float surgeIntensity = lerp(0.4, 1.4, saturate(_WarpStrength));
    float sloshRaw = (surgeX * 0.55 + surgeZ * 0.50 + surgeDiag * 0.35) * surgeIntensity;
    float2 dSloshRaw_dp = float2(
        (dSurgeX_dx * 0.55 + dSurgeDiag_dx * 0.35) * surgeIntensity,
        (dSurgeZ_dy * 0.50 + dSurgeDiag_dy * 0.35) * surgeIntensity
    );

    float sloshSurge = sloshRaw * wallBoost;
    float2 dSloshSurge_dp = dSloshRaw_dp * wallBoost + sloshRaw * dWallBoost_dp;
    float2 dSloshSurge_dpos = dSloshSurge_dp * invHalfSize;

    // 3. Counter-Reflecting Standing Waves with analytical gradients
    float2 uv = posOS_xz * _NoiseScale;
    float2 move = float2(_NoiseSpeed * 0.65, _NoiseSpeed * 0.50) * animTime;

    float2 gF, gRx, gRz;
    float nForward  = FluidSimplex2D_Grad(uv + move, gF);
    float nReflectX = FluidSimplex2D_Grad(float2(-uv.x, uv.y) + float2(-move.x, move.y), gRx);
    float nReflectZ = FluidSimplex2D_Grad(float2(uv.x, -uv.y) + float2(move.x, -move.y), gRz);

    float2 dReflectX_duv = float2(-gRx.x, gRx.y);
    float2 dReflectZ_duv = float2(gRz.x, -gRz.y);

    float nStanding = nForward * 0.50 + nReflectX * 0.25 + nReflectZ * 0.25;
    float2 dStanding_duv = gF * 0.50 + dReflectX_duv * 0.25 + dReflectZ_duv * 0.25;

    if (_NoiseOctaves > 1.5)
    {
        float2 uv2 = uv * 2.1;
        float2 move2 = move * 1.35;
        float2 g2F, g2R;
        float n2F = FluidSimplex2D_Grad(uv2 - move2, g2F);
        float n2R = FluidSimplex2D_Grad(float2(-uv2.x, uv2.y) + float2(move2.x, -move2.y), g2R);
        float2 d2R_duv2 = float2(-g2R.x, g2R.y);

        nStanding += 0.25 * (n2F + n2R * 0.5);
        dStanding_duv += 0.25 * (g2F * 2.1 + d2R_duv2 * (2.1 * 0.5));
    }

    float2 dStanding_dpos = dStanding_duv * _NoiseScale;

    // 4. Combined Slosh & Standing Waves
    float totalH = sloshSurge + nStanding * 0.85;
    float2 dTotalH_dpos = dSloshSurge_dpos + dStanding_dpos * 0.85;

    FluidNoiseSample sample;
    sample.height = totalH * _NoiseAmplitude;
    sample.gradient = dTotalH_dpos * _NoiseAmplitude;
    return sample;
}

// Mode 1: Height Only
float SampleBasinSloshHeightOnly(float2 posOS_xz, float animTime)
{
    float2 invHalfSize = GetFluidDomainInvSize() * 2.0;
    float2 p = clamp(posOS_xz * invHalfSize, -1.2, 1.2);
    float distSq = dot(p, p);

    float wallBoost = 0.70 + 0.85 * saturate(distSq * 0.7);

    float sloshSpeed = _NoiseSpeed * 1.1;
    float theta = animTime * sloshSpeed;

    float surgeX = sin(p.x * 1.5708) * cos(theta);
    float surgeZ = sin(p.y * 1.5708) * sin(theta * 1.06 + 0.4);
    float surgeDiag = sin(p.x * 1.5708) * sin(p.y * 1.5708) * cos(theta * 1.32 + 0.8);

    float surgeIntensity = lerp(0.4, 1.4, saturate(_WarpStrength));
    float sloshSurge = (surgeX * 0.55 + surgeZ * 0.50 + surgeDiag * 0.35) * (surgeIntensity * wallBoost);

    float2 uv = posOS_xz * _NoiseScale;
    float2 move = float2(_NoiseSpeed * 0.65, _NoiseSpeed * 0.50) * animTime;

    float nForward  = FluidSimplex2D(uv + move);
    float nReflectX = FluidSimplex2D(float2(-uv.x, uv.y) + float2(-move.x, move.y));
    float nReflectZ = FluidSimplex2D(float2(uv.x, -uv.y) + float2(move.x, -move.y));

    float nStanding = nForward * 0.50 + nReflectX * 0.25 + nReflectZ * 0.25;
    if (_NoiseOctaves > 1.5)
    {
        float2 uv2 = uv * 2.1;
        float2 move2 = move * 1.35;
        float n2F = FluidSimplex2D(uv2 - move2);
        float n2R = FluidSimplex2D(float2(-uv2.x, uv2.y) + float2(move2.x, -move2.y));
        nStanding += 0.25 * (n2F + n2R * 0.5);
    }

    float totalH = sloshSurge + nStanding * 0.85;
    return totalH * _NoiseAmplitude;
}

// Unified Noise Dispatchers
FluidNoiseSample EvaluateFluidNoise(float2 posOS_xz, float animTime)
{
    FluidNoiseSample sample;
    if (_NoiseAmplitude < 0.001)
    {
        sample.height = 0.0;
        sample.gradient = float2(0.0, 0.0);
        return sample;
    }

    if (_NoiseType > 0.5)
    {
        return EvaluateBasinSlosh(posOS_xz, animTime);
    }
    else
    {
        return EvaluateClassicSimplex(posOS_xz, animTime);
    }
}

float SampleFluidNoiseHeightOnly(float2 posOS_xz, float animTime)
{
    if (_NoiseAmplitude < 0.001) return 0.0;

    if (_NoiseType > 0.5)
    {
        return SampleBasinSloshHeightOnly(posOS_xz, animTime);
    }
    else
    {
        return SampleClassicSimplexHeightOnly(posOS_xz, animTime);
    }
}

// ============================================================================
// 5. Interactive Surface Evaluation (RenderTexture Sampling & Gradient)
//
// LOD 0: Explicitly samples mip 0 because this buffer is treated as a single-resolution
// simulation field, and allows evaluation in vertex stages lacking ddx/ddy quad derivatives.
//
// 1.5 Texel Offset: Four bilinear samples at ±1.5 texel offsets provide a
// spatially distributed low-pass / anti-aliasing effect against high-frequency grid noise.
//
// Stencil Design: Height uses a 5-tap custom weighted smoothing filter (center weight 0.4
// + 4 diagonal corners 0.15 each). Gradient is estimated independently from the filtered
// height value using a symmetric 4-tap diagonal central difference from corner pairs.
// ============================================================================
float SampleInteractiveHeightOnly(float2 uv)
{
    if (_InteractiveHeightScale < 0.001) return 0.0;

    float2 dUV = _DisplacementMap_TexelSize.xy * 1.5;
    float hCenter = SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_LinearClamp, uv, 0).r;
    float h1 = SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_LinearClamp, uv + float2( dUV.x,  dUV.y), 0).r;
    float h2 = SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_LinearClamp, uv + float2(-dUV.x,  dUV.y), 0).r;
    float h3 = SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_LinearClamp, uv + float2( dUV.x, -dUV.y), 0).r;
    float h4 = SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_LinearClamp, uv + float2(-dUV.x, -dUV.y), 0).r;

    return (hCenter * 0.4 + (h1 + h2 + h3 + h4) * 0.15) * _InteractiveHeightScale;
}

FluidSurfaceSample EvaluateInteractiveSurface(float2 uv)
{
    FluidSurfaceSample s;
    if (_InteractiveHeightScale < 0.001)
    {
        s.height = 0.0;
        s.gradient = float2(0.0, 0.0);
        return s;
    }

    float2 dUV = _DisplacementMap_TexelSize.xy * 1.5;
    float hCenter = SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_LinearClamp, uv, 0).r;
    float h1 = SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_LinearClamp, uv + float2( dUV.x,  dUV.y), 0).r;
    float h2 = SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_LinearClamp, uv + float2(-dUV.x,  dUV.y), 0).r;
    float h3 = SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_LinearClamp, uv + float2( dUV.x, -dUV.y), 0).r;
    float h4 = SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_LinearClamp, uv + float2(-dUV.x, -dUV.y), 0).r;

    // Filtered height (center weight 0.4 + corner weights 0.15 each)
    s.height = (hCenter * 0.4 + (h1 + h2 + h3 + h4) * 0.15) * _InteractiveHeightScale;

    // Symmetrical diagonal central difference gradient from corner pairs
    float2 domainSize = GetFluidDomainSize();
    float worldDx = max(0.0001, domainSize.x * dUV.x);
    float worldDz = max(0.0001, domainSize.y * dUV.y);

    s.gradient.x = (((h1 + h3) - (h2 + h4)) * (0.25 * _InteractiveHeightScale)) / worldDx;
    s.gradient.y = (((h1 + h2) - (h3 + h4)) * (0.25 * _InteractiveHeightScale)) / worldDz;

    return s;
}

// Combined Analytical Surface Evaluation (Noise + RT)
FluidSurfaceSample EvaluateFluidSurface(float2 posOS_xz, float2 uv, float animTime)
{
    FluidNoiseSample noise = EvaluateFluidNoise(posOS_xz, animTime);
    FluidSurfaceSample rt = EvaluateInteractiveSurface(uv);

    FluidSurfaceSample surface;
    surface.height = noise.height + rt.height;
    surface.gradient = noise.gradient + rt.gradient;
    return surface;
}

// ============================================================================
// 6. Public Vertex Displacement API
// Shifts vertices along object-space Y based on procedural waves and simulation heightfield.
// ApplyFluidDisplacementOnly omits normal calculation for shadow and depth passes.
// ============================================================================

// 1. Fast Displacement Only: for ShadowCaster, DepthOnly, and Per-Pixel Normal (Sobel/ddx) passes
void ApplyFluidDisplacementOnly(inout float3 posOS, float2 uv)
{
    float animTime = GetFluidAnimationTime(_Time.y);
    float noiseH = SampleFluidNoiseHeightOnly(posOS.xz, animTime);
    float rtH = SampleInteractiveHeightOnly(uv);
    posOS.y += (noiseH + rtH);
}

// 2. Full Displacement with Analytical Normal: for Gouraud vertex normal passes
void ApplyFluidDisplacementWithNormal(inout float3 posOS, out float3 normalOS, float2 uv)
{
    float animTime = GetFluidAnimationTime(_Time.y);
    FluidSurfaceSample s = EvaluateFluidSurface(posOS.xz, uv, animTime);
    posOS.y += s.height;
    normalOS = normalize(float3(-s.gradient.x, 1.0, -s.gradient.y));
}

// 3. Backward Compatibility Wrapper: preserves existing calls in legacy shaders
void ApplyFluidDisplacement(inout float3 posOS, inout float3 normalOS, float2 uv)
{
    ApplyFluidDisplacementWithNormal(posOS, normalOS, uv);
}

// ============================================================================
// 7. Normal Reconstruction & Filtering
// ============================================================================

// Mode 0: 8-Tap Sobel Filter.
// 3x3 convolution kernel with [1, 2, 1] cross-smoothing.
// Suppresses high-frequency grid aliasing across discrete heightfield boundaries.
float3 CalculatePixelNormal(float2 uv, float2 posOS_xz)
{
    float dHdx_rt = 0.0;
    float dHdz_rt = 0.0;

    // Skip texture sampling when interactive simulation height is zero
    if (_InteractiveHeightScale > 0.001)
    {
        float2 texel = _DisplacementMap_TexelSize.xy * 1.5;

        // 8-tap Sobel stencil around uv
        float hL  = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_LinearClamp, uv - float2(texel.x, 0.0)).r;
        float hR  = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_LinearClamp, uv + float2(texel.x, 0.0)).r;
        float hB  = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_LinearClamp, uv - float2(0.0, texel.y)).r;
        float hT  = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_LinearClamp, uv + float2(0.0, texel.y)).r;

        float hBL = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_LinearClamp, uv + float2(-texel.x, -texel.y)).r;
        float hBR = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_LinearClamp, uv + float2( texel.x, -texel.y)).r;
        float hTL = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_LinearClamp, uv + float2(-texel.x,  texel.y)).r;
        float hTR = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_LinearClamp, uv + float2( texel.x,  texel.y)).r;

        // Weighted Sobel filter (suppresses high-frequency grid aliasing)
        // With sample offset d = domainSize * texel, the difference ((hTR + 2hR + hBR) - (hTL + 2hL + hBL)) * 0.125
        // evaluates to a * d for linear field H(x) = a * x.
        // Dividing by worldStepX = d (domainSize.x * texel.x) recovers the exact unbiased derivative a.
        float dX_rt = ((hTR + 2.0 * hR + hBR) - (hTL + 2.0 * hL + hBL)) * (0.125 * _InteractiveHeightScale);
        float dZ_rt = ((hTL + 2.0 * hT + hTR) - (hBL + 2.0 * hB + hBR)) * (0.125 * _InteractiveHeightScale);

        float2 domainSize = GetFluidDomainSize();
        float worldStepX = max(0.0001, domainSize.x * texel.x);
        float worldStepZ = max(0.0001, domainSize.y * texel.y);
        dHdx_rt = dX_rt / worldStepX;
        dHdz_rt = dZ_rt / worldStepZ;
    }

    // Analytical noise gradient (evaluated in 1 pass alongside height)
    float animTime = GetFluidAnimationTime(_Time.y);
    FluidNoiseSample noise = EvaluateFluidNoise(posOS_xz, animTime);

    float total_dHdx = noise.gradient.x + dHdx_rt;
    float total_dHdz = noise.gradient.y + dHdz_rt;

    float3 normalOS = normalize(float3(-total_dHdx, 1.0, -total_dHdz));
    return TransformObjectToWorldNormal(normalOS);
}

// Mode 1: 4-Tap Diagonal Filter.
// Evaluates central differences on 4 diagonal corner taps at 1.5 texel spacing.
// Normalizes gradients by domain dimensions and texel footprint to eliminate resolution bias.
float3 CalculateFourTapPixelNormal(float2 uv, float2 posOS_xz)
{
    float dHdx_rt = 0.0;
    float dHdz_rt = 0.0;

    if (_InteractiveHeightScale > 0.001)
    {
        float2 domainSize = GetFluidDomainSize();
        float2 dUV = _DisplacementMap_TexelSize.xy * 1.5;

        // 4 diagonal taps of the bilinear surface
        float h1 = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_LinearClamp, uv + float2( dUV.x,  dUV.y)).r;
        float h2 = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_LinearClamp, uv + float2(-dUV.x,  dUV.y)).r;
        float h3 = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_LinearClamp, uv + float2( dUV.x, -dUV.y)).r;
        float h4 = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_LinearClamp, uv + float2(-dUV.x, -dUV.y)).r;

        // Symmetric diagonal central difference normalized by metric separation in world units
        float worldStepX = max(0.0001, domainSize.x * 2.0 * dUV.x);
        float worldStepZ = max(0.0001, domainSize.y * 2.0 * dUV.y);

        float dX_rt = ((h1 + h3) - (h2 + h4)) * (0.5 * _InteractiveHeightScale);
        float dZ_rt = ((h1 + h2) - (h3 + h4)) * (0.5 * _InteractiveHeightScale);

        dHdx_rt = dX_rt / worldStepX;
        dHdz_rt = dZ_rt / worldStepZ;
    }

    // Analytical noise gradient (evaluated in 1 pass alongside height)
    float animTime = GetFluidAnimationTime(_Time.y);
    FluidNoiseSample noise = EvaluateFluidNoise(posOS_xz, animTime);

    float total_dHdx = noise.gradient.x + dHdx_rt;
    float total_dHdz = noise.gradient.y + dHdz_rt;

    float3 normalOS = normalize(float3(-total_dHdx, 1.0, -total_dHdz));
    return TransformObjectToWorldNormal(normalOS);
}

// Backward-compatibility alias
float3 CalculateParabolicPixelNormal(float2 uv, float2 posOS_xz)
{
    return CalculateFourTapPixelNormal(uv, posOS_xz);
}

// Normal resolver dispatcher:
// Mode 0: 8-tap Sobel (per-pixel smooth, suppresses grid noise)
// Mode 1: 4-tap Diagonal (per-pixel, 4 taps, resolution-independent metric radius)
// Mode 2: Gouraud Mesh Normal (interpolated vertex normals, 0 pixel texture taps)
// Mode 3: Screen-space partial derivatives (ddx/ddy, faceted geometric normals)
float3 ResolveFluidNormal(float normalMode, float3 normalWS, float3 positionWS, float2 uv, float2 posOS_xz)
{
    if (normalMode < 0.5)
    {
        return CalculatePixelNormal(uv, posOS_xz);
    }
    else if (normalMode < 1.5)
    {
        return CalculateFourTapPixelNormal(uv, posOS_xz);
    }
    else if (normalMode < 2.5)
    {
        return normalize(normalWS);
    }
    else
    {
        // Geometric screen-space normal derived from rasterized triangle derivatives (ddx/ddy of world position)
        float3 ddxPos = ddx(positionWS);
        float3 ddyPos = ddy(positionWS);
        return normalize(cross(ddyPos, ddxPos));
    }
}

// Backward-compatible overload
float3 ResolveFluidNormal(float normalMode, float3 normalWS, float3 positionWS, float2 uv)
{
    float2 domainSize = GetFluidDomainSize();
    float2 fallbackPosOS_xz = (uv - 0.5) * domainSize;
    return ResolveFluidNormal(normalMode, normalWS, positionWS, uv, fallbackPosOS_xz);
}

// State sampler helper (ARGBHalf): R = Height, G = Velocity, B = Pigment Concentration C, A = Captured Crest Height
float4 SampleFluidState(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_LinearClamp, uv, 0);
}

#endif // FLUID_DISPLACEMENT_CORE_INCLUDED
