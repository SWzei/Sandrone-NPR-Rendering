# 第三方角色资产配置

本仓库公开项目原创的 Unity 工程结构、C#、Shader、配置、材质参数和技术文档，但不分发桑多涅角色模型及其可还原衍生资产。角色来源文件随附说明包含禁止二次配布和禁止商业使用的限制，因此新克隆的仓库不是开箱即用的完整角色资产包。

## 环境

- Unity `6000.5.3f1`
- Universal Render Pipeline `17.5.0`
- Blender 及项目 `Scripts/Blender` 中的转换脚本
- 正式目标平台：Windows PC

## 获取与放置源资产

1. 从原发布页面自行获取“【原神】桑多涅”模型，并阅读发布页、下载文件中的最新条款及本仓库按原字节保留的[原始使用说明](ThirdParty/Sandrone/README_ORIGINAL_ZH.txt)。
2. 不要把下载的压缩包、PMX、原始纹理、转换后的 FBX、Blend 文件或 Player 构建提交到本仓库。
3. 将源资产保存在本机 `Source/MMD/【桑多涅】`。该目录已由 `.gitignore` 排除。
4. 使用 `Scripts/Blender` 中的脚本完成 PMX—Blender—FBX 转换；阶段性脚本的具体参数和验收条件见技术管线及 M0–M9 文档。
5. 将主角色 FBX 生成到 `Unity/SandroneToon/Assets/Sandrone/Models/Sandrone_M0.fbx`，并恢复阶段所需的可选 FBX。仓库保留相应 `.meta` 文件，用于维持既有 Unity GUID。
6. 将原始 BaseMap 放入 `Unity/SandroneToon/Assets/Sandrone/Textures/SourceBase`，保持现有文件名。运行 M0–M9 Bootstrap 时，工程会按阶段生成或配置项目自有的 Ramp、Face SDF 种子、控制图、MatCap、材质、描边网格和测试产物。

原说明允许完善物理、修正权重、表情等问题，允许改色、适度调整衣装及添加 spa/toon；同时明确禁止二次配布、拆取部件改造其他模型、18 禁、极端宗教宣传、血腥恐怖猎奇、人身攻击和商业用途。使用者必须以原文及来源页的最新条款为准，不得把本项目的转换、渲染或 MIT License 理解为对模型资产的再授权。

## 本地验证

1. 使用上述 Unity 版本打开 `Unity/SandroneToon`。
2. 等待首次资源导入与 Shader 编译完成，确认 Console 没有编译错误。
3. 按 `Unity/SandroneToon/Assets/Sandrone/Docs` 中的阶段顺序执行 M0–M9 Bootstrap 和 Validator。
4. 重新生成的完整证据位于 `Unity/SandroneToon/TestArtifacts`。除 README 使用的八张项目截图外，该目录默认不进入 Git。
5. Player、Frame Debugger、Profiler、D3D11 和 D3D12 结论必须以本机本轮生成的日志和证据为准，不能由仓库中的展示图片代替。

## 公开仓库边界

MIT License 只适用于项目贡献者原创的软件与文档，不授予第三方模型、纹理、角色设计、商标或其他游戏资产的权利。详细边界见 `THIRD_PARTY_NOTICES.md`。
