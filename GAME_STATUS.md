# GAME_STATUS.md

此文档由 AI 维护，用于记录 Jumping Ninja 的实现状态、结构和后续注意事项。

## 当前版本

- 游戏版本：v1.0.3
- 最近更新：2026-08-30
- Unity：6000.5.9f1（arm64）
- 当前构建目标：Android
- 状态：核心玩法与菜单流程已实现；v1.0.3 Android APK 已发布到 GitHub Release

## V1 已实现功能

- 进入游戏后显示带 Potatoed Mice Logo 的加载页。
- 首次运行要求创建本地 Ninja 用户，仅需用户名。
- 本地用户表、当前用户和最高分使用 `PlayerPrefs` 保存，存储键为 `JumpingNinja.Users.v1`。
- 主菜单包含开始游戏、排行榜和切换用户。
- 排行榜按最高分降序显示；切换用户页支持创建新用户。
- Ninja 视觉保持 1×1 红色方形忍者头像，物理碰撞箱默认缩为视觉范围的 82%，始终受 2D 重力影响。
- 每次向左或向右跳跃时，忍者头像会播放压缩、拉伸、位移和方向倾斜动画；动画只影响视觉子对象，不改变碰撞体。
- 死亡时忍者头像会震动、弹起、旋转、缩小并淡出，动画结束后才显示结算界面。
- 点击左/右半屏会把 Ninja 速度重设为偏离竖直方向指定角度的左上/右上速度。
- 地图默认宽 15 格，左右边缘由 1×1 浅色忍者石墙组成；墙砖与黑色机关砖采用匹配的边框、斜纹和手里剑图案，并以象牙白/银灰和深黑色保持清晰区分。白墙碰撞完全交给 Unity 2D 求解器，不再在持续接触时手动清零横向速度，因此可以立即向离墙方向跳开。
- 黑色方块会在 Unity 报告真实碰撞进入时结束本局；第 0 层下方为完整致死地板。黑块及致死地板使用克制的深色忍者纹理，并随层数切换色调。所有方块均为实体碰撞体；Ninja 使用 Continuous 碰撞检测以及摩擦力 0、弹性 0 的物理材质，不再使用会提前判死的 Collider 预测扫掠。
- 无限地图按默认 9 格高度动态向上生成。层间有 1–2 个随机的 1×3 缺口，缺口周围不会生成额外障碍。
- 第 1–5 层无额外障碍，第 6–10 层每层 1 个，第 11–20 层每层 2 个，之后每十层递增。
- 镜头默认显示 9 格横向范围，纵向范围按设备宽高比计算；跟随 Ninja 并限制在地图左右和底部边界内。
- 背景使用低透明度的忍者主题纹样；背景颜色、纹样色调与黑块色调均每 10 层切换一次，当前提供 6 组循环主题。
- Ninja 默认出生 Y 坐标为 4.5，位于 9 格高的第 0 层中央，避免与第一层边界重叠。
- 左上角显示当前等级及下一个可超越的用户分数，包括当前用户自己的历史纪录。
- 超越其他用户或刷新个人纪录时显示限时提示。
- 开场 TAP 操作提示和纪录提示均不参与 UI 射线检测，显示期间不会阻挡左右半屏点击。
- 暂停菜单支持返回主菜单；恢复游戏前显示完整 3 秒倒计时。
- 死亡后以本局到达的最高层数结算，可重试或返回主菜单。

## 主要文件结构

- `README.md`：项目简介、APK 下载入口和开发环境说明。
- `scripts/release-windows.ps1`：Windows 下一键完成 Android 构建、Git 标签和 GitHub Release 发布。
- `.agents/skills/windows-unity-release/SKILL.md`：仓库级 Windows Release 操作说明。
- `ReleaseNotes/`：随仓库维护的各版本 GitHub Release Notes。
- `Assets/Scripts/GameBootstrap.cs`：场景加载后自动建立 V1 游戏入口。
- `Assets/Scripts/GameApp.cs`：加载页、用户创建、主菜单、排行榜、切换用户和游戏流程切换。
- `Assets/Scripts/UserRepository.cs`：本地用户表和最高分持久化。
- `Assets/Scripts/RuntimeUi.cs`：运行时 uGUI 创建工具与统一视觉样式。
- `Assets/Scripts/JumpingNinjaConfig.cs`：Inspector 可调参数定义。
- `Assets/Scripts/Gameplay/GameController.cs`：单局状态、HUD、计分、纪录提示、暂停和结算。
- `Assets/Scripts/Gameplay/NinjaController.cs`：Ninja 物理、左右转向和碰撞规则。
- `Assets/Art/Ninja/ninja-head.png`：透明背景的红色方形忍者头像。
- `Assets/Art/World/ninja-background-pattern.png`：低对比度忍者主题背景纹样。
- `Assets/Art/World/hazard-block.png`：用于黑块与致死地板的深色忍者方块纹理。
- `Assets/Art/World/safe-wall-block.png`：与黑块同系列、用于安全边墙的浅色忍者方块纹理。
- `Assets/Scripts/Gameplay/WorldGenerator.cs`：无限分层地图、墙、缺口及障碍生成。
- `Assets/Scripts/Gameplay/CameraFollower.cs`：竖屏自适应镜头跟随和边界限制。
- `Assets/Resources/JumpingNinjaConfig.asset`：可直接在 Inspector 修改的 V1 参数资产。
- `Assets/Editor/V1ProjectSetup.cs`：Android 项目设置及轻量验证命令。

## Inspector 可调参数

在 `Assets/Resources/JumpingNinjaConfig.asset` 中可调整：

- 左右跳跃角度 `steeringAngle`（默认 15°）
- 跳跃速度、重力倍率、Ninja 视觉尺寸和碰撞箱比例 `playerColliderScale`（默认 `0.82`）
- Ninja 头像、跳跃动画时长/拉伸幅度、死亡动画时长/震动幅度
- 地图宽度、层高、初始 Y 坐标和镜头横向范围
- 背景、黑块及白墙 Sprite、背景纹样透明度、主题切换层数，以及背景和黑块的主题色组
- 预生成层数
- 地图随机种子：`0` 表示每局随机，非零值表示生成可复现地图
- 加载时间、提示持续时间和主要颜色

## Android 设置

- 方向：仅 Portrait
- Application ID：`com.potatoedmice.jumpingninja`
- 最低 Android API：26
- 架构：ARM64
- Scripting Backend：IL2CPP
- Product Name：Jumping Ninja
- Company Name：Potatoed Mice
- Version Name：1.0.3
- Android Version Code：3

## 验证记录

- 2026-08-24 Windows 环境检查：仓库唯一的 Git LFS 文件 `Assets/Logos/logo.png` 已完整下载，工作区文件和本地 LFS 对象的 SHA-256 均与指针 OID 一致。
- 已确认项目要求和本机编辑器均为 Unity `6000.5.9f1`；编辑器位于 `G:/Unity/Editor/6000.5.9f1/Editor/Unity.exe`，Unity CLI `1.0.0-beta.5` 可执行。
- 受限命令行环境无法写入 Hub 数据库和 Unity 用户缓存；使用 `--editor-path` 可启动正确版本，但不能用该环境的认证/Hub 状态代表开发者桌面会话。
- Unity CLI 的 `projects info` 可完整识别 `G:/unity/Projects/JumpingNinja`（版本、GUID、URP 和包清单均正常），确认项目结构及 Mac 到 Windows 的迁移内容有效。
- 开发者桌面会话已确认 Unity Personal 为 Assigned、Unlimited；此前 batchmode/GUI 许可失败来自受限进程无法共享 Hub IPC 和访问令牌，不是账户未激活。
- 2026-08-24 忍者动画更新由 Unity `6000.5.9f1` 完成 `Assembly-CSharp.dll` 与 `Assembly-CSharp-Editor.dll` 编译，Tundra 构建和 Mono 域重载成功，日志没有 C# 编译错误。
- 2026-08-24 碰撞修复使用 Unity `6000.5.9f1` 自带 Roslyn 和当前 Bee 响应文件重新编译 `Assembly-CSharp.dll`、`Assembly-CSharp-Editor.dll`，两项均为 0 error；同时确认 Physics2D 层碰撞矩阵全部开启，方块与 Ninja 均为非 Trigger 的 2D Collider。
- 方形忍者 PNG 为 1254×1254 RGBA，四角 alpha 为 0；Unity 配置资产已引用该 Sprite，Android 导入最大尺寸为 1024。
- Unity CLI 在 Android 活动目标下成功导入并编译全部新增代码。
- 编译日志没有 C# error 或 warning。
- `V1ProjectSetup.ValidateV1` 已验证配置资产、Logo 引用、地图/镜头参数、竖屏、Application ID 和 Build Settings 场景。
- 2026-08-28 Unity `6000.5.9f1` 已成功导入忍者背景与黑块 Sprite；Tundra 脚本构建成功，`V1ProjectSetup.ValidateV1` 验证 15 格地图、9 格视野、9 格层高、素材引用及每 10 层主题变化后，以返回码 0 退出。
- 2026-08-28 v1.0.2 更新在已打开的 Unity `6000.5.9f1` 编辑器中成功导入浅色墙砖，Tundra 完成运行时和 Editor 程序集编译；`V1ProjectSetup.ValidateV1` 验证墙砖引用、版本名 `1.0.2` 和 Android Version Code `2` 后通过。移除一次性验证入口后的最终清理编译同样成功。
- 2026-08-29 碰撞重构由 Unity `6000.5.9f1` 两次完成 Tundra 脚本构建：首次完整构建更新 10 项，第二次增量构建更新 1 项，均成功且无 C# 编译错误。批处理在编译后的 Unity 域重载阶段长时间无日志并持续增长内存，因此为保护开发机主动停止，未取得本轮 `V1ProjectSetup.ValidateV1` 的最终成功标记；碰撞箱范围检查已加入该验证方法。
- 2026-08-30 清理损坏的可再生 `Library` 缓存并移除项目未引用的实验包 `com.unity.pipeline` 后，Unity `6000.5.9f1` 完成全量资源导入、ARM64 IL2CPP/NDK 编译和 Gradle `assembleRelease`；增量构建最终以 `Build Finished, Result: Success`、返回码 0 退出。
- `Builds/JumpingNinja-v1.0.3.apk` 的包名为 `com.potatoedmice.jumpingninja`，Version Name 为 `1.0.3`，Version Code 为 `3`，最低 API 为 26；文件大小 39,462,249 字节，SHA-256 为 `2886FC0E9D1280A23186070C214DAEEDF264877B9E1B82FD7C6AC45B78E13A0C`。
- APK 签名结构已通过 `apksigner` 验证（APK Signature Scheme v2）；当前签名证书为 Unity 使用的 Android Debug 证书。
- v1.0.3 已作为正式 Git 标签和 GitHub Release 发布，Release 地址为 `https://github.com/LeonY34/JumpingNinja/releases/tag/v1.0.3`，APK 资源状态为 `uploaded`。
- 2026-08-30 新增 `scripts/release-windows.ps1` 与仓库级 `windows-unity-release` skill；PowerShell 语法解析和 skill 官方校验器均通过，脚本也正确拒绝了覆盖已有 `v1.0.3` Release。
- 脚本已实测发布 `v1.0.3-test` prerelease：标签与 `v1.0.3` 指向同一提交，复用同一个 APK，远端资源状态为 `uploaded`，大小和 SHA-256 与 `v1.0.3` 完全一致；本次未重新构建或执行真机测试。
- 本次修改通过现有 `Assembly-CSharp.csproj` 重新编译，结果为 0 error、0 warning。
- 本次临时 Unity 副本已完成脚本编译阶段，但因 Mac 剩余空间不足未完成全量资源导入；临时生成目录已清理，未生成 APK/AAB。
- 按开发者要求跳过手机安装与真机玩法测试；本次仅执行 Unity 构建、APK 元数据、哈希及签名结构检查。

## 仓库清理

- 已从 Git 移除 `.DS_Store` 和 `.vscode` 个人配置。
- `.gitignore` 已覆盖 macOS/Windows 元数据、VS Code/Rider 配置、Unity `Library/Temp/Obj/Build/Logs/UserSettings`、IDE 工程文件及 APK/AAB 构建产物。

## 后续注意事项

- 背景、安全白墙与致死黑块均已有忍者主题纹理；背景和黑块具备动态配色，但尚无音效或粒子效果。
- 用户及排行榜完全保存在本机，不包含联网账户或云排行榜。
- 当前 Build Settings 仍使用 `Assets/Scenes/SampleScene.unity`；游戏内容由运行时 Bootstrap 构建。
- 切换用户页面当前显示排行榜前 8 个用户，足够 V1 使用；用户规模扩大时应改为滚动列表。
- 当前 v1.0.3 APK 使用 Android Debug 证书签名，可直接下载安装；后续若作为正式长期分发版本，应配置并妥善保存自有 keystore，否则无法保证跨开发机的升级签名一致性。
