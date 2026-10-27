# 拾荒惊魂（Scavenge Nightmare）

1-4 人合作物理拾荒微恐喜剧，Steam PC 买断制。一局 10-20 分钟：进图 → 拾荒 → 被追 → 撤离 → 结算。

> 当前状态：**D1 工程脚手架完成**（2026-10-27）。空场景可运行，URP 管线生效，等待 D2 Steam 大厅接入。

## 引擎与版本

| 项 | 值 |
|---|---|
| Unity | 6000.3.27f1（Unity 6.3 LTS） |
| 渲染 | URP（PC_RPAsset） |
| 语言 | C# |
| 目标平台 | Windows / Steam |
| 联机（D3 起） | Mirror + KcpTransport + Steamworks(Facepunch) |

硬约束：禁止 HDRP、禁止 DOTS/ECS；所有可调数值走 ScriptableObject / JSON；活跃物理物 ≤ 40；Git 提交粒度 = 每天一个任务，消息格式 `#D{n} 任务名`。

## 目录结构

```
game/
├─ Assets/
│  ├─ Settings/              # URP 管线、Renderer、Volume Profile（模板自带，勿删）
│  ├─ InputSystem_Actions    # 输入资产（模板自带）
│  └─ _Game/
│     ├─ Scripts/            # 运行时代码（Editor/ 放编辑器脚本）
│     ├─ Scenes/             # Main.unity 为唯一启动场景
│     ├─ Data/               # ScriptableObject / JSON 数值表
│     ├─ Art/                # 模型、贴图、材质
│     ├─ Audio/              # 音效与音乐
│     └─ UI/                 # UI 预制体与样式
├─ Packages/                 # 包依赖（manifest.json / packages-lock.json）
├─ ProjectSettings/          # 工程设置（GraphicsSettings 已指向 PC_RPAsset）
├─ Logs/                     # 本地日志（不入库）
└─ Docs/                     # 开发日志与验收记录
```

## 打开方式

1. Unity Hub → Add → 选择 `D:\drlj\game`，用 **6000.3.27f1** 打开。
2. 首次打开等待包导入完成；打开 `Assets/_Game/Scenes/Main.unity`。
3. 直接 Play 应看到空场景（Main Camera + Directional Light）。

命令行验证（无需 GUI）：

```bat
"D:\Unity\6000.3.27f1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath "D:\drlj\game" ^
  -executeMethod D1ProjectSetup.Validate ^
  -logFile "D:\drlj\game\Logs\d1_validate.log"
```

日志出现 `D1_VALIDATE_OK pipeline=PC_RPAsset` 即通过。

## 版本管理

- Git + Git LFS：模型（fbx/obj/glb/blend）、贴图（png/psd/tga/exr）、音频（wav/mp3/ogg）、视频、DLL 走 LFS；`.unity`、`.prefab`、`.asset`、`.cs` 保持文本 diff。
- 忽略 `Library/`、`Temp/`、`Logs/`、`Builds/`、`UserSettings/`。
- 提交信息：`#D1 工程脚手架`、`#D2 Steam大厅` …

## D1 验收记录

- [x] 工程使用 Unity 6000.3.27f1 + URP 创建
- [x] `Assets/_Game/{Scripts,Scenes,Data,Art,Audio,UI}` 目录建立
- [x] `Main.unity` 生成，含 Main Camera（URP Camera Data）与 Directional Light
- [x] Build Settings 仅登记 `Assets/_Game/Scenes/Main.unity`
- [x] Graphics Settings / Quality Settings 指向 `PC_RPAsset`
- [x] 批处理验证通过：`D1_VALIDATE_OK pipeline=PC_RPAsset`
- [x] `.gitignore` 与 Git LFS 规则就位

## 下一步

D2（10.28）：接入 Facepunch.Steamworks，实现 Steam 大厅（创建 / 加入 / 好友邀请 / 离开，上限 4 人），两个客户端可互进房间。
