# 联机骨架（D3）

## 状态

- 库：**Mirror v96.11.3**（以 Assets 插件形式放在 `Assets/Plugins/Mirror`，不走 UPM：v96 起仓库内无 `package.json`，UPM git 路径装不上）
- 传输：**kcp2k.KcpTransport**，端口 7777
- 角色：房主权威（Host = Server + Client），`maxConnections = 4`
- 玩家同步：`NetworkTransformReliable`，`syncDirection = ClientToServer`（客户端权威）
- 生成点：4 个 `NetworkStartPosition`，`playerSpawnMethod = RoundRobin`
- 已自动验证：4 客户端并行连接、各自生成、位置双向同步、RTT 可见、构建包含 Mirror
- 待人工验证：GUI 下 4 个真实窗口的观感（抖动、镜头）——见文末清单

## 场景与预制体

`Assets/_Game/Scenes/Main.unity` 根对象：

| 对象 | 作用 |
|---|---|
| `Main Camera` | 离线观察机位，本地玩家生成后被自动关闭 |
| `Directional Light` | 模板自带 |
| `Ground` | 200m × 200m 白盒地面（Plane，缩放 20） |
| `SpawnPoint_1..4` | `NetworkStartPosition`，半径 3.5m 环形分布，y=1.05 |
| `NetworkManager` | Mirror `NetworkManager` + `KcpTransport` + `NetDebugHUD` + `D3AutoRunner` |
| `SteamLobby` | D2 的 `SteamLobbyService` + `LobbyDebugUI`，保持不变 |

`Assets/_Game/Prefabs/Player.prefab`：

- `CharacterController`（height 2 / radius 0.5 / center 0）
- `NetworkIdentity`（assetId = 1675433747）
- `NetworkTransformReliable`（ClientToServer）
- `PlayerMotor`
- 子物体 `Mesh`（胶囊，无碰撞体）、`PlayerCamera`（tag MainCamera，默认关闭，仅本地玩家开启）

## 代码结构

| 文件 | 作用 |
|---|---|
| `Scripts/Networking/PlayerMotor.cs` | 新输入系统 WASD + 鼠标视角 + CharacterController 移动；含自动化输入注入接口 |
| `Scripts/Networking/D3AutoRunner.cs` | 命令行驱动的多开/批处理验证外壳，正常启动不生效 |
| `Scripts/UI/NetDebugHUD.cs` | 临时联机面板：Host / Client / Stop、地址输入、RTT、连接数、本地玩家坐标；F1 收起 |
| `Scripts/Editor/D3SceneSetup.cs` | 幂等装配脚本：生成 prefab、地面、出生点、NetworkManager；含独立校验入口 |
| `Scripts/Editor/BuildWindows.cs` | D2 已有的构建入口（未改） |

## 启动方式

**编辑器**：打开 `Main.unity` → Play → 面板点「Host」；另一台机器改地址后点「Client」。

**构建包**：`Builds/Windows/ScavengeNightmare.exe`，同上。

**多开自动化（无键鼠）**：

```
ScavengeNightmare.exe -batchmode -logFile <path> -d3server -duration=30 -fps=60
ScavengeNightmare.exe -batchmode -logFile <path> -d3client -address=127.0.0.1 -duration=14 -automoveback -fps=60
```

| 参数 | 说明 |
|---|---|
| `-d3server` / `-d3client` | 角色；不传则完全走正常流程（GUI 面板） |
| `-address=` | 客户端连接地址，默认 127.0.0.1 |
| `-port=` | 端口，默认 7777 |
| `-duration=` | 运行秒数后自动退出 |
| `-fps=` | 固定帧率，默认 60（必须，见踩坑 3） |
| `-automove` / `-automoveback` | 客户端自动前进 / 后退，用于验证位置同步 |

日志标记：`D3_SERVER_LISTENING`、`D3_CLIENT_CONNECTED`、`D3_STATE`、`D3_AUTORUN_DONE`。

**装配与校验（批处理）**：

```bat
"D:\Unity\6000.3.27f1\Editor\Unity.exe" -batchmode -quit -projectPath "D:\drlj\game" ^
  -executeMethod ScavengeNightmare.EditorTools.D3SceneSetup.Setup -logFile "D:\drlj\game\Logs\d3_setup.log"
:: 期望 D3_SETUP_OK
"D:\Unity\6000.3.27f1\Editor\Unity.exe" -batchmode -quit -projectPath "D:\drlj\game" ^
  -executeMethod ScavengeNightmare.EditorTools.D3SceneSetup.Validate -logFile "D:\drlj\game\Logs\d3_validate.log"
:: 期望 D3_VALIDATE_OK transport=kcp maxConnections=4 spawnPoints=4
```

## 自动化验收证据（2026-10-27）

1 服务端 + 4 客户端，同机 loopback，60fps：

```
服务端：conns=4 players=4（连续 7 次采样）
  pos=[(-25.46,1.02,0.00),(0.00,1.02,18.83),(0.00,1.02,-12.06),(5.23,1.02,0.00)]
  ...
  pos=[(-44.72,1.02,0.00),(0.00,1.02,38.09),(0.00,1.02,-31.32),(24.49,1.02,0.00)]

客户端：全部 connected=True，rtt 16-33ms，位移速率 3.2 m/s（与 walkSpeed 一致）
  c1 (-47.59,1.02,0.00)  c2 (0.00,1.02,41.31)  c3 (0.00,1.02,-34.91)  c4 (28.45,1.02,0.00)
```

四个出生点两两不重叠，四条轨迹互不干扰，Y 轴全程 1.02 未穿透地面；服务端位置曲线与各客户端保持恒定偏移（无回弹、无跳变）。

## 踩坑记录（后续别重复犯）

1. **Mirror 装不上 UPM**：v96 的仓库里 `Assets/Mirror` 没有 `package.json`，`?path=Assets/Mirror` 会失败；且本机 `raw.githubusercontent.com` 被阻断。改为下载 release tar.gz → 解压到 `Assets/Plugins/Mirror`（剔除 Examples / Tests，1050 个文件 / 15MB）。
2. **出生点全挤在同一个点**：`NetworkManager.playerSpawnMethod` 默认 `Random`，3 个客户端抽到同一个出生点。改为 `RoundRobin`。
3. **批处理环境不限帧**：无头运行实测 2-4 万 FPS，单帧位移 < `CharacterController.skinWidth` 被物理吞掉，移动速度只剩 0.19 m/s。自动化必须 `-fps=60`，否则会误判「手感坏了」。
4. **prefab 的 assetId 不会自动落盘**：`PrefabUtility.LoadPrefabContents` 得到的是预览对象，Mirror 的 `SetupIDs` 不认；而 getter 在编辑器里每次都重算，看起来「有值」其实文件里是 0。做法：用 `NetworkIdentity.AssetGuidToUint(GUID)` 算出值写进序列化字段，再按文件内容校验。玩家走 `playerPrefab` 直连生成不依赖它，但 D4/D6 用 `NetworkServer.Spawn` 生成拾荒物时必须靠 assetId。
5. **场景相机与 AudioListener 冲突**：玩家 prefab 自带相机和监听器，本地玩家生成后会同时存在两个相机与两个监听器，`PlayerMotor` 负责关闭其它 Camera 与其 AudioListener，退出时恢复。

## 4 客户端人工验收清单（GUI）

1. 双击 `Builds/Windows/ScavengeNightmare.exe` 开 4 个窗口（或 1 编辑器 + 3 构建包）。
2. 第 1 个窗口点「Host」，其余 3 个把地址填成房主的局域网 IP（或同机 127.0.0.1）后点「Client」。
3. 面板应显示 `连接数：4 / 4`，四个胶囊体分别出现在地面四角，互不重叠。
4. 每个窗口走动，其它窗口应看到对应胶囊体同步移动；**观察是否抖动、滑步、穿透地面**。
5. 面板 RTT 应有数值（局域网通常 < 10ms）；F1 可收起面板。
6. 任一方点「停止」，服务端连接数应减 1。
7. 记录：是否有卡顿峰值、四人同时跳跃/对撞时的表现、镜头是否平顺。

## 已知限制

- **未接 Steam P2P**：当前只走 KCP 直连 / 局域网。Mirror 官方 `KcpTransport` 不支持 Steam 好友直连，要做「大厅邀请后自动连」需要额外引入 Steam 传输层（FizzySteamworks 等），已排到 D9 评估。
- **无插值调优 / 无 AOI**：`NetworkTransformReliable` 默认 30Hz 发送，同屏物件上限仍是 40（D9 压测）。
- **无断线重连**：D9 处理。
- **移动参数硬编码在 `PlayerMotor` 字段里**：D5 迁到 ScriptableObject。
- **`D3AutoRunner` 是测试外壳**：随正式版本一起打进去但不带 `-d3*` 参数时不生效，正式发布前移除。

---

# D9 · 联机稳定性（2026-10-28）

## 一、要解决的三件事

计划原文：断线检测与提示；房主掉线后客户端 10 秒内收到提示并可回大厅（房主迁移先做简单版）；物理物件同屏压测 20/40 个；延迟显示与插值调优。验收：40 物内同步稳定、掉线有明确反馈。

## 二、关键决策：为什么必须改 KCP 超时

`KcpTransport.Timeout` 默认 **10000ms**。这意味着房主硬杀后，客户端最长要等 10 秒才收到断开事件——**光检测就吃满「10 秒内提示」的全部预算，加提示必然超标**。

因此把超时压到 **4000ms**，并额外做了一层不依赖 transport 的检测：

| 层 | 判定依据 | 阈值 | 作用 |
|---|---|---|---|
| 预警 | `NetworkConnection.lastMessageTime`（PingInterval=0.1s，正常每秒更新约 10 次） | 3.0s | 先给"连接不稳定"软提示，避免玩家以为卡死 |
| 硬断线 | 同上 | 5.0s | transport 没报错时的兜底 |
| transport | `KcpTransport.Timeout` | 4.0s | 实际最先触发的那条（见下方实测） |

**实测分工**：真实故障里 transport 超时（4.0s）先于我们的 watchdog（5.0s）触发，watchdog 是保险而非主力。这个分工是刻意的——transport 能报就信它（原因更准），报不了才用静默判定兜底。

## 三、实测数据

### 断线检测（两种故障分别测）

| 故障类型 | 检测机制 | 耗时 | 提示文案 |
|---|---|---|---|
| 房主进程被杀（OS 发 RST） | transport 立即报错 | **131 ms** | 与服务器的连接已断开 |
| 房主冻结 / 网线松动（不发 RST） | 3.0s 预警 → 4.0s transport 超时 | **3988 ms** | 连接超时（网络中断或房主已退出） |

- 冻结用「主线程 `Thread.Sleep` 阻塞 12 秒」模拟：进程与套接字都活着、一个包都不发，这正是真实故障的样子。
- 客户端在冻结前有 7 条 `D9_DCLIENT_ALIVE` 心跳，证明它已进入稳态，测的不是握手期失败。
- 状态机全程无跳变：`Normal ×6 → Warning(3.0s) → Disconnected(3.99s)`。
- 两种故障都远低于 10 秒预算。

### 物件压测（20 / 40 个同时运动）

关键设计：**必须让物件持续运动**。`NetworkTransform` 对静止物件跳过同步，静止压测测出的是「接近零流量」，对判断能否扛住 40 物毫无意义。

| 档位 | 客户端可见物件 | 单副本带宽 | 4 人局预估上行 | 房主 fps | 同步误差（均值 / 最大） |
|---|---|---|---|---|---|
| 20 | 31（20 压测 + 11 原有） | 230.0 kbps | 689.9 kbps | 60.0 | **0.003 m / 0.020 m** |
| 40 | 51（40 压测 + 11 原有） | 451.1 kbps | 1353.2 kbps | 60.0 | **0.019 m / 0.050 m** |

- 同步误差按 **UTC 墙钟对齐**计算（只取两端时间差 ≤120ms 的配对），不对齐数组下标——这是 D6.5 踩过的坑。
- 40 物件下 fps 守住 60，4 人局上行预估 1.35 Mbps，家用宽带上行与 Steam 中继都能承受。
- 同步误差远优于 D6.5 玩家的 0.28m，因为压测物件速度（2.2 m/s）低于玩家（3.7 m/s）。

## 四、带宽口径（两次修正后的正确算法）

`NetworkDiagnostics.MessageInfo` 给的是 `bytes`（单份大小）与 `count`（发给了几个连接）。这里有两个坑：

1. **`bytes × count` 不等于真实上行**：Host 模式下本地连接也计入 `count`，直接乘会高估一倍。
2. 正确口径：`单副本字节 × 真实远程客户端数`（`connections.Count - 1`）。

首版代码就是错的：`peak_up_kbps` 恒为 0（读数与累加变量不是同一个），`avg_up_kbps=446` 是含本地连接的高估值。修正后 40 物档单副本 451 kbps，与客户端实测收到的 ~185 kbps 量级吻合。

同时把阈值从拍脑袋的「200 kbps」改为按 4 人局外推的「2500 kbps」——原阈值是在只有 2 个连接、且口径错误的数据上定的，属于典型的错误数据推出错误标准。

## 五、断线提示与「回大厅」

`ConnectionStatusHUD` 的三态：

- **预警**（Warning）：屏幕中上方一行"连接不稳定，正在尝试恢复…"，不遮挡操作。
- **断线**（Disconnected）：全屏暗色遮罩，显示**具体原因**（超时/地址无法解析/连接被拒绝/房主退出）+ 两个出口（返回大厅 / 退出游戏）。
- **房主视角**：玩家掉线时只在顶部显示 8 秒「玩家掉线」横幅，**不弹全屏遮罩**——房主不是掉线方，弹遮罩属于误报。

**「返回大厅」的实现边界**：停掉当前网络会话，让 D2 的 Steam 大厅面板重新可用，由玩家重新开房或加入。

**真正的房主迁移（保留本局进度）标注为 LOCKED**：需要全量物件状态快照、所有权重建、新 host 的端口接管，未实现。HUD 上明写「房主迁移（保留本局进度）：LOCKED — 未实现」，不做假展示。

## 六、顺手修掉的 HUD 重叠缺陷

D9 之前三个面板全在 `(12,12)`：`LobbyDebugUI`（520×560）、`NetDebugHUD`（320×260）、`TraitHUD`（340×74）完全重叠，D8 的身份面板会被联机面板盖住。

现在的布局：

| 面板 | 位置 | 说明 |
|---|---|---|
| TraitHUD（身份/缺点） | 左上 (12,12) | 玩家最常看 |
| NetDebugHUD（联机） | 左上 (12,130) | 让位给身份面板，新增「质量」一行 |
| LobbyDebugUI（Steam 大厅） | 左上 (12,12) | **联机中自动隐藏**（F2 唤出），它 520×560 太大 |
| ConnectionStatusHUD | 屏幕中央 | 仅断线/预警时出现 |
| FrictionHUD | 右上 | 未动 |
| FeelSwitchHUD | 左下 | 未动 |

## 七、延迟显示

复用 Mirror v96 内建的 `NetworkClient.onConnectionQualityChanged`，底层按 `rtt + jitter` 分档：

| 档位 | 判定 | 含义 |
|---|---|---|
| EXCELLENT | rtt ≤100ms 且 jitter ≤100ms | 理想 |
| GOOD | rtt ≤200ms 且 jitter ≤200ms | 一般玩家可玩 |
| FAIR | rtt ≤400ms 且 jitter ≤500ms | 延迟明显 |
| POOR | 更差 | 几乎不可玩 |

不自己按 rtt 单指标分档：jitter 对「搬东西」这类连续操作的影响不比 rtt 小，内建算法两者都考虑。

Host 模式显示「本地房主（无网络延迟）」，因为本地连接 rtt 恒为 0，分档无意义。

## 八、插值调优结论

**保持 D6.5 的设置不变**（`interpolatePosition/Rotation = true`）。依据是压测数据：40 物件下同步误差均值 0.019m、最大 0.050m，插值没有引入可见偏差；fps 守住 60，说明插值的 CPU 开销不是瓶颈。**没有数据支撑的调参就是破坏基线**，因此不改。

## 九、验证入口

| 目的 | 命令 |
|---|---|
| 断线阈值边界（不依赖网络） | `-d9dlogic` → `D9_LOGIC_DONE all_passed=7` |
| 断线场景房主 | `-d9dhost`（可加 `-d9dfreeze=12` 模拟冻结） |
| 断线场景客户端 | `-d9dclient` |
| 压测房主 | `-d9host -d9stress=40` |
| 压测客户端 | `-d9client -d9stress=40` |
| 装配校验 | `-executeMethod ScavengeNightmare.EditorTools.D9StabilitySetup.Validate` → `D9_VALIDATE_OK` |

## 十、已知限制

1. **房主迁移 LOCKED**：只做到"提示 + 回大厅重开"，不保留本局进度。
2. **断线检测依赖 KCP 超时**：若将来换 transport（如 Steam P2P），需重新确认其超时行为与 `lastMessageTime` 更新频率。
3. **压测只测了 1 房主 + 1 客户端**：40 物件下"1 个远程客户端"的实测带宽 451 kbps，4 人局是按 `×3` 外推的，未实测 3 客户端同时收包。D22 压测时补。
4. **预警/断线 UI 未做多语言与无障碍**：IMGUI 临时件，D32 换正式 UI。

---

## 十一、Sprint 4A 权限矩阵与踩坑（2026-10-30）

### 11.1 谁有权写

| 状态 | 类型 | 权威方 | 客户端可写 |
|---|---|---|---|
| `TeamAccount.balance / legProgress / legQuota / legIndex / quotaReached` | SyncVar | 服务端 | ❌ |
| `TeamAccount.ownedMask / visitPurchases` | SyncVar | 服务端 | ❌ |
| 购买 / 撤销 | `[Command]` → 服务端七步 | 服务端 | 仅上报意图 |
| `CartController.state / loadKg / itemCount / parked` | SyncVar | 服务端 | ❌ |
| 推车物理位姿 | `NetworkTransformReliable` ServerToClient | 服务端 | ❌ |
| `PlayerEquipment.lightOn` | SyncVar（`CmdToggleLight`） | 服务端 | 仅上报按键 |
| 关卡切换 | 服务端 `ServerChangeScene` | 服务端 | ❌ |
| 场景重建后的玩家落点 | 服务端 `LevelRouteService` | 服务端 | ❌ |

**唯一写盘出口**：`P0SaveService.TryCommit / TryCommitState`。玩法代码一律不得 `File.Write*`。
购买与撤销同样走这条出口，用**负向流水**（`value = -price`，`cargoId = shop:<pid>`）承载，
所以 `balance == SumFromTxns()` 这条恒等式在购物之后依然成立。

### 11.2 三条踩过的坑

1. **`[Command]` 不能挂在场景对象上**。场景对象没有 owner，`connectionToClient` 恒为 `null`，
   服务端复核距离永远拿到 `-1`，表现是「客户端以为在店里、服务端一律判不在店里」。
   商店的买入/撤销 Command 因此挂在 `PlayerInteract`（玩家自己拥有）上，再由它调 `ShopTerminal` 裁决。
2. **换关后组件 `Start()` 不会重跑**。NetworkManager 是 `DontDestroyOnLoad` 的，它身上挂的
   `PropSpawner` / `S4ABootstrap` 一生只执行一次 `Start()`。换关必须显式调用
   `S4ABootstrap.EnsureSceneObjects()`（→ `SpawnCart()` + `PropSpawner.RespawnForNewScene()`）。
   早先这里走 `if (TeamAccount.Instance != null) yield break`，结果是第二关进场没有车也没有货。
3. **路由摆位不能无条件做**。用例/玩法刚把玩家放到某处，0.6s 后被「挪回入口锚点」，
   一次打挂商店、推车、公秤三条用例。现在只救「还留在原点」的玩家——
   那才是 `GetStartPosition()` 拿到空表的真实故障表现。

