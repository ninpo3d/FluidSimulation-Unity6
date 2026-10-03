# Flexus Fluid Simulation

Real-time 2.5D shallow-water fluid simulation for mobile devices (Unity 6 / Universal Render Pipeline).  
Features 2D wave equation advection, kinetic pigment excitation/dispersion, procedural surface vertex displacement, and touch interaction.

## Android APK

- **Download:** [Flexus_Fluid_Demo.apk](https://github.com/ninpo3d/FluidSimulation-Unity6/releases/download/v1.0.0/Flexus_Fluid_Demo.apk)
- **Target Architecture:** ARM64 (IL2CPP)
- **Minimum OS:** Android 7.0 (API 24)
- **Graphics API:** Vulkan / GLES3

## Project Setup

- **Engine:** Unity 6 (`6000.3.25f1`)
- **Pipeline:** Universal Render Pipeline (URP 17.3.0)
- **Color Space:** Linear
- **Main Scene:** `Assets/FluidSimulation/Scenes/LVL3_PaintDemo.unity`

To run in the editor:
1. Open the project in Unity Hub with **Unity 6**.
2. Open `LVL3_PaintDemo.unity`.
3. Press **Play**.
4. Interact using mouse or touch in **Game View** / **Device Simulator**.
5. Adjust fluid parameters (viscosity, brush radius, impulse strength) via the top HUD.

## Implementation Details

### Simulation Core (Wave Equation)
- Double-buffered ping-pong render textures in `ARGBHalf` format (128x128 resolution, preserving square aspect ratio).
- Channel packing:
  - **R:** Surface height displacement.
  - **G:** Vertical wave velocity.
  - **B:** Pigment concentration (advected with currents).
  - **A:** Peak crest height for dynamic foam/highlights.
- Solved via a 4-tap Laplacian finite-difference stencil pass within a custom blit shader.

### Surface Shading & Vertex Displacement
- Procedurally generated tessellated plane (`ProceduralPlane.cs`) with symmetric frustum culling bounds.
- Custom vertex/fragment URP shader (`FluidDisplacement.shader`) computing analytical normal vectors directly from heightfield neighbor differentials.
- Dual reflection model: Directional specular lobe combined with modulated environment cubemap reflections.

### Mobile Optimizations
- **Pigment-Aware Sleep State:** Blits freeze to 0 draw calls when both fluid oscillation energy and active pigment dissipate below visibility thresholds.
- **Ray-Plane Touch Mapping:** Analytical mathematical plane intersection with safe-area bounding to avoid expensive physics mesh colliders.
- **Runtime uGUI HUD:** Collapsible high-DPI interface with raycast blocking to prevent stroke bleeding beneath touch controls.
