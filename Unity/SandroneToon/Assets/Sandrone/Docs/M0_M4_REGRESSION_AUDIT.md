# 桑多涅 M0–M4 回归审计（2026-07-15）

## 1. 审计结论

- 环境：Unity `6000.5.3f1`、URP `17.5.0`、Linear、Direct3D 11、RTX 4060 Laptop GPU；自动截图未使用 `-nographics`。
- 从当前源码重新执行 `M0 -> M1 -> M2 -> M3 -> M4 -> 综合审计`：M0 `67/67`、M1 `61/61`、M2 `88/88`、M3 `88/88`、M4 `94/94`、综合 `30/30`。
- PC Forward+ 与 Mobile Forward 均真实渲染；同帧 8-bit RGB MAE `0.374`，差异来自阴影分辨率/级联/软阴影策略；两者无粉色、黑色整片或空画面。
- 31 槽、Base/Ramp/Control/MatCap、Opaque/Cutout/Transparent 状态及 ShadowCaster 合约通过。透明槽显式禁用 ShadowCaster Pass。
- Frame Debugger 的实际事件序号与 Render Target 仍需 GUI 人工确认；自动结果只按 Renderer/slot/material/pass 锁定，不把旧的 24/28/29 序号当稳定接口。
- 2026-07-15 补充：地面矩形阴影已在真实 768×1680 Play Mode Game View 中复现，并由 Frame Debugger 当前事件 81 锁定到 `M4_ShadowGround/M3ShadowReceiver`。Receiver 已从顶点级 atlas 坐标插值改为逐像素 cascade selection；详见 `GAME_VIEW_SHADOW_FIX.md`。

## 2. 阴影异常：复现、根因、修复

| 问题 | 复现证据 | 根因 | 修复 | 结果 |
|---|---|---|---|---|
| 角色自阴影条纹/锯齿 | `M3_CastShadowRaw_Self_before.png` | Forward 与 ShadowCaster 共用 `Cull Off`，薄片正反面同时写阴影图；PC bias 低时放大深度竞争 | Forward 保留 `_Cull`；ShadowCaster 独立 `_ShadowCull=Back`。单面 Cutout 卡片按资源显式设 Off | 内部 Laplacian 高频比 `0.067677 -> 0.059666`；新图无原先成片条纹，仍需 Game View 人眼确认细小 PCF 抖动 |
| Alpha Clip 无投影 | 4 cascades 首次 IoU `0.0000` | 单面 Quad 被 Back Cull | Cutout 卡片 `_ShadowCull=Off`，角色实体仍 Back | 投影恢复 |
| 矩形/对角截断 | 恢复双面后 IoU `0.3224` | 旧测试把水平 caster/receiver 放入不同 cascade culling tile；临时改 1 cascade 只是在隐藏问题 | 移除 1-cascade 特例；验证布局改为正面相机、竖直 caster/receiver、相近相机深度，全程生产 4 cascades | Forward/Shadow Alpha IoU `0.9890` |

近/中/远分别在 `z=0/-12/-32`、相机 far=50 下生成 RawShadow 与 CascadeIndex 图。最新完整重建的地面投影 MAE `0.882`、覆盖率 `0.02518`；外部 blocker 角色区 MAE `63.259`。Mobile 1-cascade/硬阴影由综合审计单独真实渲染。

## 3. 全部 Debug Mode 对照表

M0 没有枚举式 Debug Mode；EyeAL(槽 9)、Blush(槽 30) 与 clockwork bone 是运行时控制，不属于 Shader Debug 输出。

| Phase/编号 | 名称 | 数据来源 | 理论输出 | 有效范围 | 验证 |
|---|---|---|---|---|---|
| M1/0 | BaseLit | BaseMap、主光、NdotL | 基础受光色 | 全材质 | 当前重建、唯一 Hash |
| M1/1 | NdotL | world normal/main light | signed NdotL 映射 0..1 | 可见表面 | 灯前/右/后旋转 MAE>1 |
| M1/2 | NdotV | world normal/view | 0..1 灰度 | 可见表面 | 相机响应、唯一 Hash |
| M1/3 | HeadAxis | 法线投影至头骨 R/U/F | RGB 轴向色 | 头骨绑定有效时 | 轴正交检查、唯一 Hash |
| M1/4 | MainLightColor | URP MainLight.color | RGB 光色 | 主光有效时 | 白光输出、唯一 Hash |
| M1/5 | DistanceAttenuation | MainLight.distanceAttenuation | `(A,0.5A,0)` 热图，R 精确保存 A | 方向光恒 1；点光可变 | 与模式 4 不再同图、唯一 Hash |
| M2/0 | FinalToon | Base、band、Ramp | 最终 M2 色 | 全材质 | 当前重建、唯一 Hash |
| M2/1 | HalfLambert | NdotL | 0..1 灰度 | 可见表面 | 唯一 Hash |
| M2/2 | BandMask | HalfLambert/threshold/AA | 明暗二带 | 可见表面 | 前/后灯 MAE>1 |
| M2/3 | RampUV | band、RampRow | UV 可视化 | Ramp 有效时 | 唯一 Hash |
| M2/4 | RampSample | RampMap | 实际采样色 | Ramp 有效时 | 贴图绑定+唯一 Hash |
| M2/5 | NdotV | normal/view | 0..1 灰度 | 可见表面 | 唯一 Hash |
| M2/6 | HeadAxis | 头骨轴 | RGB 轴向色 | 头骨绑定有效时 | 唯一 Hash |
| M2/7 | Silhouette | Alpha/几何 | 白色轮廓 | 可见像素 | 近/远轮廓截图 |
| M3/0 | FinalToon | M2+实时阴影 | 最终 M3 色 | 收/投影有效时 | 当前重建、唯一 Hash |
| M3/1 | CastShadowRaw | URP shadowAttenuation | 原始阴影灰度 | 主光阴影范围 | blocker、近中远 |
| M3/2 | CastShadowStyled | raw+low/high/strength | 风格化阴影灰度 | profile 有效时 | Raw/Styled MAE `6.613` |
| M3/3 | FormBand | NdotL band | 形体明暗带 | 可见表面 | 唯一 Hash |
| M3/4 | FinalLitMask | min(form, cast) | 最终光照 mask | 可见表面 | Form/Final MAE `13.200` |
| M3/5 | RampSample | RampMap/final mask | 实际 Ramp 色 | Ramp 有效时 | 唯一 Hash |
| M3/6 | CascadeIndex | ComputeCascadeIndex | 级联编号灰度 | PC 4 cascades；Mobile 为 0 | 近中远图、唯一 Hash |
| M3/7 | Silhouette | Alpha/几何 | 白色轮廓 | 可见像素 | 唯一 Hash |
| M4/0 | FinalToon | M3+Control/response/MatCap | 最终 M4 色 | 全材质 | 当前重建、唯一 Hash |
| M4/1 | ControlR | Control.R | 红色强度图 | Control 有效时 | 通道绑定、唯一 Hash |
| M4/2 | ControlG | Control.G | 绿色强度图 | 当前 G=1 中性 AO | 唯一 Hash；未伪造 AO |
| M4/3 | ControlB | Control.B | 蓝色金属 mask | Control 有效时 | 唯一 Hash |
| M4/4 | ControlA | Control.A | 黄色保留通道 | 当前 A=0 | 黑底、唯一 Hash |
| M4/5 | NDotH | normal/light/view | 0..1 灰度 | 可见表面 | 灯/相机响应、唯一 Hash |
| M4/6 | Specular | response/control/cast | 高光贡献灰度 | Skin/Silk/Metal | 唯一 Hash |
| M4/7 | MatCapUV | view normal | RG=UV | 可见表面 | 相机旋转响应 |
| M4/8 | MatCapSample | MatCap/metal mask | MatCap 贡献 | 金属族 | 正面/3/4 MAE `1.722` |
| M4/9 | MaterialResponse | Profile response type | Matte/Skin/Silk/Metal 分类色 | 31 槽 | 唯一 Hash |
| M4/10 | FinalLitMask | M3 final mask | 0..1 灰度 | 可见表面 | 唯一 Hash |
| M4/11 | Silhouette | Alpha/几何 | 白色轮廓 | 可见像素 | 唯一 Hash |

综合报告验证每阶段 Hash 唯一。额外响应探针把头骨旋转 25° 后的 M1 HeadAxis 与旋转前比较，MAE=`3.638`；临时将槽 24 ControlMap 替换为 RGBA=`(.2,.4,.6,.8)`，R/G/B/A 模式平均色分别为 `(124,0,0)`、`(0,170,0)`、`(0,0,203)`、`(231,231,0)`，四图 Hash 全异，探针结束后恢复原材质。M3/M4 改为逐材质槽 MPB，保留 M0 槽 9/30 数据；M4 不再使用全局 Keyword，也不再修改共享材质。保存/重载稳定场景后 MPB 读回断言为 1；切换 Debug 后最终模式可恢复，无全局 `M4_*` Keyword 残留。

## 4. Frame Debugger 24/28/29 映射与 A/B

| 旧序号/稳定身份 | Renderer / Submesh | Material / Pass | Queue / Blend / ZWrite / Cull | 数据绑定与响应 | 最终着色 A/B |
|---|---|---|---|---|---|
| 24 / 饰 | `桑多涅_mesh` / 24 | `M4_24_M24_MetalOrnament` / `M4MaterialResponse` | 2000 / One,Zero / On / Off；ShadowCull Back | T_Skirt、SkirtControl、MetalMatCap；Response Metal、Feature Metal、Spec .78、MatCap .46、fallback 1 | target `2.348`，non-target `0.000` |
| 28 / 袜+ | `桑多涅_mesh` / 28 | `M4_28_M28_StockingOverlay` / `M4MaterialResponse` | 3028 / SrcAlpha,OneMinusSrcAlpha / Off / Off；ShadowCaster disabled | T_Overlay、NeutralControl、MetalMatCap；Response Silk、Feature Stocking、Spec .32 | target `1.427`，non-target `0.000` |
| 29 / 髮+ | `桑多涅_mesh` / 29 | `M4_29_M29_HairHighlightOverlay` / `M4MaterialResponse` | 3029 / SrcAlpha,OneMinusSrcAlpha / Off / Off；ShadowCaster disabled | T_Overlay、NeutralControl、MetalMatCap；Response Matte、Feature Hair、OverlayBoost 1.65 | target `1.217`，non-target `0.000` |

空间 mask 由 M4 Silhouette Debug 采样真实 BaseMap Alpha；Isolation Shader 只确认槽位几何，不再作为功能通过证据。A/B 与放大 4 倍差异图位于 `TestArtifacts/M4/AB`。实际 Render Target、Depth attachment、运行时 pipeline keyword 与事件序号必须在同帧 Frame Debugger GUI 读取，当前标记为**待人工验证**。

## 5. 修改文件与原因

- `SandroneToonShadowM3.shader`、`SandroneMaterialResponseM4.shader`：ShadowCull 分离；M4 uniform Debug/局部 feature weight、OverlayBoost、条件 MatCap 采样。
- `SandroneM3Controller.cs`、`SandroneM4Controller.cs`：逐槽 MPB；移除 M4 全局 Keyword/共享材质突变；增加槽级 feature override。
- `SandroneM3Bootstrap.cs`：显式复制/持久化材质属性；4-cascade Alpha 布局；近中远截图；透明 ShadowCaster 禁用。
- `SandroneM4Bootstrap.cs`：修复 Control/MatCap/Profile 序列化；保存重载后截图；Alpha-aware mask 与槽 24/28/29 最终 A/B。
- `SandroneM4Validator.cs`：精确资源引用、目标/非目标 MAE、差异图、全局状态检查。
- `SandroneM0M4RegressionAudit.cs`：全链重建、Debug Hash、头骨旋转与合成 ControlMap 响应探针、PC/Mobile 实机渲染、表面状态审计。
- `SandroneMainLightM1.shader`：DistanceAttenuation 使用可区分热图，R 通道仍保存原值。

## 6. 自动命令与证据

```powershell
$UnityEditor = '<Unity 6000.5.3f1 Editor executable>'
& $UnityEditor -batchmode -quit -force-d3d11 `
  -projectPath <project> `
  -executeMethod SandroneToon.Editor.SandroneM0M4RegressionAudit.RunFullRegression `
  -logFile <project>\M0M4_FullRegression_D3D11.log
```

关键证据：`TestArtifacts/Audit/M0M4RegressionAudit.json`、`TestArtifacts/M3/M3ValidationReport.json`、`TestArtifacts/M4/M4ValidationReport.json`、`TestArtifacts/Audit/Pipeline`、`TestArtifacts/Audit/DebugResponse`、`TestArtifacts/M4/AB`。最新完整重建日志为 `Final_FullRegression_ResponseProbes_D3D11.log`；命令返回 0，Shader compiler message 为 0。Unity 启动阶段仍记录一次 `com.unity.collections` 包缓存 DLL 缺失及 Probe Volume 内置资源日志，但随后程序集成功重载、各 Phase 验证及截图均完成；这两项归类为编辑器/PackageCache 警告，不等同项目编译错误。

## 7. Unity 人工验收

1. 打开 `ToonCalibration_M4.unity`，Game View 固定 768×1280，关闭动态分辨率，暂停同一帧。
2. Frame Debugger 启用后，不按序号搜索；依次锁定 Renderer=`桑多涅_mesh`、Submesh=24/28/29、Pass=`M4MaterialResponse`。记录事件序号、Color/Depth RT、pipeline keyword、MPB `_M4DebugMode/_M4FeatureWeight`。
3. 分别禁用事件或步进到事件前一项截图；用对应 Alpha-aware mask 计算：目标 MAE>1、非目标 MAE<0.5。自动 A/B 已达标；GUI 事件禁用仍待确认。
4. 在 M3 场景检查自阴影、地面、Alpha Clip；相机近/中/远移动时不得出现矩形切断或成片平行条纹。
5. 切换全部 Debug Mode；旋转灯、相机、头骨，确认表中响应。回到 Final 后材质 Inspector 数值不得改变，全局 keyword 不得出现 `M4_*`。
6. PC 选 Forward+，Mobile 选 Forward；两者不得粉色/黑片/透明排序整片错误。透明袜/髮片边界不得形成双重深度遮挡。

## 8. 性能与兼容性

- M4 keyword 理论组合由旧全局 Debug/A-B 设计约 `3848` 降至 `42`；没有 Debug/Feature 全局状态污染。
- 最坏基础提交仍约 31 Forward draws；透明 ShadowCaster 禁用后最多约 28 character shadow draws，另有地面/URP renderer features。
- MatCap 源码采样存在但只在强度非零或 MatCap Debug 时执行。PC/Mobile 均实渲染通过。
- 逐槽 MPB 会使该 Renderer 不能获得完整 SRP Batcher 收益；单角色 31 独立材质下接受此换取无共享状态污染。多角色扩展应改为每角色 material instance 或 GPU character buffer，并重新 profile。
- 768×1280 可见覆盖：槽 24 `2.359%`、槽 28 `5.441%`、槽 29 `0.301%`。袜片是主要透明 overdraw 风险。
- GPU 时间、实际 SetPass/Draw Call 与透明 overdraw 热图无法由可靠的 batch `Camera.Render` 得出，标记**待人工 Profiler/Frame Debugger 验证**；不得写成已通过。

## 9. 已知问题、风险、回退

- ControlMap 是 BaseMap 推断的制作种子，G 保持 1、A 保持 0，不等同游戏 ILM。回退：将材质绑定改回 NeutralControl，Profile 保留。
- Hair OverlayBoost=1.65 是为现有低 Alpha/近同色贴图建立可观察局部贡献；回退为 Profile 中改回 1.0，不影响槽映射。
- ShadowCull Back 可能漏掉刻意反面的薄片；此类资产应逐材质设 Off，不能恢复全局 Cull Off。回退字段为 `_ShadowCull`。
- MPB 影响 SRP Batcher；若多角色成本过高，保持接口不变，替换后端数据传输。
- Frame Debugger GUI 事件与 GPU 时间待人工；事件号可能因 SSAO、Forward/Forward+、Unity 小版本变化。

## 10. 原模型图样参考与文档修正

正面/侧面材质颜色对应、红黑白金主色与饰品槽映射正确；开发时参考的原模型图样具有更明显的柔和环境渐变、面部专用阴影、头发高光和描边。M0–M4 尚未实现 M5 Face SDF、M6 头发/眼睛专用响应、M7 描边，因此这些差异是阶段边界，不提前实现；当前工程不保留具体图样依赖。

修正文档结论：禁止用“Shader 默认纹理非空”证明 Control/MatCap 已绑定；必须比较精确 Asset 引用并序列化重载。禁止切为单 cascade 证明 Alpha Clip。Isolation 只能证明槽位，最终功能必须用 Alpha-aware mask 的真实着色 A/B。下一阶段仅建议 M5 Face SDF，不在本次实现。
