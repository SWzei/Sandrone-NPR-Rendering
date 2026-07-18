# M1 验收说明：Main light baseline

## 范围与状态

- 已实现：单主方向光、导入法线的世界空间变换、`NdotL/NdotV/HeadAxis` 调试、头骨局部轴校准、Forward/Forward+ 兼容、正/侧 BaseLit 截图与量化对比。
- 未实现：M2 Ramp/硬色阶、M3 ShadowCaster/实时阴影、M4 ControlMap/MatCap、高级面部与头发、描边、Emission/Bloom、最终后处理。
- 自动检查已通过；Unity Game/Scene/Frame Debugger 的人工验收仍需操作者执行后才可宣布 M1 全部验收完成。

## M1 锁定契约

| 项 | 契约 |
|---|---|
| Unity / 管线 | 6000.5.3f1 / URP 17.5.0 / Linear |
| 场景 | `Assets/Sandrone/Tests/Scenes/ToonCalibration_M1.unity` |
| Shader | `Sandrone/M1/MainLightBaseline`，一个 UniversalForward Pass |
| 变体 | 仅 `_CLUSTER_LIGHT_LOOP`：Forward 与 Forward+ 两条管线路径；调试视图不用 keyword |
| 主光 | 单 Directional、白色、Intensity 1、无实时阴影；URP `direction` 解释为表面指向光源 |
| BaseLit | `BaseMap × MainLightColor × saturate(NdotL×0.5+0.5)`；不是 M2 最终二分光照 |
| 头部轴 | 从角色根语义轴校准到 `頭` 骨局部空间，再变换回世界空间；R/U/F 为右手正交基 |
| M0 回归 | 31 材质槽、BaseMap、透明状态、拓扑/骨骼契约必须保持 |

## Unity 操作与人工验收

1. 使用 Unity 6000.5.3f1 打开工程；Console 必须为 0 Error。
2. 打开 `ToonCalibration_M1.unity`，确认角色非粉色、非黑色剪影，31 个材质槽颜色与 M0 一致。
3. 选中 `Sandrone_M1` 的 `Sandrone M1 Controller`，依次切换 Debug Mode：
   - `NdotL`：灰度随灯光方向连续改变；
   - `NdotV`：朝向相机的表面接近白，掠射表面变暗；
   - `HeadAxis`：显示 RGB 彩色方向编码，不能全灰或整片单色；
   - 两个 MainLight 诊断均应在角色区域非黑，白色单位方向光下 attenuation 视图应为白。
4. 保持 `NdotL`，将 `M1_MainDirectionalLight` 绕角色 Y 轴依次旋转 0°、90°、180°、270°；亮侧必须按光源方向移动，不能左右反号。可对照 `TestArtifacts/M1/Debug` 三张光扫图。
5. 将角色根 Y 旋转 90°，再恢复 0°；HeadAxis 图应随角色旋转。只旋转 `頭` 骨 15°，头部区域轴色应变化，身体区域不应被错误重定义。
6. Frame Debugger 检查角色为 `M1MainLight / UniversalForward`；不能出现 M2 Ramp、ShadowCaster、Outline、Emission Pass。PC Forward+ 下 Shader 应选择 `_CLUSTER_LIGHT_LOOP` 变体。
7. Game 视图正/侧观察透明叠层、双面法线与极端掠射角；允许尚未解决的排序细节，但不允许整片黑面、光照跳变或法线反号。

通过标准：Console 0 Error；61 项自动检查仍为 0 failure；主光扫描方向正确；根/头旋转不破坏头部局部轴语义；BaseLit 保留材质颜色；M0 功能无回归。人工步骤完成前状态为“实现及自动验证通过，人工验收待确认”。

## Blender 资产制作与导出

M1 不修改 Blender 文件，不生成新 UV/贴图/法线。继续使用 M0 的 `Source/Blender/Sandrone_MMD_Baseline.blend` 与 FBX：Normals=`Import`，Tangents=`Calculate Mikktspace`，Shape Keys/骨骼/31 子网格保持。

若发现灯扫方向局部异常：在 Blender 显示 Face Orientation 与自定义 Split Normals；只修正确定的反面/坏法线，重新导出后必须重跑 M0/M1 拓扑、材质和光扫测试。不要在 M1 烘焙 FaceMap 或描边法线。

## 缺失资源及制作方案（本 Phase 不制作）

| 资源 | 用途/建议规格 | 制作、导入与验证 |
|---|---|---|
| Ramp（M2） | 暖/冷二分与材质族，256×32/64 RGBA | 每行留 padding；作为 LUT 时 sRGB Off、Clamp、No Compression；逐行扫光检查串色 |
| ControlMap（M4） | R 高光、G AO、B 响应、A 材质/Ramp ID，2048² | 按 UV 绘制；Linear；四通道独立调试 |
| MatCap（M4） | 金属/饰品视空间响应，512² | 正交球绘制/烘焙；Clamp；旋转相机验证视空间锁定 |
| FaceMap（M5） | 面部光角 SDF，1024–2048² | 依 `頭` 的 R/U/F 轴制作；Linear/Clamp/No Compression；360° 光角扫描验证镜像与单调性 |
| Outline Normal/Width（M7） | 平滑轮廓法线与局部宽度 | Blender 同位置顶点平均，仅用于描边；建议 Color.rg + Color.a 宽度；逐通道可视化 |
| EmissionMask（M8） | 眼睛/晶体/屏幕发光选择 | Linear 单通道或打包；皮肤、白布、腮红必须为 0；Bloom extraction 验证 |

## 自动验证与证据

重建命令：

```powershell
$UnityEditor = '<Unity 6000.5.3f1 Editor executable>'
$ProjectPath = (Resolve-Path 'Unity/SandroneToon').Path
& $UnityEditor -batchmode -quit -projectPath $ProjectPath -executeMethod SandroneToon.Editor.SandroneM1Bootstrap.Build -accept-apiupdate -force-d3d11 -logFile (Join-Path $ProjectPath 'TestArtifacts/M1/unity_m1.log')
```

项目开发时参考了原模型的图样，但当前工程不保留或依赖对应图片。`compare_m1.py` 仅作为可选离线分析工具；如需使用，必须由使用者显式提供合法取得的输入，结果不得冒充默认自动门禁证据。

实际结果：Unity 退出码 0；61 checks / 0 failures / 1 inherited warning；Shader messages=0、Pass=1、keyword pragma=1。NdotL front/back mean=90.055/51.536，front/back MAE=38.873，front/right MAE=17.081。报告与截图位于 `TestArtifacts/M1`。

## 原模型图样参考与阶段差异

- 原模型图样具有更明确的暗部压缩、脸/发设计阴影、材质高光与轮廓；M1 只有连续法线主光，这些属于 M2–M7 的特征尚未实现。
- BaseMap 的发色、肤色、红/黑/白/金分区、眼部与服饰纹理映射正确；构图覆盖率差异来自姿势、裁切和附属几何，不做配准变形。

## 风险与回退

- Forward+ 兼容依赖 URP 17.5 的 `_CLUSTER_LIGHT_LOOP`。升级 URP 时必须复查 `Core.hlsl/RealtimeLights.hlsl`，不能无记录删掉或改名。
- 当前双面材质在片元阶段翻转背面法线；透明/叠层的最终排序、Stencil 与深度策略留给 M6。
- 31 个材质意味着单 Forward Pass 仍有相应子网格开销；M9 前不为降低 Draw Call 破坏脸/眼/发状态边界。
- 回退到 M0：删除 M1 专用材质、Shader、Controller、Editor 脚本、场景、文档与 `TestArtifacts/M1`，并把 Build Settings 指回 `ToonCalibration_M0.unity`。M0 与源资产无需恢复。
