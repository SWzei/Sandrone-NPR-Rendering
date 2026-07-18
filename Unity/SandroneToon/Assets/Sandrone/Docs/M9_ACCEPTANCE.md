# M9 最终合成 / 性能收口验收

## 1. Phase 目标与完成状态

M9 的既定功能与本轮证据链已实现。正式目标平台为 Windows PC；仓库中的 Mobile Asset 仅是历史兼容配置，不代表 Android/真机发布支持。2026-07-16 已修复 M3/M4/M5/M6/M8 角色 Forward 的 PC 四级级联坐标契约，并通过 D3D11/D3D12 各 10 张边界/视角/光向审计。M0–M9 自动链、D3D11/D3D12 可见 Game View / Frame Debugger 与 Windows Player 均为零失败。M7–M9 单一所有者状态已实现禁用/销毁恢复；M0–M6 共享 MPB 重叠字段的任意中间组件独立卸载仍需统一协调器，属于架构待确认项。没有提前定义或实现 M10。

## 2. 现状检查及设计依据

- Unity `6000.5.3f1`、URP `17.5.0`、Linear；M8 `110/110`、M7 `131/131`、M6 `154/154`、M5 `94/94`、M0–M4 `30/30` 为入口硬门。
- M8 参考对比表明前景饱和度 `0.2917`，高于未配准参考的 `0.1782`；亮度 `0.4837`，参考为 `0.4598`。因此 M9 只做有界全局收口，不改材质颜色或环境光。
- Neutral 与 ACES 同机位 MAE 约 `24/255`；ACES 明显压暗黑红区域并改变整体响应，Neutral 更能保留 M4–M8 已验证的材质关系。
- TAA 需要运动向量、历史帧、透明/发光与像素描边稳定性证据；当前静态标定场景没有这组证据，故明确延期，桌面采用 SMAA High。
- 31 槽含不同 Stencil、透明队列、Blend、ShadowCaster 和 M5–M8 专用 Pass；692 骨骼也没有覆盖动作集。M9 不做高风险材质合并或删骨。

## 3. 实现方法与关键技术原理

- M9 使用 priority `20` 的独立全局 Volume；Profile 只含 `Tonemapping(Neutral)` 与 `ColorAdjustments`。M8 Bloom 仍在独立 Volume，避免调色资产重写 Bloom。
- 固定参数：Post Exposure `-0.08`、Saturation `-18`、Contrast/Hue `0`、Color Filter 白色；这些参数对 M8 基线实施有界降饱和，同时不注入色偏。
- `SandroneM9FinalController` 只切换 Volume weight 与相机 AA：PC=`SMAA High`，Mobile=`FXAA`；不写 Renderer、Material 或 MPB。
- 构建前变体处理只针对 `Sandrone/*`，裁掉本项目未使用的 screen-space main shadow、soft low/medium 和 punctual-light caster 组合；回调早于 URP 内建裁剪。本轮 PC RP Asset 重建实测输入 `45`、移除 `1`、保留 `44`；旧 `33/1/32` 是构建配置未锁定前的历史值，不再作为当前证据。
- Windows Player 先预热 `120` 帧，再采样 `240` 帧；记录 FrameTiming CPU/GPU、Profiler Render counters、当前 GPU/API 和真实 Player 帧。
- Overdraw 使用审计专用加法 Shader、原 Cull/AlphaClip、`ZTest Always`，统计角色+描边+剑的片元层数；它是固定视角层数估算，不等价于硬件 early-Z/ROP 计数。

## 4. 修改文件及逐项修改原因

- `Runtime/SandroneM9FinalProfile.cs`：版本化 Tonemapping、调色、AA 和 TAA 决策契约。
- `Runtime/SandroneM9FinalController.cs`：独立控制调色与 PC/Mobile AA，不接触材质。
- `Runtime/SandroneM9PerformanceProbe.cs`：Release Player 预热、FrameTiming、Profiler counter 和 PPM 当前帧输出。
- `Shaders/SandroneOverdrawAuditM9.shader`：仅用于 Overdraw 证据，不进入角色最终材质。
- `Editor/SandroneM9ShaderVariantAudit.cs`：保守的 Sandrone Shader 变体裁剪与逐 Pass 报告。
- `Editor/SandroneM9Bootstrap.cs`：M8 硬门、Profile/Volume/场景、A/B、Overdraw、Player build/run。
- `Editor/SandroneM9Validator.cs`：91 项配置、精确材质、证据、构建、性能和回归验证。
- `Editor/SandroneM9GameViewAudit.cs`：真实可见 Play Mode Game View 与 Frame Debugger 逐事件审计。
- `Scripts/Analysis/compare_m9.py`：可选离线分析、同机位差异、接触表和 Player PPM→PNG；外部图样输入不属于默认工程依赖。
- 生成资产：M9 Profile、Volume、场景、截图、报告与 Windows Player。

## 5. 当前目录结构

```text
Scripts/Analysis/compare_m9.py
Unity/SandroneToon/Assets/Sandrone/
  Configs/SandroneFinalProfile_M9.asset
  Configs/SandroneFinalVolume_M9.asset
  Runtime/SandroneM9*.cs
  Shaders/SandroneOverdrawAuditM9.shader
  Editor/SandroneM9*.cs
  Tests/Scenes/ToonCalibration_M9.unity
  Docs/M9_ACCEPTANCE.md
Unity/SandroneToon/TestArtifacts/M9/
Unity/SandroneToon/TestArtifacts/M9GameViewAudit/D3D11|D3D12/
Unity/SandroneToon/TestArtifacts/Audit/Evidence/
Unity/SandroneToon/TestArtifacts/Audit/CascadeShadowContract/D3D11|D3D12/
Unity/SandroneToon/TestArtifacts/Audit/Lifecycle/
```

## 6. 对外接口、输入输出及后续扩展点

- 输入：完整 M8 场景、M8 Bloom Volume、Windows PC URP Asset、M9 Final Profile。Mobile URP Asset 不属于正式发布目标。
- 运行接口：`GradingEnabled`、`AntiAliasingEnabled`、`MobileQuality`；输出为 Neutral 最终合成和平台 AA。
- 构建接口：菜单 `Sandrone > M9 > Build Final Composition and Player`；输出 Windows x64 Player、性能 JSON、变体 JSON 和 PPM 帧。
- 扩展点：在获得动画/运动向量证据后可在同 Profile 契约新增 TAA 方案；目标机 PIX/RenderDoc 报告可追加到性能证据，但不得改写现有 Unity 指标含义。

## 7. Unity 配置与场景操作

打开 `Assets/Sandrone/Tests/Scenes/ToonCalibration_M9.unity`。默认相机 HDR/Post 开启，桌面 SMAA High；M8 Bloom Volume 与 M9 Final Volume 同时启用且资产分离。Build Settings 只启用 M9 场景。真实审计必须以可见 Editor 调用 `SandroneToon.Editor.SandroneM9GameViewAudit.RunFromCommandLine`，不可用旧截图、`-nographics` 或离屏数据替代 Frame Debugger。

## 8. Blender 资产制作与导出设置

M9 不需要 Blender 写入，也未修改任何 FBX、PMX、UV、骨骼、法线或纹理。继续沿用 M8 已验证的标准角色与晶体剑导入设置。若未来删骨，必须先建立覆盖表情、裙摆物理和武器挂点的动作语料，再对 bindpose、BlendShape 和蒙皮逐帧回归；本阶段没有这项证据。

## 9. 缺失资源及制作方案

- Android/物理移动设备不在当前正式目标范围；现存 PC GPU 上的 Mobile FXAA 截图只能作为历史兼容诊断，不能写成移动支持或性能证据。
- 缺少外部显存带宽：Unity 仅暴露上传/分配类 counter；需用目标机 PIX 或 RenderDoc 记录外存读写、带宽和 ROP/early-Z。
- 缺少 TAA 运动稳定性素材：需至少包含骨骼动画、裙摆运动、头发透明层、发光、相机平移和描边亚像素移动。
- M5 FaceMap、M6 ControlMap、M7 Outline Normal/width、M8 EmissionMask 仍包含已标注的工程种子；M9 没有把调色作为替代美术资源。

## 10. 自动自检、测试命令与实际结果

```powershell
$UnityEditor = '<Unity 6000.5.3f1 Editor executable>'
$ProjectPath = (Resolve-Path 'Unity/SandroneToon').Path
& $UnityEditor -batchmode -quit `
  -projectPath $ProjectPath `
  -executeMethod SandroneToon.Editor.SandroneM9Bootstrap.Build `
  -logFile (Join-Path $ProjectPath 'TestArtifacts/M9/unity_m9_build_02.log')
```

- M9 Validator：D3D11/D3D12 均 `91/91`，failure `0`，warning `3`，Shader compiler message `0`；M0–M8 硬门全部由同一 evidence session 重新生成。M3 因新增五 Shader 级联契约检查为 `93/93`。
- Windows x64 Release Player：Succeeded，error `0`，build warning `0`，`31.333 s`，总目录 `455,014,334 bytes`。报告锁定产品名 `Sandrone Toon M9`、Quality `PC`、`PC_RPAsset`、render scale `1`、4 cascades 与 soft shadow。
- Player：D3D12 / RTX 4060 Laptop，768×1280，120 warmup + 240 sample；frame mean/median/P95 `1.870/1.750/2.956 ms`。CPU mean/median/P95/max `1.874/1.758/2.957/7.316 ms`（240 样本）；GPU mean/median/P95/max `0.702/0.693/0.738/0.920 ms`（173 个有效 GPU 样本）。GPU 样本未覆盖全部 240 帧，因此只属本机固定窗口部分验证。
- Render counters：Standard Draw Calls mean `91`、SetPass mean `99`、Triangles mean `236,184`、Vertices mean `256,265`、SRP Batcher Draw Calls mean `1`；均为该探针窗口的实际记录值。Standard Draw 远高于 SRP Batcher Draw，当前逐槽 MPB/多材质成本仍是产品化风险。
- 变体：45 输入、1 移除、44 保留；Overdraw 前景覆盖 `27.282%`、mean `7.797`、p95 `19`、max `38` 层。
- 修复后的阶段分布门：M8/M9 饱和度 `0.2921/0.2525`，降幅有界；亮度 `0.4837/0.4820`，变化受控。Validator 只比较锁定机位的项目内 M8 基线与 M9 最终帧，不读取外部图片。

## 11. 人工验收步骤与通过标准

1. 可见 Editor 打开 M9 场景，Play，Game View 设 768×1280。
2. 对比 Grading Off/Neutral On：材质槽和红裙响应不变，只发生全局有界曝光/饱和度变化；Neutral MAE 应约 `2/255`。
3. Windows PC 选 SMAA High。历史 Mobile/FXAA 路径只作非正式兼容诊断，不作为 Android 或真机验收。
4. Frame Debugger 应看到 M5/M6/M4=`2/10/18`、M8 Eye/VFX/Sword=`1/1/1`、Outline `14`、ShadowCaster `46`、Receiver `1`、Bloom `16`、SMAA `3`、Final Post `1`。
5. 退出 Play 并重新打开场景，Controller 必须恢复 Neutral+PC SMAA 默认状态。

本次真实可见审计：D3D12 / RTX 4060 为 122 events，D3D11 为 134 events；所有目标 Pass 计数命中，failure `0`，生命周期恢复全部通过。四张同条件截图的 D3D11/D3D12 MAE 为 `0.000003–0.000005/255`，最大通道差 1。

## 12. 图样参考与项目内对比分析

开发时参考了原模型的图样，但工程不保留或读取具体图片。M8/M9、Neutral/ACES、PC/Mobile 使用锁定机位的项目内帧逐像素比较：M8→M9 MAE `2.382/255`，前景饱和度从 `0.2978` 降至 `0.2523`，亮度从 `0.4814` 变为 `0.4820`；Neutral→ACES MAE `24.002`，证实两种 Tonemapper 并非等价；PC→Mobile MAE `1.086`，变化覆盖 `9.22%`，主要来自 AA/管线路径。M9 最终红裙 `96,829 px`、粉色错误 `0`；Windows Player 为红裙 `58,100 px`、粉色错误 `0`。

## 13. 已知问题、风险与回退方案

- 当前 Player 数据来自本机 RTX 4060 Laptop，不可外推到其他 PC 硬件；Android/物理移动设备不在正式范围。
- Unity 未提供外存带宽，当前不能宣称带宽通过；Overdraw 也不是硬件 early-Z/ROP 计数。
- 性能探针本身会写报告/帧，极值与平均值只代表固定标定窗口，生产场景仍需独立采样。
- 31 槽、692 骨骼未优化是有证据的保守决定，不代表已经达到最终内容规模的 Draw/骨骼预算。
- M3/M4/M5/M6/M8 级联坐标问题已修复：D3D11/D3D12 均检查 5 个 Shader、10 个级联边界/视角/光向实拍，compiler message/failure 均为 0。修复前后同条件角色 Raw Shadow 的变化像素为近/中/远 `104/272/450`，影响随距离增加但集中在少量阴影像素。
- M0–M6 在同一 Renderer material-index MPB 中存在跨阶段共享和重叠 `_Head*`/`_CastShadow*` 字段。当前启用链与多实例隔离已通过；若要求任意中间组件独立移除并只删除自己的键，需要统一所有权协调器，不能用 `MaterialPropertyBlock.Clear` 局部实现。
- 干净脚本重编译有 114 条唯一 CS0618 弃用警告，分布在 25 个 Editor Bootstrap/Validator/Audit 文件，主要为 Unity 6.5 废弃的 `FindFirstObjectByType` 和带 `FindObjectsSortMode` 的重载。当前不影响构建，但不是零警告基线。
- 回退：Build Settings 指回 M8，删除 M9 场景、两个 Config、M9 Runtime/Editor/Overdraw Shader 和 M9 证据即可；M0–M8 与原始资产不受影响。也可仅把 M9 Volume weight 置 0、AA=None，精确回到同 HDR 的 M8 合成基线。

## 14. 文档修正内容

- “M9 必须记录带宽”改为“Unity 内可记录上传/分配，外存带宽必须由 PIX/RenderDoc 补测”；未测不写成通过。
- “可直接材质合并/删骨”改为证据驱动：不同 Pass/Stencil/透明状态与无动作语料时拒绝优化。
- TAA 不因引擎支持就默认启用；没有运动向量与描边稳定性证据时，SMAA 是当前可验证桌面路径。
- 构建后变体数必须来自真实 Player build 的 preprocess 回调，而不是 Editor 关键字枚举。
- Overdraw 热图必须注明 `ZTest Always` 加法层数方法和硬件计数边界。
- Player 构建必须在调用 `BuildPipeline.BuildPlayer` 前显式选择 PC Quality 与 `PC_RPAsset`；性能 JSON 必须记录产品名、RP Asset、render scale、cascade/soft-shadow 状态、独立 CPU/GPU 样本数及 median/P95，禁止用旧报告或错误管线冒充 PC 性能。
- 所有正式证据必须先创建 `SandroneEvidenceSession`；最终 manifest 记录 session ID、起止 UTC、Unity/API/设备、正式源文件与本轮产物的 SHA-256/长度/写入时间。结构化 `failureCount/failures`、生成时间和会话 ID必须同时通过，禁止用文件存在或字符串命中代替。
- 负向测试必须验证：篡改报告、旧时间戳和源码总指纹漂移都被拒绝；`-nographics` 的全黑渲染失败日志保留为错误运行条件证据。

## 15. 下一 Phase 建议（仅说明，不实施）

文档定义的 M0–M9 已结束，没有 M10。若进入产品化，应作为新范围先建立目标设备/内容预算：真机 GPU/带宽捕获、动画压力场景、TAA 稳定性、正式美术遮罩替换与按 Pass/Stencil 兼容组的材质合并实验；在新验收标准确认前不应直接改当前基线。
