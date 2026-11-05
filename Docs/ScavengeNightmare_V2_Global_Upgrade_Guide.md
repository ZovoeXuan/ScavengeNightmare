# 《拾荒惊魂》(ScavengeNightmare) V2.0 全局代码审查、Bug 修复与玩法升级蓝图

本文档基于对 GitHub 仓库 [ZovoeXuan/ScavengeNightmare](https://github.com/ZovoeXuan/ScavengeNightmare) 下全部 C# 源码（[Assets/_Game/Scripts/](https://github.com/ZovoeXuan/ScavengeNightmare/tree/main/Assets/_Game/Scripts)）与设计文档（[Docs/](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/tree/main/Docs)）的逐行审查编写，涵盖 **26 项关键 Bug 根因与修复方案**、**V2.0 全局玩法升级设计（瞬间-单局-长线三层循环）** 以及 **一键替换落地指南**。

## 1. 核心架构与回归门禁兼容性原则

项目在 Sprint 4A–4C 构建了严密的 CLI 自动化验收体系（[Tools/run\_regression\_4a.bat](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Tools/run_regression_4a.bat) 及 [Assets/_Game/Scripts/Networking/](https://github.com/ZovoeXuan/ScavengeNightmare/tree/main/Assets/_Game/Scripts/Networking) 下的 18 项回归用例）。因此，本次 V2.0 代码重构严格遵循以下工程底线：

- **100% 保持自动化测试契约**：完整保留所有 `[S4A]`、`[S4B]`、`[S4C]`、`[D8]`、`[D9]`、`[D12]`、`[D13]` 与 `GATE_*` 日志签名，以及 `ServerTestWarp`、`ServerForceGrab`、`LoadEventCount`、`FoldRejectCount`、`MassRatioRejectCount`、`SkipNextBalanceHook`、`FlattenForTest` 等测试钩子。
- **服务端权威（Host/Server Authority）闭环**：修复所有客户端与服务端状态脱节、双物理控制源抢夺（`MovePosition` 与 `linearVelocity` 打架）以及 `ClientToServer` 传送失效问题。
- **数据驱动解耦**：补齐硬编码生成逻辑对 [LegContentTable.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Economy/LegContentTable.cs)、[CartProfile.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Cart/CartProfile.cs) 与 [MonsterProfile.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Monsters/MonsterProfile.cs) 的读取闭环。

## 2. 源码级 Bug 诊断与修复全表（共 26 项）

### 2.1 推车物理、抓握与工具带模块（Cart & Gameplay）

| 编号 | 严重度 | 所属脚本 | 原始代码缺陷与复现机制 | V2.0 代码修复方案 |
| :--- | :--- | :--- | :--- | :--- |
| **CART-01** | **Critical** | [CartCargo.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Cart/CartCargo.cs) / [GrabController.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Gameplay/GrabController.cs) | 玩家从推车板上直接抓起已装载货物时，`CartCargo.Tick()` 从未检查 `prop.IsHeld`，导致每帧继续 `body.MovePosition(planar)` 把货物往车板上拽，与手部 `linearVelocity` 剧烈抢夺直至超出 `breakDistance` 强制脱手。 | 在 `CartCargo.Tick()` 中检测 `prop.IsHeld` 自动解绑锚点，并在 `GrabController.ServerGrab` 与 `BeltSlot.ServerBelt` 中调用 `CartController.ServerNotifyPropGrabbed(prop)` 实时更新 `loadKg` 与 `itemCount`。 |
| **CART-02** | **High** | [CartController.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Cart/CartController.cs) | `TryLoadFrom()` 仅将当前车上货物的半宽简单累加求 `cursor`。若取走第 1 件货再放新货，新货计算出的 `lx` 会直接叠在第 2 件货物的旧锚点上，引发刚体穿模把货物弹飞出车外。 | 在 `CartCargo` 中实现 `TryFindOpenDeckPosition()` 区间防重叠搜索算法，若顺序游标与存量货物锚点重叠，自动沿板面搜索首个无碰撞空档。 |
| **CART-03** | **High** | [CartController.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Cart/CartController.cs) | 推车处于推行状态（`pusher != null`）时直接按 `E` 折叠抱起进入 `CartState.Carried`，`ToggleFold()` 未调用 `ReleasePusher()`，且 `FixedUpdate` 在 `Carried` 分支提前 `return`，导致原推行者永久卡在推车限速（`2.0–3.0 m/s`）且 `handsBusy = true` 永远无法抓货。 | 在 `ToggleFold()` 切换至 `Carried` 前强制执行 `ReleasePusher()` 与 `ReleaseCarrier()`，并在 `OnDestroy` 中兜底释放。 |
| **CART-04** | **High** | [CartController.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Cart/CartController.cs) | 抱车玩家（`carrier`）倒地或断线时，`UpdateCarried()` 直接 `return`，推车保持 `isKinematic = true` 且碰撞体关闭悬停在半空；此外队友帮忙展开推车时，`ToggleFold` 解除了交互者的 `handsBusy` 而非原 `carrier` 的 `handsBusy`。 | 独立维护 `ReleaseCarrier()` 精准释放原抱车人双手；当检测到 `carrier == null \|\| carrier.VitalsDowned` 时自动调用 `ForceUnfoldToGround()` 让推车落地恢复物理。 |
| **CART-05** | **Medium** | [CartController.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Cart/CartController.cs) | `Awake()` 中 `deckCollider = GetComponentInChildren<BoxCollider>()` 抓到了与 `hullCollider` 相同的根节点碰撞体，子物体碰撞体在抱车爬楼时仍处于激活状态。 | 缓存 `GetComponentsInChildren<Collider>(true)`，在抱车/展开切换时统一开关全部非 Trigger 实体碰撞体。 |
| **CART-06** | **Medium** | [CartParkingZone.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Cart/CartParkingZone.cs) | `Apply()` 使用 `other.GetComponent<CartController>()`，推车子碰撞体进出触发区时返回 `null`，且未统计重叠碰撞体数量。 | 改用 `GetComponentInParent<CartController>()` 并维护 `HashSet<Collider> activeCartColliders`，仅当全部碰撞体离开时才置 `SetParked(false)`。 |
| **BELT-01** | **High** | [BeltSlot.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Gameplay/BeltSlot.cs) | ① `ServerBeltNearest()` 未过滤 `prop.IsOnCart`，玩家站在推车旁按 `G` 会把车板上的小件偷挂到腰上引发双锚点抢夺；② 未检查 `vitals.IsDowned`，倒地玩家仍可按 `G` 挂货；③ `ServerHoldStep` 限速 `3 m/s` 低于玩家冲刺速度 `5 m/s`，冲刺时腰带货物严重掉队撞墙。 | ① 过滤 `prop.IsOnCart` 并阻止倒地玩家挂货；② 挂载期间关闭货物重力（掉落时恢复），将跟随最大速度提升至 `9.5 m/s`（加速度 `48`）；③ 正式游玩中周围无小件可挂时按 `G` 支持主动卸下腰带物品。 |
| **PROP-01** | **Medium** | [FragileProp.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Gameplay/FragileProp.cs) | `SpawnFragments()` 用 `CreatePrimitive(PrimitiveType.Cube)` 生成的 4 块碎渣默认开启 `BoxCollider`，在满载易碎品的推车内爆开会连锁撞碎相邻花瓶或卡住车轮。 | 生成碎块时立即设置 `shardCol.enabled = false`，保留纯视觉物理抛洒表现。 |
| **PROP-02** | **Medium** | [DoorTrap.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Gameplay/DoorTrap.cs) & [NarrowDoorWatcher.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Gameplay/NarrowDoorWatcher.cs) | `DoorTrap` 将世界坐标 AABB `leafCollider.bounds.extents` 叠加上 `leaf.rotation` 传入 `OverlapBox` 导致 45° 时检测盒膨胀变形；`NarrowDoorWatcher` 在同一物理帧被多碰撞体物件触发多次导致 `moved` 被误判为 0。 | `DoorTrap` 改为基于局部 `leafCollider.size * lossyScale` 计算 OBB 并仅在服务端执行推挤；`NarrowDoorWatcher` 增加 `LastFixedFrame` 单帧去重与 `lossyScale` 缩放补偿。 |

### 2.2 联机同步、生存救援与经济存档模块（Networking, Combat, Economy & Save）

| 编号 | 严重度 | 所属脚本 | 原始代码缺陷与复现机制 | V2.0 代码修复方案 |
| :--- | :--- | :--- | :--- | :--- |
| **ECO-01** | **Critical** | [TeamAccount.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Economy/TeamAccount.cs) | `OnStartServer()` 从 `wallet.sav` 恢复了 `balance` 和 `legIndex`，但**完全遗漏了从 `save.Data.purchases` 恢复 `ownedMask`、`selfInjectorCharges` 和 `medkitCharges`**。重启主机后，买装备扣掉的钱不会回来，但买好的手电筒、手套、工具带全部归零消失！ | 新增 `RestorePurchasesFromSave(TeamSaveData)`，在 `OnStartServer()` 读档时遍历所有 `PurchaseRecord.Active` 记录，精准重建 `ownedMask` 位掩码与消耗品剩余次数。 |
| **ECO-02** | **High** | [QuotaTerminal.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Economy/QuotaTerminal.cs) | ① 玩家按住 `E` 读条到一半走出公秤触发区时，`if (!zone.bounds.Contains(...)) continue;` 直接跳过而未将 `hold[motor]` 清零，下次进区瞬间触发提交；② 倒地玩家未拦截；③ `SubmitCart` 先 `cart.UnloadAll()` 再写盘，若写盘锁死则货物已脱离推车。 | 玩家离开区域或倒地时立即重置 `hold[motor] = 0f`；`SubmitCart` 先校验 `P0SaveService` 可写状态再执行卸车提交。 |
| **ECO-03** | **Medium** | [TeamAccount.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Economy/TeamAccount.cs) | `TryUndoPurchase` 撤销最近一笔购买后，未将 `LastPurchaseId` 回退到更早一笔未撤销的购买记录，导致连续按两次 `U` 时第二次必报 `already_revoked`。 | 每次撤销成功后，自动从当前访问周期的 `save.Data.purchases` 中逆序查找上一笔 `Active` 记录更新 `LastPurchaseId`。 |
| **ECO-04** | **Medium** | [LevelPortal.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Economy/LevelPortal.cs) | `UpdateNight` 与 `UpdateSafehouse` 未检查 `motor.VitalsDowned`，倒地爬行的玩家可以在夜间按 `E` 触发全队撤离。 | 在传送门读条与就绪判定中过滤 `motor.VitalsDowned`。 |
| **NET-01** | **High** | [PlayerEquipment.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Networking/PlayerEquipment.cs) | `Update()` 第一行 `if (!isLocalPlayer) return;` 在 `EnsureSpot()` 之前提前返回。虽然 `lightOn` 是 `[SyncVar]`，但远端队友实例永远不会创建或开启 `Light` 组件，四人局里看队友永远不开灯！ | 将 `EnsureSpot()` 移至 `if (!isLocalPlayer) return;` 之前，并读取 `motor.ViewPitchDeg` 实时同步远端队友手电筒光束的上下俯仰角。 |
| **NET-02** | **High** | [PlayerEquipment.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Networking/PlayerEquipment.cs) & [MonsterSenses.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Monsters/MonsterSenses.cs) | `CmdToggleLight` 未校验手电筒所有权与倒地状态；玩家倒地后手电筒不灭，且 `MonsterSenses.IsBeamLit()` 未过滤 `candidate.IsDowned`，导致倒地玩家躺在地上就能把空眼照晕。 | `CmdToggleLight` 增加服务端校验，玩家进入 `EnterDowned` 时强制调用 `ServerForceLightOff()`，且 `MonsterSenses.IsBeamLit()` 跳过倒地玩家。 |
| **NET-03** | **High** | [LevelRouteService.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Economy/LevelRouteService.cs) & [PlayerMotor.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Networking/PlayerMotor.cs) | `MovePlayersToAnchor()` 在服务端直接调用 `motor.transform.SetPositionAndRotation(...)`，但玩家带有激活的 `CharacterController` 且 `NetworkTransformReliable` 为 `ClientToServer` 模式，远端客户端会立刻用本地旧坐标覆盖服务端坐标。 | 在 `PlayerMotor` 中新增 `ServerTeleport` + `[TargetRpc] TargetTeleport`，在服务端与目标客户端同步临时关闭 `CharacterController`、更新坐标并 `Physics.SyncTransforms()`，同时为 4 名玩家计算防重叠出生偏移。 |
| **NET-04** | **High** | [PlayerTraits.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Traits/PlayerTraits.cs) | `[Command(requiresAuthority = false)] CmdDebugAssign` 无任何权限校验，正式包中任意客户端可篡改全房玩家的角色身份与缺点牌。 | 限制非测试/非编辑器环境下仅允许修改 `sender == connectionToClient` 自身角色。 |
| **NET-05** | **Medium** | [PlayerMotor.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Networking/PlayerMotor.cs) | 手动输入下按住 `LeftShift` 站在原地不动（`moveInput == Vector2.zero`），`sprintHeld` 仍为 `true`，5 秒内把体力条耗光进入力竭。 | 要求 `moveInput.sqrMagnitude > 0.01f` 时 `sprintHeld` 才为 `true`。 |
| **NET-06** | **Medium** | [PlayerMotor.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Networking/PlayerMotor.cs) | `SyncCarryMassFromHands()` 仅对比 `lastSeenHeldMass`，玩家持物或挂腰带站在商店买下 `grip_training`（`CarryBoost`）时不会触发 `SetCarryMass` 重算移速。 | 同时缓存 `lastSeenCarryBoost`，商品生效或撤销瞬间立即重算负重移速。 |
| **NET-07** | **Medium** | [PlayerInteract.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Networking/PlayerInteract.cs) | 消费 `S4AInput.PendingBuyIndex` 与 `UndoPressed` 时未将 `AutoBuyIndex` 和 `AutoUndo` 复位，导致自动化置位期间每帧狂发购买/撤销 Command。 | 消费瞬间立即复位 `S4AInput.AutoBuyIndex = 0` 与 `S4AInput.AutoUndo = false`。 |
| **COM-01** | **High** | [PlayerVitals.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Combat/PlayerVitals.cs) | `EnterDowned()` 在服务端调用 `grab.Release(false)`，而 `Release(false)` 在主机（`NetworkClient.active == true`）上会走 `CmdRequestRelease`，若玩家正拿着「无字牌位」（`RequiredReleaseHold > 0`），会被服务端当作客户端秒扔拦截（`GATE_CURSE_HOLD_REJECT`），导致倒地玩家手里死死粘着牌位！ | 在 `GrabController` 新增 `[Server] public void ServerForceRelease()`，供 `PlayerVitals.EnterDowned`、`CartController`、`QuotaTerminal`、`ScreamCanBrain` 等服务端权威脱手直接调用。 |
| **COM-02** | **Medium** | [ReviveInteractor.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Combat/ReviveInteractor.cs) & [TeamWipeDetector.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Combat/TeamWipeDetector.cs) | 队友 A 正在按 `E` 救人（进度 80%），队友 B 路过 2.5m 内未按 `E` 触发 `ResetTarget` 会把倒地者的 `reviveProgress` 刷回 0；`TeamWipeDetector` 动态重算 `requiredGrace` 导致倒地超过 3s 后若自救针状态变化会瞬间判团灭。 | 在 `PlayerVitals` 引入 `activeReviverNetId` 锁 (`ServerUpdateReviveProgress`)；在 `TeamWipeDetector` 锁定 `latchedGraceSeconds` 并在团灭时重置 `NoiseBus.Reset()`。 |

### 2.3 怪物 AI 与诅咒模块（Monsters & Curses）

| 编号 | 严重度 | 所属脚本 | 原始代码缺陷与复现机制 | V2.0 代码修复方案 |
| :--- | :--- | :--- | :--- | :--- |
| **AI-01** | **High** | [PlasticGreeterBrain.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Monsters/PlasticGreeterBrain.cs) | `IsWatchedBy()` 仅将 `player.transform.forward` 投影到水平面比较夹角，忽略了玩家的上下俯仰角 `ViewPitchDeg`——玩家哪怕低头垂直盯着地板或抬头看天花板，只要水平朝向在 25° 内依然能冻住塑料导购。 | 引入 `PlayerMotor.ViewPitchDeg` 俯仰角校验（俯仰偏离超过 `50°` 时判定为未注视），兼顾平视容错与真实空间视角。 |
| **AI-02** | **Medium** | [MonsterBrain.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Monsters/MonsterBrain.cs) & [MonsterVisual.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Monsters/MonsterVisual.cs) | `PlasticGreeterBrain.windupAt` 与 `ScreamCanBrain.windupEndsAt` 均为服务端私有变量，未做网络同步，导致远端客户端 `brain.IsWindingUp` 恒为 `false`，永远看不到怪物突进/尖叫前摇的 `24 rad/s` 急促闪烁预警。 | 在基类 `MonsterBrain` 新增 `[SyncVar] protected bool syncWindingUp;`，前摇起止时同步给全体客户端。 |
| **AI-03** | **Medium** | [MonsterBrain.cs](https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Monsters/MonsterBrain.cs) | `UpdateCartKnockback` 用 `(cartPosition - lastCartPosition) / deltaTime` 计算推车速度，当推车发生 `TeleportWithCargo` 或脱困瞬移时，单帧位移除以 `deltaTime` 产生数十米每秒的伪速度，若怪物恰在 1.8m 内会误触发 `GATE_MON_CART_HIT`。 | 增加单帧位移阈值校验（`frameDelta.magnitude > 3.0f` 时视为瞬移帧跳过速度计算）。 |
| **CUR-01** | **Medium** | [CursedItem.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Curses/CursedItem.cs) & [CurseRuntime.cs](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Assets/_Game/Scripts/Curses/CurseRuntime.cs) | `CursedItem.IsCarriedByLocalHand()` 仅判断 `self.IsHeld` 而未核对 `holderNetId == NetworkClient.localPlayer.netId`；`CurseRuntime.ServerSpawnForLeg` 硬编码 `0/1/2` 而未调用 `LegContentTable.Instance.CursesFor(leg)`。 | 校验 `holderNetId == localPlayer.netId`；接入 `LegContentTable.Instance.CursesFor(leg)`。 |

## 3. V2.0 全局玩法升级设计（按游戏设计三层循环全面进化）

在修复全部底层 Bug 的基础上，我们对照 [Docs/design/ScavengeNightmare\_Design\_v1.md](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Docs/design/ScavengeNightmare_Design_v1.md) 与 [Docs/design/Monster\_Roster\_v1.md](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Docs/design/Monster_Roster_v1.md) 的完整愿景，为游戏注入了 **6 大全局玩法升级**：

### 3.1 瞬间循环（Moment-to-Moment，0–30秒）：物理协作与推车博弈深化
1. **双人协力抬重物（Co-op Assisted Heavy Lift，集成于 `GrabController.cs` & `GrabbableProp.cs`）**：
   - **痛点**：原版 42kg–70kg 重物（保险箱、雕像、冰箱）单人搬运被死死限制在 `3.135 m/s` 且极易因门框卡顿超过 `breakDistance` 脱手，队友在旁边只能干看着。
   - **升级机制**：当一名玩家抓起 `>= 20kg` 的重物时，任意空手存活队友只需走到货物 `2.4m` 内按住鼠标左键（`GrabHeldServer`），即可进入 **双人协力态（`IsCoopAssisted = true`）**：主搬运者有效负重立减 **45%**，抓握跟随刚度提升 **+35%**，脱手阈值距离扩大 **+45%**，重物搬运限速上限提升至 **3.85 m/s**！
2. **空眼撞车震落货物 vs 打包胶带克制（集成于 `CartCargo.cs` & `MonsterBrain.cs`）**：
   - **痛点**：原版「打包胶带（`packing_tape`）」仅将已经很紧的水平锚点刚度从 `40` 乘以 `2`，玩家感知极弱；空眼攻击推车手时车上货物纹丝不动。
   - **升级机制**：当空眼（`HollowEye`）命中正在推车的玩家且距离推车 `<= 2.6m` 时，会触发 `CartCargo.ServerJoltCargo()` 将车板上最轻的一件货物震飞下车；而一旦团队购买了 **打包胶带（`CartSlipScale >= 1.5f`）**，全车货物获得刚性捆扎，100% 免疫怪物震落！

### 3.2 单局循环（Session Loop，5–20分钟）：新增 3 只机制型怪物 + 诅咒净化祭坛 + 动态节奏导演
1. **新增 3 只高交互机制怪物（补齐 [Monster\_Roster\_v1.md](https://www.google.com/search?q=https://github.com/ZovoeXuan/ScavengeNightmare/blob/main/Docs/design/Monster_Roster_v1.md) 核心生态）**：
   - **`LifterBrain.cs`（搬尸者 / 偷货怪，`lifter`，T2）**：专门盯着玩家已装上推车的货物或地面高价值物资（`>= $120`）。潜行靠近后直接从推车上抱走货物并以 `2.4 m/s` 逃向最远巢穴锚点。**克制手段**：用手电筒照射 `1.0s` 或推车冲撞，逼其当场丢下赃物并进入僵直。
   - **`DoorWormBrain.cs`（窄门兽，`door_worm`，T1）**：自动寻找并盘踞在 `NarrowDoorWatcher` 窄门洞处，膨胀肉体堵死 `1.2m` 门洞并挤压擦伤强行闯关的玩家。**克制手段**：用手电筒照射 `1.2s` 或推车撞击，窄门兽会畏光收缩至 **35% 体积** 并退让 `8 秒`，让推车顺畅通过。
   - **`NightScaleBrain.cs`（夜秤，`night_scale`，T3）**：在公秤（`QuotaTerminal`）周边或高噪音区游荡，展开 `6.5m`「重力失衡场」，将场内负重玩家的移速压制到 `2.15 m/s`。**克制手段**：手电筒照射 `1.5s` 使其僵直，失衡场立刻消散 `10 秒`。
2. **诅咒净化祭坛（`CursePurifyShrine.cs` + `CursedItem.cs` 封印状态同步）**：
   - **痛点**：原版 `CursedItem.Sealed` 硬编码为 `false`，笑面偶与无字牌位永远只能按 **60% 折价**（$510 / $780）贱卖，缺乏风险博弈。
   - **升级机制**：玩家可手持未封印诅咒物前往场景中的 `CursePurifyShrine` 长按 `E` 键 `2.0s`。净化完成瞬间会爆发一次 **`+35` 噪音脉冲**（高风险：可能引来附近空眼），但诅咒物将永久进入 **`Sealed = true` 已封印态**——彻底停止发笑、手抖与 `1.5s` 粘手惩罚，且交货价值恢复至 **100% 全额标价（$850 / $1,300）**！
3. **动态锯齿难度与撤离高潮导演（`NightPacingDirector.cs`）**：
   - **达标撤离冲刺（Extraction Rush）**：当团队在公秤首次交够当晚定额（`QuotaReached == true`）解锁撤离传送门瞬间，触发一次声波震荡（`+18` 噪音）唤醒附近怪物向撤离路线施压，同时立刻重置并注满全体存活玩家的冲刺体力（肾上腺素爆发），塑造高潮迭起的「最后百米撤离战」。
   - **孤狼残局缓冲（Clutch Cushion）**：多人局中当仅剩 1 名玩家存活、其余队友全部倒地时，导演自动开启负反馈保护，使全局噪音额外加速衰减（`+2.0/s`），为孤狼玩家潜行拉起队友创造翻盘窗口。

### 3.3 长线与备战循环（Meta & Prep Loop）：安全屋角色与缺点牌终端
- **安全屋角色换装与缺点牌重抽终端（`SafehouseTraitTerminal.cs`）**：
  - 将 D8 原型中的 **4 大角色身份（大力士、近视眼、瘸子、灵敏）× 5 张缺点补偿牌（手抖、看不清小件、腿软、贪财、窄门恐惧）** 正式接入安全屋备战区！玩家在安全屋终端旁按 `C` 键可自由切换角色身份，按 `V` 键可重新抽取缺点补偿牌（在 CLI 自动化测试下自动静默，确保测试基线恒定）。

## 4. 一键升级与 Unity 编辑器挂载指南

### 4.1 运行一键升级脚本
将生成的 `apply_scavenge_nightmare_v2_upgrade.py` 放到你的本地 `ScavengeNightmare` 仓库根目录（与 `Assets/` 同级），在终端执行：

```bash
python apply_scavenge_nightmare_v2_upgrade.py
```

脚本会自动将原文件备份至 `.backup_v1/<时间戳>/`，并写入全部 **16 个修复版核心脚本** 与 **6 个新增 V2 玩法脚本**。

### 4.2 新增玩法组件在 Unity 场景中的挂载方式（可选扩展）

| 新增组件脚本 | 挂载位置建议 | 关键 Inspector 参数说明 |
| :--- | :--- | :--- |
| `NightPacingDirector.cs` | 挂载在 `S4AGameRoot` 预制体或 `Night1` 场景的带 `NetworkIdentity` 节点上 | 自动监听 `TeamAccount.QuotaReached` 与 `PlayerVitals.All`，无需手动连线 |
| `CursePurifyShrine.cs` | 挂载在 `Night1` 场景新增的祭坛物体上（需带 `NetworkIdentity`） | `interactRadius = 2.2f`（交互半径）、`purifyHoldSeconds = 2.0f`（长按 E 秒数）、`purifyNoiseBurst = 35f` |
| `SafehouseTraitTerminal.cs` | 挂载在 `Safehouse` 场景的备战储物柜物体上（需带 `NetworkIdentity`） | `interactRadius = 2.5f`（靠近按 `C` 切角色身份、按 `V` 重抽缺点补偿牌） |
| `LifterBrain.cs` / `DoorWormBrain.cs` / `NightScaleBrain.cs` | 复制现有 `Monster_HollowEye` 预制体，将 `HollowEyeBrain` 替换为对应子类脚本并注册到 `NetworkManager.spawnPrefabs` | 自动继承 `MonsterBrain`、`MonsterNavigator`、`MonsterSenses` 与 `MonsterVisual` 全部链路 |
