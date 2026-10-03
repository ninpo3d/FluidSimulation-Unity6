using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Flexus.FluidSimulation
{
    public struct FluidStrokeState
    {
        public Vector4 HitData;           // x=UV.x, y=UV.y, z=isInteracting(1/0), w=isReleaseFrame(1/0)
        public Vector4 PrevHitData;       // x=prevUV.x, y=prevUV.y, z=wasInteracting(1/0), w=unused
        public Vector4 BrushVelocity;     // x=dir.x, y=dir.y (domain space), z=speed (domain units/s), w=unused
        public bool IsInteracting;        // True while pointer is held down on the surface
        public bool HasImpulseThisFrame;  // True if either interacting or release impulse occurred
    }

    // Handles touch, mouse, and pointer surface raycasts
    public class FluidPointerInput
    {
        private Vector2 _currentHitUV;
        private Vector2 _prevHitUV;
        private Vector2 _lastInteractedUV;
        private Vector2 _smoothedVelocity;
        private bool _isInteractingNow;
        private bool _wasInteractingLastFrame;

        private string _debugInputSource = "Idle";
        private bool _debugHitSuccess = false;

#if UNITY_EDITOR
        private bool _isSceneInteracting;
        private Vector2 _sceneHitUV;
        private bool _scenePressedThisFrame;
#endif

        public string DebugInputSource => _debugInputSource;
        public bool DebugHitSuccess => _debugHitSuccess;
        public bool IsInteracting => _isInteractingNow;

        public void Reset()
        {
            _wasInteractingLastFrame = false;
            _isInteractingNow = false;
            _smoothedVelocity = Vector2.zero;
#if UNITY_EDITOR
            _isSceneInteracting = false;
            _scenePressedThisFrame = false;
#endif
        }

        // Analytical ray-plane intersection mapped to [0, 1] UV space
        public bool RaycastPlaneUV(Ray ray, Transform surfaceTransform, Vector2 domainSize, out Vector2 hitUV)
        {
            hitUV = Vector2.zero;
            if (surfaceTransform == null) return false;

            Plane surfacePlane = new Plane(surfaceTransform.up, surfaceTransform.position);

            if (surfacePlane.Raycast(ray, out float enterDistance))
            {
                Vector3 worldPoint = ray.GetPoint(enterDistance);
                Vector3 localPoint = surfaceTransform.InverseTransformPoint(worldPoint);

                float sizeX = Mathf.Max(0.001f, domainSize.x);
                float sizeZ = Mathf.Max(0.001f, domainSize.y);
                float halfX = sizeX * 0.5f;
                float halfZ = sizeZ * 0.5f;

                if (Mathf.Abs(localPoint.x) <= halfX && Mathf.Abs(localPoint.z) <= halfZ)
                {
                    float uvX = Mathf.Clamp01((localPoint.x / sizeX) + 0.5f);
                    float uvY = Mathf.Clamp01((localPoint.z / sizeZ) + 0.5f);
                    hitUV = new Vector2(uvX, uvY);
                    return true;
                }
            }
            return false;
        }

        private static Vector2 GetDefaultDomainSize(Transform surfaceTransform)
        {
            if (surfaceTransform != null)
            {
                Vector3 scale = surfaceTransform.lossyScale;
                return new Vector2(Mathf.Max(0.5f, scale.x), Mathf.Max(0.5f, scale.z));
            }
            return Vector2.one;
        }

        public bool RaycastPlaneUV(Ray ray, Transform surfaceTransform, out Vector2 hitUV)
        {
            return RaycastPlaneUV(ray, surfaceTransform, GetDefaultDomainSize(surfaceTransform), out hitUV);
        }

#if UNITY_EDITOR
        public void HandleSceneGUI(SceneView sceneView, Transform surfaceTransform, bool isSimulationSleeping, Vector2 domainSize)
        {
            if (!Application.isPlaying) return;

            if (!isSimulationSleeping && sceneView != null)
            {
                sceneView.Repaint();
            }

            Event e = Event.current;
            if (e == null) return;

            // Allow camera orbiting (Alt + Mouse) and right-click flythrough without painting
            if (e.alt || (e.button != 0 && e.type != EventType.Repaint && e.type != EventType.Layout))
            {
                if (_isSceneInteracting)
                {
                    _isSceneInteracting = false;
                }
                return;
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0)
                    {
                        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                        if (RaycastPlaneUV(ray, surfaceTransform, domainSize, out Vector2 uv))
                        {
                            GUIUtility.hotControl = controlId;
                            _isSceneInteracting = true;
                            _sceneHitUV = uv;
                            _scenePressedThisFrame = true;
                            e.Use();
                            sceneView.Repaint();
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId || _isSceneInteracting)
                    {
                        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                        if (RaycastPlaneUV(ray, surfaceTransform, domainSize, out Vector2 uv))
                        {
                            _isSceneInteracting = true;
                            _sceneHitUV = uv;
                        }
                        else
                        {
                            _isSceneInteracting = false;
                        }
                        e.Use();
                        sceneView.Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId || _isSceneInteracting)
                    {
                        GUIUtility.hotControl = 0;
                        _isSceneInteracting = false;
                        e.Use();
                        sceneView.Repaint();
                    }
                    break;
            }
        }

        public void HandleSceneGUI(SceneView sceneView, Transform surfaceTransform, bool isSimulationSleeping)
        {
            HandleSceneGUI(sceneView, surfaceTransform, isSimulationSleeping, GetDefaultDomainSize(surfaceTransform));
        }

        private bool IsMouseInteractingWithGameView()
        {
            EditorWindow mouseWindow = EditorWindow.mouseOverWindow;

            if (mouseWindow == null && _wasInteractingLastFrame)
            {
                mouseWindow = EditorWindow.focusedWindow;
            }

            if (mouseWindow == null) return false;

            System.Type type = mouseWindow.GetType();
            while (type != null)
            {
                string name = type.Name;
                if (name == "GameView" || name == "PlayModeView" || name == "SimulatorWindow")
                {
                    return true;
                }
                type = type.BaseType;
            }

            return false;
        }
#endif

        public FluidStrokeState Update(Transform surfaceTransform, Camera camera, float deltaTime, Vector2 domainSize)
        {
            _isInteractingNow = false;
            _debugHitSuccess = false;
            _debugInputSource = "Idle";

            bool hasValidHit = false;
            Vector2 hitUV = Vector2.zero;

#if UNITY_EDITOR
            // Priority 0: Scene Viewport Direct Mouse Painting
            if (_isSceneInteracting || _scenePressedThisFrame)
            {
                _isInteractingNow = true;
                hasValidHit = true;
                hitUV = _sceneHitUV;
                _scenePressedThisFrame = false;
                _debugHitSuccess = true;
                _debugInputSource = "Scene Viewport";
            }
#endif

                // Priority 1: Game View / Device Simulator / Touch Input
                if (!_isInteractingNow)
                {
#if UNITY_EDITOR
                if (!IsMouseInteractingWithGameView())
                {
                    _debugInputSource = "Editor / Ignored";
                }
                else
#endif
                {
                    Vector2 screenPos = Vector2.zero;
                    bool hasScreenPos = false;

#if ENABLE_INPUT_SYSTEM
                    if (Mouse.current != null && !Mouse.current.enabled)
                    {
                        InputSystem.EnableDevice(Mouse.current);
                    }

                    if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
                    {
                        screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
                        hasScreenPos = true;
                        _debugInputSource = "Touch";
                    }
                    else if (Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed))
                    {
                        screenPos = Mouse.current.position.ReadValue();
                        hasScreenPos = true;
                        _debugInputSource = Mouse.current.leftButton.isPressed ? "Mouse Left" : "Mouse Right";
                    }
                    else if (Pointer.current != null && Pointer.current.press.isPressed)
                    {
                        screenPos = Pointer.current.position.ReadValue();
                        hasScreenPos = true;
                        _debugInputSource = "Pointer";
                    }
#else
                    if (Input.touchCount > 0)
                    {
                        Touch touch = Input.GetTouch(0);
                        if (touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                        {
                            screenPos = touch.position;
                            hasScreenPos = true;
                            _debugInputSource = "Legacy Touch";
                        }
                    }
                    else if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
                    {
                        screenPos = Input.mousePosition;
                        hasScreenPos = true;
                        _debugInputSource = "Legacy Mouse";
                    }
#endif

                    // Validate screen coordinates (prevent inf/NaN and out-of-frustum exceptions)
                    if (hasScreenPos)
                    {
                        if (float.IsInfinity(screenPos.x) || float.IsInfinity(screenPos.y) ||
                            float.IsNaN(screenPos.x) || float.IsNaN(screenPos.y))
                        {
                            hasScreenPos = false;
                        }
                    }

                    // Ignore clicks and touches on UI elements (Canvas or HUD menu)
                    if (hasScreenPos)
                    {
                        if (FluidMobileUI.IsPointerOverUI(screenPos) ||
                            (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
                        {
                            hasScreenPos = false;
                        }
                    }

                    // Analytical plane raycast for Game View / Device Simulator
                    if (hasScreenPos && camera != null)
                    {
                        Rect camRect = camera.pixelRect;
                        if (camRect.width > 0 && camRect.height > 0 && camRect.Contains(screenPos))
                        {
                            Ray ray = camera.ScreenPointToRay(screenPos);
                            if (RaycastPlaneUV(ray, surfaceTransform, domainSize, out hitUV))
                            {
                                hasValidHit = true;
                                _debugHitSuccess = true;
                            }
                        }
                    }
                }
            }

            _isInteractingNow = hasValidHit;

            // Stroke and Motion Velocity Processing
            Vector4 hitData = new Vector4(-1f, -1f, 0f, 0f);
            Vector4 prevHitData = new Vector4(-1f, -1f, 0f, 0f);
            Vector4 brushVelocityData = Vector4.zero;
            bool hasImpulse = false;

            if (hasValidHit)
            {
                hasImpulse = true;
                _currentHitUV = hitUV;

                if (_wasInteractingLastFrame)
                {
                    Vector2 deltaPos = Vector2.Scale(_currentHitUV - _prevHitUV, domainSize);
                    float strokeDt = Mathf.Max(0.001f, deltaTime);
                    Vector2 instantVel = deltaPos / strokeDt;
                    float alpha = 1.0f - Mathf.Exp(-25.0f * deltaTime);
                    _smoothedVelocity = Vector2.Lerp(_smoothedVelocity, instantVel, alpha);

                    prevHitData = new Vector4(_prevHitUV.x, _prevHitUV.y, 1.0f, 0.0f);
                }
                else
                {
                    // Stroke began this frame
                    _prevHitUV = _currentHitUV;
                    _smoothedVelocity = Vector2.zero;
                    prevHitData = new Vector4(_currentHitUV.x, _currentHitUV.y, 1.0f, 0.0f);
                }

                float speed = _smoothedVelocity.magnitude;
                Vector2 dir = speed > 0.0001f ? _smoothedVelocity.normalized : Vector2.zero;
                brushVelocityData = new Vector4(dir.x, dir.y, speed, 0.0f);

                hitData = new Vector4(_currentHitUV.x, _currentHitUV.y, 1.0f, 0.0f);
                _lastInteractedUV = _currentHitUV;
                _prevHitUV = _currentHitUV;
                _wasInteractingLastFrame = true;
            }
            else
            {
                // Check for release frame (rebound pop splash)
                if (_wasInteractingLastFrame)
                {
                    hasImpulse = true;
                    hitData = new Vector4(_lastInteractedUV.x, _lastInteractedUV.y, 0.0f, 1.0f);
                    prevHitData = new Vector4(_lastInteractedUV.x, _lastInteractedUV.y, 0.0f, 0.0f);

                    _wasInteractingLastFrame = false;
                    _smoothedVelocity = Vector2.zero;
                }
            }

            FluidStrokeState state = new FluidStrokeState
            {
                HitData = hitData,
                PrevHitData = prevHitData,
                BrushVelocity = brushVelocityData,
                IsInteracting = _isInteractingNow,
                HasImpulseThisFrame = hasImpulse
            };

            return state;
        }

        public FluidStrokeState Update(Transform surfaceTransform, Camera camera, float deltaTime)
        {
            return Update(surfaceTransform, camera, deltaTime, GetDefaultDomainSize(surfaceTransform));
        }
    }
}
