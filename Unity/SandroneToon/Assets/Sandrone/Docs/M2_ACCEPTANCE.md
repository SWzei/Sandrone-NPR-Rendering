# M2 验收说明：Two-tone & Warm/Cool Ramp

## 范围与状态

- 已实现：Half-Lambert、材质族阈值二分、五行暖/冷 Ramp、基于 `fwidth` 的边界抗锯齿、Forward/Forward+ 主光兼容、8 种无关键字调试视图、正/侧截图及量化对比。
- 未实现：M3 ShadowCaster/实时投射阴影，M4 ControlMap/MatCap，M5 Face SDF，M6 眼/发 Stencil，M7 描边，M8 Emission/Bloom，M9 后处理与优化。
- 主工程自动验证为 88/88 通过；Unity Game/Scene/Frame Debugger 人工验收仍待操作者确认，因此状态是“实现及自动验证通过，人工验收待确认”。

## M2 锁定契约

| 项 | 契约 |
|---|---|
| Unity / 管线 | 6000.5.3f1 / URP 17.5.0 / Linear；PC Forward+、Mobile Forward |
| 场景 | `Assets/Sandrone/Tests/Scenes/ToonCalibration_M2.unity` |
| Shader | `Sandrone/M2/ToonRamp`；一个 UniversalForward Pass |
| 变体 / 采样 | 仅 `_CLUSTER_LIGHT_LOOP`；BaseMap + RampMap 共 2 次采样 |
| 二分 | `h=saturate(NdotL*0.5+0.5)`；材质族阈值 0.46–0.52；`smoothstep` 边界 |
| 抗锯齿 | `edge=max(_BandSoftness + fwidth(h)*_BandAA, 1e-4)`；Softness=0.015、AA=1 |
| Ramp | 256×64 RGBA、5 个逻辑行；`V=(row+0.5)/5`；Linear、Clamp、Bilinear、No Mip、Uncompressed |
| 材质族 | Skin/Face、Light Cloth、Dark Cloth/Hair、Metal、Eye；31 槽必须且只能映射一次 |
| 主光 | 单白色 Directional、Intensity 1、无实时阴影；M3 前不采样 shadow attenuation |

Ramp 作为线性颜色倍率而不是最终颜色。亮端收敛到 0.82–0.93，避免 BaseMap 白布和红裙在二分亮区过曝；暗端保留冷紫/暖肤差异。PNG 仅在缺失时由 Bootstrap 生成，已有艺术家编辑不会被重建命令覆盖。

## Unity 操作与人工验收

1. 用 Unity 6000.5.3f1 打开工程，Console 必须 0 Error；打开 `ToonCalibration_M2.unity`。
2. 确认角色非粉色/黑剪影，Renderer 为 31 个 `M2_*` 材质；每个材质绑定同一 Ramp，`_RampRow` 为整数 0–4。
3. 在 `Sandrone M2 Controller` 切换：
   - `HalfLambert`：连续灰度，灯绕角色时方向正确；
   - `BandMask`：只有黑/白主体和约 1 px 解析抗锯齿边界，不应出现大面积灰雾；
   - `RampUV`：角色上恰好出现 5 个稳定行色；`RampSample` 能看到材质族暖/冷差异；
   - `NdotV`、`HeadAxis`：分别验证视线与头骨局部轴；`Silhouette` 用于图像掩码核对。
4. 将 `M2_MainDirectionalLight` 绕 Y 轴旋转 0°/90°/180°/270°：二分边界应随光移动，无左右反号。正背光 BandMask 的角色区应几乎互换。
5. 在 Game 视图使用 994×1654 正面和 662×1032 侧面；再把相机 Orthographic Size 设为默认值的 0.65×、1.45×。运动或缩放时边界不能出现连续闪烁、断裂或明显多像素模糊。
6. Frame Debugger 检查每个材质只有 `M2ToonRamp / UniversalForward`；PC Forward+ 选择 `_CLUSTER_LIGHT_LOOP`。M2 不应出现 ShadowCaster、Outline、MatCap、FaceMap、Emission Pass。
7. 重点看脸、白布、红裙、深色袖和金属：BaseMap 线条仍清楚；脸允许尚无设计脸影，但不允许普通法线产生碎斑；透明叠层不能因 M2 改变 M1 的 Queue/Blend/ZWrite/Cull。

通过标准：Console 0 Error；`M2ValidationReport.json` 仍为 88 checks / 0 failures；光扫方向正确；五行无串行；近/远边界稳定；正面/侧面无 BaseMap 重复压黑；M0/M1 无回归。人工步骤完成前不要宣布整个 Phase 人工验收通过。

## Blender 资产制作与导出

M2 不需要修改 `.blend`、UV、法线或 FBX。继续使用 `Source/Blender/Sandrone_MMD_Baseline.blend` 与 M0 FBX：Normals=`Import`，Tangents=`Calculate Mikktspace`，保留 31 子网格、61 BlendShapes、692 renderer bones。

若二分边界只在某一片面异常，先在 Blender 检查 Face Orientation、自定义 Split Normals 与重叠壳；不要通过增加 `_BandSoftness` 掩盖坏法线。修正模型后必须重跑 M0 拓扑、M1 光向及 M2 边界测试。

## 缺失资源及制作方案（后续 Phase，不制作占位图）

| 资源 | 用途/规格 | 制作、导入与验证 |
|---|---|---|
| ControlMap（M4） | R 高光、G AO、B 响应、A 材质/行控制；建议 2048² RGBA | 按 UV 绘制/烘焙；Linear、Uncompressed 起步；四通道独立调试，ID 不得受压缩污染 |
| MatCap（M4） | 金属与饰品视空间响应；512² RGBA | 正交球绘制/烘焙；Clamp；相机绕角色时高光应锁到视空间 |
| FaceMap（M5） | 左右可翻转的脸部光角 SDF；1024–2048² | 以 `頭` 骨 R/U/F 制作；Linear、Clamp、No Mip/自制 Mip；水平光角 360° 检查单调性和镜像 |
| Outline Normal/Width（M7） | 平滑外扩方向与局部线宽 | Blender 同位置顶点平均，仅供描边；建议 Color.rg 编码法线、Color.a 线宽；1/3/10 m 检查宽度与尖刺 |
| EmissionMask（M8） | 眼睛、晶体、显示屏的 HDR 选择 | Linear 单通道或打包；皮肤/白布必须为 0；Bloom Extraction 视图验证泄漏 |

`toon_defo.bmp` 是 16×16 MMD toon 查找图，不满足五行材质族、可追踪颜色与行中心 padding 契约，不能冒充本阶段 Ramp。M2 已生成项目自有的 `Sandrone_Ramp_WarmCool.png`，其余资源保持明确缺失。

## 自动验证与证据

重建命令（必须有图形设备，不要使用 `-nographics`）：

```powershell
$UnityEditor = '<Unity 6000.5.3f1 Editor executable>'
$ProjectPath = (Resolve-Path 'Unity/SandroneToon').Path
& $UnityEditor -batchmode -quit -projectPath $ProjectPath -executeMethod SandroneToon.Editor.SandroneM2Bootstrap.Build -logFile (Join-Path $ProjectPath 'TestArtifacts/M2/unity_m2_build.log')
```

项目开发时参考了原模型的图样，但当前工程不保留或依赖对应图片。`compare_m2.py` 仅作为可选离线分析工具；如需使用，必须由使用者显式提供合法取得的输入。

实际结果：Unity 退出码 0；88/88 checks，0 failure，2 warnings；Shader messages=0、Pass=1、keyword pragma=1、samples=2。BandMask 正/背 MAE=252.037；最终色正/背 MAE=21.865；中间像素比默认/近/远为 0.012143/0.021175/0.037435；M2/M1 正面前景亮度比 0.953。

两项警告均非 M2 新回归：Unity/PMX 顶点计数差异沿用 M0 的稳定不变量判断；31 子网格对应 31 个 Forward draw 的实际性能需人工 Frame Debugger/Profiler 测量，尚未声称通过性能预算。

## 图样参考、阶段差异、风险与回退

- 正面 M1/M2 亮度为 0.547/0.521，侧面为 0.503/0.519；侧向光下硬亮区占比增加且 M3 阴影/环境项尚未实现，不用越阶段功能伪装修复。
- 红裙与蓝眼仍较鲜明。原模型图样体现的柔和整体调色、材质高光、环境暗部和后处理不属于 M2。
- 面部仍没有 Face SDF，发丝没有专用高光，金属仅使用独立 Ramp 行，且无描边；只能确认 BaseMap 分区和二分管线正确，不能宣称最终风格匹配。
- 当前 31 材质保持可回退和语义边界，但 draw call 偏高；M9 前不激进合并。
- 回退到 M1：删除 `Assets/Sandrone/Materials/M2`、M2 Shader/Controller/Profile/Editor 脚本、Ramp、M2 场景/文档和 `TestArtifacts/M2`，将 Build Settings 指回 `ToonCalibration_M1.unity`。M0/M1 与源资产无需恢复。
