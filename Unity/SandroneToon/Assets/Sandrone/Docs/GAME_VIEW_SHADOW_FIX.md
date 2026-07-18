# Game View 地面矩形阴影修复（2026-07-15）

## 复现条件与证据边界

- Unity 6000.5.3f1、URP 17.5.0、Linear、D3D11、RTX 4060 Laptop GPU。
- 场景 `ToonCalibration_M4.unity`，实际 Play Mode Game View，固定 `768×1680`；截图使用 `ScreenCapture`，不是 Scene View、`Camera.Render` 或 `-nographics`。
- 当前层级只有正式接收面 `M4_ShadowGround` 与角色 Renderer。`M3_AlphaClipProbe`、`M3_ValidationBlocker` 不存在于 M4 场景；在 M3 场景中也序列化为禁用。

## Frame Debugger 定位

- 当前帧共 116 个事件。事件 2–70 是 69 个角色主光 ShadowCaster draw，目标为 `_MainLightShadowmapTexture_2048x2048_Shadowmap`；透明槽不提交 ShadowCaster。
- 事件 81 是 `M4_ShadowGround / M3_ShadowReceiver / Sandrone/M3/ShadowReceiver / M3ShadowReceiver`。
- 事件 81 状态：`UniversalForward`、CameraTarget 768×1680、Depth 有效、Cull Back、ZWrite On、ZTest LEqual、Blend One/Zero。
- limit 停在事件 80 时只有清屏；加入事件 81 后所有矩形网格和被切断的角色投影同时出现。修复前后事件号、对象、材质、Pass、RT 与 Render State 均不变。

## 根因与修复

旧接收 Shader 在顶点阶段调用 `GetShadowCoord(positionInputs)`。四级联模式会在顶点处选择 Shadow Atlas tile，再把坐标跨三角形插值。Unity 内置 Plane 是规则网格；当三角形顶点属于不同 cascade 时，插值坐标跨 tile，产生与网格单元一致的矩形分块、边界截断和错误采样。

修复：顶点只输出 world position 与 screen position。非 Screen-space 路径在像素阶段调用 `TransformWorldToShadowCoord(input.positionWS)`，逐像素选择 cascade；`_MAIN_LIGHT_SHADOWS_SCREEN` 保留正确的插值 screen position。没有关闭阴影、隐藏接收面、改 Bias、改变级联、扩大地面或裁图。

`SandroneM3Validator` 的旧契约曾硬编码要求顶点 `GetShadowCoord`。该检查没有删除或放宽，而是改为强制逐像素 world-to-shadow、强制 Screen-space 分支，并禁止 `output.shadowCoord = GetShadowCoord(positionInputs)`。

## 实际结果

- 同机位前后全帧 MAE `0.692`；地面 ROI MAE `1.453`；变化像素 `2.302%`，集中于错误矩形、错误 cascade 采样及被截断的影子。
- Frame Debugger Ground-only 前后 MAE `0.868`，地面 ROI MAE `1.715`；修复后 Ground event 为单一连续接收面，角色影子连续。
- 旋转主光后地面 ROI MAE `7.754`，证明投影方向随主光响应而非静态硬编码。
- PC Forward+ 与 Mobile Forward 的全帧 MAE `1.389`，差异来自 2048/四级联/软阴影与 1024/单级联/硬阴影；两者均无矩形分块或影子截断。
- 实际 Game View 已覆盖默认、主光旋转、相机 near/mid/far、PC Forward+、Mobile Forward。
- 修复后完整 M0–M4：M0 67、M1 61、M2 88、M3 88、M4 94、综合 30，均 0 failure；Shader compiler message 0，进程返回 0。

## 修改与回退

- `SandroneShadowReceiverM3.shader`：逐像素 cascade selection。
- `SandroneM3Validator.cs`：新接收 Shader 契约。
- `SandroneGameViewShadowAudit.cs`：Editor-only 真实 Game View/Frame Debugger 证据工具，不进入 Player runtime。

代码回退只需恢复以上三个文件。但恢复旧 Receiver Shader 会确定性重现矩形；若只需禁用审计工具，可删除 `SandroneGameViewShadowAudit.cs`，不影响运行时。

## 人工复验

1. 打开 M4 场景，将 Game View 固定 768×1680 并进入 Play Mode。
2. Frame Debugger 锁定 Renderer/材质/Pass，而非固定事件号；当前记录为事件 81。
3. 在 Ground draw 前后步进：加入 Ground 后应只出现一个连续接收面和完整影子，不应按 Plane 网格分块。
4. 主光绕 Y 轴旋转；相机在约 2.8、4.9、12 m 观察；不得出现矩形、共面闪烁、Shadow Acne、Peter Panning 或级联跳变。
5. 切换 PC/Mobile Quality，确认 Forward+/Forward 均满足同一条件。

