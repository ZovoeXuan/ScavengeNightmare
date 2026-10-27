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
