using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Flexus.FluidSimulation
{
    public enum FluidSimulationMode
    {
        Standard = 0,   // Standard Mode: Interactive ripples & surface physics
        Waves = 1       // Waves Mode: Combined ambient fluid waves + interactive ripples
    }

    public enum FluidColorBlendMode
    {
        Normal = 0,
        Additive = 1,
        Multiply = 2,
        Screen = 3,
        Overlay = 4,
        SoftLight = 5,
        ColorDodge = 6
    }

    [RequireComponent(typeof(MeshRenderer))]
    public class FluidSurfaceInteraction : MonoBehaviour
    {
        [Header("Simulation Mode")]
        [SerializeField] private FluidSimulationMode _simulationMode = FluidSimulationMode.Standard;

        [Header("Domain & Grid Dimensions")]
        [Tooltip("Physical dimensions of the water surface in world/object units (Width X, Length Z)")]
        [SerializeField] private Vector2 _domainSize = new Vector2(10.0f, 10.0f);
        [Tooltip("Automatically resizes attached ProceduralPlane mesh to match DomainSize")]
        [SerializeField] private bool _syncProceduralPlaneMesh = true;

        [Header("Render Texture Settings")]
        [Tooltip("Base resolution for simulation RenderTextures along the reference axis (e.g. 128, 256, 512).")]
        [SerializeField] private int _textureResolution = 256;
        [Tooltip("When enabled, calculates non-square RT resolution to preserve 1:1 square isotropic simulation cells.")]
        [SerializeField] private bool _preserveSquareTexels = true;
        [SerializeField] private Shader _simulationShader;

        [Header("Fluid Physics (Viscosity Model)")]
        [Tooltip("Fluid Viscosity: 0.0 = Water (bouncy ripples, splash), 0.5 = Gel/Honey (quick settling), 1.0 = Solid Plastic (permanent furrow, zero melting)")]
        [Range(0.0f, 1.0f)]
        [SerializeField] private float _viscosity = 1.0f;

        [Header("Procedural Wave Settings")]
        [Tooltip("Procedural wave algorithm: 0 = Classic Simplex, 1 = Basin Slosh (Standing Wave)")]
        [Range(0, 1)]
        [SerializeField] private int _noiseType = 1;
        [Tooltip("Strength of basin surge / directional slosh tilt")]
        [Range(0f, 1f)]
        [SerializeField] private float _warpStrength = 0.35f;

        [Header("Brush Settings")]
        [Tooltip("Interaction brush radius in fluid object-space units (meters at unit scale)")]
        [Range(0.10f, 3.00f)]
        [SerializeField] private float _brushRadius = 0.70f;
        [Tooltip("Depth of the carved furrow / interaction force")]
        [Range(0.1f, 2.0f)]
        [SerializeField] private float _brushStrength = 0.70f;

        [Header("Trail Decay & Length")]
        [Tooltip("Decay speed of the drawn trail. 0 = Infinite persistent trail (no fading), 0.5 = Long wake (~3s), 1.5 = Normal (~1.3s), 4.0 = Short quick trail (~0.5s)")]
        [Range(0.0f, 6.0f)]
        [SerializeField] private float _decaySpeed = 1.0f;

        // Dynamic fluid parameters computed from Viscosity
        private float _oscillationDamping = 13.0f;
        private float _springStiffness = 15.0f;
        private float _plasticity = 1.0f;
        private float _trailDecay = 0.0f;
        private float _rimHeightFactor = 0.20f;
        private float _rimWidthFactor = 0.35f;
        private float _bowWaveIntensity = 2.0f;
        private float _effectiveSpring = 0.0f;
        private bool _isPhysicsDirty = true;
        [Header("Kinetic Velocity Normalization")]
        [SerializeField] private float _minVelocity = 0.0f;
        [SerializeField] private float _maxVelocity = 1.0f;
        [SerializeField] private bool _autoVelocityRange = true;
        private float _observedPeakSpeed = 0.0f;
        private float _observedMinSpeed = float.MaxValue;
        private bool _wasInteracting = false;

        [Header("Pigment Dynamics (Kinetic Dispersion)")]
        [Tooltip("Wave velocity threshold below which no pigment is excited")]
        [Range(0.01f, 2.0f)]
        [SerializeField] private float _pigmentVelocityMin = 0.05f;
        [Tooltip("Wave velocity at which pigment excitation reaches maximum saturation")]
        [Range(0.2f, 10.0f)]
        [SerializeField] private float _pigmentVelocityMax = 1.5f;
        [Tooltip("Excitation injection rate per unit time")]
        [Range(0.1f, 20.0f)]
        [SerializeField] private float _pigmentInjection = 6.0f;
        [Tooltip("Spatial diffusion rate of pigment concentration across adjacent cells")]
        [Range(0.0f, 10.0f)]
        [SerializeField] private float _pigmentDiffusion = 2.0f;
        [Tooltip("Exponential temporal dissipation rate of the pigment")]
        [Range(0.0f, 5.0f)]
        [SerializeField] private float _pigmentDecay = 0.4f;

        private FluidSimulator _simulator;
        public FluidSimulator Simulator => _simulator;

        private Material _planeMaterial;
        private Texture2D _runtimeRampTexture;
        private Camera _mainCamera;

        // Pointer input and raycast handler
        private FluidPointerInput _pointerInput = new FluidPointerInput();
        public FluidPointerInput PointerInput => _pointerInput;

        // Freezing / Sleep state optimization
        [Header("Freezing & Sleep Settings")]
        [Tooltip("Time in seconds before the fluid freezes into idle sleep after interaction ends")]
        [Range(0.5f, 20.0f)]
        [SerializeField] private float _freezeTime = 5.0f;

        private float _wakeTimer = 0f;
        private bool _isSimulationSleeping = false;
        public bool IsSimulationSleeping => _isSimulationSleeping;
        public float FreezeTime
        {
            get => _freezeTime;
            set
            {
                _freezeTime = Mathf.Max(0.1f, value);
                WakeSimulation();
            }
        }

        // Public API for EditorWindow & external controllers
        public float Viscosity
        {
            get => _viscosity;
            set
            {
                _viscosity = Mathf.Clamp01(value);
                _isPhysicsDirty = true;
                UpdatePhysicalParameters();
                WakeSimulation();
            }
        }
        public float BrushRadius
        {
            get => _brushRadius;
            set
            {
                _brushRadius = Mathf.Clamp(value, 0.10f, 3.00f);
                _isPhysicsDirty = true;
                WakeSimulation();
            }
        }
        public float BrushStrength
        {
            get => _brushStrength;
            set
            {
                _brushStrength = Mathf.Clamp(value, 0.1f, 2.0f);
                _isPhysicsDirty = true;
                WakeSimulation();
            }
        }
        public float DecaySpeed
        {
            get => _decaySpeed;
            set
            {
                _decaySpeed = Mathf.Clamp(value, 0.0f, 8.0f);
                _isPhysicsDirty = true;
                UpdatePhysicalParameters();
                WakeSimulation();
            }
        }
        public Vector2 DomainSize
        {
            get => _domainSize;
            set
            {
                _domainSize = new Vector2(Mathf.Max(0.5f, value.x), Mathf.Max(0.5f, value.y));
                SyncDomainDimensions();
                WakeSimulation();
            }
        }
        public bool SyncProceduralPlaneMesh
        {
            get => _syncProceduralPlaneMesh;
            set => _syncProceduralPlaneMesh = value;
        }
        private int _simulationSubsteps = 1;
        private int _blitsIssuedThisFrame = 0;
        public int SimulationSubsteps
        {
            get => _simulationSubsteps;
            set => _simulationSubsteps = Mathf.Clamp(value, 0, 2000);
        }
        public int BlitsIssuedThisFrame => _blitsIssuedThisFrame;
        public float WakeTimer => _wakeTimer;
        public int TextureResolution => _textureResolution;
        public bool PreserveSquareTexels
        {
            get => _preserveSquareTexels;
            set
            {
                _preserveSquareTexels = value;
                SyncDomainDimensions();
                WakeSimulation();
            }
        }
        public Vector2Int SimulationResolution => _simulator != null ? _simulator.Resolution2D : ComputeSimulationResolution(_domainSize);
        public float TexelDensityX => SimulationResolution.x / Mathf.Max(0.001f, _domainSize.x);
        public float TexelDensityZ => SimulationResolution.y / Mathf.Max(0.001f, _domainSize.y);

        // Computes simulation resolution preserving square isotropic texels
        public Vector2Int ComputeSimulationResolution(Vector2 domain)
        {
            if (!_preserveSquareTexels)
            {
                int square = Mathf.Clamp(_textureResolution, 32, 2048);
                return new Vector2Int(square, square);
            }

            float maxDim = Mathf.Max(0.001f, Mathf.Max(domain.x, domain.y));
            int resX = Mathf.Clamp(Mathf.RoundToInt(_textureResolution * (domain.x / maxDim)), 32, 2048);
            int resY = Mathf.Clamp(Mathf.RoundToInt(_textureResolution * (domain.y / maxDim)), 32, 2048);

            // Keep dimensions multiples of 2 for GPU alignment
            resX = Mathf.Max(32, (resX / 2) * 2);
            resY = Mathf.Max(32, (resY / 2) * 2);

            return new Vector2Int(resX, resY);
        }
        public Material PlaneMaterial
        {
            get
            {
                if (Application.isPlaying)
                {
                    if (_planeMaterial == null)
                    {
                        MeshRenderer mr = GetComponent<MeshRenderer>();
                        if (mr != null) _planeMaterial = mr.material;
                    }
                    return _planeMaterial;
                }
                else
                {
                    MeshRenderer mr = GetComponent<MeshRenderer>();
                    return mr != null ? mr.sharedMaterial : null;
                }
            }
        }
        public int NormalMode
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(NormalModeId) 
                ? Mathf.RoundToInt(PlaneMaterial.GetFloat(NormalModeId)) 
                : 2;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(NormalModeId))
                {
                    mat.SetFloat(NormalModeId, value);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public int NoiseType
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(NoiseTypeId)
                ? Mathf.RoundToInt(PlaneMaterial.GetFloat(NoiseTypeId))
                : _noiseType;
            set
            {
                _noiseType = Mathf.Clamp(value, 0, 1);
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(NoiseTypeId))
                {
                    mat.SetFloat(NoiseTypeId, _noiseType);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float WarpStrength
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(WarpStrengthId)
                ? PlaneMaterial.GetFloat(WarpStrengthId)
                : _warpStrength;
            set
            {
                _warpStrength = Mathf.Clamp01(value);
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(WarpStrengthId))
                {
                    mat.SetFloat(WarpStrengthId, _warpStrength);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float NoiseAmplitude
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(NoiseAmplitudeId) ? PlaneMaterial.GetFloat(NoiseAmplitudeId) : 0.0f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(NoiseAmplitudeId))
                {
                    mat.SetFloat(NoiseAmplitudeId, Mathf.Max(0.0f, value));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float NoiseScale
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(NoiseScaleId) ? PlaneMaterial.GetFloat(NoiseScaleId) : 0.5f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(NoiseScaleId))
                {
                    mat.SetFloat(NoiseScaleId, Mathf.Clamp(value, 0.05f, 5.0f));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float NoiseSpeed
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(NoiseSpeedId) ? PlaneMaterial.GetFloat(NoiseSpeedId) : 0.8f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(NoiseSpeedId))
                {
                    mat.SetFloat(NoiseSpeedId, Mathf.Clamp(value, 0.0f, 5.0f));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public int NoiseOctaves
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(NoiseOctavesId) ? Mathf.RoundToInt(PlaneMaterial.GetFloat(NoiseOctavesId)) : 2;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(NoiseOctavesId))
                {
                    mat.SetFloat(NoiseOctavesId, Mathf.Clamp(value, 1, 3));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float MinHeight
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(MinHeightId) ? PlaneMaterial.GetFloat(MinHeightId) : -0.5f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(MinHeightId))
                {
                    mat.SetFloat(MinHeightId, value);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float MaxHeight
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(MaxHeightId) ? PlaneMaterial.GetFloat(MaxHeightId) : 1.0f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(MaxHeightId))
                {
                    mat.SetFloat(MaxHeightId, value);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float FresnelPower
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(FresnelPowerId) ? PlaneMaterial.GetFloat(FresnelPowerId) : 2.2f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(FresnelPowerId))
                {
                    mat.SetFloat(FresnelPowerId, Mathf.Max(0.1f, value));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float FresnelIntensity
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(FresnelIntensityId) ? PlaneMaterial.GetFloat(FresnelIntensityId) : 1.0f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(FresnelIntensityId))
                {
                    mat.SetFloat(FresnelIntensityId, Mathf.Max(0f, value));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float FresnelBias
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(FresnelBiasId) ? PlaneMaterial.GetFloat(FresnelBiasId) : 0.0f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(FresnelBiasId))
                {
                    mat.SetFloat(FresnelBiasId, Mathf.Clamp(value, -0.5f, 0.5f));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float Roughness
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(RoughnessId) ? PlaneMaterial.GetFloat(RoughnessId) : 0.08f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(RoughnessId))
                {
                    mat.SetFloat(RoughnessId, Mathf.Clamp(value, 0.01f, 1f));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float Metallic
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(MetallicId) ? PlaneMaterial.GetFloat(MetallicId) : 0.25f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(MetallicId))
                {
                    mat.SetFloat(MetallicId, Mathf.Clamp01(value));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float SpecularIntensity
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(SpecularIntensityId) ? PlaneMaterial.GetFloat(SpecularIntensityId) : 1.0f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(SpecularIntensityId))
                {
                    mat.SetFloat(SpecularIntensityId, Mathf.Max(0f, value));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public Color SpecularColor
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(SpecularColorId) ? PlaneMaterial.GetColor(SpecularColorId) : Color.white;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(SpecularColorId))
                {
                    mat.SetColor(SpecularColorId, value);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float ReflectionIntensity
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(ReflectionIntensityId) ? PlaneMaterial.GetFloat(ReflectionIntensityId) : 1.2f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(ReflectionIntensityId))
                {
                    mat.SetFloat(ReflectionIntensityId, Mathf.Max(0f, value));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public bool UseCustomCubemap
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(UseCustomCubemapId) && PlaneMaterial.GetFloat(UseCustomCubemapId) > 0.5f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(UseCustomCubemapId))
                {
                    mat.SetFloat(UseCustomCubemapId, value ? 1.0f : 0.0f);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public Cubemap CustomCubemap
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(CustomCubemapId) ? PlaneMaterial.GetTexture(CustomCubemapId) as Cubemap : null;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(CustomCubemapId))
                {
                    mat.SetTexture(CustomCubemapId, value);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public int ColorDebugMode
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(ColorDebugModeId)
                ? Mathf.RoundToInt(PlaneMaterial.GetFloat(ColorDebugModeId))
                : 5;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(ColorDebugModeId))
                {
                    mat.SetFloat(ColorDebugModeId, Mathf.Clamp(value, 0, 8));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public FluidColorBlendMode ColorBlendMode
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(ColorBlendModeId)
                ? (FluidColorBlendMode)Mathf.Clamp(Mathf.RoundToInt(PlaneMaterial.GetFloat(ColorBlendModeId)), 0, 6)
                : FluidColorBlendMode.Normal;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(ColorBlendModeId))
                {
                    mat.SetFloat(ColorBlendModeId, (float)value);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float MinVelocity
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(MinVelocityId) ? PlaneMaterial.GetFloat(MinVelocityId) : _minVelocity;
            set
            {
                _minVelocity = value;
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(MinVelocityId))
                {
                    mat.SetFloat(MinVelocityId, value);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public float MaxVelocity
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(MaxVelocityId) ? PlaneMaterial.GetFloat(MaxVelocityId) : _maxVelocity;
            set
            {
                _maxVelocity = Mathf.Max(_minVelocity + 0.01f, value);
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(MaxVelocityId))
                {
                    mat.SetFloat(MaxVelocityId, _maxVelocity);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }
        public bool AutoVelocityRange
        {
            get => _autoVelocityRange;
            set => _autoVelocityRange = value;
        }
        public float ObservedPeakSpeed => _observedPeakSpeed;
        public float ObservedMinSpeed => _observedMinSpeed < float.MaxValue ? _observedMinSpeed : 0.0f;
        public void ResetVelocityRange()
        {
            _observedPeakSpeed = 0.0f;
            _observedMinSpeed = float.MaxValue;
            _wasInteracting = false;
            MinVelocity = 0.0f;
            MaxVelocity = 1.0f;
        }

        public float PigmentVelocityMin
        {
            get => _pigmentVelocityMin;
            set
            {
                _pigmentVelocityMin = Mathf.Max(0.001f, value);
                _isPhysicsDirty = true;
                WakeSimulation();
            }
        }
        public float PigmentVelocityMax
        {
            get => _pigmentVelocityMax;
            set
            {
                _pigmentVelocityMax = Mathf.Max(_pigmentVelocityMin + 0.01f, value);
                _isPhysicsDirty = true;
                WakeSimulation();
            }
        }
        public float PigmentInjection
        {
            get => _pigmentInjection;
            set
            {
                _pigmentInjection = Mathf.Max(0.0f, value);
                _isPhysicsDirty = true;
                WakeSimulation();
            }
        }
        public float PigmentDiffusion
        {
            get => _pigmentDiffusion;
            set
            {
                _pigmentDiffusion = Mathf.Max(0.0f, value);
                _isPhysicsDirty = true;
                WakeSimulation();
            }
        }
        public float PigmentDecay
        {
            get => _pigmentDecay;
            set
            {
                _pigmentDecay = Mathf.Max(0.0f, value);
                _isPhysicsDirty = true;
                WakeSimulation();
            }
        }

        public void ApplyPigmentPreset(string presetName)
        {
            switch (presetName.ToLowerInvariant())
            {
                case "ink":
                    _pigmentVelocityMin = 0.05f;
                    _pigmentVelocityMax = 1.5f;
                    _pigmentInjection = 6.0f;
                    _pigmentDiffusion = 2.0f;
                    _pigmentDecay = 0.4f;
                    break;
                case "gel":
                    _pigmentVelocityMin = 0.1f;
                    _pigmentVelocityMax = 2.5f;
                    _pigmentInjection = 4.0f;
                    _pigmentDiffusion = 0.5f;
                    _pigmentDecay = 0.2f;
                    break;
                case "neon fluid":
                case "neonfluid":
                    _pigmentVelocityMin = 0.08f;
                    _pigmentVelocityMax = 1.8f;
                    _pigmentInjection = 5.0f;
                    _pigmentDiffusion = 2.5f;
                    _pigmentDecay = 0.8f;
                    break;
                case "fast dissolve":
                case "fastdissolve":
                    _pigmentVelocityMin = 0.05f;
                    _pigmentVelocityMax = 1.2f;
                    _pigmentInjection = 4.0f;
                    _pigmentDiffusion = 4.0f;
                    _pigmentDecay = 2.5f;
                    break;
            }
            _isPhysicsDirty = true;
            WakeSimulation();
        }

        public bool EnableOpticalAbsorption
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(EnableOpticalAbsorptionId)
                ? PlaneMaterial.GetFloat(EnableOpticalAbsorptionId) > 0.5f
                : true;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(EnableOpticalAbsorptionId))
                {
                    mat.SetFloat(EnableOpticalAbsorptionId, value ? 1.0f : 0.0f);
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }

        public float PigmentDensity
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(PigmentDensityId)
                ? PlaneMaterial.GetFloat(PigmentDensityId)
                : 2.5f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(PigmentDensityId))
                {
                    mat.SetFloat(PigmentDensityId, Mathf.Clamp(value, 0.1f, 10.0f));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }

        public float OpticalDepthScale
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(OpticalDepthScaleId)
                ? PlaneMaterial.GetFloat(OpticalDepthScaleId)
                : 1.5f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(OpticalDepthScaleId))
                {
                    mat.SetFloat(OpticalDepthScaleId, Mathf.Clamp(value, 0.1f, 5.0f));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }

        public float OpticalDepthFromHeight
        {
            get => PlaneMaterial != null && PlaneMaterial.HasProperty(OpticalDepthFromHeightId)
                ? PlaneMaterial.GetFloat(OpticalDepthFromHeightId)
                : 1.2f;
            set
            {
                Material mat = PlaneMaterial;
                if (mat != null && mat.HasProperty(OpticalDepthFromHeightId))
                {
                    mat.SetFloat(OpticalDepthFromHeightId, Mathf.Clamp(value, 0.0f, 3.0f));
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
                }
            }
        }

        public float WaveDamping => _oscillationDamping;
        public float OscillationDamping => _oscillationDamping;
        public float SpringStiffness => _springStiffness;
        public float Plasticity => _plasticity;
        public float TrailDecay => _trailDecay;
        public string DebugInputSource => _pointerInput != null ? _pointerInput.DebugInputSource : "Idle";
        public bool DebugHitSuccess => _pointerInput != null && _pointerInput.DebugHitSuccess;
        public bool IsInteractingNow => _pointerInput != null && _pointerInput.IsInteracting;

        // Shared material property IDs
        private static readonly int ViscosityId = Shader.PropertyToID("_Viscosity");

        // Plane Material property IDs
        private static readonly int MinVelocityId = Shader.PropertyToID("_MinVelocity");
        private static readonly int MaxVelocityId = Shader.PropertyToID("_MaxVelocity");
        private static readonly int NoiseAmplitudeId = Shader.PropertyToID("_NoiseAmplitude");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        private static readonly int NoiseSpeedId = Shader.PropertyToID("_NoiseSpeed");
        private static readonly int NoiseOctavesId = Shader.PropertyToID("_NoiseOctaves");
        private static readonly int NoiseTimeId = Shader.PropertyToID("_NoiseTime");
        private static readonly int NoiseTypeId = Shader.PropertyToID("_NoiseType");
        private static readonly int WarpStrengthId = Shader.PropertyToID("_WarpStrength");
        private static readonly int InteractiveHeightScaleId = Shader.PropertyToID("_InteractiveHeightScale");
        private static readonly int NormalModeId = Shader.PropertyToID("_NormalMode");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int RoughnessId = Shader.PropertyToID("_Roughness");
        private static readonly int SpecularIntensityId = Shader.PropertyToID("_SpecularIntensity");
        private static readonly int SpecularColorId = Shader.PropertyToID("_SpecularColor");
        private static readonly int ReflectionIntensityId = Shader.PropertyToID("_ReflectionIntensity");
        private static readonly int UseCustomCubemapId = Shader.PropertyToID("_UseCustomCubemap");
        private static readonly int CustomCubemapId = Shader.PropertyToID("_CustomCubemap");
        private static readonly int MinHeightId = Shader.PropertyToID("_MinHeight");
        private static readonly int MaxHeightId = Shader.PropertyToID("_MaxHeight");
        private static readonly int FresnelPowerId = Shader.PropertyToID("_FresnelPower");
        private static readonly int FresnelIntensityId = Shader.PropertyToID("_FresnelIntensity");
        private static readonly int FresnelBiasId = Shader.PropertyToID("_FresnelBias");
        private static readonly int RampMapId = Shader.PropertyToID("_RampMap");
        private static readonly int FluidDomainSizeOSId = Shader.PropertyToID("_FluidDomainSizeOS");
        private static readonly int ColorDebugModeId = Shader.PropertyToID("_ColorDebugMode");
        private static readonly int ColorBlendModeId = Shader.PropertyToID("_ColorBlendMode");
        private static readonly int EnableOpticalAbsorptionId = Shader.PropertyToID("_EnableOpticalAbsorption");
        private static readonly int PigmentDensityId = Shader.PropertyToID("_PigmentDensity");
        private static readonly int OpticalDepthScaleId = Shader.PropertyToID("_OpticalDepthScale");
        private static readonly int OpticalDepthFromHeightId = Shader.PropertyToID("_OpticalDepthFromHeight");

        // Continuous smooth time accumulation for ambient noise waves
        private float _accumulatedNoiseTime;

        public RenderTexture CurrentDisplacementTexture => _simulator != null ? _simulator.ActiveTexture : null;
        public FluidSimulationMode CurrentMode => _simulationMode;

        public void WakeSimulation(float duration = -1f)
        {
            if (duration < 0f)
            {
                // Pigment-aware wake duration: guarantee sufficient time for kinetic pigment concentration C to decay to < 1%
                // For exponential decay C(t) = C0 * exp(-lambda * t), C(t) reaches 0.01 at t = ln(100)/lambda = 4.605 / lambda.
                float pigmentClearTime = 4.6f / Mathf.Max(0.05f, _pigmentDecay);
                duration = Mathf.Max(_freezeTime, pigmentClearTime);
            }
            _wakeTimer = Mathf.Max(_wakeTimer, duration);
            _isSimulationSleeping = false;
        }

#if UNITY_EDITOR
        private void OnEnable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            _pointerInput?.Reset();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            _pointerInput?.HandleSceneGUI(sceneView, transform, _isSimulationSleeping, _domainSize);
        }
#endif

        private void Start()
        {
            FindActiveCamera();

            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            _planeMaterial = meshRenderer.material;

            SyncDomainDimensions();

            if (_simulationShader == null)
            {
                _simulationShader = Shader.Find("Flexus/FluidSimulationBlit");
            }

            _simulator = new FluidSimulator();
            Vector2Int simRes = ComputeSimulationResolution(_domainSize);
            _simulator.Initialize(_simulationShader, simRes, _planeMaterial);
            _simulator.SetDomainSize(_domainSize);

            // Ensure ramp texture is assigned if missing without overwriting user material parameters
            if (_planeMaterial != null && (!_planeMaterial.HasProperty(RampMapId) || _planeMaterial.GetTexture(RampMapId) == null))
            {
#if UNITY_EDITOR
                Texture2D ramp = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(GradientTextureGenerator.RampTexturePath);
                if (ramp != null)
                {
                    _planeMaterial.SetTexture(RampMapId, ramp);
                }
#endif
            }

            // Sync physics from serialized fields (preserves saved user settings)
            UpdatePhysicalParameters();
        }

        private void FindActiveCamera()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                _mainCamera = FindFirstObjectByType<Camera>();
            }
        }

        public void ClearCanvas()
        {
            _simulator?.ClearCanvas();
            _pointerInput?.Reset();
            ResetVelocityRange();
            WakeSimulation(0.2f);
        }

        public void SetResolution(int resolution)
        {
            _textureResolution = Mathf.Clamp(resolution, 32, 2048);
            if (_simulator != null)
            {
                Vector2Int simRes = ComputeSimulationResolution(_domainSize);
                _simulator.SetResolution(simRes, PlaneMaterial);
            }
            WakeSimulation(0.2f);
        }

        public void SetSimulationMode(FluidSimulationMode mode)
        {
            _simulationMode = mode;
            ApplyModeSettings(mode);
            WakeSimulation();
        }

        public void ResetToDefaultNeonRamp()
        {
            SetCustomGradient(GradientTextureGenerator.CreateReferenceNeonGradient());
        }

        public void SetCustomGradient(Gradient gradient)
        {
            Material mat = PlaneMaterial;
            if (mat == null) return;

            if (_runtimeRampTexture != null)
            {
                if (Application.isPlaying) Destroy(_runtimeRampTexture);
                else DestroyImmediate(_runtimeRampTexture);
            }

            _runtimeRampTexture = GradientTextureGenerator.CreateRampTexture(gradient);
            mat.SetTexture(RampMapId, _runtimeRampTexture);
        }

        public void SyncDomainDimensions()
        {
            _domainSize = new Vector2(Mathf.Max(0.5f, _domainSize.x), Mathf.Max(0.5f, _domainSize.y));

            if (_syncProceduralPlaneMesh)
            {
                ProceduralPlane plane = GetComponent<ProceduralPlane>();
                if (plane != null)
                {
                    plane.SetDimensions(_domainSize);
                }
            }

            Vector4 domainVec = new Vector4(
                _domainSize.x,
                _domainSize.y,
                1.0f / _domainSize.x,
                1.0f / _domainSize.y
            );

            Material mat = PlaneMaterial;
            if (mat != null)
            {
                if (mat.HasProperty(FluidDomainSizeOSId))
                {
                    mat.SetVector(FluidDomainSizeOSId, domainVec);
                }

#if UNITY_EDITOR
                if (mat.HasProperty(RampMapId) && mat.GetTexture(RampMapId) == null)
                {
                    Texture2D defaultRamp = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(GradientTextureGenerator.RampTexturePath);
                    if (defaultRamp != null)
                    {
                        mat.SetTexture(RampMapId, defaultRamp);
                    }
                }
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
            }

            if (_simulator != null)
            {
                _simulator.SetDomainSize(_domainSize);
                Vector2Int simRes = ComputeSimulationResolution(_domainSize);
                _simulator.SetResolution(simRes, PlaneMaterial);
            }
        }

        private void OnValidate()
        {
            if (transform.localScale != Vector3.one)
            {
                Debug.LogWarning("<color=yellow>[FluidSurfaceInteraction]</color> Transform scale is not (1,1,1). Surface dimensions should be adjusted exclusively via DomainSize to guarantee metric precision.", this);
            }
            SyncDomainDimensions();
            _isPhysicsDirty = true;
            UpdatePhysicalParameters();
        }

        public void UpdatePhysicalParameters()
        {
            _viscosity = Mathf.Clamp01(_viscosity);

            if (_viscosity >= 0.99f)
            {
                // Maximum viscosity (plastic state): zero spring restoration, high damping, zero decay
                _oscillationDamping = 50.0f;
                _springStiffness = 0.0f;
                _plasticity = 1.0f;
                _trailDecay = 0.0f;
                _bowWaveIntensity = 0.0f;
                _rimHeightFactor = 0.22f;
                _rimWidthFactor = 0.35f;
            }
            else
            {
                // 1. Oscillation damping: controls amplitude attenuation over time (e^(-damping * dt))
                float baseDamping = Mathf.Lerp(1.20f, 22.0f, Mathf.Pow(_viscosity, 1.8f));
                float dampingScale = (_decaySpeed <= 0.01f) ? 0.60f : Mathf.Max(0.5f, _decaySpeed);
                _oscillationDamping = Mathf.Max(0.9f, baseDamping * dampingScale);

                // 2. Oscillation frequency / surface tension (restoring spring stiffness k)
                _springStiffness = Mathf.Lerp(140.0f, 5.0f, _viscosity);

                // 3. Plasticity: suppresses elastic restoration force, allowing impressions to remain deformed
                // Uses InverseLerp + SmoothStep so plasticity is strictly 0.0 for water (viscosity <= 0.65)
                // and transitions smoothly to 1.0 for clay/gel (viscosity >= 0.90).
                float plasticT = Mathf.InverseLerp(0.65f, 0.90f, _viscosity);
                _plasticity = Mathf.SmoothStep(0.0f, 1.0f, plasticT);

                // 4. Trail decay: relaxation rate returning deformed surface toward equilibrium
                if (_decaySpeed <= 0.01f)
                {
                    _trailDecay = 0.0f;
                }
                else
                {
                    _trailDecay = _decaySpeed * Mathf.Lerp(0.30f, 1.2f, _viscosity);
                }

                // 5. Displaced rim & bow wave scaling
                _rimHeightFactor = Mathf.Lerp(0.02f, 0.22f, _viscosity);
                _rimWidthFactor = Mathf.Lerp(0.15f, 0.35f, _viscosity);
                _bowWaveIntensity = Mathf.Lerp(0.6f, 2.2f, _viscosity);
            }

            _effectiveSpring = _springStiffness * (1.0f - _plasticity);

            // 6. Push material parameters to surface shader
            Material mat = PlaneMaterial;
            if (mat != null)
            {
                mat.SetFloat(ViscosityId, _viscosity);
                mat.SetFloat(NoiseTypeId, _noiseType);
                mat.SetFloat(WarpStrengthId, _warpStrength);
                if (mat.HasProperty(FluidDomainSizeOSId))
                {
                    mat.SetVector(FluidDomainSizeOSId, new Vector4(_domainSize.x, _domainSize.y, 1.0f / _domainSize.x, 1.0f / _domainSize.y));
                }
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
            }

            // 7. Upload static parameters to simulation blit material via FluidSimulator
            if (_simulator != null)
            {
                _simulator.SetPhysicalParameters(
                    _brushRadius,
                    _brushStrength,
                    _rimWidthFactor,
                    _rimHeightFactor,
                    _bowWaveIntensity,
                    _oscillationDamping,
                    _trailDecay,
                    _viscosity,
                    _plasticity,
                    _effectiveSpring);

                _simulator.SetPigmentParameters(
                    _pigmentVelocityMin,
                    _pigmentVelocityMax,
                    _pigmentInjection,
                    _pigmentDiffusion,
                    _pigmentDecay);

                _isPhysicsDirty = false;
            }
            else
            {
                _isPhysicsDirty = true;
            }
        }

        private void ApplyModeSettings(FluidSimulationMode mode)
        {
            Material mat = PlaneMaterial;
            if (mat == null) return;

            switch (mode)
            {
                case FluidSimulationMode.Standard:
                    // Standard interactive mode: responsive surface ripples (no ambient noise)
                    mat.SetFloat(NoiseAmplitudeId, 0.0f);
                    mat.SetFloat(InteractiveHeightScaleId, 1.0f);
                    break;

                case FluidSimulationMode.Waves:
                    // Waves mode: combined ambient procedural waves + interactive ripples
                    _noiseType = 1;
                    mat.SetFloat(NoiseAmplitudeId, 0.12f);
                    mat.SetFloat(InteractiveHeightScaleId, 1.0f);
                    break;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
            UpdatePhysicalParameters();
        }

        private void Update()
        {
            _blitsIssuedThisFrame = 0;
            if (_simulator == null || !_simulator.IsInitialized) return;

            // Advance ambient wave animation time with continuous smooth speed scaling based on viscosity
            float waveSpeed = Mathf.Lerp(1.30f, 0.15f, _viscosity);
            _accumulatedNoiseTime += Time.deltaTime * waveSpeed;

            if (_planeMaterial != null)
            {
                _planeMaterial.SetFloat(NoiseTimeId, _accumulatedNoiseTime);
            }

            if (_mainCamera == null)
            {
                FindActiveCamera();
            }

            FluidStrokeState stroke = _pointerInput.Update(transform, _mainCamera, Time.deltaTime, _domainSize);

            if (stroke.HasImpulseThisFrame || (_wasInteracting && !stroke.IsInteracting))
            {
                WakeSimulation();
            }

            float strokeSpeed = stroke.BrushVelocity.z;
            float cursorMotion01 = 0.0f;

            // Baseline ceiling representing upper speed limit for color ramp (1.0).
            float baselineMaxSpeed = 2.0f;

            if (stroke.IsInteracting)
            {
                const float stationaryThreshold = 0.03f;

                if (strokeSpeed > stationaryThreshold)
                {
                    _observedMinSpeed = Mathf.Min(_observedMinSpeed, strokeSpeed);
                    _observedPeakSpeed = Mathf.Max(_observedPeakSpeed, strokeSpeed);

                    // Dynamic normalization range:
                    // If AutoVelocityRange is enabled, ceiling expands to encompass user's fastest swipe.
                    float effectiveMax = _autoVelocityRange ? Mathf.Max(baselineMaxSpeed, _observedPeakSpeed) : baselineMaxSpeed;
                    float speedT = Mathf.Clamp01((strokeSpeed - stationaryThreshold) / (effectiveMax - stationaryThreshold));
                    cursorMotion01 = speedT;
                }
                else
                {
                    cursorMotion01 = 0.0f;
                }

                if (_autoVelocityRange)
                {
                    MinVelocity = 0.0f;
                    MaxVelocity = 1.0f;
                }
            }
            else
            {
                // When interaction ends, gradually relax peak speed back toward baseline
                if (_observedPeakSpeed > baselineMaxSpeed)
                {
                    _observedPeakSpeed = Mathf.MoveTowards(_observedPeakSpeed, baselineMaxSpeed, 0.5f * Time.deltaTime);
                }
            }

            _wasInteracting = stroke.IsInteracting;


            // Sleep countdown when idle
            if (!stroke.IsInteracting && stroke.HitData.w < 0.5f)
            {
                _wakeTimer -= Time.deltaTime;
                if (_wakeTimer <= 0f)
                {
                    _isSimulationSleeping = true;
                }
            }

            // If sleeping, skip GPU simulation blit while idle
            if (_isSimulationSleeping)
            {
                return;
            }

            Vector4 brushVel = new Vector4(stroke.BrushVelocity.x, stroke.BrushVelocity.y, stroke.BrushVelocity.z, cursorMotion01);
            ExecutePingPongBlit(stroke.HitData, stroke.PrevHitData, brushVel);
        }

        private void ExecutePingPongBlit(Vector4 hitData, Vector4 prevHitData, Vector4 brushVelocityData)
        {
            if (_simulator == null || _simulationSubsteps <= 0) return;

            // Sync static physics parameters to Blit Shader only when dirty
            if (_isPhysicsDirty)
            {
                UpdatePhysicalParameters();
            }

            float interactiveScale = 1.0f;
            if (_planeMaterial != null && _planeMaterial.HasProperty(InteractiveHeightScaleId))
            {
                interactiveScale = _planeMaterial.GetFloat(InteractiveHeightScaleId);
            }

            _blitsIssuedThisFrame = _simulationSubsteps;
            _simulator.ExecutePingPongBlit(hitData, prevHitData, brushVelocityData, Time.deltaTime, _planeMaterial, interactiveScale, _simulationSubsteps);
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            SceneView.duringSceneGui -= OnSceneGUI;
#endif
            if (_runtimeRampTexture != null)
            {
                if (Application.isPlaying) Destroy(_runtimeRampTexture);
                else DestroyImmediate(_runtimeRampTexture);
                _runtimeRampTexture = null;
            }
            if (_simulator != null)
            {
                _simulator.Dispose();
                _simulator = null;
            }
            if (Application.isPlaying && _planeMaterial != null)
            {
                Destroy(_planeMaterial);
            }
        }
    }
}
