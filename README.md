<h2 align="center">Showcase</h2>
<hr style="border: none; border-top: 1px solid #444; height: 1px; width: 100%;"/>

<img width="736" height="410" alt="672922c275558a5bc9d850be78c7dcc4" src="https://github.com/user-attachments/assets/94a2979a-c04d-4adc-8494-3a4735927c41" />
<img width="1910" height="1075" alt="9ec952cb38b68f63496caccff327c753" src="https://github.com/user-attachments/assets/13ab4e8b-48fd-4a99-8f5b-049c9de18ec2" />
<img width="750" height="416" alt="15a1591774d9b956f383aab22845d474" src="https://github.com/user-attachments/assets/c8bb80d4-7f58-4730-9bd9-86fbedc9c48a" />
<img width="735" height="754" alt="0a0127c03dfff3d044c1d4e86ca034c8" src="https://github.com/user-attachments/assets/2f2d974a-e754-4bfa-8146-59966a01f158" />

---

<h2 align="center">Introduction</h2>
<hr style="border: none; border-top: 1px solid #444; height: 1px; width: 100%;"/>

This project implements a real-time Global Illumination (GI) system similar to Unreal Engine Lumen, tailored for the Unity Universal Render Pipeline (URP).  
The core approach uses RadianceCache and ReSTIR, combines multiple ray tracing methods, performs low-resolution sampling followed by super-resolution, and applies various denoising techniques.  
Default settings: 1/4 SPP, 1 Bound.

---

<h2 align="center">Features</h2>
<hr style="border: none; border-top: 1px solid #444; height: 1px; width: 100%;"/>

- LocalSDF  
- Global Voxelization  
- Screen Space Reflections (SSR)  
- Spatiotemporal Variance-Guided Filtering (SVGF)  
- Warranted Adaptive Light Reprojection (WALR)  
- Optional Intel Open Image Denoise (OIDN)  
- Optional NVIDIA OptiX  
- Surface Cache  
- ReSTIR Direct Illumination (ReSTIRDI)  
- ReSTIR Global Illumination (ReSTIRGI)  
- Resolution Downscaling  
- Super-Resolution  
- Light Occlusion Culling  
- No Baking Required  
- Fully Dynamic Objects (mesh changes, material changes, light transforms or parameter changes)

---

<h2 align="center">Usage</h2>
<hr style="border: none; border-top: 1px solid #444; height: 1px; width: 100%;"/>

1. **Import Package**  
   - Import the provided UnityPackage into your project. A `PhotonGISystem` folder will appear in the root directory.

2. **Scene Setup**  
   - Drag the root prefab (`PhotonGISystem.prefab`) from `PhotonGISystem/Prefabs` into your scene.

3. **Fix Issues**  
   - Select the `PhotonGISystem` object in the scene and click **Fix Issues** in the Inspector.

4. **URP Shadow Settings**  
   - It is recommended to disable URP shadows (Project Settings → Graphics → URP Asset → Shadows → None),  
   - or use the provided `Photon_RPAsset` in the root directory.

5. **Enable/Disable GI**  
   - Select `PhotonGISystem` and toggle **EnableDebug** in the DebugManager to switch real-time GI on or off.

6. **Resolution Settings**  
   - In the DebugManager, select a downscaling factor under **Resolution** (e.g., 1/2, 1/4, 1/8).

7. **AI Denoising**  
   - Select the `FilterManager` object and check **Enable Advance Denoiser**.  
   - Requires CUDA and OptiX installed. If the project fails to open without these, start in Safe Mode to disable this option.

8. **Enable Ray Tracing Calculations**  
   - **Objects**: Add the **PhotonObject** component to meshes or GameObjects that should participate in ray tracing sampling.  
   - **Lights**: Add the **LightReporter** component to Lights that should be included in ray tracing.

> **Note**: Many parameters are experimental and may have no noticeable effect. If issues arise, revert to defaults or contact the developer.

---

<h2 align="center">Compatibility</h2>
<hr style="border: none; border-top: 1px solid #444; height: 1px; width: 100%;"/>

- **Unity**: 6.0 and above  
- **Render Pipeline**: URP 12.0 and above  
- **Platforms**: Windows, macOS, Linux  
- **Hardware Requirements**: GTX 1660 or higher (minimum quality)  
