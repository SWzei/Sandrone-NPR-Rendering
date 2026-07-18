# M0 验收说明：Asset correctness / Unlit BaseMap

## 范围与状态

- 已完成：标准 `桑多涅.pmx` 的只读 Blender 基线、FBX、31 槽映射、Unlit BaseMap、61 个顶点 Morph、692 个源骨骼、两个材质 Morph 替代参数与一个骨骼 Morph 替代参数、前/侧视自动截图。
- 未实现：主光、Ramp、实时阴影、ControlMap、MatCap、FaceMap、头发/眼睛 Stencil、描边、Emission、Bloom、最终后处理。它们属于 M1–M9。
- 阶段截图采用正面+侧面作为主验收；反面由 360° 人工检查覆盖。

## 已锁定契约

| 项 | M0 契约 |
|---|---|
| Unity / 管线 | 6000.5.3f1 / URP 17.5.0 / Linear |
| 源模型 | 标准 `桑多涅.pmx`；SHA-256 `f73cd498580b0950856536223d57df04eb1164e01836c783cc75188a4c5c7514` |
| 拓扑不变量 | 69,864 triangles / 31 submeshes / 61 blend shapes |
| 骨骼 | FBX/Unity 692 个 PMX deform bones；不导出 MMD Tools 的 46 个 helper/shadow bones |
| 身高 | 1.55–1.72 m；实测 1.6445 m |
| 材质映射 | `SandroneMaterialMap_v1_M0`，源槽 0–30 一一对应 |
| Shader | `Sandrone/M0/UnlitBaseMap`，UniversalForward 单 Pass、0 keyword pragma |
| 朝向 | Unity 根变换保持 TRS identity；保存场景相机位于 +Z 看向原点；自动截图通过临时模型旋转取得正/侧视 |

Unity 导入后的顶点缓冲为 53,386，低于 PMX 的 61,973。FBX 转换链允许按属性焊接/拆点，故顶点数不是跨格式稳定标识；69,864 三角形、31 子网格、61 个 BlendShape 及实际形变才是本阶段硬门槛。该差异保留为 validator warning，不能被描述为“完全同顶点序”。

## Unity 人工验收

1. 用 Unity Hub/Editor 6000.5.3f1 打开项目，等待导入结束；Console 必须为 0 Error。
2. 打开 `Assets/Sandrone/Tests/Scenes/ToonCalibration_M0.unity`，Game 视图确认正面朝相机、角色身高和画面无粉材质。
3. 选中 `Sandrone_M0`，Inspector 中逐项把 `Eye AL Weight`、`Blush Weight` 从 0→1→0：只允许眼部叠层/腮红变化，不得改变其余槽。
4. 把 `Clockwork Rotation Weight` 从 0→1→0：`KeyB02_M` 应连续转动并准确回到基线；不得把该控制当 BlendShape。
5. Scene 视图绕角色水平旋转 360°：无丢贴图、明显反面剔除或透明层整片穿透。半透明层排序仍需在 M6 用 Stencil 正式解决，M0 只检查灾难性错误。
6. Frame Debugger 检查角色仅使用 `UniversalForward`；M0 不应出现 ShadowCaster、Outline 或自定义 Renderer Feature。

通过标准：0 编译错误、0 Shader 编译消息、31/31 材质非空且 BaseMap 正确、前/侧朝向正确、控制可逆、无粉色/整片缺失。若透明毛发边缘或眼层有局部排序差异，记录但不以 M0 阴影/Stencil 方案掩盖。

## Blender 基线与导出

1. Blender 5.1.2，启用 MMD Tools 4.5.13；不要用 `--factory-startup` 验证扩展，因为它会卸载用户扩展并产生假阴性。
2. `File > Open` 打开 `Source/Blender/Sandrone_MMD_Baseline.blend`；这是包含物理对象的只读导入基线。
3. 如需重建，运行 `Scripts/Blender/import_sandrone_m0.py -- <PMX> <BLEND> <FBX> <REPORT>`；导入比例 0.08。
4. FBX 只导出角色 mesh 与 armature；`Add Leaf Bones` 关闭、`Only Deform Bones` 开启、Apply Unit/Transform 保持脚本设置、Animation 关闭、Shape Keys 保留。
5. 不把 Blender 物理预览当 Unity 真值：MMD Tools 导入 `KeyB02_M` 时报告 dependency-cycle 警告，但网格、骨骼和形态导出完整；二级运动需后续在 Unity 独立验收。

## 缺失依赖/资源（本 Phase 不制作）

| 资源 | 用途与建议规格 | 制作/导入与验证 |
|---|---|---|
| Ramp（M2） | 五行材质族色阶，建议 256×32/64 RGBA | Blender/绘图软件绘制，每行留 padding；若作为数值/LUT 则 sRGB Off、Clamp、No Compression；逐行扫 `NdotL` 验证无串色 |
| ControlMap（M4） | R 高光类型/强度、G AO、B 材质响应、A Ramp/材质 ID；建议 2048² | 按 UV 区域绘制；Linear、MipMaps 视距离测试、通道分别可视化，禁止误当颜色纹理 |
| MatCap（M4） | 金属/饰品视空间响应，建议 512² | 球体正交烘焙/绘制；颜色型可 sRGB On，Clamp；旋转相机验证随视图而非世界锁定 |
| FaceMap（M5） | 脸部光角 SDF，1024–2048² | 固定头部轴按角度制作有符号距离；Linear、Clamp、No Compression；左右光角扫描验证单调和镜像 |
| Outline Normal/Width（M7） | 平滑轮廓法线与局部宽度 | Blender 同位置顶点平均，只用于描边；建议 Color.rg/独立 UV 编码法线、Color.a 宽度，契约名 `ToonVertexLayout_v1`；Unity 调试视图逐通道确认 |
| EmissionMask（M8） | 眼睛/晶体/显示屏选择性发光 | Linear 单通道/通道打包；皮肤、白布、腮红必须为 0；HDR 阈值前后可视化 |

## 证据与回退

- 自动报告：`TestArtifacts/M0/M0ValidationReport.json`。
- 正/侧阶段截图：`TestArtifacts/M0/ReferenceComparison/`。目录名沿用历史约定，但不依赖外部图样文件。
- 回退：删除 `Assets/Sandrone/Materials/M0`、`Assets/Sandrone/Configs/SandroneMaterialMap.asset`、`Assets/Sandrone/Tests/Scenes/ToonCalibration_M0.unity` 后重新执行 Bootstrap；源 PMX、原贴图和 `.blend` 基线均不修改。若需完全回退 M0，移除 `Assets/Sandrone` 与 `TestArtifacts/M0`，不会影响模板 URP 资产。
