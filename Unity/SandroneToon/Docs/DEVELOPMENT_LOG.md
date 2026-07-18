# 开发记录

## 2026-07-16 — M0–M9 独立核验与局部修复

- 对当前嵌套 Unity 工程、根目录/Source 重复资源、技术文档、M0–M9 代码与 Shader、配置/场景/报告、PMX/FBX/Blend 不变量、纹理导入语义、URP/Quality/Graphics/Build Settings 和历史日志完成交叉核验。Blender 5.1.2 只读打开主体、可动眼齿轮与晶体剑 `.blend`；主体和可动眼齿轮仍各有两条 `KeyB02_M` dependency-cycle warning。
- 完成四组局部修复：M3 同一 ShadowProfile 对象内部数值变化会刷新逐槽 MPB；M8 两张遮罩和剑模型关闭无必要 Read/Write；M9 在构建前强制 PC Quality/`PC_RPAsset`，性能报告增加管线身份、独立样本数、中位数/P95；M6 纯渲染地面不再遗留导致 Physics-stripped 构建警告的 MeshCollider。
- 新鲜 D3D12 全链重建通过：M0–M4 `30/30`，M4/M5/M6/M7/M8/M9 分别 `94/94`、`94/94`、`154/154`、`131/131`、`110/110`、`91/91`，Shader message 0。Windows x64 Player build 为 `0 error / 0 warning`、454,999,822 bytes，并记录 `Sandrone Toon M9`、D3D12、PC、`PC_RPAsset`、render scale 1、4 cascades、soft shadow。
- 最终 Player 120 帧预热+240 帧窗口：frame mean/median/P95 `1.773/1.618/2.593 ms`；CPU `1.772/1.619/2.606 ms`（240 样本），GPU `0.702/0.694/0.741 ms`（201 有效样本）。较早同配置一轮 frame mean/P95 为 `2.552/5.408 ms`，说明 CPU 窗口仍有明显运行间波动。变体 `45/1/44`；Overdraw mean/P95/max 仍为 `7.797/19/38` 层。仅代表本机固定标定窗口。
- D3D11/D3D12 可见 Game View/Frame Debugger 新鲜复验分别为 134/122 events，目标 Pass 计数一致、failure 0；四张配对截图跨 API MAE `0.000003–0.000005/255`。可见 Editor 日志中的 Unity service-token TLS 异常与项目渲染无关，未中断审计。
- 从最终 M9 状态直接调用 M8 Validator 的失败被原样保留：退出码 1，因为该阶段门要求 Build Settings 只含 M8，而正式工程正确只含 M9。随后通过 M9 依赖链临时建立 M8 阶段状态，M8 `110/110`，再恢复 M9 Build Settings 并以退出码 0 完成；没有降低门槛。
- 未实施重大问题：M3/M4/M5/M6/M8 角色 Forward Shader 在 PC cascade 变体插值顶点阴影坐标，而 URP 17.5 在片元选择 cascade；现有近景证据没有覆盖级联切换。该问题跨越历史 M3–M8，等待统一 Shader 方案和 D3D11/D3D12 近/中/远回归确认。
- 未实施证据体系问题：Validator 可在没有时间戳/输入 Hash/生产者溯源时接受旧产物；Python 比较脚本只报告、不设失败阈值。干净重编译另有 25 个 Editor 文件中的 114 条唯一 CS0618 warning。Android/物理移动设备、Android 构建、外存带宽和动画压力场景仍未验证。

## 2026-07-16 — M9 最终合成、变体与性能收口（历史证据，已由上方复核取代）

- 以 M8 `110/110` 为硬门，保持 M0–M8 角色 Shader、31 槽材质、模型、UV、692 骨骼和贴图不变；M9 只增加独立最终 Volume/Controller、审计与构建测量。
- 采用 Neutral、Post Exposure `-0.08`、Saturation `-18`、Contrast/Hue `0`；桌面 SMAA High、移动 FXAA。ACES 同机位 MAE `24.002/255` 且明显压缩黑红响应，故否定；没有运动向量/描边稳定性证据，TAA 明确延期。
- M9 Volume 精确只含 Tonemapping + ColorAdjustments，与 M8 Bloom 分离。M8→M9 前景饱和度 `0.2917→0.2526`，向未配准参考 `0.1782` 收敛；亮度 `0.4837→0.4820`，参考 `0.4598`；红裙保持，粉色错误像素 0。
- 实际构建并运行 Windows x64 Release Player：D3D12/RTX 4060 Laptop、768×1280、120 帧预热+240 帧采样；CPU mean/max `1.329/6.540 ms`、GPU `0.308/0.529 ms`、frame p95 `2.040 ms`，Standard Draw/SetPass/Triangles/Vertices 均值 `68/81/181,364/203,029`。
- Sandrone-only 保守变体审计：输入 33、移除 1 个无用 punctual-light ShadowCaster、保留 32。固定视角加法 Overdraw 的 mean/p95/max 为 `7.797/19/38` 层；不冒充硬件 early-Z/ROP 计数。
- 真实可见 D3D12 Game View/Frame Debugger 为 122 events：M5/M6/M4=`2/10/18`，M8 Eye/VFX/Sword=`1/1/1`，Outline 14、ShadowCaster 46、Receiver 1、Bloom 16、SMAA 3、Final Post 1；0 failure，退出 Play 后状态恢复。
- 最终 M9 `91/91`、warning 3、Shader message 0，M0–M8 全部硬门通过。外存带宽和物理移动设备仍明确未测；因 Pass/Stencil/透明状态和动作语料证据不足，拒绝材质合并与删骨。

## 2026-07-16 — M7 像素尺度彩色反面外扩描边

- 以 M6 `154/154` 为硬门，仅实现 M7；M6 源 Renderer 与材质不改，未加入 M8 HDR/Bloom 或 M9 功能。
- 标准 FBX 无 Outline Normal/顶点宽度数据。17,767 个同位置+骨索引组中源法线断裂 12,919 组；独立派生网格平均后为 0，原 FBX 和着色法线未覆盖。
- 仅槽 `0,1,12–18,20,22–24,26` 提交 0.72–1.30 px 彩色外壳；透明叠层和重叠内裙排除。Pass 为 `Cull Front / ZWrite Off / ZTest Less / Blend One,Zero`。
- 首版 31 子网格即使空索引仍产生 31 Draw；最终压缩为 14 个有效子网格/材质。真实 768×1680 D3D11 Frame Debugger 为 14 M7 + 2 M5 + 11 M6 + 18 M4 Forward，0 failure。
- 近/中/远线宽 `0.984/0.930/0.984 px`；关闭描边与 M6 MAE 为 0。同相机 M6→M7 MAE `0.4814/255`、变化覆盖 `1.403%`。
- 回归：M7 `131/131`、M6 `154/154`、M5 `94/94`、M0–M4 `30/30`、Shader message 0。GPU/SRP Batcher/目标机与正式 DCC 描边数据仍未测/缺失。

## 2026-07-16 — M6 头发 / 眼睛

- 仅实现标准模型 11 个眼/发目标槽；脸保持精确 M5，其他槽保持精确 M4。
- 新增槽 6 虹膜写入/槽 7–11 装饰层读取的 Stencil、有界 LDR 眼层、可动画 Eye AL 与保守的 Control-R 切线发高光；真实 A/B 已否定会裁掉虹膜的初始眼白模板方案。未实现 M7 Outline 或 M8 Emission/Bloom。
- 真实 768×1680 Play Mode Game View / Frame Debugger 通过：2 M5 + 11 M6 + 18 M4 Forward、46 ShadowCaster、1 receiver、0 failure；红裙保持 93,480 pixels。
- 独立可选 33 槽可动眼齿轮完成桌面 0.5–10 m 审计，不进入 Build Settings；建议 5 m 起退回标准眼层。
- 回归：M6 154/154、M5 94/94、M0–M4 30/30、Shader messages 0。目标设备性能仍未测；M7–M9 未触碰。

## 2026-07-15 — M5 长裙片 Face-only 最终修复

- 补充逐槽资产审计：解析 PMX 纹理索引，记录真实 Renderer 的 31 槽材质/Base/Control/Ramp/MatCap GUID、BaseMap 源哈希、ST、BaseColor 和 MPB，并与 Frame Debugger 31 条 Forward 的有效签名多重集核对。结果 0 failure，排除错误贴图/材质/ST/颜色/MPB 覆盖。
- M5 最终采用混合绑定：槽 0/1 使用 `Sandrone/M5/FaceSDF`；槽 2–30 直接引用对应 M4 Material 资产和 `Sandrone/M4/MaterialResponse`。裙片槽 20–27 不再经过 M5 Shader，也不存在克隆材质属性漂移。
- 加固 Bootstrap、Controller、Validator 和可见审计。Validator 在打开场景后重新加载依赖，强制 29 个非脸对象引用与 M4 完全相同，检查 Shader 内全部 CBUFFER，并在材质验证没有执行时直接失败。
- 最终 D3D11：M0–M4 综合 `30/30`；M5 Validator `93/93`；Shader compiler message `0`；Special Audit 与可见 Game View/Frame Debugger 均 0 failure。
- Frame Debugger：2 条 M5 face Forward + 29 条 M4 non-face Forward + 69 条角色 ShadowCaster + 1 条地面 receiver。同条件 M4→M5 的非脸/裙片 ROI MAE=`0/0`，新增近黑像素/连通块=`0/0`。
- 两个脸材质仍复用 M4 ShadowCaster，所以 CBUFFER 前缀修复不能单独撤销。M6–M9 未实现。

## 2026-07-14 — M0 Asset correctness / Unlit BaseMap

### 现状检查

- 用户锁定 Unity `6000.5.3f1`；工程初始不存在，当前 Phase 可唯一判定为 M0。
- 完整审阅 813 行技术参考、全部现有资产，并参考了原模型的图样；初始无代码、Unity 配置或历史开发记录。当前工程不保留或依赖具体图样文件。
- 标准 PMX：61,973 vertices、69,864 triangles、31 materials、692 bones、64 morphs（61 vertex + 2 material + 1 bone）。选择标准主体，不用眼齿轮可动变体替换基线。
- Blender 5.1.2 + MMD Tools 4.5.13 可用；初始压缩包检查时许可和可动眼齿轮提示来自 PMX 内部注释，项目后续补充取得的原作者说明现按原字节保存在 `ThirdParty/Sandrone/README_ORIGINAL_ZH.txt`。

### 设计与实现决策

- 最小 URP 17.5.0 manifest，只保留 `com.unity.render-pipelines.universal`。原模板写 17.0.1，但 6000.5.3f1 的内置包实际解析为 17.5.0，故以 `packages-lock.json` 的实值回写 manifest。随 Editor 分发的 cross-platform 模板含旧 Timeline/Version Control 等可选包，在 6000.5.3f1 上分别触发 `Object.GetInstanceID` 与非泛型 `TreeView` 的 CS0619 error；因此不把模板的全套可选依赖当兼容基线。
- M0 Shader 只采样 BaseMap；透明槽保留独立材质和队列，腮红/眼叠层用 `_LayerWeight` 替代 PMX material morph。
- `Clockwork Rotation` 用 `KeyB02_M` 局部四元数插值重建；PMX/Blender 四元数顺序和坐标转换已显式记录在 controller 中。
- 自动截图必须使用 D3D11；`-nographics` 下 URP 无渲染表面，会得到仅背景的伪截图。Validator 现检查图像亮度跨度，拒绝“文件存在但空白”。
- 首轮 controller 在字段初始化器创建 `MaterialPropertyBlock`，Unity 6000.5 明确禁止在 MonoBehaviour constructor 路径调用；已改为首次使用时惰性创建。
- 模型正向和 FBX 前向不同：截图按 Front Y=180°、Side Y=90°；保存场景维持模型根 identity，由相机从 +Z 看向原点。

### 自动测试实绩

- 最终命令退出码：0；`M0ValidationReport.json` failures=0、warnings=1。
- Unity 6000.5.3f1 / URP assigned / Linear：通过。
- 69,864 triangles、31 submeshes、61 blend shapes、692 renderer bones、31 material slots、1.6445 m：通过。
- Shader compiler messages=0，keyword pragmas=0，无 Lighting/ShadowCaster/Outline：通过。
- 31 BaseMap/sRGB 绑定、材质映射序号、源 SHA-256、控制骨骼/槽：通过。
- 警告：Unity 顶点缓冲 53,386 vs PMX 61,973；跨 FBX 顶点序/计数不稳定，但三角形、子网格和 BlendShape 不变量完整。

### 原模型图样参考

- 主验收图：正面、侧面；背景 RGB 分别参考约 `[40,39,39]` / `[35,34,34]`，M0 输出 `[39,38,38]`，背景已对齐。
- M0 前景平均亮度比参考高约 0.132（front）/0.176（side），饱和度约高 0.028/0.127；符合“仅 BaseMap、无阶段性光照/阴影/色调映射”的预期差异，但不是最终画质通过。
- 参考前景覆盖率较高，尤其侧视含更宽的发饰/裙摆与不同构图；M0 输出保留完整角色且未做裁切拟合。脸部方向、发色、黑白红金材质分区、眼层和服饰纹理对应正确。
- M0 不能评判最终明暗分界、阴影层级、材质高光、环境光、描边；这些差异保留给 M1–M7，禁止在本阶段虚构已匹配。

### 回退

生成资产均位于 `Assets/Sandrone` 和 `TestArtifacts/M0`；源 MMD 与 Blender 基线只读。按 `M0_ACCEPTANCE.md` 删除生成目录即可回退，不修改原 PMX/贴图。

## 2026-07-14 — M1 Main light baseline

### 现状与范围

- 用户确认继续 M1，视为 M0 人工验收授权；重新完整审阅技术文档、M0 代码/资产/配置/报告，并参考原模型的图样。
- 工程锁定 Unity `6000.5.3f1`、URP `17.5.0`、Linear；PC Renderer 为 Forward+，Mobile Renderer 为 Forward。
- 本阶段只实现主方向光、导入法线、视线与头部局部坐标调试；Ramp、实时阴影、ControlMap/MatCap、FaceMap、Stencil、描边、Emission/Bloom 均未提前实现。

### 设计与实现

- 新建 `Sandrone/M1/MainLightBaseline`，BaseLit 为 `BaseMap × MainLightColor × saturate(NdotL×0.5+0.5)`；使用有符号重映射保持 M1 可见性，硬二分留给 M2。
- `NdotL`、`NdotV`、`HeadAxis` 使用数值分支，不为每种调试视图制造 Shader keyword。诊断视图额外保留 `MainLightColor` 与 `MainLightDistanceAttenuation`。
- `SandroneM1Controller` 在 `頭` 骨骼局部空间校准角色语义 Forward/Right/Up，再随骨骼变换到世界空间；角色根旋转和头部局部旋转均有自动测试。
- M1 材质从 31 个 M0 材质复制贴图、颜色、透明状态到独立目录；M0 Shader、材质和场景不改写。

### 失败、证据与修正

- 首轮 Shader 把 `SV_IsFrontFace` 放在顶点输出，D3D11 报 `invalid vs_4_0 output semantic 'SV_IsFrontFace'`；按 HLSL 语义把它移到 Fragment 参数后编译通过。
- 第二轮报告曾显示 0 failure，但 BaseLit 截图实际为黑色剪影。原“图像有亮度跨度”检查只检测到背景差异，属于假阳性；已增加 BaseLit 色彩保留、主光颜色和 attenuation 诊断门，并明确不把该轮记为通过。
- 诊断证明 `_MainLightColor` 非零而 `mainLight.distanceAttenuation` 为零。检查 URP 17.5 本地源码与工程配置后确认：PC 使用 Forward+，该路径不填逐物体 `unity_LightData`；自定义 Shader 缺少 `_CLUSTER_LIGHT_LOOP` 变体时错误走普通 Forward 分支。补上 `#pragma multi_compile _ _CLUSTER_LIGHT_LOOP` 后 `GetMainLight()` 在 Forward+ 将方向光 attenuation 设为 1，同时保留 Mobile Forward 兼容。
- Validator 相应修正为仅允许这个 URP 必需关键字：一个 pragma、Forward/Forward+ 两个管线路径；M1 调试模式仍不新增变体。

### 自动测试实绩

- 最终 D3D11 批处理退出码 0；`M1ValidationReport.json` 为 61 checks、0 failures、1 warning。
- Shader compiler messages=0、Forward Pass=1、keyword pragma=1；M0 回归门通过。
- NdotL 正/背平均亮度 90.055/51.536；正背 MAE 38.873，正右 MAE 17.081，证明光向扫描有效。
- HeadAxis 平均通道跨度 22.708；三轴单位化、正交、右手性、根/头旋转跟踪与光方向符号均通过。
- 唯一警告继承自 M0：Unity 顶点缓冲 53,386 vs PMX 61,973；稳定拓扑不变量仍为 69,864 triangles、31 submeshes、61 blend shapes、692 bones。

### 原模型图样参考

- 开发时参考了原模型的图样，但当前工程不保留相应图片或量化依赖。相比 M0，M1 已引入连续主光，但没有 Ramp 暗部色、投射阴影、环境光、高光、描边与后处理，层次仍较平。
- 角色材质分区和 BaseMap 对应保持正确；正面构图覆盖率比参考低 0.123，侧面低 0.226，主要来自参考构图裁切、姿势/附属几何差异，不通过缩放或图像配准伪造一致。
- 面部当前仍使用普通法线响应；脸影形状、头发高光、金属响应与轮廓线不能在 M1 宣称匹配。

### 回退

删除 `Assets/Sandrone/Materials/M1`、M1 Shader/Controller/Bootstrap/Validator、`ToonCalibration_M1.unity`、`M1_ACCEPTANCE.md` 与 `TestArtifacts/M1` 即可回到完整 M0；重新把 Build Settings 场景指向 M0。M0 资产与源模型未被覆盖。

## 2026-07-14 — M2 Two-tone & Warm/Cool Ramp

### 现状与范围

- 用户授权继续 M2；重新完整审阅技术文档、M0/M1 代码/资产/配置/报告和开发记录，并参考原模型的图样，阶段可唯一判定为 M2。
- 工程固定 Unity `6000.5.3f1`、URP `17.5.0`、Linear；M1 回归报告为 61/61 通过。现有 `toon_defo.bmp` 仅 16×16 MMD toon 图，不符合五行 Ramp 契约。
- M2 只实现二分和 Ramp；ControlMap、MatCap、FaceMap、描边法线/宽度、EmissionMask 均确认缺失并留给规定阶段，不制作误导性占位资源。

### 设计与实现

- 新建 `Sandrone/M2/ToonRamp`：Half-Lambert 经材质族阈值和 `smoothstep` 形成 BandMask；边缘宽度为固定最小值 0.015 加 `fwidth` 导数项，兼顾静态可读性和缩放抗闪烁。
- 项目自绘 256×64 RGBA Ramp，5 行分别为 Skin/Face、Light Cloth、Dark Cloth/Hair、Metal、Eye；采样 `V=(row+0.5)/5`，导入为 Linear/Clamp/Bilinear/No Mip/Uncompressed。
- Ramp 是线性颜色倍率。首版亮端接近 1，实测正面几乎不比 M1 暗、侧面更亮；修正到亮端 0.82–0.93、暗端 0.50–0.76，避免 BaseMap 亮色和红裙在硬亮区过曝。Bootstrap 只在 PNG 缺失时生成，保护后续美术修改。
- 31 个 M2 材质从 M1 复制 BaseMap 与 Queue/Blend/ZWrite/Cull，显式绑定同一 Ramp、整数行和阈值。新场景、Profile、Controller、截图与 Validator 均独立，不覆盖 M0/M1。

### 失败、证据与修正

- 长路径隔离副本虽最终跑到 88/88，但 Windows 包缓存产生 `DirectoryNotFoundException`，该轮不作为最终证据。
- 主工程首次使用 `-nographics` 时 NullGfxDevice 无 RenderTexture surface，7 个图像项正确失败（MAE=0、中间像素=1、M2 亮度=0）。保留该事实并改用有图形设备的隐藏 batchmode；最终退出码 0。README/验收命令明确禁止把 `-nographics` 用于截图验证。
- 首版 Ramp 正面/侧面亮度 0.543/0.540，亮区过强；收敛倍率后主工程正面/侧面为 0.521/0.519。正面相对参考的亮度误差较 M1 缩小 0.026，侧面仍恶化 0.016，记录为 M3 阴影与后续调色之前的阶段差异。
- 最终复验发现 Unity 6 的材质 `[Enum]` Drawer 无法构造 8 项调试枚举，虽不影响 Shader 编译仍会产生 Console Warning；调试值本来由 Controller 写入，故改为 `[HideInInspector]`，复验日志已无该警告。

### 自动测试实绩

- 最终主工程 batchmode 退出码 0；`M2ValidationReport.json` 为 88 checks、0 failures、2 warnings。
- Shader compiler messages=0、Forward Pass=1、keyword pragma=1、texture samples=2；M1 回归门与 31 个 BaseMap/表面状态均通过。
- BandMask 正/背光 masked MAE=252.037，FinalToon 正/背 MAE=21.865；默认/近/远中间边界像素比为 0.012143/0.021175/0.037435，证明光向响应与导数 AA 有效。
- Ramp 5 行可见、通道色差 6.806；M2/M1 正面亮度比 0.953，未重复压黑 BaseMap。
- 警告：继承的 Unity 53,386 vs PMX 61,973 顶点缓冲差异；31 材质 Forward draw 需人工 Frame Debugger/Profiler 测量，未声称性能通过。

### 阶段对照与回退

- 正面 M1/M2 亮度为 0.547/0.521，侧面为 0.503/0.519；M2 的红、蓝区域仍较鲜明。原模型图样只作为开发时的风格参考，不作为当前可执行依赖。
- BaseMap 分区、肤/发/黑白红金/眼部对应保持正确；脸影、发高光、金属高光、环境层次、描边和最终调色均未越阶段实现。
- 删除 M2 材质、Ramp/Profile、M2 代码/场景/文档与 `TestArtifacts/M2`，并把 Build Settings 指回 M1，即可完整回退；M0/M1 与源 Blender/PMX 未改写。

## 2026-07-14 — M3 Real shadows

### 现状与范围

- 用户授权继续 M3，视为 M2 阶段门授权；完整复审技术文档、M0–M2 代码/资产/配置/报告和开发记录，并参考原模型的图样。
- 工程仍为 Unity `6000.5.3f1`、URP `17.5.0`、Linear；PC Forward+ / 4 cascades / 2048 / soft，Mobile Forward / 1 cascade / 1024 / hard。M2 回归门 88/88。
- 本阶段只实现实时投射/接收阴影及与 M2 明暗分区的合并；ControlMap、MatCap、Face SDF、Stencil、Outline、Emission/Bloom 均未越阶实施。

### 设计与实现

- `Sandrone/M3/ToonShadow` 为 2 Pass：`UniversalForward` 通过 `GetShadowCoord`/`GetMainLight(shadowCoord)` 读取主光阴影，`ShadowCaster` 通过 `ApplyShadowBias`/`ApplyShadowClamping` 写入深度。
- Raw attenuation 经 low/high/strength 映射为 Styled Cast，再与 M2 Form Band 取 `min`；避免 PCF 软边与硬 Ramp 直接相乘导致双边。
- Opaque/Cutout 前向和阴影 Pass 共享 BaseMap/BaseColor/LayerWeight/Cutoff；8 个 alpha-blended overlay 显式不投射二值影子，23 个 opaque 正常投射。
- 增加版本化 ShadowProfile、MPB Controller、隔离 receiver/blocker/alpha probe、16 张正式截图、独立 M3 场景与 80 项 Validator；M0–M2 均不被覆盖。

### 失败、证据与修正

- 首轮为 77/79：`SoftShadowIntermediatePixels=0.446319` 与 Alpha IoU=0.3210。未宣布通过。
- 直方图证明阴影强度 0.85 下的线性 0.15 在 sRGB PNG 约为 108；旧指标把整片稳定阴影平台都当成“软边”。修正为稳定遮挡/受光/过渡三项，最终 0.2087/0.5537/0.2377。
- Alpha 测试首先降低探针高度、倾斜光向和翻转 Quad 法线，IoU 仍只有 0.315–0.326；因此否定“纯 Bias/UV/法线错误”的初步推测。
- 关闭 AlphaClip 后的不透明方形仍出现同样的三角形/多边形空洞，证明根因是顶视正交平面在 4-cascade per-cascade caster culling 中被分割，不是 alpha sampler。仅在 Alpha 契约截图期间临时切单级联，`finally` 恢复 4；最终 IoU=0.9692。角色自影、外物接收和地面投影仍在生产 4 级联下验证。

### 自动测试实绩

- 最终 batchmode 退出码 0；`M3ValidationReport.json` 为 80/80、0 failures、2 warnings。Shader compiler messages=0、Pass=2、keyword pragma=4、source samples=3。
- 外部 blocker 接收 masked MAE=61.095；角色地面投影 MAE=0.924，coverage=2.637%；Raw→Styled MAE=7.351，Form→Final Lit MAE=27.577。
- Alpha 可见/投射 IoU=0.9692；M3/M2 验证器前景亮度比=0.9632；M2 回归门通过。
- 两项 warning：M0 继承的 Unity/PMX 顶点缓冲差异；31 Forward + 31 ShadowCaster 的实际 draw/GPU 成本需人工 Frame Debugger/Profiler，未宣布性能通过。

### 图样参考、风险与回退

- 开发时参考了原模型图样中的脸影、发/金属高光、环境暗部和描边，但这些特征不属于 M3。当前只保留阶段内截图与指标，不保留外部图样文件或其量化依赖。
- BaseMap 黑/白/红/金、肤/发/眼分区保持；姿态和构图差异不用图像配准掩盖。
- 删除 M3 材质、Shader/Runtime/Editor/Profile/场景/探针/文档与 `TestArtifacts/M3`，Build Settings 指回 M2，即可回退；M0–M2 和源 Blender/PMX 未改写。

## 2026-07-14 — M4 ControlMap / material response / MatCap

### 范围与数据契约

- M3 回归门 80/80。工程仍为 Unity `6000.5.3f1`、URP `17.5.0`、Linear；本阶段未实现 Face SDF、发丝各向异性、Stencil、描边、Emission/Bloom。
- 未发现可信 ILM/ControlMap、FaceMap、MatCap 或 Outline Normal。新增三张 2048² 工程自有 ControlMap 种子：R=由 BaseMap 明度推断的高光权重，G=1 中性 AO，B=暖金候选，A=0 保留；明确标记为合理推断而非游戏原通道结论。
- 共享 UV 无法无冲突编码 31 个材质 ID，因此 M4 的 Matte/Skin/Silk/Metal 类型由版本化 Profile/材质常量提供；这修正了“无条件把材质 ID 填入 Control A”的文档假设。

### 实现与失败证据

- 新增 2-Pass `Sandrone/M4/MaterialResponse`，保留 M3 Forward/ShadowCaster 契约；Forward 为 Base+Ramp+Control+MatCap 四次采样，Caster 一次 Base alpha。高光乘 Styled Cast，避免投射阴影内泄漏。
- Skin 使用弱宽高光、Silk 使用宽峰+Fresnel、Metal 使用 Control B/slot fallback、NdotH 分级和 view-space normal MatCap。MatCap 为 512² sRGB/Clamp/NoMip 项目自绘种子。
- 首轮报告 72/75：MPB Debug/A-B 截图完全同哈希。随后验证材质属性读回正确但 `Camera.Render` 未消费 31-submesh edit-mode 动态状态；尝试 renderer/per-material MPB、材质实例、独立 float CBUFFER、local/global keyword，均保留失败日志，未把 0 MAE 改写为通过。
- 当前用 multi-compile global keyword 保留调试接口，并增加验证专用 `ValidationIsolation` pass。最终着色 AllOn/Off 仍为 0 MAE；isolation On/Off 的槽 24/28/29 MAE 分别为 3.696/13.572/5.105，只证明独立寻址，不宣称最终贡献通过。

### 自动结果、对比与风险

- 最终 batchmode 退出码 0；`M4ValidationReport.json` 为 81/81、0 failures、5 warnings；Shader messages=0、Pass=2、pragma=10、source samples=5。Debug 截图语义未被自动指标证明，保持人工待验收。
- MatCap 视角变化 MAE=20.756；M4/M3 validator 前景亮度比=1.0207。无配准参考统计中，M4 相对 M3 正面/侧面亮度仅 +0.0081/+0.00085，但仍比参考亮 +0.0968/+0.1268。
- 风险：31 槽、Forward+ShadowCaster；ControlMap 为启发式种子；Debug/A-B global keyword 组合最多 96 个 Forward 片元状态；同场景多个角色不能使用不同全局调试状态；最终着色槽 A/B 必须人工 Frame Debugger 验收。
- 回退：删除 M4 材质、Control/MatCap、M4 Shader/Runtime/Editor/Profile/场景/文档与 `TestArtifacts/M4`，Build Settings 指回 M3；M0–M3 与源 FBX/PMX 未覆盖。
# 2026-07-15 — M5 strict regression and repair

- Reproduced a real M5 failure after fresh compilation: non-face MAE 1.561 and skirt-slot MAE up to 13.670.
- Restored the exact M4 `UnityPerMaterial` prefix required by the reused ShadowCaster; appended M5 fields only.
- Replaced hard Face SDF UV sign switching with dual-sample continuous blending around `dot(Lh,HeadRight)=0`.
- Added 31-slot, black-region, 20-degree actual-light, 17-debug-semantic, visible Game View, lifecycle and Frame Debugger validators.
- Final evidence: M0–M4 combined 30/30; M5 80/80; non-face/skirt MAE 0; Game View/Frame Debugger failures 0; 31 Forward + 69 ShadowCaster + one formal receiver identified.
- A stale Mobile capture initially exposed a validator false positive. The visible audit now clears old evidence, warms the Mobile pipeline before capture, rejects cyan/debug frames and PC/Mobile 0-degree MAE above 5, requires a non-zero Mobile light response, and exits non-zero on report failure. Fresh original-project evidence is PC/Mobile full-frame MAE 1.220.
- The FaceMap remains a project seed. GPU time/SRP Batcher and physical mobile-device profiling remain open measurements; M6–M9 were not implemented.
# 2026-07-16 — M8 VFX / Bloom

- Implemented only EyeLight and crystalline-sword HDR emission, using explicit Linear masks and a Bloom-only URP Volume. Display and M9 remain deferred.
- Preserved 30/31 M7 character material objects, the M7 outline renderer, Face SDF and eye stencil; only slot 10 uses the M8 eye shader.
- Fixed persisted Bloom sub-asset creation and stencil-aware eye isolation. Same-HDR all-off/control MAE is 0; automated M8 is 110/110 after final revalidation with all earlier gates clean.
- Real D3D12 Game View/Frame Debugger: 119 events, Eye/VFX/M0 sword/Bloom = 1/1/1/16, failures 0; PC/Mobile MAE 1.350 and red skirt 59,651 px.
- Masks are project seeds and GPU/SRP Batcher/built-player/physical-mobile profiling remains open. No M9 feature was added.

# 2026-07-16 — Windows PC cascade/evidence/lifecycle专项修复

- 修复 M3/M4/M5/M6/M8 五个 Forward Shader 的 cascade 坐标契约：仅允许 URP 指定变体插值顶点坐标，PC 四级级联在片元按当前 `positionWS` 选择。旧 M3 Validator 曾把错误调用写成通过条件，现改为五 Shader 统一契约检查。
- D3D11/D3D12 分别完成 10 张 split 前后、双视角和侧/背光真实渲染，5 个 Shader 均为 0 compiler message / 0 failure；修复前后近/中/远 Raw Shadow 变化像素为 104/272/450。
- 新增 evidence session：跨阶段报告必须属于本轮生成时间和 session；最终记录正式输入/产物 SHA-256、长度、mtime、Unity/API/设备。旧时间戳、篡改报告和源码指纹漂移均须在负向测试中被拒绝。
- M4–M8 增加状态脏检查；M7–M9 增加单一所有者状态的禁用/销毁恢复。参数刷新、无材质实例化、多实例隔离和 M8→M9 场景切换共 18/18。
- M0–M6 的重叠 MPB 字段独立卸载需要统一协调器，未使用会擦除其他阶段数据的 `MaterialPropertyBlock.Clear`。该项按架构问题汇总等待确认。
- 同一新鲜会话内：M0–M9、M0–M4 30/30、D3D11/D3D12 可见 Frame Debugger、Windows Player 全部零失败。正式目标平台明确为 Windows PC；Android/移动真机不在范围。

# 2026-07-18 — GitHub 资产边界与外部图样依赖清理

- 删除根目录外部图样及其不可复验的派生对比表、指标 JSON 和两组本地分析目录；M0–M9 阶段渲染截图、Player、Frame Debugger 与其他证据不受影响。
- M9 Validator 不再读取仓库外部图片，改为比较锁定机位的项目内 M8 基线与 M9 最终帧，并检查饱和度降幅和亮度变化均处于有界范围。
- 补充取得原作者说明，按原字节保存为 `ThirdParty/Sandrone/README_ORIGINAL_ZH.txt`，SHA-256 为 `78b25e862eb859b14db9cc20b14c09972c0a9f02c541e9a9f6dc18793b717b60`；项目说明只做忠实归纳，不替代或扩大原授权。
- Unity 普通批处理编译退出码 0，C#/Shader error 为 0。无活动 Evidence Session 的 M9 Validator 按设计拒绝旧会话证据，但新 `SaturationReductionBounded` 与 `LuminanceChangeBounded` 门禁分别以 `0.0452` 和 `0.0006` 通过；本次未将该失败表述为全链回归通过。
- Git 暂存区只包含项目源码、配置、文档、原始使用说明和 README 使用的八张渲染图；第三方模型、转换资产、Player 和完整测试产物继续保留在本机并被忽略。
