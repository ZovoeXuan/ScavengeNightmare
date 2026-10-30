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

## D8 · 2026-10-28 · 缺点牌灰盒

- **做了什么**：新增 `ScavengeNightmare.Traits` 命名空间——`CharacterIdentity`（4 身份）+ `DrawbackCard`（5 牌）+ `TraitDatabase`/`TraitSettings`（数据库 + 加权抽取）+ `PlayerLoadout`（身份×牌的显式合成点）+ `PlayerTraits`（服务端权威分配，SyncVar 下发）+ `TraitHUD`（左上角显示身份/缺点/补偿/冲突标红）+ `NarrowZones`（窄区注册表）+ `TraitBootstrap`；`PlayerMotor` 接入移速/冲刺禁令/负重分母/硬直倍率/窄门恐惧；`GrabController` 接入交互距离/小件拦截/手抖抖动/投掷倍率；`FeelSettings` 增加带玩家倍率的 `ThrowSpeed` 与带分母覆盖的 `CarryMultiplier`。参数体系补齐到四层（物件档×手感方案×身份×缺点牌），全部乘法叠加、全部在数据表。
- **验收**：通过。离线段 `-d8test` 8 项全过（4 身份/5 牌/每张有代价有补偿/同轴不叠 buff/冲突率 58.7%/叠 buff 排除/任意组合数值不归零/四身份均进冲突统计）；联机段 `-d8host`+3 客户端 15 项全过（identity_unique count=4、无重复、大力士投 6.20/灵敏投 2.90/瘸子不能冲刺、小件拦截、手抖 grabScale 0.39~0.74、窄区注册）；`D8_VALIDATE_OK identities=4 cards=5 traits=1 hud=1 bootstrap=1`；`BUILD_OK`。回归：D4 投掷 4.14、D6 硬直 0.28、D3 四人 conns=4 players=4 全部恢复。
- **遗留问题**：
  1. 纯灰盒（染色胶囊），无美术无音效，D16 换正式角色模型。
  2. 「笑点信号」仍需真人验证：自动化只能证明数值差异生效，证明不了好不好笑，要靠 D7 朋友局采集。
  3. 缺点牌倍率是首版拍脑袋，D7/D10 拿到真人反馈后大概率要调。
  4. 身份是服务端按进场顺序发，无选人 UI（计划未排）；窄门恐惧只测了单人进门洞。

## D9 · 2026-10-28 · 联机稳定性

- **做了什么**：
  1. `NetworkHealthService`（挂 NetworkManager）：把 KCP `Timeout` 从默认 10000ms 压到 4000ms、开启服务端不活跃连接清理（20s）、订阅 Mirror 内建的 `onConnectionQualityChanged` 做连接质量分档、额外用 `NetworkConnection.lastMessageTime` 做 3s 预警 / 5s 兜底断线判定、断线原因归类（超时/解析失败/拒绝/房主退出）。
  2. `ConnectionStatusHUD`：断线全屏遮罩含具体原因与两个出口（返回大厅 / 退出）；预警只在中上方给一行软提示；房主视角用「玩家掉线」横幅而非全屏遮罩。房主迁移标注 LOCKED。
  3. `D9StressTest`：生成 20/40 个联网物件并持续驱动其运动，用 `NetworkDiagnostics` 量带宽、量 fps、按 UTC 墙钟对齐量同步误差。
  4. `D9DisconnectTest`：断线专项。房主支持 `-d9dfreeze=N` 阻塞主线程模拟「进程存活但不发包」的真实故障；`-d9dlogic` 做阈值边界的确定性验证。
  5. 修 HUD 重叠：三个面板原本全在 (12,12)，改为身份面板 (12,12)、联机面板 (12,130)、大厅面板联机中自动隐藏（F2 唤出）。
  6. `D9StabilitySetup`：幂等装配 + 校验，把 KCP 超时与不活跃清理持久化进场景资产。
- **验收**：通过。`D9_VALIDATE_OK health=1 stress=1 hud=1 kcpTimeout=4000ms`；断线阈值边界 7 项全过；断线实测两种故障——房主硬杀 **131ms**、房主冻结（不发 RST）**3988ms**（3.0s 预警 → 4.0s 断线），均远低于 10 秒预算；压测 20 物件 230kbps/60fps/同步误差 0.003m、40 物件 451kbps/60fps/同步误差 0.019m，4 人局上行预估 1.35Mbps。回归：D3 四人 conns=4 players=4、D4 投掷 4.14、D6 五条全过、D8 联机 15 项全过。
- **遗留问题**：
  1. **房主迁移 LOCKED**：只做到「提示 + 回大厅重开」，不保留本局进度；需要状态快照与所有权重建，未实现且不假装实现。
  2. 压测只测了 1 房主 + 1 客户端，4 人局带宽是按 ×3 外推，未实测 3 客户端同时收包（D22 补）。
  3. 断线检测依赖 KCP 超时行为；换成 Steam P2P transport 后需重新确认。
  4. 预警/断线 UI 是 IMGUI 临时件，D32 换正式 UI。

## D11 · 2026-10-28 · M1 验收准备（门未过，不打 tag）

- **D11 是什么**：M1 里程碑验收门，执行者是「我」（人类）。五条标准、交付 30 分钟录像 + 验收报告。AI 不参与录像与主观判断，本轮只做「让验收能产出可辩护证据」的部分。
- **修掉一个拦路 bug**：**负重减速从未接线**。`PlayerMotor.SetCarryMass` 全工程只有 1 个调用点、且在内部传初始值 0；`GrabController` 抓起 42kg 雕像从不通知移动端 → 抱着雕像走与空手同速，直接违反 D1 冻结的「跑会被拖累」。修复 `PlayerMotor.SyncCarryMassFromHands()`，每帧读 `GrabController` 的 SyncVar `heldMass`。必须在客户端做：玩家移动是客户端权威，服务端改系数对本地速度无效。
- **新增** `M1Acceptance`：重量感量化（`-m1weight`）+ 延迟量化（`-m1lathost/-m1latclient`）；`D11AcceptanceSetup` 幂等装配 + 校验，把 `LatencySimulation` 的 `wrap` 预先连到 `KcpTransport` 并保持禁用。
- **验收数据**：
  - 4 人同步：`conns=4 players=4`、四端全连、服务端异常 0
  - 重量感：投掷跨度 **4.30x**（2kg 15.99 → 42kg 3.72 m/s）、负重最大减速 **42%**，两者单调递减，`M1_WEIGHT_OK`
  - 延迟：请求 200ms RTT、**实测 250ms**；抓取往返 248ms、投掷往返 251ms（阈值 600ms），`M1_LATENCY_OK`
  - 摩擦：D6 五条全过
- **阻塞项**：第 5 条「缺点牌冲突组合有笑点信号」需要 D7 真人数据，未执行 → **不打 tag v0.1-poc**。五条缺一条证据就打 tag 等于自评合格。
- **顺手清理**：`Builds/Windows/PlaytestLogs/` 里 26 个 CSV 全是我自动化测试的产物，已移入 `_autotest_archive/`，避免与将来真人实测数据混淆。
- **延迟测试两次假通过（记下来）**：① 只改 `nm.transport` 不改 `Transport.active`（它在 `NetworkManager.Awake` 就已绑定），注入了 200ms 却测出 `rtt=24ms`；② 重量探针首版把玩家挪到物件旁而不改物件位置，服务端持物目标点太远，`breakDistance` 首帧就判脱手，测出 `throw=0 carry=0`。两处都是「看起来在测、其实没测到」。
- **遗留**：延迟只测 200ms 一档（未测 400ms POOR）；4 人测试是同机 4 进程非跨机；重量感的手感部分无法自动化。

## D12 · 2026-10-29 · 1 号图「废弃超市」白盒

- **做什么**：40×40m 白盒地图。3 条主路径、2 个窄门、1 段楼梯（12 级 ×0.25m = 3.0m）、3 个拾荒区（货架/仓库/收银）、1 个撤离点（后门）、5 个巡逻锚点、18 个物件锚点。
- **首要决定**：新建 `Supermarket.unity`，**不动 Main**。Main 是 D3/D4/D6/D8/D9 全部自动化基线的沙盒，改它会一次废掉所有回归。做法是复制 Main 的配置骨架（NetworkManager/SteamLobby/TraitBootstrap/HUD/灯光），清沙盒几何体后按坐标重建。打包 `BuildWindows` 加 `-map=supermarket`，默认仍打 Main 且输出目录分开（`Builds/Supermarket/`），避免冲掉测试基线包。
- **新增**：`PropSpawnAnchor`（场景锚点，替代 PropSpawner 的硬编码坐标表；**无锚点时回退 entries，保证 Main 不受影响**）、`EvacZone`（进入检测，D13 挂结算）、`PatrolAnchor`（D14 用）、`D12SceneSetup`（幂等生成+校验）、`D12SmokeTest`（-d12smoke 进图冒烟）。原 `MapMarkers.cs` 已拆成三个独立 .cs。
- **验收**：`D12_VALIDATE_OK spawns=4 doors=2 patrols=5 anchors=18 steps=12 stairTop=3.0m`（连续两次一致）；冒烟 `D12_SMOKE_OK`——18 件按锚点刷出、抓到 3kg 花瓶、楼梯爬升 3.00m（1.02→4.02）。
- **回归**：Main 重建后 D4 投掷 4.14、D6 五条、D8 八项、D9 七项全过；D3 四人 conns=4 players=4、4/4 已连（唯一异常是无 Steam 客户端的初始化报错，既有预期）。
- **踩坑（重要）**：Setup 报 5 个巡逻锚点、Validate 只读到 1 个且每次剩下的都不同。根因是**一个 .cs 放多个 MonoBehaviour** 时 Unity 用「文件 guid + 类专属本地 fileID」定位，非首个类的序列化退化为无 guid 的本地 ID。两次错误修法：统一写成 `11500000+guid` 导致全部解析成第一个类（24 个道具锚点/0 巡逻）；保留原 fileID 补 guid 则保留了从 Main 复制来的陈旧 ID。最终**每个类拆成独立文件**根除，修完 `repaired=0` 不再需要修复。附带两条：改场景文件前必须先关场景（否则被内存旧数据覆盖）；统计要放在重开场景之后（别信内存里的数量）。
- **遗留**：撤离点只做检测不结算（D13）；巡逻锚点只摆位（D14）；夹层只有 2.6m 进深平台不是完整二层；动线合理性仅坐标推演+单机冒烟，未真人跑图。

## D13 · 2026-10-29 · 物件系统化

- **做什么**：物件参数表扩成完整结构 + 20 种拾荒物 + 撤离结算读表。
- **参数表新增**：`propType`（普通/易碎/滚动/重型，同时驱动配色与行为组件挂载）、`gripOffset`（抓握点本地偏移，持物时对准准星；长条件横拿、大件抓侧面）。原有字段继续承担质量/摩擦/价值/易碎阈值。
- **20 种物件**（四类各 5）：易碎 花瓶/玻璃杯/酒瓶/电视/灯泡箱；滚动 圆桶/西瓜/篮球/轮胎/油桶；重型 雕像/衣柜/保险箱/洗衣机/冰箱；普通 罐头/纸箱/米袋/工具箱/微波炉。Cube/Cylinder/Capsule/Sphere 组合占位，四类四种配色。
- **硬约束**：既有 6 件（can/box/barrel/statue/cabinet/vase）**只写 propType/gripOffset/value/isFragile，物理数值一律不动**——D4 投掷基线 4.14 依赖 `barrel.throwImpulse=70`。回归证实 D4 仍是 4.14 未漂移。
- **撤离结算**：`EvacSettlement`（SyncVar 同步总额），玩家带货进区即计入；价值读 `GrabbableProp.CurrentValue`（易碎件碎后归 0），不读 Profile.value；结算组件本身不含任何硬编码数值。
- **验收**：`D13_VALIDATE_OK profiles=20 prefabs=20 registered=20 types[5/5/5/5]`；`D13_ACCEPT_OK`（四类各一件，800+150+900+20=1870 与表一致）。**改表即生效实证**：can 的 value 20→999 后总额 1870→2849，未改一行代码；随后已恢复。
- **回归**：D4 投掷 4.14、D6 五条、D8 八项、D9 七项全过；Supermarket 的 D12 进图冒烟全过。
- **踩坑**：① 既有 6 件的 `value` 原本是 0，而我为保护物理基线把整个数值写入都跳过了，导致结算输入全 0——value 不是物理参数，应照设。② 验收测试瞬移玩家到撤离区时没带走手持物件，服务端 `breakDistance` 立刻判脱手，结算永远 0——瞬移必须连货一起挪。
- **遗留**：20 件只注册进 Supermarket（Main 是冻结基线）；易碎件碎后只是拿不到钱、没有扣分设计；抓握点没做双手握持；**没有结算 UI**（总额已 SyncVar 同步但玩家看不到）；价值数值未经真人校准。

## Sprint 4A · Story 1 · 公秤提交 + 服务端公共账本 + GameRoot 跨场景存活（2026-10-29）

- **做了什么**：新增 `Economy/TeamAccount`、`Economy/QuotaTerminal`、`Economy/S4ABootstrap`、`Economy/S4AInput`、`UI/WalletHUD`、`Networking/S4ADepositTest`、`Editor/S4ASceneSetup`；`BuildWindows` 支持 `-map=sprint4a` 打 `Night1` 到 `Builds/Sprint4A`。
- **场景策略**：新建 `Night1.unity`（从 Supermarket 复制），删掉 `EvacZone`/`EvacSettlement`/`TraitBootstrap`，重配 16 个 4A 临时锚点；`Main` 与 `Supermarket` 一行未动，作为 D3–D13 回归基线。
- **验收**：`S4A_VALIDATE_OK night1=1 quotaTerminal=1 anchors=16 evacRemoved=1 traits=none gameroot=1`；`-s4adeposit` 通过——`S4A_DEPOSIT_TALLY accepted=4 uniqueCargo=4 expectedSum=2650 actualSum=2650 dupBlocked=1 ok=True`，`GATE_WALLET delta=0`；`S4A_RELOAD_CHECK before=2650 after=2650 instanceAlive=True`：重载场景后账本与余额存活。
- **回归**：D4 投掷 4.14、D6 五条、D8 八项、D9 七项全过（重建 Main 后）。
- **踩坑（都很隐蔽，记下来防复发）**：
  1. **不能读 `UnityEngine.Input`**：工程已切 Input System，`Input.GetKey` 每帧抛 `InvalidOperationException`，属于“报错但看起来还在跑”的假运行状态。交互抽象改走 `Keyboard.current`，自动化用 `S4AInput.AutoInteract`。
  2. **`NetworkTransformReliable` 会把手动瞬移拉回去**：`motor.transform.position = x` 实测不生效（距离仍是原锚点距离）。必须先 `enabled=false` 再改坐标后恢复。
  3. **抓取后下一帧被自动松手**：`GrabController.Update` 在“左键未按下”时调用 `RequestRelease(true)`，自动化没有鼠标输入 → 现象是 `immediate=True / afterFrame=False`。必须先 `grab.SetInputEnabled(false)`。
  4. **持物点必须按质心对齐**：判定用的是 `body.worldCenterOfMass`，不是 transform；大件轴心偏移会让首帧脱手。
  5. **CharacterController 触发器不稳定**：玩家没有 Rigidbody，`OnTriggerEnter` 实测不进（inside 恒为空）。公秤改用 `Collider.bounds.Contains(playerPos)` 判定，结果可断言。
  6. **删 `TraitBootstrap` 还不够**：`PlayerTraits` 自带 database 会继续抽身份（`D8_ASSIGN identity=brute card=sniffer`）。必须在自动化/启动入口显式 `TraitSettings.ForceNoTraits = true`，否则负重与交互距离被污染且断言仍“自洽通过”。
- **遗留 / 下一 Story**：仍是**内存态**，没有落盘（Story 2 做 `P0SaveService` 原子写与强杀恢复）；`-s4adeposit` 目前只有内存余额校验，`path=(memory)`；没有安全屋与商店（Story 4）；没有推车（Story 3）。

### Story 1 复审（同日，四处重点复查后修复）

- **修掉一个隐藏的假联机（最严重）**：`QuotaTerminal.Update` 在服务端直接读 `S4AInput.InteractHeld`——服务器读的是**自己那台机器**的键盘。主机模式下房主按 E 能提交、一切正常，但远端客户端永远按不动，属于典型的「主机单机跑通 = 联机也跑通」。新增 `Networking/PlayerInteract`：`isLocalPlayer` 读本地按键 → `[Command]` 上报 → 服务端 `[SyncVar]`，公秤只读 `InteractHeldServer`。**惰性**：场景里没有 `QuotaTerminal` 时完全不发消息，Main / Supermarket 零网络增量；`Player.prefab` 由 `S4ASceneSetup` 幂等挂载并纳入 `S4A_VALIDATE_OK` 校验项。
- **测试不再会假通过**：原 `WaitFor` 超时只打一行 `LogError` 就继续跑，最后照样输出 `ok=True`。改为硬失败计数 `failures`，所有超时、抓取失败、重放被接受、重载后余额变动都计入；最终必出 `S4A_STORY1_OK` 或 `S4A_STORY1_FAIL`，退出码也跟着变（`Application.Quit(0/1)`）。另新增反向断言：重放同一 `cargoId` 后余额必须**一动不动**，不能只看「返回 false」。
- **补上 QA 列为最高优先级的 `GATE_QUOTA_GATE`**：每次入账打 `leg=/progress=/quota=/carry_over=/portal=(locked|unlocked)`。它同时证明**超额不截断**——实测第 2 件就把 1800 撞穿，`carry_over=300→700→850` 全部照常入账，避免「交超了反而亏」这类陷阱选项。`NextLeg` 会重置 `quotaReached`。
- **账本单例加了重复守卫**：跨场景常驻的唯一风险是同进程出现第二份账本，旧实例被顶掉时余额静默归零，而所有断言读新实例会**自洽通过**。现在打 `S4A_ACCOUNT_DUPLICATE` 并拒绝覆盖。`S4ABootstrap` 同时对纯客户端进程立即返回（原逻辑会干等 20 秒）。
- **公秤不再每帧 `FindObjectsByType`**：改为 0.5s 缓存并顺带清理已销毁玩家的 `hold` 字典（A12 性能是阻断项，不能留每帧全表分配）。
- **内存态被显式化**：`TeamAccount.Persisted => false`，`GATE_WALLET` 增加 `persisted=False` 字段，防止把这轮结果误读成「钱已经不会丢」。
- **复审后验收**：`S4A_DEPOSIT_TALLY accepted=4 uniqueCargo=4 expectedSum=2650 actualSum=2650 dupBlocked=1 failures=0 ok=True`、`S4A_STORY1_OK balance=2650 reloadSurvived=True`。
- **复审后回归**：D4 投掷 4.14、D6 五条、D8 八项、D9 七项全过——证明给 `Player.prefab` 加组件对基线完全惰性。
- **本轮新增踩坑**：**服务器不能读本地输入**。凡是「长按/按键」类交互，必须是客户端 `[Command]` 上报，否则主机模式自测永远通过、一上双人就废。

## Sprint 4A · Story 2 · P0SaveService：原子写 / 三级备份 / 强杀恢复（2026-10-29）

- **做了什么**：新增 `Save/TeamSaveData`（含 `SaveTxnRecord` 事务流水）、`Save/P0SaveService`（`Initialize` / `LoadOrCreate` / `TryCommit` / `TryCommitState` / `WriteAtomic` / 单实例锁）、`Save/S4ACli`、`Networking/S4ASaveTest`、`Tools/s4a_save_suite.sh`；`TeamAccount` 全量改为「先落盘、再改内存」；`S4ABootstrap` 启动顺序改为 `Instantiate → Initialize/LoadOrCreate → NetworkServer.Spawn`；`WalletHUD` 增加存档状态行（SAVED / RECOVERED / CORRUPT / LOCKED / FAILED，异常态变红）。
- **写盘链路**：`FileStream(tmp) + Flush(true)` → `bak2` 删除 → `bak → bak2` → `File.Replace(tmp, sav, bak)`；非 NTFS 抛 `PlatformNotSupportedException` 时降级为「删 sav → Move(tmp,sav)」并记 `S4A_SAVE_FALLBACK_NONATOMIC`。校验和放在文件首行（`SHA256:<hex>\n{json}`），不塞进 JSON 内部，避免「算校验和要先清空校验和字段」的自指问题和字段顺序依赖。
- **验收（33 项全过，`S4A_STORY2_SUITE_OK`）**：
  - C1 三笔提交：`S4A_SAVE_OK txn=3 balance=2100`，每笔后立刻 `S4A_SAVE_IMMEDIATE disk_balance=` 与内存一致——**不等退出就能从盘上读到**。
  - C2 重启校验：`S4A_SAVE_VERIFY_OK txn=3 dup=0 balance=2100 sumCommitted=2100 from=sav`、`GATE_WALLET expected=2100 actual=2100 delta=0`。
  - C3 外部强杀：`taskkill /F` 停在第 3 笔 → 重启 `txn=3 balance=1500`，正好等于最后一条成功事务。
  - C4 进程内 `Environment.Exit` 对照：同样停在 txn=3 → `txn=3 balance=1500`，与 C3 **逐字一致**，证明 `OnApplicationQuit` / `ProcessExit` 没有任何偷偷刷盘。
  - C5 `sav` 改坏 → `S4A_SAVE_RECOVERED from=bak`，回退到上一笔（txn=2/1200），坏档留证 `corrupt-*-sav.sav`。
  - C6 三档全坏 → `S4A_SAVE_CORRUPT_RECOVERED`，3 个坏档全部留证，新建空档后可继续使用。
  - C7 写失败注入：`S4A_SAVE_FAIL reason=io_error` + `memory_same=True disk_same=True txn_same=True`（内存、磁盘 sha256、事务计数三者均未变）。
  - C8 不可写路径：`S4A_SAVE_FAIL reason=io_error phase=init`，入账被 `state=Rejected reason=save_unavailable` 拒绝，用例自判 `ok=False`（没有静默降级成内存入账）。
  - C9 同机双开：第二进程 `S4A_SAVE_LOCKED`、`S4A_SAVE_DOUBLE_OK second_process_readonly=1`，入账被拒。
- **回归**：重建 Main 后 D4 投掷 **4.14**（与基线逐字一致）、D6 五条全过（滚动 3.87 / 硬直 0.28 / 易碎归零 / 夹手脱手 / 窄门卡位）、D8 八项全过（冲突率 58.7%）。D9 七项留到 S4A-5 收口统一复跑。
- **踩坑（都是「看起来在跑、其实结论错了」的类型）**：
  1. **批量删除被安全策略拦下，脚本却继续跑**：首行只打印 `SAFE_DELETE_BULK_CONFIRM_REQUIRED`，上一轮的 `wallet.sav` 原封不动，于是每个用例都从旧余额继续累加，13 条断言整批错位。改成每次用时间戳独立目录，从根上不需要删。
  2. **`StartHost` 抛异常会让进程永久挂死**：端口没放干净时抛 `SocketException`，异常一抛 `Start` 就中断，协程根本没启动 → 没有 `WaitFor`、没有 `Quit`。加 `try/catch` + 90/150 秒 watchdog + 每个用例独立端口（17701–17712）。
  3. **强杀与进程内退出必须停在同一个事务点**才叫对照：一开始两边各跑各的（8 笔 vs 2 笔），只能说「各自自洽」，证明不了 `ProcessExit` 有没有刷盘。改成 `-stopafter=3` 死等外部 `taskkill`，与 `-killafter=3` 逐字比对。
  4. **不持久化 `committedIds`**：`cargoId = runId-legIndex-netId`，而 `netId` 在进程重启后会被复用。把旧 id 带回来会让新局重新生成的物件被误判成重复提交，表现为「这货怎么交都进不了账」。跨进程重启 = 新局：钱留下、`runId` 换新、去重表清空；同进程重连仍由内存去重表保证。
  5. **存档服务在、但没加载成功时不能退回内存路径**：只读盘 / 双开场景下若降级为内存入账，HUD 会显示「入账成功」而盘上一分没有，是最坏的一种假账。现在一律 `reason=save_unavailable` 整笔拒绝。
- **遗留 / 下一 Story**：CD6「余额不足购买被拒且磁盘逐字节未变」需要商店，推迟到 Story 4 用真实购买路径验；团灭场景要等 4B 有怪物与倒地；D9 七项与 D11/D12/D13 在 S4A-5 统一收口；存档尚无版本迁移与云同步（P0 不做）。

## Sprint 4A · Story 3 · 折叠推车 v0（2026-10-29 ~ 30）

- **做了什么**：新增 `Cart/CartProfile`（数值资源化）、`Cart/CartController`、`Cart/CartCargo`、
  `Cart/CartSpawnAnchor`、`Cart/CartParkingZone`、`Noise/NoiseBus`、`Networking/S4ACartTest`；
  `GrabController` 加 `SetHandsBusy`、`PlayerMotor` 加 `SetExternalSpeedCap`；
  `QuotaTerminal` + `TeamAccount` 支持整车提交（车停进位 + 有货 + 长按 1.2s）。
- **交互定案**：推行 = 靠近 + **按住鼠标左键**（与抓货同一键位）；推车时 `handsBusy`，
  不能一边抱货一边推车。
- **验收**：
  - `-s4acart`：装 3 件 → 推行 8.2m → 停车位整车提交入账，`kept=3/3`（连跑两次一致）
  - `-s4acart-a4`：102kg 两件推行 **30.00m**，不掉货、无卡死
  - `-s4acart-a5`：卸空 → 折叠 → 抱车爬 12 级楼梯 **3.00m** 不脱手 → 楼顶展开，**69s < 90s**
  - `-s4acart-noise`：三档 3.0 / 4.0 / 7.0 单调，衰减 3/s，记忆按 age 遗忘
- **板面尺寸（实测推翻原假设，最重要的一条）**：实测各件大件水平尺寸为
  保险箱 0.81m、雕像 0.85m、电视 0.86m——**单件就比原板宽 0.75m 还大**。
  技术计划「三件占地 0.76㎡ / 板面 0.975㎡」严重低估。板面由 1.3×0.75m 改为 **1.8×0.9m**。
- **A4 指标变更（design 层）**：≥110kg（60+42+12=114kg 三件）→ **≥100kg（60+42=102kg 两件）**，
  已同步 `P0_VerticalSlice.md`（标 v1.2）与 `Sprint_4A_TechnicalPlan.md`，均写明实测依据。
- **踩坑**：
  1. 装货排布必须从固定间距改为**按实际包围盒**，放不下就 `deck_full` 直接拒绝——
     「显示装上了、一推车掉一地」比拒绝难排查得多。
  2. 推行判定必须用**水平距离**：把 y 算进去，玩家站在坡上就超出把手范围，车速直接归零。
  3. 推行必须在**专用跑道**上测：地面推行会随机撞货架，同一份代码在 8.8m / 0.24m 之间横跳。
  4. 抱车必须**关掉车身碰撞**：否则车顶在台阶上，玩家被自己的车堵在楼梯口（40 秒爬升 0 米）。
  5. 爬升判定用**相对高度**：玩家身体中心比脚底高 1.05m，绝对 y=3.0 时还差一层楼。
  6. `PlayerInteract.Update` 里原有的 `if (held == lastSent) return;` 会吃掉新加的输入通道，
     导致 `autoGrab=True` 但服务端 `grabHeldServer` 始终 False。两条输入要各自独立判断。
  7. **不要用运行时切 kinematic 做软锁定**：解冻瞬间与车体穿透修正，60kg 件被抛到 260m 外。
  8. 1kg 配 42kg（42:1 质量比）会被接触求解弹出板外，试过排布 / CCD / 装货顺序都没压住；
     60+42（1.4:1）完全稳定。这是物理引擎的质量比限制。
- **遗留**：同板质量比限制未做（若要让 1kg 与 42kg 同板）；窄门三态与
  `GATE_HEAVY_MANUAL via_cart=False` 未补；D4 / D6 回归留到 Story 5 收口。

### Story 3 收尾（2026-10-30）

- **新增三条判据，全部有拒绝码**：
  - `-s4acart-narrow`：`S4A_CART_NARROWDOOR_OK empty=1 loaded=1 folded=1 lost=0`
  - `-s4acart-heavy`：`GATE_HEAVY_MANUAL cargoId=cabinet mass=35 via_cart=False` + `cartLoads=0`
  - `-s4acart-ratio`：`S4A_CART_RATIO_OK rejected=True accepted=True`（42:1 拒 / 6:1 收）
- **三处推翻原计划（都写了实测依据，不造假断言）**：
  1. **「载货车必被窄门挡住」不成立**。推车碰撞体是 `deckLength × topY × deckWidth` = 1.8 × 0.4 × **0.9**，
     门净开口 **1.20m**，货沿板长布列不增加侧向宽度 ⇒ 物理上过得去。原计划要断言 `blocked=True`
     只能是假通过。改为断言真不变量：`lost==0`（过门不掉货）+ `blocked_fold==True`
     （载货时折叠必须被拒，`hint=unload_first`）——后者才是「窄门 → 卸货折叠」这条玩法规则的真正落点。
  2. **`GATE_HEAVY_MANUAL` 不走 `EvacMarker`**：`EvacZone` / `EvacSettlement` 已在 4A 从 Night1 移除
     （`S4ASceneSetup.Validate` 断言 `evac==0`），改为徒手搬 `cabinet` 到公秤入账。
  3. **`via_cart` 不用「grep 日志里没有该 id」判**：改用 `CartController.LoadEventCount` 计数器，
     `S4A_CART_HEAVY_RESULT cartLoads=0`。日志受帧序影响，「没抓到」与「没发生」分不开。
- **顺带修掉两个真 bug（都是收尾用例逼出来的）**：
  1. **推车是横着走的**。`DriveBehind` 把车 yaw 直接设成玩家 yaw，而车体长轴是 local +X
     （板面 1.8 沿 X、0.9 沿 Z），结果是侧向宽度变成 1.8m、任何 1.2m 窄门都过不去，
     货横着摆在车上、把手飘在车侧 0.35m 外。改为 `playerYaw - 90`，把手一并挪到 `-X`（车尾）。
     实测狭窄门 yaw=89.9，稳定。
  2. **装货布局每件都重新量实时包围盒**。货落板后会转、会倾斜，量出的半宽从 0.265 涨到 0.47，
     第三件被误判 `deck_full`（`need=1.01 limit=0.90`，而 0.53+0.50+0.44 其实塞得下）。
     改为**装载瞬间锁定半宽**（`loadHalfWidth` 字典，`PruneLoadWidths` 按当前板面货物清幽灵），
     `-s4acart` 由 `failures=4` 回到 `failures=0`。
- **回归**：`-s4acart` / `-a4` / `-a5` / `-noise` / `-narrow` / `-heavy` / `-ratio` / `-s4adeposit` 八条全绿
  （A4 30.01m，A5 3.00m / 69.1s，Story 1 余额 2650 未变）。

### Story 4 · 商品效果接线（2026-10-30）

- **四件商品全部接到真实判定点**（不是只上架）：
  - `grip_gloves` 400：`GrabController.ServerHoldStep` 的脱手阈值、跟随上限速度、跟随加速度同时 ×1.5
  - `packing_tape` 500：`CartCargo.Tick` 的水平锚点刚度 ×2（钳到 1.0 = 完全捆死）
  - `flashlight` 350：`PlayerEquipment` + Command/SyncVar，F 键开关，射程 18m，撤销后自动熄灭
  - `grip_training` 900：负重减速量与**负重硬上限**同时 ×1.15
- **握力训练的语义改过一次（有实测依据）**：原本只缩放减速量（0.85），实测 42kg 时
  基准速度 3.335 已经高于硬上限 3.135，缩小减速量会被上限一起钳住，比值只有 **1.017**
  ——等于花了 900 块什么都没买到。改成「上限也一起抬」后 `heavyRatio=1.150`，正好 +15%。
- **新增负重硬上限**：≥15kg 携带时速度 ≤ 3.135m/s（`S4A_CARRY_CAP applied=1`），
  握力训练把它抬到 3.605。这是兜底线，主手感仍是负重减速。
- **三个坑**：
  1. `Command` 不能挂在场景对象上：场景对象没有 owner，`connectionToClient` 恒为 null，
     服务端复核距离永远拿到 -1。商店买入/撤销的 Command 必须挂在玩家身上。
  2. 无人值守跑批时必须 `S4AInput.SuppressManual = true`：跑批时动一下鼠标，
     开局就会被判成「正在推车」，`handsBusy` 锁住抓货，A4 第一件货直接 grab 失败。
  3. 装货布局不能用世界 AABB 量货物宽度：锚点带随机朝向，同一件雕像转 45° 后
     AABB 从 0.55 涨到 0.96，A4 两件装车被判成 `deck_full`。改用 BoxCollider 局部尺寸 × 缩放。
- **窄门用例的探针位置也是坑**：门南侧 1m 就是公秤停车位，站在那儿按 E 会先被
  `QuotaTerminal` 抢去整车提交（货瞬间清零），「载货折叠被拒」根本走不到，
  断言会全绿但什么都没验。探针改到远离公秤的门前再打。
- **验收**：`S4A_CARRY_CAP_OK base=5.175 boosted=5.250 lightRatio=1.014 heavyBase=3.135
  heavyBoost=3.605 cap=3.135 flashlight=1 ok=True`；九条 4A 用例全绿（A4 30.03m / A5 3.00m / 商店 identity=1）。

### Story 4 · 换关闭环（2026-10-30）

- **新增**：`LevelRouteService`（挂 NM 物体，DDOL，靠场景名变化驱动重建）、`LevelPortal`（长按 1.2s +
  过关额闸门）、`SceneEntryAnchor`（出生锚点）、`Safehouse.unity`（从零搭的封闭房间 + 商店 + 返回门）。
  **刻意不写 NetworkManager 子类**：那要把三个场景的 NM 组件整体迁移，迁移失败＝联机配置全丢，
  而真正需要 NM 钩子的只有「换关后玩家放哪」「换关后重刷物件」，用场景名轮询就能做完。
- **修掉 `S4ABootstrap` 的历史 bug**：换关回来时 `TeamAccount.Instance != null` 走 `yield break`，
  导致**第二天进关没有车、没有货**。抽出 `EnsureSceneObjects()` 作为重入入口，`PropSpawner` 加
  `RespawnForNewScene()`（只增不改）。
- **验收**：`S4A_LOOP_OK legs=2 balance=2700 dupBlocked=1 sync=match cart=True ok=True`
  - 夜路→安全屋：钱包 2700 不丢、商店在、**安全屋没有推车**（有的话说明旧 NetworkObject 没清）
  - `GATE_LEVEL n=2 quota=2632 source=table`，progress 归零，回到夜路推车与货都在
  - `GATE_WALLET_SYNC server=2700 client=2700 match=True`
  - `-s4asyncbreak`：`match=False`（探针自检；注入点必须在**所有换关之后**，
    早注入会被换关时的初始状态重发覆盖掉，实测 dropped=2700 但最终仍 match=True）
- **踩坑**：
  1. 路由「把玩家挪到入口锚点」不能无条件做：用例刚把人站到店里，0.6s 后被拎回出生点，
     一次打挂商店/推车/公秤三条用例。改成**只救还在原点的玩家**（这才是换关空表的真实故障表现）。
  2. `-s4aloop` 也必须等路由摆位（1.8s）再传送到门前，否则前脚站到门上、后脚被拎走。
  3. 安全屋场景从零搭而不是复制夜路再删——复制会把货架、推车锚点、公秤全带过来，
     漏删一个就是「安全屋里也能刷货」这种没人会发现的问题。
- **已知资产待修**：`vase` 的 BoxCollider 是占位尺寸（板面布局实测 width=0.00，会和其他货叠到同一坐标），
  推行时必掉。已在 `-s4acart` 候选池里挡掉并注明，属资产修复项，不是推车逻辑问题。

## Sprint 4B · Story 1 · 玩家生存：HP / 倒地 / 救援 / 团灭（2026-10-30）

- **做了什么**：新增 `Combat/PlayerVitals`（HP/单次伤害保护/倒地/自救/救援）、`ReviveInteractor`（2.5m 长按 E 4s）、`TeamWipeDetector`（全队倒地 → 3s/10s 宽限 → 本趟结束）、`S4BInput`、`DownedHud`、`RevivePrompt`；`PlayerMotor` 增加倒地限速 0.8m/s 与禁冲刺/跳跃；`GrabController` 倒地禁抓；`TeamAccount` 增加 `ServerEndRun`（余额保留、leg 回 1、runId 换新）与自救针/急救包库存；商店补 `self_injector` 120 / `medkit` 200（共 6 件）。
- **验收**：`S4B_VALIDATE_OK vitals=1 revive=1 wipe=1 downedHud=1 shop6=1 tests=3`；`-s4bvitals` / `-s4bwipe` / `-s4bdownhost`+`-s4bdownclient` 全绿；4A 回归 `13/13`。
- **顺手修掉三个真 bug**：① 首场景 `LevelRouteService` 重复调 `EnsureSceneObjects`，Night1 刷出 32 件货（团灭日志 `cargoDestroyed=33`），改首场景跳过后恢复 16 件 / 17 个销毁对象；② 安全屋被 `EnsureSceneObjects` / `PropSpawner` 回退刷出 11 件货和一辆车，已加 Safehouse 守卫；③ `GrabController.inputEnabled` 被当成抓取闸门，打挂 `s4adeposit` / `s4acarrycap` 两条回归，改为只判 `PlayerVitals.IsDowned`。
- **遗留**：S4B-2 噪音+空眼；`medkit` 双人治疗断言未做；A8 团灭已补齐为 4/4，报告措辞待 S4B-5 统一更新。
