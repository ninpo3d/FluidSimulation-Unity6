// ============================================================================
// Flexus Fluid Simulation - Displacement & Waves Input Declarations
// Macro definitions, texture bindings, and samplers for modular fluid shaders
// ============================================================================

#ifndef FLUID_DISPLACEMENT_INPUT_INCLUDED
#define FLUID_DISPLACEMENT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Shared material declarations required by the fluid displacement core.
// Declare this macro inside the consuming shader's UnityPerMaterial CBUFFER
// to keep these material properties SRP Batcher compatible.
#define FLUID_DISPLACEMENT_UNIFORMS \
    float _NoiseScale; \
    float _NoiseSpeed; \
    float _NoiseAmplitude; \
    float _NoiseOctaves; \
    float _NoiseTime; \
    float _Viscosity; \
    float _NoiseType; \
    float _WarpStrength; \
    float _NormalMode; \
    float _DebugNormals; \
    float4 _FluidDomainSizeOS; \
    float4 _DisplacementMap_TexelSize; \
    float _InteractiveHeightScale;

// Textures & Samplers
TEXTURE2D(_DisplacementMap);
// Note: sampler_LinearClamp is provided globally by URP (Core.hlsl -> GlobalSamplers.hlsl)

#endif // FLUID_DISPLACEMENT_INPUT_INCLUDED
