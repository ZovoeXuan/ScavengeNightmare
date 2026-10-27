# 拾荒惊魂（Scavenge Nightmare）

1-4 人合作物理拾荒微恐喜剧，Steam PC 买断制。一局 10-20 分钟：进图 → 拾荒 → 被追 → 撤离 → 结算。

> 当前状态：**D4 物理抓取完成**（2026-10-27）。抓 / 扔 / 负重三件套已可玩，4 个物件物理档数据驱动；下一步 D5 手感参数三方案。
>
> 里程碑：D1 工程脚手架 ✓ / D2 Steam 大厅 ✓（含人工验收）/ D3 联机骨架 ✓（含人工验收）/ D4 物理抓取 ✓

## 引擎与版本

| 项 | 值 |
|---|---|
| Unity | 6000.3.27f1（Unity 6.3 LTS） |
| 渲染 | URP（PC_RPAsset） |
| 语言 | C# |
| 目标平台 | Windows / Steam |
| Steamworks | Facepunch.Steamworks 2.5.2（Win64） |
| 联机（D3 起） | Mirror + KcpTransport + Steamworks(Facepunch) |
| 开发 AppID | 480（Spacewar，仅开发用） |

硬约束：禁止 HDRP、禁止 DOTS/ECS；所有可调数值走 ScriptableObject / JSON；活跃物理物 ≤ 40；Git 提交粒度 = 每天一个任务，消息格式 `#D{n} 任务名`。

## 目录结构

```
game/
├─ Assets/
│  ├─ Plugins/
│  │  ├─ Facepunch.Steamworks/  # Steamworks 2.5.2 托管 DLL + steam_api64.dll
│  │  └─ Mirror/               # Mirror v96.11.3（含 KcpTransport，第三方代码勿改）
│  ├─ Settings/              # URP 管线、Renderer、Volume Profile（模板自带，勿删）
│  ├─ InputSystem_Actions    # 输入资产（模板自带）
│  └─ _Game/
│     ├─ Scripts/            # 运行时代码（Editor/ 放编辑器脚本）
│     │  ├─ Networking/      # SteamLobbyService / PlayerMotor / D3AutoRunner
│     │  └─ UI/              # LobbyDebugUI（D2）/ NetDebugHUD（D3，均临时）
│     ├─ Prefabs/            # Player.prefab
│     ├─ Scenes/             # Main.unity 为唯一启动场景
│     ├─ Data/               # ScriptableObject / JSON 数值表
│     ├─ Art/                # 模型、贴图、材质
│     ├─ Audio/              # 音效与音乐
│     └─ UI/                 # UI 预制体与样式
├─ Packages/                 # 包依赖（manifest.json / packages-lock.json）
├─ ProjectSettings/          # 工程设置（GraphicsSettings 已指向 PC_RPAsset）
├─ Logs/                     # 本地日志（不入库）
└─ Docs/                     # 开发日志与验收记录（DevLog / SteamLobby / Networking）
```

## 打开方式

1. Unity Hub → Add → 选择 `D:\drlj\game`，用 **6000.3.27f1** 打开。
2. 首次打开等待包导入完成；打开 `Assets/_Game/Scenes/Main.unity`。
3. 直接 Play 应看到地面 + 左上角联机面板；点「Host」即可单机进图，点「Client」连接 127.0.0.1 或局域网 IP。
4. 联机细节与多开自动化参数见 `Docs/Networking.md`。

命令行验证（无需 GUI）：

```bat
"D:\Unity\6000.3.27f1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath "D:\drlj\game" ^
  -executeMethod ScavengeNightmare.EditorTools.D3SceneSetup.Validate ^
  -logFile "D:\drlj\game\Logs\d3_validate.log"
```

日志出现 `D3_VALIDATE_OK transport=kcp maxConnections=4 spawnPoints=4` 即通过。工程脚手架的老校验入口 `D1ProjectSetup.Validate` 仍可用。

## 版本管理

- Git + Git LFS：模型（fbx/obj/glb/blend）、贴图（png/psd/tga/exr）、音频（wav/mp3/ogg）、视频、DLL 走 LFS；`.unity`、`.prefab`、`.asset`、`.cs` 保持文本 diff。
- 忽略 `Library/`、`Temp/`、`Logs/`、`Builds/`、`UserSettings/`。
- 提交信息：`#D1 工程脚手架`、`#D2 Steam大厅` …

## Steam 大厅（D2）

- 开发 AppID：**480**，工程根目录 `steam_appid.txt` 必须存在；构建包需要把该文件复制到 exe 同级目录。
- 入口：`Main.unity` 里的 `SteamLobby` 物体（`SteamLobbyService` + `LobbyDebugUI`）。
- 面板：创建 4 人好友大厅、按 ID 加入、邀请好友（Steam 内置邀请面板）、复制大厅 ID、离开。
- 详细验收步骤与已知限制见 [Docs/SteamLobby.md](Docs/SteamLobby.md)。
- 发布前必须把 AppID 换成正式 ID，且不要把开发用 `steam_appid.txt` 打进商店包。

命令行冒烟测试：

```bat
"D:\Unity\6000.3.27f1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath "D:\drlj\game" ^
  -executeMethod D2SteamSmokeTest.Run ^
  -logFile "D:\drlj\game\Logs\d2_smoke.log"
```

日志出现 `D2_SMOKE_OK` 即通过（会真实创建一个 480 测试大厅后离开）。

## D1 验收记录

- [x] 工程使用 Unity 6000.3.27f1 + URP 创建
- [x] `Assets/_Game/{Scripts,Scenes,Data,Art,Audio,UI}` 目录建立
- [x] `Main.unity` 生成，含 Main Camera（URP Camera Data）与 Directional Light
- [x] Build Settings 仅登记 `Assets/_Game/Scenes/Main.unity`
- [x] Graphics Settings / Quality Settings 指向 `PC_RPAsset`
- [x] 批处理验证通过：`D1_VALIDATE_OK pipeline=PC_RPAsset`
- [x] `.gitignore` 与 Git LFS 规则就位

## D2 验收记录

- [x] Facepunch.Steamworks 2.5.2（Win64）接入，`steam_appid.txt=480`
- [x] Steam 初始化成功（本机账号 `撤步哈登`，SteamID 76561199507779783）
- [x] 创建大厅成功：`lobby=109775242490028284`，`1/4`，`host=True`
- [x] 好友邀请入口（Steam 邀请面板）、按 ID 加入、复制 ID、离开按钮
- [x] 回调改为手动泵（`asyncCallbacks:false` + `RunCallbacks`），避免 Unity 主线程死锁
- [x] Windows 构建成功，`Facepunch.Steamworks.Win64.dll` 与 `steam_api64.dll` 均进入包
- [x] 构建包运行确认 Steam 初始化成功（`steam_appid.txt` 生效）
- [ ] 双客户端好友邀请实测（需要第二个 Steam 账号/机器，按 Docs/SteamLobby.md 执行）

## 下一步

D3（10.29）：导入 Mirror + KcpTransport，房主开服 / 客户端连接，4 人胶囊体同步移动，HUD 显示延迟。
