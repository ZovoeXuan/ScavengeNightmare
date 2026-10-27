# 开发日志

> 格式：每天三行——做了什么 / 验收是否通过 / 遗留问题。遗留问题写进次日提示词开头。

## D1 · 2026-10-27 · 工程脚手架

- **做了什么**：用 Unity 6000.3.27f1 的 3D URP 模板建立 `D:\drlj\game`；生成 `Assets/_Game/{Scripts,Scenes,Data,Art,Audio,UI}`；创建 `Main.unity`（Main Camera + Directional Light + URP Camera Data）并登记到 Build Settings；配置 Git + LFS + `.gitignore` + README。
- **验收**：通过。`D1_VALIDATE_OK pipeline=PC_RPAsset scene=Assets/_Game/Scenes/Main.unity`；Unity 批处理退出码 0；空场景可打开。
- **遗留问题**：
  1. 本机 Git 未配置 `user.name` / `user.email`，本次提交使用临时身份 `Qoder <qoder@local>`，未写入全局配置；首次推送 GitHub 前建议替换为本人身份。
  2. 里程碑 M4/M5 写“3 图 5 怪”，与范围冻结清单“更多地图不做、保持 1 张到 M5”冲突，需在 D5 前确认以哪份为准。
  3. 冻结清单未显式列出“基础 HUD / 大厅 UI / 微恐氛围（灯光+音效）”，建议补进必做或应该做，避免 D16/D32 时才补需求。

## D2 · 2026-10-27 · Steam 大厅

- **做了什么**：接入 Facepunch.Steamworks 2.5.2（Win64 + `steam_api64.dll`，AppID 480）；实现 `SteamLobbyService`（初始化 / 创建 4 人好友大厅 / 按 ID 加入 / Steam 邀请面板 / 离开 / 房主与成员状态）；添加 `LobbyDebugUI` 临时面板并挂到 `Main` 场景；新增 `D2SceneSetup`、`D2SteamSmokeTest`、`BuildWindows`。
- **验收**：通过（自动化部分）。`D2_VALIDATE_OK`；冒烟测试真实创建大厅 `lobby=109775242490028284`、`1/4`、`host=True` 后正常离开，日志 `D2_SMOKE_OK`；Windows 构建成功且 `steam_api64.dll` / `Facepunch.Steamworks.Win64.dll` 均进包；构建包以 `-batchmode` 运行确认 Steam 初始化成功（账号 `撤步哈登`）。双客户端好友邀请需第二个 Steam 账号，待人工验收（步骤见 `Docs/SteamLobby.md`）。
- **遗留问题**：
  1. `JoinLobbyAsync` 失败原因只有通用提示，D9 做错误细分与重连。
  2. Steam 覆盖层邀请面板只能在真实客户端验证；批处理只能验证按 ID 加入的 API 通路。
  3. 开发 AppID 480 与 `steam_appid.txt` 不能进入正式商店包，发布前替换。
  4. 本机 Git 仍未配置身份，D2 继续使用临时身份 `Qoder <qoder@local>`。
