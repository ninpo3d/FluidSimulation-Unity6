#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Flexus.FluidSimulation.Editor
{
    /// <summary>
    /// Custom Material Inspector for fluid surface materials.
    /// Groups shader properties into categorized sections (Color Ramp, PBR Lighting,
    /// Procedural Waves, Normal Reconstruction, Reflections, Emission) and provides one-click palette presets.
    /// Dedicated simulation properties (Viscosity, brush, domain) are owned by FluidSurfaceInteraction.
    /// </summary>
    public class FluidMaterialEditor : ShaderGUI
    {
        private static bool _rampFold = true;
        private static bool _advancedPigmentFold = false;
        private static bool _pigmentOpticsFold = true;
        private static bool _debugFold = true;
        private static bool _pbrFold = true;
        private static bool _wavesFold = true;
        private static bool _normalFold = true;
        private static bool _reflectionsFold = false;
        private static bool _emissionFold = false;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material targetMat = materialEditor.target as Material;
            if (targetMat == null) return;

            // 1. Unified Controller Banner
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b><size=12><color=#00E5FF>FLEXUS</color> FLUID MATERIAL (UNIFIED RAMP & PBR)</size></b>", GetRichStyle());
            if (GUILayout.Button("Open Controller Window", EditorStyles.miniButton, GUILayout.Width(160)))
            {
                FluidDynamicsWindow.Open();
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            // 2. Color Ramp & Bounding Box Section
            _rampFold = EditorGUILayout.BeginFoldoutHeaderGroup(_rampFold, "COLOR RAMP & HEIGHT PROFILE");
            if (_rampFold)
            {
                MaterialProperty rampMap = FindProperty("_RampMap", properties, false);
                if (rampMap != null)
                {
                    materialEditor.TexturePropertySingleLine(new GUIContent("Color Ramp (1D Texture)"), rampMap);
                }

                EditorGUILayout.Space(2);

                EditorGUILayout.Space(2);

                DrawRange(materialEditor, properties, "_MinHeight", "Min Height (Trough)");
                DrawRange(materialEditor, properties, "_MaxHeight", "Max Height (Crest)");
                DrawRange(materialEditor, properties, "_FresnelPower", "Fresnel Exponent");
                DrawRange(materialEditor, properties, "_FresnelBias", "Fresnel Bias");
                DrawRange(materialEditor, properties, "_FresnelIntensity", "Fresnel Intensity");
                DrawRange(materialEditor, properties, "_WaveColorModulation", "Wave Crest Glow");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // 3. Kinetic Stroke & Wave Color
            _pigmentOpticsFold = EditorGUILayout.BeginFoldoutHeaderGroup(_pigmentOpticsFold, "KINETIC STROKE & WAVE COLOR");
            if (_pigmentOpticsFold)
            {
                EditorGUILayout.HelpBox("Water elevation and motion capture high-water color from the active ramp, softly diffusing and dissolving back into resting fluid.", MessageType.None);

                MaterialProperty blendMode = FindProperty("_ColorBlendMode", properties, false);
                if (blendMode != null)
                {
                    int currBlend = Mathf.Clamp(Mathf.RoundToInt(blendMode.floatValue), 0, 6);
                    int newBlend = EditorGUILayout.Popup("Color Blend Mode", currBlend, new string[] {
                        "0: Normal (Alpha Lerp)",
                        "1: Additive (Linear Dodge)",
                        "2: Multiply",
                        "3: Screen",
                        "4: Overlay",
                        "5: Soft Light",
                        "6: Color Dodge"
                    });
                    if (newBlend != currBlend)
                    {
                        blendMode.floatValue = newBlend;
                    }
                }

                DrawRange(materialEditor, properties, "_PigmentDensity", "Color Density");

                _advancedPigmentFold = EditorGUILayout.Foldout(_advancedPigmentFold, "Advanced Optical Settings", true);
                if (_advancedPigmentFold)
                {
                    EditorGUI.indentLevel++;
                    MaterialProperty optAbs = FindProperty("_EnableOpticalAbsorption", properties, false);
                    if (optAbs != null) materialEditor.ShaderProperty(optAbs, "Beer-Lambert Optical Absorption");

                    DrawRange(materialEditor, properties, "_OpticalDepthScale", "Optical Depth Scale");
                    DrawRange(materialEditor, properties, "_OpticalDepthFromHeight", "Depth Influence (Trough Thicker)");
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // 5. Color Component Debug Modes
            _debugFold = EditorGUILayout.BeginFoldoutHeaderGroup(_debugFold, "COLOR COMPONENT DEBUG MODES");
            if (_debugFold)
            {
                MaterialProperty debugMode = FindProperty("_ColorDebugMode", properties, false);
                if (debugMode != null)
                {
                    int currDebug = Mathf.Clamp(Mathf.RoundToInt(debugMode.floatValue), 0, 8);
                    int newDebug = EditorGUILayout.Popup("Debug Visualizer", currDebug, new string[] {
                        "0: Debug A - Height Map",
                        "1: Debug B - Velocity Magnitude (|v|)",
                        "2: Debug C - Signed Velocity (v)",
                        "3: Signed Vel -> Ramp",
                        "4: Captured Stroke Ramp Color",
                        "5: Final PBR Composition",
                        "6: Surface Normals",
                        "7: Unlit Blended Base Color",
                        "8: Pigment C (Grayscale)"
                    });
                    if (newDebug != currDebug)
                    {
                        debugMode.floatValue = newDebug;
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // 5. PBR Surface & Specular Highlights
            _pbrFold = EditorGUILayout.BeginFoldoutHeaderGroup(_pbrFold, "PBR SURFACE & GLOSS");
            if (_pbrFold)
            {
                DrawRange(materialEditor, properties, "_Roughness", "Roughness / Gloss");
                DrawRange(materialEditor, properties, "_Metallic", "Metallic");
                DrawRange(materialEditor, properties, "_DiffuseIntensity", "Diffuse Intensity");

                MaterialProperty specColor = FindProperty("_SpecularColor", properties, false);
                if (specColor != null) materialEditor.ColorProperty(specColor, "Specular Tint");
                DrawRange(materialEditor, properties, "_SpecularIntensity", "Specular Intensity");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // 4. Procedural Waves (Ambient Motion)
            _wavesFold = EditorGUILayout.BeginFoldoutHeaderGroup(_wavesFold, "PROCEDURAL WAVES (AMBIENT MOTION)");
            if (_wavesFold)
            {
                MaterialProperty noiseType = FindProperty("_NoiseType", properties, false);
                if (noiseType != null)
                {
                    int currNoise = Mathf.RoundToInt(noiseType.floatValue);
                    int newNoise = EditorGUILayout.Popup("Wave Algorithm", currNoise, new string[] { "0: Classic Simplex", "1: Basin Slosh (Ref)" });
                    if (newNoise != currNoise)
                    {
                        noiseType.floatValue = newNoise;
                    }
                }

                DrawRange(materialEditor, properties, "_NoiseScale", "Wave Scale / Frequency");
                DrawRange(materialEditor, properties, "_NoiseSpeed", "Wave Animation Speed");
                DrawRange(materialEditor, properties, "_NoiseAmplitude", "Wave Amplitude / Height");
                DrawRange(materialEditor, properties, "_WarpStrength", "Slosh Surge / Tilt");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // 5. Normal Reconstruction
            _normalFold = EditorGUILayout.BeginFoldoutHeaderGroup(_normalFold, "NORMAL RECONSTRUCTION");
            if (_normalFold)
            {
                MaterialProperty normalMode = FindProperty("_NormalMode", properties, false);
                if (normalMode != null)
                {
                    int currNorm = Mathf.Clamp(Mathf.RoundToInt(normalMode.floatValue), 0, 3);
                    int newNorm = EditorGUILayout.Popup("Normal Mode", currNorm, new string[] {
                        "0: Sobel 8-Tap",
                        "1: Diagonal 4-Tap",
                        "2: Vertex Mesh (Gouraud)",
                        "3: Screen-Space Derivatives"
                    });
                    if (newNorm != currNorm)
                    {
                        normalMode.floatValue = newNorm;
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // 6. Reflections & Cubemap
            _reflectionsFold = EditorGUILayout.BeginFoldoutHeaderGroup(_reflectionsFold, "REFLECTIONS & CUBEMAP");
            if (_reflectionsFold)
            {
                MaterialProperty useCube = FindProperty("_UseCustomCubemap", properties, false);
                if (useCube != null) materialEditor.ShaderProperty(useCube, "Use Custom Cubemap");

                if (useCube != null && useCube.floatValue > 0.5f)
                {
                    MaterialProperty cube = FindProperty("_CustomCubemap", properties, false);
                    if (cube != null) materialEditor.TexturePropertySingleLine(new GUIContent("Custom Cubemap"), cube);
                }

                DrawRange(materialEditor, properties, "_ReflectionIntensity", "Reflection Intensity");
                MaterialProperty refTint = FindProperty("_ReflectionTint", properties, false);
                if (refTint != null) materialEditor.ColorProperty(refTint, "Reflection Tint");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // 7. Glow & Emission
            _emissionFold = EditorGUILayout.BeginFoldoutHeaderGroup(_emissionFold, "GLOW & EMISSION");
            if (_emissionFold)
            {
                MaterialProperty emCol = FindProperty("_EmissionColor", properties, false);
                if (emCol != null) materialEditor.ColorProperty(emCol, "Emission Tint");
                DrawRange(materialEditor, properties, "_EmissionIntensity", "Emission Intensity");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }



        private void DrawRange(MaterialEditor editor, MaterialProperty[] properties, string name, string label)
        {
            MaterialProperty prop = FindProperty(name, properties, false);
            if (prop != null)
            {
                editor.RangeProperty(prop, label);
            }
        }

        private void DrawColor(MaterialEditor editor, MaterialProperty[] properties, string name, string label)
        {
            MaterialProperty prop = FindProperty(name, properties, false);
            if (prop != null)
            {
                editor.ColorProperty(prop, label);
            }
        }

        private static GUIStyle _richStyle;
        private static GUIStyle GetRichStyle()
        {
            if (_richStyle == null)
            {
                _richStyle = new GUIStyle(EditorStyles.boldLabel) { richText = true };
            }
            return _richStyle;
        }
    }
}
#endif
