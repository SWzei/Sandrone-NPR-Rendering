# M5 Face SDF 验收记录

> 2026-07-16 专项修订：M5 Face Forward 的 PC cascade 坐标已与 URP 17.5 契约对齐；D3D11/D3D12 级联边界审计均为 0 failure。Controller 新增头部轴、开关、材质与阴影参数脏检查，无变化帧不重复写 MPB。

## 1. Phase 目标与完成状态

M5 仅完成脸部 SDF/阈值光照，不实现 M6 头发/眼睛、M7 描边或 M8/M9。长裙片专项修复后的最终状态：M5 Validator `94/94`、Special Audit 0 failure、真实 D3D11 Game View/Frame Debugger 0 failure；M0–M4 综合回归 `30/30`。M5 场景只有槽 0/1 使用 M5 Face SDF 材质，槽 2–30 直接引用对应的 M4 Material 资产；slot 21 的黑色背面层使用 `Cull Back`。SRP Batcher 与 GPU 时间仍需用 Profiler 采样，不能表述为已通过。

## 2. 现状检查及设计依据

- Unity `6000.5.3f1`，URP `17.5.0`，Linear；PC 为 Forward+，Mobile 为 Forward。
- 标准主体：53,386 顶点、69,864 三角、31 submesh、692 renderer bones；头骨 `頭` 存在。
- 源资产没有 ILM/Face SDF；`T_Face.png` 是 BaseMap。Face 响应只对应槽 0/1 `颜/颜2`；槽 17/18 是颈/身体皮肤。
- 文档 8.2 的 Head Forward/Right/Up、水平光投影、`q=(1-f)/2`、左右翻转与 `fwidth` AA 作为基线；“游戏原管线一定相同”仍属未证实推广。

## 3. 实现方法与关键技术原理

`Lh = normalize(L-U*dot(L,U))`，`f=dot(Lh,F)`，`r=dot(Lh,R)`，`q=(1-f)/2`。Shader 同时采样 `uv` 与 `1-uv.x`，在 `r=0` 附近按 `_FaceMirrorBlendWidth=0.10` 做连续混合；这替代了会在符号过零时整脸跳变的硬分支。FaceMap R 经 `smoothstep(q-softness-AA,q+softness+AA,sdf)` 得到 FaceLit。接近垂直光时用 HeadForward 回退。最终 `FinalLit=min(lerp(LambertBand,FaceLit,FaceWeight),StyledCastShadow)`，所以 M5 不绕过真实自阴影或投影阴影。

M5 的脸槽保留 M4 的 ControlMap、MatCap、ResponseType、透明状态；ShadowCaster 复用已审计的 M4 Pass。专项审计确认：复用 Pass 时 `UnityPerMaterial` 必须保留完整、同顺序的 M4 前缀，否则 SRP Batcher/Pass 间常量解释未定义。为从结构上保证 M5 只影响面部，最终场景不再为非脸槽复制 M5 材质：槽 2–30 直接复用 M4 资产与 `Sandrone/M4/MaterialResponse` Pass。Debug/开关通过逐材质槽 MPB 写入，不改 shared material、不使用全局 Debug Keyword。

## 4. 修改文件及逐项修改原因

- `Runtime/SandroneM5FaceProfile.cs`：FaceMap、槽索引、Softness/AA 的版本化契约。
- `Runtime/SandroneM5Controller.cs`：头骨轴、灯光、Face/M4 功能开关和 17 个 Debug Mode 的逐槽 MPB 更新。
- `Shaders/SandroneFaceSDFM5.shader`：M4 Forward 扩展 Face SDF；复用 M4 ShadowCaster。
- `Editor/SandroneM5Bootstrap.cs`：仅为槽 0/1 生成 M5 材质，槽 2–30 原样复用 M4 资产；负责稳定场景、真实 D3D11 截图和主动响应探针。
- `Editor/SandroneM5Validator.cs`：编译/绑定/状态/贴图/坐标/阴影/Debug/PC-Mobile/截图 Hash 与 mask MAE 验证；强制检查 29 个非脸槽与 M4 资产为同一对象，并检查 Shader 内部各 `UnityPerMaterial` 布局一致。
- `Editor/SandroneM5SpecialAudit.cs`：31 槽 M4/M5 回归、近黑连通域、20° 灯光扫角、过零连续性和 17 模式语义门。
- `Editor/SandroneM5GameViewAudit.cs`：可见 Editor 的 768×1680 Game View、Play/Reload、PC/Mobile 与 Frame Debugger 事件证据。
- `Textures/Face/Sandrone_Face_SDF.png`：2048² 项目制作种子；只在缺失时生成。
- `Configs/SandroneFaceProfile_M5.asset`、`Materials/M5/*`、`Tests/Scenes/ToonCalibration_M5.unity`：序列化运行资产。
- 本文、`DEVELOPMENT_LOG.md`、根技术文档：同步证据边界、首轮失败和最终结果。

## 5. 当前目录结构

```text
Assets/Sandrone/
  Configs/SandroneFaceProfile_M5.asset
  Editor/SandroneM5Bootstrap.cs
  Editor/SandroneM5Validator.cs
  Materials/M5/                         # 历史生成资产保留；最终场景只绑定槽 0/1
  Runtime/SandroneM5Controller.cs
  Runtime/SandroneM5FaceProfile.cs
  Shaders/SandroneFaceSDFM5.shader
  Textures/Face/Sandrone_Face_SDF.png
  Tests/Scenes/ToonCalibration_M5.unity
  Docs/M5_ACCEPTANCE.md
TestArtifacts/M5/
  AB/ Debug/ Masks/ Pipeline/ ReferenceComparison/
  M5ValidationReport.json
```

## 6. 对外接口、输入输出及后续扩展点

- 输入：`Renderer`、CharacterRoot、`頭`、主方向光、M3 ShadowProfile、FaceProfile、Base/Ramp/Control/MatCap/FaceMap。
- 输出：面部 form-light mask、保留 cast-shadow 的最终 Ramp 颜色、17 个观测模式。
- 运行接口：`DebugMode`、`FaceSdfEnabled`、`SetLightDirectionToSource()`、`Apply()`；M4 三个 Feature 开关保持兼容。
- 扩展点：美术 FaceMap 可直接替换同路径资产；FaceProfile 可扩展 bias/AO，但本 Phase 未加入刘海投影、眼睛 Stencil 或头发高光。

### Debug Mode 对照表

| 编号 | 名称 | 数据来源 | 理论输出/范围 | 验证 |
|---:|---|---|---|---|
| 0 | FinalToon | Base/Ramp/Control/MatCap/Face/Shadow | 最终 RGB | On/Off mask MAE、PC/Mobile |
| 1 | ControlR | Control.r | 红通道 0–1 | Hash、M4 绑定回归 |
| 2 | ControlG | Control.g | 绿通道 0–1 | Hash、M4 绑定回归 |
| 3 | ControlB | Control.b | 蓝通道 0–1 | Hash、M4 绑定回归 |
| 4 | ControlA | Control.a | 当前种子 A=0，理论灰度 0–1；当前应为黑 | 通道语义/像素范围 |
| 5 | NDotH | N、L、V | 灰度 0–1 | Hash |
| 6 | Specular | M4 响应/Control.r | 灰度 >=0 | Hash |
| 7 | MatCapUV | View normal.xy | RG 0–1 | Hash |
| 8 | MatCapSample | MatCap × metal mask | RGB | Hash |
| 9 | MaterialResponse | ResponseType | Matte灰/Skin红/Silk蓝/Metal金 | Hash |
| 10 | FinalLitMask | form/face 与 cast | 灰度 0–1 | Hash |
| 11 | Silhouette | Base alpha/clip | 白色可见像素 | mask 生成 |
| 12 | FaceSDF | FaceMap.r | 仅槽0/1灰度 0–1 | Hash、范围/单调性 |
| 13 | FaceThreshold | `q=(1-f)/2` | 仅槽0/1灰度 0–1 | 左右光响应 |
| 14 | FaceLitMask | smoothstep(SDF,q) | 仅槽0/1灰度 0–1 | 左右光 MAE 2.967；合成图 6.713 |
| 15 | HeadLightAxes | dot(L,R/F/U) | RGB 映射 0–1 | 头骨25° masked MAE 4.789 |
| 16 | FaceVsLambert | `abs(FaceLit-FormBand)` | 仅槽0/1灰度 0–1 | Hash、A/B 定位 |

17 个模式逐项检查通道、灰度/RGB、作用域与输入来源；不再以“Hash 不同”单独判定。切回 0 后槽 0 MPB 读回 `_M5DebugMode=0`、`_FaceSDFWeight=1`；无 `M5_*` 全局 Keyword，未发现 shared material 污染。

## 7. Unity 配置与场景操作

打开 `ToonCalibration_M5.unity`：

1. 选择 `Sandrone_M5`，确认 `SandroneM5Controller` 的 Renderer、Root、`頭`、Directional Light、Shadow Profile 均非空。
2. Game View 固定 768×1280（若继续阴影专项则用 768×1680），关闭 Scene Lighting 干扰，进入 Play。
3. `Face Sdf Enabled` 切换前后只允许脸槽变化；旋转 `M5_MainDirectionalLight` 水平 360°，边界应连续并在左右光时镜像。
4. 把 `Debug Mode` 依次切 12–16；退出 Debug 后阴影、材质槽、Feature 开关应恢复。
5. 在 Graphics/Quality 分别切 `PC_RPAsset` 与 `Mobile_RPAsset`，重复正/侧/背光。

## 8. Blender 资产制作与导出设置

本轮未自动启动 Blender，以下为最终 FaceMap 的可复现制作方案：

1. 导入标准 PMX/FBX，只选 `颜/颜2`，锁定 `頭`、相机焦距、正面和 3/4 验收机位。
2. 建立 Head Forward/Right/Up 空物体；每 15°–30° 水平旋转 Sun，手绘或渲染理想二值脸影。
3. 对每角度遮罩求二维距离/边界并合成为“每 UV 像素开始受光的角阈值”；左右共用一侧数据并验证 UV 镜像。
4. 输出 16-bit 灰度工作图；交付 Unity 时可量化为 8-bit PNG，逐角扫描确认无条带后再决定是否保留 16-bit。
5. 不修改 Base UV、骨骼、自定义法线或 31 槽；FBX 保留自定义法线/骨骼，禁用 Leaf Bones 与无关 All Actions。

## 9. 缺失资源及制作方案

- 缺失：最终美术 `Sandrone_Face_SDF`。当前 2048² 图仅为确定性种子，R=阈值、G/B 保留、非 sRGB、Clamp、Bilinear、无 Mip、无压缩；用途是验证数据链和坐标响应。
- 尚未进入 Phase：头发高光遮罩/切线、眼睛 Stencil 数据、Outline Normal/Width、EmissionMask。不得把当前 FaceMap 或 `T_Face` 兼作这些资源。
- 验证最终图：R 范围覆盖 <0.125 到 >0.875；水平光角扫动时边界单调；左右镜像；鼻翼/脸颊无碎影；远距不闪烁。

## 10. 自动自检、测试命令与实际结果

```powershell
$UnityEditor = '<Unity 6000.5.3f1 Editor executable>'
$ProjectPath = (Resolve-Path 'Unity/SandroneToon').Path
& $UnityEditor -batchmode -quit `
  -projectPath $ProjectPath `
  -executeMethod SandroneToon.Editor.SandroneM5Bootstrap.Build `
  -accept-apiupdate -force-d3d11 `
  -logFile '<project>\TestArtifacts\M5\unity_m5_build_d3d11_round2.log'
```

实际：返回正常，M4 gate `94/94`，最终 M5 Validator `94/94`；RTX 4060 Laptop GPU / Direct3D11，Shader compiler message `0`。专项报告：非面部全帧 MAE `0`、31 槽中除脸槽外最大 MAE `0`、裙摆槽 20–27 最大 MAE `0`、新近黑像素/最大连通块 `0/0`；Face 镜像过零 MAE `0.798`、`359°/0°` MAE `0.416`、边界相邻最大位移 `0.658 px`；Debug 语义 `17/17`。

随后执行 `SandroneM0M4RegressionAudit.RunAudit`：综合 `30/30`、0 failure；最终 M5 Validator 为 `94/94`、0 failure、3 warning。可见 Editor 真实 Game View 为 768×1680：Frame Debugger `116` 个帧事件中采集 `101` 条详细 Draw，包含 `2` 条 `M5FaceSDF` Forward、`29` 条 `M4MaterialResponse` Forward、`69` 条角色 ShadowCaster、`1` 条正式地面接收，0 failure。Play 进入/退出、场景重开、Domain Reload 和 Controller 状态恢复均通过。未使用 `-nographics`。

## 11. 人工验收步骤与通过标准

1. Game View 正面、左右 90°、背光：面部无鼻嘴 Lambert 碎影；左右光影方向正确，背光可进入暗部。
2. 水平旋灯 360°：边界无反向、跳变或中央断裂；垂直光附近无 NaN/黑脸。
3. 头骨局部 Y 旋转 ±30°，根节点不动：阴影随头而非世界轴；恢复旋转后画面恢复。
4. Frame Debugger 锁定 `Sandrone_M5` 槽 0/1、Pass `M5FaceSDF`：确认 Base/Ramp/Control/MatCap/FaceMap、MPB、Color/Depth RT、Cull/Blend/ZWrite；ShadowCaster 为复用 Pass，透明槽不提交。
5. Face 开关 A/B：脸部 ROI MAE >1，槽外 <0.5；地面投影和非脸材质不变。
6. PC Forward+ / Mobile Forward、近中远：无粉色/黑色剪影、透明排序变化、阴影矩形或级联跳变。
7. Profiler/Rendering Debugger：记录 Draw Call、SetPass、SRP Batcher、GPU ms；同机位至少 300 帧，报告中位数/P95。此项当前待人工验证。

## 12. 图样参考与阶段差异分析

- 正面默认光：M5 保持原模型图样所体现的平整脸部风格，M4→M5 的脸槽 masked MAE `0.656`，没有为展示效果强行制造大块阴影；原模型图样不作为工程文件依赖。
- 侧向控制图：M5 On 消除了 M4 Lambert 的鼻梁竖直硬切，并产生可设计的脸颊渐进边界；差异仅限槽0/1。
- 参考正面整体更柔和、低饱和；粗略前景统计（受参考背景渐变与构图影响，仅作趋势）为参考 L/S/V `0.351/0.128/0.389`，M5 `0.529/0.218/0.584`。M5 仍偏亮、偏饱和。
- 颜色素材和 31 槽对应保持 M4；脸/颈暗部连续性需要最终 FaceMap 和锁定曝光后人工调色。
- 头发高光、眼部合成、描边、环境渐变与后处理差异属于 M6–M9，未借 M5 越阶段修补。

## 13. 已知问题、风险与回退方案

- 当前 FaceMap 是解析种子，不是最终美术；侧向边界“工程正确”不代表造型最终匹配。回退：关闭 Controller 的 FaceSdfEnabled，或把槽0/1退回 M4 材质。
- FaceMap 无 Mip，远距可能闪烁；先做真实距离测试，再尝试无损 Mip，不用压缩或加大 Softness 掩盖。
- 逐槽 MPB 可能影响 SRP Batcher；需 GUI/Profiler 实测。回退：静态角色将轴/参数固化到材质实例，保留动态路径供动画角色。
- M5 ShadowCaster 使用 M4 `UsePass`，是有意减少重复；`UnityPerMaterial` 的 M4 前缀现由 Validator 和逐槽回归共同约束。回退 CBUFFER 修复会重新引入裙摆/阴影未定义行为，不应单独回退；若未来需要脸专用 Alpha/形变，应复制并版本化 Pass。
- Frame Debugger 在 Unity 6000.5 的 `m_MeshSubset` 对拆分 Draw 均返回 0，不能当槽号。审计使用 `_M5AuditSlotId` 作为 `Stencil Ref`，但 `ReadMask/WriteMask=0`、`Always/Keep`，不读写模板也不改变像素；删除该诊断通道可回退事件映射，不影响渲染。
- 自动 PC/Mobile 差异受管线和阴影采样影响：0° 全帧/前景 MAE `1.220/3.184`，需在目标移动设备继续记录 GPU 时间、透明 Overdraw 和 SRP Batcher。

## 14. 文档修正内容

- 根文档实现状态更新为 M5 94/94、Face-only 材质绑定、slot 21 Cull 修复、Special Audit 与真实 Game View/Frame Debugger 已完成。
- 修正 M4 残留的“当前 Debug 使用 global multi_compile”矛盾：当前 M4/M5 Debug 均是逐槽 MPB uniform；变体仅来自 URP 光照/阴影。
- 新增第 18 节，记录 FaceMap 证据边界、垂直光回退、Face/Shadow 组合、槽位作用域和 ROI 验证方法。
- 开发记录保留首轮 3 项失败及修复依据，没有删除失败检查或降低阈值。

## 15. 下一 Phase 建议（仅说明，不实施）

M6 先审计 `髮/髮+` 与眼部多层的实际槽顺序、Alpha/Morph 和已有 `sp.png/hair_s.bmp` 语义，再设计遮罩驱动头发高光与眼睛/刘海遮挡。M5 的 GPU 时间、SRP Batcher 与真机移动端仍是进入 M6 前的人工性能记录项；本轮未实现任何 M6 功能。

## 16. 2026-07-15 专项校验补充

### 黑裙与常量缓冲

- 首轮重新编译后的严格测试真实失败：非脸 MAE `1.561`，裙摆槽最高 `13.670`；不是沿用旧“通过”报告。
- 根因是 M5 `UsePass` 复用 M4 ShadowCaster，但 M5 的 `UnityPerMaterial` 删除/重排了 M4 字段。Unity/URP 要求同 Shader 的逐材质 CBUFFER 布局兼容；跨 Pass 错位属于未定义数据解释。
- 修复为“完整 M4 字段前缀、顺序不变，M5 字段只追加”；并保留 `_M4DebugMode` 兼容写入。修复后槽 20–27 均为 `0 MAE`，非脸全帧和裙摆 Game View ROI 均为 `0 MAE`。
- 可见 Game View 的临时旧布局变体会产生青色未定义输出，证明布局错误严重，但不宣称与用户未归档附件的局部黑裙像素一致。最终画面与逐槽数值才是修复验收依据。

### Face SDF 与灯光

- 主方向光由 `SetLightDirectionToSource()` 实际写入 `Directional Light.transform.rotation`；20° 扫描中 Transform 方向点积误差小于 `1e-5`。
- `r=dot(Lh,HeadRight)` 的硬 UV 翻转改为双采样连续混合；`_FaceMirrorBlendWidth=0.10` 是消除符号不连续的角域，不用于补偿 FaceMap 造型。
- 768×1680 Game View 的 Face ROI：20° 相邻最大 MAE `3.778`，`340°→0°=1.065`，所有 18 张 Hash 唯一；左右镜像对平均 MAE `5.021`，差异主要来自不对称发丝/饰品遮挡。
- `Sandrone_Face_SDF.png` 仍是工程种子。最终资源需按固定头骨/相机对多光角遮罩求阈值场，至少提供 20° 扫描、左右镜像、鼻翼/脸颊边界和远距 Mip 验证。

### Frame Debugger 最终映射

PC Forward+、同一 M5 场景与 768×1680 Game View 下：地面 `M3ShadowReceiver` 1 条；角色 Forward 为槽 0/1 的 `M5FaceSDF` 2 条与槽 2–30 的 `M4MaterialResponse` 29 条；角色 ShadowCaster 69 条。Forward 的共同状态为 Camera Color RT、`ZTest LessEqual`；Opaque 为 `Blend One/Zero, ZWrite=1`，Transparent 为 `SrcAlpha/OneMinusSrcAlpha, ZWrite=0`。只有槽 0/1 启用 Face SDF；其余槽直接绑定 M4 材质，根本不进入 M5 Forward。

专项复核曾发现验证器在首次切换 Mobile Render Pipeline 的同一帧截图，错误捕获了前一阶段的青色 CBUFFER 故障画面，而报告仍为 0 failure。现已把 Pipeline 切换与截图拆成独立预热阶段，并新增青色像素、768×1680、PC/Mobile 0° MAE `<=5`、Mobile 旋光响应 `>0.05` 四项硬门；最终 M5 Validator 的 PC Forward+/Mobile Forward MAE 为 `0.341`。

| 槽 | 最终事件/候选组 | 材质 | Pass | ControlMap | Cull |
|---:|---|---|---|---|---|
| 0 | 103 | `M5_00_M00_Face` | `M5FaceSDF` | Neutral | Off |
| 1 | 104 | `M5_01_M01_FaceSecondary` | `M5FaceSDF` | Neutral | Off |
| 20 | 94,101（与 15 同签名） | `M4_20_M20_Skirt` | `M4MaterialResponse` | Body | Off |
| 21 | 88,89（与 27 同签名） | `M4_21_M21_SkirtSecondary` | `M4MaterialResponse` | Skirt | **Back** |
| 22 | 85,92（与 23 同签名） | `M4_22_M22_SkirtTertiary` | `M4MaterialResponse` | Skirt | Off |
| 23 | 85,92（与 22 同签名） | `M4_23_M23_SkirtOrnament` | `M4MaterialResponse` | Skirt | Off |
| 24 | 91 | `M4_24_M24_MetalOrnament` | `M4MaterialResponse` | Skirt | Off |
| 25 | 84 | `M4_25_M25_OrnamentInner` | `M4MaterialResponse` | Body | Back |
| 26 | 90 | `M4_26_M26_SkirtInner` | `M4MaterialResponse` | Skirt | Back |
| 27 | 88,89（与 21 同签名） | `M4_27_M27_SkirtOrnamentInner` | `M4MaterialResponse` | Skirt | Back |

事件号只适用于 20:01–20:02 这次帧。Unity 6000.5 在该 SRP 路径把 M4 draw 的 `m_MeshSubset` 报为 0，因此相同有效签名的槽按候选组与多重集数量核对；槽 0/1 另由 M5 诊断身份锁定。证据位于 `TestArtifacts/M5GameViewAudit/` 与 `TestArtifacts/M5SpecialAudit/`。

## 17. 2026-07-15 长裙片 Face-only 最终修复

- **问题复现**：旧架构把 31 个槽全部迁移到 M5 Shader。严格重新编译后，非脸 MAE 为 `1.561`，裙片槽最高为 `13.670`；受控不兼容 CBUFFER 变体相对最终画面的裙片 ROI MAE 为 `136.084`。
- **架构根因与修复**：旧 M5 CBUFFER 错位是独立故障源，而“所有槽都换成 M5 Shader”扩大了故障面且违背 M5 只影响脸的边界。保留脸 Shader 的 M4 CBUFFER 前缀修复，同时把槽 2–30 改为直接引用 M4 Material 资产。
- **审计加固**：Validator 重新加载场景依赖，避免 Unity 对已失效对象的 fake-null 导致材质检查静默跳过；新增 `ExactNonFaceM4Reuse`、场景材质检查执行门、M4/M5 Pass 和内部 CBUFFER 一致性检查。可见审计的临时故障 Shader 只破坏目标 CBUFFER 声明，不再额外制造 `_M4DebugMode` 编译错误。
- **最终证据**：M4→M5 Game View 的低半帧 MAE 为 `0`，两帧红裙像素均为 `93,464`；Frame Debugger 为 `2 M5 + 29 M4` Forward、`69` ShadowCaster、`1` receiver，0 failure。槽 20–27 均报告为 M4 材质和 `Sandrone/M4/MaterialResponse`。
- **当前视觉直接根因**：slot 21 黑层与 slot 26 红层轮廓重合。隔离与 Cull A/B 证明 slot 21 的大面积黑色是背面片元；其 `Cull Off` 造成 Opaque 深度竞争。仅将 slot 21 改为 `Cull Back` 后，同一 768×1680 Game View 的 M4/M5 都直接呈红色，不再依赖条件推断或修改截图参数。
- **回退**：若只回退本次 Face-only 收口，可恢复 `SandroneM5Bootstrap.CreateMaterials` 的 31 槽 M5 克隆逻辑、Controller 的 M5-only 写入与旧 Validator 预期，再重建 M5 场景；不应单独撤销 CBUFFER 前缀修复，否则会重新引入跨 Pass 未定义解释。更安全的运行时降级是关闭 `FaceSdfEnabled`，或仅把槽 0/1 换回 M4 材质。

## 18. 错误材质 / 错误贴图资产排除证据

- 直接解析标准 `桑多涅.pmx`：裙槽 20/25 的源纹理就是 `tex\体.png`，21–24、26–27 就是 `tex\裙.png`；现有 M0 MaterialMap 没有把这两类反向绑定。
- 原 `体.png/裙.png` 与 Unity `T_Body/T_Skirt` 的 SHA-256 分别完全一致：`042e37fe…b4ba8c`、`04d82670…6ef1ec`。两张图自身均含红色与黑/深色区域，不能仅凭最终颜色猜测换图。
- 当前真实 `桑多涅_mesh` Renderer 的 31/31 材质 GUID、BaseMap GUID、BaseMap 源哈希均通过；裙槽 20–27 的 ST 都是 `(1,1,0,0)`，BaseColor 都是 `(1,1,1,1)`。
- MPB 对 31 槽的 BaseMap、ST、BaseColor、ControlMap、Ramp、MatCap 覆盖数为 0；裙槽只含 Debug=0、FeatureWeight=1 与单位头轴等预期运行参数。
- 真实 D3D11 Frame Debugger 的 31 条角色 Forward 均暴露 ST/颜色，并按 Base/Control/Ramp/MatCap 与关键材质常量的有效签名多重集和 Renderer 31 槽完全匹配，0 failure。
- slot 21 的最终 RasterState 为 `Cull Back / ZWrite On / ZTest LessEqual / Blend One,Zero`；slot 26 红色正面保持完整。黑色属于正确 `T_Skirt` 的合法黑区，只是旧 Cull 状态错误显示了背面。
- Unity 6000.5 不直接给这些 SRP Draw 提供可靠 submesh/GUID；相同材质参数的槽按候选事件组与数量核对，未伪造唯一槽号。完整逐槽数据在 `TestArtifacts/M5GameViewAudit/M5GameViewAudit.json`，摘要在 `TestArtifacts/M5/M5_ASSET_BINDING_AUDIT.md`。
