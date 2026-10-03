Shader "Flexus/FluidDisplacement"
{
    Properties
    {
        [Header(Ambient Noise Waves)]
        _NoiseScale ("Noise Scale / Frequency", Range(0.05, 5.0)) = 0.5
        _NoiseSpeed ("Noise Speed", Range(0.0, 5.0)) = 0.8
        _NoiseAmplitude ("Noise Amplitude / Height", Range(0.0, 2.5)) = 0.0
        _NoiseOctaves ("Noise Octaves (FBM)", Range(1, 3)) = 2
        _NoiseTime ("Noise Time (Internal)", Float) = 0.0
        _Viscosity ("Fluid Viscosity", Range(0.0, 1.0)) = 0.5
        _NoiseType ("Noise Algorithm: 0=Classic Simplex, 1=Basin Slosh", Float) = 1.0
        _WarpStrength ("Slosh Surge / Tilt", Range(0.0, 1.0)) = 0.35

        [Header(Interactive Simulation Displacements)]
        [NoScaleOffset] [HideInInspector] _DisplacementMap ("Interactive Displacement RT", 2D) = "black" {}
        _InteractiveHeightScale ("Interactive Height Scale", Range(0.0, 3.0)) = 1.0
        _FluidDomainSizeOS ("Fluid Domain Size (OS: size.xy, invSize.zw)", Vector) = (10.0, 10.0, 0.1, 0.1)

        [Header(Normal Reconstruction Mode)]
        [Enum(Sobel_8Tap, 0, FourTap_Diagonal, 1, Mesh_Gouraud, 2, Flat_ScreenSpace, 3)] _NormalMode ("Normal Mode", Float) = 1
        [Toggle] _DebugNormals ("Visualize Normals as Color", Float) = 0.0

        [Header(Color Ramp and Bounding Box)]
        [NoScaleOffset] _RampMap ("Color Ramp Texture (1D)", 2D) = "black" {}
        _MinHeight ("Bounding Box Min Height (Trough)", Range(-3.0, 1.0)) = -0.5
        _MaxHeight ("Bounding Box Max Height (Crest)", Range(-0.5, 3.0)) = 1.0
        _FresnelPower ("Fresnel Exponent", Range(0.1, 8.0)) = 2.2
        _FresnelBias ("Fresnel Bias", Range(-0.5, 0.5)) = 0.0
        _FresnelIntensity ("Fresnel Intensity", Range(0.0, 3.0)) = 1.0
        _WaveColorModulation ("Wave Crest Glow", Range(0.0, 2.0)) = 0.6

        [Header(Kinetic Stroke Color Dynamics)]
        _ColorBlendMode ("Color Blend Mode", Float) = 4
        _PigmentDensity ("Pigment Optical Density", Range(0.1, 15.0)) = 8.0
        _EnableOpticalAbsorption ("Beer-Lambert Optical Absorption", Float) = 1.0
        _OpticalDepthScale ("Optical Depth Scale", Range(0.1, 10.0)) = 5.0
        _OpticalDepthFromHeight ("Depth Influence (Trough Thicker)", Range(0.0, 3.0)) = 0.18
        _MinVelocity ("Min Velocity (Debug Ramp 0.0)", Range(0.0, 30.0)) = 0.0
        _MaxVelocity ("Max Velocity (Debug Ramp 1.0)", Range(0.01, 50.0)) = 1.0

        [Header(Color Component Debug Modes)]
        _ColorDebugMode ("Color Debug Mode", Float) = 5

        [Header(PBR Surface Properties)]
        _Roughness ("Roughness / Softness", Range(0.01, 1.0)) = 0.08
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.25
        _DiffuseIntensity ("Diffuse Intensity", Range(0.0, 2.0)) = 0.7

        [Header(Specular Highlights)]
        [HDR] _SpecularColor ("Specular Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _SpecularIntensity ("Specular Intensity", Range(0.0, 5.0)) = 1.0

        [Header(Cubemap and Reflections)]
        [Toggle] _UseCustomCubemap ("Use Custom Cubemap", Float) = 0.0
        [NoScaleOffset] _CustomCubemap ("Custom Cubemap", Cube) = "_Skybox" {}
        _ReflectionIntensity ("Reflection Intensity", Range(0.0, 5.0)) = 1.2
        _ReflectionTint ("Reflection Tint", Color) = (1.0, 1.0, 1.0, 1.0)

        [Header(Glow and Emission)]
        [HDR] _EmissionColor ("Emission Tint", Color) = (0.1, 0.02, 0.15, 1.0)
        _EmissionIntensity ("Emission Intensity", Range(0.0, 5.0)) = 0.2
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
            "Queue" = "Geometry" 
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
        #include "FluidDisplacementInput.hlsl"

        // Textures & Samplers for Chameleon Surface
        TEXTURE2D(_RampMap);
        SAMPLER(sampler_RampMap);

        TEXTURECUBE(_CustomCubemap);
        SAMPLER(sampler_CustomCubemap);

        CBUFFER_START(UnityPerMaterial)
            // Fluid displacement properties imported via module macro (100% SRP Batcher compatible)
            FLUID_DISPLACEMENT_UNIFORMS

            // Surface color & ramp properties
            float _MinHeight;
            float _MaxHeight;
            float _FresnelPower;
            float _FresnelBias;
            float _FresnelIntensity;
            float _WaveColorModulation;

            // Kinetic Stroke Color Dynamics
            float _ColorBlendMode;
            float _PigmentDensity;
            float _EnableOpticalAbsorption;
            float _OpticalDepthScale;
            float _OpticalDepthFromHeight;
            float _MinVelocity;
            float _MaxVelocity;

            // Color Component Debug Modes
            float _ColorDebugMode;

            // PBR Surface Properties
            float _Roughness;
            float _Metallic;
            float _DiffuseIntensity;
            float4 _SpecularColor;
            float _SpecularIntensity;
            float _UseCustomCubemap;
            float4 _ReflectionTint;
            float _ReflectionIntensity;
            float4 _EmissionColor;
            float _EmissionIntensity;
        CBUFFER_END

        #define FLUID_DISPLACEMENT_UNIFORMS_DECLARED
        #include "FluidDisplacementCore.hlsl"
        ENDHLSL

        // ========================================================
        // Pass: Forward Lit
        // ========================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float2 uv           : TEXCOORD2;
                float  height       : TEXCOORD3;
                float2 posOS_xz     : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                // Capture initial flat mesh height and object-space horizontal coordinates
                float origY = input.positionOS.y;
                output.posOS_xz = input.positionOS.xz;

                // Compute analytical vertex normals only when Gouraud mode is active (Mode 2)
                // For pixel-stage normal modes (Sobel, 4-Tap Diagonal, Screen-space), skip vertex normal calculation
                if (_NormalMode > 1.5 && _NormalMode < 2.5)
                {
                    ApplyFluidDisplacementWithNormal(input.positionOS.xyz, input.normalOS, input.uv);
                }
                else
                {
                    ApplyFluidDisplacementOnly(input.positionOS.xyz, input.uv);
                }

                // Relative displacement height (used below for height-based color mapping)
                output.height = input.positionOS.y - origY;

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.uv = input.uv;

                return output;
            }

            // Blends base color A (Height Map Ramp) with motion color B (Velocity Ramp)
            // according to blend mode [0..6] with blend weight w [0..1]
            half3 ApplyColorBlend(half3 A, half3 B, float w, float mode)
            {
                w = saturate(w);
                if (w < 0.001) return A;

                half3 blended = A;

                if (mode < 0.5)
                {
                    // 0: Normal (Standard Alpha Lerp)
                    blended = lerp(A, B, w);
                }
                else if (mode < 1.5)
                {
                    // 1: Additive (Linear Dodge - energetic luminous glow)
                    blended = saturate(A + B * w);
                }
                else if (mode < 2.5)
                {
                    // 2: Multiply (Rich deep pigment fusion)
                    blended = lerp(A, A * B, w);
                }
                else if (mode < 3.5)
                {
                    // 3: Screen (Luminous bright non-clipping blend)
                    blended = lerp(A, 1.0 - (1.0 - A) * (1.0 - B), w);
                }
                else if (mode < 4.5)
                {
                    // 4: Overlay (Combines Multiply on darks & Screen on lights)
                    half3 overlayColor = (A < 0.5) ? (2.0 * A * B) : (1.0 - 2.0 * (1.0 - A) * (1.0 - B));
                    blended = lerp(A, saturate(overlayColor), w);
                }
                else if (mode < 5.5)
                {
                    // 5: Soft Light (Gentle photographic tonal wash)
                    half3 softLightColor = (1.0 - 2.0 * B) * (A * A) + 2.0 * B * A;
                    blended = lerp(A, saturate(softLightColor), w);
                }
                else
                {
                    // 6: Color Dodge (Radiant high-contrast highlights)
                    half3 dodgeColor = saturate(A / max(0.01, 1.0 - B));
                    blended = lerp(A, dodgeColor, w);
                }

                return blended;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 1. Fetch simulation state & reconstruct surface normal:
                // Channel R = Wave height, Channel G = Wave velocity, Channel B = Kinetic Pigment Concentration C, Channel A = Captured Stroke Height
                float4 rtState = SampleFluidState(input.uv);
                float waveHeight = rtState.r * _InteractiveHeightScale;
                float waveVelocity = rtState.g;
                float pigmentC = rtState.b;
                float capturedH = rtState.a * _InteractiveHeightScale;

                // Reconstruct pixel normal (e.g. 4-tap Diagonal or 8-tap Sobel)
                float3 N = ResolveFluidNormal(_NormalMode, input.normalWS, input.positionWS, input.uv, input.posOS_xz);

                // Normal visualizer debug mode (sanity check for slope directions or Mode 6)
                if (_DebugNormals > 0.5 || (_ColorDebugMode > 5.5 && _ColorDebugMode < 6.5))
                {
                    return half4(N * 0.5 + 0.5, 1.0);
                }

                // Camera view vector
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
                float NdotV = saturate(dot(N, V));

                // Bounding box height normalization [MinHeight -> MaxHeight]
                float heightRange = max(0.001, _MaxHeight - _MinHeight);
                float heightNorm = saturate((input.height - _MinHeight) / heightRange);

                // Wave momentum & kinetic energy (boosts crest highlights on fast ripples)
                float waveEnergy = saturate(abs(waveHeight) * 0.8 + abs(waveVelocity) * 0.05);

                // Fresnel grazing factor
                float effectiveFresnelPower = max(0.1, _FresnelPower - waveEnergy * _WaveColorModulation * 0.8);
                float fresnelFactor = saturate(_FresnelBias + pow(1.0 - NdotV, effectiveFresnelPower) * _FresnelIntensity);

                // ========================================================
                // Unified Height-Ramp & Kinetic Pigment Optics:
                // (h, v) -> (C, h_captured) -> Ramp(h_captured) -[Beer-Lambert]-> BodyColor -> SurfaceOptics
                // ========================================================

                // 1. Level 1 Base Fluid Color:
                // Height-mapped depth color combined with camera angle Fresnel iridescence (Level 1 requirement)
                half3 heightColor = SAMPLE_TEXTURE2D_LOD(_RampMap, sampler_RampMap, float2(heightNorm, 0.5), 0).rgb;
                half3 fresnelColor = SAMPLE_TEXTURE2D_LOD(_RampMap, sampler_RampMap, float2(fresnelFactor, 0.5), 0).rgb;
                half3 baseFluidColor = lerp(heightColor, fresnelColor, fresnelFactor * 0.85);

                // 2. Captured High-Water Stroke Color sampled from the EXACT SAME RAMP:
                // When the brush passed or wave crested, it reached capturedH. We sample the ramp at capturedH:
                float capturedNorm = saturate((capturedH - _MinHeight) / heightRange);
                half3 strokeColor = SAMPLE_TEXTURE2D_LOD(_RampMap, sampler_RampMap, float2(capturedNorm, 0.5), 0).rgb;

                // 3. Optical Absorption & Transmittance (Beer-Lambert Law):
                float blendWeight = pigmentC;
                if (_EnableOpticalAbsorption > 0.5)
                {
                    // Signed optical depth path (troughs are deeper/thicker, crests are thinner)
                    float depth = saturate(-waveHeight * _OpticalDepthScale);
                    float opticalPath = 1.0 + depth * _OpticalDepthFromHeight;

                    // Beer-Lambert transmittance: T = exp(-sigma * C * L)
                    float opticalDensity = _PigmentDensity * pigmentC * opticalPath;
                    float transmittance = exp(-opticalDensity);
                    blendWeight = 1.0 - transmittance;
                }

                // Blend base fluid color with captured stroke color using the selected blend mode:
                // 0: Normal, 1: Additive, 2: Multiply, 3: Screen, 4: Overlay, 5: Soft Light, 6: Color Dodge
                half3 bodyColor = ApplyColorBlend(baseFluidColor, strokeColor, blendWeight, _ColorBlendMode);

                // ========================================================
                // Color Component Debug Modes (0 to 8)
                // 0: Debug A - Height Map (0.5 = Rest, 1.0 = Crest, 0.0 = Trough)
                // 1: Debug B - Velocity Magnitude (|v| Grayscale 0..1)
                // 2: Debug C - Signed Velocity (v Grayscale: 0.5 = Rest, 1.0 = Up, 0.0 = Down)
                // 3: Signed Velocity -> Ramp (0=Down, 0.5=Rest, 1=Up, Black in Calm)
                // 4: Speed [0..1] -> Active Pigment
                // 5: Final PBR Composition (Base + Specular + Cubemap Reflection + Emission)
                // 6: Surface Normals
                // 7: Unlit Body Color (Pure Base + Pigment Optical Blend without PBR)
                // 8: Pigment Concentration C (Grayscale: 0 = Black, 1 = White)
                // ========================================================
                if (_ColorDebugMode < 0.5)
                {
                    // 0: Debug A - Height
                    float hVis = saturate(waveHeight * 0.5 + 0.5);
                    return half4(hVis, hVis, hVis, 1.0);
                }
                else if (_ColorDebugMode < 1.5)
                {
                    // 1: Debug B - Velocity Magnitude (|v| Grayscale)
                    float velSat = max(0.1, _MaxVelocity);
                    float speed01 = saturate(abs(waveVelocity) / velSat);
                    return half4(speed01, speed01, speed01, 1.0);
                }
                else if (_ColorDebugMode < 2.5)
                {
                    // 2: Debug C - Signed Velocity (v Grayscale)
                    float velRange = max(0.001, _MaxVelocity - _MinVelocity);
                    float signedV01 = saturate((waveVelocity - _MinVelocity) / velRange);
                    return half4(signedV01, signedV01, signedV01, 1.0);
                }
                else if (_ColorDebugMode < 3.5)
                {
                    // 3: Signed Velocity -> Ramp
                    float velRange = max(0.001, _MaxVelocity - _MinVelocity);
                    float signedV01 = saturate((waveVelocity - _MinVelocity) / velRange);
                    half3 signedRamp = SAMPLE_TEXTURE2D_LOD(_RampMap, sampler_RampMap, float2(signedV01, 0.5), 0).rgb * pigmentC;
                    return half4(signedRamp, 1.0);
                }
                else if (_ColorDebugMode < 4.5)
                {
                    // 4: Captured Stroke Ramp Color * Concentration
                    return half4(strokeColor * pigmentC, 1.0);
                }
                else if (_ColorDebugMode > 6.5 && _ColorDebugMode < 7.5)
                {
                    // 7: Unlit Body Color (Pure Base + Pigment Optical Blend without PBR)
                    return half4(bodyColor, 1.0);
                }
                else if (_ColorDebugMode > 7.5 && _ColorDebugMode < 8.5)
                {
                    // 8: Debug Pigment Concentration C (Grayscale: 0 = Black, 1 = White)
                    float cVis = saturate(pigmentC);
                    return half4(cVis, cVis, cVis, 1.0);
                }

                // ========================================================
                // Step 5: Final Shading Composition (Physical PBR Surface Optics)
                // ========================================================
                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float NdotL = saturate(dot(N, L));

                // 1. Diffuse shading on the internal body color (suppressed on high metallic)
                float diffuseFactor = (1.0 - _Metallic) * _DiffuseIntensity;
                float3 diffuse = bodyColor * (NdotL * mainLight.color * diffuseFactor + SampleSH(N) * diffuseFactor);

                // 2. Specular highlights (roughness-controlled normalized specular approximation):
                // Energy-conserving Blinn-Phong parameterized to match GGX microfacet width (Walter et al. 2007)
                // Perceptual roughness parameterization: alpha = roughness^2
                float3 H = normalize(L + V);
                float NdotH = saturate(dot(N, H));
                float perceptualRoughness = clamp(_Roughness, 0.02, 1.0);

                // Curvature AA: prevents specular shimmering/fireflies on steep wave ripples
                float normalVariance = length(fwidth(N));
                float effectiveRoughness = clamp(perceptualRoughness + normalVariance * 0.25, 0.02, 1.0);

                float alpha = effectiveRoughness * effectiveRoughness;
                float alpha2 = max(alpha * alpha, 0.0001);
                float specPower = clamp(2.0 / alpha2 - 2.0, 1.0, 4096.0);

                // Energy conservation term (specPower + 8) / (32 * PI) avoids HDR blowout
                float specularNormalization = (specPower + 8.0) / (32.0 * PI);
                float specTerm = pow(NdotH, specPower) * specularNormalization;
                
                // Metal tints specular with body color; dielectrics stay pure white
                float3 specBaseTint = lerp(_SpecularColor.rgb, _SpecularColor.rgb * bodyColor, _Metallic);
                float3 specularHighlight = specBaseTint * (specTerm * _SpecularIntensity * NdotL * mainLight.color);

                // 3. Environment reflections (Reflection Probe or custom cubemap with roughness-derived mip level):
                float3 R = reflect(-V, N);
                half3 envSample = half3(0, 0, 0);
                if (_UseCustomCubemap > 0.5)
                {
                    half mipLevel = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
                    envSample = SAMPLE_TEXTURECUBE_LOD(_CustomCubemap, sampler_CustomCubemap, R, mipLevel).rgb;
                }
                else
                {
                    envSample = GlossyEnvironmentReflection(R, input.positionWS, perceptualRoughness, 1.0);
                }

                // Specular F0: dielectric 0.04 scaled by specular tint, conductor albedo scaled by specular tint
                float3 F0 = lerp(float3(0.04, 0.04, 0.04) * _SpecularColor.rgb, bodyColor * _SpecularColor.rgb, _Metallic);
                float3 fresnelReflect = lerp(F0, _SpecularColor.rgb, fresnelFactor);
                float3 reflection = envSample * fresnelReflect * _ReflectionTint.rgb * (_ReflectionIntensity * _SpecularIntensity);

                // 4. Emission / Glow (energized by wave movement)
                float3 emission = _EmissionColor.rgb * (_EmissionIntensity * (0.15 + fresnelFactor * 0.85 + waveEnergy * _WaveColorModulation));

                // Final color composition
                float3 finalColor = diffuse + specularHighlight + reflection + emission;

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // ========================================================
        // Pass: Shadow Caster (Accounts for vertex wave height)
        // Evaluates surface displacement on shadow caster geometry to align shadow maps with wave peaks
        // ========================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct ShadowAttributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
            };

            struct ShadowVaryings
            {
                float4 positionCS   : SV_POSITION;
            };

            ShadowVaryings ShadowPassVertex(ShadowAttributes input)
            {
                ShadowVaryings output;

                // Fast displacement only: skips normal/gradient ALU since shadow pass only needs depth
                ApplyFluidDisplacementOnly(input.positionOS.xyz, input.uv);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _MainLightPosition.xyz));
                return output;
            }

            half4 ShadowPassFragment(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    CustomEditor "Flexus.FluidSimulation.Editor.FluidMaterialEditor"
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
