using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Flexus.FluidSimulation
{
    [DisallowMultipleComponent]
    public class FluidMobileUI : MonoBehaviour
    {
        private static FluidMobileUI _instance;
        public static FluidMobileUI Instance => _instance;

        [Header("Target Interaction")]
        [SerializeField] private FluidSurfaceInteraction _interaction;

        [Header("Initial State")]
        [SerializeField] private bool _isExpanded = true;

        // UI hierarchy references
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private RectTransform _topPanelRect;
        private RectTransform _contentContainerRect;
        private Text _toggleButtonText;
        private Text _fpsText;

        private Slider _viscositySlider;
        private Text _viscosityValueText;

        private Slider _brushSizeSlider;
        private Text _brushSizeValueText;

        private Slider _brushForceSlider;
        private Text _brushForceValueText;

        // Generated procedural sprites
        private readonly List<Sprite> _generatedSprites = new List<Sprite>();
        private readonly List<Texture2D> _generatedTextures = new List<Texture2D>();

        // Layout constants (in reference canvas 1080p pixels)
        private const float CanvasReferenceWidth = 1080f;
        private const float CanvasReferenceHeight = 1920f;
        private const float CollapsedHeight = 72f;
        private const float ExpandedHeight = 360f;

        // FPS counter variables
        private float _fpsAccumulator = 0f;
        private int _fpsFrames = 0;
        private float _fpsTimeLeft = 0.5f;
        private float _currentFps = 60f;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            ResolveInteractionTarget();
            EnsureEventSystemExists();
            BuildUserInterface();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            foreach (var sprite in _generatedSprites)
            {
                if (sprite != null) Destroy(sprite);
            }
            _generatedSprites.Clear();

            foreach (var tex in _generatedTextures)
            {
                if (tex != null) Destroy(tex);
            }
            _generatedTextures.Clear();
        }

        private void Update()
        {
            UpdateFpsCounter();
            SyncUIFromParameters();
            UpdateSafeAreaPadding();
        }

        // Checks if pointer is over the HUD or active UI element
        public static bool IsPointerOverUI(Vector2 screenPoint)
        {
            if (_instance == null || _instance._topPanelRect == null) return false;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return true;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_instance._topPanelRect, screenPoint, null))
            {
                return true;
            }

            return false;
        }

        private void ResolveInteractionTarget()
        {
            if (_interaction == null)
            {
                _interaction = GetComponent<FluidSurfaceInteraction>();
                if (_interaction == null)
                {
                    _interaction = Object.FindFirstObjectByType<FluidSurfaceInteraction>();
                }
            }
        }

        private void EnsureEventSystemExists()
        {
            EventSystem existingEs = Object.FindFirstObjectByType<EventSystem>();
            if (existingEs == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();

#if ENABLE_INPUT_SYSTEM
                esGo.AddComponent<InputSystemUIInputModule>();
#else
                esGo.AddComponent<StandaloneInputModule>();
#endif
            }
        }

        private void BuildUserInterface()
        {
            Font defaultFont = GetBuiltinFont();

            // 1. Root Canvas
            GameObject canvasGo = new GameObject("Fluid_MobileCanvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 999;

            _scaler = canvasGo.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(CanvasReferenceWidth, CanvasReferenceHeight);
            _scaler.matchWidthOrHeight = 0.0f; // Lock width to reference width

            canvasGo.AddComponent<GraphicRaycaster>();

            // 2. Full-Width Top Glass Panel
            GameObject panelGo = new GameObject("TopGlassPanel");
            panelGo.transform.SetParent(canvasGo.transform, false);

            _topPanelRect = panelGo.AddComponent<RectTransform>();
            _topPanelRect.anchorMin = new Vector2(0f, 1f);
            _topPanelRect.anchorMax = new Vector2(1f, 1f);
            _topPanelRect.pivot = new Vector2(0.5f, 1f);
            _topPanelRect.sizeDelta = new Vector2(0f, _isExpanded ? ExpandedHeight : CollapsedHeight);
            _topPanelRect.anchoredPosition = Vector2.zero;

            Image panelBg = panelGo.AddComponent<Image>();
            panelBg.color = new Color(0.04f, 0.07f, 0.12f, 0.82f); // Deep frosted translucent glass

            // Bottom subtle border accent
            GameObject borderGo = new GameObject("BottomBorderAccent");
            borderGo.transform.SetParent(panelGo.transform, false);
            RectTransform borderRect = borderGo.AddComponent<RectTransform>();
            borderRect.anchorMin = new Vector2(0f, 0f);
            borderRect.anchorMax = new Vector2(1f, 0f);
            borderRect.pivot = new Vector2(0.5f, 0f);
            borderRect.sizeDelta = new Vector2(0f, 2f);
            borderRect.anchoredPosition = Vector2.zero;
            Image borderImg = borderGo.AddComponent<Image>();
            borderImg.color = new Color(0.30f, 0.65f, 1.0f, 0.40f);

            // 3. Top Header Bar (Height = 72)
            GameObject headerBarGo = new GameObject("HeaderBar");
            headerBarGo.transform.SetParent(panelGo.transform, false);
            RectTransform headerRect = headerBarGo.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, CollapsedHeight);
            headerRect.anchoredPosition = Vector2.zero;

            // Title & FPS
            GameObject titleGo = new GameObject("TitleText");
            titleGo.transform.SetParent(headerBarGo.transform, false);
            RectTransform titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(0.65f, 1f);
            titleRect.offsetMin = new Vector2(32f, 0f);
            titleRect.offsetMax = new Vector2(0f, 0f);

            Text titleText = titleGo.AddComponent<Text>();
            titleText.font = defaultFont;
            titleText.text = "FLUID DYNAMICS";
            titleText.fontSize = 24;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = new Color(0.65f, 0.88f, 1.0f, 1.0f);
            titleText.alignment = TextAnchor.MiddleLeft;

            // FPS Counter badge
            GameObject fpsGo = new GameObject("FpsText");
            fpsGo.transform.SetParent(headerBarGo.transform, false);
            RectTransform fpsRect = fpsGo.AddComponent<RectTransform>();
            fpsRect.anchorMin = new Vector2(0.42f, 0f);
            fpsRect.anchorMax = new Vector2(0.65f, 1f);
            fpsRect.offsetMin = Vector2.zero;
            fpsRect.offsetMax = Vector2.zero;

            _fpsText = fpsGo.AddComponent<Text>();
            _fpsText.font = defaultFont;
            _fpsText.text = "60 FPS";
            _fpsText.fontSize = 18;
            _fpsText.fontStyle = FontStyle.Normal;
            _fpsText.color = new Color(0.45f, 0.70f, 0.90f, 0.85f);
            _fpsText.alignment = TextAnchor.MiddleLeft;

            // Toggle Expand Button (Right side of header)
            Sprite pillSprite = CreateRoundedRectSprite(180, 52, 26f, new Color(0.16f, 0.25f, 0.40f, 0.95f));
            GameObject toggleBtnGo = new GameObject("ToggleMenuButton");
            toggleBtnGo.transform.SetParent(headerBarGo.transform, false);
            RectTransform toggleBtnRect = toggleBtnGo.AddComponent<RectTransform>();
            toggleBtnRect.anchorMin = new Vector2(1f, 0.5f);
            toggleBtnRect.anchorMax = new Vector2(1f, 0.5f);
            toggleBtnRect.pivot = new Vector2(1f, 0.5f);
            toggleBtnRect.sizeDelta = new Vector2(180f, 50f);
            toggleBtnRect.anchoredPosition = new Vector2(-28f, 0f);

            Image toggleBtnImg = toggleBtnGo.AddComponent<Image>();
            toggleBtnImg.sprite = pillSprite;
            toggleBtnImg.type = Image.Type.Sliced;
            toggleBtnImg.color = Color.white;

            Button toggleBtn = toggleBtnGo.AddComponent<Button>();
            ColorBlock colors = toggleBtn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.8f, 0.9f, 1.0f);
            colors.pressedColor = new Color(0.35f, 0.65f, 1.0f);
            toggleBtn.colors = colors;
            toggleBtn.onClick.AddListener(ToggleExpanded);

            GameObject toggleTxtGo = new GameObject("BtnLabel");
            toggleTxtGo.transform.SetParent(toggleBtnGo.transform, false);
            RectTransform toggleTxtRect = toggleTxtGo.AddComponent<RectTransform>();
            toggleTxtRect.anchorMin = Vector2.zero;
            toggleTxtRect.anchorMax = Vector2.one;
            toggleTxtRect.sizeDelta = Vector2.zero;

            _toggleButtonText = toggleTxtGo.AddComponent<Text>();
            _toggleButtonText.font = defaultFont;
            _toggleButtonText.fontSize = 19;
            _toggleButtonText.fontStyle = FontStyle.Bold;
            _toggleButtonText.color = new Color(0.75f, 0.92f, 1.0f);
            _toggleButtonText.alignment = TextAnchor.MiddleCenter;
            _toggleButtonText.text = _isExpanded ? "▲ HIDE" : "▼ CONTROLS";

            // 4. Collapsible Content Container
            GameObject contentGo = new GameObject("CollapsibleContent");
            contentGo.transform.SetParent(panelGo.transform, false);
            _contentContainerRect = contentGo.AddComponent<RectTransform>();
            _contentContainerRect.anchorMin = new Vector2(0f, 0f);
            _contentContainerRect.anchorMax = new Vector2(1f, 1f);
            _contentContainerRect.offsetMin = new Vector2(32f, 16f);
            _contentContainerRect.offsetMax = new Vector2(-32f, -CollapsedHeight);

            // Build Sliders & Buttons inside Content
            Sprite sliderTrackSprite = CreateRoundedRectSprite(400, 16, 8f, new Color(0.12f, 0.16f, 0.24f, 1.0f));
            Sprite sliderFillSprite = CreateRoundedRectSprite(400, 16, 8f, new Color(0.28f, 0.62f, 0.98f, 1.0f));
            Sprite sliderHandleSprite = CreateCircleSprite(36, new Color(0.45f, 0.80f, 1.0f, 1.0f));

            float curY = -12f;
            float rowSpacing = 68f;

            // Slider 1: Viscosity
            _viscositySlider = CreateSliderRow(_contentContainerRect, "Viscosity", defaultFont,
                sliderTrackSprite, sliderFillSprite, sliderHandleSprite,
                0.0f, 1.0f, curY, out _viscosityValueText);
            _viscositySlider.onValueChanged.AddListener(val =>
            {
                if (_interaction != null) _interaction.Viscosity = val;
                if (_viscosityValueText != null) _viscosityValueText.text = val.ToString("F2");
            });
            curY -= rowSpacing;

            // Slider 2: Brush Size
            _brushSizeSlider = CreateSliderRow(_contentContainerRect, "Brush Size", defaultFont,
                sliderTrackSprite, sliderFillSprite, sliderHandleSprite,
                0.10f, 2.50f, curY, out _brushSizeValueText);
            _brushSizeSlider.onValueChanged.AddListener(val =>
            {
                if (_interaction != null) _interaction.BrushRadius = val;
                if (_brushSizeValueText != null) _brushSizeValueText.text = $"{val:F2} m";
            });
            curY -= rowSpacing;

            // Slider 3: Brush Force
            _brushForceSlider = CreateSliderRow(_contentContainerRect, "Brush Force", defaultFont,
                sliderTrackSprite, sliderFillSprite, sliderHandleSprite,
                0.10f, 2.00f, curY, out _brushForceValueText);
            _brushForceSlider.onValueChanged.AddListener(val =>
            {
                if (_interaction != null) _interaction.BrushStrength = val;
                if (_brushForceValueText != null) _brushForceValueText.text = val.ToString("F2");
            });
            curY -= (rowSpacing + 4f);

            // Action Buttons: Reset & Clear
            CreateActionButtons(_contentContainerRect, defaultFont, pillSprite, curY);

            // Set initial collapse state
            _contentContainerRect.gameObject.SetActive(_isExpanded);
        }

        private Slider CreateSliderRow(RectTransform parent, string label, Font font,
            Sprite trackSprite, Sprite fillSprite, Sprite handleSprite,
            float minVal, float maxVal, float yPos, out Text valText)
        {
            GameObject rowGo = new GameObject(label + "_Row");
            rowGo.transform.SetParent(parent, false);
            RectTransform rowRect = rowGo.AddComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.sizeDelta = new Vector2(0f, 56f);
            rowRect.anchoredPosition = new Vector2(0f, yPos);

            // Label
            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(rowGo.transform, false);
            RectTransform labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(0.7f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            labelRect.sizeDelta = new Vector2(0f, 24f);
            labelRect.anchoredPosition = Vector2.zero;

            Text lbl = labelGo.AddComponent<Text>();
            lbl.font = font;
            lbl.text = label;
            lbl.fontSize = 19;
            lbl.fontStyle = FontStyle.Bold;
            lbl.color = new Color(0.85f, 0.90f, 0.96f);
            lbl.alignment = TextAnchor.MiddleLeft;

            // Value Text
            GameObject valGo = new GameObject("Value");
            valGo.transform.SetParent(rowGo.transform, false);
            RectTransform valRect = valGo.AddComponent<RectTransform>();
            valRect.anchorMin = new Vector2(0.7f, 1f);
            valRect.anchorMax = new Vector2(1f, 1f);
            valRect.pivot = new Vector2(1f, 1f);
            valRect.sizeDelta = new Vector2(0f, 24f);
            valRect.anchoredPosition = Vector2.zero;

            valText = valGo.AddComponent<Text>();
            valText.font = font;
            valText.text = "0.00";
            valText.fontSize = 19;
            valText.fontStyle = FontStyle.Bold;
            valText.color = new Color(0.40f, 0.78f, 1.0f);
            valText.alignment = TextAnchor.MiddleRight;

            // Slider Object
            GameObject sliderGo = new GameObject("Slider");
            sliderGo.transform.SetParent(rowGo.transform, false);
            RectTransform sliderRect = sliderGo.AddComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0f);
            sliderRect.anchorMax = new Vector2(1f, 0f);
            sliderRect.pivot = new Vector2(0.5f, 0f);
            sliderRect.sizeDelta = new Vector2(0f, 28f);
            sliderRect.anchoredPosition = new Vector2(0f, 2f);

            Slider slider = sliderGo.AddComponent<Slider>();
            slider.minValue = minVal;
            slider.maxValue = maxVal;

            // Background Track
            GameObject bgGo = new GameObject("Background");
            bgGo.transform.SetParent(sliderGo.transform, false);
            RectTransform bgRect = bgGo.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.5f);
            bgRect.anchorMax = new Vector2(1f, 0.5f);
            bgRect.sizeDelta = new Vector2(0f, 14f);
            bgRect.anchoredPosition = Vector2.zero;
            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = trackSprite;
            bgImg.type = Image.Type.Sliced;

            // Fill Area
            GameObject fillAreaGo = new GameObject("Fill Area");
            fillAreaGo.transform.SetParent(sliderGo.transform, false);
            RectTransform fillAreaRect = fillAreaGo.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRect.sizeDelta = new Vector2(-28f, 14f);
            fillAreaRect.anchoredPosition = Vector2.zero;

            GameObject fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            RectTransform fillRect = fillGo.AddComponent<RectTransform>();
            fillRect.sizeDelta = Vector2.zero;
            Image fillImg = fillGo.AddComponent<Image>();
            fillImg.sprite = fillSprite;
            fillImg.type = Image.Type.Sliced;

            // Handle Area
            GameObject handleAreaGo = new GameObject("Handle Slide Area");
            handleAreaGo.transform.SetParent(sliderGo.transform, false);
            RectTransform handleAreaRect = handleAreaGo.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = new Vector2(0f, 0f);
            handleAreaRect.anchorMax = new Vector2(1f, 1f);
            handleAreaRect.sizeDelta = new Vector2(-28f, 0f);
            handleAreaRect.anchoredPosition = Vector2.zero;

            GameObject handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(handleAreaGo.transform, false);
            RectTransform handleRect = handleGo.AddComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(34f, 34f);
            Image handleImg = handleGo.AddComponent<Image>();
            handleImg.sprite = handleSprite;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;

            return slider;
        }

        private void CreateActionButtons(RectTransform parent, Font font, Sprite btnSprite, float yPos)
        {
            GameObject barGo = new GameObject("ActionsBar");
            barGo.transform.SetParent(parent, false);
            RectTransform barRect = barGo.AddComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(0f, 48f);
            barRect.anchoredPosition = new Vector2(0f, yPos);

            // Reset Button (Left half)
            GameObject resetGo = new GameObject("ResetButton");
            resetGo.transform.SetParent(barGo.transform, false);
            RectTransform resetRect = resetGo.AddComponent<RectTransform>();
            resetRect.anchorMin = new Vector2(0f, 0f);
            resetRect.anchorMax = new Vector2(0.48f, 1f);
            resetRect.sizeDelta = Vector2.zero;
            resetRect.anchoredPosition = Vector2.zero;

            Image resetImg = resetGo.AddComponent<Image>();
            resetImg.sprite = btnSprite;
            resetImg.type = Image.Type.Sliced;
            Button resetBtn = resetGo.AddComponent<Button>();
            resetBtn.onClick.AddListener(ResetDefaults);

            GameObject resetTxtGo = new GameObject("Label");
            resetTxtGo.transform.SetParent(resetGo.transform, false);
            RectTransform resetTxtRect = resetTxtGo.AddComponent<RectTransform>();
            resetTxtRect.anchorMin = Vector2.zero;
            resetTxtRect.anchorMax = Vector2.one;
            resetTxtRect.sizeDelta = Vector2.zero;
            Text resetTxt = resetTxtGo.AddComponent<Text>();
            resetTxt.font = font;
            resetTxt.fontSize = 18;
            resetTxt.fontStyle = FontStyle.Bold;
            resetTxt.color = Color.white;
            resetTxt.alignment = TextAnchor.MiddleCenter;
            resetTxt.text = "Reset Defaults";

            // Clear Button (Right half)
            GameObject clearGo = new GameObject("ClearButton");
            clearGo.transform.SetParent(barGo.transform, false);
            RectTransform clearRect = clearGo.AddComponent<RectTransform>();
            clearRect.anchorMin = new Vector2(0.52f, 0f);
            clearRect.anchorMax = new Vector2(1f, 1f);
            clearRect.sizeDelta = Vector2.zero;
            clearRect.anchoredPosition = Vector2.zero;

            Image clearImg = clearGo.AddComponent<Image>();
            clearImg.sprite = btnSprite;
            clearImg.type = Image.Type.Sliced;
            Button clearBtn = clearGo.AddComponent<Button>();
            clearBtn.onClick.AddListener(ClearFluidCanvas);

            GameObject clearTxtGo = new GameObject("Label");
            clearTxtGo.transform.SetParent(clearGo.transform, false);
            RectTransform clearTxtRect = clearTxtGo.AddComponent<RectTransform>();
            clearTxtRect.anchorMin = Vector2.zero;
            clearTxtRect.anchorMax = Vector2.one;
            clearTxtRect.sizeDelta = Vector2.zero;
            Text clearTxt = clearTxtGo.AddComponent<Text>();
            clearTxt.font = font;
            clearTxt.fontSize = 18;
            clearTxt.fontStyle = FontStyle.Bold;
            clearTxt.color = new Color(1.0f, 0.70f, 0.70f);
            clearTxt.alignment = TextAnchor.MiddleCenter;
            clearTxt.text = "Clear Waves";
        }

        public void ToggleExpanded()
        {
            _isExpanded = !_isExpanded;

            if (_topPanelRect != null)
            {
                _topPanelRect.sizeDelta = new Vector2(0f, _isExpanded ? ExpandedHeight : CollapsedHeight);
            }

            if (_contentContainerRect != null)
            {
                _contentContainerRect.gameObject.SetActive(_isExpanded);
            }

            if (_toggleButtonText != null)
            {
                _toggleButtonText.text = _isExpanded ? "▲ HIDE" : "▼ CONTROLS";
            }
        }

        private void ResetDefaults()
        {
            if (_interaction != null)
            {
                _interaction.Viscosity = 0.35f;
                _interaction.BrushRadius = 0.70f;
                _interaction.BrushStrength = 0.70f;
            }
            SyncUIFromParameters();
        }

        private void ClearFluidCanvas()
        {
            if (_interaction != null)
            {
                _interaction.ClearCanvas();
            }
        }

        private void SyncUIFromParameters()
        {
            if (_interaction == null) return;

            if (_viscositySlider != null && !Mathf.Approximately(_viscositySlider.value, _interaction.Viscosity))
            {
                _viscositySlider.SetValueWithoutNotify(_interaction.Viscosity);
                if (_viscosityValueText != null) _viscosityValueText.text = _interaction.Viscosity.ToString("F2");
            }

            if (_brushSizeSlider != null && !Mathf.Approximately(_brushSizeSlider.value, _interaction.BrushRadius))
            {
                _brushSizeSlider.SetValueWithoutNotify(_interaction.BrushRadius);
                if (_brushSizeValueText != null) _brushSizeValueText.text = $"{_interaction.BrushRadius:F2} m";
            }

            if (_brushForceSlider != null && !Mathf.Approximately(_brushForceSlider.value, _interaction.BrushStrength))
            {
                _brushForceSlider.SetValueWithoutNotify(_interaction.BrushStrength);
                if (_brushForceValueText != null) _brushForceValueText.text = _interaction.BrushStrength.ToString("F2");
            }
        }

        private void UpdateFpsCounter()
        {
            _fpsTimeLeft -= Time.unscaledDeltaTime;
            _fpsAccumulator += Time.timeScale / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            _fpsFrames++;

            if (_fpsTimeLeft <= 0.0f)
            {
                _currentFps = _fpsAccumulator / _fpsFrames;
                _fpsTimeLeft = 0.5f;
                _fpsAccumulator = 0.0f;
                _fpsFrames = 0;

                if (_fpsText != null)
                {
                    _fpsText.text = $"{Mathf.RoundToInt(_currentFps)} FPS";
                }
            }
        }

        private void UpdateSafeAreaPadding()
        {
            if (_topPanelRect == null) return;

            // Calculate safe area top offset relative to reference canvas height
            Rect safeArea = Screen.safeArea;
            float topOffsetPixels = Screen.height - (safeArea.y + safeArea.height);
            float normalizedTopOffset = (topOffsetPixels / Mathf.Max(1f, Screen.height)) * CanvasReferenceHeight;

            _topPanelRect.anchoredPosition = new Vector2(0f, -normalizedTopOffset);
        }

        private static Font GetBuiltinFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.fontdata");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 18);
            return font;
        }

        private Sprite CreateRoundedRectSprite(int width, int height, float cornerRadius, Color fill)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color transparent = new Color(fill.r, fill.g, fill.b, 0f);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Abs(x - width * 0.5f) - (width * 0.5f - cornerRadius));
                    float dy = Mathf.Max(0f, Mathf.Abs(y - height * 0.5f) - (height * 0.5f - cornerRadius));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(cornerRadius - dist + 0.5f);
                    tex.SetPixel(x, y, Color.Lerp(transparent, fill, alpha * fill.a));
                }
            }
            tex.Apply();

            _generatedTextures.Add(tex);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(cornerRadius, cornerRadius, cornerRadius, cornerRadius));
            _generatedSprites.Add(sprite);
            return sprite;
        }

        private Sprite CreateCircleSprite(int size, Color fill)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float radius = size * 0.5f;
            Color transparent = new Color(fill.r, fill.g, fill.b, 0f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                    float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    tex.SetPixel(x, y, Color.Lerp(transparent, fill, alpha * fill.a));
                }
            }
            tex.Apply();

            _generatedTextures.Add(tex);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            _generatedSprites.Add(sprite);
            return sprite;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Object.FindFirstObjectByType<FluidMobileUI>() != null) return;
            FluidSurfaceInteraction interaction = Object.FindFirstObjectByType<FluidSurfaceInteraction>();
            if (interaction != null)
            {
                interaction.gameObject.AddComponent<FluidMobileUI>();
            }
        }
    }
}
