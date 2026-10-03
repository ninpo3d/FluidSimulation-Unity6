#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Flexus.FluidSimulation.Editor
{
    /// <summary>
    /// Dockable editor utility window for interactive fluid simulation control.
    /// Exposes real-time render target previews, color ramp baking, preset management,
    /// and physical parameter adjustments in both edit and play modes.
    /// </summary>
    public class FluidDynamicsWindow : EditorWindow
    {
        [MenuItem("Window/Flexus/Fluid Dynamics Controller %#f", priority = 100)]
        [MenuItem("Flexus/Fluid Dynamics Window", priority = 10)]
        public static void Open()
        {
            FluidDynamicsWindow window = GetWindow<FluidDynamicsWindow>("Fluid Dynamics");
            window.minSize = new Vector2(340, 560);
            window.Show();
        }

        [SerializeField] private FluidSurfaceInteraction _targetInteraction;
        [SerializeField] private Gradient _activeGradient;
        private Vector2 _scrollPos;

        private void OnEnable()
        {
            titleContent = new GUIContent("Fluid Dynamics", EditorGUIUtility.IconContent("d_NavMeshAgent Icon").image);
            FindTargetIfNeeded();
        }

        private void Update()
        {
            // Continuously repaint during Play Mode so RT preview, timers, and sliders update at full FPS
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void FindTargetIfNeeded()
        {
            if (_targetInteraction == null)
            {
                _targetInteraction = Object.FindFirstObjectByType<FluidSurfaceInteraction>();
            }
        }

        private void OnGUI()
        {
            FindTargetIfNeeded();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();

            EditorGUILayout.Space(6);

            _targetInteraction = (FluidSurfaceInteraction)EditorGUILayout.ObjectField("Target Simulation", _targetInteraction, typeof(FluidSurfaceInteraction), true);

            if (_targetInteraction == null)
            {
                EditorGUILayout.HelpBox("No active FluidSurfaceInteraction component found in the open scene.\nPlease open the fluid demo scene or select a GameObject with FluidSurfaceInteraction.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space(4);

            DrawStagesSection();

            EditorGUILayout.Space(8);

            DrawViscositySection();

            EditorGUILayout.Space(8);

            DrawNoiseSection();

            EditorGUILayout.Space(8);

            DrawColorRampSection();

            EditorGUILayout.Space(8);

            DrawKineticStrokeColorSection();

            EditorGUILayout.Space(8);

            DrawBrushSection();

            EditorGUILayout.Space(8);

            DrawDomainAndGridSection();

            EditorGUILayout.Space(8);

            DrawRenderingSection();

            EditorGUILayout.Space(8);

            DrawDisplacementMapPreview();

            EditorGUILayout.Space(8);

            DrawDiagnosticsSection();

            EditorGUILayout.Space(8);

            DrawSaveSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.BeginHorizontal();
            GUILayout.Label("<b><size=13><color=#00E5FF>FLEXUS</color> FLUID DYNAMICS CONTROLLER</size></b>", GetRichLabelStyle());

            if (Application.isPlaying)
            {
                string statusText = _targetInteraction != null && _targetInteraction.IsSimulationSleeping
                    ? "<color=#FFD54F><b>[FROZEN] (0% GPU)</b></color>"
                    : $"<color=#00E676><b>[ACTIVE] ({(_targetInteraction != null ? _targetInteraction.WakeTimer.ToString("F1") : "0")}s)</b></color>";

                GUILayout.Label(statusText, GetRichLabelRightStyle(), GUILayout.Width(150));
            }
            else
            {
                GUILayout.Label("<color=#888888>[Edit Mode]</color>", GetRichLabelRightStyle(), GUILayout.Width(90));
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawStagesSection()
        {
            EditorGUILayout.LabelField("SIMULATION MODES", EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();
            DrawStageButton("Standard Mode", FluidSimulationMode.Standard);
            DrawStageButton("Waves Mode", FluidSimulationMode.Waves);
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(3);

            GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);
            if (GUILayout.Button("Clear Surface Canvas", GUILayout.Height(24)))
            {
                _targetInteraction.ClearCanvas();
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawStageButton(string label, FluidSimulationMode mode)
        {
            bool isActive = _targetInteraction.CurrentMode == mode;
            GUI.backgroundColor = isActive ? new Color(0.2f, 1.0f, 0.4f) : Color.white;
            if (GUILayout.Button(label, GUILayout.Height(24)))
            {
                MarkDirty($"Set Simulation Mode to {mode}");
                _targetInteraction.SetSimulationMode(mode);
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawViscositySection()
        {
            string viscosityType;
            if (_targetInteraction.Viscosity < 0.35f) viscosityType = "Water";
            else if (_targetInteraction.Viscosity < 0.75f) viscosityType = "Gel / Honey";
            else if (_targetInteraction.Viscosity < 0.99f) viscosityType = "Thick Slime (Ref)";
            else viscosityType = "Solid Plastic (No Melting)";

            EditorGUILayout.LabelField($"VISCOSITY PHYSICS ({_targetInteraction.Viscosity:F2} - {viscosityType})", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            float newVisc = EditorGUILayout.Slider("Fluid Viscosity", _targetInteraction.Viscosity, 0.0f, 1.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Viscosity");
                _targetInteraction.Viscosity = newVisc;
            }

            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Quick Presets");
            if (DrawSmallPreset("Water (0.05)", Mathf.Abs(_targetInteraction.Viscosity - 0.05f) < 0.04f))
            {
                MarkDirty("Set Water Viscosity Preset");
                _targetInteraction.Viscosity = 0.05f;
            }
            if (DrawSmallPreset("Honey (0.50)", Mathf.Abs(_targetInteraction.Viscosity - 0.50f) < 0.04f))
            {
                MarkDirty("Set Honey Viscosity Preset");
                _targetInteraction.Viscosity = 0.50f;
            }
            if (DrawSmallPreset("Slime (0.85)", Mathf.Abs(_targetInteraction.Viscosity - 0.85f) < 0.04f))
            {
                MarkDirty("Set Slime Viscosity Preset");
                _targetInteraction.Viscosity = 0.85f;
            }
            if (DrawSmallPreset("Plastic (1.00)", _targetInteraction.Viscosity >= 0.99f))
            {
                MarkDirty("Set Plastic Viscosity Preset");
                _targetInteraction.Viscosity = 1.0f;
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            string decayDesc;
            if (_targetInteraction.Viscosity >= 0.99f)
            {
                decayDesc = "Infinite (Solid Plastic)";
            }
            else if (_targetInteraction.DecaySpeed <= 0.01f)
            {
                decayDesc = "Infinite / Persistent (0% decay)";
            }
            else
            {
                float approxLife = Mathf.Clamp(1.8f / _targetInteraction.DecaySpeed, 0.15f, 12f);
                decayDesc = $"~{approxLife:F1}s wake duration";
            }

            EditorGUILayout.LabelField($"TRAIL LENGTH & DECAY ({decayDesc})", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            float newDecay = EditorGUILayout.Slider("Trail Decay Speed", _targetInteraction.DecaySpeed, 0.0f, 6.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Trail Decay Speed");
                _targetInteraction.DecaySpeed = newDecay;
            }

            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Trail Presets");
            if (DrawSmallPreset("Infinite (0)", _targetInteraction.DecaySpeed <= 0.05f))
            {
                MarkDirty("Set Infinite Trail Preset");
                _targetInteraction.DecaySpeed = 0.0f;
            }
            if (DrawSmallPreset("Long (0.5)", Mathf.Abs(_targetInteraction.DecaySpeed - 0.5f) < 0.25f))
            {
                MarkDirty("Set Long Trail Preset");
                _targetInteraction.DecaySpeed = 0.5f;
            }
            if (DrawSmallPreset("Normal (1.2)", Mathf.Abs(_targetInteraction.DecaySpeed - 1.2f) < 0.25f))
            {
                MarkDirty("Set Normal Trail Preset");
                _targetInteraction.DecaySpeed = 1.2f;
            }
            if (DrawSmallPreset("Short (3.5)", Mathf.Abs(_targetInteraction.DecaySpeed - 3.5f) < 0.4f))
            {
                MarkDirty("Set Short Trail Preset");
                _targetInteraction.DecaySpeed = 3.5f;
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            float newFreeze = EditorGUILayout.Slider("Idle Sleep Time (sec)", _targetInteraction.FreezeTime, 0.5f, 20.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Idle Sleep Time");
                _targetInteraction.FreezeTime = newFreeze;
            }
        }

        private bool DrawSmallPreset(string label, bool selected)
        {
            GUI.backgroundColor = selected ? new Color(0.2f, 0.9f, 1.0f) : Color.white;
            bool clicked = GUILayout.Button(label, EditorStyles.miniButton);
            GUI.backgroundColor = Color.white;
            return clicked;
        }

        private void DrawNoiseSection()
        {
            EditorGUILayout.LabelField("AMBIENT NOISE & PROCEDURAL WAVES", EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Noise Mode");
            int currNoise = _targetInteraction.NoiseType;
            if (DrawSmallPreset("Classic Simplex", currNoise == 0))
            {
                MarkDirty("Set Classic Simplex Noise");
                _targetInteraction.NoiseType = 0;
            }
            if (DrawSmallPreset("Basin Slosh (Ref)", currNoise == 1))
            {
                MarkDirty("Set Basin Slosh Noise");
                _targetInteraction.NoiseType = 1;
            }
            GUILayout.EndHorizontal();

            // 1. Noise Amplitude / Height
            EditorGUI.BeginChangeCheck();
            float newAmp = EditorGUILayout.Slider("Noise Amplitude (Height)", _targetInteraction.NoiseAmplitude, 0.0f, 1.5f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Noise Amplitude");
                _targetInteraction.NoiseAmplitude = newAmp;
            }

            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Height Presets");
            if (DrawSmallPreset("Off (0.0)", _targetInteraction.NoiseAmplitude <= 0.005f))
            {
                MarkDirty("Set Noise Off");
                _targetInteraction.NoiseAmplitude = 0.0f;
            }
            if (DrawSmallPreset("Subtle (0.08)", Mathf.Abs(_targetInteraction.NoiseAmplitude - 0.08f) < 0.03f))
            {
                MarkDirty("Set Subtle Noise");
                _targetInteraction.NoiseAmplitude = 0.08f;
            }
            if (DrawSmallPreset("Normal (0.20)", Mathf.Abs(_targetInteraction.NoiseAmplitude - 0.20f) < 0.04f))
            {
                MarkDirty("Set Normal Noise");
                _targetInteraction.NoiseAmplitude = 0.20f;
            }
            if (DrawSmallPreset("Strong (0.45)", Mathf.Abs(_targetInteraction.NoiseAmplitude - 0.45f) < 0.06f))
            {
                MarkDirty("Set Strong Noise");
                _targetInteraction.NoiseAmplitude = 0.45f;
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // 2. Noise Scale / Frequency / Repetitions
            EditorGUI.BeginChangeCheck();
            float newScale = EditorGUILayout.Slider("Noise Scale (Repetitions)", _targetInteraction.NoiseScale, 0.05f, 3.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Noise Scale");
                _targetInteraction.NoiseScale = newScale;
            }

            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Scale Presets");
            if (DrawSmallPreset("Broad (0.25)", Mathf.Abs(_targetInteraction.NoiseScale - 0.25f) < 0.08f))
            {
                MarkDirty("Set Broad Scale");
                _targetInteraction.NoiseScale = 0.25f;
            }
            if (DrawSmallPreset("Medium (0.50)", Mathf.Abs(_targetInteraction.NoiseScale - 0.50f) < 0.08f))
            {
                MarkDirty("Set Medium Scale");
                _targetInteraction.NoiseScale = 0.50f;
            }
            if (DrawSmallPreset("Dense (1.00)", Mathf.Abs(_targetInteraction.NoiseScale - 1.00f) < 0.12f))
            {
                MarkDirty("Set Dense Scale");
                _targetInteraction.NoiseScale = 1.00f;
            }
            if (DrawSmallPreset("Fine (2.00)", Mathf.Abs(_targetInteraction.NoiseScale - 2.00f) < 0.20f))
            {
                MarkDirty("Set Fine Scale");
                _targetInteraction.NoiseScale = 2.00f;
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // 3. Noise Speed
            EditorGUI.BeginChangeCheck();
            float newSpeed = EditorGUILayout.Slider("Wave Speed", _targetInteraction.NoiseSpeed, 0.0f, 3.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Wave Speed");
                _targetInteraction.NoiseSpeed = newSpeed;
            }

            // 4. Detail Octaves
            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Noise Octaves (FBM)");
            int currOctaves = _targetInteraction.NoiseOctaves;
            if (DrawSmallPreset("1 (Smooth)", currOctaves == 1))
            {
                MarkDirty("Set Noise Octaves 1");
                _targetInteraction.NoiseOctaves = 1;
            }
            if (DrawSmallPreset("2 (Default)", currOctaves == 2))
            {
                MarkDirty("Set Noise Octaves 2");
                _targetInteraction.NoiseOctaves = 2;
            }
            if (DrawSmallPreset("3 (Detailed)", currOctaves == 3))
            {
                MarkDirty("Set Noise Octaves 3");
                _targetInteraction.NoiseOctaves = 3;
            }
            GUILayout.EndHorizontal();

            if (_targetInteraction.NoiseType == 1)
            {
                EditorGUI.BeginChangeCheck();
                float newWarp = EditorGUILayout.Slider("Slosh Surge (Tilt)", _targetInteraction.WarpStrength, 0.0f, 1.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    MarkDirty("Change Slosh Surge");
                    _targetInteraction.WarpStrength = newWarp;
                }
            }
        }

        private void DrawColorRampSection()
        {
            EditorGUILayout.LabelField("COLOR RAMP (NEON SLIME) & HEIGHT PROFILE", EditorStyles.boldLabel);

            if (_activeGradient == null)
            {
                _activeGradient = GradientTextureGenerator.CreateReferenceNeonGradient();
            }

            EditorGUI.BeginChangeCheck();
            _activeGradient = EditorGUILayout.GradientField("Neon Gradient Ramp", _activeGradient);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Gradient Ramp");
                _targetInteraction.SetCustomGradient(_activeGradient);
            }

            EditorGUILayout.Space(2);

            EditorGUI.BeginChangeCheck();
            float newMinH = EditorGUILayout.Slider("Min Height (Trough)", _targetInteraction.MinHeight, -3.0f, 1.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Min Height");
                _targetInteraction.MinHeight = newMinH;
            }

            EditorGUI.BeginChangeCheck();
            float newMaxH = EditorGUILayout.Slider("Max Height (Crest)", _targetInteraction.MaxHeight, -0.5f, 3.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Max Height");
                _targetInteraction.MaxHeight = newMaxH;
            }

            EditorGUI.BeginChangeCheck();
            float newFP = EditorGUILayout.Slider("Fresnel Exponent", _targetInteraction.FresnelPower, 0.2f, 12.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Fresnel Exponent");
                _targetInteraction.FresnelPower = newFP;
            }

            EditorGUI.BeginChangeCheck();
            float newFI = EditorGUILayout.Slider("Fresnel Intensity", _targetInteraction.FresnelIntensity, 0.0f, 3.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Fresnel Intensity");
                _targetInteraction.FresnelIntensity = newFI;
            }

            EditorGUI.BeginChangeCheck();
            float newFB = EditorGUILayout.Slider("Fresnel Bias", _targetInteraction.FresnelBias, -0.5f, 0.5f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Fresnel Bias");
                _targetInteraction.FresnelBias = newFB;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("PBR SURFACE & GLOSS", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            float newRough = EditorGUILayout.Slider("Roughness / Gloss", _targetInteraction.Roughness, 0.01f, 1.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Roughness");
                _targetInteraction.Roughness = newRough;
            }

            EditorGUI.BeginChangeCheck();
            float newMetal = EditorGUILayout.Slider("Metallic", _targetInteraction.Metallic, 0.0f, 1.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Metallic");
                _targetInteraction.Metallic = newMetal;
            }

            EditorGUI.BeginChangeCheck();
            float newSpec = EditorGUILayout.Slider("Specular Intensity", _targetInteraction.SpecularIntensity, 0.0f, 5.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Specular Intensity");
                _targetInteraction.SpecularIntensity = newSpec;
            }

            EditorGUI.BeginChangeCheck();
            float newRefl = EditorGUILayout.Slider("Reflection Intensity", _targetInteraction.ReflectionIntensity, 0.0f, 5.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Reflection Intensity");
                _targetInteraction.ReflectionIntensity = newRefl;
            }

            EditorGUI.BeginChangeCheck();
            bool newUseCube = EditorGUILayout.Toggle("Use Custom Cubemap", _targetInteraction.UseCustomCubemap);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Toggle Custom Cubemap");
                _targetInteraction.UseCustomCubemap = newUseCube;
            }

            if (_targetInteraction.UseCustomCubemap)
            {
                EditorGUI.BeginChangeCheck();
                Cubemap newCube = (Cubemap)EditorGUILayout.ObjectField("Custom Cubemap", _targetInteraction.CustomCubemap, typeof(Cubemap), false);
                if (EditorGUI.EndChangeCheck())
                {
                    MarkDirty("Change Custom Cubemap");
                    _targetInteraction.CustomCubemap = newCube;
                }
            }
        }

        private static bool _showAdvancedPigment = false;

        private void DrawKineticStrokeColorSection()
        {
            EditorGUILayout.LabelField("KINETIC STROKE & WAVE COLOR", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox("Water elevation and motion capture high-water color from the active ramp, softly diffusing and dissolving back into the resting fluid.", MessageType.None);

            EditorGUI.BeginChangeCheck();
            FluidColorBlendMode newBlend = (FluidColorBlendMode)EditorGUILayout.EnumPopup("Color Blend Mode", _targetInteraction.ColorBlendMode);
            float newDecay = EditorGUILayout.Slider("Dissolve Rate (Decay)", _targetInteraction.PigmentDecay, 0.05f, 3.0f);
            float newDiff = EditorGUILayout.Slider("Diffusion (Softness)", _targetInteraction.PigmentDiffusion, 0.0f, 6.0f);
            float newDensity = EditorGUILayout.Slider("Color Density", _targetInteraction.PigmentDensity, 0.5f, 15.0f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Kinetic Stroke Color");
                _targetInteraction.ColorBlendMode = newBlend;
                _targetInteraction.PigmentDecay = newDecay;
                _targetInteraction.PigmentDiffusion = newDiff;
                _targetInteraction.PigmentDensity = newDensity;
            }

            EditorGUILayout.Space(3);

            EditorGUILayout.Space(3);

            // Collapsible advanced physics foldout (hidden by default to keep UI clean and uncluttered)
            _showAdvancedPigment = EditorGUILayout.Foldout(_showAdvancedPigment, "Advanced Internal Dynamics", true);
            if (_showAdvancedPigment)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                bool optAbs = EditorGUILayout.Toggle("Beer-Lambert Optics", _targetInteraction.EnableOpticalAbsorption);
                float vMin = EditorGUILayout.Slider("Velocity Sensitivity Min", _targetInteraction.PigmentVelocityMin, 0.01f, 1.0f);
                float vMax = EditorGUILayout.Slider("Velocity Saturation Max", _targetInteraction.PigmentVelocityMax, 0.2f, 5.0f);
                float inj = EditorGUILayout.Slider("Injection Rate", _targetInteraction.PigmentInjection, 0.5f, 15.0f);
                float depthScale = EditorGUILayout.Slider("Optical Depth Scale", _targetInteraction.OpticalDepthScale, 0.1f, 10.0f);
                float depthInf = EditorGUILayout.Slider("Depth Influence", _targetInteraction.OpticalDepthFromHeight, 0.0f, 2.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    MarkDirty("Change Advanced Pigment Settings");
                    _targetInteraction.EnableOpticalAbsorption = optAbs;
                    _targetInteraction.PigmentVelocityMin = vMin;
                    _targetInteraction.PigmentVelocityMax = vMax;
                    _targetInteraction.PigmentInjection = inj;
                    _targetInteraction.OpticalDepthScale = depthScale;
                    _targetInteraction.OpticalDepthFromHeight = depthInf;
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("COLOR DEBUG VISUALIZER", EditorStyles.boldLabel);

            int currDebug = _targetInteraction.ColorDebugMode;
            GUILayout.BeginHorizontal();
            if (DrawSmallPreset("Final PBR", currDebug == 5))
            {
                MarkDirty("Set Debug Mode Final PBR");
                _targetInteraction.ColorDebugMode = 5;
            }
            if (DrawSmallPreset("Stroke Ramp", currDebug == 4))
            {
                MarkDirty("Set Debug Mode Stroke Ramp");
                _targetInteraction.ColorDebugMode = 4;
            }
            if (DrawSmallPreset("Height Map", currDebug == 0))
            {
                MarkDirty("Set Debug Mode Height");
                _targetInteraction.ColorDebugMode = 0;
            }
            if (DrawSmallPreset("Pigment (C)", currDebug == 8))
            {
                MarkDirty("Set Debug Mode Pigment C");
                _targetInteraction.ColorDebugMode = 8;
            }
            if (DrawSmallPreset("Unlit Base", currDebug == 7))
            {
                MarkDirty("Set Debug Mode Unlit Base");
                _targetInteraction.ColorDebugMode = 7;
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawBrushSection()
        {
            EditorGUILayout.LabelField("INTERACTION BRUSH", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            float newRadius = EditorGUILayout.Slider("Brush Radius (Meters)", _targetInteraction.BrushRadius, 0.10f, 3.00f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Brush Radius");
                _targetInteraction.BrushRadius = newRadius;
            }

            EditorGUI.BeginChangeCheck();
            float newStrength = EditorGUILayout.Slider("Brush Strength", _targetInteraction.BrushStrength, 0.10f, 2.00f);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Brush Strength");
                _targetInteraction.BrushStrength = newStrength;
            }
        }

        private void DrawDomainAndGridSection()
        {
            EditorGUILayout.LabelField("GRID & DOMAIN DIMENSIONS", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            Vector2 currentSize = _targetInteraction.DomainSize;
            Vector2 newSize = EditorGUILayout.Vector2Field("Domain Size (Meters)", currentSize);
            newSize.x = Mathf.Max(0.5f, newSize.x);
            newSize.y = Mathf.Max(0.5f, newSize.y);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Change Domain Size");
                _targetInteraction.DomainSize = newSize;
            }

            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Size Presets");
            if (DrawSmallPreset("10x10", Mathf.Approximately(currentSize.x, 10f) && Mathf.Approximately(currentSize.y, 10f)))
            {
                MarkDirty("Preset 10x10");
                _targetInteraction.DomainSize = new Vector2(10f, 10f);
            }
            if (DrawSmallPreset("20x20", Mathf.Approximately(currentSize.x, 20f) && Mathf.Approximately(currentSize.y, 20f)))
            {
                MarkDirty("Preset 20x20");
                _targetInteraction.DomainSize = new Vector2(20f, 20f);
            }
            if (DrawSmallPreset("20x10", Mathf.Approximately(currentSize.x, 20f) && Mathf.Approximately(currentSize.y, 10f)))
            {
                MarkDirty("Preset 20x10");
                _targetInteraction.DomainSize = new Vector2(20f, 10f);
            }
            if (DrawSmallPreset("30x15", Mathf.Approximately(currentSize.x, 30f) && Mathf.Approximately(currentSize.y, 15f)))
            {
                MarkDirty("Preset 30x15");
                _targetInteraction.DomainSize = new Vector2(30f, 15f);
            }
            GUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            bool preserveSquare = EditorGUILayout.Toggle("Preserve Square Texels", _targetInteraction.PreserveSquareTexels);
            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty("Toggle Preserve Square Texels");
                _targetInteraction.PreserveSquareTexels = preserveSquare;
            }

            ProceduralPlane plane = _targetInteraction.GetComponent<ProceduralPlane>();
            if (plane != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Mesh Tessellation: {plane.SegmentsX}x{plane.SegmentsZ} quads ({plane.SegmentsX * plane.SegmentsZ:N0} total quads, {(plane.SegmentsX + 1) * (plane.SegmentsZ + 1):N0} vertices)", EditorStyles.miniLabel);
                float quadSizeX = currentSize.x / plane.SegmentsX;
                float quadSizeZ = currentSize.y / plane.SegmentsZ;
                Vector2Int simRes = _targetInteraction.SimulationResolution;
                float texelsPerMeterX = _targetInteraction.TexelDensityX;
                float texelsPerMeterZ = _targetInteraction.TexelDensityZ;
                EditorGUILayout.LabelField($"Quad Size: {quadSizeX:F2}m x {quadSizeZ:F2}m | RT: {simRes.x}x{simRes.y} ({texelsPerMeterX:F1} x {texelsPerMeterZ:F1} tex/m)", EditorStyles.miniLabel);

                if (GUILayout.Button("Regenerate Plane Mesh", EditorStyles.miniButton))
                {
                    plane.SetDimensions(currentSize, true);
                    EditorUtility.SetDirty(plane);
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawRenderingSection()
        {
            EditorGUILayout.LabelField("RENDERING PIPELINE", EditorStyles.boldLabel);

            Vector2Int currentSimRes = _targetInteraction.SimulationResolution;
            EditorGUILayout.LabelField($"Active RT Resolution: {currentSimRes.x} x {currentSimRes.y} (Base: {_targetInteraction.TextureResolution})", EditorStyles.miniLabel);

            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Base RT Resolution");
            if (DrawSmallPreset("64", _targetInteraction.TextureResolution == 64))
            {
                MarkDirty("Set RT Resolution 64");
                _targetInteraction.SetResolution(64);
            }
            if (DrawSmallPreset("128 (Silk)", _targetInteraction.TextureResolution == 128))
            {
                MarkDirty("Set RT Resolution 128");
                _targetInteraction.SetResolution(128);
            }
            if (DrawSmallPreset("256", _targetInteraction.TextureResolution == 256))
            {
                MarkDirty("Set RT Resolution 256");
                _targetInteraction.SetResolution(256);
            }
            if (DrawSmallPreset("512", _targetInteraction.TextureResolution == 512))
            {
                MarkDirty("Set RT Resolution 512");
                _targetInteraction.SetResolution(512);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Normal Modes");
            int currNormal = _targetInteraction.NormalMode;
            if (DrawSmallPreset("0: Sobel", currNormal == 0))
            {
                MarkDirty("Set Normal Mode Sobel");
                _targetInteraction.NormalMode = 0;
            }
            if (DrawSmallPreset("1: Diagonal", currNormal == 1))
            {
                MarkDirty("Set Normal Mode Diagonal");
                _targetInteraction.NormalMode = 1;
            }
            if (DrawSmallPreset("2: Mesh", currNormal == 2))
            {
                MarkDirty("Set Normal Mode Mesh");
                _targetInteraction.NormalMode = 2;
            }
            if (DrawSmallPreset("3: ddx/ddy", currNormal == 3))
            {
                MarkDirty("Set Normal Mode ddx/ddy");
                _targetInteraction.NormalMode = 3;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawDisplacementMapPreview()
        {
            EditorGUILayout.LabelField("DISPLACEMENT TEXTURE (LIVE RT)", EditorStyles.boldLabel);

            RenderTexture rt = _targetInteraction.CurrentDisplacementTexture;
            if (rt != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                Rect previewRect = GUILayoutUtility.GetRect(160, 160, GUILayout.ExpandWidth(true));
                float size = Mathf.Min(previewRect.width, 160f);
                Rect centeredRect = new Rect(previewRect.x + (previewRect.width - size) * 0.5f, previewRect.y, size, size);

                EditorGUI.DrawPreviewTexture(centeredRect, rt, null, ScaleMode.ScaleToFit);
                EditorGUILayout.LabelField($"Format: {rt.width}x{rt.height} {rt.format}  |  Filtering: {rt.filterMode}", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.HelpBox("Displacement RenderTexture is initialized on Play Mode.", MessageType.None);
            }
        }

        private void DrawDiagnosticsSection()
        {
            EditorGUILayout.LabelField("SIMULATION DIAGNOSTICS", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Input Source", _targetInteraction.DebugInputSource);
            EditorGUILayout.LabelField("Surface Hit", _targetInteraction.DebugHitSuccess ? "Valid (Over Plane)" : "Miss");
            EditorGUILayout.LabelField("Wave Damping", _targetInteraction.WaveDamping.ToString("F1"));
            EditorGUILayout.LabelField("Restoring Force", _targetInteraction.SpringStiffness.ToString("F1"));
            EditorGUILayout.LabelField("Yield Plasticity", _targetInteraction.Plasticity.ToString("F2"));

            EditorGUILayout.EndVertical();
        }

        private void DrawSaveSection()
        {
            if (!Application.isPlaying)
            {
                GUI.backgroundColor = new Color(0.2f, 0.9f, 1.0f);
                if (GUILayout.Button("Save Scene & Material Settings (Ctrl+S)", GUILayout.Height(28)))
                {
                    MarkDirty("Save All Settings");
                    UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                    AssetDatabase.SaveAssets();
                    Debug.Log("<color=#00E5FF>[FluidDynamicsWindow]</color> Scene and material settings successfully saved to disk!");
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                EditorGUILayout.HelpBox("Play Mode active: adjustments react live in real time.", MessageType.Info);
            }
        }

        private void MarkDirty(string undoName = "Change Fluid Simulation Setting")
        {
            if (_targetInteraction == null) return;

            Undo.RecordObject(_targetInteraction, undoName);
            EditorUtility.SetDirty(_targetInteraction);

            Material mat = _targetInteraction.PlaneMaterial;
            if (mat != null)
            {
                Undo.RecordObject(mat, undoName);
                EditorUtility.SetDirty(mat);
            }

            if (!Application.isPlaying)
            {
                if (_targetInteraction.gameObject.scene.IsValid())
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(_targetInteraction.gameObject.scene);
                }
            }
        }

        private static GUIStyle _richLabelStyle;
        private static GUIStyle GetRichLabelStyle()
        {
            if (_richLabelStyle == null)
            {
                _richLabelStyle = new GUIStyle(EditorStyles.label) { richText = true };
            }
            return _richLabelStyle;
        }

        private static GUIStyle _richLabelRightStyle;
        private static GUIStyle GetRichLabelRightStyle()
        {
            if (_richLabelRightStyle == null)
            {
                _richLabelRightStyle = new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleRight };
            }
            return _richLabelRightStyle;
        }
    }
}
#endif
