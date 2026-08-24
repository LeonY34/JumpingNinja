# GAME_STATUS.md

此文档由 AI 维护，用于记录 Jumping Ninja 的实现状态、结构和后续注意事项。

## 当前版本

- 游戏版本：V1
- 完成日期：2026-08-23
- Unity：6000.5.9f1（arm64）
- 当前构建目标：Android
- 状态：核心玩法与菜单流程已实现；方形忍者头像、跳跃动画和死亡动画已接入，最新脚本编译通过

## V1 已实现功能

- 进入游戏后显示带 Potatoed Mice Logo 的加载页。
- 首次运行要求创建本地 Ninja 用户，仅需用户名。
- 本地用户表、当前用户和最高分使用 `PlayerPrefs` 保存，存储键为 `JumpingNinja.Users.v1`。
- 主菜单包含开始游戏、排行榜和切换用户。
- 排行榜按最高分降序显示；切换用户页支持创建新用户。
- Ninja 保持 1×1 方块碰撞体，视觉使用红色方形忍者头像，始终受 2D 重力影响。
- 每次向左或向右跳跃时，忍者头像会播放压缩、拉伸、位移和方向倾斜动画；动画只影响视觉子对象，不改变碰撞体。
- 死亡时忍者头像会震动、弹起、旋转、缩小并淡出，动画结束后才显示结算界面。
- 点击左/右半屏会把 Ninja 速度重设为偏离竖直方向指定角度的左上/右上速度。
- 地图默认宽 25 格，左右边缘由 1×1 白色墙块组成；碰墙后横向速度归零，纵向速度保留。
- 黑色方块会立即结束本局；第 0 层下方为完整致死地板。所有方块使用与视觉缩放分离的显式尺寸实体碰撞体；Ninja 除碰撞回调外还会在每个物理帧做 Collider 形状扫掠，防止高速跳跃穿透黑块或白墙。
- 无限地图按默认 15 格高度动态向上生成。层间有 1–2 个随机的 1×3 缺口，缺口周围不会生成额外障碍。
- 第 1–5 层无额外障碍，第 6–10 层每层 1 个，第 11–20 层每层 2 个，之后每十层递增。
- 镜头默认显示 15 格横向范围，纵向范围按设备宽高比计算；跟随 Ninja 并限制在地图左右和底部边界内。
- Ninja 默认出生 Y 坐标为 7.5，位于 15 格高的第 0 层中央，避免与第一层边界重叠。
- 左上角显示当前等级及下一个可超越的用户分数，包括当前用户自己的历史纪录。
- 超越其他用户或刷新个人纪录时显示限时提示。
- 暂停菜单支持返回主菜单；恢复游戏前显示完整 3 秒倒计时。
- 死亡后以本局到达的最高层数结算，可重试或返回主菜单。

## 主要文件结构

- `Assets/Scripts/GameBootstrap.cs`：场景加载后自动建立 V1 游戏入口。
- `Assets/Scripts/GameApp.cs`：加载页、用户创建、主菜单、排行榜、切换用户和游戏流程切换。
- `Assets/Scripts/UserRepository.cs`：本地用户表和最高分持久化。
- `Assets/Scripts/RuntimeUi.cs`：运行时 uGUI 创建工具与统一视觉样式。
- `Assets/Scripts/JumpingNinjaConfig.cs`：Inspector 可调参数定义。
- `Assets/Scripts/Gameplay/GameController.cs`：单局状态、HUD、计分、纪录提示、暂停和结算。
- `Assets/Scripts/Gameplay/NinjaController.cs`：Ninja 物理、左右转向和碰撞规则。
- `Assets/Art/Ninja/ninja-head.png`：透明背景的红色方形忍者头像。
- `Assets/Scripts/Gameplay/WorldGenerator.cs`：无限分层地图、墙、缺口及障碍生成。
- `Assets/Scripts/Gameplay/CameraFollower.cs`：竖屏自适应镜头跟随和边界限制。
- `Assets/Resources/JumpingNinjaConfig.asset`：可直接在 Inspector 修改的 V1 参数资产。
- `Assets/Editor/V1ProjectSetup.cs`：Android 项目设置及轻量验证命令。

## Inspector 可调参数

在 `Assets/Resources/JumpingNinjaConfig.asset` 中可调整：

- 左右跳跃角度 `steeringAngle`（默认 15°）
- 跳跃速度、重力倍率和 Ninja 尺寸
- Ninja 头像、跳跃动画时长/拉伸幅度、死亡动画时长/震动幅度
- 地图宽度、层高、初始 Y 坐标和镜头横向范围
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
- 本次修改通过现有 `Assembly-CSharp.csproj` 重新编译，结果为 0 error、0 warning。
- 本次临时 Unity 副本已完成脚本编译阶段，但因 Mac 剩余空间不足未完成全量资源导入；临时生成目录已清理，未生成 APK/AAB。
- 按开发者要求没有进行大量测试，也没有生成 APK；尚未做真机触控、性能和发布签名验证。

## 仓库清理

- 已从 Git 移除 `.DS_Store` 和 `.vscode` 个人配置。
- `.gitignore` 已覆盖 macOS/Windows 元数据、VS Code/Rider 配置、Unity `Library/Temp/Obj/Build/Logs/UserSettings`、IDE 工程文件及 APK/AAB 构建产物。

## 后续注意事项

- 地图仍使用纯色方块；角色已有方形忍者头像及程序化跳跃/死亡动画，但尚无音效或粒子效果。
- 用户及排行榜完全保存在本机，不包含联网账户或云排行榜。
- 当前 Build Settings 仍使用 `Assets/Scenes/SampleScene.unity`；游戏内容由运行时 Bootstrap 构建。
- 切换用户页面当前显示排行榜前 8 个用户，足够 V1 使用；用户规模扩大时应改为滚动列表。
- 发布 Android Release 前需要配置正式 keystore，并执行至少一次真机 APK/AAB 构建测试。
