Shader "Flexus/FluidSimulationBlit"
{
    Properties
    {
        _MainTex ("Previous State", 2D) = "black" {}
        _HitData ("Hit UV (xy), Active (z), Release (w)", Vector) = (-1, -1, 0, 0)
        _PrevHitData ("Prev Hit UV (xy), WasActive (z), Unused (w)", Vector) = (-1, -1, 0, 0)
        _BrushVelocity ("Brush Velocity (dir.xy, speed, unused)", Vector) = (0, 0, 0, 0)
        _BrushRadius ("Brush Radius", Range(0.10, 3.00)) = 0.70
        _BrushStrength ("Brush Strength", Range(0.05, 1.5)) = 0.35
        _RimWidthFactor ("Rim Width Factor", Range(0.2, 1.2)) = 0.55
        _RimHeightFactor ("Rim Height Factor", Range(0.1, 1.0)) = 0.50
        _BowWaveIntensity ("Bow Wave Push Factor", Range(0.0, 5.0)) = 2.0
        _TrailDecay ("Trail Viscous Decay Rate", Range(0.0, 1.0)) = 0.0
        _DeltaTime ("Delta Time", Float) = 0.016
        _Viscosity ("Viscosity", Range(0.0, 1.0)) = 0.85
        _Plasticity ("Plasticity", Range(0.0, 1.0)) = 1.0
        _EffectiveSpring ("Spring Stiffness", Float) = 35.0
        _EffectiveDamping ("Damping Factor", Float) = 0.96
        _FluidDomainSizeOS ("Domain Size (OS)", Vector) = (10.0, 10.0, 0.1, 0.1)

        _PigmentVelocityMin ("Pigment Velocity Min", Float) = 0.08
        _PigmentVelocityMax ("Pigment Velocity Max", Float) = 1.8
        _PigmentInjection ("Pigment Injection Strength", Float) = 4.5
        _PigmentDiffusion ("Pigment Diffusion Rate", Float) = 1.8
        _PigmentDecay ("Pigment Decay Rate", Float) = 0.75
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "SimulationPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _HitData;
                float4 _PrevHitData;
                float4 _BrushVelocity;
                float _BrushRadius;
                float _BrushStrength;
                float _RimWidthFactor;
                float _RimHeightFactor;
                float _BowWaveIntensity;
                float _TrailDecay;
                float _DeltaTime;
                float _Viscosity;
                float _Plasticity;
                float _EffectiveSpring;
                float _EffectiveDamping;
                float4 _FluidDomainSizeOS;

                float _PigmentVelocityMin;
                float _PigmentVelocityMax;
                float _PigmentInjection;
                float _PigmentDiffusion;
                float _PigmentDecay;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float C2Smooth(float t)
            {
                t = saturate(t);
                return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 state = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float height = state.r;
                float velocity = state.g;
                float prevC = state.b;
                float prevCapturedH = state.a;
                float prevHeight = height;

                float dt = clamp(_DeltaTime, 0.001, 0.04);
                bool isInteracting = _HitData.z > 0.5;
                bool wasInteracting = _PrevHitData.z > 0.5;
                bool isReleaseFrame = _HitData.w > 0.5;

                float2 currUV = _HitData.xy;
                float2 prevUV = wasInteracting ? _PrevHitData.xy : currUV;
                float2 domainSize = _FluidDomainSizeOS.xy > 0.01 ? _FluidDomainSizeOS.xy : float2(10.0, 10.0);

                float R = max(0.05, _BrushRadius);
                float footprint = 0.0;

                // Continuous capsule stroke
                if (isInteracting)
                {
                    float2 pa = (input.uv - prevUV) * domainSize;
                    float2 ba = (currUV - prevUV) * domainSize;
                    float segLenSq = dot(ba, ba);

                    float2 closestUV;
                    if (segLenSq > 1e-7)
                    {
                        float t = saturate(dot(pa, ba) / segLenSq);
                        closestUV = prevUV + (currUV - prevUV) * t;
                    }
                    else
                    {
                        closestUV = currUV;
                    }

                    float2 toClosest = (input.uv - closestUV) * domainSize;
                    float distSq = dot(toClosest, toClosest);

                    float2 toTip = (input.uv - currUV) * domainSize;
                    float distTipSq = dot(toTip, toTip);

                    float rimWidth = max(0.12, _RimWidthFactor);
                    float rimEnd = 1.0 + rimWidth;
                    float maxRadius = R * rimEnd;
                    float maxRadiusSq = maxRadius * maxRadius;
                    float bowMaxRadiusSq = (R * 1.9) * (R * 1.9);

                    if (distSq < maxRadiusSq || distTipSq < bowMaxRadiusSq)
                    {
                        float dist = sqrt(distSq);
                        float r = dist / R;
                        float distTip = sqrt(distTipSq);
                        float2 dirFromTip = distTip > 1e-5 ? (toTip / distTip) : float2(0.0, 0.0);

                        float forwardAlign = dot(dirFromTip, _BrushVelocity.xy);
                        float speedFactor = saturate(_BrushVelocity.z * 0.35);
                        float forwardFactor = max(0.0, forwardAlign);
                        float directionalLobe = pow(forwardFactor, 1.2);

                        // Directional bow wave
                        float bowWave = 0.0;
                        float bowEnvelope = 0.0;
                        float r_bow = distTip / R;
                        if (directionalLobe > 0.02 && speedFactor > 0.02 && r_bow > 0.20 && r_bow < 1.9 && _Viscosity < 0.99)
                        {
                            float bowRise = C2Smooth(saturate((r_bow - 0.20) / 0.65));
                            float bowFall = 1.0 - C2Smooth(saturate((r_bow - 0.85) / 1.05));
                            bowEnvelope = directionalLobe * speedFactor * bowRise * bowFall;
                            bowWave = _BrushStrength * _BowWaveIntensity * bowEnvelope * 0.50;
                        }

                        // Surface depression under brush
                        if (r < 1.0)
                        {
                            footprint = 1.0;
                            float dent = -_BrushStrength * (1.0 - C2Smooth(r));
                            if (dent < -0.001)
                            {
                                height = min(height, dent);
                            }
                        }
                        // Displaced rim along furrow flanks
                        else if (r < rimEnd)
                        {
                            float r_rim = (r - 1.0) / max(0.01, rimWidth);
                            footprint = 1.0 - C2Smooth(r_rim);

                            float rise = C2Smooth(saturate(r_rim / 0.35));
                            float fall = 1.0 - C2Smooth(saturate((r_rim - 0.35) / 0.65));
                            float rim = _BrushStrength * _RimHeightFactor * rise * fall;

                            if (height > -0.05)
                            {
                                height = max(height, rim);
                            }
                        }

                        if (bowEnvelope > 0.001)
                        {
                            footprint = max(footprint, bowEnvelope);
                        }

                        if (bowWave > 0.001)
                        {
                            height = max(height, bowWave);
                        }

                        if (_Plasticity < 0.95 && _Viscosity < 0.99 && bowEnvelope > 0.001)
                        {
                            float momentumStrength = lerp(20.0, 2.0, _Viscosity);
                            velocity += bowEnvelope * _BowWaveIntensity * momentumStrength * dt;
                        }
                    }
                }

                // Stroke release impulse
                if (isReleaseFrame && _Plasticity < 0.95 && _Viscosity < 0.99)
                {
                    float2 toCurr = (input.uv - currUV) * domainSize;
                    float releaseDistSq = dot(toCurr, toCurr);
                    float maxReleaseR = R * 1.3;
                    if (releaseDistSq < maxReleaseR * maxReleaseR)
                    {
                        float releaseDist = sqrt(releaseDistSq);
                        float releaseR = releaseDist / maxReleaseR;
                        float popFactor = 1.0 - C2Smooth(releaseR);
                        float popStrength = lerp(0.3, 0.05, _Viscosity) * _BrushStrength;
                        height += popFactor * popStrength * (1.0 - _Plasticity);
                        velocity += popFactor * popStrength * 8.0 * (1.0 - _Plasticity);
                    }
                }

                if (footprint > 0.001)
                {
                    float deformRate = (height - prevHeight) / max(dt, 0.001);
                    velocity = lerp(velocity, deformRate, saturate(15.0 * dt));
                }

                // Physics integration
                if (_Plasticity < 0.95 && _Viscosity < 0.99)
                {
                    velocity = (velocity - _EffectiveSpring * height * dt) * _EffectiveDamping;
                    height += velocity * dt;

                    if (_TrailDecay > 0.0)
                    {
                        height *= exp(-_TrailDecay * dt);
                        velocity *= exp(-_TrailDecay * dt);
                    }
                }
                else
                {
                    if (_TrailDecay > 0.0)
                    {
                        height *= exp(-_TrailDecay * dt);
                    }
                    velocity *= exp(-max(12.0, _TrailDecay * 5.0) * dt);
                }

                height = clamp(height, -2.5, 2.5);
                velocity = clamp(velocity, -50.0, 50.0);

                // 2D 4-neighbor Laplacian stencil for pigment diffusion
                float4 sN = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(0.0, _MainTex_TexelSize.y));
                float4 sS = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(0.0, _MainTex_TexelSize.y));
                float4 sE = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(_MainTex_TexelSize.x, 0.0));
                float4 sW = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(_MainTex_TexelSize.x, 0.0));

                float laplacianC = sN.b + sS.b + sE.b + sW.b - 4.0 * prevC;
                float laplacianH = sN.a + sS.a + sE.a + sW.a - 4.0 * prevCapturedH;

                float diffusion = min(_PigmentDiffusion * dt, 0.24);
                float diffusedC = prevC + diffusion * laplacianC;
                float diffusedH = prevCapturedH + diffusion * laplacianH;

                float decayedC = diffusedC * exp(-_PigmentDecay * dt);
                float decayedH = diffusedH * exp(-_PigmentDecay * dt);

                float speed = abs(velocity);
                float velMin = max(0.001, _PigmentVelocityMin);
                float velMax = max(velMin + 0.01, _PigmentVelocityMax);
                float excitation = smoothstep(velMin, velMax, speed);

                float strokeElevation = max(height, footprint * _BrushStrength * 1.5);
                float targetH = max(strokeElevation, 0.0);

                float totalExcitation = max(excitation, saturate(footprint * 3.0));
                float sourceC = totalExcitation * _PigmentInjection * dt;
                float newC = saturate(decayedC + sourceC);

                float captureBlendRate = saturate(totalExcitation * 20.0 * dt);
                float newCapturedH = lerp(decayedH, max(decayedH, targetH), captureBlendRate);

                return half4(height, velocity, newC, newCapturedH);
            }
            ENDHLSL
        }
    }
}
