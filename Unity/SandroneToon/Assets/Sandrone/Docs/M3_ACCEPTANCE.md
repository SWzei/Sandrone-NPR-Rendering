# M3 验收说明：Real Shadows

> 2026-07-16 专项修订：角色 Forward 已与 URP 17.5 的级联坐标契约对齐。非级联/屏幕分支仅在 `REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR` 时使用顶点阴影坐标；四级 cascade 在片元由 `TransformWorldToShadowCoord(input.positionWS)` 选择。M3 当前为 `93/93`，D3D11/D3D12 各 10 张切换边界/视角/光向审计为 0 failure。

## 1. Phase 目标与完成状态

- 已实现：URP 主方向光实时接收/投射阴影、`ShadowCaster` Pass、4 级 CSM 与软阴影变体、投射阴影风格化、M2 明暗分区合并、Alpha Clip 前向/阴影一致性、8 种调试视图、正/侧截图与定量对比。
- 未实现：M4 ControlMap/MatCap/材质高光，M5 Face SDF，M6 眼/发 Stencil，M7 描边，M8 Emission/Bloom，M9 后处理/优化。
- 自动验证：2026-07-16 真实 D3D11 全链退出码 0，`M3ValidationReport.json` 为 **93/93 checks、0 failures**；D3D11/D3D12 `CascadeShadowAudit.json` 均为 5 shaders、10 captures、0 compiler message、0 failure。
- 当前状态：级联坐标专项已通过自动实拍与可见 M9 Frame Debugger 回归；产品内容场景的长时间动态镜头仍需按具体交付镜头补测。

## 2. 现状检查及设计依据

- 工程锁定 Unity `6000.5.3f1`、URP `17.5.0`、Linear；PC=Forward+，Mobile=Forward。M2 回归门为 88/88。
- PC RP Asset：2048 主光 Shadow Map、4 cascades、Soft Shadows、Distance=50、Depth Bias=0.1、Normal Bias=0.5。Mobile：1024、1 cascade、Hard Shadows、Bias=1/1。M3 不改写这些既有设置。
- URP 17.5 本地源码确认主光关键字为 `_MAIN_LIGHT_SHADOWS[_CASCADE/_SCREEN]`，软阴影为 `_SHADOWS_SOFT[_LOW/_MEDIUM/_HIGH]`，ShadowCaster 点光变体为 `_CASTING_PUNCTUAL_LIGHT_SHADOW`。
- 对文档公式的项目选择：先把 `shadowAttenuation` 映射成可控投射遮罩，再用 `min(formBand, cast)` 合并，不直接做“PCF 软边 × 硬色阶”的重复边界。

## 3. 实现方法与关键技术原理

`UniversalForward` 复用 URP Lit 的条件契约：仅在 `REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR` 时由顶点写入 `GetShadowCoord`；若定义 `MAIN_LIGHT_CALCULATE_SHADOWS` 但不允许该插值器（四级 cascade），片元使用当前像素 `positionWS` 调用 `TransformWorldToShadowCoord`，再传给 `GetMainLight(shadowCoord)`。`rawCast=shadowAttenuation`，`styledCast=lerp(1-strength,1,smoothstep(low,high,rawCast))`，最终 `litMask=min(formBand,styledCast)`后采样同一 M2 Ramp。

`ShadowCaster` 使用 URP `ApplyShadowBias`/`ApplyShadowClamping`。Opaque/Cutout 前向和投射 Pass 共享 `_BaseMap×_BaseColor.a×_LayerWeight`、`_Cutoff`；`_ZWrite<0.5` 的 alpha-blended overlay 不写入二值 Shadow Map，避免把半透明混合误解为不透明投影。当前 Shader 共 2 Pass、4 条 keyword pragma、3 次源码级纹理采样（Forward Base+Ramp，Caster Base alpha）。

## 4. 修改文件及原因

| 文件/目录 | 原因 |
|---|---|
| `Shaders/SandroneToonShadowM3.shader` | M2 着色+实时阴影接收、ShadowCaster、Alpha Clip 契约和调试视图 |
| `Shaders/SandroneShadowReceiverM3.shader` | 隔离测试地面接收结果，不自行投影 |
| `Runtime/SandroneM3Controller.cs` | 保留头部语义轴，通过 MPB 设置阴影参数/调试模式 |
| `Runtime/SandroneM3ShadowProfile.cs` | 版本化 `SandroneShadowProfile_v1_M3` 输入契约 |
| `Editor/SandroneM3Bootstrap.cs` | 可重建材质、场景、探针纹理、16 张正式截图与回归门 |
| `Editor/SandroneM3Validator.cs` | 93 项编译/结构/材质/管线/图像自检，覆盖五个角色 Shader 的级联契约 |
| `Editor/SandroneCascadeShadowAudit.cs` | D3D11/D3D12 级联边界、视角和光向真实渲染审计 |
| `Materials/M3` | 31 个从 M2 复制的角色材质 + receiver/blocker/alpha probe |
| `Configs/SandroneShadowProfile_M3.asset` | 实例化阴影风格参数 |
| `Tests/Textures/M3_AlphaClipProbe.png` | 项目自生成的 128² 环形 Alpha 一致性探针 |
| `Tests/Scenes/ToonCalibration_M3.unity` | M3 独立校准场景，不覆盖 M0–M2 |
| `Scripts/Analysis/compare_m3.py` | 可选离线颜色分布工具；外部输入必须由使用者合法取得并显式提供 |

## 5. 当前目录结构

```text
Assets/Sandrone/
  Configs/SandroneShadowProfile_M3.asset
  Materials/M3/                 # 31 character + 3 validation materials
  Runtime/SandroneM3Controller.cs
  Runtime/SandroneM3ShadowProfile.cs
  Shaders/SandroneToonShadowM3.shader
  Shaders/SandroneShadowReceiverM3.shader
  Editor/SandroneM3Bootstrap.cs
  Editor/SandroneM3Validator.cs
  Tests/Scenes/ToonCalibration_M3.unity
  Tests/Textures/M3_AlphaClipProbe.png
  Docs/M3_ACCEPTANCE.md
TestArtifacts/M3/
  M3ValidationReport.json
  ReferenceComparison/
  Debug/
```

## 6. 对外接口、输入输出与扩展点

- 输入：BaseMap/BaseColor/LayerWeight、M2 Ramp/行/阈值、主光、Shadow Map、`SandroneM3ShadowProfile`。
- 输出：最终颜色和 ShadowCaster depth；调试输出为 Final、Raw Cast、Styled Cast、Form Band、Final Lit Mask、Ramp Sample、Cascade Index、Silhouette。
- M4 可在不改 ShadowCaster alpha 契约的前提下加 ControlMap/MatCap；M5 可替换脸部 form mask；M7 为独立描边 Pass。

## 7. Unity 配置与场景操作

1. 用 Unity 6000.5.3f1 打开，Console 0 Error，打开 `ToonCalibration_M3.unity`。
2. `M3_MainDirectionalLight`：Realtime、Soft Shadows、Strength=0.85；场景保存状态为地面激活、blocker/alpha probe 关闭。
3. 正式目标为 Windows PC，使用 `PC_RPAsset` 的 Forward+ / 4 cascades / 2048。Mobile Asset 仅保留历史兼容配置，本阶段不做 Android/真机支持声明。
4. Alpha 探针全程保持生产 4 cascades；验证布局使 caster/receiver 处于相近相机深度，禁止临时切 1 cascade。

## 8. Blender 资产制作与导出设置

M3 不修改 `.blend`/FBX。继续使用 `Source/Blender/Sandrone_MMD_Baseline.blend`：Apply Transform，Normals=`Import`，Tangents=`Calculate Mikktspace`，保留 31 submeshes、61 BlendShapes、692 renderer bones。

若出现阴影痤疮/漏影：先检查 Face Orientation、重叠面、反向法线和过长三角面，再调 Bias。Alpha Clip 边界应在 UV 缝和远距离下也与主 Pass 一致。

## 9. 缺失资源及制作方案

| 资源 | 用途/规格 | 制作、导入与验证 |
|---|---|---|
| ControlMap（M4） | R 高光、G AO、B 响应、A ID/行；2048² RGBA | UV 烘焙+手绘；Linear、No lossy compression；通道逐项显示 |
| MatCap（M4） | 金属/饰品视空间响应；512² | 正交球绘制/烘焙；Clamp；旋转相机检查锁定行为 |
| FaceMap（M5） | 左右可镜像 SDF；1024–2048² | 以 `頭` 骨 R/U/F 制作；Linear/Clamp/No Mip 起步；360° 水平光扫 |
| Outline Normal/Width（M7） | 平滑外扩方向+局部线宽 | Blender 同位置顶点平均，Color.rg 法线/Color.a 宽度；1/3/10 m 检查 |
| EmissionMask（M8） | 眼/晶体/显示屏 HDR 选择 | Linear 单通道/打包；皮肤与白布为 0；Bloom extraction 验证 |

M3 所需 Ramp 已由 M2 提供；Alpha 探针是测试资产，不冒充角色 FaceMap/ControlMap。

## 10. 自动自检、测试命令与实际结果

必须使用图形设备，不加 `-nographics`：

```powershell
$UnityEditor = '<Unity 6000.5.3f1 Editor executable>'
$ProjectPath = (Resolve-Path 'Unity/SandroneToon').Path
& $UnityEditor -batchmode -quit -projectPath $ProjectPath -executeMethod SandroneToon.Editor.SandroneM3Bootstrap.Build -logFile (Join-Path $ProjectPath 'TestArtifacts/M3/unity_m3_build.log')
```

开发时参考了原模型的图样；当前工程不保留图样文件，也不把外部图样作为 M3 自动验证输入。

最新完整回归实测：接收 blocker masked MAE=63.259；角色对地面 MAE=0.854，差异覆盖=2.439%；Raw→Styled MAE=6.613；Form→Final Lit MAE=13.200；Alpha 可见/投射 IoU=0.9890；M3/M2 前景亮度比=0.9808。稳定遮挡/受光/软边过渡像素比为 0.1975/0.5944/0.2082。

补充 Game View 证据：在 768×1680 Play Mode 下，旧接收 Shader 的顶点级 cascade/atlas 坐标插值会沿 Unity Plane 网格产生矩形分块。现改为 fragment 级 `TransformWorldToShadowCoord(input.positionWS)`，并保留 Screen-space 分支；Frame Debugger Ground event 前后已证明修复对象是正式接收面，而非验证几何。完整记录见 `GAME_VIEW_SHADOW_FIX.md`。

两项 warning：M0 继承的 Unity 53,386 vs PMX 61,973 顶点缓冲差异；31 材质理论可产生 31 Forward + 31 ShadowCaster draws，实际 GPU 成本待人工 Profiler。

## 11. 人工验收步骤与通过标准

1. Game 视图切 `FinalToon/CastShadowRaw/CastShadowStyled/FormBand/FinalLitMask/CascadeIndex`；阻挡物启用前后，遮影必须仅出现在正确光向区域。
2. 方向光绕 Y 轴 360°，观察脸、刘海、手臂、裙摆：无大片痤疮、明显漏影、阴影反向；分区边和投射边不出现稳定的双重边。
3. 相机在 0.65×/1×/1.45× Orthographic Size 下移动；级联边界无明显跳变，软边无连续闪烁。
4. 启用 `M3_AlphaClipProbe`；环形可见轮廓与地面影子的内孔/缺口同时保留，无整张 Quad 投影。
5. Frame Debugger：每个角色子网格是 `M3ToonShadow/UniversalForward` + `ShadowCaster`；PC 有 Forward+ 及 4 cascade；无 M4+ ControlMap/MatCap/Outline/Emission Pass。
6. Profiler/Frame Debugger 记录 draw calls、Shadow Map 更新和 GPU ms；只记录实测值，不以理论 62 draws 宣布性能通过。

通过标准：Console 0 Error；报告仍 88/88；角色能投影且能被外物遮影；Alpha IoU>0.80；没有可感知的级联跳变/痤疮/漂浮/双边；M2 颜色和透明状态无回归。

## 12. 图样参考与阶段差异分析

- 原模型图样仅用于开发时理解风格方向，不进行配准、形变或自动门禁比较。
- M3 增加了可验证的自遮挡、外物遮挡与地面投影；原模型图样体现的脸影、发高光、金属高光、环境暗部和描边分别属于 M4–M7，本阶段不用越阶功能修饰。
- 黑/白/红/金、肤/发/眼的 BaseMap 分区保持正确；姿态、构图和附属几何差异不通过图像配准掩盖。

## 13. 已知问题、风险与回退方案

- 31 材质槽导致 ShadowCaster draws 增长；M9 前保留可追踪材质边界，不提前合并。
- Alpha-blended overlay 当前不投射二值阴影；若美术需要应改为显式 dither/coverage 契约，不能隐式当 Opaque。
- 顶视正交平面的 4-cascade 测试会因 per-cascade caster culling 出现多边形空洞；该布局已废止。正式 Alpha 契约截图采用正面相机与竖直 caster/receiver，全程 4 级联。
- 回退：删除 `Materials/M3`、M3 Shader/Runtime/Editor/Profile/场景/探针/文档与 `TestArtifacts/M3`，Build Settings 指回 `ToonCalibration_M2.unity`。M0–M2、源 PMX/Blender/FBX 无需恢复。

## 14. 文档修正内容

- 实现状态更新为 M0–M3 自动验证通过。
- 记录 `shadowAttenuation` 截图中的线性 0.15 在 sRGB PNG 约为 108；软边指标必须排除稳定阴影平台，不能把所有灰阶都当过渡。
- 记录顶视正交平面的级联裁剪伪影，并明确禁止以单级联隔离替代 4 级联验收。
- 修正“Receiver 必须在顶点调用 GetShadowCoord”的过时契约：大三角形/规则 Plane 上必须逐像素选择 cascade，禁止跨 atlas tile 插值。

## 15. 下一 Phase 建议（仅说明）

M4 已完成并以 M3 88/88 为回归门；下一阶段仅建议 M5 Face SDF，本次回归未实施 M5。
