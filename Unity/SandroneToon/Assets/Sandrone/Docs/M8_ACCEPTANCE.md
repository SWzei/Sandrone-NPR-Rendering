# M8 VFX / Bloom 阶段验收

> 2026-07-16 专项修订：M8 Eye Forward 的 PC cascade 坐标已与 URP 17.5 对齐。M8 Controller 增加参数脏检查、Volume/Crystal 状态快照恢复及自有 MPB 字段复位；多实例隔离与启用/禁用/销毁路径纳入 18/18 生命周期审计。

## 1. Phase 目标与完成状态

M8 已闭环完成，三候选中选择 `EyeLight + CrystallineSword`：角色槽 10 增加受遮罩 HDR 眼部发光；`大剑.pmx/Mat_Cyrstal` 作为独立校准道具增加遮罩、Fresnel、HDR 发光；两者进入专用 URP Bloom。显示屏为第三候选，明确延后。这里的阶段边界是“M8 当时未实现 M9”；当前工程已在后续独立阶段实现 M9。

## 2. 现状检查及设计依据

- Unity `6000.5.3f1`、URP `17.5.0`、Linear；入口硬门为 M7 `131/131`、M6 `154/154`、M5 `94/94`、M0–M4 `30/30`。
- `目光.png` 为 512² RGBA，可从 Alpha 得到局部眼光遮罩；标准资产没有独立 EmissionMask。
- `大剑.pmx` 为 7,473 顶点、6,492 三角形、2 材质；`Mat_Cyrstal` 独立占 1,576 三角形，适合作为晶体提交边界。
- `武器1.png` 含青色晶体绘制，但没有作者声明的发光语义；当前晶体遮罩只是可替换项目种子，不冒充原始通道。
- M7 标定相机关闭 HDR。M8 为真实 Bloom 启用 HDR；旧 M7 LDR PNG 与 M8 HDR PNG 的 MAE `3.7922` 是渲染配置差异，材质回归改在同一 M8 HDR/相机/灯光配置下比较，结果 MAE `0`。

## 3. 实现方法与关键技术原理

- 角色仅替换槽 10；其他 30 个角色材质保持 M7 精确对象引用，独立 M7 描边 Renderer 不变。
- 眼部 Shader 保留完整 M6/M4 `UnityPerMaterial` 前缀并只追加 M8 字段；Stencil 继续由槽 6 写入、槽 10 `Equal` 读取。Emission 在有界 LDR Base 之后相加，不被 `saturate` 截断。
- 晶体使用独立 FBX 的第二子网格，BaseMap + Linear EmissionMask + Fresnel 形成 HDR；第一子网格仍用 M0 BaseMap Shader。
- 专用全局 VolumeProfile 只含 Bloom：Threshold `1.1`、Intensity `0.35`、Scatter `0.55`、Clamp `8`、Gaussian、Half、6 iterations、无 Dirt。相机 HDR/Post 开启，无 Tonemapping、Color Grading 或 AA。
- Runtime 逐槽 MPB 只覆盖 `_M8EmissionWeight/_M8DebugMode/_M8BloomThreshold`；不覆盖 BaseMap、EmissionMask、ST 或 BaseColor。Bloom 开关只改变 Volume weight。

## 4. 修改文件及逐项修改原因

- `Runtime/SandroneM8VfxBloomProfile.cs`：版本化两模块、HDR 和 Bloom 参数契约。
- `Runtime/SandroneM8VfxBloomController.cs`：眼部/晶体/可见性/Bloom/Debug 开关与逐槽 MPB。
- `Shaders/SandroneHairEyeEmissionM8.shader`：保留 M6 眼层/Stencil 基线并追加局部 HDR Emission。
- `Shaders/SandroneVfxEmissionM8.shader`：晶体 Base、Mask、Fresnel、HDR 与 Debug 输出。
- `Editor/SandroneM8Bootstrap.cs`：M7 硬门、资源生成/导入、材质/场景/Volume、A/B/HDR 证据。
- `Editor/SandroneM8Validator.cs`：结构、CBUFFER、资源、绑定、MPB、A/B、HDR、PC/Mobile 与 M0–M7 回归。
- `Editor/SandroneM8GameViewAudit.cs`：真实 Play Mode Game View 与 Frame Debugger 逐事件审计。
- `Scripts/Blender/import_m8_crystal.py`：可复现 PMX→Blend→FBX 和结构/Hash 报告。
- `Scripts/Analysis/compare_m8.py`：可选离线分析与同相机 M8 A/B 对比；外部图样输入不属于默认工程依赖。
- 生成资产：M8 三材质、三纹理、Profile、Bloom Volume、晶体 FBX、M8 场景和证据。

## 5. 当前目录结构

```text
Blender/VFX/Sandrone_CrystallineSword_M8.blend
Scripts/Blender/import_m8_crystal.py
Scripts/Analysis/compare_m8.py
Unity/SandroneToon/Assets/Sandrone/
  Configs/*_M8.asset
  Materials/M8/
  Models/Optional/Sandrone_CrystallineSword_M8.fbx
  Textures/M8/
  Runtime/SandroneM8*.cs
  Shaders/*M8.shader
  Editor/SandroneM8*.cs
  Tests/Scenes/ToonCalibration_M8.unity
  Docs/M8_ACCEPTANCE.md
Unity/SandroneToon/TestArtifacts/M8/
Unity/SandroneToon/TestArtifacts/M8GameViewAudit/
```

## 6. 对外接口、输入输出及后续扩展点

- 输入：M7 角色 Renderer、槽 10、晶体 Renderer/槽 1、专用 Volume、`SandroneVfxBloomProfile_v1_M8`。
- 运行接口：EyeEmission、CrystalEmission、CrystalVisible、Bloom、DebugMode；不会改共享材质或非目标槽。
- 输出：HDR 眼光、HDR 晶体、专用 Bloom、EmissionMask/HDR/BloomExtraction Debug、A/B 与 Frame Debugger JSON。
- 可替换点：正式美术 Eye/Crystal EmissionMask 可直接替换种子；显示屏可作为独立第三模块接入，但不在本阶段实现。

## 7. Unity 配置与场景操作

打开 `Assets/Sandrone/Tests/Scenes/ToonCalibration_M8.unity`。菜单 `Sandrone > M8 > Build VFX and Bloom` 重建，`Sandrone > M8 > Validate VFX and Bloom` 复验。相机保持 HDR=true、Post=true、AA=None；Volume 仅含 Bloom。Build Settings 只启用 M8 场景。真实审计调用 `SandroneToon.Editor.SandroneM8GameViewAudit.RunFromCommandLine`，不得使用 `-batchmode/-nographics` 代替 Game View/Frame Debugger。

## 8. Blender 资产制作与导出设置

Blender `5.1.2` + mmd_tools 以 scale `0.08` 导入 `大剑.pmx`，不改模型、UV、骨骼或贴图，保存独立 Blend 并导出 FBX；Unity 导入法线、Mikk tangent，保留 hierarchy，关闭动画与材质自动导入。报告验证 7,473 顶点、6,492 三角、2 材质、1 源骨，`Mat_Cyrstal=1,576` 三角；FBX SHA-256 进入门禁。运行时不做 CPU 网格访问，因此 FBX Importer 的 Read/Write 关闭。

## 9. 缺失资源及制作方案

- Eye EmissionMask：当前为 `目光.png` Alpha 的精确 Linear 提取；正式美术可手绘亮点层，保持黑底、Linear、Mip、无压缩、Read/Write Off，并重测远距。
- Crystal EmissionMask：当前以 `min(G,B)-R` 的青色分离生成，并由独立 `Mat_Cyrstal` 子网格二次隔离；当前同样为 Linear/Mip/无压缩/Read/Write Off，正式版本应由美术按 UV 绘制单通道遮罩。
- Display：第三候选未实现；若后续选择，应从 `显示屏2.png` Alpha 制作独立透明发光层，不复用角色 Volume 参数掩盖排序问题。
- 粒子、溶解、拖尾、最终调色、Tonemapping、AA、物理移动设备性能均不属于 M8 已完成内容。

## 10. 自动自检、测试命令与实际结果

```powershell
$UnityEditor = '<Unity 6000.5.3f1 Editor executable>'
$ProjectPath = (Resolve-Path 'Unity/SandroneToon').Path
& $UnityEditor -batchmode -quit -projectPath $ProjectPath -executeMethod SandroneToon.Editor.SandroneM8Bootstrap.Build -logFile (Join-Path $ProjectPath 'TestArtifacts/M8/unity_m8_build_05.log')
```

- M8 Validator：`108/108`，failure `0`，warning `3`，Shader compiler message `0`；最终增加 MPB 门后为 `110/110`。
- 回归硬门：M7 `131/131`、M6 `154/154`、M5 `94/94`、M0–M4 `30/30`。
- 同配置 M7 材质控制组 MAE `0`；眼部/晶体 target MAE `7.244/30.006`，变化像素 `135/236`。
- Eye/Crystal/Combined Bloom MAE `0.1276/0.0985/0.0979`；Extraction 像素 `141/8,700`。
- HDR peak `4.478`，线性阈值 `1.233`，阈值以上 `440 px`；最终红裙像素 `99,902`；离线 PC/Mobile MAE `1.290`。
- 失败证据保留：旧 .NET Hash API 编译失败、Bloom 子资产未序列化、`-nographics` 被真实设备门禁拒绝、首轮 Frame Debugger 审计假设失败；均未改写为通过。

## 11. 人工验收步骤与通过标准

1. 用可见 Editor 打开 M8 场景并进入 Play，Game View 锁定 768×1280。
2. 依次切换 AllOff、Emission+NoBloom、Final；确认眼光/晶体局部改变，Bloom 关闭不改变 Base 纹理或非目标材质。
3. 检查 EmissionMask 和 BloomExtraction Debug：只有 EyeLight 与 `Mat_Cyrstal` 有信号，皮肤、白衣、红裙为黑。
4. 打开 Frame Debugger：应有 M8 Eye/VFX 各 1、M0 剑身 1、Bloom 16、M7 Outline 14、M5 2、M6 10、M4 18、ShadowCaster 46、Receiver 1。
5. 检查 M8 两事件 MPB：Role `1/2`、Weight `1`、Threshold `1.1`、Intensity `3.2/2.8`；退出 Play 后场景与 Controller 恢复。

最终真实 D3D12/RTX 4060 报告：119 frame events、failure `0`，Bloom toggle MAE `0.0812`、PC/Mobile MAE `1.350`、红裙 `59,651 px`。

## 12. 图样参考与阶段差异分析

开发时参考了原模型的图样，但不保留或配准具体图片；M8 只对项目内 A/B 帧进行逐像素比较。M8 Final 前景亮度为 `0.4814`、饱和度为 `0.2978`，调色留给 M9。Final 相对 AllOff 同机位变化覆盖 `1.431%`、MAE `1.731/255`；Bloom 相对 Emission-only 覆盖 `0.0827%`、MAE `0.0979/255`，说明泛光局部且克制。红裙保持红色主色和既有响应。

## 13. 已知问题、风险与回退方案

- 晶体为校准道具，未完成角色挂点、动画和最终构图；当前不投射/接收阴影是 VFX 审计边界。
- 两张 EmissionMask 都是明确标注的工程种子；正式美术替换后必须重跑 Extraction、HDR、PC/Mobile 与远距 Mip 验收。
- Bloom 是全屏成本；GPU 时间、带宽、SRP Batcher、构建后变体和物理移动设备未测，不能声称性能通过。
- 旧 M7 LDR 与 M8 HDR PNG 不可作为同配置材质回归；当前同 HDR 控制组 MAE 0 才是有效证据。
- 回退：Build Settings 指回 M7，删除 M8 场景/Profile/Volume/三材质/三纹理/晶体 FBX、M8 Shader/Runtime/Editor 即可；M0–M7 与源 PMX/纹理未覆盖。

## 14. 文档修正内容

- `VolumeProfile.Add<T>` 只更新内存列表；Editor 生成资产必须 `AddObjectToAsset`，否则重启后 YAML 为 `fileID: 0`，Bloom 实际不存在。
- 眼部隔离不能隐藏槽 6 的 Stencil Writer；应保留其模板写入并仅将颜色压黑，否则槽 10 的正确 `Equal` 测试会产生假空图。
- 旧 LDR/HDR 截图不能承担材质等价门禁；必须在同 HDR、相机、灯光、分辨率和管线下切换控制材质。
- 全帧 MAE 会稀释小面积发光；眼光/晶体同时记录变化像素与 target MAE，Bloom 仍记录全帧扩散 MAE。
- 未消费的审计常量会被编译器剔除；Frame Debugger 只接受实际参与 Shader 计算并在 GPU 有效状态中可见的参数。

## 15. 下一 Phase 建议（仅说明，不实施）

M9 已在锁定 M8 HDR/Bloom 基线上独立实现 Tonemapping、Color Grading、AA、变体与本机 GPU timing，详见 `M9_ACCEPTANCE.md`。物理移动设备、外存带宽和正式美术遮罩仍是后续产品化范围；M8 本身没有创建任何 M9 资产或 Renderer Feature。
