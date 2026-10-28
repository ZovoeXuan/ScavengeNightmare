# 开发日志

> 格式：每天三行——做了什么 / 验收是否通过 / 遗留问题。遗留问题写进次日提示词开头。

## D1 · 2026-10-27 · 工程脚手架

- **做了什么**：用 Unity 6000.3.27f1 的 3D URP 模板建立 `D:\drlj\game`；生成 `Assets/_Game/{Scripts,Scenes,Data,Art,Audio,UI}`；创建 `Main.unity`（Main Camera + Directional Light + URP Camera Data）并登记到 Build Settings；配置 Git + LFS + `.gitignore` + README。
- **验收**：通过。`D1_VALIDATE_OK pipeline=PC_RPAsset scene=Assets/_Game/Scenes/Main.unity`；Unity 批处理退出码 0；空场景可打开。
- **遗留问题**：
  1. 本机 Git 未配置 `user.name` / `user.email`，本次提交使用临时身份 `Qoder <qoder@local>`，未写入全局配置；首次推送 GitHub 前建议替换为本人身份。
  2. ~~里程碑 M4/M5 写“3 图 5 怪”，与范围冻结清单冲突~~ → **已关闭（2026-10-27）**：冻结清单定稿改为「M4 = 1 图 2 怪 + 10 事件 + 稳定性」，2/3 号图延至 M5 之后。
  3. ~~冻结清单未显式列出“基础 HUD / 大厅 UI / 微恐氛围”~~ → **已关闭（2026-10-27）**：已补入 P0 第 11/12/13 条（大厅入口 D2 / 结算界面 D16 / 功能 HUD D17 / 定调美化 D32 / 灯光 D29 / 音效 D31）。

## D2 · 2026-10-27 · Steam 大厅

- **做了什么**：接入 Facepunch.Steamworks 2.5.2（Win64 + `steam_api64.dll`，AppID 480）；实现 `SteamLobbyService`（初始化 / 创建 4 人好友大厅 / 按 ID 加入 / Steam 邀请面板 / 离开 / 房主与成员状态）；添加 `LobbyDebugUI` 临时面板并挂到 `Main` 场景；新增 `D2SceneSetup`、`D2SteamSmokeTest`、`BuildWindows`。
- **验收**：通过（自动化部分）。`D2_VALIDATE_OK`；冒烟测试真实创建大厅 `lobby=109775242490028284`、`1/4`、`host=True` 后正常离开，日志 `D2_SMOKE_OK`；Windows 构建成功且 `steam_api64.dll` / `Facepunch.Steamworks.Win64.dll` 均进包；构建包以 `-batchmode` 运行确认 Steam 初始化成功（账号 `撤步哈登`）。
- **人工验收**：2026-10-27 通过——双客户端经 Steam 好友邀请进入同一大厅，成员数与房主标记显示正确。D2 全项关闭。
- **遗留问题**：
  1. `JoinLobbyAsync` 失败原因只有通用提示，D9 做错误细分与重连。
  2. Steam 覆盖层邀请面板已人工验证；批处理环境仍无法覆盖，后续回归以人工为主。
  3. 开发 AppID 480 与 `steam_appid.txt` 不能进入正式商店包，发布前替换。
  4. 本机 Git 仍未配置身份，D2 继续使用临时身份 `Qoder <qoder@local>`。

## D3 · 2026-10-27 · 联机骨架

- **做了什么**：引入 Mirror v96.11.3（`Assets/Plugins/Mirror`，剔除 Examples/Tests）；接 `kcp2k.KcpTransport`（7777）；生成 `Assets/_Game/Prefabs/Player.prefab`（CharacterController + NetworkIdentity + NetworkTransformReliable + PlayerMotor + 子相机）；`Main` 场景补 `Ground`（200m）+ 4 个 `NetworkStartPosition` + `NetworkManager`（4 人、RoundRobin 生成、KcpTransport、NetDebugHUD、D3AutoRunner）；实现新输入系统 WASD + 鼠标视角；HUD 显示 RTT / 连接数 / 本地玩家坐标；新增 `Docs/Networking.md`。
- **验收**：通过。`D3_SETUP_OK` / `D3_VALIDATE_OK transport=kcp maxConnections=4 spawnPoints=4` / `D3_ASSET_ID assetId=1675433747`；Windows 构建 `BUILD_OK`；1 服务端 + 4 客户端并行实测 `conns=4 players=4`，四个出生点不重叠、四条轨迹互不干扰、位移速率 3.2 m/s 与 `walkSpeed` 一致、RTT 16-33ms、全程无掉线与崩溃。
- **遗留问题**：
  1. **未接 Steam P2P**：当前只有 KCP 直连 / 局域网。Mirror 官方 `KcpTransport` 不支持 Steam 好友直连，需额外接 Steam 传输层（FizzySteamworks 等）。D9 评估，若要「大厅邀请即自动进游戏」必须补。
  2. **GUI 4 窗口观感未验**：抖动、滑步、镜头平顺度只能在真实窗口下判断，清单见 `Docs/Networking.md` 末节。
  3. **移动参数仍硬编码在 `PlayerMotor` 字段**：D5 迁 ScriptableObject。
  4. **`D3AutoRunner` 是测试外壳**：正式发布前移除或加 `#if !UNITY_EDITOR` 开关。
  5. 本机 Git 仍未配置身份，D3 继续使用临时身份 `Qoder <qoder@local>`。

## D4 · 2026-10-27 · 物理抓取 v0

- **做了什么**：新增 `PropPhysicsProfile`（质量 / 摩擦 / 弹性 / 抓握加速度 / 投掷冲量）、4 个物件物理档与对应 prefab（罐头 2kg / 纸箱 9kg / 圆桶 22kg / 雕像 42kg）；`GrabbableProp` 负责应用物理档 + 准星高亮 + 速度兜底；`GrabController` 实现「射线拾取 → 速度控制跟随（不 SetParent）→ 松手投掷继承玩家速度 → 超距强制脱手」；负重按质量降低移动速度；`GrabHUD` 提供准星与重量提示；场景加 `PropPen`（8 个可抓物件）与 `ThrowWall`；新增 `Docs/Grab.md`。
- **验收**：通过。`D4_SETUP_OK profiles=4 prefabs=4` / `D4_VALIDATE_OK profiles=4 props=8 wall=True`；构建 `BUILD_OK`；`-d4test` 自动化：`D4_GRAB_OK distance=1.70/1.60`、`D4_FOLLOW_OK propMoved=1.45`、`D4_THROW_OK speed=3.18`（= 70/22，验证冲量÷质量）、`D4_TUNNEL_OK closestSide=-0.40`（恰为墙半厚+胶囊半径，30 m/s 未穿模）。
- **遗留问题**：
  1. **手感未经人眼验证**：重量感 / 力度感 / 负重体感只能在真实窗口里判断，排在 D7 朋友局；D5 先按「轻快 / 稳重 / 沉重」出三套参数。
  2. **物件未联网**：`GrabbableProp` 仍是纯本地物理，无 NetworkIdentity 与所有权切换。两人抓同一件会各算各的。排 D6 末或 D9。
  3. **投掷无蓄力、无镜头反馈**：D5 / D10 再补。
  4. `GrabController` 与 `NetDebugHUD` / `LobbyDebugUI` 同属临时 UI/调试件，D32 一并替换。
  5. 本机 Git 身份仍待配置。

## D5 · 2026-10-27 · 手感参数 v0（M0 冻结收尾）

- **做了什么**：新增 `FeelScheme`（倍率型手感方案）+ `FeelSettings`（全局激活点），把抓握、投掷、负重、玩家速度全部改为读方案；新增 `PlayerMoveSettings` 数据表并接入 `PlayerMotor`（关闭 D3 遗留第 3 条）；生成三套方案 `Feel_{light,solid,heavy}`；玩家 prefab 挂 `FeelSwitchHUD`（数字键 1/2/3 热切换，左下角显示方案与笑点侧重）；`-d4test` 支持 `-feel=` 指定方案做对比；新增 `Docs/HandFeel_v0.md`（含三方案数值表、派生结果、笑点场景映射、D7 待验假设）。
- **验收**：通过。`D5_SETUP_OK schemes=3` / `D5_VALIDATE_OK schemes=3 moveSettings=true playerWired=true`；构建 `BUILD_OK`；同一自动化用例切换三方案实测：轻快 4.14 m/s、稳重 3.18 m/s、沉重 1.92 m/s（= 冲量×倍率÷质量，1.30×/1.00×/0.60×），跟手距离 1.71 / 1.70 / 1.85 m，三档撞墙均未穿模。
- **M0 状态**：概念与范围（D1）+ 参数冻结（D5）均已落盘；唯一未完成项是 D7 朋友局后拍板主方案。
- **遗留问题**：
  1. **三套方案的手感本质仍需人验**：自动化只能证明「参数按设计生效」，证明不了「好不好笑」。
  2. 雕像 42kg 在稳重/沉重下都触到负重下限 0.45，两方案搬它一样慢，可能削弱差异 —— D7 观察点。
  3. 方案只作用于本地，不跨客户端同步；若需要全房统一，要在房间配置加字段（未排期）。
  4. 本机 Git 身份仍待配置。

## 维护 · 2026-10-27 · Git 身份修正

- **背景**：D1–D5 五个提交的作者/提交者均为 `Qoder <qoder@local>`（工程搭建时的临时身份）。
- **处理**：配置全局与仓库级 `user.name = ZovoeXuan`、`user.email = asd1358080254@gmail.com`；用 `git filter-branch --env-filter` 重写全部 5 个提交的作者与提交者。
- **校验**：改写前后 HEAD 树哈希相同（`04d0b603…`），确认文件内容零改动，仅元数据变更；工作区干净，1213 个受跟踪文件不变；仓库原有 `refs/original` 备份 ref 已清理。
- **注**：改写发生在配置远程仓库之前，因此无外部影响；此后新提交自动使用新身份。D1/D3/D4/D5 日志中「Git 身份未配置」的遗留项至此全部关闭。

## 维护 · 2026-10-27 · 提交邮箱改为 GitHub 私有地址

- **原因**：提交历史里的真实 Gmail 会公开，且 GitHub 的「Keep my email addresses private」开关不影响已产生的历史提交，必须直接改写提交邮箱才能隐藏。
- **处理**：全局与仓库级 `user.email` 改为 `310389188+ZovoeXuan@users.noreply.github.com`；`user.name` 保持 `ZovoeXuan`；用 `git filter-branch --env-filter` 重写全部 6 个提交的作者与提交者邮箱。
- **校验**：改写前后 HEAD 树哈希一致（`cae58aa9…`），内容零改动；工作区干净；备份 ref 已清理。
- **说明**：GitHub 按邮箱关联账号，私有地址同样计入贡献图；如需回到真实邮箱，改两处 config 后再跑一次同样的命令即可。

## D5 补记 · 2026-10-28 · 主方案选定

- **实测**（真人单机试玩，三方案热切换对比）：最爽 = 轻快；罐头/雕像重量差「明显」；沉重被判「好笑」而非「恶心」；无显著别扭点。
- **决策**：主方案定为 `light`，`FeelSwitchHUD.defaultIndex = 0`；D7 朋友局基线用 light，**对照组改用 heavy**（两极对比信息量大于 light vs solid）。
- **判读**：轻快没有把 D1 的「重量感」洗掉，说明重量差主要由质量与摩擦承载，不靠拖沓感硬撑——这条对后续加速节奏是利好。
- **仍待 D7 核验**：单人「爽」≠ 朋友局「好笑」，必须用「局均笑点 ≥2」验；heavy 在四人局是否变成「互相拖累到烦」。

## D6 · 方案评审件（待确认后实现）

- 新增 `Docs/Friction_D6_Plan.md`：五条摩擦（窄门卡位 / 手滑砸脚 / 滚动体 / 夹手 / 易碎）的触发条件、判定数据、反馈、验收与风险。
- **关键发现**：现有 4 个物件全长宽小于玩家胶囊（直径 1.0m），无论怎么抱都能穿过任何玩家可通行的门洞 ——「窄门卡位」缺物理前提。方案里提出新增「大衣柜 1.6m」与「玻璃花瓶」，需用户拍板。
- **技术债**：圆桶目前用 CapsuleCollider（Unity Cylinder 图元自带），滚动像胶囊不像桶，需换 Convex MeshCollider，会导致 D4 撞墙基线需重测。
- 状态：**未动代码**，等三项确认（新增物件 / 换碰撞体 / 是否接数值结算）。

## D6 · 2026-10-28 · 搬运摩擦前 5 条

- **做了什么**：新增 `FrictionTuning`（5 条开关 + 全部阈值）、`FrictionEvents`（统一日志/屏幕提示/程序合成占位音）、`FrictionHUD`；实现 `NarrowDoorWatcher`（位移判卡位）、`RollingProp`、`FragileProp`（相对速度判碎、碎块生成、价值归零）、`DoorTrap`（开合状态机 + 闭合阶段夹取推出 + E 键交互）；`GrabbableProp` 增加持有者与「被撞硬直」判定；`PlayerMotor` 增加 `Stun`；`PropPhysicsProfile` 增加 `isFragile/value/breakSpeed`；新增大衣柜与玻璃花瓶两类物件；圆桶碰撞体改 Convex MeshCollider；场景补 `NarrowDoor` / `SwingDoor` / `FrictionProps`；新增 `D6AutoTest` 与 `D6FrictionSetup`。
- **验收**：通过。`D6_SETUP_OK` / `D6_VALIDATE_OK tuning=1 barrel=convex vase=fragile cabinet=1 narrowDoor=1 swingDoor=1`；构建 `BUILD_OK`；`-d6test` 连跑两次结果一致：滚动 3.88m（进入滚动态）、22kg 圆桶 10.4 m/s 命中硬直 1.0s、花瓶 8.3 m/s 碎裂且价值归零、门夹强制脱手、大衣柜卡 1.2m 门洞有反馈。D4 基线复测 `closestSide=-0.40` 与换碰撞体前一致。
- **遗留问题**：
  1. **物件仍未联网**（最高优先级，见下）：四个客户端各算各的物理，联机时位置与碎裂结果不一致 —— **D7 是 2-3 人联机实测，不补这条 D7 无法进行**。
  2. 摩擦事件（卡位提示、硬直、碎裂）未跨端同步。
  3. 音效为程序合成占位、HUD 为临时件，D31/D32 替换。
  4. 门只有 E 键开关，无交互提示与开关动画。

## D6.5 · 2026-10-28 · 物件联网（半成品，同步质量不达标）

- **已完成**：
  1. 6 个物件 prefab 全部加上 `NetworkIdentity` + `NetworkTransformReliable` + `NetworkedProp`，assetId 已落盘，并注册进 `NetworkManager.spawnPrefabs`。
  2. 放弃「场景里摆好的实例直接同步」——Mirror 要求场景对象有 editor 期分配的 sceneId，prefab 实例拿不到（实测 netId 恒为 0，客户端还会在 `NetworkClient.Broadcast` 每帧空引用）。改为 `PropSpawner` 在服务端开局从 prefab 实例化并 `NetworkServer.Spawn`，11 个物件正常生成（`D65_SPAWNED count=11`）。
  3. 所有权模型跑通：出生交给房主，抓取时转给抓的人，松手交回房主；非持有端刚体设为 kinematic。双端 netId 一致、初始位置一致、客户端 `kinematic=True` 符合预期。
- **未达标（阻塞 D7）**：位移同步严重滞后。同一物件同一时刻，主机在 x=13.3m，客户端才到 0.9m（同索引位置差均值 8.67m），表现为客户端极慢地追赶。已排除：连接失败、netId 不一致、初始位置不同步。
- **根因分析**：`syncDirection` 在运行时按「谁持有」切换（房主持有 ServerToClient / 远程客户端持有 ClientToServer）。切换后 NetworkTransformReliable 的可靠基线会重置，插值按旧快照慢慢爬。
- **下一步方案（未实施）**：改为**服务端权威**——抓取走 Command（请求抓取 → 服务端沿玩家朝向做速度控制 → 投掷由服务端施加冲量），`NetworkTransform` 保持默认 ServerToClient 不再运行时切换。改动集中在 `GrabController`，估计 3–4h，能一次性消除同步方向问题。
- **当前代码状态**：可编译、可构建、物件能生成并同步初始状态；抓取后的位移同步不可用。**D7 不能在此状态下开**。

## D6.5 补记 · 2026-10-28 · 改为服务端权威后达标

- **改造**：`GrabController` 重写为服务端权威——客户端只做本地射线高亮与按键上报（Command），服务端在 `FixedUpdate` 里做速度控制与投掷；`NetworkTransform` 固定 `ServerToClient`，运行时不再翻转同步方向；`PlayerMotor` 增加 10Hz 的俯仰同步（服务端据此算准星方向）与 `RpcStun` 广播。
- **测量方法的教训（重要）**：上一轮我按「两端日志的采样序号」对比，得出「同刻位置差 8.67m」的结论，**是错的**——两个进程的 0.5s 采样相位不同（客户端晚连 5s），序号对齐毫无意义。给日志加上统一 UTC 毫秒戳后按墙钟对齐，真实误差是：
  ```
  同刻位置差 均值 0.28m 最大 0.48m
  末值：主机 (10.50, -2.10) / 客户端 (10.50, -2.10) —— 完全一致
  ```
  0.28m ≈ 玩家 3.7m/s 行走时的 75ms 滞后，对合作拾荒可接受。
- **回归**：D6 五条摩擦全部通过（滚动 3.88m / 硬直 1.0s / 碎裂价值归零 / 门夹脱手 / 窄门卡位）。改造中一度丢失 `GrabbableProp.Holder` 导致门夹找不到人脱手（`D6_CLAMP_FAIL`），已补回。
- **已知代价**：远程客户端抓着物件时，他看到的物件是自己服务端位置的延迟回放（约 75–100ms）；本地房主无此问题。D9 可考虑加客户端预测。
- **结论**：D6.5 达标，**D7 可以开**。

## D6.5 人工验收 · 2026-10-28 · 三条全过

- **验收内容**（两个窗口，Host + Client 127.0.0.1）：
  1. 房主抓起圆桶走动，客户端同步跟随（允许轻微拖尾）—— 通过
  2. 客户端抓取物件，房主可见 —— 通过
  3. 摔碎花瓶，两端同时碎 —— 通过
- **结论**：D6.5 从「半成品」转正为「已完成」，物件联网可用于 D7。

## D7 执行件 · 2026-10-28 · 实测记录器 + 执行清单

- **背景**：D7 的执行角色是「人 + 朋友」，AI 不参与实测。这一轮只交付「让实测产出可用数据」的工具与判据。
- **新增 `PlaytestRecorder`**（挂 NetworkManager，服务端生效）：把三类事实落成 CSV——人工标记（笑点/问题/金句）、摩擦事件（订阅 `FrictionEvents.Reported`）、动作计数（抓取/投掷/在线人数心跳）。每 45 秒整表重写防崩溃丢数据；`session_end` 直接给时长、各类计数与每人笑点数。CSV 落在包目录 `PlaytestLogs/session_yyyyMMdd_HHmmss.csv`，UTF-8 BOM 供 Excel 直开。
- **新增 `PlaytestMarker`**（挂玩家 prefab）：F5 笑点 / F6 问题 / F7 金句，走 `[Command]` 上报房主记录器，本地即时回显「已记 笑点 #3 20:14:33」，右下角常驻提示与本次计数。玩家自己按，不需要旁观记录员。
- **`FeelSwitchHUD` 支持 `-feel=` 启动锁定**：D7 两轮分别用 `-feel=light` / `-feel=heavy` 起包，键盘 1/2/3 失效，防止战斗中误切污染数据。实测证实锁定作用于玩法而不只是 UI：沉重方案投掷 22kg 圆桶 1.9 m/s（=70×0.6÷22），轻快为 4.1。
- **玩家代号规则**：`P1` 固定为房主（按 `connectionToClient == NetworkServer.localConnection` 判定），其余按进场顺序 `P2..P4`。首版按「首次动作」排序导致客户端被编成 P1，已修正。
- **验收**：`D7_VALIDATE_OK recorder=1 marker=1 feelHud=1`；构建 `BUILD_OK`；双端跑 `-d65host -pttest` / `-d65client -pttest` 得到 17 行 CSV：`session_start` → `feel` → `player_join` → `issue(P2)` → `grab/laugh(P1)×2` → `friction×7` → `throw` → `player_leave` → `session_end(laughs=2 issues=1 grabs=1 throws=1)`。客户端标记经 Command 落到房主 CSV，与真人按键同一条路径。
- **新增 `Docs/Playtest_D7.md`**：联机方式三选一（推荐虚拟局域网）、四硬条件、两轮启动参数、35 分钟四段流程脚本、逐条观察判据、记录表模板、局均笑点公式与停机规则、已知限制。
- **产出测试包**：`Builds/ScavengeNightmare_D7_20261028.zip`（42MB，剔除了 BurstDebugInformation）。
- **核心风险（必须在约人前决定）**：当前建连是 KCP 直连 IP，**Steam 大厅不承载游戏流量**。远程约人必须先用虚拟局域网（ZeroTier/Radmin）把 IP 打通；Steam P2P 传输层仍未接入（且现有工程用 Facepunch.Steamworks，与多数依赖 Steamworks.NET 的 Mirror transport 存在绑定冲突，不是换个包就能好）。
