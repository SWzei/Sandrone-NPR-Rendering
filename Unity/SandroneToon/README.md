# Sandrone Toon — M9

Unity `6000.5.3f1` / URP `17.5.0` / Linear。当前磁盘中已实现 M0–M9；正式目标平台为 **Windows PC**。默认场景为 `Assets/Sandrone/Tests/Scenes/ToonCalibration_M9.unity`，默认与 PC Quality 都绑定 `PC_RPAsset`，Build Settings 只包含 M9 场景。仓库中的 Mobile RP Asset 仅保留为历史兼容配置，不构成 Android、真机或移动发布支持声明。

本轮 2026-07-16 专项修复在 RTX 4060 Laptop 上完成：M3/M4/M5/M6/M8 五个角色 Forward Shader 已按 URP 17.5 的 `REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR` / `MAIN_LIGHT_CALCULATE_SHADOWS` 契约修复；D3D11/D3D12 各 10 张级联切换、视角和光向实拍，5 个 Shader 均为 0 compiler message / 0 failure。M0–M9 当前链为 M0 67、M1 61、M2 88、M3 93、M4 94、M5 94、M6 154、M7 131、M8 110、M9 91，均为 0 failure；M0–M4 综合回归 30/30。可见 Game View/Frame Debugger 的 D3D11/D3D12 分别为 134/122 events、0 failure，Play 退出与场景重开状态恢复通过。

Windows x64 Release Player 实际为 D3D12、PC、`PC_RPAsset`、4 cascades、soft shadow；构建 `0 error / 0 warning`、455,014,334 bytes。120 帧预热、240 帧窗口中，frame mean/median/P95 为 `1.870/1.750/2.956 ms`，CPU 为 `1.874/1.758/2.957 ms`，GPU 为 `0.702/0.693/0.738 ms`（173 个有效 GPU 样本）。这些数值只代表本机固定标定窗口，不外推为内容预算。

先创建新证据会话，再重建 Player 与全链回归：

```powershell
$UnityEditor = '<Unity 6000.5.3f1 Editor executable>'
$ProjectPath = (Resolve-Path 'Unity/SandroneToon').Path

& $UnityEditor -batchmode -quit -force-d3d11 `
  -projectPath $ProjectPath `
  -executeMethod SandroneToon.Editor.SandroneEvidenceSession.BeginFromCommandLine `
  -logFile (Join-Path $ProjectPath 'TestArtifacts/Audit/EvidenceBegin.log')

& $UnityEditor -batchmode -quit -force-d3d11 `
  -projectPath $ProjectPath `
  -executeMethod SandroneToon.Editor.SandroneM9Bootstrap.Build `
  -logFile (Join-Path $ProjectPath 'TestArtifacts/Audit/Logs/M9FullBuild.log')
```

真实 Game View / Frame Debugger 审计必须使用可见 Editor，不能加 `-batchmode` 或 `-nographics`：

```powershell
& $UnityEditor -projectPath $ProjectPath `
  -force-d3d12 -executeMethod SandroneToon.Editor.SandroneM9GameViewAudit.RunFromCommandLine `
  -logFile (Join-Path $ProjectPath 'TestArtifacts/Audit/Logs/M9GameView.log')
```

完成 D3D11/D3D12 可见审计、级联审计和 `SandroneMpbLifecycleAudit.Run` 后，必须调用 `SandroneEvidenceSession.FinalizeFromCommandLine`；它会校验本会话时间、结构化失败字段、会话 ID、所有正式输入与产物 SHA-256。随后 `RunNegativeTestsFromCommandLine` 必须确认篡改文件、旧时间戳和源码指纹漂移均被拒绝。

当前专项范围已完成，但仍不能把整个项目表述为无条件产品验收：M7–M9 的单一所有者状态已实现禁用/销毁恢复，M4–M8 无变化帧已停止重复 MPB 写入，多实例隔离审计为 18/18；M0–M6 仍共享同一材质槽 MPB 且存在重叠字段，若要求任意中间阶段组件独立卸载而不影响其他阶段，需要统一所有权协调器，属于待确认的架构调整。另有 114 条既有 Unity 6.5 弃用 API 警告及外部显存带宽未验证。Android/物理移动设备不在当前正式目标范围内。

完整证据边界、修复、风险与回退见 `Assets/Sandrone/Docs/M9_ACCEPTANCE.md`、`Assets/Sandrone/Docs/DEVELOPMENT_LOG.md` 和根目录 `原神风格非真实感渲染技术管线.md`。
