# Flexus Fluid Simulation - Interactive Mobile Demo

An optimized real-time 2.5D shallow-water fluid simulation with kinetic pigment dispersion, custom URP vertex displacement, and mobile touch interaction.

---

## 📱 Deliverable APK Download

- **Direct Download (GitHub Releases / Cloud Drive):** [Download Flexus_Fluid_Demo.apk](#) *(Add your Google Drive / Release link here)*
- **Target Platform:** Android (ARM64, Android 7.0+ / API 24+)
- **Graphics API:** Vulkan / OpenGLES 3
- **Scripting Backend:** IL2CPP

---

## 🛠 Project Environment

- **Unity Version:** `Unity 6 (6000.3.25f1)`
- **Render Pipeline:** Universal Render Pipeline (URP 17.3.0)
- **Color Space:** Linear
- **Target Scene:** `Assets/FluidSimulation/Scenes/LVL3_PaintDemo.unity`

---

## 🚀 How to Run in Unity Editor

1. Open **Unity Hub**.
2. Click **Add** -> **Add project from disk** and select this repository folder.
3. Open the project using **Unity 6 (6000.x / 6000.3.25f1)**.
4. Navigate to `Assets/FluidSimulation/Scenes/` and open **`LVL3_PaintDemo.unity`**.
5. Press **Play** in Unity.
6. Interact with the fluid surface using mouse drag or touch in the **Game View** or **Device Simulator**.
7. Use the top **FLUID DYNAMICS** menu to adjust Viscosity, Brush Size, and Brush Force in real time.

---

## 🌊 Architecture & Technical Highlights

1. **Wave Equation Blit Simulation:**
   - Alternating double-buffered render textures (`ARGBHalf` format).
   - High numerical stability using Laplacian 4-neighbor stencil kernel.
   - Encodes Height (R), Wave Velocity (G), Pigment (B), and Wave Crest (A) within a single memory-efficient pass.

2. **Kinetic Pigment Excitation & Dispersion:**
   - Fluid wave velocity dynamically excites neon pigment concentration.
   - Advection and diffusion spread pigment along water currents.

3. **Custom Displacement Surface Shader:**
   - Analytical normal reconstruction from heightfield gradients.
   - Dual-layer specular reflection (Directional Light specular highlights + Environment Cubemap reflections).
   - Chromatic tinting and subsurface scattering depth cues.

4. **Mobile Performance Optimizations:**
   - **Pigment-Aware Sleep State:** The simulation automatically freezes blits to 0 draw calls when both wave motion and pigment concentration settle below visual thresholds.
   - **SRP Batcher Compatible:** Zero CPU batching overhead for surface geometry.
   - **Safe Area & High-DPI UI:** Mobile HUD automatically respects camera notches and punch-holes.
