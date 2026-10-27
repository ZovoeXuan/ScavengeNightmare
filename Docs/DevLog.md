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
