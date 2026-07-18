# M6 头发与眼睛阶段验收

> 2026-07-16 专项修订：M6 Hair/Eye Forward 的 PC cascade 坐标已改为片元级选择；D3D11/D3D12 各 10 张级联/视角/光向审计均为 0 failure。Controller 新增状态脏检查，无变化帧不再重复写目标材质槽 MPB。

## 1. Phase 目标与完成状态

M6 已实现并闭环：标准 31 槽模型增加眼部模板/叠层、弱平光与头发低频切线高光；M5 Face SDF、M4 非目标材质和红色长裙基线保持不变。可动眼齿轮作为独立 33 槽可选资产完成 Blender→FBX→Unity 与 0.5/2/5/10 m 桌面审计，未替换标准场景。这里的阶段边界是“M6 当时未实现 M7–M9”；当前工程已在后续独立阶段实现 M7–M9，分别以其验收文档为准。

## 2. 现状检查及设计依据

- Unity `6000.5.3f1`、URP `17.5.0`、Linear；PC 为 Forward+ / 4 cascades / 2048，Mobile 为 Forward / 1 cascade / 1024。
- 标准 PMX 为 31 材质槽；M6 目标槽仅 `2,3,6,7,8,9,10,11,12,13,29`。脸槽 0/1 精确复用 M5，其他非目标槽精确复用 M4。
- 原 `髮.png` 已含绘制高光，`髮+`/`sp.png` 又是叠层；因此只给前/后发增加 Control R 门控的低频切线亮带，槽 29 禁止程序高光，避免双重亮带。
- 标准模型已有眼白、虹膜和多张眼层；实测以不透明虹膜几何写 Stencil、装饰眼层读取最稳定，不把 EyeLight 提前做成 HDR 发光。

## 3. 实现方法与关键技术原理

- `Sandrone/M6/HairEye` 保留完整 M4 `UnityPerMaterial` 前缀，M6 常量只追加，并复用已审计 M4 ShadowCaster。
- 槽 6 不透明虹膜写 Stencil bit 0；槽 7–11 以 `Equal` 读取，避免装饰层越出虹膜模板。槽 3 眼白保持原 Opaque 基线；透明层保持原队列、Blend、ZWrite 与 Cull。
- 眼层通过有界 LDR 弱平光减轻球面 Lambert 过暗；权重为 0.18–0.30，不代替 BaseMap。`Eye AL` 初始 `_LayerWeight=0`，由既有层权重/MPB 动画。
- 发高光使用网格 Tangent、视线与光方向构造低频 lobe，乘 `Control.r`、Styled Cast、HairBase role 和总开关；槽 29 HairOverlay 的强度固定为 0。

## 4. 修改文件及逐项原因

- `Runtime/SandroneM6HairEyeProfile.cs`：版本化 11 槽语义与参数契约。
- `Runtime/SandroneM6Controller.cs`：逐槽 MPB、头部轴、开关与 8 个 Debug 模式。
- `Shaders/SandroneHairEyeM6.shader`：M6 Forward、Stencil、眼层与头发高光；无 Outline/Emission/Bloom。
- `Editor/SandroneM6Bootstrap.cs`：从 M5/M4 基线生成 11 个目标材质、场景与自动截图。
- `Editor/SandroneM6Validator.cs`：154 项结构、绑定、状态、截图、A/B 与回归门。
- `Editor/SandroneM6GameViewAudit.cs`：真实 Play Mode Game View 与逐事件 Frame Debugger 审计。
- `Editor/SandroneM6OptionalEyeGearAudit.cs`：独立可动眼齿轮导入、材质、距离/转角审计。
- `Editor/SandroneAssetPostprocessors.cs`：仅为可选 FBX 与齿轮 Alpha 贴图补充导入规则。
- `Scripts/Analysis/compare_m6.py`：可选离线分析及同相机 M5/M6 逐像素对照；外部图样输入不属于默认工程依赖。

## 5. 当前目录结构

```text
Assets/Sandrone/
  Configs/SandroneHairEyeProfile_M6.asset
  Materials/M6/                         # 11 个标准 M6 材质
  Materials/M6OptionalEyeGear/          # 3 个独立 AlphaClip 齿轮材质
  Models/Optional/Sandrone_EyeGear_M6.fbx
  Textures/OptionalEyeGear/T_EyeGear{1,2,3}.png
  Runtime/SandroneM6*.cs
  Shaders/SandroneHairEyeM6.shader
  Editor/SandroneM6*.cs
  Tests/Scenes/ToonCalibration_M6.unity
  Tests/Scenes/ToonCalibration_M6_OptionalEyeGear.unity
Blender/Optional/Sandrone_EyeGear_M6.blend
TestArtifacts/M6*/
```

## 6. 对外接口、输入输出及扩展点

- 输入：Renderer、角色根、头骨、方向光、M3 ShadowProfile、M6 Profile。
- 运行接口：`HairSpecularEnabled`、`EyeLayersEnabled`、`DebugMode`、`SetLightDirectionToSource`；`Eye AL` 继续通过该槽 `_LayerWeight` 驱动。
- 输出：标准 M6 场景、11 个材质、Debug/A-B/Pipeline 截图和 JSON 报告。
- M7 可增加独立 Outline Pass；M8 可消费 EyeLight/Emission Mask；M9 可做变体剥离与目标机性能。当前阶段没有预实现这些功能。

## 7. Unity 配置与场景操作

打开 `Assets/Sandrone/Tests/Scenes/ToonCalibration_M6.unity`，使用 `Sandrone > M6 > Build Hair and Eyes` 重建，`Sandrone > M6 > Validate Hair and Eyes` 复验。真实审计需在可见 Editor 中调用 `SandroneToon.Editor.SandroneM6GameViewAudit.RunFromCommandLine`，不得加 `-batchmode` 或 `-nographics`。标准 M6 场景是唯一启用的 Build Settings 场景；可选齿轮场景明确排除。

## 8. Blender 资产制作与导出设置

标准 M6 没有改模型、UV、法线、骨骼或贴图。可选路径从 `桑多涅_目齿轮可动.pmx` 经 Blender 5.1.2 + mmd_tools 按 0.08 scale 导入并导出 FBX；报告记录 66,599 顶点、78,744 三角、33 材质、692 源骨、61 BlendShape、UVMap/UV1/UV2。FBX 作为独立资产，不覆盖标准 31 槽 FBX。

## 9. 缺失资源及制作方案

- 真正的 Hair ILM/ControlMap 不存在；当前 `Sandrone_Hair_Control` 是 M4 项目种子。后续应按前/后发 UV 手绘 R 高光覆盖，Linear、Clamp，先无损验证再选平台压缩。
- 标准眼层来自原纹理，不缺 BaseMap；`Eye AL` 的时间曲线/动画 Clip 尚未制作，只保留可动画权重接口。
- Outline Normal/宽度数据、HDR Emission Mask、Bloom 和最终 LUT 分别属于 M7–M9，本阶段未制作。
- 可选齿轮三张纹理是原 `目2.png/目遮罩.png/目遮2.png` 的字节复制；导入为 sRGB、Mip、Preserve Coverage、Clamp、Bilinear、AlphaClip 0.5。

## 10. 自动自检、测试命令与实际结果

```powershell
$UnityEditor = '<Unity 6000.5.3f1 Editor executable>'
$ProjectPath = (Resolve-Path 'Unity/SandroneToon').Path
& $UnityEditor -batchmode -quit -projectPath $ProjectPath -executeMethod SandroneToon.Editor.SandroneM6Bootstrap.Build -logFile (Join-Path $ProjectPath 'TestArtifacts/M6/unity_m6_final.log')
```

- M6 Validator：`154/154`，failure `0`，warning `3`，Shader compiler message `0`。
- M0–M4 综合回归：`30/30`；M5 Validator：`94/94`。
- A/B：Hair Spec MAE `0.4338`；Eye Layers `0.0506`；Eye AL 改变 664 px、目标区域 MAE `8.268`；刘海投影 `0.0751`；PC/Mobile离线渲染 MAE `0.4038`。
- 可选齿轮审计：failure `0`；0.5/2/5/10 m 覆盖分别 `9074/436/68/14 px`，5 m 起建议退回标准眼层。

## 11. 人工验收步骤与通过标准

1. 在锁定 M6 场景进入 Play，Game View 设 768×1680；确认脸、红裙和非目标材质与 M5 基线一致。
2. 在 Inspector 切 Hair Spec：仅前/后发低频亮带变化，原 `髮+` 叠层不出现第二条程序亮带。
3. 切 Eye Layers 与 Eye AL 0→1：变化只在槽 6 不透明虹膜写入的 Stencil 模板内，虹膜不压黑，Eye AL 初始隐藏且可动画。
4. 打开 Frame Debugger：应识别 2 个 M5 face、11 个 M6 target、18 个 M4 baseline Forward；槽 6 为 Stencil writer，槽 7–11 为 reader。
5. 旋转主光与头部：Face SDF 继续只作用槽 0/1，刘海投影随真实方向光变化；裙摆保持红色。

通过标准：无 Console/Shader error；上述槽数、状态和作用域满足报告；无眼层越界/透明排序突变/双重发高光；标准场景不引用可选 33 槽模型。

## 12. 图样参考与阶段差异分析

开发时参考了原模型的图样，但不做几何配准、图像变形或文件依赖。M5/M6 蓝色眼部代理像素均为 `539`；M6 相对 M5 饱和度仅 `-0.000873`、Value `+0.002845`，说明弱眼层没有压缩虹膜覆盖。M5→M6 使用真实 Game View 同相机帧，全帧 RGB MAE `0.1978`，仅 `1.413%` 像素变化，符合眼/发局部阶段边界。整体色调、描边、Bloom 和后处理差异留给 M7–M9。

## 13. 已知问题、风险与回退方案

- 当前 ControlMap 是启发式种子；GPU 时间、SRP Batcher、构建后变体数和物理移动设备尚未测，不能声称性能通过。
- Frame Debugger 会剥离未消费的审计 float，故逐槽映射使用 BaseMap、Role、权重、Stencil、Blend/ZWrite 的有效签名；相同签名只按候选多重集，不伪造唯一槽号。
- 可动齿轮 10 m 仅 14 px，亚像素稳定性不足；标准模型保持默认，建议最迟 5 m LOD/切回。
- 回退：Build Settings 指回 M5；删除 M6 Profile/11 材质、M6 Shader/Runtime/Editor/场景与可选目录即可。M0–M5、标准 FBX、原 PMX、UV、骨骼和纹理均未覆盖。

## 14. 文档修正内容

- 将“头发切线各向异性”收窄为本资产可证实的 `原绘制高光 + 原叠层 + Control R 门控低频切线亮带`；完整双频物理模型不是当前资产事实。
- Stencil 采用槽 6 不透明虹膜 writer + 槽 7–11 眼层 reader，而不是眼白 writer，也不是仅靠透明排序。
- 可动齿轮从假设性风险升级为独立实测：桌面 0.5–10 m 已有证据，物理移动设备仍未测。
- 眼部亮层保持 LDR；HDR Emission/Bloom 明确延后到 M8。

## 15. 下一 Phase 建议（仅说明，不实施）

M7 应先审计现有法线/硬边/UV seam 与可能的平滑描边法线来源，再实现像素尺度受控的反面外扩，并保持脸、眼透明层和 ShadowCaster 状态不被轮廓 Pass 污染。本次没有创建 M7 代码或资产。
