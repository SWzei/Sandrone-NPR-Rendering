# M4 验收：ControlMap、材质响应与 MatCap

> 2026-07-15 回归修订：旧 `81/81` 结论因默认纹理与运行时材质突变造成假阳性，已废止。当前验证为 `94/94`；槽 24/28/29 最终着色 target MAE=`2.348/1.427/1.217`、non-target=`0/0/0`。Isolation 仅作槽位定位，详见 `M0_M4_REGRESSION_AUDIT.md`。

> 2026-07-16 专项修订：M4 Forward 的 PC cascade 坐标已改为片元 `positionWS` 选择，并保留非级联/屏幕分支的条件顶点插值。D3D11/D3D12 级联审计均为 0 failure；Controller 对无变化帧不再重复写 31 个材质槽 MPB。

## 1. Phase 目标与完成状态

实现 Skin/Matte/Silk/Metal 响应、ControlMap/MatCap 数据契约及槽 24/28/29 独立寻址。2026-07-15 真实 D3D11 自动闭环 **94/94、0 failure、4 warnings**；全部 Debug Hash 唯一，最终着色局部 A/B 已达标。Unity Frame Debugger 事件身份与 GPU 时间仍待人工确认。

## 2. 现状检查及设计依据

固定 Unity 6000.5.3f1、URP 17.5.0、Linear；M3 88/88 为硬门。资产无可信 ILM/ControlMap/MatCap；`sp.png` 不冒充 ILM。Face SDF、Stencil、发丝各向异性、描边、Emission 均未越阶。

## 3. 实现方法与关键技术原理

R/G/B/A 分别为高光权重/中性 AO/金属候选/保留；材质类型由 Profile 常量提供。H=normalize(L+V)，Skin/Silk/Metal 使用不同宽度和阈值；Metal 叠加 view-space normal MatCap；Spec/MatCap 乘 Styled Cast。

## 4. 修改文件及逐项修改原因

- `SandroneMaterialResponseM4.shader`：M4 正向与 ShadowCaster。
- `SandroneM4Isolation.shader`：只用于槽映射验收，不进入最终效果。
- `SandroneM4MaterialResponseProfile.cs`：版本化 31 槽响应契约。
- `SandroneM4Controller.cs`：Debug 与三组开关。
- `SandroneM4Bootstrap.cs` / `SandroneM4Validator.cs`：可重建场景、资源、截图、报告。
- `compare_m4.py`：可选离线颜色统计工具；外部输入不属于默认工程依赖。
- README、技术文档、开发记录：同步证据、限制与回退。

## 5. 当前目录结构

`Assets/Sandrone/{Configs,Docs,Editor,Materials/M4,Runtime,Shaders,Textures/Control,Textures/MatCap,Tests/Scenes}`；运行证据在 `TestArtifacts/M4/{AB,Debug,ReferenceComparison}`。

## 6. 对外接口、输入输出及后续扩展点

输入为 Base/Ramp/Control/MatCap、ResponseType/FeatureGroup、M3 ShadowProfile；输出为 URP Forward color 与 ShadowCaster depth。Profile 可替换美术 ControlMap/MatCap；A 通道和 Face/Hair 专用接口留待后续 Phase。

## 7. Unity 配置与场景操作

打开 `ToonCalibration_M4.unity`；主光 Soft Shadow、相机 Orthographic/HDR Off。组件 `SandroneM4Controller` 可切 12 个视图及 Metal/Stocking/HairOverlay。当前 Debug/Feature 状态通过逐材质槽 MPB 写入，不使用全局关键字；多个 Controller 不会因全局 Keyword 串扰，但逐槽 MPB 会破坏对应 Renderer 的 SRP Batcher 路径，性能影响必须实测。

## 8. Blender 资产制作与导出设置

M4 未改 FBX。继续使用 M0 的 Apply Transform/米制、法线/切线、31 submesh、61 blend shapes、692 bones 契约；如在 Blender 绘制 ControlMap，UV 不变，输出 PNG RGBA、Linear 数据。

## 9. 缺失资源及制作方案

现有三张 ControlMap 为启发式种子，须按 UV 人工绘制 R 高光、G AO、B 金属；A 保持 0。MatCap 可在正交球上绘制暖主峰/冷辅峰并烘 512² sRGB。FaceMap、Outline Normal、Emission Mask 仍缺失且不属于 M4。

## 10. 自动自检、测试命令与实际结果

执行回归文档命令（禁止 `-nographics`）。结果：退出码 0；94/94；Shader message=0、Pass=2、pragma=4、源码 sample=5；最终着色槽 24/28/29 target MAE 2.348/1.427/1.217；MatCap 视角 MAE 1.722；M4/M3 亮度比约 1.017。

## 11. 人工验收步骤与通过标准

1. 在 Game View 固定 768×1280，依次切 Control R/G/B/A：R 有变化、G 全白、B 仅候选金属、A 全黑。
2. 切 MaterialResponse：Matte/skin/silk/metal 应为灰/红/蓝/黄；不得显示最终贴图。
3. Frame Debugger 检查槽 24/28/29，逐项关闭；目标 draw 的响应/透明贡献消失，其他槽不变。
4. 旋转到三分之四视角，MatCap 峰随视角移动；阴影内无独立高光。
5. 记录 Forward/ShadowCaster draw 与 GPU 时间。以上未由自动化代替。

## 12. 图样参考与阶段差异分析

开发时参考了原模型图样中的材质和明暗关系，但不保留具体图片或量化依赖。M4 相对 M3 的正/侧亮度变化仅 +0.0081/+0.00085；金色略亮，面/发仍缺专用阴影和高光，描边、环境暗部和最终调色尚未实现。

## 13. 已知问题、风险与回退方案

Isolation 只证明槽可寻址；最终着色 A/B 已用 Alpha-aware mask 验证。ControlMap 为推断种子；31 槽、透明 overdraw 与逐槽 MPB 的 SRP Batcher 影响仍需 Profiling。删除 M4 新增目录与文件并把 Build Settings 指回 M3 即可回退。

## 14. 文档修正内容

修正“Control A 必然可放材质 ID”为条件性方案；明确 G=1、R/B 为推断；记录场景必须保存重载后取证；移除 global keyword 兼容路径，改为逐槽 MPB；所有未测项保持未通过表述。

## 15. 下一 Phase 建议（仅说明，不实施）

人工确认 M4 后再进入 M5 Face SDF：先制作/校验左右镜像 FaceMap、头部语义轴和脸部专用材质，不复用当前启发式 Body ControlMap。
