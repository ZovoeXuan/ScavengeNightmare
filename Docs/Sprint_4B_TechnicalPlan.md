# Sprint 4B 技术实施计划（压力源与一局成立）

> 目标：把 P0 从「数据层与物理层已交卷」推进到「有一局完整压力体验」：噪音被怪物消费、玩家会受伤/倒地、诅咒生效、商店补齐 8 件、三关梯度成立、团灭场景可验。
> 依据：`P0_VerticalSlice.md` v1.2、`P0_AcceptanceGate.md` v1.4、`Sprint_4A_TechnicalPlan.md` v1.4、`Docs/design/Monster_Roster_v1.md`、`Docs/design/Items_Characters_Shop_v1.md`、`Docs/design/Maps_v1.md`、`Docs/DevLog.md`（D1–D13 + 4A）。
> 范围纪律：**只写计划，不写代码、不改代码**。本文件是 Sprint 4B 的唯一技术入口。
>
> ## 本文通过 ≠ P0 通过
>
> P0 的范围由 `P0_VerticalSlice.md` 定义（1 张图、1–3 只怪、2 诅咒、8 商品、3 关梯度、A1–A12）。4B 只补齐其中**压力源与一局成立**的部分；
> A1/A2/A11 的真人 3 关、A10 的真人时长、正式美术音频、房主迁移、暂停、四难度仍不在 4B。
> 4B 收口时的对外措辞只能是 **「一局压力成立，真人门未开」**，不许说「P0 已完成」。
>
> **版本记录**
> - v1.0（2026-10-30，首版）：按三份 4B 设计底稿收口；明确「4B 不接击杀经济」「4B 只用 Night1 一张图」「过关额 1800/2400/2800」「噪音 HUD 是基线不是商品」四条裁决；给出 5 个 Story、8 条新测试钩子、21 条回归用例与 A1–A12 覆盖表。

---

## 零、先裁决六件事（否则 4B 一定返工）

| # | 争议 | 4B 的裁决 | 依据 / 代价 |
|---|---|---|---|
| **R1** | 设计稿 v1 允许击杀、尸体可提交、武器与掉落经济；章程 17.20 明确「不用击杀怪物提供经济收益」 | **4B 不接击杀经济**：怪物是压力源，不是战利品。投掷/推车/手电只做**驱散、击退、短硬直**；尖啸罐可被提前引爆（无掉落）；不实现 HP 死亡、尸体、武器、伤害公式 | 章程不可妥协项优先。设计稿的 HP/伤害/尸体整章降为 **Post-P0 设计储备**，等用户明确修订 P0 范围后再立项，不能顺手做进来 |
| **R2** | 设计稿 Maps_v1 给了 L1/L2/L3 三张新图；P0 §1 写「同一张手工图，差异只在锚点/诅咒/过关额」 | **4B 只用 Night1 一张图**；L2/L3 地图降为 Post-4B 的 Should/Could | 一张图 + 三种锚点分布已经能验证 A1/A2/A7/A9/A10；三张新图会把 4B 变成 4C |
| **R3** | 设计稿已裁决过关额 2400/2600/3000（按三张新图总额 4600/4365/5335）；P0 §3.3 原表是 1800/2400/2800（按 3155/4365/4785） | **4B 用 1800/2400/2800**，因为 4B 用的是 P0 的 12/14/16 锚点构成，不是 Maps_v1 的三张新图。三关占比 **57.0% / 55.0% / 58.5%**，全在 50–60% 带内 | 把 2400 套到 3155 的图上会变成 76%，第 1 关失去教学属性。设计稿的 2400/2600/3000 只在「L1/L2/L3 三张新图都建完」时成立，写进技术债。**生成版 `ScavengeNightmare_DesignDoc.html` 里「过关额统一 2632」是 4A 历史口径，不是 4B 配额**；以本文件 §4.6 为准 |
| **R4** | 设计稿 Items 把「噪音表 550」当商品卖；P0 §2.4 与 A9 要求 HUD **必须**显示噪音主要来源与方向 | **噪音 HUD 是系统基线，不是商品**；商店不卖 `noise_meter`。A9 的证据不能被「没买就看不到」破坏 | 卖 A9 的判据等于把验收项做成付费项 |
| **R5** | 设计稿 Items 新增 11 件商品；P0 §2.5 的 4B 批次只有 4 件 | **4B 商店 = 4A 4 件 + 4 件**：自救针 120 / 急救包 200 / 工具带 700 / 体能训练 1200。设计稿其余 7 件（撬棍/拖车钩/投掷背带/信号棒/噪音表/加宽板/静音轮）随击杀与推车改件一起进 Post-P0 | 8 件是 P0 门票；多上的每一件都必须能被自动断言，否则就是假购买 |
| **R6** | 设计稿提到角色差异与第四队友槽；P0 明确不做角色选择 | **Night1 继续 `traits=none`**；身份/缺点牌仍只在 Main / Supermarket 的 D8 路径生效。4A 的 T19（身份×牌×商品同轴只叠一次）继续留债 | 4B 重点是压力源，不是角色系统 |

> **三项需要用户事后确认但不影响开工**：R1 的击杀经济是否在 P0 之后单独立项；R3 的 1800/2400/2800 是否接受；R2 的三张新图是否留到 4C。默认按上表执行，改口径只改数据表，不改流程代码。

---

## 一、现状

### 1.1 4A 已经交付（4B 直接复用，不再造）

| 能力 | 落点 | 证据 |
|---|---|---|
| 13 条 4A 自动化全绿 | `Tools/run_regression_4a.bat` + `Tools/regression_4a_cases.txt` | 连跑 3 轮 `passes=13 expected=13` |
| 服务端公共账本 + 原子存档 | `Economy/TeamAccount.cs`、`Save/P0SaveService.cs` | `S4A_SAVE_VERIFY_OK`、强杀/损坏恢复 |
| 公秤整车提交 + 去重 | `Economy/QuotaTerminal.cs` | `S4A_DEPOSIT_TALLY` |
| 折叠推车 | `Cart/CartController.cs`、`CartCargo.cs` | A4 30m / A5 3.00m |
| 安全屋 / 商店 / 换关 | `Economy/LevelRouteService.cs`、`LevelPortal.cs`、`ShopTerminal.cs` | `S4A_LOOP_OK legs=2` |
| 噪音采集与归因记忆 | `Noise/NoiseBus.cs` | `NOISE_ADD` 带 source/position；**无消费方** |
| 白盒地图 + 16 锚点 + 5 巡逻锚点 | `Scenes/Night1.unity`（由 `S4ASceneSetup` 生成） | `S4A_VALIDATE_OK anchors=16` |

### 1.2 4B 要新建的全部东西

- **没有玩家生命**：没有 HP、受伤、倒地、救援、团灭；`PlayerMotor` 只有被砸硬直。
- **没有怪物**：`PatrolAnchor` 只是摆位，`NoiseBus` 没有消费方，A9 无法验。
- **没有诅咒**：两件诅咒物、60% 折价、休眠规则都不存在。
- **没有三关梯度**：`LevelRouteService` 两关同配额 2632，Night1 固定 16 锚点。
- **商店只有 4 件**：缺自救针/急救包/工具带/体能训练，且商品效果类型只有 4 个。
- **没有 A8 团灭场景**：4A 只覆盖正常退出/强杀/重连 3/4。
- **没有正式噪音 HUD**：`NoiseBus` 的等级与来源只进日志与 CSV，玩家看不到。

### 1.3 不可动基线

| 场景 | 纪律 |
|---|---|
| `Main.unity` | D3/D4/D6/D8/D9/D11 的沙盒基线，**一行不动** |
| `Supermarket.unity` | D12/D13 白盒与裸结算基线，**不手改**（`D12SceneSetup` 会重生成） |
| `Night1.unity` | 4A 基线（16 锚点/公秤/推车/换关），4B 的增量只由 `S4BSceneSetup` 幂等写入 |
| 4A 13 条用例 | **全部保持绿**；唯一允许改动的断言是 `-s4aloop` 的 `quota==2632` → 改为读表（R3 是设计变更，不是基线漂移） |

---

## 二、范围

### 2.1 4B 交付清单

| 项 | 4B 内容 | 复用 | 必须新建 | 关键约束 |
|---|---|---|---|---|
| 玩家生存 | HP 100 / 受伤 / 倒地 / 爬行 / 救援 / 团灭 | `PlayerMotor.Stun`、`GrabController` | `PlayerVitals`、`ReviveInteractor`、`TeamWipeDetector` | 服务端权威；倒地必须掉手持物；团灭余额不丢 |
| 怪物 | 3 只：空眼 / 塑料导购 / 尖啸罐 | `PatrolAnchor`×5、`NoiseBus` | 状态机 + 感知 + 导航 + 生成器 + 占位体 | 7 状态；追逐 3.2 < 空手 3.68；0.8s 攻击前摇；不永久卡墙 |
| 噪音 | HUD 显示等级/来源/方向；怪物本地听觉半径 4/10/16m | `NoiseBus` 记忆模型 | `NoiseNetworkState`、`NoiseHUD`、`AudibleAt` | **全局 70 不得直接触发全图怪**；无来源调查 = 0 |
| 诅咒 | 笑面偶 / 无字牌位 | `GrabbableProp`、`NoiseBus` | 2 件 prefab + `CursedItem` + `CurseRuntime` | 同人最多 1 种生效；超出休眠并明示；未封印 60% 计价 |
| 商店 | 补 4 件到 8 件 | `TeamAccount.TryPurchase`、`EquippedEffects` | 4 个 `ShopItem` + 4 个效果接线点 | 每件都能自动断言；消耗品按人计数 |
| 三关梯度 | 12/14/16 锚点、0/1/2 诅咒、1/3/4 怪（追逐上限 2）、1800/2400/2800 | `PropSpawnAnchor`、`LevelRouteService` | `legsMask` + `LegContentTable` + `MonsterDirector` | 配额从表读；比例必须 50–60% |
| 团灭 | 全队倒地 → 本趟结束、余额保留、货丢失 | `TeamAccount`、`LevelRouteService` | `TeamAccount.ServerEndRun` | 盘上余额逐位不变；进度与 cargo 清空 |
| 性能 | 1 推车 + 40 物 + 4 怪（L3 满配） | `NetworkDiagnostics` | `S4BPerfTest` | ≥60fps@1080p、≤600kbps |

### 2.2 4B 明确不做（写死，防范围膨胀）

| 不做 | 归属 |
|---|---|
| 击杀 / HP 死亡 / 尸体 / 武器 / 伤害公式 / 掉落经济 | Post-P0（R1） |
| L2 冷链仓 / L3 医院 / BOSS / T3 精英 / 其余 12 种怪 | Post-4B（R2） |
| 封印系统 / `CURSED_TEAM` / `CURSED_ZONE` / 显咒粉 | P1 |
| 角色选择 / 身份倍率恢复 / 第四队友槽 | P1（R6） |
| 工具型货物的主动使用（人字梯/铜锣/灭火器/门板/收音机） | P1 |
| 真刚体侧翻 / 推车改件 / 推车耐久 | P1 |
| 四难度 / 正式美术音频 / 手柄 / 多语言 / 房主迁移 / 暂停 | P0 外 |
| 云端存档 / 排行榜 / 反作弊 | P2 |

---

## 三、架构

### 3.1 分层

```
[表现层]  UI/NoiseHUD、DownedHUD、CurseHUD、RevivePrompt
          —— 只读 SyncVar，不持有游戏状态
[流程层]  Flow/LegContentTable            —— 每关锚点/诅咒/怪物/配额
          Flow/LevelRouteService          —— 已存在，4B 只扩配额与每关重建
[玩法层]  Monsters/MonsterBrain 及子类      —— 服务端状态机与感知
          Combat/PlayerVitals               —— HP / 倒地 / 自救 / 救援
          Combat/TeamWipeDetector           —— 团灭判定
          Curses/CurseRuntime               —— 诅咒持有/休眠/折价/发笑
          Cart/*                            —— 已存在，不接击杀
[账本层]  Economy/TeamAccount              —— 已存在，4B 只加 ServerEndRun
[基础设施] Noise/NoiseBus                  —— 已存在，4B 加 AudibleAt（不改 Decay）
          Noise/NoiseNetworkState           —— GameRoot 上的同步桥
          Monsters/MonsterSpawner           —— 服务端生成与每关重建
```

**分层铁律（4B 追加）**
- 只有 `PlayerVitals` 能改 HP/倒地；怪物只能调 `ServerApplyDamage`，不许直接改玩家状态。
- 只有 `TeamWipeDetector` 能判团灭；任意单点伤害不得直接触发本趟结束。
- 只有 `CurseRuntime` 能判「哪种诅咒生效/休眠」；`CursedItem` 只描述自己。
- 只有 `MonsterSpawner` 能生成怪物；场景里只摆 `MonsterSpawnAnchor`。
- 怪物不得调用 `TeamAccount.Deposit`；尖啸罐死亡也不产生入账。

### 3.2 新增组件清单（一文件一类，D12 教训）

| 文件 | 基类 | 权威方 | 职责 |
|---|---|---|---|
| `Scripts/Monsters/MonsterProfile.cs` | ScriptableObject | — | 单只怪的速度/视野/听觉/攻击/丢失/生成参数 |
| `Scripts/Monsters/MonsterState.cs` | enum | — | Spawn/Patrol/Investigate/Chase/Attack/Stunned/Search（7+1） |
| `Scripts/Monsters/MonsterBrain.cs` | NetworkBehaviour | 服务端 | 状态机骨架、目标选择、攻击前摇、SyncVar |
| `Scripts/Monsters/HollowEyeBrain.cs` | MonsterBrain | 服务端 | 空眼：噪音驱动 + 手电僵直 + 冲撞 |
| `Scripts/Monsters/PlasticGreeterBrain.cs` | MonsterBrain | 服务端 | 塑料导购：视线冻结 + 直线冲刺 |
| `Scripts/Monsters/ScreamCanBrain.cs` | MonsterBrain | 服务端 | 尖啸罐：定点警报 + 提前引爆 |
| `Scripts/Monsters/MonsterSenses.cs` | 纯类（server） | 服务端 | 视线锥 + LOS + `NoiseBus.AudibleAt` |
| `Scripts/Monsters/MonsterNavigator.cs` | 纯类（server） | 服务端 | 锚点巡逻、直线追击、避障、防卡死 |
| `Scripts/Monsters/MonsterVisual.cs` | MonoBehaviour | 客户端 | 占位胶囊染色、前兆脉冲、状态表现 |
| `Scripts/Monsters/MonsterSpawnAnchor.cs` | MonoBehaviour | — | 场景摆位 |
| `Scripts/Monsters/MonsterSpawner.cs` | MonoBehaviour（NM） | 服务端 | 按 `LegContentTable` 生成/清理怪物 |
| `Scripts/Monsters/MonsterDirector.cs` | MonoBehaviour（NM） | 服务端 | 追逐预算：同屏最多 2 只处于 Chase，其余留在 Investigate/Patrol |
| `Scripts/Combat/PlayerVitals.cs` | NetworkBehaviour | 服务端 | HP、倒地、无敌、自救、团灭输入 |
| `Scripts/Combat/ReviveInteractor.cs` | NetworkBehaviour | 服务端 | 队友长按 E 4s 救援，进度 SyncVar |
| `Scripts/Combat/TeamWipeDetector.cs` | MonoBehaviour（NM） | 服务端 | 全队倒地 → `ServerEndRun` |
| `Scripts/Combat/PlayerStamina.cs` | MonoBehaviour（本地） | 客户端 | 体力 100/140、跑耗、回复、HUD 值 |
| `Scripts/Combat/BeltSlot.cs` | NetworkBehaviour | 服务端 | 工具带第 2 小件槽（≤7kg），G 取放 |
| `Scripts/Noise/NoiseNetworkState.cs` | NetworkBehaviour | 服务端 | `noiseLevel/source/dir` 的 SyncVar 桥 |
| `Scripts/Noise/NoiseHud.cs` | MonoBehaviour | 客户端 | 噪音条 + 主要来源 + 方向箭头 |
| `Scripts/Curses/CursedItem.cs` | MonoBehaviour | — | 单件诅咒：类型、发笑/放下规则、60% 计价 |
| `Scripts/Curses/CurseRuntime.cs` | MonoBehaviour（NM） | 服务端 | 持有者跟踪、休眠、发笑计时、Rpc |
| `Scripts/Curses/CurseHud.cs` | MonoBehaviour | 客户端 | 当前诅咒/休眠/倒计时/60% 明示 |
| `Scripts/Flow/LegContentTable.cs` | ScriptableObject | — | 每关锚点掩码/诅咒数/怪物数/配额/安全期 |
| `Scripts/UI/DownedHud.cs` | MonoBehaviour | 客户端 | 倒地遮罩、救援进度、自救倒计时 |
| `Scripts/UI/RevivePrompt.cs` | MonoBehaviour | 客户端 | 队友倒地时的「长按 E 救援」提示 |
| `Scripts/Editor/S4BSceneSetup.cs` | static Editor | — | 幂等写入 4B 锚点/掩码/生成器/校验 |
| `Scripts/Networking/S4BVitalsTest.cs` | MonoBehaviour | — | `-s4bvitals` / `-s4bdown` |
| `Scripts/Networking/S4BWipeTest.cs` | MonoBehaviour | — | `-s4bwipe` |
| `Scripts/Networking/S4BNoiseTest.cs` | MonoBehaviour | — | `-s4bnoise` |
| `Scripts/Networking/S4BMonsterTest.cs` | MonoBehaviour | — | `-s4bmon` / `-s4bgreeter` / `-s4bscream` |
| `Scripts/Networking/S4BCurseTest.cs` | MonoBehaviour | — | `-s4bcurse` / `-s4blegcurse`（`-s4bleg` 归 S4B-5，见 Story S4B-4） |
| `Scripts/Networking/S4BShop8Test.cs` | MonoBehaviour | — | `-s4bshop8` |
| `Scripts/Networking/S4BLegTest.cs` | MonoBehaviour | — | `-s4bleg` / `-s4blegclient` |
| `Scripts/Networking/S4BPerfTest.cs` | MonoBehaviour | — | `-s4bperf` |

### 3.3 对既有脚本的全部改动（只列必要项）

| 文件 | 改动 | 为什么安全 |
|---|---|---|
| `PlayerMotor.cs` | 新增 `SetVitalsSpeedCap(float)`（默认 -1，与推车 cap 取 min）；倒地时禁止跳/冲刺 | 默认值不改变 D3–D13 行为 |
| `GrabController.cs` | 新增 `SetInputEnabled(bool)` 已有；增加「放下需长按」读取 `CursedItem.requiredReleaseHold`；倒地时强制释放 | 只有诅咒物带该字段，普通货为 0 |
| `GrabbableProp.cs` | `CurrentValue` 增加 `CursedItem` 分支（60% 计价） | 无诅咒组件时行为不变 |
| `PropSpawnAnchor.cs` | 新增 `legsMask`（默认 7 = 1/2/3 关都生成） | 默认值让 4A/Supermarket 完全不变 |
| `PropSpawner.cs` | 按当前 leg 过滤 `legsMask`；`-s4a*` 进程忽略过滤（见 §3.6） | 4A 回归仍拿到 16 件 |
| `LevelRouteService.cs` | 配额表改 `{1800,2400,2800}`；换关后调 `S4B` 每关重建 | 4A 只依赖「读表」行为，不依赖 2632 |
| `TeamAccount.cs` | 新增 `ServerEndRun(reason)`：余额保留、leg 归 1、进度清零、runId 换新；不改入账路径 | 只有团灭调用 |
| `S4ABootstrap.cs` | `EnsureSceneObjects()` 追加 `MonsterSpawner.RespawnForNewScene()` 与 `CurseRuntime.RespawnForNewLeg()`（空引用跳过） | 4A 场景没有这两个组件，零行为变化 |
| `ShopItem.cs` | `ShopEffect` 枚举**只追加** `SelfInjector/Medkit/ToolBelt/Stamina`；`maxOwned` 支持消耗品上限 | 位掩码按目录顺序，追加不重排 |
| `ShopCatalog.cs` | 目录追加 4 件（顺序只许追加） | 老存档 ownedMask 位序不变 |
| `EquippedEffects.cs` | 增加 4 个查询（HasBelt/HasStaminaTraining/HasMedkit/...） | 纯新增 |
| `S4ACli.cs` | `IsTestRun` 增加 `-s4b*`；新增 `Is4ARun`（只看 `-s4a` 前缀） | 4A 兼容闸门需要 |
| `PlayerInteract.cs` | 商店热键从 1–4 扩到 1–8；其余不动 | 1–4 行为不变 |
| `ShopTerminal.cs` | 非安全屋购买拒绝 `reason=wrong_scene`；`-s4a*` 进程跳过该检查 | 4A `-s4ashop` 仍绿；正式玩法不能在夜路买 |

> 纪律：其余既有脚本不得修改。需要新行为一律加新文件。

### 3.4 Mirror 权限矩阵（4B 新增）

| 状态 | 权威方 | 机制 | 理由 |
|---|---|---|---|
| 怪物位置/朝向 | **服务端** | `NetworkTransformReliable` ServerToClient 15Hz | 客户端不得自算 AI |
| 怪物状态/目标 | **服务端** | `SyncVar byte state` + `uint targetNetId` | 状态驱动动画/音效 |
| 怪物攻击判定 | **服务端** | 服务端 0.8s 前摇 → `PlayerVitals.ServerApplyDamage` | 防客户端改伤害 |
| 玩家 HP / 倒地 / 无敌 | **服务端** | `SyncVar ushort hp`、`bool downed`、`float invulnUntil` | 防复活作弊 |
| 救援进度 | **服务端** | `SyncVar float reviveProgress` + 长按 E 上报 | 防秒救 |
| 噪音等级/来源/方向 | **服务端** | `NoiseNetworkState` SyncVar，5Hz | HUD 必须与 AI 同一真值 |
| 诅咒持有者/休眠 | **服务端** | 服务端读 `GrabbableProp.Holder`，`SyncVar byte activeCurse` | 防双持双生效 |
| 诅咒发笑 | **服务端判定** | `RpcCurseLaugh` | 四端同时表现 |
| 团灭 | **服务端** | `TeamWipeDetector` → `TeamAccount.ServerEndRun` | 不能由客户端宣布 |
| 体力 | 客户端本地 | `PlayerStamina` + HUD | 移动本身是客户端权威，P0 无经济影响 |
| 工具带第 2 槽 | **服务端** | `SyncVar uint beltNetId` + `Command` | 防隔空多拿 |

### 3.5 场景与构建

| 场景 | 4B 动作 |
|---|---|
| `Main` / `Supermarket` | 一行不动 |
| `Night1` | `S4BSceneSetup` 幂等追加：6 个 `MonsterSpawnAnchor`、2 个诅咒锚点、`LegContentTable` 引用、16 个锚点的 `legsMask`、`NoiseNetworkState` 到 GameRoot prefab、`MonsterSpawner`/`CurseRuntime`/`TeamWipeDetector` 到 NM |
| `Safehouse` | 只加 `PlayerVitals` 的重生重置逻辑（玩家对象跨场景保留），场景几何不动 |

- 构建仍走 `BuildWindows -map=sprint4a`，输出 `Builds/Sprint4B/`，场景列表 `[Night1, Safehouse]`。
- `Night1` 的 4A 校验项 `anchors=16` **保持不变**；4B 新增 `S4B_VALIDATE_OK anchors=16 legCounts=12/14/16 monsters=6 curseAnchors=2 noiseHud=1`。

### 3.6 4A / 4B 双口径闸门（最容易出假通过的地方）

4A 的 `-s4acart-a4` 需要 `statue + safe`（102kg），而 P0 第 1 关的 12 锚点里**没有 statue**。如果直接按 leg 过滤锚点，4A 的 A4 会当场失效。

**规则**：
```
若进程带 -s4a* 参数（S4ACli.Is4ARun）：
    PropSpawner 忽略 legsMask，仍生成全部 16 件（4A 基线口径）
    配额仍按表读，但 -s4aloop 的断言改为「读 QuotaFor(2)」而不是 2632
否则（正常游玩 / -s4b* 测试）：
    PropSpawner 按当前 leg 过滤 legsMask（12/14/16）
    怪物/诅咒/噪音按 LegContentTable 生效
```
每次生成打一条 `GATE_PROPS_MODE mode=(legacy4a|p0leg) leg=<n> anchors=<n> totalValue=<n>`。
**任何 4B 证据不得来自 `mode=legacy4a`**；任何 4A 证据不得来自 `mode=p0leg`。这条是 4B 防假通过的第一道门。

---

## 四、数据表（首版，全部落下，改表即生效）

### 4.1 玩家生存

| 项 | 值 | 说明 |
|---|---|---|
| HP | 100 | 服务端权威 |
| 单次伤害保护 | HP ≥15 时，任何单次伤害后最低保留 1 | 玩家永不单次即死 |
| 倒地 | HP 锁 1、爬行 0.8 m/s、掉落手持物、禁止抓取/交互 | 倒地不减少结算资格 |
| 队友救援 | 2m 内长按 E **4s**，起身 50 HP | 救援者移动 >2.5m 或松手则进度清零 |
| 自救 | 倒地满 **8s** 后按 R，起身 50 HP；每关免费 1 支 + 商店可加购 1 支 | 免费基线不可被商品替代（17.5） |
| 起身无敌 | 2s | 防连倒 |
| 体力 | 100，跑 -7.5/s，静止 +12/s；训练后 140 / 5.5/s | 客户端本地；HUD 显示 |
| 团灭 | 全队倒地且无人可救 → 3s 宽限 → 本趟结束 | 见 §4.7 |

### 4.2 噪音听觉（A9 的核心）

| 全局等级 | 区间 | 怪物本地听觉半径 | 行为 |
|---|---|---|---|
| 0–39 | 安静 | 4 m | 只巡逻，不因全局值追猎 |
| 40–69 | 警觉 | 10 m | 走向最近的可听声源 |
| 70–99 | 追猎 | 16 m | 走向 `TopSourcePosition`，但仍需本地可听 |
| 100 | 爆发 | 16 m | 一次性，随后回落 |

- `NoiseBus.AudibleAt(listenerPos, radius)`：遍历未遗忘记忆，`attenuation = 1 - distance/radius`，`<0.2` 不触发；返回 `score` 最高的来源。
- **全局 70 不等于全图怪追猎**：怪物必须能听到具体来源才进 Chase；`source == none` 的 Chase 直接判 A9 失败。
- HUD 必须显示：当前等级、主要来源、方向箭头（屏幕边缘）、最近一次可归因事件。

### 4.3 怪物（4B 三只）

| 项 | 空眼 holloweye | 塑料导购 plastic_greeter | 尖啸罐 scream_can |
|---|---|---|---|
| 类型 | T1 噪音追猎 | T1 视线冻结 | T1 定点警报 |
| 巡逻 / 调查 / 追逐 | 0.9 / 1.0 / 3.2 m/s | 0 / — / 3.2（冲刺 ≤0.8s） | 0（定点） |
| 视野 | 12m / 90°，需 LOS | 被任意玩家注视即冻结（25° / 18m / LOS） | 6m 近距触发 |
| 攻击前摇 | 0.8s（吸气+压低+红眼脉冲） | 0.35s 颈裂 + 0.8s 直线冲刺 | 1.5s 滋滋升调 |
| 伤害 | 50（冷却 2.5s） | 30（冷却 3s） | 尖叫 +45 噪音 + 12m 内眩晕 2s |
| 克制 | 手电直射 1.5s → 僵直 2s；推车撞击后退 | 全员移开视线才动；撞墙僵直 1.5s | 前摇内投掷任意物件引爆（噪音 +15，无掉落） |
| 丢失 | 脱离视线 + 噪音回落，4s；搜索最后目击点 3s | 同 | 不适用 |
| 生成安全期 | L1 120s / L2 90s / L3 60s | 同 | 同 |
| 同屏上限 | 2 | 1 | 2 |

- **七状态**：`Spawn → Patrol → Investigate → Chase → Attack → Search → Patrol`，另有 `Stunned` 与 `DeadOnlyForScreamCan`（尖啸罐被引爆后消失，不掉落）。
- **不得永久卡墙**：`MonsterNavigator` 连续 2s 位移 <0.05m 且处于 Chase/Patrol → 自动回退到最近 `PatrolAnchor`（打 `GATE_MON_UNSTICK rescued=self`）；连续两次仍失败 → `rescued=fail` 判失败。
- **楼梯**：怪物使用 `CharacterController`（stepOffset 0.4），可直接走 12×0.25m 台阶；不要求怪物折叠推车。
- **推车**：怪物可被推车推开/撞退，推车不会被怪物永久顶死；同辆车对同怪 0.8s 内只结算一次击退。
- **生成表**：L1 空眼×1；L2 空眼×1 + 导购×1 + 尖啸罐×1；L3 空眼×2 + 导购×1 + 尖啸罐×1。
- **追逐预算（P0 §2.2「最多 2 只」）**：`MonsterDirector` 统一裁决，同屏最多 2 只怪物处于 `Chase`；第 3 只想追时保持 `Investigate` 并打 `GATE_MON_BUDGET denied=<id> active=2`。单人/双人仍是 1 只上限（`-players=1|2` 由人数决定，3–4 人才放到 2）。

### 4.4 诅咒（两件）

| 项 | 笑面偶 | 无字牌位 |
|---|---|---|
| 质量 / 标价 | 2kg / 850 | 15kg / 1300 |
| 规则 | 持有者每 12–20s 发笑，噪音 +8；前 1.5s 头部抖动 + 吸气 | 放下需长按 **1.5s**，不能秒扔；抓握时手抖 |
| 休眠 | 同一玩家最多 1 种生效；第 2 种休眠，HUD 明示 | 同 |
| 折价 | 未封印按 **60%** 计价（510） | 未封印 60%（780） |
| 解法 | 换手 / 放下 / 丢车外 | 提前规划落点 / 队友接 / 绕开窄门 |
| 触发 | L2 起 1 件，L3 起 2 件 | 同 |

- **P0 不做封印**，所以两件在 4B 全部按 60% 计价；HUD 必须常驻「未封印 60%」。
- 发笑必须走 `NoiseBus.Report("curse_doll", 8, holderPos)`，**可归因**；不得只加全局值。

**归属与休眠的裁决口径**（设计未指定，由实现反推确定，写下来以免将来被当成 bug）：

| 规则 | 口径 |
|---|---|
| 归属来源 | 手持 > 随车。随车归属 = **把它装上车的人**（`CartController.TryLoadFrom` 在装车那一刻记 `CursedItem.CartOwnerNetId`），不是当前推行者、也不是离车最近的人——后者会变成「谁路过谁背锅」 |
| 解除 | 卸下 / 掉落 / 提交后 `CartCargo` 里判不到，随车归属立即解除 |
| 双持优先级 | **先入手的生效，后入手的休眠**（`dormant=1`）。这是「第 2 种休眠」的字面读法 |
| 归属人离线 / 倒地 | 整批保持休眠态，**不转移给别人**，避免队友替你背一件他没碰过的诅咒 |
| 裁决留痕 | 每次归属变化打 `S4B_CURSE_OWNER prop=<id> owner=<netId> source=hand|cart` |

P0 没有工具带槽（`tool_belt` / `BeltSlot` 属 S4B-5，且牌位 15kg 超过腰带 ≤7kg 限制），
所以「手持一件 + 车上载着另一件」是当前唯一能成立的双持路径；等工具带做完，
只是在归属来源里多一条，上面这些规则都不用动。

### 4.5 商店 8 件（4A 4 件 + 4B 4 件）

| id | 名称 | 类 | 价 | 效果 | 自动断言 |
|---|---|---|---|---|---|
| `grip_gloves` | 防滑手套 | 耐用品 | 400 | 脱手阈值/跟随速度/加速度 ×1.5 | 已有 |
| `packing_tape` | 打包胶带 | 耐用品 | 500 | 车上水平锚点刚度 ×2 | 已有 |
| `flashlight` | 手电筒 | 耐用品 | 350 | F 开关，18m | 已有 |
| `grip_training` | 握力训练 | 永久 | 900 | 负重减速量与硬上限 ×1.15 | 已有 |
| `self_injector` | 自救针 | 消耗 | 120 | 倒地 8s 后自救至 50 HP；每关 +1 次 | 倒地 9s 后 R 起身，次数 -1；用尽后 R 拒绝 |
| `medkit` | 急救包 | 消耗 | 200 | 对 2.5m 内队友回复 60 HP，施法 3s；对自己无效 | HP 40→100；对自己使用被拒 |
| `tool_belt` | 工具带 | 永久 | 700 | 第 2 小件槽（≤7kg）+ 交互速度 ×1.25 | 手持雕像时仍可把玻璃杯挂到腰上；质量仍计入负重 |
| `stamina_training` | 体能训练 | 永久 | 1200 | 体力 100→140，跑耗 7.5→5.5/s | 连续冲刺时长增加 ≥35% |

- **消耗品按玩家计数**，不进 `ownedMask`；`TeamAccount` 只负责扣钱与落盘，`PlayerVitals` 持有次数。
- `HardCapPerVisit = 8` 保持不变；8 件商品刚好一访客最多买齐，不会出现第 9 件被 hard_cap 误拒。
- **不卖 `noise_meter`**（R4）：噪音 HUD 是基线。

### 4.6 三关梯度（同一张 Night1）

| 关 | 锚点 | 类型构成 | 地图总额 | 过关额 | 占比 | 诅咒 | 怪物 |
|---|---|---|---:|---:|---:|---|---|
| 1 教学 | 12 | 普通5 / 易碎3 / 滚动2 / 重型2 | 3155 | 1800 | 57.0% | 0 | 空眼×1 |
| 2 标准 | 14 | 普通5 / 易碎4 / 滚动3 / 重型2 | 4365 | 2400 | 55.0% | 1（笑面偶） | 空眼×1 + 导购×1 + 尖啸罐×1 |
| 3 压力 | 16 | 普通6 / 易碎4 / 滚动3 / 重型3 | 4785 | 2800 | 58.5% | 2（笑面偶 + 无字牌位） | 空眼×2 + 导购×1 + 尖啸罐×1 |

**锚点掩码（写死在 `S4BSceneSetup`，不许手改场景）**

| 槽位 | profile | 1 关 | 2 关 | 3 关 |
|---|---|---|---|---|
| 1–2 | can ×2 | ✓ | ✓ | ✓ |
| 3 | can ×1 | — | — | ✓ |
| 4–5 | box ×2 | ✓ | ✓ | ✓ |
| 6 | toolbox | ✓ | ✓ | ✓ |
| 7 | vase | ✓ | ✓ | ✓ |
| 8 | tv | — | ✓ | ✓ |
| 9–10 | glass ×2 | ✓ | ✓ | ✓ |
| 11 | barrel | ✓ | ✓ | ✓ |
| 12 | tire | — | ✓ | ✓ |
| 13 | watermelon | ✓ | ✓ | ✓ |
| 14 | statue | — | ✓ | ✓ |
| 15 | cabinet | ✓ | — | ✓ |
| 16 | safe | ✓ | ✓ | ✓ |
| **合计** | | **12** | **14** | **16** |

> 为什么不是设计稿的 2400/2600/3000：那组数是按 Maps_v1 的 4600/4365/5335 算的。4B 用 P0 的 3155/4365/4785，套 2400 会让第 1 关变成 76%，失去教学属性。三关总额与 P0 §3.3 逐字一致，这是可以复算的。

### 4.7 团灭

```
全队玩家 downed（无存活队友可救）
  → 3s 宽限（防止最后一秒被救起）
  → TeamAccount.ServerEndRun("wipe")
      余额：保留（先落盘，后改内存，走 TryCommitState）
      legIndex：归 1
      legProgress：0
      legQuota：表[0] = 1800
      runId：换新（netId 跨场景复用，旧 cargoId 不能带回来）
      committed：清空
      所有未提交货物/推车：销毁（不折价、不入账）
  → GATE_WIPE legBefore=<n> legAfter=1 balanceBefore=<b> balanceAfter=<b> cargoDestroyed=<n>
  → 进 Safehouse，玩家 HP/倒地重置
```
- **余额逐字不变**（A8 第 4 场景）。
- **未提交货物丢失**（Q3）：团灭不折算、不补偿。
- 团灭不是「失败扣钱」，是「这趟白跑，钱还在」。

---

## 五、Story 切分（5 个，按依赖排序）

> 排序原则：先把「伤害落到谁身上」做对，再做「谁制造伤害」；否则怪物一上线就会和「没有 HP」互相打补丁。

---

### Story S4B-1 · 玩家生存：HP / 倒地 / 救援 / 团灭

**范围**：`PlayerVitals`、`ReviveInteractor`、`TeamWipeDetector`、`DownedHud`、`RevivePrompt`；`PlayerMotor.SetVitalsSpeedCap`、`GrabController` 倒地释放；商店 `self_injector` / `medkit`。

**完成定义**
1. `-s4bvitals` 单人：
   - 50 伤害 → HP 50；再 60 伤害 → HP 1（单次保护生效，不倒地）；再 1 伤害 → 倒地。
   - 倒地瞬间：手持物脱落（`holder=None`）、移速 0.8、抓取被禁。
   - 倒地 8s 后按 R → 起身 50 HP、2s 无敌、免费自救针次数 -1。
   - 第二次倒地且无针 → 3s 后 `GATE_WIPE`。
2. `-s4bdown` 双人：client 倒地 → host 在 2m 内长按 E 4s → client 起身 50 HP；中途松手/走开 2.5m → 进度清零并打 `GATE_REVIVE_REJECT reason=interrupted`。
3. `-s4bwipe`：盘上余额写入前 `b0`，团灭后重启读盘 `b1`，`b1==b0`；`legIndex=1`、`progress=0`、`cargoDestroyed>0`；`GATE_WIPE` 字段齐全。
4. 商店生存件：`self_injector` 购买后倒地自救次数 +1 并能真的消耗；`medkit` 对队友 40→100、对自己被拒 `reason=self_target`；余额与盘一致。**8 件全上架与 `-s4bshop8` 归 S4B-5**（工具带/体能属于 S4B-5 的系统）。
5. 回归：4A 13 条全绿（`mode=legacy4a`）。

**测试钩子**：`-s4bvitals` / `-s4bdownhost` + `-s4bdownclient` / `-s4bwipe`

**关键决策**：倒地不减少结算资格（P0 §5.2）；免费自救针是系统基线，不是商品；消耗品按**队伍库存**计数（`TeamAccount` 的 SyncVar），不占 `ownedMask`。

**风险**：HP 与 D6「被砸硬直」两套受伤逻辑叠加 —— 硬直只顿一下，不扣 HP；只有怪物攻击/尖啸罐扣 HP。两者必须分开记日志（`D6_STUN` vs `GATE_DAMAGE`）。

**估时**：3.0 人日

---

### Story S4B-2 · 噪音消费 + 空眼（第一只完整怪物）

**范围**：`NoiseBus.AudibleAt`、`NoiseNetworkState`、`NoiseHud`、`MonsterProfile`、`MonsterState`、`MonsterBrain`、`HollowEyeBrain`、`MonsterSenses`、`MonsterNavigator`、`MonsterVisual`、`MonsterSpawnAnchor`、`MonsterSpawner`、`MonsterDirector`。

**完成定义**
1. `-s4bnoise`：
   - 在 4m/10m/16m 半径外制造噪音，怪物不进入 Investigate；半径内进入，且 `GATE_MON_HEARD` 带 `source/pos/band/attenuation`。
   - 全局等级拉到 70 但没有任何本地记忆 → 怪物**不**进 Chase，`unattributed=0`。
   - 衰减单调：距离越远，`attenuation` 越小。
2. `-s4bmon`（安全期强制 0）：
   - 七状态按序出现：`Patrol → Investigate → Chase → Attack → Search → Patrol`（至少各 1 次）。
   - 追逐实测速度 3.2 ±0.1 m/s，低于空手 3.68；攻击前摇 0.8s ±0.1；丢失目标 ≤6s。
   - 手电直射 1.5s → Stunned 2s；推车撞 → 后退 ≥1m；不穿墙。
   - 走 12 级楼梯爬升 3.00m；连续 2s 卡住 → `GATE_MON_UNSTICK rescued=self`，不出现 `fail`。
3. `GATE_MON_CHASE monster=holloweye state=Chase source=noise:<id>|sight:<player> at=(x,z) band=quiet|alert|hunt level=<n> dist=<n>`：每一次 Chase 都带来源；`source=none` 记 `S4B_A9_FAIL`。
4. `NoiseHud` 在客户端显示等级/来源/方向；`-s4bnoise` 断言 HUD 文本含非空来源。
5. 回归：4A 13 条全绿。

**测试钩子**：`-s4bnoise` / `-s4bmon`

**关键决策**：
- 怪物 AI 全服务端；客户端只看到 15Hz 位置与状态。
- 巡逻走 `PatrolAnchor` 图（D12 已摆 5 个），不烘 NavMesh；追击直接朝目标 + 避障，丢失后回最后目击点，再回最近锚点。
- 空眼不可击杀（R1）；手电僵直 2s、推车击退是仅有的两种驱散手段。

**风险**：
- 货架迷宫导致绕柱失败 → 用避障 + 自动回退锚点兜底；A9 测试必须包含“柱子后丢失”。
- `NoiseBus` 是静态类，换关不重置 → `S4B-5` 每关重建时调 `NoiseBus.Reset()`。

**估时**：3.5 人日

---

### Story S4B-3 · 塑料导购 + 尖啸罐

**范围**：`PlasticGreeterBrain`、`ScreamCanBrain`、`MonsterSpawner` 生成表、`MonsterVisual` 前兆表现。

**完成定义**
1. `-s4bgreeter`：
   - 任一玩家注视（25°/18m/LOS）→ 位移 == 0；全员移开视线 → 0.35s 前摇 + 0.8s 内位移 >2m；撞墙 → 僵直 1.5s。
   - 4 人时 `watchedMask` 正确（每端一位，全部移开后 1s 内启动）。
   - 攻击 30 伤害；不可击杀（只僵直/撞墙）。
2. `-s4bscream`：
   - 6m 内玩家或本地噪音 ≥40 → 1.5s 前摇 → 尖叫：`NoiseBus` +45、12m 内玩家眩晕 2s 并掉手持物。
   - 前摇内投掷任意物件 → 引爆：`NOISE_ADD source=scream_can_preempt amount=15`，无掉落。
   - 尖叫后最近的空眼进入 Investigate（`source=scream_can`），不是全图直接 Chase。
3. `S4B_MON_OK monsters=3 states=7 unattributed=0`。
4. 追逐预算：L3 同时存在 3 只追逐型（空眼×2 + 导购）时，同一时刻 `Chase` ≤2；第 3 只打 `GATE_MON_BUDGET denied=<id> active=2`；单人/双人上限 1。
5. 回归：4A 13 条全绿。

**测试钩子**：`-s4bgreeter` / `-s4bscream`

**关键决策**：尖啸罐是「噪音放大器」，不是击杀目标；它的死亡只产生噪音，不产生钱。

**估时**：2.5 人日

---

### Story S4B-4 · 两件诅咒

**范围**：2 个诅咒 prefab + `PropPhysicsProfile`（笑面偶 2kg/850、无字牌位 15kg/1300）、`CursedItem`、`CurseRuntime`、`CurseHud`；`GrabbableProp.CurrentValue` 60% 分支；`GrabController` 1.5s 放下。

**完成定义**
1. `-s4bcurse`：
   - 两件各触发 ≥1 次发笑/放下；发笑间隔落在 12–20s，噪音 +8，`NOISE_ADD source=curse_doll`。
   - 同一玩家双持（笑面偶 + 牌位，需要 `tool_belt` 槽）→ 只有 1 种生效，第 2 种 `dormant=1`，HUD 明示。
   - 价值：笑面偶 510、牌位 780；HUD 常驻「未封印 60%」。
   - 牌位放下：按 <1.5s 松手 → `GATE_CURSE_HOLD_REJECT need=1.5 actual=<n>`；≥1.5s → 放下成功。
2. L1/L2/L3 生成 0/1/2 件（`-s4blegcurse` 覆盖）。
3. 回归：4A 13 条全绿（普通货 `requiredReleaseHold=0`，不受影响）。

**测试钩子**：`-s4bcurse` / `-s4blegcurse`

> **钩子划分（别再撞）**：本 Story 的关卡梯度钩子是 **`-s4blegcurse`**；
> **`-s4bleg` 由 S4B-5 独占**（配额 1800/2400/2800、锚点 12/14/16、怪物 1/3/4）。
> 两者**不得共用监听器**——两个 fixture 同时认同一个开关，会在同一进程各调一次
> `NetworkManager.StartHost()`，直接崩。S4B-5 的 leg 断言请并进
> `Scripts/Networking/S4BCurseTest.cs`，不要新建第二个 MonoBehaviour。

**关键决策**：P0 无封印，所以恒 60%；休眠不是「隐藏」，HUD 必须写清楚；发笑必须可归因，禁止只加全局值。

**估时**：2.0 人日

---

### Story S4B-5 · 三关梯度 + 工具带/体能 + 回归收口

**范围**：`LegContentTable`、`PropSpawnAnchor.legsMask`、`PropSpawner` 过滤、`LevelRouteService` 配额表、`S4BSceneSetup`、`ShopTerminal` 安全屋限制、`BeltSlot`、`PlayerStamina`、`Tools/run_regression_4b.bat` + `regression_4b_cases.txt`、`Docs/Sprint_4B_DevLog.md`。

**完成定义**
1. `-s4bleg`：
   - 连跑 3 关：`GATE_LEVEL n=1/2/3 quota=1800/2400/2800 source=table`；实际生成 12/14/16 件，总额 3155/4365/4785，比例 57.0/55.0/58.5。
   - 诅咒 0/1/2、怪物 1/3/4（含尖啸罐，追逐上限 ≤2）。
   - 余额单调不减；`legProgress` 每关归零；`dupBlocked≥1`。
   - `GATE_PROPS_MODE mode=p0leg`；不得出现 `legacy4a`。
2. A4 在 P0 第 3 关配置下复跑：`-s4acart-a4 -p0props -leg=3` → 102kg、30m、卡死 <1.5s。
3. `-s4bperf`：1 推车 + 40 活跃物 + 4 怪（L3 满配）→ `S4B_PERF_OK fps>=60 kbps<=600`。
4. 工具带：手持 42kg 时按 G 把 ≤7kg 小件挂腰，负重仍计入；倒地自动掉落；`S4B_BELT_OK`。
5. 体能：连续冲刺时长 ≥35%，`S4B_STAMINA_OK`。
6. `ShopTerminal` 在 Night1 拒绝购买 `reason=wrong_scene`；`-s4a*` 进程跳过（4A 用例保绿）。
7. `run_regression_4b.bat` 一条命令跑完 **21 条**（4A 13 + 4B 8），输出 `S4B_REGRESSION_OK passes=21 expected=21`；`expected` 从清单行数读，不硬编码。
8. 4A 13 条全绿；`-s4aloop` 的 `quota==2632` 断言改为 `QuotaFor(2)==2400`。

**测试钩子**：`-s4bleg` / `-s4blegclient` / `-s4bperf` / `-s4acart-a4 -p0props -leg=3`

**风险**：
- 每关重建怪物/诅咒/噪音：必须在 `S4ABootstrap.EnsureSceneObjects()` 之后，且顺序固定（先清旧怪 → 清噪音 → 生成货 → 生成怪 → 生成诅咒）。
- `LevelRouteService` 的 0.6s 延迟与 `MonsterSpawner` 的安全期重叠 → 怪物可能在玩家还没落地时就生成；统一用「安全期 ≥60s」兜底。
- `BeltSlot` 若做不稳，降级为「交互速度 ×1.25 + 手持小件不计减速」并在 DevLog 明写降级，不许假装是完整第二槽。

**估时**：3.0 人日

**合计：约 14.0 人日**（S4B-1 3.0 + S4B-2 3.5 + S4B-3 2.5 + S4B-4 2.0 + S4B-5 3.0）。

---

## 六、验收与回归

### 6.1 A1–A12 覆盖表（4B 结束时必须照实填）

| A | 指标 | 4B 覆盖 | 证据 |
|---|---|---|---|
| A1 单人 3 关 | 30–40 分钟、0 崩溃 | **自动化 3 关 ✅ / 真人 ❌** | `-s4bleg`，真人留 4C/A11 |
| A2 双人 3 关 | 同上 | **双人 1 关冒烟 ✅ / 3 关真人 ❌** | `-s4blegclient` |
| A3 徒手 ≥35kg | 每关至少 1 件 | ✅（沿用 4A + 每关锚点保证含重型） | `GATE_HEAVY_MANUAL` |
| A4 满载 30m | 102kg、卡死 <1.5s | ✅（P0 第 3 关复跑） | `GATE_CART_STALL rescued=` |
| A5 楼梯 ≤90s | 折叠→搬→展开 | ✅（沿用 4A） | `GATE_TIMER tag=stair_cycle` |
| A6 公秤不重复 | `dupBlocked≥1` | ✅（沿用 4A） | `S4A_DEPOSIT_TALLY` |
| A7 两诅咒 | 各触发 ≥1、仅 1 生效、60% | ✅ | `-s4bcurse` + `GATE_CURSE_*` |
| A8 资金不丢不复制 | 4 场景差额 = 0 | ✅ **4/4**（团灭补齐） | `-s4bwipe` + 强杀/重连/正常 |
| A9 噪音可归因 | 无来源调查 = 0 | ✅ | `-s4bnoise` + `GATE_MON_CHASE` |
| A10 单关时长 | 8–12 分钟 | **只记录不判定**：`-s4bleg` 出机器人时长；真人留 A11 | `GATE_TIMER tag=leg_total` |
| A11 真人体验 | ≥30 分钟、再走一关 ≥50% | ❌（4C） | 录像 + 访谈 |
| A12 性能带宽 | ≥60fps、≤600kbps（含怪） | ✅ | `-s4bperf`（L3 满配：1 车 + 40 物 + 4 怪） |

**结论口径**：`4B 覆盖 A3/A4/A5/A6/A7/A8/A9/A12 全量；A1/A2 自动化 3 关 + 真人待 4C；A10 只记录；A11 未覆盖`。
不许把「自动化 3 关」写成 A1/A2 通过，也不许把 A10 的机器人时长当节奏证据。

### 6.2 回归矩阵（4B 每 Story 结束必跑，Story 5 全跑）

| 用例 | 期望 | 4B 为什么可能碰到它 |
|---|---|---|
| D3 四人 | `conns=4 players=4` | 玩家 prefab 加了 `PlayerVitals/BeltSlot` |
| D4 投掷 | **4.14（light）** | `GrabController` 加了倒地释放与长按放下 |
| D6 五条 | 滚动/硬直/易碎/夹手/窄门 | 同上 + 物件 prefab 新增诅咒件 |
| D8 八项 + 15 项 | 全过 | Night1 仍 `traits=none`；Main/Supermarket 不动 |
| D9 七项 + 断线 | 131ms / 3988ms | NM 加了 `MonsterSpawner/TeamWipeDetector` |
| D11 重量感/延迟 | 4.30x / 42% / 250ms | HP 不应污染负重 |
| D12 白盒 | `anchors=18 stairTop=3.0m` | Supermarket 不动 |
| D13 结算 | 1870 | Supermarket 不动 |
| 4A 13 条 | 全绿 | `-s4a*` 走 `legacy4a` 口径 |

**失败处理纪律**：任一既有用例漂移 → 先回滚 4B 改动再定位；不许「顺手改基线让它过」。唯一例外是 `-s4aloop` 的配额断言（设计变更，且日志里能看出是读表）。

### 6.3 反向验证（防假通过，缺一即本轮证据作废）

| # | 注入 | 期望被捕获 |
|---|---|---|
| **B1** | 全局噪音 70、无任何本地声源 | 怪物不进 Chase，`unattributed=0`；若进了 → A9 判 FAIL |
| **B2** | 怪物 Chase 但 `source=none` | `S4B_A9_FAIL`；探针本身无效则本轮 A9 证据作废 |
| **B3** | 倒地后仍试图抓取 | 抓取被拒 `reason=downed`；手持物已脱落 |
| **B4** | 团灭前后对比盘上余额 sha256 | 余额不变；若变 → A8 FAIL |
| **B5** | 双持两件诅咒 | 只有 1 种生效；第 2 种 `dormant=1`；若两种都发笑 → A7 FAIL |
| **B6** | 第 1 关按 12 锚点跑，却报 `legacy4a` | 证据无效：`mode` 必须 `p0leg` |
| **B7** | 配额读表但比例越界 | `S4B_LEG_RATIO_FAIL`；不许手改断言通过 |
| **B8** | 商店在 Night1 购买 | 拒绝 `wrong_scene`；`-s4a*` 除外 |

> 与 `P0_AcceptanceGate.md` §5.2 的 13 条假通过清单合并执行；4B 新增 B1–B8。

### 6.4 一键验收

```
run_regression_4b.bat
  ├ 前置：S4A_NEUTRAL_OK traits=none feel=light
  ├ 编辑器：S4BSceneSetup.Validate（anchors=16 legCounts=12/14/16 monsters=6 curseAnchors=2）
  ├ 构建：BuildWindows -map=sprint4a → Builds/Sprint4B
  ├ 4A 基线段：D4 -feel=light / D6 / D8 / D9 / D12 / D13 / S4A 13 条
  ├ 4B 段：-s4bvitals / -s4bdown / -s4bwipe / -s4bshop8 /
  │        -s4bnoise / -s4bmon / -s4bgreeter / -s4bscream /
  │        -s4bcurse / -s4blegcurse / -s4bleg / -s4blegclient / -s4bperf
  └ 最终 S4B_REGRESSION_OK passes=21 expected=21
```
- `expected` 从 `Tools/regression_4b_cases.txt` 行数读。
- 每条用例独立 `-savedir=`；每条用例前 `taskkill` 清残留；ASCII-only `.bat`（4A 踩过的编码坑）。

---

## 七、风险

| # | 风险 | 影响 | 缓解 | 降级预案 |
|---|---|---|---|---|
| R1 | 玩家 HP 与 D6 硬直互相污染 | 手感与伤害边界混乱 | 硬直不扣 HP；只有怪物/尖啸罐扣 HP；两类日志分开 | 若仍混，砍怪物攻击硬直，只保留伤害+击退 |
| R2 | 怪物在货架迷宫绕柱失败 | 追猎变成笑话 | 锚点巡逻 + 直追 + 避障 + 2s 自动回退锚点 | 若 2 天调不稳 → 怪物只在「主通道 + 仓库」巡逻，不做全图追 |
| R3 | 三关重建顺序错（怪/货/诅咒/噪音） | 「第二关没怪」「旧怪留在安全屋」 | 固定顺序 + 每关 `NoiseBus.Reset()` + `MonsterSpawner.DespawnAll()` | 若仍错，改为每关 `ServerChangeScene` 后 1.5s 统一 `S4BRespawnAll` |
| R4 | 诅咒发笑与怪物听觉叠加过强 | 抱着娃娃必被追，玩家认为无解 | 发笑间隔 12–20s、+8、1.5s 前兆；L2 才上 | 若仍过强，L2 改 18–25s、+6 |
| R5 | 工具带第二槽做不稳 | 商品变假购买 | 先做「交互速度 ×1.25 + 手持小件不计减速」的可断言版本 | 降级必须在 DevLog 与技术债写明，不许说成完整槽 |
| R6 | 4A 兼容闸门写错（`legacy4a` 泄漏到 4B） | 4B 证据全假 | `GATE_PROPS_MODE` 强制校验；4B 用例必须 `p0leg` | 无可降级：口径错了就重跑 |
| R7 | 团灭跨场景重置把账本弄丢 | A8 直接失败 | `ServerEndRun` 先落盘再改内存；盘上 sha256 对比 | 若换关丢账，改为「团灭只结算不换关」，4C 再补 |
| R8 | 8 件商品里消耗品计数与存档不一致 | 买到的针重启后消失 | 消耗品按玩家计数，**购买事务先落盘**；跨进程不保留次数（每关重置） | 若需要跨重启保留，写进 `TeamSaveData`，不做内存计数 |
| R9 | 设计扩张（击杀/三图/11 商品）被顺手做进 4B | 4B 做不完 | 本文 §2.2 的「不做」就是护栏 | 新需求一律进「Post-P0 清单」，不绕过 4B 范围 |

---

## 八、技术债（4B 结束时仍是债，必须如实登记）

| # | 欠账 | 何时还 |
|---|---|---|
| T1 | 怪物不可击杀，无 HP/尸体/武器/伤害公式 | Post-P0（R1 修订后） |
| T2 | 只有一张图；L2/L3 只存在于设计稿 | 4C 或 P1 |
| T3 | 空眼/导购/尖啸罐只有占位胶囊与程序化染色，无模型动画 | 美术阶段 |
| T4 | 怪物无 NavMesh、无门交互、无推车推开的完整物理（只做击退） | P1 |
| T5 | 诅咒无封印，恒 60% 计价 | P1 |
| T6 | 工具带第二槽的最小实现（若降级） | P1 |
| T7 | 体力是客户端本地，未做服务端校验 | P1 |
| T8 | 团灭后从第 1 关重开，未做「本趟结算演出」 | P2 |
| T9 | 三关同图重复感（P0 已知 R4） | P1 扩图 |
| T10 | 正式 UI/音频未做；噪音/倒地/诅咒 HUD 仍是 IMGUI | 美术/UI 阶段 |
| T11 | 身份/缺点牌仍关闭，T19 断言未补 | 恢复角色系统前必还 |
| T12 | `Night1` 商店终端仍在场景里，只靠 `wrong_scene` 拒绝 | 4B 收口时移除或迁到 Safehouse |
| T13 | 怪物感知只做视线 + 噪音，无「声音穿门/遮挡」 | P1 |
| T14 | `-s4bleg` 的机器人时长不能当 A10 节奏证据 | A11 真人 |

---

## 九、待用户确认（不影响开工，只影响数值与范围）

| # | 事项 | 默认 | 代价 |
|---|---|---|---|
| 1 | 4B 不接击杀经济（R1） | 接受 | 若要做，需先修订 P0 §七-12 与章程 17.20，并单独立项 |
| 2 | 过关额 1800/2400/2800（R3） | 接受 | 若改 2400/2600/3000，必须同时建 L1/L2/L3 三张新图 |
| 3 | 只用 Night1 一张图（R2） | 接受 | 三张新图推迟到 4C/P1 |
| 4 | 工具带第二槽的最小实现（R5） | 先做可断言版 | 完整工具槽系统留 P1 |

---

## 附录 A · 日志码、字段契约与开关

**日志前缀**：`[S4B]`；新码 `S4B_*`，门码 `GATE_*`，与 4A 风格一致。

**新增 `S4B_*` 码**：
`S4B_VITALS_OK` / `S4B_DOWN_OK` / `S4B_WIPE_OK` / `S4B_NOISE_OK` / `S4B_MON_OK` / `S4B_GREETER_OK` / `S4B_SCREAM_OK` / `S4B_CURSE_OK` / `S4B_SHOP8_OK` / `S4B_LEG_OK` / `S4B_PERF_OK` / `S4B_BELT_OK` / `S4B_STAMINA_OK` / `S4B_VALIDATE_OK` / `S4B_REGRESSION_OK` / `S4B_A9_FAIL` / `S4B_TEST_ABORT`

**新增 `GATE_*` 码（字段名不许改）**：

| 用途 | 行格式 | 判据 |
|---|---|---|
| 伤害 | `GATE_DAMAGE target=<netId> amount=<n> hpBefore=<n> hpAfter=<n> source=<monster>` | `hpAfter>=1`（HP≥15 时）；`hpAfter==0` 才允许倒地 |
| 倒地/起身 | `GATE_REVIVE mode=(teammate\|self) target= reviver= seconds= hpAfter= free=<bool>` | 队友 4s / 自救 8s；起身 50 |
| 救援拒绝 | `GATE_REVIVE_REJECT target= reason=(interrupted\|no_target\|self)` | 不静默失败 |
| 团灭 | `GATE_WIPE legBefore= legAfter= balanceBefore= balanceAfter= cargoDestroyed=` | `balanceAfter==balanceBefore`；`legAfter==1` |
| 怪物听觉 | `GATE_MON_HEARD monster= source= at= band= level= dist= attenuation=` | `attenuation>=0.2` 才触发 |
| 怪物追猎 | `GATE_MON_CHASE monster= state=Chase source= at= band= level= dist=` | `source!=none` |
| 追逐预算 | `GATE_MON_BUDGET denied=<id> active=<n> limit=<n>` | `active<=limit`；单/双人 `limit=1`，3–4 人 `limit=2` |
| 怪物丢失 | `GATE_MON_LOST monster= seconds= searched=(true\|false)` | `seconds<=6` |
| 怪物卡住 | `GATE_MON_UNSTICK monster= duration= where= rescued=(self\|fail)` | 不出现 `fail` |
| 诅咒发笑 | `GATE_CURSE_LAUGH item= holder= interval= noise= active=<bool> dormant=<bool>` | 间隔 12–20s、`noise=8` |
| 诅咒放下 | `GATE_CURSE_HOLD item= itemNeed= actual= ok=<bool>` | 牌位 `itemNeed=1.5` |
| 诅咒估值 | `GATE_CURSE_VALUE item= base= actual= ratio=0.60` | `actual==floor(base*0.6)` |
| 三关 | `GATE_LEVEL n= quota= source= anchors= totalValue= ratio=` | `source=table`；比例 50–60% |
| 生成口径 | `GATE_PROPS_MODE mode=(legacy4a\|p0leg) leg= anchors= totalValue=` | 4B 证据必须 `p0leg` |
| 商店 8 | `GATE_BUY itemId= price= balanceAfter= purchaseId=` + 逐件断言 | 每件效果都可断言 |
| 性能 | `S4B_PERF_OK fps= kbps= props= carts= monsters=` | `fps>=60 && kbps<=600` |

**命令行开关**：
`-s4bvitals` / `-s4bdown` / `-s4bwipe` / `-s4bnoise` / `-s4bmon` / `-s4bgreeter` / `-s4bscream` / `-s4bcurse` / `-s4bshop8` / `-s4blegcurse` / `-s4bleg` / `-s4blegclient` / `-s4bperf` / `-p0props` / `-leg=<n>` / `-players=<n>` / `-savedir=<abs>` / `-feel=light`

---

## 附录 B · 交付物清单

| 类型 | 交付物 |
|---|---|
| 场景 | `Night1.unity`（幂等追加怪物/诅咒锚点与 legsMask）；`Safehouse.unity` 只加重生逻辑 |
| Prefab | 3 个怪物占位体、2 个诅咒物、Player.prefab 追加 `PlayerVitals/BeltSlot/PlayerStamina` |
| 数据 | 3 × `MonsterProfile.asset`、2 × 诅咒 `PropPhysicsProfile.asset`、`LegContentTable.asset`、4 × 新 `ShopItem.asset` |
| 代码 | §3.2 的 30 个新文件 + §3.3 的既有脚本改动 |
| 编辑器 | `S4BSceneSetup`（Setup/Validate）、`BuildWindows -map=sprint4a` |
| 脚本 | `Tools/run_regression_4b.bat`、`Tools/regression_4b_cases.txt` |
| 文档 | 本文件、`Docs/Sprint_4B_DevLog.md`、`Docs/Networking.md` 4B 追加节 |
| 构建 | `Builds/Sprint4B/`（可双开） |
