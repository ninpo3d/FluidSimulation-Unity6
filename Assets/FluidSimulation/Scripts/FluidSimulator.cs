using System;
using UnityEngine;

namespace Flexus.FluidSimulation
{
    // Manages ping-pong render targets and simulation blit passes
    public class FluidSimulator : IDisposable
    {
        private static readonly int DisplacementMapId = Shader.PropertyToID("_DisplacementMap");
        private static readonly int HitDataId = Shader.PropertyToID("_HitData");
        private static readonly int PrevHitDataId = Shader.PropertyToID("_PrevHitData");
        private static readonly int BrushVelocityId = Shader.PropertyToID("_BrushVelocity");
        private static readonly int BrushRadiusId = Shader.PropertyToID("_BrushRadius");
        private static readonly int BrushStrengthId = Shader.PropertyToID("_BrushStrength");
        private static readonly int RimWidthFactorId = Shader.PropertyToID("_RimWidthFactor");
        private static readonly int RimHeightFactorId = Shader.PropertyToID("_RimHeightFactor");
        private static readonly int BowWaveIntensityId = Shader.PropertyToID("_BowWaveIntensity");
        private static readonly int TrailDecayId = Shader.PropertyToID("_TrailDecay");
        private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
        private static readonly int ViscosityId = Shader.PropertyToID("_Viscosity");
        private static readonly int PlasticityId = Shader.PropertyToID("_Plasticity");
        private static readonly int EffectiveSpringId = Shader.PropertyToID("_EffectiveSpring");
        private static readonly int EffectiveDampingId = Shader.PropertyToID("_EffectiveDamping");
        private static readonly int InteractiveHeightScaleId = Shader.PropertyToID("_InteractiveHeightScale");
        private static readonly int FluidDomainSizeOSId = Shader.PropertyToID("_FluidDomainSizeOS");
        private static readonly int PigmentVelocityMinId = Shader.PropertyToID("_PigmentVelocityMin");
        private static readonly int PigmentVelocityMaxId = Shader.PropertyToID("_PigmentVelocityMax");
        private static readonly int PigmentInjectionId = Shader.PropertyToID("_PigmentInjection");
        private static readonly int PigmentDiffusionId = Shader.PropertyToID("_PigmentDiffusion");
        private static readonly int PigmentDecayId = Shader.PropertyToID("_PigmentDecay");

        private Material _simMaterial;
        private RenderTexture _rtA;
        private RenderTexture _rtB;
        private bool _isPingPongA = true;
        private Vector2Int _resolution = new Vector2Int(128, 128);
        private float _oscillationDamping = 1.8f;
        private bool _isDisposed = false;

        public RenderTexture ActiveTexture => _isPingPongA ? _rtA : _rtB;
        public RenderTexture TextureA => _rtA;
        public RenderTexture TextureB => _rtB;
        public Vector2Int Resolution2D => _resolution;
        public int Resolution => Mathf.Max(_resolution.x, _resolution.y);
        public Material SimulationMaterial => _simMaterial;
        public bool IsInitialized => _simMaterial != null && _rtA != null && _rtB != null;

        public void Initialize(Shader simulationShader, Vector2Int resolution, Material targetPlaneMaterial)
        {
            if (simulationShader == null)
            {
                Debug.LogError("<color=red>[FluidSimulator]</color> Simulation shader is null!");
                return;
            }

            _isDisposed = false;

            if (_simMaterial == null)
            {
                _simMaterial = new Material(simulationShader);
            }

            _resolution = new Vector2Int(Mathf.Clamp(resolution.x, 32, 2048), Mathf.Clamp(resolution.y, 32, 2048));
            _isPingPongA = true;

            InitializeRenderTextures(targetPlaneMaterial);
        }

        public void Initialize(Shader simulationShader, int resolution, Material targetPlaneMaterial)
        {
            Initialize(simulationShader, new Vector2Int(resolution, resolution), targetPlaneMaterial);
        }

        private void InitializeRenderTextures(Material targetPlaneMaterial)
        {
            ReleaseRenderTextures();

            // ARGBHalf: R=Height, G=Velocity, B=Pigment, A=Captured Crest
            RenderTextureDescriptor desc = new RenderTextureDescriptor(_resolution.x, _resolution.y, RenderTextureFormat.ARGBHalf, 0)
            {
                sRGB = false,
                autoGenerateMips = false,
                useMipMap = false
            };

            _rtA = new RenderTexture(desc)
            {
                name = "Fluid_PingPong_A",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _rtA.Create();

            _rtB = new RenderTexture(desc)
            {
                name = "Fluid_PingPong_B",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _rtB.Create();

            ClearCanvas();

            if (targetPlaneMaterial != null)
            {
                targetPlaneMaterial.SetTexture(DisplacementMapId, _rtA);
            }
        }

        public void ClearCanvas()
        {
            if (_rtA != null && _rtB != null)
            {
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = _rtA;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = _rtB;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = prev;
            }
        }

        public void SetResolution(Vector2Int resolution, Material targetPlaneMaterial)
        {
            int clampedX = Mathf.Clamp(resolution.x, 32, 2048);
            int clampedY = Mathf.Clamp(resolution.y, 32, 2048);
            if (_resolution.x == clampedX && _resolution.y == clampedY && _rtA != null && _rtA.width == clampedX && _rtA.height == clampedY)
            {
                return;
            }

            _resolution = new Vector2Int(clampedX, clampedY);
            _isPingPongA = true;
            InitializeRenderTextures(targetPlaneMaterial);
        }

        public void SetResolution(int resolution, Material targetPlaneMaterial)
        {
            SetResolution(new Vector2Int(resolution, resolution), targetPlaneMaterial);
        }

        public void SetPhysicalParameters(
            float brushRadius,
            float brushStrength,
            float rimWidthFactor,
            float rimHeightFactor,
            float bowWaveIntensity,
            float oscillationDamping,
            float trailDecay,
            float viscosity,
            float plasticity,
            float effectiveSpring)
        {
            _oscillationDamping = oscillationDamping;

            if (_simMaterial == null) return;

            _simMaterial.SetFloat(BrushRadiusId, brushRadius);
            _simMaterial.SetFloat(BrushStrengthId, brushStrength);
            _simMaterial.SetFloat(RimWidthFactorId, rimWidthFactor);
            _simMaterial.SetFloat(RimHeightFactorId, rimHeightFactor);
            _simMaterial.SetFloat(BowWaveIntensityId, bowWaveIntensity);
            _simMaterial.SetFloat(TrailDecayId, trailDecay);
            _simMaterial.SetFloat(ViscosityId, viscosity);
            _simMaterial.SetFloat(PlasticityId, plasticity);
            _simMaterial.SetFloat(EffectiveSpringId, effectiveSpring);
        }

        public void SetPigmentParameters(
            float velocityMin,
            float velocityMax,
            float injection,
            float diffusion,
            float decay)
        {
            if (_simMaterial == null) return;

            _simMaterial.SetFloat(PigmentVelocityMinId, velocityMin);
            _simMaterial.SetFloat(PigmentVelocityMaxId, velocityMax);
            _simMaterial.SetFloat(PigmentInjectionId, injection);
            _simMaterial.SetFloat(PigmentDiffusionId, diffusion);
            _simMaterial.SetFloat(PigmentDecayId, decay);
        }

        public void SetDomainSize(Vector2 domainSize)
        {
            if (_simMaterial == null) return;
            float sizeX = Mathf.Max(0.5f, domainSize.x);
            float sizeZ = Mathf.Max(0.5f, domainSize.y);
            _simMaterial.SetVector(FluidDomainSizeOSId, new Vector4(sizeX, sizeZ, 1.0f / sizeX, 1.0f / sizeZ));
        }

        public void ExecutePingPongBlit(
            Vector4 hitData,
            Vector4 prevHitData,
            Vector4 brushVelocityData,
            float dt,
            Material targetPlaneMaterial,
            float interactiveHeightScale,
            int iterations = 1)
        {
            if (iterations <= 0) return;

            int loopCount = Mathf.Max(1, iterations);
            float substepDt = Mathf.Clamp(dt / loopCount, 0.00005f, 0.04f);
            float effectiveDamping = Mathf.Exp(-_oscillationDamping * substepDt);

            _simMaterial.SetVector(HitDataId, hitData);
            _simMaterial.SetVector(PrevHitDataId, prevHitData);
            _simMaterial.SetVector(BrushVelocityId, brushVelocityData);
            _simMaterial.SetFloat(DeltaTimeId, substepDt);
            _simMaterial.SetFloat(EffectiveDampingId, effectiveDamping);

            RenderTexture destination = null;

            for (int i = 0; i < iterations; i++)
            {
                RenderTexture source = _isPingPongA ? _rtA : _rtB;
                destination = _isPingPongA ? _rtB : _rtA;

                Graphics.Blit(source, destination, _simMaterial);
                _isPingPongA = !_isPingPongA;
            }

            if (targetPlaneMaterial != null && destination != null)
            {
                targetPlaneMaterial.SetTexture(DisplacementMapId, destination);
            }
        }

        private void ReleaseRenderTextures()
        {
            if (_rtA != null)
            {
                _rtA.Release();
                if (Application.isPlaying) UnityEngine.Object.Destroy(_rtA);
                else UnityEngine.Object.DestroyImmediate(_rtA);
                _rtA = null;
            }

            if (_rtB != null)
            {
                _rtB.Release();
                if (Application.isPlaying) UnityEngine.Object.Destroy(_rtB);
                else UnityEngine.Object.DestroyImmediate(_rtB);
                _rtB = null;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            ReleaseRenderTextures();

            if (_simMaterial != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_simMaterial);
                else UnityEngine.Object.DestroyImmediate(_simMaterial);
                _simMaterial = null;
            }
        }
    }
}
