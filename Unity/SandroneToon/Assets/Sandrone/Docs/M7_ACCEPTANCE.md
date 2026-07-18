# M7 描边阶段验收

## 1. Phase 目标与完成状态

M7 已闭环完成：在 M6 标准 31 槽角色之外增加独立反面外扩 Renderer，以像素尺度、分材质彩色描边覆盖 14 个不透明主体槽。M6 原 Renderer、材质对象、模型、UV、骨骼、BlendShape、Face SDF、眼部 Stencil、头发高光与红裙响应均未改动。这里的阶段边界是“M7 当时未实现 M8/M9”；当前工程已在后续独立阶段实现 M8/M9。

## 2. 现状检查及设计依据

- Unity `6000.5.3f1`、URP `17.5.0`、Linear；M6 入口门为 `154/154`。
- 标准 FBX 有 53,386 顶点、31 子网格、61 BlendShape、692 bindpose，但无顶点色或独立 Outline Normal。
- 源法线审计得到 17,767 个同位置+骨索引组，其中 12,919 组存在断裂，最大夹角约 90°；直接沿源法线外扩有硬边/UV seam 裂线风险。
- 透明槽 `7–10,19,28–30`、已知重叠内裙槽 `21,25,27` 与细节槽不提交外壳。目标槽固定为 `0,1,12–18,20,22–24,26`。

## 3. 实现方法与关键技术原理

- 独立 SkinnedMeshRenderer 使用派生网格；按“量化位置 + 4 个骨索引”分组、半球对齐后平均法线，保持骨权重、bindpose、BlendShape 与索引不变。
- 只保留 14 个有效描边子网格，避免空子网格仍被 Unity 提交。每槽材质含颜色、像素宽度和源槽 ID。
- 顶点阶段将 view-space normal XY 转为 clip/NDC 位移，乘 `positionCS.w * 2 / _ScaledScreenParams`，得到近似屏幕像素恒宽；掠射方向有最小 XY 限幅。
- Pass 为 `Cull Front / ZWrite Off / ZTest Less / Blend One Zero`，队列在不透明主体之后；外壳不投射或接收阴影，不写 Stencil，不含 Emission/Bloom。
- Runtime Controller 只向描边 Renderer 写 MPB，并同步 BlendShape；关闭描边后源画面与 M6 基线逐像素一致。

## 4. 修改文件及逐项修改原因

- `Runtime/SandroneM7OutlineProfile.cs`：版本化目标槽、颜色、宽度与法线来源契约。
- `Runtime/SandroneM7OutlineController.cs`：独立 Renderer 开关、总宽度、Debug 与 BlendShape 同步。
- `Shaders/SandroneOutlineM7.shader`：反面外扩、像素宽度和彩色描边 Pass。
- `Editor/SandroneM7Bootstrap.cs`：以 M6 为硬门，生成派生网格、14 材质、Profile、场景和离线证据。
- `Editor/SandroneM7Validator.cs`：结构、法线、槽映射、A/B、尺度、管线、裙色与 M0–M6 回归。
- `Editor/SandroneM7GameViewAudit.cs`：真实 Play Mode Game View 与 Frame Debugger 逐事件审计。
- `Scripts/Analysis/compare_m7.py`：可选离线分析与同相机 M6/M7 像素对照；外部图样输入不属于默认工程依赖。
- 生成资产：`Models/Generated/Sandrone_Outline_M7.asset`、`Materials/M7Outline/*`、`Configs/SandroneOutlineProfile_M7.asset`、`Tests/Scenes/ToonCalibration_M7.unity`。

## 5. 当前目录结构

```text
Assets/Sandrone/
  Configs/SandroneOutlineProfile_M7.asset
  Materials/M7Outline/               # 14 个有效槽材质
  Models/Generated/Sandrone_Outline_M7.asset
  Runtime/SandroneM7*.cs
  Shaders/SandroneOutlineM7.shader
  Editor/SandroneM7*.cs
  Tests/Scenes/ToonCalibration_M7.unity
  Docs/M7_ACCEPTANCE.md
TestArtifacts/M7/
TestArtifacts/M7GameViewAudit/
Scripts/Analysis/compare_m7.py
```

## 6. 对外接口、输入输出及后续扩展点

- 输入：M6 源 SkinnedMeshRenderer、描边 Renderer、`SandroneOutlineProfile_v1_M7`。
- 运行接口：描边启用、总宽度倍率、DebugMode；槽级宽度/颜色来自 Profile/材质，MPB 只作用描边 Renderer。
- 输出：M7 场景、派生网格、14 材质、Game View/Frame Debugger JSON 与 A/B/尺度/管线截图。
- 后续可用 DCC authored Outline Normal/顶点色 A 替换当前生成回退；契约和槽表无需改变。M8/M9 本阶段没有预实现。

## 7. Unity 配置与场景操作

打开 `Assets/Sandrone/Tests/Scenes/ToonCalibration_M7.unity`。菜单 `Sandrone > M7 > Build Outline` 重建；`Sandrone > M7 > Validate Outline` 复验。Build Settings 仅启用 M7 场景。真实审计必须使用可见 Editor，Game View 锁定 768×1680，调用 `SandroneToon.Editor.SandroneM7GameViewAudit.RunFromCommandLine`，不得使用 `-batchmode`/`-nographics` 代替 Frame Debugger。

## 8. Blender 资产制作与导出设置

本阶段未修改或重新导出标准 FBX，也未改原 PMX、UV、骨骼和纹理。正式 DCC 替换方案：复制拓扑或使用数据传递生成连续 Outline Normal，烘入明确 UV/Color 通道；顶点色 A 作为 0–1 宽度权重；导出 FBX 时保留 Skin、BlendShape、Tangent、Color 和一致顶点顺序。导入后必须复验 53,386 顶点、61 BlendShape、692 bindpose、14 槽索引计数及近/中/远线宽。

## 9. 缺失资源及制作方案

- 缺少 artist-authored Outline Normal：当前派生网格的同位置+骨索引平均法线是明确标注的工程回退，不覆盖着色法线。
- 缺少手绘宽度：当前派生顶点色为白色 A=1，槽宽 0.72–1.30 px 提供第一层控制；后续应在 DCC 将脸、发梢、薄片收细并重测动画。
- 透明眼层、皮肤/袜/发叠层、腮红及重叠内裙不做几何描边；需要局部内线时应由纹理或独立方案提供。

## 10. 自动自检、测试命令与实际结果

```powershell
$UnityEditor = '<Unity 6000.5.3f1 Editor executable>'
$ProjectPath = (Resolve-Path 'Unity/SandroneToon').Path
& $UnityEditor -batchmode -quit -projectPath $ProjectPath -executeMethod SandroneToon.Editor.SandroneM7Bootstrap.Build -logFile (Join-Path $ProjectPath 'TestArtifacts/M7/unity_m7_build_05.log')
```

- M7 Validator：`131/131`，failure `0`，warning `3`，Shader compiler message `0`。
- 回归硬门：M6 `154/154`、M5 `94/94`、M0–M4 综合 `30/30`。
- 源/派生法线断裂组：`12,919/0`；派生网格顶点、BlendShape、bindpose 与源一致。
- 描边开/关 MAE `0.3812`，关闭描边相对 M6 MAE `0`；红裙像素开/关 `80,962/84,134`，保留率 `96.23%`。
- 近/中/远测得厚度 `0.984/0.930/0.984 px`；PC/Mobile 离线 MAE `0.6312`。

## 11. 人工验收步骤与通过标准

1. 在锁定 M7 场景进入 Play，Game View 设 768×1680；切换 Outline On/Off，确认仅轮廓和部件边界改变。
2. 查看正面、头部三分之四和侧面；脸/浅色区域线较细，身体/红裙较重，无硬边放射、外壳翻面或透明片整块着色。
3. 查看 Near/Mid/Far；宽度应约 1 px，不随距离按世界单位明显膨胀。
4. 打开 Frame Debugger：应为 14 条 `M7Outline`，每条 `Cull Front / ZWrite Off / ZTest Less`，宽度 0.72–1.30；同时保留 2 M5、11 M6、18 M4 Forward。
5. 检查脸部 Face SDF、眼层 Stencil、头发高光和红裙；关闭 Outline 后须与 M6 同相机帧一致。

通过标准：无 Console/Shader error，14 个槽和渲染状态匹配报告，Play 退出后场景/Controller 恢复，无 M8/M9 功能。

## 12. 图样参考与阶段差异分析

开发时参考了原模型的图样，但不保留或配准具体图片；M6/M7 使用项目真实 Game View 的同相机帧逐像素对照。M6→M7 RGB MAE `0.4814/255`，变化覆盖 `1.403%`，18,104 个变化像素中约 28.54% 位于 M6 前景外侧，其余为有效部件交界线。红裙仍保持红色主色与 M6 材质响应，M7 不通过越阶段曝光或调色掩盖差异。

## 13. 已知问题、风险与回退方案

- 当前法线和宽度为 Unity 生成回退，极端动画、负缩放和 DCC 换拓扑需重测；原/平滑法线正面截图 MAE 为 0，因此缝隙改善以网格数据审计为证，不虚构视觉差异。
- M7 增加 14 个角色 Draw；GPU 时间、SRP Batcher、构建后变体和物理移动设备尚未测，不能声称性能通过。
- 首版保留 31 子网格，即使 17 个为空，真实 Frame Debugger 仍提交 31 Draw；已改为 14 个压缩子网格。失败日志保留。
- 回退：Build Settings 指回 M6，删除 M7 Profile/14 材质/派生网格、M7 Shader/Runtime/Editor/场景即可；M0–M6 与原资产未覆盖。

## 14. 文档修正内容

- 将“需要平滑描边法线”落实为可量化契约：源 12,919 个断裂组、派生 0；当前数据是工程生成回退，不冒充 DCC 美术数据。
- 像素恒宽由实际 near/mid/far 测量支持，不再只凭公式声称。
- 透明/薄片不是统一开启反面外扩；按真实 31 槽排除叠层和重叠内裙。
- Unity 会提交空 SkinnedMesh 子网格，因此“禁用材质”不等于零 Draw；生成网格必须压缩为有效子网格。

## 15. 下一 Phase 建议（仅说明，不实施）

M8 应先审计真实 Emission Mask/HDR 需求与 URP Bloom 的隔离范围，再实现可开关的局部发光和阈值可控 Bloom；不得用 Bloom 掩盖当前色调、透明排序或描边问题。本次未创建 M8/M9 资产。
