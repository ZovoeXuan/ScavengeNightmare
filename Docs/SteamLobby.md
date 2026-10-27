# Steam 大厅（D2）

## 状态

- 库：Facepunch.Steamworks **2.5.2**（Win64，netstandard2.1）
- 开发 AppID：**480**（Spacewar）
- 大厅：**4 人上限**，FriendsOnly + Joinable，房主 = lobby owner
- 已自动验证：Steam 初始化、创建大厅、房主标记、离开、Windows 构建包含插件
- 已人工验证（2026-10-27）：两个 Steam 账号通过好友邀请进入同一大厅，成员数与房主标记正确

## 前置条件

1. Steam 客户端已启动并登录（**不同客户端需要不同 Steam 账号**）。
2. 工程根目录存在 `steam_appid.txt`，内容为 `480`。
3. 构建包需要在 exe 同级目录放一份 `steam_appid.txt`（`BuildWindows` 会自动复制）。
4. 好友邀请依赖 Steam 覆盖层：Steam 设置 → 游戏中 → 启用 Steam 覆盖层。

## 双客户端验收步骤

1. 客户端 A 打开 `Assets/_Game/Scenes/Main.unity`，Play。
2. A 点 **创建大厅（4 人）**，面板显示大厅 ID 与 `1/4`，角色为“房主”。
3. A 点 **邀请好友**，在 Steam 覆盖层选择好友 B 发送邀请。
4. B 接受邀请（Steam 聊天/覆盖层），游戏收到 `OnGameLobbyJoinRequested` 后自动加入。
   - 备用路径：A 点“复制大厅 ID”，B 在面板输入 ID 后点“加入”。
5. 两端应显示 `2/4`、成员列表包含双方、A 显示“房主”、B 显示“成员”。
6. 任一方点“离开大厅”，另一端成员数应回到 `1/4`。

> 同一台机器同一个 Steam 账号无法模拟两个玩家；需要第二台机器、第二个 Windows 用户或借用朋友账号。

## 代码结构

| 文件 | 作用 |
|---|---|
| `Assets/_Game/Scripts/Networking/SteamLobbyService.cs` | Steam 初始化、大厅创建/加入/邀请/离开、房主与成员状态 |
| `Assets/_Game/Scripts/UI/LobbyDebugUI.cs` | D2 临时 IMGUI 面板（D32 替换正式 UI） |
| `Assets/_Game/Scripts/Editor/D2SceneSetup.cs` | 把服务与面板挂到 Main 场景的 `SteamLobby` |
| `Assets/_Game/Scripts/Editor/D2SteamSmokeTest.cs` | 批处理冒烟测试（真实创建大厅后离开） |

## 关键设计

- **手动泵回调**：`SteamClient.Init(480, asyncCallbacks: false)`，在 `Update` 里调用 `SteamClient.RunCallbacks()`。Facepunch 默认后台循环会捕获 Unity 同步上下文，主线程 `Wait()` 会死锁（D2 冒烟测试实际踩到过）。
- **主线程队列**：Steam 回调先入 `ConcurrentQueue`，下一帧在主线程处理，避免在回调里直接碰 Unity API。
- **房主判定**：`lobby.IsOwnedBy(SteamClient.SteamId)`，同时写入 lobby data `host` / `hostName` 供后续 Mirror 握手使用。
- **好友邀请**：`SteamFriends.OpenGameInviteOverlay(lobby.Id)`；接受邀请后由 `SteamFriends.OnGameLobbyJoinRequested` 触发加入。
- **大厅可见性**：FriendsOnly，避免公开列表被陌生人加入；按 ID 加入作为本地测试后门。
- **4 人上限**：创建时 `CreateLobbyAsync(4)`，Steam 侧强制。

## 已知限制

- `JoinLobbyAsync` 失败只返回空，面板提示是通用原因（不存在/已满/未开放）；D9 再做细分错误与重连。
- 批处理/无覆盖层环境无法测试好友邀请面板。
- 目前只负责大厅，不含 Mirror 传输、玩家实体、场景同步（D3）。
- `steam_appid.txt=480` 仅用于开发，发布前必须替换为正式 AppID。

## 构建

```bat
"D:\Unity\6000.3.27f1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath "D:\drlj\game" ^
  -executeMethod BuildWindows.Build ^
  -logFile "D:\drlj\game\Logs\build.log"
```

输出：`Builds/Windows/ScavengeNightmare.exe`，并自动复制 `steam_appid.txt`。
