# Sprint 4A 技术实施计划

> 目标：打通「单人进图 → 搬货 → 基础推车 → 公秤提交 → 永久公共资金入账 → 安全屋商店买工具 → 进入下一关」的最小闭环。
> 依据：玩法总章程 V3.0（第 17 章）+ `P0_VerticalSlice.md` v1.2 + `P0_AcceptanceGate.md` + D1–D13 技术基线。
> 范围纪律：**只写计划，不写代码、不改代码**。本文件本身是 Sprint 4A 的唯一技术入口。
>
> ## 本文通过 ≠ P0 通过
>
> **P0 的范围由 `P0_VerticalSlice.md` 定义，包含 1 只怪物「盘点员」、2 种诅咒、8 件商店商品、3 关梯度；P0 是否通过由该规格的 A1–A12 判定，不由本文判定。**
> 本文是 **Sprint 4A**：只做闭环骨架，不含 4B 的怪物 / 诅咒 / 8 件商品 / 3 关梯度。
> 因此本文所有「不做」都写作 **「4A 之外」**，不是「P0 之外」。4A 收口时的对外措辞只能是
> **「数据层与物理层已交卷，体验门未开」**（design-strategist v1.1 明确要求的口径），不许说「P0 过半」。
>
> 明确 4A 之外：怪物与 AI、诅咒、攻击性推车改件（撞刺/震荡）、四种难度、新增 18 种物件、8 名角色、房主迁移、暂停、工具槽与耐力系统。
>
> **版本记录**
> - **v1.4（工程侧一致性收口，本版为交付版）**：① 总估时定 **12.5 人日**（S4A-3 4.0，删除"不脱困 12 人日"旧分支）；② 推车噪音曲线 3.0 m/s 由 18/s 降到 **7/s**，18–50 留给超载/甩落/撞击脉冲（避免 4B「全速推 5 秒必追猎」）；③ `GATE_CART_STALL` 加 `rescued=(self|unstick|fail)` 三态；④ `S4A_CARRY_CAP` 补 `OK`/`SKIP` 两种输出行；⑤ 脱困长按明确 **1.0 s**（与提交长按 1.2 s 区分）；⑥ 补「免费自救针 1 支/关是系统基线、不是商品」；⑦ 新增 T19：4B 恢复 `TraitBootstrap` 前必须先补「身份×牌×商品同轴只叠一次」的离线断言；⑧ T5 补「A10 节奏证据不得从 4A 取」；⑨ 抑制同步注入统一命名 `-s4asyncbreak`；⑩ 过期口径全清（6 件/18 锚点/v1.0/3.18/1.0s 提交）。
> - **v1.3（主理人最终汇编）**：补齐 16 锚点固定构成与类型计数断言；`ShopCatalog` 类注释 6→4；打包布带滑移阈值改为按载重缩放，避免满载永远不滑的假购买；对齐 `P0_VerticalSlice.md` v1.2 的整车提交与 4A 临时配额。
> - **v1.2（吸收 team-lead 初审 3 项 + design v1.1 3 项 + QA 5 处口径冲突与 11 项钩子）**：
>   ① 全文「P0 之外」→「4A 之外」，新增「本文通过 ≠ P0 通过」声明；② 修存档轮换顺序 bug（先 bak→bak2 再 Replace）并补 `Flush(true)`；
>   ③ 锚点数量参数化 + 标注「4A 临时口径」；④ 新增 `TraitBootstrap` 中性化（否则 D11 3.68 与 42kg 减速全被身份倍率污染）；
>   ⑤ 修 D4 回归基线（light 主方案下是 **4.14**）；⑥ 提交长按确认为 **1.2 s**；
>   ⑦ 过关额改为**从数据表读**（缺省才回退 `×0.55`）；⑧ 推车脱困规则按 design v1.1 §2.7 落地；
>   ⑨ `S4A_CARRY_CAP` 改为**按质量分段 + 硬钳制**（原判据会误杀全部轻件）；⑩ 新增 HK-01…HK-14 钩子映射与全部 `GATE_*` 日志契约。
> - v1.1：商品 6→4 件、`maxCargoCount`、超载 0.5kg 容差、锚点改 12 件池、强杀改 `taskkill /F`、A1–A12 覆盖表。


---

## 一、现状

### 1.1 已具备（可直接复用，不再造）

| 能力 | 落点 | 验证证据 |
|---|---|---|
| Unity 6000.3.27f1 + URP 3D 工程 | `Assets/_Game/` | D1 `D1_VALIDATE_OK` |
| Steam 大厅（4 人、邀请、房主标记） | `Networking/SteamLobbyService.cs` | D2 人工验收通过 |
| Mirror v96.11.3 + KcpTransport 7777，`maxConnections=4` | `Main.unity` / `Supermarket.unity` 的 NetworkManager | D3 `conns=4 players=4` |
| 玩家移动（客户端权威）+ 俯仰上报 | `Networking/PlayerMotor.cs` | D3 速率 3.2 m/s、D8 身份倍率 |
| 20 种物件参数表 + prefab + 注册 | `Gameplay/PropPhysicsProfile.cs`、`Assets/_Game/Data/Props/` | D13 `profiles=20 prefabs=20 registered=20` |
| 物件服务端权威抓取/投掷 | `Gameplay/GrabController.cs`、`NetworkedProp.cs` | D6.5 同刻误差 0.28m |
| 物件按场景锚点生成 | `Gameplay/PropSpawner.cs` + `PropSpawnAnchor.cs` | D12 `D65_SPAWNED count=18 source=anchors` |
| 五条搬运摩擦（卡位/砸脚/滚动/夹手/易碎） | `Gameplay/*Friction*`、`DoorTrap`、`FragileProp`、`RollingProp` | D6 五条全过 |
| 撤离区进入检测 + 结算总额 SyncVar | `Gameplay/EvacZone.cs`、`EvacSettlement.cs` | D13 `D13_DELIVER … total=1870` |
| 断线检测/提示/带宽口径 | `Networking/NetworkHealthService.cs`、`UI/ConnectionStatusHUD.cs` | D9 131ms / 3988ms |
| 身份与缺点牌 | `Scripts/Traits/*` | D8 15 项联机全过 |
| 40×40 白盒地图（3 路径 / 2 窄门 / 楼梯 / 3 拾荒区 / 撤离点） | `Scenes/Supermarket.unity` | D12 `D12_VALIDATE_OK … stairTop=3.0m` |

### 1.2 完全缺失（4A 要新建的全部东西）

- **没有任何存档**。工程里零 `persistentDataPath` 写入、零序列化存档。资金、进度、购买全部无载体。
- **没有账户/货币概念**。`EvacSettlement` 只是一个 SyncVar 累加器，不落盘、不跨局、不跨场景。
- **没有推车**。
- **没有商店**。所有参数（手感方案、身份、缺点牌）是「全局数据表」，不存在「谁买了什么」。
- **没有场景切换**。Build Settings 只登记了 `Main.unity`；`Supermarket.unity` 靠 `BuildWindows -map=supermarket` 单场景打包。一局 = 一个场景，没有任何跨场景状态。
- **没有噪音系统**（V3.0 17.11）。有的只是 `FrictionEvents.Report` 这种一次性事件广播，无累积值、无来源、无衰减。
- **没有关卡概念**。没有 legIndex、没有过关额、没有过关判定。
- **没有交互提示层**。玩家看不到「按 E 提交」「需先卸货」这类文案，只有 `FrictionHUD` 的摩擦事件流水。

### 1.3 两条不可动的基线

| 场景 | 角色 | 纪律 |
|---|---|---|
| `Scenes/Main.unity` | D3/D4/D6/D8/D9/D11 全部自动化用例的沙盒基线 | **一行不动**。任何 4A 改动若改变 Main 上的行为即视为破坏 |
| `Scenes/Supermarket.unity` | D12 白盒 + D13 结算验收基线，由 `D12SceneSetup` **代码生成**（`CleanSandbox` 会删掉一切非 `KeepRoots` 的根对象） | **不手改**。手改会被下一次 `Setup` 抹掉 |

> **由此推导出 4A 的场景策略**：新开 `Night1.unity`（夜路）与 `Safehouse.unity`（安全屋），Main 与 Supermarket 原样保留为回归基线。理由见 §2.3。

---

## 二、架构

### 2.1 分层

```
[表现层]  UI/*HUD (IMGUI 临时)          —— 只读 SyncVar，不持有游戏状态
[流程层]  Flow/LevelRouteService        —— 关卡序号、过关判定、场景切换
          Flow/SafehouseSession         —— 收工 / 继续 / 撤销窗口
[玩法层]  Economy/QuotaTerminal         —— 公秤提交入口（唯一入账口）
          Cart/CartController+CartCargo —— 推车物理与承载
          Shop/ShopService              —— 购买事务
          Economy/EquippedEffects       —— 已购商品 → 数值乘子注入
[账本层]  Economy/TeamAccount           —— 余额 / 交关进度 / 购买记录（唯一真值）
          Save/P0SaveService            —— 原子写盘 / 备份 / 事务 / 恢复
[基础设施] Noise/NoiseBus               —— 噪音采集（无 AI 消费方）
          Flow/S4AGameRoot              —— 跨场景常驻容器
```

**分层铁律**：
- 只有 `TeamAccount` 能改余额。任何其它模块写余额都是 bug。
- 只有 `P0SaveService` 能碰磁盘。玩法代码不得 `File.Write*`。
- 只有 `QuotaTerminal` 能触发入账。别处调用 `TeamAccount.Deposit` 一律视为绕过。
- `EquippedEffects` 是唯一「购买 → 玩法数值」的映射点，商品效果不许散落在 `PlayerMotor`/`GrabController` 里各写一份。

### 2.2 新增组件清单（每个类一个文件 —— D12 踩坑教训）

| 文件 | 基类 | 权威方 | 职责 |
|---|---|---|---|
| `Scripts/Flow/S4AGameRoot.cs` | NetworkBehaviour | 服务端 | 跨场景常驻容器标记；运行时 Spawn 的宿主 |
| `Scripts/Flow/S4ABootstrap.cs` | MonoBehaviour（挂 NetworkManager） | 服务端 | 启动时实例化 GameRoot 并 `NetworkServer.Spawn` |
| `Scripts/Flow/LevelRouteService.cs` | NetworkBehaviour | 服务端 | legIndex / quota / progress / 场景切换 |
| `Scripts/Flow/LevelPortal.cs` | NetworkBehaviour | 服务端 | 撤离门触发器：进度达标才允许进安全屋 |
| `Scripts/Flow/SafehouseSession.cs` | NetworkBehaviour | 服务端 | 收工 / 继续 / 购买撤销窗口 |
| `Scripts/Economy/TeamAccount.cs` | NetworkBehaviour | 服务端 | 余额、累计收支、交关进度、购买记录（唯一账本） |
| `Scripts/Economy/QuotaTerminal.cs` | NetworkBehaviour | 服务端 | 公秤：提交手持货 / 提交车上货 |
| `Scripts/Economy/CargoIdentity.cs` | NetworkBehaviour | 服务端 | 货物唯一实例 ID（防重复入账） |
| `Scripts/Economy/EquippedEffects.cs` | MonoBehaviour（GameRoot 子物体） | 服务端下发 + 客户端读 | 已购商品 → 乘子查询 |
| `Scripts/Cart/CartController.cs` | NetworkBehaviour | 服务端 | 展开/折叠/推行/把手/噪音 |
| `Scripts/Cart/CartCargo.cs` | MonoBehaviour（挂车） | 服务端 | 载重汇总、板上货物清单、滑落判定 |
| `Scripts/Cart/CartProfile.cs` | ScriptableObject | — | 推车全部数值（质量/尺寸/速度/转向/噪音曲线） |
| `Scripts/Cart/CartSpawnAnchor.cs` | MonoBehaviour | — | 场景摆位（同 `PropSpawnAnchor` 模式） |
| `Scripts/Save/P0SaveService.cs` | MonoBehaviour（GameRoot 子物体） | 服务端（仅 Host 磁盘） | 原子写 / 备份 / 事务 / 恢复 / 单实例锁 |
| `Scripts/Save/TeamSaveData.cs` | `[Serializable]` 纯数据 | — | 存档 schema |
| `Scripts/Save/SaveTxn.cs` | 纯数据 | — | 事务定义（Deposit / Purchase / Revoke / LegAdvance） |
| `Scripts/Shop/ShopCatalog.cs` | ScriptableObject | — | 商品表（4 件） |
| `Scripts/Shop/ShopItem.cs` | ScriptableObject | — | 单件商品（id/价/类/效果键） |
| `Scripts/Shop/ShopService.cs` | NetworkBehaviour | 服务端 | 购买 / 撤销 / 已购查询 |
| `Scripts/Noise/NoiseBus.cs` | 静态类（服务端） | 服务端 | 0–100 累积噪音、来源归因、自然衰减 |
| `Scripts/UI/WalletHUD.cs` | MonoBehaviour | 客户端 | 余额 / 交关进度 / 持有状态 |
| `Scripts/UI/QuotaHUD.cs` | MonoBehaviour | 客户端 | 公秤交互提示与提交反馈 |
| `Scripts/UI/CartHUD.cs` | MonoBehaviour | 客户端 | 载重 / 折叠状态 / 噪音来源 |
| `Scripts/UI/ShopHUD.cs` | MonoBehaviour | 客户端 | 商店列表 / 余额 / 购买 / 撤销 |
| `Scripts/Editor/S4ASceneSetup.cs` | static Editor | — | 生成 Night1 + Safehouse，幂等 + 校验 |
| `Scripts/Networking/S4ADepositTest.cs` | MonoBehaviour | — | `-s4adeposit` |
| `Scripts/Networking/S4ASaveTest.cs` | MonoBehaviour | — | `-s4asave` / `-s4asave-verify` |
| `Scripts/Networking/S4ACartTest.cs` | MonoBehaviour | — | `-s4acart` |
| `Scripts/Networking/S4AShopTest.cs` | MonoBehaviour | — | `-s4ashop` |
| `Scripts/Networking/S4ALoopTest.cs` | MonoBehaviour | — | `-s4aloop`（单人）/ `-s4aloopclient`（双人） |
| `Scripts/Networking/S4ACarryCapTest.cs` | MonoBehaviour | — | `-s4acarrycap`，按质量分段断言携带速度钳制 |
| `Scripts/Networking/S4ASyncProbeTest.cs` | MonoBehaviour | — | `-s4asyncbreak`，抑制一次余额广播以证明 `GATE_WALLET_SYNC` 探针有效 |
| `Scripts/Networking/S4APerfTest.cs` | MonoBehaviour | — | `-s4aperf`，复用 `NetworkDiagnostics` 出帧率/kbps/物件数 |

**对既有脚本的全部改动，只有这 4 处，且都是「加默认关闭的开关」型**：

| 文件 | 改动 | 为什么安全 |
|---|---|---|
| `GrabController.cs` | 新增 `SetHandsBusy(bool)` + `RequestGrab` 首行判 busy 直接返回 | Main / Supermarket 无人调用 → `handsBusy` 恒 false，行为不变 |
| `PlayerMotor.cs` | 新增 `SetExternalSpeedCap(float)`，在 `ApplyMove` 末尾对 `speed` 取 `min` | 默认 `float.PositiveInfinity`，行为不变 |
| `PropPhysicsProfile.cs` | 新增序列化字段 `cargoVolumeHint`（默认 `Vector3.zero` = 不参与计算） | 纯新增字段，既有计算一律不读它 |
| `BuildWindows.cs` | 新增 `-map=sprint4a` 分支（两场景打包到 `Builds/Sprint4A/`） | 默认分支零改动 |

> 纪律：**任何其它既有脚本都不得修改**。需要新行为一律加新文件。

### 2.3 场景与构建

| 场景 | 来源 | 生成方式 | 用途 |
|---|---|---|---|
| `Scenes/Main.unity` | 已有 | 不动 | 回归基线 |
| `Scenes/Supermarket.unity` | 已有 | `D12SceneSetup` | 回归基线（D12/D13） |
| `Scenes/Night1.unity` | **从 Supermarket 复制** | `S4ASceneSetup`：先跑 `D12SceneSetup.Setup` 保证基线最新 → `AssetDatabase.CopyAsset` → 追加 4A 增量 | 夜路（可反复重载 = 无限夜路） |
| `Scenes/Safehouse.unity` | **从 Main 复制骨架** | `S4ASceneSetup`：复制 → 清几何 → 生成 12×10m 安全屋 + 商店终端 + 返回门 | 安全屋 |

Night1 的**增量**（不动已有几何，只加）：
1. `CartSpawnAnchor` × 1（入口内侧，z≈+17）
2. `QuotaTerminal` × 1（收银区旁主通道，x≈+6, z≈+2）
3. `CartParkingZone`（公秤前的停车位 Trigger，1.8×2.6m）
4. `LevelPortal` × 1（后门撤离点原位 x=0,z=-18.5）
5. **删除**从 Supermarket 继承来的 `EvacZone` 与 `EvacSettlement` 实物（不删脚本）
6. **禁用 `TraitBootstrap`**（见 §2.6，这条不做会让 D11 全部基线失真）
7. 锚点按 `P0_VerticalSlice.md` §3.1 的 12 件物件池重配，**数量参数化**：`S4ASceneSetup -anchors=<N>`，缺省 **16**。做法是**先删除从 Supermarket 继承的 18 个 `PropSpawnAnchor`，再按 12 件池 + 参数重新生成**（幂等，重跑不叠加）。

   **16 个锚点的默认固定构成（必须由 `S4ASceneSetup` 写死，并进 `S4A_VALIDATE_OK` 的类型计数断言）**：

   | 类型 | 数量 | 具体物件 |
   |---|---:|---|
   | 普通 | 6 | 罐头×3、纸箱×2、工具箱×1 |
   | 易碎 | 4 | 花瓶×1、老电视×1、玻璃杯×2 |
   | 滚动 | 3 | 圆桶×1、轮胎×1、西瓜×1 |
   | 重型 | 3 | 雕像×1、大衣柜×1、保险箱×1 |

   地图总价值约 **4785†**。**4A 两关统一配额 2632**（= 4785 × 0.55，仍在普通模式 50–60% 区间内），作为 `LevelQuotaTable` 的 4A 值；两关同配额才与 §5.2「4A 的关卡差异 = 仅 legIndex 与文案」自洽。

   > **不要再写成 1800 / 2400。** 那两个数是 P0 规格 §3.3 给 **3155 / 4365** 两张图的值（57% / 55%）。如果把 1800 套到 4785 的图上，占比只有 **37.6%**，玩家搬两件重货就达标 —— 4A 会比 P0 实际容易约 1.5 倍，而这正是 4B 最容易出现的误判（"4A 明明很好过，加了怪物怎么这么难"）。P0 的 1800 / 2400 / 2800 仍然有效，**但只配它自己那三张图**。

   这套构成保证 A3 至少有三件重型件可测、A4 能凑出保险箱 60 + 雕像 42 + 老电视 12 = 114kg 单层载货、打包布带能在满载急转时产生可区分的滑移事件。

> **4A 临时口径声明（必须写进交付报告）**：4A 用 16 个锚点、12 件物池、单一关卡配置，这是**闭环验证口径**，不等于 P0 的节奏与选型。P0 的三关分别是 12 / 14 / 16 个锚点、不同过关额与诅咒数量（`P0_VerticalSlice.md` §3.3）。4B 只改 `-anchors=` 参数与配额表，不重写场景生成代码。
> 6 种新货（工具箱/老电视/玻璃杯/轮胎/西瓜/保险箱）的 prefab 已在 `NetworkManager.spawnPrefabs` 注册，只是 Supermarket 没摆锚点，`S4ASceneSetup` 给锚点写 `profileId` 即可刷出，**零额外成本**。

**为什么不直接改 Supermarket**：`D12SceneSetup.CleanSandbox` 会删掉一切非 `KeepRoots` 的根对象，手改必被抹；而改 `D12SceneSetup` 会让 D12 基线漂移。复制新场景是唯一能同时保住两条基线的做法，且 D12 已经用同样的套路成功过一次（复制 Main → Supermarket）。

**构建**：`BuildWindows.Build` 加 `-map=sprint4a`：
```
scenes = [ Night1.unity, Safehouse.unity ]   // Night1 索引 0
output = Builds/Sprint4A/ScavengeNightmare.exe
NetworkManager.onlineScene = "Night1"; offlineScene = "";
```

### 2.4 Mirror 权限矩阵（4A 新增部分）

| 状态 | 权威方 | 机制 | 理由 / 依据 |
|---|---|---|---|
| 玩家位置朝向 | **客户端** | `NetworkTransformReliable` ClientToServer | D3 定案，手感优先，不改 |
| 玩家俯仰 | 客户端→服务端 | 10Hz `Cmd` → `SyncVar syncPitch` | 服务端算准星用 |
| 物件物理位姿 | **服务端** | `NetworkTransform` 固定 ServerToClient | D6.5 血案：运行期翻方向会崩（差 8.67m） |
| 物件持有关系 | **服务端** | `SyncVar isHolding/heldMass` + `Command` | 防隔空抓 |
| 易碎碎裂 | **服务端判定** | `RpcShatter` | 四端同时碎 |
| **推车物理位姿** | **服务端** | `NetworkTransform` ServerToClient | 同物件；车不能被两个客户端各算一套 |
| **推车折叠状态/载重** | **服务端** | `SyncVar cartState/loadKg` + `Command` | 载重影响速度与噪音，必须单真值 |
| **推车推行意图** | 客户端→服务端 | `CmdCartInput(bool push, float yaw)` | 客户端只发意图，不算物理 |
| **推车人限速** | **服务端→客户端** | `SyncVar pushSpeedCap`，本地 `SetExternalSpeedCap` | 防止人跑出车导致拖拽抖动 |
| **噪音值/来源** | **服务端** | `SyncVar noiseLevel/noiseSource` | 必须可归因、单一真值 |
| **公共资金余额** | **服务端（Host 磁盘）** | `SyncVar long balance` | 防复制；客户端永不写 |
| **交关进度 / 配额** | **服务端** | `SyncVar legProgress/legQuota` | 同一提交事件驱动 |
| **购买记录** | **服务端** | `SyncVar` + 全量重发（条数少） | 防刷；撤销也要落盘 |
| **关卡序号 / 场景** | **服务端** | `SyncVar legIndex` | 客户端不能自己跳关 |
| **安全屋会话状态** | **服务端** | `SyncVar enum phase` | 收工/继续需全队确认 |
| **货物唯一 ID** | **服务端** | `SyncVar string cargoId` | 客户端不得自行声明 |
| 手电开关（商品效果） | 客户端 | 本地表现 | P0 无玩法影响 |
| HUD / 准星 / 交互提示 | 客户端 | 本地 | 不持有游戏状态 |

### 2.5 已踩过的坑（4A 必须按这个清单自检）

1. **Host 本地连接计入连接数**。`maxConnections=4` 的实际含义是「Host 的本地连接 + 3 个远程客户端」。单人 = 1 连接、双人 = 2。`NetworkServer.connections.Count` 含本地连接；带宽必须按 `count - 1` 算（D9 首版口径错，`avg_up=446` 是含本地的高估值）。**任何「还差几个人」的 UI 都不许直接拿 lobby `MemberCount` 和 `connections.Count` 相加**。
2. **`NetworkTransform.syncDirection` 不能运行时翻转**。推车与货物一律固定 ServerToClient，运行时一次都不许改。
3. **场景里摆的 prefab 实例拿不到 sceneId**。所有联网物件/推车必须服务端运行时 `NetworkServer.Spawn`（D6.5 已确立）。场景里只摆锚点。
4. **prefab 的 assetId 不自动落盘**。新增 Cart prefab 必须用 `NetworkIdentity.AssetGuidToUint(GUID)` 写序列化字段并按文件内容校验。
5. **一个 `.cs` 放多个 MonoBehaviour 会让场景脚本引用丢 guid**。本计划所有新类一文件一类，不许合并。
6. **批处理必须 `-fps=60`**。无头不限帧时单帧位移 < `skinWidth` 被物理吞掉，会误判「推车不走」。
7. **改 `NetworkManager.transport` 要同时改 `Transport.active`**（D11 假通过教训）。
8. **瞬移玩家必须连货带车一起挪**。只挪玩家 → 服务端 `breakDistance` 首帧判脱手（D13 假通过教训）。
9. **改场景文件前必须先关场景，统计要重开场景后再数**（D12 教训）。`S4ASceneSetup.Validate` 必须重开场景统计。
10. **静态 `Instance` 在场景重载后会指向已销毁对象**。所有 `Instance` 的 `OnDestroy` 必须判 `Instance == this` 再置空（`EvacSettlement` 已是此写法，照抄）。

### 2.6 身份/缺点牌必须中性化（否则 D11 整条基线失真）

`Night1.unity` 从 `Supermarket.unity` 复制会**继承 `TraitBootstrap`**，而 `PlayerTraits` 默认会自动分配身份 × 缺点牌（除非 `TraitSettings.ForceNoTraits` 或命令行 `-notraits`）。P0 规格明确「不实现角色选择，只有 4 色染色」。

**如果 4A 不显式中性化，后果是静默的**：D11 的 3.68 m/s 空手速度、42kg 减速 42%、D4 投掷 4.14、每件货的负重手感，全部会被身份倍率（`SpeedScale` / `CarryDivisor` / `ThrowScale`）污染，出现「同一件货不同局不同速度」的假数据，而所有断言仍然"通过"——因为它们比较的是被污染后的值。

**做法（选第一种，场景层面写死，不依赖传参）**：
1. `S4ASceneSetup` 在生成 Night1 / Safehouse 时**删除 `TraitBootstrap` 根对象**（`KeepRoots` 的反向操作）。
2. `S4ASceneSetup.Validate` 断言 `FindAnyObjectByType<TraitBootstrap>() == null` → 输出 `S4A_VALIDATE_OK … traits=none`。
3. 测试钩子启动时再断言一次 `TraitSettings.ForceNoTraits == true`（双保险：万一场景被手改回来，测试会立刻炸而不是悄悄用倍率跑）。
4. 回归脚本第一行打 `S4A_NEUTRAL_OK traits=none feel=light`，两条任一不满足直接退出。
5. **Main / Supermarket 的 D8 路径保持原样**，一行不动 —— 这条只作用于 4A 新建的两个场景。

> Story 1 完成定义（CD6）与 Story 5 回归（CD3）都含这条断言。没有它，4A 的全部数值证据都不可信。

---

## 三、数据与存档（含公秤入账链路）

### 3.1 资金口径（先定死，再写代码）

V3.0 17.4：**一笔提交的 value 同时进入「本关交关进度」和「永久公共资金」，不额外扣税**。

```
ServerDeposit(cargo) :
  value = cargo.CurrentValue              // 读 GrabbableProp.CurrentValue，易碎碎后为 0
  reject if cargoId ∈ currentLegSubmitted  // 幂等：同一件货只能提交一次
  reject if value <= 0                     // 碎成 0 的货不入账，也不能刷「提交次数」
  ok = TryCommit(Deposit{ value, cargoId }) // 事务内部改 balance/legProgress 并落盘
  if (!ok) → 提示失败，什么都不发生
  → 销毁货物
  → RpcTxnConfirmed(balance, legProgress)
```

- **唯一入账口**：`QuotaTerminal`。`EvacSettlement` 在 Night1 里不存在（已删实物），在 Supermarket 里保留仅服务 D13 回归。
- **口径统一**：`CurrentValue` 而不是 `Profile.value` —— 易碎件碎后归 0，这条 D13 已经验过，直接沿用。
- **已提交不能拿回**：提交成功即 `NetworkServer.Destroy(prop)`，与 D13 `Deliver` 同一套动作。

### 3.2 存档 schema（`TeamSaveData`，JSON）

```jsonc
{
  "schemaVersion": 1,
  "teamId": "8f1c…",                  // 首次创建生成，队伍档案的唯一身份
  "createdAtUtc": "2026-10-30T09:12:00Z",
  "updatedAtUtc": "2026-10-30T09:41:22Z",
  "txnSeq": 12,                        // 单调递增，每次提交事务 +1
  "wallet": { "balance": 3050, "lifetimeEarned": 4950, "lifetimeSpent": 1900 },
  "run": { "runId": "b73e…", "legIndex": 2, "deepestLeg": 2, "startedAtUtc": "…" },
  "purchases": [
    { "purchaseId": "a1…", "itemId": "grip_gloves", "price": 300,
      "atUtc": "…", "state": "Active" }          // Active | Revoked
  ],
  "legCargoSubmitted": ["b73e-2-1041", "b73e-2-1042"]   // 只保留当前 leg，换 leg 时清空
}
```

- **`int` 不够用**：余额与累计都用 `long`。JSON 里写数字，读时按 `long` 解。
- **`purchases` 只增不删**，撤销是置 `state=Revoked`（保留账目可审计，别删记录）。
- **`legCargoSubmitted` 按 leg 清理**：同一件货跨关重生成会拿到新 `cargoId`，无需保留历史。

### 3.3 路径与写入

| 项 | 值 |
|---|---|
| 根目录 | `Application.persistentDataPath/Saves/teams/` （Win：`%USERPROFILE%\AppData\LocalLow\<company>\<product>\Saves\teams\`） |
| 主档 | `team_default.sav` |
| 备份 | `team_default.sav.bak`（上一版）、`team_default.sav.bak2`（上上版） |
| 临时 | `team_default.sav.tmp` |
| 锁 | `.lock`（`FileShare.None` 独占打开，进程生死由 OS 释放） |
| 损坏留证 | `team_default.corrupt-<yyyyMMdd_HHmmss>.sav`（**改名保留，绝不覆盖**） |

**P0 只有一个队伍档案 `team_default`**。多队伍、加入他人队伍、「用别人队伍档案不带钱回来」全部留 P1 —— 但 schema 里 `teamId` 字段现在就留着，避免以后迁移格式。

### 3.4 事务顺序（「不丢不复制」的全部推理）

```csharp
bool TryCommit(SaveTxn txn)
{
    TeamSaveData next = current.Clone();
    if (!txn.Apply(next)) return false;             // 业务校验失败（余额不足/已拥有/已撤销）
    next.txnSeq = current.txnSeq + 1;
    next.updatedAtUtc = DateTime.UtcNow;

    if (!WriteAtomic(next)) return false;           // ★ 先落盘
    current = next;                                 // ★ 落盘成功才改内存
    ApplyAuthoritativeSideEffects(txn);             // 改 SyncVar / 销毁货物 / 应用效果
    RpcTxnConfirmed(txn);                           // 广播
    return true;
}
```

**为什么是这个顺序**：
- 若「先改内存再落盘」：玩家 HUD 上钱涨了 → 落盘失败 → 重启后钱消失。**丢账**。
- 若「先落盘再改内存」：落盘成功后在改内存前被强杀 → 重启读盘 = 已入账，正确。**不丢**。
- 复制是不可能的：余额永远只是「盘上一次值」，没有任何「累加两次」的路径；客户端从不提交金额，只提交「我要提交这件货」，金额由服务端自己读。

**原子写**（注意轮换顺序，写反会让三级备份链断掉）：
```
1. 序列化 next → 写 sav.tmp
   FileStream.Flush(flushToDisk: true)   // ★ 必须是 true。普通 Flush() 只清托管缓冲，
                                          //   不做 FlushFileBuffers，扛不住强杀与掉电
   关闭流
2. 轮换备份（★ 必须在 Replace 之前）：
   若 bak 存在 → File.Copy(bak, bak2, overwrite: true)   // 逐级下移：上一代 bak 变成 bak2
   若 bak 不存在 → 跳过（首次写盘没有上一代）
3. 若 sav 存在 → File.Replace(tmp, sav, bak)   // NTFS 原子替换，旧 sav 进 bak
   若 sav 不存在 → File.Move(tmp, sav)
4. 任一步抛异常 → 删 tmp，返回 false（内存不动）
```
> **为什么第 2 步必须在第 3 步之前**：`File.Replace(tmp, sav, bak)` 会把**旧 sav** 写进 `bak`，也就是 `bak` 在这一步被覆盖。如果在 Replace **之后**才做「bak → bak2」，复制到 `bak2` 的其实是"上一版 sav"，真正的**上一代 bak** 已经丢了 —— `sav → bak → bak2` 三级链就变成了 `sav → sav-1 → sav-1` 的两级链，坏了两次的档就救不回来。
> 正确顺序下，任一时刻盘上至少有三种不同代际：`sav`（当前）、`bak`（上一版）、`bak2`（上上版）。

如果进程在步骤 1 中途被杀：只有 `.tmp` 是坏的，`sav` 完好。启动时发现残留 `.tmp` **直接删除**（它从未被承认）。

**`S4A_SAVE_FAIL` 行**：`TryCommit` 的每个失败出口都必须打印 `S4A_SAVE_FAIL reason=(io_error|no_space|nonatomic_fallback|locked|schema)`，否则「写盘失败后内存回滚」无法被断言（QA HK-05 要求）。

### 3.5 读档与恢复

```
TryLoad():
  对 sav → bak → bak2 依次尝试：
    解析成功且 schemaVersion 认识 → 采用，返回
    解析失败 / 版本不认识 → 记 SAVE_LOAD_FAIL source=xxx，继续下一个
  全部失败：
    把 sav 改名成 corrupt-<ts>.sav（保留证据）
    新建空档案（balance=0, legIndex=1）并立即写盘
    记 SAVE_CORRUPT_RECOVERED（不是静默重置，HUD 必须提示玩家）
  成功但来源不是 sav（用了 bak）：
    立刻用恢复内容写一次 sav，恢复主档
    记 SAVE_RECOVERED from=bak
```

**单实例锁**：拿不到 `.lock` → 进入**只读模式**：`canSpend = false`，HUD 常驻 `SAVE_LOCKED：存档被其他进程占用，购买与入账已禁用`。**不允许假装能存**。

**`txnSeq` 倒退检测**：读到的 `txnSeq < 内存里的` → 说明发生过回滚，记 `SAVE_SEQ_REGRESS` 告警，**以盘为准**（盘是真相）。

### 3.6 强杀/重连/退出各自的语义

| 情况 | 结果 |
|---|---|
| 正常退出（`Application.quitting`） | 不额外做事。每次都实时写盘，退出时无脏数据可刷 |
| 强杀（任务管理器/崩溃） | 最后一次成功事务为准。`.tmp` 残留被清理。**不丢已入账，不产生重复** |
| 客户端掉线重连 | 客户端重连后由服务端下发 `TeamAccount` 全量 `SyncVar`；客户端本地**没有任何账本**，所以重连不会造成双份 |
| Host 掉线/退出 | 本局结束（V3.0：房主退出仍结束本局，房主迁移 LOCKED）。资金已落盘，下次开房继续 |
| 同机双开 | 第二个进程拿到 `SAVE_LOCKED` 只读模式 |

### 3.7 公秤 `QuotaTerminal` 交互

```
每帧检测（仅本地玩家）：玩家站在秤前 2.2m 内且面向秤（点积 > 0.6）
提示：
  空手 + 车不在停车位   → 「公秤：手上没有货」
  手持货                → 「按 E 提交 <名称>（价值 <v>）」
  车在停车位且有货      → 「按 E 提交车上 N 件（合计 <v>）」→ 长按 **1.2s** 确认（防误触，`P0_VerticalSlice.md` §17.16）
服务端校验：距离 + 视线 + 长按时长 + 货物归属（必须真在这辆车上/这个人手上）
成功后：货物销毁、钱入账、秤体闪一下 + 一条 HUD 流水「+<v>  余额 <b> / 进度 <p>/<q>」
```

- **不吸附近物品**（V3.0 明令）：只提交「手上」或「停车位里这辆车上」的货。
- **推车不会被收走**。P0 简化为「车上货一次全提交」；V3.0 的「挑件提交、未选中的留在车上继续带走」留 P1（见 §8 T5）。
- **提交按钮与使用按钮分离**（V3.0 17.8）：公秤交互与「抓取」是不同按键，绝不共用鼠标左键。

---

## 四、推车 v0

### 4.1 不做清单（先划清，避免范围膨胀）

**不做**：折叠撞刺、震荡模块、货物护板、绑带架、工具挂架、轮胎升级、推车耐久/损坏/维修、真实刚体侧翻、车上楼梯、自动拾取、物品栏式载货。

V3.0 明确「不做轮胎升级」「撞击、颠簸」属 P1 改件；「楼梯必须卸货折叠搬车」本身就是设计要求，所以 **4A 根本不实现「推车爬台阶」**，只实现「台阶前必须折叠」。

### 4.2 物理模型

**Prefab `Assets/_Game/Prefabs/Cart.prefab`**

| 部件 | 配置 |
|---|---|
| `Rigidbody` | mass 40（远大于货，防被货推走）；damping 0.5/2.0；`interpolation=Interpolate`；`collisionDetection=ContinuousDynamic` |
| 车板 `BoxCollider` | 1.3 × 0.08 × 0.75，中心 y = 0.36；物理材质 dynamicFriction 0.9 / bounciness 0 |
| 四个轮 `SphereCollider` | r=0.12，位于四角，物理材质 dynamicFriction 0.35（能滚动但会打滑） |
| 把手 `BoxCollider` | 0.9 × 0.06 × 0.06，y=0.95，仅用于交互射线与推车人判定，不参与载货 |
| `NetworkIdentity` + `NetworkTransformReliable`（ServerToClient，30Hz） | assetId 用 `AssetGuidToUint` 写入 |
| `CartController` + `CartCargo` | 见下 |

**为什么不用 `WheelCollider`**：WheelCollider 的悬挂/摩擦需要大量调参，且在 0.25m 台阶上行为不可预测；本项目要求「车不上台阶」，用球体占位 + 速度控制反而更稳、更可测。

**为什么车板质量设 40kg**：车被 70kg 冰箱砸上去时不能位移，否则货物与车互相推挤会抖到穿模。

### 4.3 货物承载（这是 4A 最大的物理风险）

**硬约束**：货物必须是独立刚体，真实放在车板上（V3.0：「不是进入独立物品栏」）。**不做** `SetParent` 绑定。

做法：
1. `CartCargo` 每 `FixedUpdate` 扫一遍车板上方 `Bounds`（1.3 × 0.9 × 0.75，中心 y=deckTop+0.45）内的 `GrabbableProp`，维护 `carried` 列表与 `totalMassKg`。
2. **竖直软约束**（防抖的唯一手段）：对板上每件货，若「水平位置在板内」且「底部低于 deckTop + 0.02」，施加一次向上的速度修正 `v.y = max(v.y, k * penetration)`，k=8。**水平方向完全不管** —— 所以货能滑、能滚、能掉下去。
3. 未绑带滑移：滑移触发阈值随载重缩放，`turnRateLimit = lerp(180, 90, loadRatio)`（°/s），实际转弯角速度超过阈值或受到冲量 > 60 N·s 时，把板上货物的水平摩擦临时降到 0.25 持续 0.5 s（表现「滑了一下」），随后恢复。**不能写死 140°/s**：满载时 `turnRateDeg` 上限只有 120°/s，固定 140 会让满载永远不滑，打包布带就没有可断言的对照。打包布带把该阈值再提高 30%，断言必须是“同一满载急转路径：无布带 ≥1 次掉货、有布带 0 次”。
4. 掉货检测：货物水平位置超出板内范围 → 从 `carried` 移除并 `FrictionEvents.Report("掉货", …)`。

**明确不做真实侧翻**：侧翻用「重心偏移 → 转向速率下降 + 超载时货物甩落倾向上升」表达。真正刚体侧翻留 P1 并在技术债清单登记。

### 4.4 状态机

```
Unfolded(载货中) --E--> 拒绝 + 提示「需先卸货」（V3.0 明令：折叠前必须先卸货）
Unfolded(空)     --长按 E 0.6s--> Folding(0.8s) --> Folded
Folded(被搬运)   --E--> 落地检测 → Unfolding(0.8s) --> Unfolded
Folded(未搬运)   --E--> 拾起（进入 Folded 被搬运）
```

- **Folded 状态 = 占用双手**：`GrabController.SetHandsBusy(true)`，此时不能抓任何物件，HUD 提示「双手被推车占用」。
- **Folded 搬运不经过 `GrabController` 的准星跟随**（那套会被 `breakDistance` 判脱手，爬楼梯必掉）。改用 `ServerFoldedFollowStep()`：位置 = 推车人前上方固定偏移 + 碰撞回避（被墙挡住就拉近），**没有脱手判定**。
- **台阶判定**：向推行方向 `SphereCast` 0.4m，命中点高差 > 0.15m → 拒绝推行 + 提示「推车过不了台阶，按 E 折叠后搬过去」。
- 窄门：车展开宽 1.3m > 门洞 1.2m → 同样被挡 + 提示折叠。

### 4.5 单人操作可行性

单人必须独立完成「装货 → 推行 → 停到公秤 → 提交」。逐条动作：

| 动作 | 单人怎么做 | 用到的键 |
|---|---|---|
| 装货 | 抱着货走到车旁，面向车板按 E「放到车上」 | E（与提交分离） |
| 取货 | 面向车板上的货按 E「拿起来」 | E |
| 推行 | 面向把手按 E「接管」→ 松开 E 也保持接管（不是按住）；再按 E「松手」 | E 切换 |
| 转向 | 推车中 WASD 绕车转向（A/D 直接改车 yaw，不依赖鼠标） | WASD |
| 刹车 | 停输入即自然减速；按 Shift 急刹 | Shift |
| 折叠 | 卸完货 → 长按 E | E 长按 |
| 搬上楼梯 | 折叠后抱着走（占双手） | WASD |
| 公秤提交 | 车停进停车位 → 面向秤长按 E | E 长按 |

**推车人的速度被车限速**：服务端算 `pushSpeedCap = cartMaxSpeed(loadKg)`，`SyncVar` 下发，推车人客户端 `SetExternalSpeedCap(cap)`。这样人不会跑出车，也不会出现「人被车拽着抖」。

### 4.6 噪音（按实际速度，不按按键）

```csharp
// 服务端 FixedUpdate
float speed = cartBody.linearVelocity.magnitude;      // ★ 实际速度，不是输入
float perSec = cartProfile.noisePerSecond.Evaluate(speed);   // 0→2 | 1.5→4 | 3.0→7（满速推行不越过追猎线）
NoiseBus.Emit(cartId, transform.position, perSec * Time.fixedDeltaTime, "cart");

// 碰撞额外脉冲
OnCollisionEnter(c) → NoiseBus.Emit(…, clamp(relativeVelocity * 1.5, 0, 25), "cart_impact")
```

- 下坡滑行、被怪物/队友撞走、车自己滚 → 全部按实际速度自然产生噪音，**不需要任何「有没有按 Shift」的判断**（V3.0 17.6 明令）。
- `NoiseBus` 维护全队 0–100 累积值，自然衰减 3/s，`SyncVar` 下发 `noiseLevel` + `noiseSource`（最近 2 秒内贡献最大的来源）。
- **4A 里没有消费者**：4A 不含怪物，没有 AI 读这个值。它只做三件事：HUD 显示、`PlaytestRecorder` 落 CSV、为 4B 的噪音导演提供接口。**明确标注为「采集与归因 v0，无 AI 消费方」**，不得在验收文档里写成「噪音系统已实现」——P0 的 A9 要的正是"怪物调查行为与噪音一致"，那条 4A 覆盖不了。

### 4.7 数值（首版参考，全部进 `CartProfile` 资产）

| 参数 | 值 | 备注 |
|---|---|---|
| emptyMass | 15 kg | 计入载重分母之外 |
| maxLoad | 120 kg | 超过即进入超载段 |
| **maxCargoCount** | **6**（件数上限） | 与 P0 §2.1「同时载货上限 6」对齐；件数上限同时是堆叠穿透概率的分母 |
| **overloadTolerance** | **+0.5 kg** | 判定用 `loadKg > maxLoad + 0.5`。120kg 正好卡线时浮点会算出 120.0001 判成超载 → A4 假失败，0.5kg 容差消除这个假阳性 |
| overloadHard | 180 kg | 超过提示「随时可能翻」 |
| deckSize | 1.3 × 0.75 m | |
| foldedWidth | 0.30 m | 折叠后碰撞体尺寸 |
| baseSpeed | 3.0 m/s | 空车推行 |
| loadSpeedCurve | 0kg→3.0 / 60→2.4 / 120→1.9 / 180→1.4 | |
| turnRateDeg | 0kg→220 / 60→170 / 120→120 / 180→80 | |
| noisePerSecond | 0→2 / 0.5→3 / 1.5→**4** / 3.0→**7** | AnimationCurve。**满速推行不能单独把噪音推过 70（追猎线）** —— 18/s 会让"全速推 5 秒必追猎"，把 4B 的追猎窗口从"玩家选择"变成"必然"。18–50 的档位留给超载、货物甩落、撞击脉冲 |
| foldSeconds | 0.8 s | 展开同 |
| stepBlockHeight | 0.15 m | 超过即拒绝推行 |

### 4.8 卡死判定与自动脱困（`P0_VerticalSlice.md` §2.7，4A 必须实现）

V3.0 17.15 明令「不出现卡住推车后无法恢复的死角」，所以脱困不是给手感加戏，是章程要求。

```
检测窗口：0.75 s；窗口内 push == true 且水平位移 < 0.08m 记为一次「停滞」
连续 2 次停滞（≈1.5 s）→ 判卡死，置 canUnstick = true，HUD 提示「长按 E 抬起推车」
玩家长按 E（1.0 s）→ 重置到「玩家身后 3m 内」的最近合法地面点
```

| 规则 | 值 | 为什么 |
|---|---|---|
| 检测窗口 | 0.75 s × 连续 2 次 | 原先写「2.0 s 窗口判 <1.5 秒」自相矛盾（design v1.1 纠错），已改 |
| 判卡死 | 1.5 s | A4 硬指标；无脱困的旧分支已删除 |
| 脱困操作 | **长按 E 1.0 s** | 与「提交长按 1.2 s」是两个不同的时长，别混 |
| 位移阈值 | 0.08 m / 窗口 | 比"完全不动"宽松，避免斜坡蠕动误判 |
| 重置目标 | **玩家身后 3m 内的最近合法地面点** | 后退语义 —— 天然不可能被当成"向前快送"或绕过窄门的捷径 |
| 货物 | **随车走，不掉落，不扣价值** | 否则脱困变成变相毁货，玩家宁可卡着 |
| 冷却 | 6 s | 防止连续瞬移当传送用 |
| 噪音 | +20（一次性脉冲） | 脱困本身必须付代价 |
| 不生成合法点的区域 | 窄门洞内、楼梯中间、夹层边缘 | 防止重置进墙 / 掉下夹层 |
| 抬不起来时 | HUD 明示「此处无法抬起，请先卸货或换位置」，**不静默失败** | 假失败比卡死更糟 |

日志：`GATE_CART_STALL duration=<ms> mass=<kg> count=<n> where=<x,z> rescued=(self|unstick|fail)`；仅在实际发生卡死时打一条（含自行脱出、脱困救回、救不回），`self` / `unstick` / `fail` 正好三态，**不再额外塞一个 `none` 把“没卡”混进来**。A4 的「单次卡死 <1.5 秒」直接 grep 这个。

---

## 五、安全屋与商店 v0

### 5.1 流程

```
Night1（公秤达标）→ LevelPortal(unlocked) → [场景切换] → Safehouse
Safehouse 内：ShopService 可买 / 可撤销 → SafehouseSession 二选一
  ├─ 「继续深入」→ legIndex+1 → [场景切换] → Night1（重新刷货、进度归零、余额保留）
  └─ 「收工结算」→ 写 runEnded → 显示本趟结算（IMGUI）→ 回 Safehouse 待机（不再进夜路）
```

- 单人：自己决定。多人：P0 简化为「房主点即为全队确认」，并在 HUD 明写 `全队确认：P0 简化为房主代决（技术债）`。**不许把 P0 简化伪装成全队确认已实现。**
- `LevelPortal` 未达标时锁死，HUD 显示「还需提交 <缺口> 价值」。

### 5.2 关卡与配额

**过关额从数据表读，不写公式到代码里**（`P0_VerticalSlice.md` §3.3 是 1800 / 2400 / 2800† 的固定表；我原计划的 `round(mapTotalValue × 0.55)` 会算出 1735，与规格的 1800 不是一回事 —— QA C4 指出必须先定口径，这里定为「表驱动」）：

```
legQuota = LevelQuotaTable[legIndex]                    // ★ 唯一口径；4A 临时表 = [1]2632 / [2]2632
          缺省（表里没有该关）= round(mapTotalValue × 0.55)  // 仅回退，且必须打 S4A_QUOTA_FALLBACK 告警
mapTotalValue = Σ 场景内所有 GrabbableProp.CurrentValue  // 生成后 1.0 s 统计一次（口径与提交侧一致）
legProgress 归零、货物重生成、玩家传送回入口
日志：GATE_LEVEL n=<legIndex> quota=<legQuota> source=(table|fallback) before=<balance> after=<balance>
日志：GATE_QUOTA_GATE leg=<n> progress=<p> quota=<q> portal=(locked|unlocked) carry_over=<balance>
```

- 用 `CurrentValue` 而不是 `Profile.value` 统计可搜价值：碎掉的货本来就换不到钱，把它算进"本图可搜价值"会让过关额虚高。
- **4A 的关卡差异 = legIndex 与文案**，**配额两关统一 2632**。同一张图、同一批 16 个物件、同一套诅咒（无），这是 4A 临时口径；V3.0 的「随机货物 / 随机封锁 / 难度递增」属 4B，写进技术债。
- 随机化预留：`LevelRouteService` 里 `seed = Hash(teamId, legIndex)` 已经存在但 4A 不消费，保证 4B 接上时不用改流程代码。

### 5.3 商品表（4 件，**每件都必须可自动断言**）

**与设计规格的对齐结论**（原计划自拟清单（6 项）已作废）：商品清单以 `P0_VerticalSlice.md` §2.5 的 8 件为准，4A 只提前上架其中**在 4A 现有能力范围内能被自动断言的 4 件**，其余 4 件按各自能力缺口排到 4B。这样 4A 的商品到 P0 全部保留，**零返工**。

不卖「买了看不出差别」的东西，是这一节的唯一硬约束。

| id | 名称 | 类 | 价 | 效果 | 自动化断言 | 来源 |
|---|---|---|---|---|---|---|
| `grip_gloves` | 防滑手套 | 耐用品 | 400 | 抓握加速度 +10%、重物破抓阈值 +10% | 抱 42kg 雕像冲刺同一条路线 3s 不脱手（无手套必脱手） | P0 §2.5 |
| `packing_tape` | 打包布带 | 耐用品 | 500 | 车上货物滑动阈值 +30% | 同一条急转路径掉货次数 0（无布带 ≥1） | P0 §2.5 |
| `flashlight` | 强光手电 | 耐用品 | 350 | 照明半径 18m，F 开关；**不改变怪物发现距离** | `spotLight.activeInHierarchy` 随 F 切换 | P0 §2.5 |
| `grip_training` | 握力训练 | 永久 | 900 | 负重**减速量**减轻 15%（`carryPenaltyScale = 0.85`） | 同一件 42kg 货的携带速度相对 D11 `-m1weight` 基线 +15%（±3%），且不超过 §7 R10 的携带速度上限 | P0 §2.5 |

**4B 再上的 4 件**（4A 无法断言，理由写在这里防止以后被当成遗漏）：`自救针 120`（无倒地）、`急救包 200`（无受伤）、`工具带 700`（需工具槽系统，工程无，约 +1 人日）、`体能训练 1200`（需耐力系统，`PlayerMotor` 无此字段）。**这是阶段裁剪，不是永久放弃**（team-lead 初审第 3 条要求写明）。

> **免费自救针 1 支/关是系统基线，不是商品**（V3.0 17.5：单人基础自救不能是"必须付费才有的唯一生路"）。商店里的 `自救针 120` 是**额外加购那 1 支**，不是把基线做成付费。4A 无怪物，两者都不实现；4B 必须保证 **A11 的「单人零购买也能完成 3 关」**成立 —— 那就是对这条基线的验收。

> **单人自救基线（4B 必须实现，不在 4A）**：每关免费发放 **1 支自救针**，不可购买、不可被商店替代；商店的 120 自救针只是**额外加购 1 支**。A11 必须验“单人零购买也能完成 3 关”。不得把“单人基础自救”做成必须付费才有的唯一生路（V3.0 17.5）。

**我原拟的 3 件为什么被移出**（design v1.1 给的理由，记在这里避免以后重新讨论）：
- **搬运肩带 / 背负架**：需要**新的搬运姿态系统**（肩扛、背负）。P0 只有「双手抱 + 推车」，做进来等于给 P0 加一套搬运模式 —— 那是 P1 的活。
- **放大镜**：依赖「未鉴定估价」概念。P0 的价值直接可见，放大镜没有作用对象 —— 上它就是假购买。
- 与肩带同轴的「负重分母」只保留一个价，语义按 design 的定义：`grip_training` 作用于**减速量**（`carryPenaltyScale = 0.85`），不是改分母。

**负重轴只有一根**：`grip_training` 的「减速量减轻 15%」是这条轴上的唯一商品，不设第二个同类项。D8「同轴不叠 buff」的纪律照旧适用于身份×缺点牌链。

### 5.4 购买事务

```
ServerPurchase(itemId):
  reject if !SaveService.CanSpend              // 只读模式
  reject if item.isDurable && AlreadyOwned(itemId)
  reject if balance < item.price
  TryCommit(Purchase{ purchaseId=GUID, itemId, price })   // 落盘成功才继续
  → balance -= price；lifetimeSpent += price；purchases.Add(record)
  → EquippedEffects.Refresh()  （重算乘子）
  → RpcPurchaseOk(purchaseId)
```

- **购买即保存，不等退出**（V3.0 明令）。
- **撤销**：`TryCommit(Revoke{ purchaseId })`，条件 = `state==Active` 且 `SafehouseSession.phase==Browsing`（未离开安全屋）。落盘成功后 `balance += price`、`state=Revoked`、`EquippedEffects.Refresh()`。离开安全屋 → `phase` 变化 → 撤销入口消失。
- **一项一事务，不做批量事务**：批量事务在 4A 只会增加回滚复杂度，收益为零。

### 5.5 效果注入点（`EquippedEffects`）

唯一的映射层，暴露只读查询：

```csharp
float CarryPenaltyScale { get; }    // 1.0 / 0.85  —— 施加在「负重减速量」上，不是分母
float GrabAccelScale { get; }       // 1.0 / 1.10
float GrabBreakScale { get; }       // 1.0 / 1.10
bool  HasFlashlight { get; }
float CartSlipThresholdScale { get; }  // 1.0 / 1.30
```

- **本 Story 只提供查询能力，不接线**。`PlayerMotor.SetCarryMass` / `GrabController` / `CartCargo` 的接线在 **Story 4** 完成 —— 先把账本与存档做对，再接效果，避免「效果不对」和「事务不对」两类 bug 互相污染定位。
- 接线方式：`GrabController` 内部已有 `Loadout` 倍率链，`EquippedEffects` 作为**同一个乘法链的下一段**插入，不改既有语义。

**负重携带速度硬钳制（`S4A_CARRY_CAP`，必须照这个实现）**：

```csharp
// 接在 PlayerMotor 算完 speed 之后，最终裁决
if (carryMassKg >= 15f)
    speed = Mathf.Min(speed, 3.135f);   // 3.3（怪物追逐） × 0.95
// carryMassKg < 15 不参与该断言
```

- **为什么是 ≥15kg 分段**：按 D11 实测曲线（0→3.68 / 2→3.61 / 9→3.35 / 22→2.87 / 42→2.13），速度恰好在 **15kg 处穿过 3.13**。轻件跑得过怪物是**有意的**（"轻装逃生"的奖励），只有重型件必须跑不掉。我 v1.1 的"任一负重 ≤3.13"判据会**误杀全部轻件**（2kg 是 3.61、9kg 是 3.35），design v1.1 已纠正。
- **为什么必须硬钳制而不是"公式算过不会超"**：15kg 处公式值恰好 = 3.13，一叠加 `grip_training`（减速量 ×0.85）就升到 3.21 直接越线。钳制让这条不变式对**未来所有商品**免疫，不必每加一件商品重算一遍。
- **断言输出（`-s4acarrycap`）**：

  ```
  S4A_CARRY_CAP_OK   mass=42 speed=2.4 limit=3.135
  S4A_CARRY_CAP_SKIP mass=2  speed=3.61 reason=below15kg
  ```

  重型件走 `OK` 行（`speed ≤ limit`），轻件走 `SKIP` 行（**只记录不判定**，`reason=below15kg`）。轻件跑得过怪物是有意的 —— 它不是漏测，是设计给定的逃生奖励。

---

## 六、Story 切分（5 个，按依赖排序）

> 排序原则：把**最可能推翻架构的风险**放最早。所以 Story 1 就把「跨场景常驻 + 服务端账本 + 运行时 Spawn 的 GameRoot」立起来，Story 4 才有得切场景。

---

### Story S4A-1 · 公秤提交 + 服务端公共账本（内存态）+ GameRoot 跨场景存活

**范围**：
- `S4AGameRoot` prefab + `S4ABootstrap`（运行时 Instantiate → `DontDestroyOnLoad` → `NetworkServer.Spawn`）
- `TeamAccount`（balance / legProgress / legQuota / **内存态**）
- `CargoIdentity`（`cargoId = $"{runId}-{legIndex}-{netId}"`）
- `QuotaTerminal`（手持货提交，长按 1.2s 确认，幂等去重）
- `WalletHUD` + `QuotaHUD`（IMGUI）
- `S4ASceneSetup`（生成 Night1，含锚点/公秤/停车位/撤离门；删 EvacZone/EvacSettlement 实物；**删 TraitBootstrap**）+ Validate
- Night1 进 Build Settings；`BuildWindows -map=sprint4a`

**完成定义**（可验收，缺一不算）：
1. Night1 里抱任意货走到公秤长按 1.2s → 钱与进度同时增加，数值等于 `CurrentValue`，货物销毁。
2. **HK-01 逐件提交事务日志**：每件一行
   `S4A_DEPOSIT cargoId=<id> value=<v> state=(Pending|Committed|Rejected) reason=<r> balance=<b> legProgress=<p> totalAfter=<t>`
   —— 单件有 Pending→Committed 两行，被拒只有 Rejected 一行。QA 的「总额 = 逐件之和，误差 0」靠这批行做逐件对账，只印 `total=<Σ>` 不够。
3. **HK-02 去重必须可断言（这是 A6 的关键，不能靠销毁规避）**：同一 `cargoId` 第二次提交 → 打印
   `S4A_DEPOSIT_DUP cargoId=<id> balanceBefore=<b0> balanceAfter=<b1>` 且 `b1 == b0`。
   **去重入口必须能按 `cargoId` 触发，不能只能靠"物件还在手上"** —— 提交成功即 `NetworkServer.Destroy`，如果测试只能靠再抓一次物件，那 A6 就会假通过（QA 指出的正是这一点）。做法：加一条服务端测试专用槽 `-s4adeposit` 内部直接调 `TeamAccount.ServerDepositId(cargoId, value)`，同一 id 提两次。
4. 碎成 0 的易碎件提交被拒（`S4A_DEPOSIT_ZERO`），余额与进度均不变。
5. **`NetworkManager.ServerChangeScene("Night1")` 重载场景后，`TeamAccount` 余额与 legIndex 不变**。
6. `S4A_VALIDATE_OK`：`night1=1 cartAnchor=1 quotaTerminal=1 portal=1 evacRemoved=1 anchors=16 traits=none`
   —— **`traits=none` 是硬断言**：`FindAnyObjectByType<TraitBootstrap>() == null`。否则身份倍率会污染 4A 全部数值证据。
7. **A6**：`-s4adeposit` → `S4A_DEPOSIT_TALLY accepted=4 uniqueCargo=4 expectedSum=<Σ> actualSum=<Σ> dupBlocked=1 ok=True`。判据见附录 A。
8. **HK-06 / A6 金额校验**：`-s4adeposit` 结束时打印
   `GATE_WALLET expected=<e> actual=<a> delta=<d> path=<存档绝对路径>`，`expected` 由测试脚本传入（或用 `-expect=<n>`），**不许测试内部自己算一个期望值然后自己比**。

**关键决策**：`TeamAccount` 挂在**运行时 Spawn 的 GameRoot** 上，不是场景对象 —— 因为 Mirror 在 `ServerChangeScene` 时会 unspawn 所有 **scene** objects，场景对象的账本一定会在切场景时消失。这一条如果做错，Story 4 必然返工。

**风险**：
- `DontDestroyOnLoad` + 运行时 Spawn 的组合在客户端侧的行为（客户端收到 spawn 时需自己 `DontDestroyOnLoad`）—— 用完成定义 5 直接测出来。
- `netId` 在场景重载后可能重用 → `cargoId` 已加 `runId-legIndex-` 前缀，跨关不冲突。
- **身份倍率污染**（§2.6）：若不删 `TraitBootstrap`，所有数值断言都会"在自己被污染的世界里自洽地通过"—— 这种假通过最难查。

**测试钩子**：
```
Unity -batchmode -executeMethod ScavengeNightmare.EditorTools.S4ASceneSetup.Setup
Unity -batchmode -executeMethod ScavengeNightmare.EditorTools.S4ASceneSetup.Validate
Builds/Sprint4A/ScavengeNightmare.exe -s4adeposit -expect=<Σ>   → S4A_DEPOSIT_TALLY + GATE_WALLET
```

**估时**：2.0 人日

---

### Story S4A-2 · P0SaveService：原子写 / 备份 / 事务 / 强杀恢复

**范围**：
- `TeamSaveData` + `SaveTxn` + `P0SaveService`（`TryCommit` / `TryLoad` / `WriteAtomic` / 单实例锁）
- `TeamAccount` 改为**所有变更经 `TryCommit`**
- 启动流程：`S4ABootstrap` → `P0SaveService.LoadOrCreate()` → `TeamAccount` 从存档初始化
- HUD 加一条存档状态行（`SAVED` / `LOCKED` / `RECOVERED`）

**完成定义**：
1. 提交一笔 → 立刻能在磁盘上读到新余额（不依赖退出）。
2. **A8（3/4 场景：正常退出 / 强杀 / 重连；团灭待 4B）**
   - **强杀必须用外部 `taskkill /F /PID`（TerminateProcess）**，不能用进程内的 `Environment.Exit`。原因：`Environment.Exit` 会跑 `ProcessExit` 托管回调，若有人在那里"顺手刷盘"，测试会通过得很漂亮，而 V3.0 17.4 恰恰禁止「退出时统一保存」。
   - 两种都跑并比对：`taskkill /F` 结果必须 = 最后一次成功事务；与 `Environment.Exit` 的结果**不一致即判不合格**（说明存在 ProcessExit 刷盘，回修）。
   - 重连场景能测的只有「client 断开 → 重连到**仍在运行的同一 Host** → SyncVar 重发 → 余额一致」。它证明的是「客户端无本地账本」，**不能证明「存档能恢复」**；验收报告里两条的证明力必须分开写。
3. 手动把 `sav` 改坏 → 启动自动回退到 `bak`，日志 `S4A_SAVE_RECOVERED from=bak`，余额等于上一笔。
4. `sav`/`bak`/`bak2` 全坏 → 生成 `corrupt-<ts>.sav` 并新建空档，日志 `S4A_SAVE_CORRUPT_RECOVERED`，HUD 亮红。
5. 同机双开 → 第二个进程 `S4A_SAVE_LOCKED`，购买与入账被拒（不是静默失败）。
6. 余额不足购买被拒，磁盘内容逐字节未变（对比 sha256）。
7. **HK-04 金额级校验行**（只报 `txn` 和 `dup` 只证明了次数，证明不了钱）：
   `S4A_SAVE_VERIFY_OK txn=<n> dup=<0> balance=<b> sumCommitted=<Σ>`
   判据：`balance == sumCommitted`（`sumCommitted` = 从存档 `purchases` 与事务日志反算，而不是从内存拿）。
8. **HK-05 写盘失败注入（证明原子性与回滚真实存在，不是靠"没出过错"）**：
   `-s4asave-failio` 让 `WriteAtomic` 在写 tmp 前抛一次 IO 异常 → 必须打印 `S4A_SAVE_FAIL reason=io_error`，
   且**内存余额不变、磁盘文件 sha256 不变、玩家收到失败提示**（不许静默）。
   另一种制造方式：`-savedir=<只读目录>`，两条都要能跑出 `S4A_SAVE_FAIL`。
9. **HK-06 余额校验导出**：`GATE_WALLET expected=<e> actual=<a> delta=<d> path=<绝对路径>`，`expected` 外部传入。

**风险**：
- `File.Replace` 在非 NTFS（例如玩家把存档放到 FAT 移动盘）会抛 `PlatformNotSupportedException` → 降级为「写 tmp → 删 sav → Move(tmp,sav)」，并在日志记 `SAVE_FALLBACK_NONATOMIC`（此时窗口极短但存在，必须诚实记录）。
- `Application.persistentDataPath` 在批处理环境可能被重定向 → 测试钩子支持 `-savedir=<path>` 覆盖。

**测试钩子**：
```
ScavengeNightmare.exe -s4asave       -savedir=D:\tmp\s4a_save1 → 3 笔 → S4A_SAVE_OK txn=3
:: 强杀（外部 TerminateProcess，不跑任何托管清理）
start "" ScavengeNightmare.exe -s4asave-slow -savedir=D:\tmp\s4a_save1
taskkill /F /PID <pid>                                          → 盘上应为 txn=4（第 2 笔已被承认则 txn=5）
ScavengeNightmare.exe -s4asave-verify -savedir=D:\tmp\s4a_save1 → S4A_SAVE_VERIFY_OK txn=<4或5> dup=0 balance=<b> sumCommitted=<Σ>
:: 对照：进程内 Environment.Exit，结果必须与上面一致，不一致即判不合格
ScavengeNightmare.exe -s4asave-kill  -savedir=D:\tmp\s4a_save2
ScavengeNightmare.exe -s4asave-verify -savedir=D:\tmp\s4a_save2 → 与 save1 的 txn 语义一致
ScavengeNightmare.exe -s4acorrupt   -savedir=D:\tmp\s4a_save1 → 造坏 sav 一次
ScavengeNightmare.exe -s4asave-verify -savedir=D:\tmp\s4a_save1 → S4A_SAVE_RECOVERED from=bak
:: 写盘失败注入（HK-05）
ScavengeNightmare.exe -s4asave-failio -savedir=D:\tmp\s4a_save3 → S4A_SAVE_FAIL reason=io_error（内存与磁盘均不变）
ScavengeNightmare.exe -s4asave        -savedir=<只读目录>        → S4A_SAVE_FAIL reason=io_error
:: 金额校验（HK-06）
ScavengeNightmare.exe -s4asave-verify -savedir=D:\tmp\s4a_save1 -expect=<n> → GATE_WALLET expected= actual= delta= path=
```
> `-s4asave-slow` = 每笔事务之间加固定间隔（默认 800ms），给外部 `taskkill` 留出可命中的窗口。**它只加间隔，不改任何写盘路径**，不许用它掩盖崩溃窗口。

**估时**：2.0 人日

---

### Story S4A-3 · 折叠推车 v0（4A 最大工作量与最大风险）

**范围**：`CartProfile` + `Cart.prefab` + `CartController` + `CartCargo` + `CartSpawnAnchor` + `CartHUD` + `NoiseBus` + `GrabController.SetHandsBusy` + `PlayerMotor.SetExternalSpeedCap`

**完成定义**：
1. 每队 1 辆，Night1 入口处生成（服务端 Spawn），双端可见、位置同步（误差 ≤ 0.05m 相同口径）。
2. 抱 2 件轻货 + 1 件 42kg 雕像放到车板 → `loadKg ≈ 46`，三件货**独立刚体真实躺在板上**（不是子物体），推行 8m 后仍在板上（无穿模、无飞出）。
3. 空车推行 3.0 m/s，满载降速到 ≤2.0 m/s；超载到 180kg 提示「随时可能翻」但**不禁止放货**；**同时载货上限 6（件数上限）**（P0 §2.1）。
4. **A4**：满载（保险箱 60 + 雕像 42 + 老电视 12 = 114kg，单层不堆叠）连续推行 ≥30m，**单次卡死 < 1.5 s**（design v1.1 §2.7 已定为硬指标）。
   - 「卡死」的自动化定义（按 design 修正）：**0.75 s 检测窗口内 `push=true` 且水平位移 < 0.08m 记一次停滞；连续 2 次停滞（≈1.5 s）判卡死**。我 v1.1 写的「2.0s 窗口判 <1.5 s」自相矛盾，已改。
   - 自动脱困必须实现（§4.8）：长按 E 抬起 → 重置到玩家**身后 3m 内**的最近合法地面点，货物随车走不掉不扣价，冷却 6s，噪音 +20。
   - 为什么用 114kg 而不是 120kg：12 件池里凑满 120kg 需要 6 个物件、占地 1.25㎡ > 板面 0.975㎡ → **必须堆两层**，那样测的是堆叠稳定性而不是路线。114kg 三件占地 0.76㎡ 单层放得下，测的才是 A4 想测的东西。
5. 有货时按 E 折叠被拒，提示「需先卸货」。
6. **A5**：卸空后折叠 → `handsBusy=true`（此时抓货被拒）→ 抱车走楼梯爬升 3.00m 不脱手 → 楼梯顶展开 → 重新装货，全流程 ≤90s（完整走一遍 V3.0 17.6 的楼梯流程）。
   - **HK-09 计时**：`GATE_TIMER tag=stair_cycle ms=<n>`（另需 `tag=unload_narrow` 供窄门卸货计时）。
7. 窄门（1.2m）：**空车展开宽约 0.75m，应直接通过**，不得强制折叠；载货后有效宽度因外伸超过 1.2m 时被挡，提示“卸货/调整货物”，卸至可过即可通过；折叠只用于楼梯、夹层或明确净宽不足的位置。禁止出现静默卡死。
8. 噪音：`speed` 0.5 / 1.5 / 3.0 三档对应噪音增量单调递增（±10%）；**推车人被队友撞动导致车滑动时也产生噪音**（不按按键判断这条要实测到）。
9. 单人：`-s4acart` 全自动完成「装 3 件 → 推行 8m → 停到停车位 → 公秤提交 → 余额增加」。
10. **A3 唯一判据源（HK-12）**：`GATE_HEAVY_MANUAL cargoId=<id> mass=<kg> via_cart=False`
    —— 同一件重型件在**全程未接触推车**的情况下搬出后门，且日志里该 `cargoId` 从未出现在任何 `GATE_CART` 的载货清单中。这才证明「推车不是强制撤离容器」。4A 只做一次，完整 A3 由 4B 的 3 关验。
11. **HK-08 推车清单与停滞**：`GATE_CART count=<n> mass=<kg> ids=<id1,id2,…>`（每次装载变化打一条）+ `GATE_CART_STALL duration=<ms> mass=<kg> count=<n> where=<x,z> rescued=(self|unstick|fail)`（仅在实际卡死结束时打一条）。
12. 回归：Main 上 D4 投掷 **4.14（light）**、D6 五条全过（证明 `SetHandsBusy` / `SetExternalSpeedCap` 的默认值没改变既有行为）。

> **给 design 的回话（A4 余量）**：A4 在 4A 是**无追猎压力下的空跑**。如果 4A 实测卡死均值接近 1.3s（贴着 1.5 s 线），我会在 Story 3 结束时立刻报出来，请你改主路线的卡点位置或放宽阈值，**不等到 4B 才发现余量不够**。

**风险（按严重度）**：
| 风险 | 缓解 |
|---|---|
| 货物在运动车板上抖动/穿透 | 竖直软约束 + 车板摩擦 0.9 + 车质量 40kg；单帧位移上限 0.15m |
| 车追不上人导致来回拉扯 | 服务端下发 `pushSpeedCap` 给推车人（本节是这条改动的唯一目的） |
| 折叠后抱车爬楼梯脱手 | 折叠搬运不走 `GrabController`，用独立的无脱手跟随 |
| 台阶判定误判（把平地判成台阶） | `SphereCast` 起点抬高 0.1m，只认「命中面法线朝上且高差 > 0.15m」 |
| 1 辆车 / 40 物的带宽 | 只有 1 辆车（推车走 NetworkTransform 约 30Hz）；D9 40 物档 451kbps 仍有余量 |

**测试钩子**：
```
ScavengeNightmare.exe -s4acart          → S4A_CART_OK count=3 mass=46 push=1.9m/s fold=1 stairs=3.00m noise=ok
                                          + GATE_CART count= mass= ids=
                                          + GATE_CART_STALL duration= mass= count= where= rescued=(self|unstick|fail)（A4 判据）
                                          + GATE_TIMER tag=stair_cycle ms=<n>
                                          + GATE_HEAVY_MANUAL cargoId= mass= via_cart=False（A3 判据）
                                       窄门三条子断言（对应 Story 3 CD7）：
                                          + S4A_CART_NARROWDOOR mode=empty  passed=True   （空车展开 0.75m 过 1.2m，未折叠）
                                          + S4A_CART_NARROWDOOR mode=loaded blocked=True hint=unload
                                          + S4A_CART_NARROWDOOR mode=unloaded passed=True
ScavengeNightmare.exe -s4acart-noise    → S4A_CART_NOISE_OK s1=3.0 s2=4.0 s3=7.0 limit70=none
（另跑）Main 包 -d4test -feel=light / -d6test  → 回归
```
> 噪音三档按 §4.7 的曲线（0.5→3 / 1.5→4 / 3.0→7）。`limit70=none` 断言「满速推行单靠车速不会把 0–100 噪音推过 70 追猎线」。

**估时**：4.0 人日（含 1.5 s 卡死判定 + 自动脱困）

---

### Story S4A-4 · 安全屋 + 商店 v0 + 进入下一关（含场景切换）

**先做「空安全屋切换骨架」再填商店**，把场景切换的风险与商店的逻辑风险分开验证。

**范围**：
- `Safehouse.unity`（`S4ASceneSetup` 生成，12×10m、商店终端、返回门、出生点）
- `LevelRouteService`（legIndex / quota / progress / `ServerChangeScene` / 玩家重定位）
- `LevelPortal`（达标解锁 + 传送）
- `SafehouseSession`（Browsing / Continue / Settle 三态）
- `ShopCatalog` + `ShopItem` + `ShopService` + `EquippedEffects` + `ShopHUD`
- 效果接线：`PlayerMotor` 负重分母、`GrabController` 抓握参数、相机手电、`CartCargo` 摩擦

**完成定义**：
1. 进度未达标 → 撤离门锁死并显示缺口。
2. 达标 → 进安全屋：**Host 与客户端都到安全屋，玩家落在安全屋出生点（不掉出地图）**，`TeamAccount` 余额不变。
3. **HK-10 购买事务日志**：商店列 4 件（全部来自 `P0_VerticalSlice.md` §2.5），余额不足置灰不可点；买入后余额立即减少、**磁盘立即更新**。必须打印：
   ```
   GATE_BUY itemId=<id> price=<p> balanceAfter=<b> purchaseId=<gid>
   GATE_BUY_REJECT itemId=<id> reason=(insufficient|already_owned|window_closed|read_only|hard_cap)
   GATE_BUY_UNDO purchaseId=<gid> refund=<p> balanceAfter=<b>
   GATE_BUY_UNDO_REJECT purchaseId=<gid> state=<state> reason=(window_closed|already_revoked)
   ```
   **撤销连点是钱最容易被复制的地方**：同一次购买连撤两次，第 2 次必须打 `GATE_BUY_UNDO_REJECT`，且余额不变。没有拒绝码，验收只能靠余额差猜。
4. 买的商品效果逐条可断言：`grip_gloves` 抱 42kg 冲刺不脱手；`packing_tape` 同路径掉货 0 次；`flashlight` F 开关灯；`grip_training` 同一件 42kg 货携带速度相对 D11 基线 +15%（±3%）。**效果断言不涉及 `S4A_CARRY_CAP` 的钳制** —— 见 §5.5。

   > 携带速度钳制是设计不变式「抱 42kg 一定被追上」的技术护栏，硬钳制在 `PlayerMotor` 里：`if (carryMassKg >= 15) speed = min(speed, 3.135)`。轻件（<15kg）**豁免**，因为轻装跑得过怪物是设计给的奖励。
5. 撤销：会话内撤销 → 余额还原、效果消失、磁盘记录 `state=Revoked`；离开安全屋后再撤销被拒（`reason=window_closed`）。
6. 「继续深入」→ 回 Night1、`legIndex=2`、进度归零、货物重新刷出 16 个物件、余额与已购商品保留。
   - **HK-11**：`GATE_LEVEL n=<legIndex> quota=<q> source=(table|fallback) before=<balance> after=<balance>`，`quota` 从配额表读，不硬编码。
7. `-s4aloop`：单人在无人工干预下连跑 2 关，日志给出 `legIndex=2`、`balance=…` 单调不减、`dupBlocked>=1`。
8. **HK-07 双端余额一致性**：host 与 client 各打
   `GATE_WALLET_SYNC server=<s> client=<c> match=<bool>`
   判据 `match==true`。
   - **同时要有「抑制同步」注入来证明这个探针本身有效**：`-s4asyncbreak` 让服务端故意不广播一次余额变更 → client 侧 `match` 必须变 `false`。如果注入了还不报警，说明探针是假的（这条是 QA 门里 X6「测试本身无效即 FAIL」的要求，不能省）。
9. 双人：host + client 各一，双人都能看到同一余额、同一进度、同一购买列表；client 购买也生效（走 Cmd）。

**风险**：
| 风险 | 缓解 |
|---|---|
| `ServerChangeScene` 后玩家对象留旧坐标 → 掉出地图 | `OnServerSceneChanged` 里把玩家传到新场景出生点（**传送必须连手持物与推车一起挪**——D13 教训） |
| 场景重载后静态 `Instance` 指向已销毁对象 | 所有 `Instance` 用 `if (Instance == this) Instance = null`（照抄 `EvacSettlement`） |
| `PropSpawner` 二次进夜路时重复刷/不刷 | `PropSpawner` 的 `spawned` 是实例字段，场景重载=新实例，天然重置；但 `S4ABootstrap` 必须在场景加载后再触发一次 |
| 商店状态与账本不一致（HUD 显示已买但盘上没有） | 一律「先落盘再广播」；HUD 只读 `SyncVar`，不自维护状态 |
| 双人下 client 购买被拒（权限搞错） | 购买走 `Cmd`，服务端执行；client 只发 `itemId`，永不发金额 |
| **同机双开被 `SAVE_LOCKED` 挡住，导致"双人"验收变成单进程** | 双人验收必须 **host 单进程持锁写盘 + client 只读不写**；客户端的"钱不丢不复制"证据 = 客户端余额与服务端一致 + 重连后一致（QA 提醒） |

**测试钩子**：
```
ScavengeNightmare.exe -s4ashop                 → S4A_SHOP_OK balance=… owned=2 revoked=1 + GATE_BUY / GATE_BUY_REJECT / GATE_BUY_UNDO / GATE_BUY_UNDO_REJECT
ScavengeNightmare.exe -s4acarrycap             → S4A_CARRY_CAP_OK max=2.4 limit=3.135 exempt_below=15kg
ScavengeNightmare.exe -s4aloop                 → S4A_LOOP_OK legs=2 balance=… dupBlocked=1 + GATE_LEVEL / GATE_WALLET_SYNC
ScavengeNightmare.exe -s4aloopclient -address=127.0.0.1   （与 host 并行）→ GATE_WALLET_SYNC match=true
ScavengeNightmare.exe -s4aloop -s4asyncbreak          → GATE_WALLET_SYNC match=false（证明探针有效）
```

**估时**：3.0 人日

---

### Story S4A-5 · 回归收口 + 单人/双人验收脚本

**范围**：`run_regression_4a.bat` 一键串联；`Docs/Sprint_4A_DevLog.md` 记录实际偏差；`Docs/Networking.md` 追加 4A 权限矩阵与踩坑。

**完成定义**：
1. `run_regression_4a.bat` 一条命令跑完并输出 **`S4A_REGRESSION_OK passes=<n> expected=<m>`**。
   - `expected` **从用例清单文件读，不硬编码**；`passes != expected` 一律判 FAIL。
   - 我 v1.1 只打 `passes=<n>`，漏跑一段仍然会打印 OK —— QA 指出这会放过空跑，已改。
   - 覆盖：D3(4 客户端) / D4 / D6 / D8 / D9 / D11 / D12 / D13 / S4A-1..4。
2. **全部 4A 用例与全部回归用例都必须带 `-feel=light`**，否则数字键 1/2/3 热切会污染数据。
   - D4 投掷现行基线在 light 下是 **4.14**（= 70 × 1.30 ÷ 22），断言按 4.14。
   - **D5 之前的默认方案值不是现行基线**（我 v1.1 的回归矩阵误用了它，QA C3 已纠，v1.2 起改回 4.14）。**全文不再以任何非 light 方案值作为 D4 基线。**
3. **`S4A_NEUTRAL_OK traits=none feel=light`**：回归脚本开头断言 `TraitBootstrap` 不存在 且 `FeelSettings.Active == light`，两条任一不满足直接退出，不往下跑（避免用污染数据跑完整套回归还报 OK）。
4. `-s4aperf`（**HK-13，复用 `NetworkDiagnostics`**）：1 推车 + 40 个活跃物理物件 ≥60fps@1080p、单客户端 ≤600kbps，输出
   `S4A_PERF_OK fps=<n> kbps=<n> props=40 carts=1`（**A12 的部分覆盖**，怪物不在 4A，写在报告里不许含糊）。
5. 单人 `-s4aloop` 连跑 2 关通过；双人（host + 1 client）自动跑 1 关，双侧 `GATE_WALLET_SYNC match=true`。
6. `Sprint_4A_DevLog.md` 列出：实际用时 vs 估时、降级项、发现的新坑、技术债增补、**A1–A12 覆盖表的实测填写**（哪条真跑了、哪条降级成人工录屏）。
7. **A8 措辞锁定**：报告里只能写「A8 部分覆盖 3/4 场景（正常退出 / 强杀 / 重连），团灭待 4B」，并附上「重连只证明客户端无本地账本、不替代强杀」这句说明。

**估时**：1.5 人日

**合计：约 12.5 人日**（单人开发约 2.5 周）。构成：S4A-1 **2.0** + S4A-2 **2.0** + S4A-3 **4.0**（含已拍板的 1.5 s 卡死判定 + 自动脱困 +0.5）+ S4A-4 **3.0** + S4A-5 **1.5** = **12.5**。已删除旧的「不脱困则 12 人日」分支。

---

## 七、风险

| # | 风险 | 影响 | 缓解 | 降级预案（触发即执行，不硬扛） |
|---|---|---|---|---|
| R1 | **货物在运动车板上抖动/穿透** | 推车不可用，V3.0 支柱 3 崩塌 | 竖直软约束 + 车 40kg + 摩擦 0.9 + 单帧位移限幅 | 若 2 天内调不稳 → 车上货物改用「板内软吸附」（水平也约束）并明确登记「P0 载货非纯物理，P1 改回」，**不许说成纯物理** |
| R2 | **Mirror 场景切换（玩家重定位/静态清理/二次刷货）** | Story 4 全面阻塞 | 先做空安全屋骨架验证；传送必须带货带车 | 若 2 天内不稳 → 降级为「单场景双区域+传送」，`LevelRouteService` 接口不变（玩法代码不返工），场景分离留 4B |
| R3 | **存档写盘失败路径没被测到** | 「不丢不复制」是 V3.0 验收硬项，假通过= 债务 | 强杀钩子 + 损坏注入 + sha256 对比 | 无可降级。测不出来就不许声称达标 |
| R4 | **`maxConnections` 与人数口径搞错**（Host 本地连接计数） | 双人测试假失败/假通过 | 权限矩阵 §2.5 第 1 条写死；UI 一律用 `connections.Count` 且说明含 Host | — |
| R5 | **推车与客户端权威移动的耦合抖动** | 手感崩，玩家判定「推车是坏的」 | 服务端下发 `pushSpeedCap` 给推车人 | 若仍抖 → 推车时临时把玩家移动切到服务端跟随（局部方案，仅推车期间），并登记技术债 |
| R6 | **既有脚本改动引入 Main 回归漂移** | D4/D6/D8 基线废掉 | 4 处改动全是「默认关闭开关」；Story 3 完成定义第 9 条强制回归 | 任一回归漂移 → 回滚该改动，改用 GameRoot 侧注入 |
| R7 | **商品「买了没效果」= 假购买** | 直接违反「不造假」纪律 | 4 件商品每件都有自动化断言（§5.3 第 6 列） | 断言不出来 → 该商品不上架，宁可少于 4 件 |
| R8 | **`D12SceneSetup` 重跑抹掉 Night1 手改** | 场景神秘丢失，浪费半天 | Night1 全部增量由 `S4ASceneSetup` 代码生成，手改一律禁止 | — |
| R9 | **设计扩张速度 > 验证速度**（V3.0 自述最大风险） | 4A 做不完 | 本计划的「不做清单」就是护栏 | 任何新增需求先入「未来清单」，不绕过 4A 验收 |
| R10 | **商品把重型件携带速度抬过怪物追逐速度 3.3 m/s**，等于卖了一件反追猎道具，悄悄推翻「抱 42kg 一定被追上」这条设计不变式 | 追猎循环整体失效，且发现时已在真人测试之后 | `PlayerMotor` 里**硬钳制**：`if (carryMassKg >= 15) speed = min(speed, 3.135)`（3.3 × 0.95）；`S4A_CARRY_CAP` 断言 `mass>=15 时 speed<=3.135`，`mass<15 豁免`（轻装逃生是设计奖励） | 无可降级。这条不变式只能守，不能谈 |
| R11 | **商品清单与设计规格不一致**（4A 自拟一套、P0 另一套）→ 4A 做完商品全换 | 白做 1–2 人日 + 假购买 | 已对齐：4A 只上 `P0_VerticalSlice.md` §2.5 里可断言的那 4 件 | 若设计后续改价/改效果，先改规格再改表，不许代码里偷改 |
| R12 | **身份/缺点牌倍率污染 4A 全部数值**（Night1 继承 `TraitBootstrap`），所有断言在"被污染的世界里自洽通过" | 假数据，且是最难查的一类假通过 | `S4ASceneSetup` 删 `TraitBootstrap` + Validate 断言 `traits=none` + 测试启动断言 `ForceNoTraits`（§2.6） | 无可降级。删不干净就不许开测 |

---

## 八、回归与验收

> **验收编号统一引用 `P0_VerticalSlice.md` §6.1 的 A1–A12，不另起一套。** 下面是 4A 对 P0 验收项的覆盖关系，这张表也是 4A 交付报告的第一页。

| P0 验收项 | 4A 覆盖度 | 落点 |
|---|---|---|
| A1 单人 3 关 | ❌ 不覆盖（需怪物与 3 关梯度） | 4B |
| A2 双人 3 关 | ❌ 不覆盖（同上） | 4B |
| A3 徒手路径独立成立 | ⚠️ 部分（4A 只做 1 次自动对照，完整版要 3 关） | Story 3 完成定义 10 |
| A4 满载推车过主路线 | ✅ 自动化 | Story 3 完成定义 4 |
| A5 推车上下楼梯 ≤90s | ✅ 自动化计时 | Story 3 完成定义 6 |
| A6 公秤不重复入账 | ✅ 自动化 | Story 1 完成定义 + `S4A_DEPOSIT_TALLY` |
| A7 两种诅咒各触发 ≥1 次 | ❌ 不覆盖（4A 无诅咒） | 4B |
| A8 资金不丢不复制 | ⚠️ **3/4 场景**：正常退出 / 强杀 / 重连 ✅；**团灭 ❌**（无怪物、无倒地） | Story 2 |
| A9 噪音可归因 | ❌ 不覆盖（4A 无怪物，噪音无消费者） | 4B |
| A10 单关时长 8–12 分钟 | ❌ 不覆盖（4A 不设过关额梯度、无怪物节奏） | 4B |
| A11 真人体验 | ❌ 不覆盖 | P0 |
| A12 性能与带宽 | ⚠️ 部分（1 推车 + 40 物可提前压，**怪物不在**） | Story 5 `-s4aperf` |

**结论必须写进交付报告**：`4A 覆盖 A4/A5/A6 全量、A3/A8/A12 部分，A1/A2/A7/A9/A10/A11 未覆盖`。不许把这张表省掉，也不许把"部分覆盖"写成"通过"。

### 8.1 回归矩阵（每 Story 结束必跑，Story 5 全跑）

| 用例 | 场景 | 期望 | 4A 为什么可能碰到它 |
|---|---|---|---|
| D3 四人 | Main | `conns=4 players=4`、速率 3.2 m/s | `PlayerMotor` 加了 `SetExternalSpeedCap` |
| D4 投掷 | Main | **`speed=4.14`（light = 70×1.30÷22）**、`closestSide=-0.40` | `GrabController` 加了 `SetHandsBusy` |
| D6 五条 | Main | 滚动 3.88m / 硬直 1.0s / 花瓶碎价值 0 / 门夹脱手 / 窄门卡位 | 同上 + 物理材质未动 |
| D8 八项离线 + 15 项联机 | Main | 全过 | 倍率链插入了 `EquippedEffects` 段 |
| D9 七项 + 断线 | Main | 131ms / 3988ms、20 物 230kbps / 40 物 451kbps | 新增 GameRoot 与 Cart 会增加 spawn 流量 |
| D11 重量感/延迟 | Main | 4.30x / 42% / 250ms、空手 3.68 | 负重减速被 `grip_training` 改写，必须确认「未购买时」完全不变 |
| D12 白盒 | Supermarket | `D12_VALIDATE_OK … stairTop=3.0m` + `-d12smoke` | Supermarket 未动，应零影响 |
| D13 结算 | Supermarket | `D13_ACCEPT_OK valueOk=True(1870==1870)` | `EvacSettlement` 未动，应零影响 |
| **S4A-1..4** | Night1 / Safehouse | 见各 Story 完成定义 | — |

> **D4 基线口径（QA C3 已纠，本版为最终值）**：D4 现行基线只有一个 —— **light 方案下的 4.14**。D5 之前的默认方案值不是现行基线，全文不再引用它。回归脚本必须显式带 `-feel=light` 并按 4.14 断言，否则会把"通过"报成 FAIL。

**回归失败的处理纪律**：任一既有用例漂移 → **先回滚 4A 改动**，再定位，不许「顺手把基线改一下让它过」。

### 8.1b QA 要的三处口径表态

| # | 问题 | 4A 的表态 |
|---|---|---|
| 1 | `-d13accept` 在公秤上线后：保留还是替换？ | **保留为「裸结算回归」**。它跑在 `Supermarket.unity` 上，`EvacZone` + `EvacSettlement` 一行未改，继续作为 D13 口径的不漂移证据。4A 的 Night1 里这两个物体被移除，两套语义各自在自己的场景里成立。**未来统一到公秤时（P0/4C）再回头把 `-d13accept` 改成走公秤**，现在不动。 |
| 2 | `D12_VALIDATE_OK` 的新期望值 | **不变**。`D12SceneSetup` 与 `Supermarket.unity` 一行未动，期望值仍是 `spawns=4 doors=2 patrols=5 anchors=18 steps=12 stairTop=3.0m`。4A 的 16 锚点只存在于 `Night1.unity`，由 `S4A_VALIDATE_OK anchors=16` 单独断言。两份期望值**成对写进报告**，避免"16 与 18 打架"。 |
| 3 | 推车联网后 D6.5 同刻位置差、D9 带宽会不会漂？ | **不漂，但会给新阈值并标告警级**。D6.5 的 0.28m 均值 / 0.48m 最大是**物件**指标，推车走同一条 `NetworkTransformReliable` 30Hz，预估均值 ≤0.35m（车比货快，滞后按速度线性放大）；D9 的 230/451kbps 是**40 物档**，加 1 辆车约 +8kbps，落在噪声里。**新阈值：推车位置差均值 ≤0.40m / 最大 ≤0.60m；4A 带宽 ≤500kbps（40 物 + 1 车）**。超了先按告警记，不判失败——因为 4A 无追猎压力，等 4B 有怪物时再定死。 |

### 8.2 一键验收

```
run_regression_4a.bat
  ├ 前置断言：S4A_NEUTRAL_OK traits=none feel=light   （不满足直接退出，不跑后面的）
  ├ 编辑器段：S4ASceneSetup.Setup / Validate、D12SceneSetup.Validate、D13PropSetup.Validate、D9StabilitySetup.Validate
  ├ 构建段：BuildWindows -map=          （Main  → Builds/Windows）
  │         BuildWindows -map=supermarket（→ Builds/Supermarket）
  │         BuildWindows -map=sprint4a  （→ Builds/Sprint4A）
  ├ 基线段（全部带 -feel=light）：-d4test / -d6test / -d8test / -d9dlogic / D3 四客户端
  ├ 白盒段：-d12smoke / -d13accept
  └ 4A 段：-s4adeposit / -s4asave(+slow/kill/verify/corrupt/failio) / -s4acart(+noise) /
           -s4ashop / -s4acarrycap / -s4aloop / -s4aloopclient / -s4asyncbreak / -s4aperf
最终打印 S4A_REGRESSION_OK passes=<n> expected=<m>
```

**`expected` 从用例清单文件读，不硬编码**；`passes != expected` 判 FAIL（防止漏跑一段仍打 OK）。

### 8.2b QA 测试钩子对照（HK-01…HK-14）

| HK | 需求 | 4A 落点 | 状态 |
|---|---|---|---|
| HK-01 | 逐件提交事务日志（instanceId/value/state/reason/totalAfter） | `S4A_DEPOSIT cargoId= value= state= reason= balance= legProgress= totalAfter=` | ✅ 计划内（Story 1 CD2） |
| HK-02 | 去重返回**可断言的拒绝**（不能靠销毁规避） | `S4A_DEPOSIT_DUP cargoId= balanceBefore= balanceAfter=`，且 `balanceAfter==balanceBefore`；去重入口可**按 cargoId** 触发 | ✅（Story 1 CD3） |
| HK-03 | 重复提交注入开关 | `-s4adeposit` 内部服务端直接 `ServerDepositId(cargoId, value)` 同 id 提两次 | ✅ |
| HK-04 | 钱包原子写 + 轮换备份 + 可立即读回 | `WriteAtomic`（`Flush(true)`）+ `sav/bak/bak2` + `-s4asave` 后立即 `-s4asave-verify` | ✅（Story 2） |
| HK-05 | 写盘失败注入 | `-s4asave-failio` / 只读 `-savedir` → `S4A_SAVE_FAIL reason=` | ✅（Story 2 CD8） |
| HK-06 | 余额校验导出（期望值外部传入） | `GATE_WALLET expected= actual= delta= path=` + `-expect=` | ✅（Story 1 CD8 / Story 2 CD9） |
| HK-07 | 服务端/客户端余额一致性 + 抑制注入 | `GATE_WALLET_SYNC server= client= match=` + `-s4asyncbreak` | ✅（Story 4 CD8） |
| HK-08 | 推车清单 + 停滞计时 | `GATE_CART count= mass= ids=` / `GATE_CART_STALL duration= mass= count= where= rescued=(self\|unstick\|fail)` | ✅（Story 3 CD11） |
| HK-09 | 计时 | `GATE_TIMER tag=(level_total\|stair_cycle\|unload_narrow) ms=` | ✅（Story 3 CD6 / Story 5） |
| HK-10 | 购买事务 + 撤销 + 拒绝码 | `GATE_BUY` / `GATE_BUY_REJECT reason=` / `GATE_BUY_UNDO` / `GATE_BUY_UNDO_REJECT reason=` | ✅（Story 4 CD3） |
| HK-11 | 关卡切换与资金继承（配额不硬编码） | `GATE_LEVEL n= quota= source=(table\|fallback) before= after=` | ✅（Story 4 CD6） |
| HK-12 | 搬运方式（A3 唯一判据源） | `GATE_HEAVY_MANUAL cargoId= mass= via_cart=False` | ✅（Story 3 CD10） |
| HK-13 | 帧率 + kbps + 物件数 | `S4A_PERF_OK fps= kbps= props= carts=`，复用 `NetworkDiagnostics` | ✅（Story 5 CD4） |
| HK-14 | 4A 版断线重连注入 | `-s4aloopclient` 断开重连到同一 Host；**能测的仅此一种**（无断线续玩，D9 房主迁移 LOCKED） | ⚠️ 部分：仅覆盖「客户端无账本」，不替代强杀 |

**没有一项降级为人工录屏**：HK-01…HK-13 全部自动化，HK-14 是语义受限（不是做不到）。QA 门里"缺项可降级人工录屏"的那几条，4A 不需要动用。

### 8.3 「禁止假实现 / 防假通过」清单（与 `P0_AcceptanceGate.md` §5.2 合并为一份）

**前 7 条是「不许假装做完」，后 6 条是「不许假装测过」** —— 后者更阴险，因为测试报告全绿。

| # | 禁令 | 4A 的具体含义 |
|---|---|---|
| 1 | **不做假联机** | 不伪造「已联机」状态；不显示不存在的房主迁移（沿用 D9 的 `LOCKED` 标注风格）；双人验收必须真起两个进程 |
| 2 | **不做假存档** | 不许 HUD 显示「已保存」而没写盘；不许退出时统一刷盘；不许内存态冒充持久；损坏恢复必须留 `corrupt-*.sav` 证据 |
| 3 | **不做假购买** | 每件商品必须有效果断言；余额不足不许「买成功」；撤销必须真回滚金额并落盘 |
| 4 | **不假称噪音系统** | 只写「采集与归因 v0，无 AI 消费方」，不得写成「噪音系统已完成」 |
| 5 | **不假称纯物理载货** | 若 R1 降级到软吸附，必须在 DevLog 与技术债里写明 |
| 6 | **不假称全队确认** | 4A 的「房主代决」必须写在 HUD 上 |
| 7 | **不假称无限内容** | 4A 的「下一关」是同一张图重载，不是程序化地图 |
| 8 | **不靠「物件已销毁」规避重复提交** | A6 的去重必须能被显式触发并按 `cargoId` 拒绝（HK-02/03）。靠"物件没了所以不可能重复"是假通过 |
| 9 | **不靠内存自算期望值** | `GATE_WALLET` 的 `expected` 必须外部传入；测试内部自算期望值再自比等于没测 |
| 10 | **不靠「改了 transport 没改 `Transport.active`」** | D11 踩过：`NetworkManager.transport` 与 `Transport.active` 是两处，只改一处会测出 `rtt=24ms` 的假通过 |
| 11 | **不靠「只挪玩家不挪货」** | D13 踩过：瞬移玩家不带货 → 服务端 `breakDistance` 首帧判脱手 → 结算永远 0，而测试"跑完了" |
| 12 | **不按采样序号对齐日志** | D6.5 踩过：两进程采样相位不同，序号对齐毫无意义 → 必须按 UTC 墙钟对齐（D9 已改成只取 ≤120ms 配对的写法） |
| 13 | **不靠「退出时刷盘」蒙混强杀测试** | `Environment.Exit` 与外部 `taskkill /F` 结果必须一致；不一致即判不合格（V3.0 17.4） |

### 8.4 4A 技术债 / 临时实现登记（4A 结束时这些还是债）

| # | 欠账 | 何时还 |
|---|---|---|
| T1 | 关卡差异只有 legIndex，货物不随机、难度不递增 | 4B |
| T2 | 推车无真实侧翻，只有重心偏移 + 转向变慢 + 卸落倾向 | P1 |
| T3 | 推车无耐久/损坏/维修，`工具修理包` 不上架 | P1 |
| T4 | 推车无任何改件（撞刺/震荡/护板/绑带架/工具挂架） | P1（V3.0 明列） |
| T5 | 公秤只支持「整车一次全提交」，不支持挑件提交。**这不只是 P1 舒适项** —— P0 §四 的「7:30–9:30 整车提交 vs 留一部分冲下一关」这个核心决策依赖挑件，P0 门（4C）必须补齐。**推论：4A 不存在"留货冲下一关"，所以 A10 的节奏证据不得从 4A 取**（4A 也没有过关额梯度与怪物压力） | 4C（P0 门）必还 |
| T6 | 商品 4 件（P0 目标 8 件）。**这是阶段裁剪，不是永久放弃**。缺的 4 件全部因为能力缺口无法断言：自救针、急救包（需倒地与受伤）、工具带（需工具槽系统）、体能训练（需耐力系统） | 随怪物进 P0（4B），补齐到 8 件 |
| T7 | 无工具槽位系统，耐用品「买到即全队生效」 | P1 |
| T8 | 存档无加密/签名/防篡改；本地存档不可信 → 不做排行榜 | P2（服务端验证能力具备后） |
| T9 | 单队伍档案 `team_default`；无「加入他人队伍用其档案」 | P1 |
| T10 | 收工/继续在多人下是「房主代决」，不是全队确认 | P1 |
| T11 | 无暂停（V3.0 单人暂停/安全屋暂停） | P1 |
| T12 | 全部 4A UI 是 IMGUI 临时件 | D32 / P1 |
| T13 | 噪音无 AI 消费方 | 第一只怪物 |
| T14 | 「继续/收工」结算界面只有文本，无演出、无 7 项结算指标 | P2 |
| T15 | 场景切换若触发 R2 降级 = 单场景双区域 | 4B |
| T17 | **A3 / A4 / A5 在 4A 是「无追猎压力下的空跑」**。等怪物进来，满载推车在追猎里的实际通过率可能低于 4A 数据。A4 的 1.5 s 阈值若 4A 实测均值贴线（≥1.3s），必须当场报给 design，不等 4B。 | 4B（怪物上线后复测） |
| T18 | `Night1` / `Safehouse` 的锚点数固定 16、关卡配置同构，是 **4A 临时口径**，不等于 P0 的 12/14/16 三关节奏 | 4B（只改 `-anchors=` 与配额表） |
| T19 | **4B 恢复 `TraitBootstrap` 之前，必须先补一条离线断言：身份 × 缺点牌 × 商品同轴项只叠一次。** 否则 `CarryDivisor × card × carryPenaltyScale` 会三重叠加 —— 4A 现在靠"删掉 `TraitBootstrap`"绕过了这个风险，4B 一恢复就会正面撞上 | 4B（恢复身份前的前置门） |
| T16 | 配额表只有 4A 两关一个值（2632），未接模式表（4A 只有普通模式一套参数） | 4B |

### 8.5 决策状态（已定 / 待定）

**本轮已被设计与 team-lead 拍板、不再是待定项**：
| 事项 | 结论 | 来源 |
|---|---|---|
| 4A 商品件数 | **4 件**（防滑手套/打包布带/强光手电/握力训练），全在 P0 的 8 件表内 | design v1.1 |
| A4 卡死阈值 | **1.5 s + 自动脱困**（无脱困的旧分支已删除），+0.5 人日 | design v1.1 §2.7 |
| A4 满载构成 | **≥110kg 且单层不堆叠**（114kg 三件） | design v1.1 §2.1 |
| `S4A_CARRY_CAP` 判据 | **按质量分段 + 硬钳制**（`mass>=15` 才钳到 3.135） | design v1.1 |
| 长按确认时长 | **1.2s** | P0 §17.16 |
| 过关额口径 | **从配额表读**，缺省才回退 `×0.55` | 本文件 C4 表态 |
| 身份中性化 | **场景层删 `TraitBootstrap`** | team-lead 初审第 3 条 |
| 术语边界 | **「4A 之外」**，本文通过 ≠ P0 通过 | team-lead 初审第 1 条 |

**仍需用户拍板的 4 项**（不影响开工，只影响 Story 4 的风险与工期）：

| # | 取舍 | 我的建议 | 不选另一边的代价 |
|---|---|---|---|
| 1 | **场景方案**：双场景 + Mirror `ServerChangeScene` vs 单场景分区传送 | **双场景**（一次到位，玩法代码不返工） | 单场景更稳，但 P1 一定要重写流程层 |
| 2 | **Night1 新建副本 vs 直接改 Supermarket** | **新建**（保住 D12/D13 基线） | 直接改省一个场景，但 D12/D13 基线全废 |
| 3 | **载货物理严格度**：纯物理（会抖）vs 竖直软约束 | **竖直软约束**（水平仍自由） | 纯物理有 30% 概率在 4A 内调不稳 |
| 4 | **双人验收强度**：只保「能连能提交」vs 完整 1 关 | **完整双人 1 关 + 单人 2 关** | 提高强度会把 4A 拖长 2–3 天 |

> 原「Story 1 是否就带存档」一项已按 team-lead「先内存态」的执行偏好定为：Story 1 内存态、Story 2 补盘。

---

## 附录 A · 日志码与命名规范

- 新日志统一前缀 `[S4A]`，码形 `S4A_XXX`，与既有 `D12_/D13_/D65_` 风格一致，便于批处理 grep。
- **`S4A_*` 码**（本计划自有）：`S4A_DEPOSIT` / `S4A_DEPOSIT_DUP` / `S4A_DEPOSIT_ZERO` / `S4A_DEPOSIT_TALLY` / `S4A_SAVE_OK` / `S4A_SAVE_VERIFY_OK` / `S4A_SAVE_FAIL` / `S4A_SAVE_RECOVERED` / `S4A_SAVE_CORRUPT_RECOVERED` / `S4A_SAVE_LOCKED` / `S4A_CART_OK` / `S4A_CART_NOISE_OK` / `S4A_SHOP_OK` / `S4A_CARRY_CAP_OK` / `S4A_LOOP_OK` / `S4A_PERF_OK` / `S4A_NEUTRAL_OK` / `S4A_VALIDATE_OK` / `S4A_QUOTA_FALLBACK` / `S4A_REGRESSION_OK`
- **`GATE_*` 码**（QA `P0_AcceptanceGate.md` 约定的验收门码，字段名不许改）：`GATE_WALLET` / `GATE_WALLET_SYNC` / `GATE_CART` / `GATE_CART_STALL` / `GATE_TIMER` / `GATE_BUY` / `GATE_BUY_REJECT` / `GATE_BUY_UNDO` / `GATE_BUY_UNDO_REJECT` / `GATE_LEVEL` / `GATE_HEAVY_MANUAL`
- 命令行开关：`-s4adeposit` / `-s4asave` / `-s4asave-slow` / `-s4asave-kill` / `-s4asave-verify` / `-s4asave-failio` / `-s4acorrupt` / `-s4acart` / `-s4acart-noise` / `-s4ashop` / `-s4acarrycap` / `-s4aloop` / `-s4aloopclient` / `-s4asyncbreak` / `-s4aperf` / `-savedir=<abs>` / `-expect=<n>` / `-anchors=<n>` / `-feel=light` / `-notraits`
- **字段契约（不许改）**：

  | 用途 | 行格式 | 判据 |
  |---|---|---|
  | A6 总额对账 | `S4A_DEPOSIT_TALLY accepted= uniqueCargo= expectedSum= actualSum= dupBlocked= ok=` | `accepted==uniqueCargo && actualSum==expectedSum && dupBlocked>=1` |
  | A6 逐件对账 | `S4A_DEPOSIT cargoId= value= state= reason= balance= legProgress= totalAfter=` | 每件一行；`Σ Committed.value == actualSum` |
  | A6 去重 | `S4A_DEPOSIT_DUP cargoId= balanceBefore= balanceAfter=` | `balanceAfter==balanceBefore` |
  | A8 存档不丢 | `S4A_SAVE_VERIFY_OK txn= dup= balance= sumCommitted=` | `balance==sumCommitted`、`dup==0` |
  | A8 金额校验 | `GATE_WALLET expected= actual= delta= path=` | `delta==0`，`expected` **外部传入** |
  | A8 双端一致 | `GATE_WALLET_SYNC server= client= match=` | `match==true`；`-s4asyncbreak` 下必须变 `false` |
  | A4 卡死 | `GATE_CART_STALL duration= mass= count= where=` | `duration<1500`；判据：0.75 s 窗口内位移 <0.08m 记停滞，连续 2 次判卡死 |
  | A5/A10 计时 | `GATE_TIMER tag= ms=` | `tag=stair_cycle` 时 `ms<90000` |
  | A3 徒手 | `GATE_HEAVY_MANUAL cargoId= mass= via_cart=False` | `via_cart==False` 且该 id 不在任何 `GATE_CART` 清单里 |
  | 商品 | `GATE_BUY itemId= price= balanceAfter= purchaseId=` / `GATE_BUY_REJECT … reason=` / `GATE_BUY_UNDO …` / `GATE_BUY_UNDO_REJECT …` | 二次撤销必须 `REJECT` |
  | 关卡 | `GATE_LEVEL n= quota= source= before= after=` | `source==table` |
  | 性能 | `S4A_PERF_OK fps= kbps= props= carts=` | `fps>=60 && kbps<=600` |
  | 中性前置 | `S4A_NEUTRAL_OK traits=none feel=light` | 回归脚本第一行，不满足即退出 |

- 命名空间：`ScavengeNightmare.{Flow,Economy,Cart,Save,Shop,Noise}`，UI 沿用 `ScavengeNightmare.UI`。

## 附录 B · 4A 交付物清单

| 类型 | 交付物 |
|---|---|
| 场景 | `Night1.unity`、`Safehouse.unity`（均由 `S4ASceneSetup` 幂等生成） |
| Prefab | `Cart.prefab`、`S4AGameRoot.prefab` |
| 数据 | `CartProfile.asset`、`ShopCatalog.asset`、4 × `ShopItem_*.asset`（防滑手套/打包布带/强光手电/握力训练，id 与价格同 `P0_VerticalSlice.md` §2.5） |
| 代码 | §2.2 的 33 个新文件（一文件一类）+ 4 处既有脚本的开关式改动 |
| 编辑器 | `S4ASceneSetup`（Setup/Validate）、`BuildWindows -map=sprint4a` |
| 脚本 | `run_regression_4a.bat` |
| 文档 | 本文件、`Docs/Sprint_4A_DevLog.md`、`Docs/Networking.md` 4A 追加节 |
| 构建 | `Builds/Sprint4A/`（可双开验收） |
