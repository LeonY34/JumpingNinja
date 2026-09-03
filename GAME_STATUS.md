# GAME_STATUS.md

此文档由 AI 维护，用于记录 Jumping Ninja 的实现状态、结构和后续注意事项。

## 当前版本

- 游戏版本：v1.0.6
- 最近更新：2026-09-03
- Unity：6000.5.9f1（arm64）
- 当前构建目标：Android、Windows x64
- 状态：在线认证与联网排行榜已合并；v1.0.6 已完成安全审计与发布准备，等待双平台构建和 GitHub Release

## 在线认证与联网排行榜实施版（VPS 已部署）

- 2026-09-01 已将 ASP.NET Core 10 API 与 PostgreSQL 部署到 VPS 的 `/opt/jumping-ninja-auth`；公网地址为 `https://jumpingninja.dukechen.top:9443`，HTTP 80 会跳转到该 HTTPS 地址。VPS 原有占用 443 的服务未改动。
- 本机 Compose 已按计划关停，未删除本地 PostgreSQL 卷；VPS 使用独立的空数据库卷，数据库只加入认证专用 Docker 网络，API 只发布到 VPS 回环地址 `127.0.0.1:15050`。
- API 提供注册、登录、`/me`、云端 Ninja、旧档导入、单 Ninja 最高分、账号聚合排行榜和在线目标；用户名全局唯一且忽略大小写，密码使用 ASP.NET Core Identity 哈希，JWT 签名密钥只来自被 Git 忽略的环境文件。
- 注册和登录按来源 IP 分别限流，Nginx 转发的真实 IP 只对认证专用代理来源生效；Ninja 写入、成绩提交和在线读取按 JWT 账号分桶限流。密码、原始令牌、签名密钥和请求密码均不写入日志。PostgreSQL 使用命名卷保存账户、Ninja、聚合榜和旧档映射。
- Unity 加载页之后进入在线登录页；注册成功自动登录并立即调用 `/me` 校验。令牌只保存在内存，主动登出、令牌过期、程序关闭重开或收到未授权响应后必须重新登录。
- `OnlineNinjaRepository` 按在线账号隔离云端快照、本地缓存、当前 Ninja 和待同步最高分；JWT、密码和令牌过期时间只在内存中保存。旧 `JumpingNinja.Users.v1` 仅作为可选导入源，不会误并入另一个账号。
- 后端 `dotnet test`：26/26 通过；API 与测试项目构建：0 警告、0 错误。现有 Identity 数据库基线登记后增量迁移、空 PostgreSQL 从零迁移和排行榜烟测均通过。
- 公网 `/health` 返回 200 且数据库状态为 `ok`；公网冒烟已验证注册、大小写重复用户名、登录、错误密码、缺失/无效令牌、`/me`、登录限流，以及容器重启后的账户持久性。
- Unity `6000.5.9f1` 批处理脚本编译通过；`V1ProjectSetup.ValidateV1` 退出码为 0，日志包含 `JUMPING_NINJA_V1_VALIDATION_OK`；4 个 Online EditMode 测试全部通过。Windows x64 构建日志包含 `JUMPING_NINJA_WINDOWS_BUILD_OK`，并已生成不含 Burst 调试目录的验证 ZIP。
- 历史 `online-leaderboard` Windows 验证构建曾通过 Unity 6000.5.9f1 导出；其目录和 ZIP 已在 2026-09-03 清理，并由下方 `import-fix` 修复版完全取代。
- Android APK 本轮未生成：本机 Unity 6000.5.9f1 没有安装 `PlaybackEngines/AndroidPlayer` 模块，Unity 返回“无法切换到 Android build target”；仓库中不把旧 APK 冒充本轮构建。
- 用户已于 2026-09-03 授权并完成本地 commit；该提交已合并并推送至 `main`，尚未创建 tag 或 GitHub Release，构建产物不纳入 Git。
- 2026-09-03 已完成生产部署：数据库卷未重建，原有 3 个账号保留；迁移历史包含 `IdentityBaseline` 和 `AddOnlineLeaderboard`。运行镜像为 `sha256:2e31c3ccc1da50e4958693b925c1726147a9a43f94ce7f5c5147cee004750024`，旧镜像保留为 `jumping-ninja-auth-api:pre-leaderboard-20260903`。
- 部署前 PostgreSQL 备份为 `/opt/jumping-ninja-auth/backups/jumpingninja-pre-leaderboard-20260903T123043Z.dump`，SHA-256 为 `E6D94D57B90DC54172D9FB1B939F11C3957FC9FF8489F8154D9ACEEDCBC11660`，权限 600，并已通过 `pg_restore -l` 验证可读。
- 公网健康、完整排行榜流程、容器重启后的登录/成绩/榜单持久性均通过；未认证的新接口返回 401。验证账号已按精确账号 ID 级联删除，正式榜单未留下测试数据。
- 2026-09-03 Import Old Ninjas 修复：导入请求现在在服务端以字符串接收并用 `Guid.TryParse` 兼容旧客户端的 `N` 格式与标准 `D` 格式；非法值返回稳定的 `legacy_profile_invalid`。客户端发送前统一为 `D` 格式，旧缓存比较也按规范化 GUID 处理。
- 导入页未选中条目改为 `Paper` 浅底配 `Ink` 深字，选中后为红底白字并显示 `✓`；导入按钮仅在选中 1 项以上且未超容量时可用。失败或部分成功会保留失败项选择和错误提示，可直接重试，成功映射立即保存。
- 修复版后端已部署：镜像 `sha256:e5cbeef430e7e327622eea5aa084f5502127e3afd9f3af8bcb4a3b8b95198f30`；部署前备份 `/opt/jumping-ninja-auth/backups/jumpingninja-pre-import-fix-20260903T130209Z.dump`，SHA-256 `DA29D3B158AA060FD2755352A7F7482D8688DCC13AC653608277C081EA07BE82`；旧排行榜镜像保留为 `jumping-ninja-auth-api:pre-import-fix-20260903`，未重建 PostgreSQL 卷。
- 修复版 API 测试 27/27 通过；Unity `V1ProjectSetup.ValidateV1` 通过，Online EditMode 测试 7/7 通过。公网临时账号使用同一 GUID 的 `N` 首次导入、`D` 重试、非法 ID 检查均通过，随后按精确用户名清理。
- `Builds/JumpingNinja-import-fix-Windows/JumpingNinja.exe` 已由 Unity `6000.5.9f1` 成功导出，构建总大小 117,959,561 字节；`Builds/JumpingNinja-import-fix-Windows.zip` 大小 44,973,390 字节，SHA-256 `90E0E2C6BA46459396BB538313CC291742EC1B285E039FE0F07F30D7EDC7083D`，已排除 Burst 调试目录。由于本次桌面 Computer Use 桥接未配置，未能在本机自动完成登录后导入页的点击式 GUI 验收；代码路径、Unity 测试和公网 N/D 烟测均已完成。
- 2026-09-03 仓库清理：保留最新 `import-fix` EXE/ZIP 与正式 `v1.0.5` Windows 构建；删除已被取代的 `auth-test`、`auth-vps-test`、`online-leaderboard` 构建，以及 `Temp/`、`Logs/`、服务端 `bin/obj` 和最新构建中的 `BurstDebugInformation_DoNotShip`。这些内容均为可再生测试或中间产物，约释放 4.0 GB。
- `.gitignore` 已重写并覆盖 Unity 缓存/构建、.NET 输出/测试结果、本地环境变量、密钥/签名材料、IDE 与操作系统文件；Unity `.meta`、服务端项目/迁移、测试和项目文档继续保持可提交，但本地代码记忆 `docs/ai/INDEX.md` 按要求不进入 Git。

## V1 已实现功能

- 进入游戏后显示带 Potatoed Mice Logo 的加载页。
- 首次运行要求创建本地 Ninja 用户，仅需用户名。
- 本地用户表、当前用户和最高分使用 `PlayerPrefs` 保存，存储键为 `JumpingNinja.Users.v1`。
- 主菜单包含开始游戏、排行榜和切换用户。
- 主菜单、排行榜、切换用户、暂停与结算画面会自动选中常用按钮；方向键可移动选择，Enter 可触发选中按钮，并保留鼠标点击和手机触控。
- 排行榜按最高分降序显示；切换用户页支持创建新用户。
- Ninja 视觉保持 1×1 红色方形忍者头像，物理碰撞箱默认缩为视觉范围的 82%，始终受 2D 重力影响。
- 每次向左或向右跳跃时，忍者头像会播放压缩、拉伸、位移和方向倾斜动画；动画只影响视觉子对象，不改变碰撞体。
- 死亡时忍者头像会震动、弹起、旋转、缩小并淡出，动画结束后才显示结算界面。
- 点击左/右半屏，或按下 `A` / `D`、左 / 右方向键，会把 Ninja 速度重设为偏离竖直方向指定角度的左上/右上速度。
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
- `scripts/release-windows.ps1`：Windows 下一键完成 Android 与 Windows 双平台构建、Git 标签和 GitHub Release 发布。
- `.agents/skills/windows-unity-release/SKILL.md`：仓库级 Windows Release 操作说明。
- `ReleaseNotes/`：随仓库维护的各版本 GitHub Release Notes。
- `Assets/Scripts/GameBootstrap.cs`：场景加载后自动建立 V1 游戏入口。
- `Assets/Scripts/GameApp.cs`：在线登录/注册/登出身份门禁，以及原有本地用户、主菜单、排行榜、切换用户和游戏流程切换。
- `Assets/Scripts/Online/OnlineModels.cs`：在线认证、云端 Ninja、排行榜/目标 JSON 模型、账号隔离缓存和待同步成绩队列。
- `Assets/Scripts/Online/AuthApiClient.cs`：UnityWebRequest 注册、登录、`/me`、Ninja、成绩、排行榜和目标请求及统一错误处理。
- `Assets/Scripts/Online/LegacyUserModels.cs`：旧 `JumpingNinja.Users.v1` 的只读导入模型。
- `Assets/Scripts/Online/JumpingNinja.Online.asmdef`：在线运行时程序集边界。
- `Assets/Tests/EditMode/OnlineNinjaRepositoryTests.cs`：云端快照合并、账号隔离、待同步成绩和纪录判断测试。
- `Assets/Scripts/UserRepository.cs`：本地旧用户表和最高分持久化，作为迁移源而非在线权威数据。
- `Assets/Scripts/RuntimeUi.cs`：运行时 uGUI 创建工具、统一视觉样式和密码输入框。
- `Assets/Scripts/PortraitViewport.cs`：Windows 可缩放窗口下的 9:16 相机视口与黑边适配。
- `Assets/Scripts/JumpingNinjaConfig.cs`：Inspector 可调参数定义。
- `Server/JumpingNinja.Api/`：ASP.NET Core 10 认证 API、Identity、JWT 和限流。
- `Server/JumpingNinja.Api/Data/`：`NinjaProfiles`、`AccountLeaderboardEntries`、`LegacyNinjaImports` 数据模型。
- `Server/JumpingNinja.Api/Migrations/`：`IdentityBaseline` 与 `AddOnlineLeaderboard` 增量迁移。
- `Server/JumpingNinja.Api/Leaderboard/`：排行榜 DTO、规则、事务服务和 API 端点。
- `Server/JumpingNinja.Tests/`：认证、排行榜、归属、导入、聚合、JWT 和限流自动化测试。
- `Server/docker-compose.yml`、`Server/Dockerfile`：本地 API/PostgreSQL 容器编排和镜像构建。
- `Server/docker-compose.vps.yml`：VPS Production API/PostgreSQL 编排；VPS 环境文件只保存在服务器，不进入仓库。
- `Server/jumpingninja.dukechen.top.nginx.conf`：公网域名的 Nginx HTTP 跳转、HTTPS 终止和认证 API 反向代理配置模板。
- `Server/verify-auth.ps1`：健康检查、注册/登录/身份校验/限流烟测，可选验证容器重启后的账户持久性。
- `Server/verify-leaderboard.ps1`：创建 Ninja、提交成绩、聚合榜、目标、重启持久性烟测。
- `Server/baseline-existing-database.sql`：现有 EnsureCreated Identity 数据库的安全基线登记脚本。
- `Assets/Scripts/Gameplay/GameController.cs`：单局状态、HUD、计分、纪录提示、暂停和结算。
- `Assets/Scripts/Gameplay/NinjaController.cs`：Ninja 物理、左右转向和碰撞规则。
- `Assets/Art/Ninja/ninja-head.png`：透明背景的红色方形忍者头像。
- `Assets/Art/World/ninja-background-pattern.png`：低对比度忍者主题背景纹样。
- `Assets/Art/World/hazard-block.png`：用于黑块与致死地板的深色忍者方块纹理。
- `Assets/Art/World/safe-wall-block.png`：与黑块同系列、用于安全边墙的浅色忍者方块纹理。
- `Assets/Scripts/Gameplay/WorldGenerator.cs`：无限分层地图、墙、缺口及障碍生成。
- `Assets/Scripts/Gameplay/CameraFollower.cs`：竖屏自适应镜头跟随和边界限制。
- `Assets/Resources/JumpingNinjaConfig.asset`：可直接在 Inspector 修改的 V1 参数资产。
- `Assets/Editor/V1ProjectSetup.cs`：Android 与 Windows 项目设置及轻量验证命令。
- `Assets/Editor/WindowsReleaseBuilder.cs`：Windows x64 Player 命令行构建入口。

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
- 在线认证 API 地址 `authApiBaseUrl`（当前为 `https://jumpingninja.dukechen.top:9443`）和请求超时 `authRequestTimeoutSeconds`（默认 10 秒）

## Android 设置

- 方向：仅 Portrait
- Application ID：`com.potatoedmice.jumpingninja`
- 最低 Android API：26
- 架构：ARM64
- Scripting Backend：IL2CPP
- Product Name：Jumping Ninja
- Company Name：Potatoed Mice
- Version Name：1.0.6
- Android Version Code：6

## Windows 设置

- 架构：x64
- 显示模式：默认 540×960 的可缩放窗口；游戏内容始终保持 9:16，多余区域以黑色补齐
- 分发格式：包含完整 Player 文件的 ZIP

## 验证记录

- 2026-09-03 v1.0.6 发布前安全检查：当前 Git 历史与跟踪文件未发现真实 `.env`、私钥、keystore 或 GitHub token；客户端 JWT 仅保存在内存，不写入 `PlayerPrefs`。公网 `/health` 返回 200 且数据库为 `ok`，TLS 证书有效期至 2026-11-30，HSTS 已启用。
- 使用微软官方 .NET SDK `10.0.400`（SHA-512 校验通过）重新构建并测试后端，27/27 测试通过；NuGet 直接与传递依赖的已知漏洞扫描结果为 0。ASP.NET Core / EF Core 已更新到 `10.0.11`，Npgsql 更新到 `10.0.3`，测试运行器更新到 `3.1.5`。
- 仓库中的生产容器配置已固定到 .NET SDK `10.0.400` / Runtime `10.0.11` 并切换为非 root 用户；Nginx 模板补充隐藏版本、`nosniff`、无 Referrer 和 `no-store`。这些服务器配置修改尚未部署到 VPS，当前线上服务仍为上一部署版本。
- 排行榜写入严格校验 JWT 账号与 Ninja 归属，并有限流、非负校验及数据库约束；但游戏客户端本身不可信，当前协议无法证明分数来自真实一局，因此修改客户端的用户仍可伪造高分。这是当前已知的反作弊风险，不属于凭据泄露或越权访问。
- 2026-09-03 `origin/codex/duke-chen` 基于当时 `main` 单提交前进，已使用 `--ff-only` 无冲突合并；Unity `6000.5.9f1` 完成全部脚本编译，新增 EditMode 测试通过 7/7。服务端测试项目要求 .NET 10 SDK，本机仅有 Unity 附带的 .NET 8 SDK且无 Docker，因此本轮未重复运行服务端测试。
- 2026-08-30 v1.0.5 使用 Unity `6000.5.9f1` 完成脚本编译与 `V1ProjectSetup.ValidateV1` 校验，日志包含 `JUMPING_NINJA_V1_VALIDATION_OK`，无 C# 编译错误；Android 与 StandaloneWindows64 构建均成功。
- `Builds/JumpingNinja-v1.0.5.apk` 大小为 39,438,533 字节，SHA-256 为 `9449BE93740B073E86CB33A5B665F873FDC981D99490B38ACDF9026A30EF7EA9`；包名、Version Name、Version Code、最低/目标 API 分别为 `com.potatoedmice.jumpingninja`、`1.0.5`、`5`、`26`/`36`，APK Signature Scheme v2 验证通过，证书仍为 Android Debug。
- `JumpingNinja-v1.0.5-Windows.zip` 已排除 Unity 的 `BurstDebugInformation_DoNotShip` 目录，大小为 43,903,184 字节，SHA-256 为 `61086137779122A926E603C0191FAD193AD2DE106D6C7627C4D55B82B9A9779A`；ZIP 包含 EXE、Data、Mono 运行时及所需 DLL，EXE 未进行代码签名。
- v1.0.5 已发布至 `https://github.com/LeonY34/JumpingNinja/releases/tag/v1.0.5`，Android 与 Windows 两个远端资源状态均为 `uploaded`；本次执行 Unity 编译、配置校验、双平台构建与静态产物检查，未执行手机安装、真机玩法或 Windows GUI 玩法测试。
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
- 2026-09-01 在线认证工作区后端测试通过 19/19，VPS 公网健康检查、认证流程、限流和 Compose 重启持久化烟测通过；本机认证 Compose 保持停止且本地数据库卷未删除。
- 2026-09-01 Unity `6000.5.9f1` 完成在线认证版脚本编译与 `V1ProjectSetup.ValidateV1` 校验，Windows x64 构建日志包含 `JUMPING_NINJA_WINDOWS_BUILD_OK`，无 C# 编译/构建错误；GUI 冒烟覆盖注册自动登录、主动登出、再次登录和程序重启要求登录。
- 2026-09-01 曾生成 `Builds/JumpingNinja-auth-vps-test-Windows.zip`：44,954,591 字节，SHA-256 为 `E478D5591A7E2B1ACA00069D68E2CA8E87392DACFAB94BDB6E74C076BCF35801`；该测试构建已在 2026-09-03 清理。
- 2026-09-02 联网排行榜实施版验证：后端测试 26/26；现有 Identity 数据库执行基线登记加 `AddOnlineLeaderboard` 迁移、空 PostgreSQL 从零迁移和容器重启持久性烟测均通过；Unity 6000.5.9f1 脚本编译、`V1ProjectSetup.ValidateV1` 和 4 个 Online EditMode 测试均通过。
- 2026-09-02 曾在一次性 `Temp/BuildVerification-2310` 中生成排行榜验证 ZIP；该验证目录已在 2026-09-03 清理。AndroidPlayer 模块缺失，因此本轮未生成 APK。
- `Builds/JumpingNinja-v1.0.3.apk` 的包名为 `com.potatoedmice.jumpingninja`，Version Name 为 `1.0.3`，Version Code 为 `3`，最低 API 为 26；文件大小 39,462,249 字节，SHA-256 为 `2886FC0E9D1280A23186070C214DAEEDF264877B9E1B82FD7C6AC45B78E13A0C`。
- APK 签名结构已通过 `apksigner` 验证（APK Signature Scheme v2）；当前签名证书为 Unity 使用的 Android Debug 证书。
- v1.0.3 已作为正式 Git 标签和 GitHub Release 发布，Release 地址为 `https://github.com/LeonY34/JumpingNinja/releases/tag/v1.0.3`，APK 资源状态为 `uploaded`。
- 2026-08-30 新增 `scripts/release-windows.ps1` 与仓库级 `windows-unity-release` skill；PowerShell 语法解析和 skill 官方校验器均通过，脚本也正确拒绝了覆盖已有 `v1.0.3` Release。
- 脚本已实测发布 `v1.0.3-test` prerelease：标签与 `v1.0.3` 指向同一提交，复用同一个 APK，远端资源状态为 `uploaded`，大小和 SHA-256 与 `v1.0.3` 完全一致；本次未重新构建或执行真机测试。
- 2026-08-30 v1.0.4 的键盘输入代码已通过 Android 和 StandaloneWindows64 两个平台的 Unity 脚本编译；Android 构建以 `Build Finished, Result: Success` 完成，Windows 构建同时写出 `JUMPING_NINJA_WINDOWS_BUILD_OK` 成功标记。
- `Builds/JumpingNinja-v1.0.4.apk` 大小为 39,467,465 字节，SHA-256 为 `3AF41F4C7E86D355AC76D5E23BD6D41C08A2827F91C82893CC9B03929B9704C8`；包名、Version Name、Version Code、最低/目标 API 分别为 `com.potatoedmice.jumpingninja`、`1.0.4`、`4`、`26`/`36`，APK Signature Scheme v2 验证通过，证书仍为 Android Debug。
- `JumpingNinja-v1.0.4-Windows.zip` 已排除 Unity 的 `BurstDebugInformation_DoNotShip` 目录，大小为 43,901,695 字节，SHA-256 为 `B15B7C4E26271DD7E940B881F8FB5827555AA7A3F721E98DCCCC2074851EF54E`；ZIP 包含 EXE、Data、Mono 运行时及所需 DLL，EXE 未进行代码签名。
- v1.0.4 已发布至 `https://github.com/LeonY34/JumpingNinja/releases/tag/v1.0.4`，Android 与 Windows 两个远端资源状态均为 `uploaded`；本次执行构建与静态产物检查，未执行手机安装、真机玩法或 Windows GUI 玩法测试。
- 本次修改通过现有 `Assembly-CSharp.csproj` 重新编译，结果为 0 error、0 warning。
- 本次临时 Unity 副本已完成脚本编译阶段，但因 Mac 剩余空间不足未完成全量资源导入；临时生成目录已清理，未生成 APK/AAB。
- 按开发者要求跳过手机安装与真机玩法测试；本次仅执行 Unity 构建、APK 元数据、哈希及签名结构检查。

## 仓库清理

- 已从 Git 移除 `.DS_Store` 和 `.vscode` 个人配置。
- `.gitignore` 已覆盖 macOS/Windows 元数据、IDE 配置、Unity `Library/Temp/Obj/Builds/Logs/UserSettings`、.NET `bin/obj/TestResults`、本地 `.env`/密钥与 APK/AAB 等构建产物；`.env.example` 和服务端源码项目仍可跟踪。

## 后续注意事项

- 背景、安全白墙与致死黑块均已有忍者主题纹理；背景和黑块具备动态配色，但尚无音效或粒子效果。
- 旧 `JumpingNinja.Users.v1` 仍保存在设备并作为只读导入源；登录后的在线 Ninja、账号最高分和联网排行榜以云端快照为权威，本地只保留按账号分区的非敏感缓存与待同步最高分。JWT 不写入 `PlayerPrefs` 或本地 JSON。
- Windows 构建前需在 Unity Hub 手动登录可用 Unity 账户；Unity Hub 的认证窗口不得由自动化脚本代操作。
- 当前 Build Settings 仍使用 `Assets/Scenes/SampleScene.unity`；游戏内容由运行时 Bootstrap 构建。
- 在线切换 Ninja 页面使用滚动列表并显示 `数量 / 20`、云端最佳分和待同步标记；联网排行榜使用滚动行、当前账号高亮及前 100 名之外的 `YOUR RANK`。
- 当前 v1.0.6 APK 计划继续使用 Android Debug 证书签名，Windows EXE 未进行代码签名；后续若作为正式长期分发版本，应配置并妥善保存 Android keystore，并考虑为 Windows 程序配置代码签名证书。
- 本地后端当前可按需运行；如需本地回归测试，在 `Server` 目录执行 `docker compose --env-file .env.local up -d --build`，再运行 `Invoke-WebRequest http://127.0.0.1:5050/health`、`.\verify-auth.ps1` 和 `.\verify-leaderboard.ps1 -VerifyPersistence`。VPS 公网健康地址为 `https://jumpingninja.dukechen.top:9443/health`；生产环境停止时不要附加 `-v`，避免删除账户卷。
- 当前公网 VPS 已运行联网排行榜 API；后续部署继续保留 `backups/` 中的数据库备份与旧镜像标签，禁止使用 `docker compose down -v`。
