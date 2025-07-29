<h2 align="center">效果展示</h2>
<hr style="border: none; border-top: 1px solid #444; height: 1px; width: 100%;"/>

<img width="736" height="410" alt="672922c275558a5bc9d850be78c7dcc4" src="https://github.com/user-attachments/assets/94a2979a-c04d-4adc-8494-3a4735927c41" />
<img width="1910" height="1075" alt="9ec952cb38b68f63496caccff327c753" src="https://github.com/user-attachments/assets/13ab4e8b-48fd-4a99-8f5b-049c9de18ec2" />
<img width="750" height="416" alt="15a1591774d9b956f383aab22845d474" src="https://github.com/user-attachments/assets/c8bb80d4-7f58-4730-9bd9-86fbedc9c48a" />
<img width="735" height="754" alt="0a0127c03dfff3d044c1d4e86ca034c8" src="https://github.com/user-attachments/assets/2f2d974a-e754-4bfa-8146-59966a01f158" />


---

<h2 align="center">简介</h2>
<hr style="border: none; border-top: 1px solid #444; height: 1px; width: 100%;"/>

本项目实现了一套 **类似 Unreal Engine Lumen** 的实时全局光照（Global Illumination, GI）系统，专为 Unity URP（Universal Render Pipeline） 定制。  
核心思路大致是使用RadianceCache与ReSTIR，并用多种光追混合，以及降分辨率采样后超分辨率，并且使用多种降噪方式进行降噪
默认1/4SPP，1Bounds

---

<h2 align="center">Feature</h2>
<hr style="border: none; border-top: 1px solid #444; height: 1px; width: 100%;"/>

- LocalSDF  
- 全局体素  
- SSR  
- SVGF  
- WALR  
- 可选择 OIDN  
- 可选择 OptiX  
- SurfaceCache  
- ReSTIRDI  
- ReSTIRGI  
- 降分辨率运算  
- 超分辨率  
- 光源遮蔽剔除  
- 无需烘焙  
- 所有物体均可动态变换（改变 Mesh、改变材质、改变光源变换或参数）

---

<h2 align="center">使用教程</h2>
<hr style="border: none; border-top: 1px solid #444; height: 1px; width: 100%;"/>

1. **导入包**  
   - 将提供的 UnityPackage 导入项目，项目根目录会出现 `PhotonGISystem` 文件夹。

2. **场景初始化**  
   - 从 `PhotonGISystem/Prefabs` 中将根 Prefab（`PhotonGISystem.prefab`）拖入当前场景。

3. **Fix Issues 一键修复**  
   - 选中场景中的 **PhotonGISystem** 对象，Inspector 中点击 **Fix Issues**。

4. **URP 阴影设置**  
   - 建议关闭 URP 默认阴影（Project Settings → Graphics → URP Asset → Shadows → None），  
   - 或者使用根目录下提供的 **Photon_RPAsset**。

5. **开启/关闭 GI**  
   - 选中 **PhotonGISystem**，在 **DebugManager** 面板勾选 **EnableDebug** 控制实时 GI。

6. **分辨率设置**  
   - 在 **DebugManager** 的 **Resolution** 下拉项中选择降分辨率级别（如 1/2、1/4、1/8）。

7. **AI 降噪**  
   - 选中场景中的 **FilterManager**，勾选 **Enable Advance Denoiser**。  
   - 前提需安装 CUDA 与 OptiX；若项目因未安装无法打开，可先以 Safe Mode 启动并关闭此选项。

8. **启用光追计算**  
   - **物体**：为需参与光追的对象添加组件 **PhotonObject**。  
   - **光源**：为需参与光追的 Light 添加组件 **LightReporter**。

> **注意**：许多调节参数为废案，实际效果可能不明显或没有。  

---
<h2 align="center">兼容版本</h2>
<hr style="border: none; border-top: 1px solid #444; height: 1px; width: 100%;"/>

- **Unity**：6.0 及以上  
- **Render Pipeline**：URP 12.0 及以上  
- **平台支持**：Windows, macOS, Linux  
- **硬件需求**：GTX 1660 及以上（最低画质）  
