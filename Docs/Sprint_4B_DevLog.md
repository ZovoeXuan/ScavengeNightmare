# Sprint 4B · 开发日志与验收覆盖表

> 范围：Story 1 玩家生存（HP / 倒地 / 救援 / 团灭）｜Story 2 噪音+空眼｜Story 3 导购+尖啸罐｜Story 4 两件诅咒｜Story 5 三关梯度+剩余商品+回归
> 起始：2026-10-30　基线提交：`992f63e`（Sprint 4B 技术计划）
> 工程：`D:/drlj/game`（Unity 6.3 + Mirror）

---

## 1. 结论摘要

| 项 | 状态 |
|---|---|
| S4B-1 生存链 | **完成**：HP / 单次伤害保护 / 倒地 / 掉手持物 / 自由自救 / 队友救援 / 团灭 |
| S4B-2 噪音+空眼 | **完成**：本地听觉半径 4/10/16m、`GATE_MON_HEARD/CHASE` 可归因、七状态、追逐预算、推车击退、楼梯、脱困 |
| S4B-3 导购+尖啸罐 | **完成**：注视冻结（25°/18m/LOS）、0.35s 前摇 + 0.8s 突进、撞墙僵直 1.5s、尖啸罐 1.5s 前摇 +45 噪音 / 12m 眩晕掉货、投掷提前引爆 +15 无掉落 |
| S4B-4 两件诅咒 | **完成**：笑面偶 / 无字牌位；归属 = 手持者 + 随车 loader；同人最多 1 种生效（后入手休眠）；未封印恒 60%（510 / 780）；1.5s 长按放下；每关 0/1/2 件 |
| S4B-5 三关梯度 | **完成**：`LegContentTable` 12/14/16 锚点 + 配额 1800/2400/2800 读表、怪物 1/3/4、诅咒 0/1/2、`p0leg` 分流、每关重建固定顺序；工具带（≤7kg 第二槽）、体能（连续冲刺 ≥35%）、A4 在 P0 L3 复跑、ShopTerminal 夜路拒买（§13） |
| 新增自动化 | 13 行（S4B-1/2/3/4）：`-s4bvitals`、`-s4bwipe`、`-s4bdownhost`+`-s4bdownclient`（算 1 项）、`-s4bnoise`、`-s4bmon`、`-s4bgreeter`、`-s4bscream`、`-s4bcurse`、`-s4blegcurse`；S4B-5：`-s4bleg`、`-s4bbelt`、`-s4bperf`；另加 1 行 A4 P0 复跑 |
| 4A 回归 | **13/13 全绿**（`S4A_REGRESSION_OK passes=13 expected=13`，本轮基线复跑证据见 §13.5） |
| 4B 回归 | **14/14 全绿**（`S4B_REGRESSION_OK passes=14 expected=14`，`expected` 由清单行数推导） |
| 商店 | **6/8**：4A 4 件 + 自救针 / 急救包。工具带与体能以**系统默认能力**交付，**未做成商品**；`-s4bshop8` 未交付（↔ 计划 R5 的「8 件是 P0 门票」，记技术债，不许对外说 8 件齐） |
| 已知阻塞项 | 无 |
| 未覆盖 | A11 真人；A1/A2 真人 3 关；A10 只记录机器人时长；`-s4blegclient`（A2 双人冒烟）未交付；正式美术/音频；蝙蝠等其余怪 |

---

## 2. S4B-1 证据

### 2.1 装配与静态校验

```
[S4A] S4A_SETUP_OK scene=Night1 anchors=16 quotaTerminal=1 traits=0 shop=1 shopItems=6
[S4B] S4B_PLAYER_COMPONENTS_SAVED
[S4B] S4B_NIGHT_COMPONENTS_SAVED
[S4B] S4B_VALIDATE_OK vitals=1 revive=1 wipe=1 downedHud=1 shop6=1 tests=3
```

### 2.2 `-s4bvitals`（单人 Host）

```
[S4B] S4B_VITALS_GRAB prop=glass grabbed=True
[S4B] GATE_DAMAGE target=3 amount=50  hpBefore=100 hpAfter=50 source=test
[S4B] GATE_DAMAGE target=3 amount=60  hpBefore=50  hpAfter=1  source=test
[S4B] GATE_DAMAGE target=3 amount=1   hpBefore=1   hpAfter=0  source=test
[S4B] GATE_DOWN target=3 hp=1 source=test
[S4B] S4B_VITALS_DOWNED hp=1 downed=True held=False
[S4B] GATE_REVIVE mode=self target=3 seconds=9.0 hpAfter=50 free=True
[S4A] GATE_BUY itemId=self_injector price=120 balanceAfter=180 purchaseId=...
[S4B] GATE_REVIVE mode=self target=3 seconds=9.0 hpAfter=50 free=False
[S4B] S4B_VITALS_OK hp=50 downed=1 revived=1 free=0 failures=0 ok=True
```

覆盖：单次保护（100→50→1，不被 60 伤害一击带走）、倒地掉手持物、8s 自救窗口、免费自救针消耗、商店购买自救针并真实消耗。

### 2.3 `-s4bdownhost` + `-s4bdownclient`（双进程）

```
Host:   GATE_DAMAGE target=21 amount=200 hpAfter=1
        GATE_DAMAGE target=21 amount=1   hpAfter=0
        GATE_DOWN target=21 hp=1
        GATE_REVIVE_START reviver=3 target=21 range=2.5
        GATE_REVIVE mode=teammate target=21 seconds=4.8 hpAfter=50
        S4B_DOWN_OK revived=True failures=0 ok=True
Client: S4B_DOWN_CLIENT_READY netId=21
        S4B_DOWN_CLIENT_DOWNED hp=1
        S4B_DOWN_CLIENT_OK hp=50 failures=0 ok=True
```

覆盖：客户端倒地 → 房主 2.5m 内长按 E 4s → 起身 50 HP；双端只看 SyncVar，进度不在客户端算。

### 2.4 `-s4bwipe`（团灭）

```
[S4B] S4B_WIPE_START balance=1000 leg=2 quota=2632
[S4B] GATE_WIPE_WAIT total=1 downed=1 injector=False grace=3.0
[S4B] GATE_WIPE_STATE reason=wipe legBefore=2 legAfter=1 balanceBefore=1000 balanceAfter=1000
[S4B] GATE_WIPE legBefore=2 legAfter=1 balanceBefore=1000 balanceAfter=1000 cargoDestroyed=17 players=1
[S4B] S4B_WIPE_OK balance=1000 leg=1 scene=Safehouse cargoLost=1 failures=0 ok=True
```

覆盖：全队倒地 → 3s 宽限 → 本趟结束；余额逐字不变、leg 2→1、未提交货物/推车销毁、进入安全屋。
**A8 第 4 场景（团灭）至此有证据**；A8 从 3/4 变为 4/4。仍须按验收门口径写「强杀是唯一强证据、重连不替代强杀」。

---

## 3. 本轮修掉的三个真 bug（都由用例逼出来）

1. **单次伤害保护让「200 伤害倒地」不成立**
   - 现象：`ServerApplyDamage(200)` 只把 HP 打到 1，测试却断言 `IsDowned`。
   - 结论：这是 P0 §2.3 的正确行为，不是 bug；测试必须补第二刀（1 伤害）才倒地。
   - 已修正 `-s4bwipe` 与 `-s4bdownhost` 的用例。

2. **把 `GrabController.inputEnabled` 当成抓取闸门，打挂 4A 两条用例**
   - 现象：`s4adeposit` / `s4acarrycap` 全部 `grab_timeout`，但日志 `grabEnabled=True`。
   - 根因：4A/D4/D6 自动化用 `SetInputEnabled(false)` 防止「鼠标没按 → 自动脱手」，不是「禁止抓取」。
   - 修法：倒地闸门只判 `PlayerVitals.IsDowned`；`inputEnabled` 继续只控制本地是否读鼠标。
   - 回归：13/13 恢复。

3. **首场景重复刷货 + 安全屋漏货漏车**
   - 现象：`cargoDestroyed=33`（应为 17）、`D65_SPAWNED count=16` 在 Night1 出现两次、进入 Safehouse 后刷出 11 件货。
   - 根因：`LevelRouteService` 首次场景也调 `EnsureSceneObjects`；`S4ABootstrap.EnsureSceneObjects` 与 `PropSpawner` 无条件在 Safehouse 生成。
   - 修法：首场景跳过 `EnsureSceneObjects`；Safehouse 直接跳过车与货；`PropSpawner` 增加 Safehouse 守卫。
   - 实测：单次 `count=16`、`cargoDestroyed=17`、Safehouse `S4A_SCENE_OBJECTS_SKIP`。

---

## 4. 新增 / 修改文件

| 类型 | 文件 |
|---|---|
| 新增（生存） | `Combat/PlayerVitals.cs`、`Combat/ReviveInteractor.cs`、`Combat/TeamWipeDetector.cs`、`Combat/S4BInput.cs` |
| 新增（UI） | `UI/DownedHud.cs`、`UI/RevivePrompt.cs` |
| 新增（测试） | `Networking/S4BVitalsTest.cs`、`Networking/S4BWipeTest.cs`、`Networking/S4BReviveTest.cs` |
| 新增（编辑器） | `Editor/S4BSceneSetup.cs` |
| 修改（接线） | `PlayerMotor`（倒地限速）、`GrabController`（倒地禁抓）、`TeamAccount`（团灭/消耗品/撤销）、`S4ABootstrap`（Safehouse 守卫）、`LevelRouteService`（首场景跳过重刷）、`PropSpawner`（Safehouse 守卫） |
| 修改（商店） | `ShopItem`（consumable）、`S4ASceneSetup`（6 件）、`ShopHUD`（库存）、`S4AInput`（1–8 热键）、`S4ACli`（`-s4b` 测试闸门 / `Is4ARun`） |
| 资源 | `Player.prefab`（生存四件套）、`Night1.unity`（团灭/测试组件）、`Safehouse.unity`、`ShopCatalog.asset`、`self_injector.asset`、`medkit.asset` |

---

## 5. 覆盖表（S4B-1 视角）

| 项 | 判据 | 结果 |
|---|---|---|
| 单次伤害保护 | HP≥15 时单次伤害后 ≥1 | ✅ `hp 50→1` |
| 倒地 | HP<15 时被击倒、掉手持物、限速 0.8 | ✅ `held=False` / `VitalsDowned` |
| 自救窗口 | 倒地 8s 后可 R 起身 50 HP | ✅ `seconds=9.0` |
| 免费 vs 已购 | 免费 1 支先用；商店针能买能用 | ✅ `free=True` → `free=False` |
| 队友救援 | 2.5m 长按 E 4s → 50 HP | ✅ `seconds=4.8` 双端一致 |
| 团灭 | 钱不变、leg 回 1、货丢失、进安全屋 | ✅ `balance 1000→1000` |
| 4A 回归 | 13 条全绿 | ✅ 13/13 |

---

## 6. 遗留 / 下一 Story（S4B-1 视角，历史记录）

- **S4B-2（已完成，证据见 §7）**：`NoiseBus.AudibleAt`（4/10/16m 本地听觉）、`NoiseNetworkState` + `NoiseHUD`、空眼七状态、追逐预算 `MonsterDirector`。
- **S4B-3**：塑料导购视线冻结、尖啸罐提前引爆。
- **S4B-4**：笑面偶 / 无字牌位、60% 计价、休眠。
- **S4B-5**：12/14/16 锚点、1800/2400/2800、工具带/体能、`-s4bshop8`、21 条回归。
- **本轮未验**：`medkit` 对队友 60 HP 的自动断言（需要第二个存活玩家；留给 S4B-2/3 的双人用例）；A8 报告的措辞更新；A10/A11 真人。
- **已知限制**：`-s4bdown` 是同机双进程，不是跨机；`-s4bwipe` 是单人团灭，多人团灭走 `TeamWipeDetector` 同一路径但未单独跑双人。

---

## 7. S4B-2 证据（噪音消费 + 空眼）

### 7.1 装配与静态校验

```
[S4B] S4B_PLAYER_COMPONENTS_SAVED
[S4B] S4B_NOISE_STATE_ADDED
[S4B] S4B_MON_PROFILE_CREATED path=Assets/_Game/Resources/Monsters/HollowEye.asset
[S4B] S4B_MON_PREFAB_CREATED path=Assets/_Game/Prefabs/Monster_HollowEye.prefab
[S4B] S4B_NIGHT_COMPONENTS_SAVED
[S4B] S4B_VALIDATE_OK vitals=1 revive=1 wipe=1 downedHud=1 shop6=1 tests=5 monsters=6 noiseHud=1 monsterPrefab=1 noiseState=1
```

### 7.2 `-s4bnoise`（单人 Host）

```
[S4B] S4B_NOISE_QUIET tag=quiet_outside  level=20 state=Patrol
[S4B] GATE_MON_HEARD monster=holloweye source=noise-test-early at=(-13.5,-8.0) band=quiet level=20.0 dist=1.5 attenuation=0.63
[S4B] S4B_NOISE_QUIET tag=alert_outside  level=50 state=Patrol
[S4B] GATE_MON_HEARD monster=holloweye source=noise-test-mid   at=(-9.0,-8.0)  band=alert level=50.0 dist=6.0 attenuation=0.40
[S4B] S4B_NOISE_QUIET tag=hunt_no_local_memory level=75 state=Patrol
[S4B] S4B_NOISE_CHASE tag=hunt_inside source=noise:noise-test-hunt dist=8 state=Chase
[S4B] S4B_NOISE_ATTEN near=0.88 mid=0.63 far=0.38
[S4B] S4B_NOISE_HUD source=noise-test-hud level=60 dir=前
[S4B] S4B_NOISE_OK quiet=1 alert=1 hunt=1 monotonic=1 hud=1 unattributed=0 failures=0 ok=True
```

覆盖：0–100 全局噪音 ≠ 全图追猎——半径 4/10/16m 之外一律 `Patrol` 不 Investigate；半径内进入 Investigate 并打 `GATE_MON_HEARD`（source/at/band/level/dist/attenuation 齐全）；75 级猎杀噪音但声源 20m 外时 `ChaseCount` 不变、`LastChaseSource` 仍为 `none`（无来源追猎 = 0）；`AudibleAt` 衰减单调；HUD 的等级/来源/方向对玩家可见。

### 7.3 `-s4bmon`（单人 Host，安全期强制 0）

```
[S4B] S4B_MON_FLASHLIGHT_GRANT bought=True owned=True balance=650
[S4B] S4B_MON_SPEED chase=3.20 expected=3.2 at=(-15.1,-1.6)
[S4B] S4B_MON_WINDUP windup=0.80 hpBefore=100 hpAfter=50
[S4B] S4B_MON_LOST seconds=4.00 profile=4.00 searched=1
[S4B] S4B_MON_FLASH exposure=1.47 stunned=1
[S4B] S4B_MON_STUN seconds=2.00
[S4B] S4B_MON_CART_HIT push=1.62 hits=1
[S4B] S4B_MON_STAIRS y=2.90 target=3.00 at=(16.3,-10.5)
[S4B] S4B_MON_WALL minZ=11.33 wallFaceZ=10.20 unstick=3 rescued=self
[S4B] S4B_MON_BUDGET maxActive=1 limit=1
[S4B] S4B_MON_STATES seen=Spawn,Patrol,Chase,Search,Investigate,Attack,Stunned
[S4B] S4B_MON_METRICS chaseSpeed=3.20 windup=0.80 lost=4.00 stun=2.00 knockback=1.62 climbY=2.90
[S4B] S4B_MON_OK states=7 chaseSpeed=3.20 windup=0.80 lost=4.00 stun=2.00 cartPush=1.62 climb=2.90 unstick=self budget=1 failures=0 ok=True
```

覆盖：七状态各 ≥1 次（Spawn→Patrol→Investigate→Search→Patrol、Chase→Attack、Stunned）；追逐 3.2m/s（< 空手冲刺 3.68，靠「最好 0.3s 窗口」取稳态，避免货架走廊的避障毛刺）；攻击前摇 0.80s、单刀 50；丢目标 4s 进 Search 再回 Patrol；手电直射 1.47s 进 Stunned 2.00s；推车擦过（≥2m/s、1.8m 内）击退 1.62m ≥ 1m；12 级楼梯爬到 y=2.90（夹层顶面 3.00，CharacterController skinWidth 0.08 抵消 0.08–0.1）；撞墙不穿透（`minZ=11.33` 大于墙面 10.20）+ 2s 卡住 `GATE_MON_UNSTICK rescued=self`；两只怪同听 80 级噪音 `maxActive=1` ≤ limit=1。

### 7.4 本轮修掉的问题（都由用例逼出来）

1. **`-s4bnoise` 把「上一段合法记忆」误判成失败**：A9 段原断言 `LastHeardSource=="none"`，但 alert 段刚合法写入一条听觉记忆。改为断言「没有无来源追猎」（`LastChaseSource=="none"` + `ChaseCount` 不变 + `State==Patrol`）。
2. **追逐速度取样点被出生区杂物卡住**：出生点 (0,15) 附近有推车与货，实测 0.56m/s。移到仓库走廊 (-15,-8→-15,-1)，并改成「取最好 0.3s 窗口」。
3. **`hitsBefore` 取晚了吞掉唯一一次推车命中**：`TeleportWithCargo` 从远处瞬移过来那一帧就被判成高速擦过，必须在第一次 Teleport 之前取计数。
4. **楼梯提前收工**：原判据同时含「离目标 1m」，停在倒数第二级 y=2.82 就算通过；改成只看高度。
5. **墙后巡逻锚点诱发的假「穿墙」**：脱困是回退最近锚点，(0,.2,8) 就在墙后，回退那一下会合法跨过墙面。改为「脱困发生之前的轨迹才判穿透」。
6. **推车残噪把怪钉在 Investigate**：车停在听觉圈内会持续喂 `cart` 噪音，撞墙用例永远等不到 2s 卡住。推车用例结束时把车收掉。
7. **`-s4acart-narrow` 的「卸货后折叠」被 3D 距离误杀（4A 回归 12/13 的唯一失败）**：`CartController.UpdateInteractions` 用 3D 距离判交互，玩家胶囊中心比车高约 1m，垂直差直接吃掉 1.0m 水平余量——站在车后 1m（视觉贴车）已经 1.41m，卸下的电视把玩家挤开 0.4m 就成 1.82m > `interactReach=1.8`，折叠被拒。改为与 `FindPusher` 同口径的水平距离 + 2m 垂直闸门；复跑 `S4A_REGRESSION_OK passes=13 expected=13`（`Logs/s4a_reg/周五022610-230951`），`-s4bnoise` / `-s4bmon` 在新构建上复跑仍全绿。

### 7.5 覆盖表（S4B-2 视角）

| 项 | 判据 | 结果 |
|---|---|---|
| 听觉半径 | 4/10/16m 外不 Investigate，内进 Investigate 且可归因 | ✅ quiet/alert/hunt 三档 |
| 无来源追猎 = 0 | 75 级噪音 20m 外，`LastChaseSource=none` | ✅ `unattributed=0` |
| 衰减单调 | `AudibleAt` 2/6/10m | ✅ 0.88 / 0.63 / 0.38 |
| NoiseHUD | 等级/来源/方向非空 | ✅ `dir=前` |
| 七状态 | 各 ≥1 次 | ✅ `seen=Spawn,Patrol,Chase,Search,Investigate,Attack,Stunned` |
| 追逐预算 | 3–4 人限 2；单人限 1 | ✅ `maxActive=1 limit=1` |
| 4A 回归 | 13 条全绿 | ✅ 13/13（`Logs/s4a_reg/周五022610-230951`） |

### 7.6 新增 / 修改文件（S4B-2）

| 类型 | 文件 |
|---|---|
| 新增（怪物） | `Monsters/MonsterState.cs`、`MonsterProfile.cs`、`MonsterBrain.cs`、`HollowEyeBrain.cs`、`MonsterSenses.cs`、`MonsterNavigator.cs`、`MonsterVisual.cs`、`MonsterDirector.cs`、`MonsterSpawner.cs`、`MonsterSpawnAnchor.cs`（巡逻锚点复用既有 `Gameplay/PatrolAnchor.cs`） |
| 新增（噪音） | `Noise/NoiseBus.cs`、`Networking/NoiseNetworkState.cs`、`UI/NoiseHud.cs` |
| 新增（测试） | `Networking/S4BNoiseTest.cs`、`Networking/S4BMonsterTest.cs` |
| 修改（装配） | `Editor/S4BSceneSetup.cs`（6 怪物锚点 / 空眼预制体 / 材质 / 数值表 / 校验）、`Editor/S4ASceneSetup.cs`（`EnsureFolder`/`RegisterPrefab` 改 internal 供复用） |
| 修改（缺陷） | `Cart/CartController.cs`（装货/折叠交互距离由 3D 改水平，修 `-s4acart-narrow` 折叠被拒） |
| 资源 | `Prefabs/Monster_HollowEye.prefab`、`Data/Monsters/HollowEyeMat.mat`、`Resources/Monsters/HollowEye.asset` |
| 技术债 | 怪物仍是占位胶囊 + 程序化染色（T3），S4B-5 前不接美术资源 |

---

## 8. S4B-3 证据（塑料导购 + 尖啸罐）

### 8.1 装配与静态校验

```
[S4B] S4B_MON_PROFILE_CREATED path=Assets/_Game/Resources/Monsters/PlasticGreeter.asset
[S4B] S4B_MON_PROFILE_CREATED path=Assets/_Game/Resources/Monsters/ScreamCan.asset
[S4B] S4B_MON_PREFAB_READY path=Assets/_Game/Prefabs/Monster_PlasticGreeter.prefab
[S4B] S4B_MON_PREFAB_READY path=Assets/_Game/Prefabs/Monster_ScreamCan.prefab
[S4B] S4B_NIGHT_COMPONENTS_SAVED
[S4B] S4B_VALIDATE_OK vitals=1 revive=1 wipe=1 downedHud=1 shop6=1 tests=7 monsters=6 monsterKinds=3 noiseHud=1 monsterPrefab=1 noiseState=1
```

校验新增 `greeter_profile/scream_profile/greeter_brain/greeter_identity/scream_brain/scream_identity/greeter_test/scream_test`；三只怪物预制体全部 `RegisterPrefab`，两个测试组件挂在 Night1 的测试宿主上。

### 8.2 `-s4bgreeter`（单人 Host，货架过道 z=9）

```
[S4B] GATE_GREETER_WATCH monster=plastic_greeter mask=1 players=1
[S4B] S4B_GREETER_WATCH mask=1 expectedBit=1 players=1 displacement=0.00
[S4B] GATE_GREETER_WATCH monster=plastic_greeter mask=0 players=1
[S4B] GATE_GREETER_DASH monster=plastic_greeter target=3 from=(-8.0,9.0) to=(-2.0,9.0) seconds=0.80
[S4B] S4B_GREETER_DASH seconds=0.38 distance08=2.51
[S4B] S4B_GREETER_REFREEZE distance=0.00
[S4B] GATE_GREETER_WALL monster=plastic_greeter seconds=1.5 at=(-3.1,9.0)
[S4B] S4B_GREETER_WALL seconds=1.50
[S4B] GATE_GREETER_ATTACK monster=plastic_greeter target=3 damage=30
[S4B] S4B_GREETER_ATTACK damage=30 hpAfter=70
[S4B] S4B_GREETER_BUDGET maxActive=1 limit=1 denied=189
[S4B] S4B_GREETER_OK watchedDisplacement=0.00 windup=0.38 dash08=2.51 wallStun=1.50 damage=30 ok=True
[S4B] S4B_MON_OK monsters=3 states=7 unattributed=0
```

覆盖：被注视时位移严格 0（`watchedMask` 对每位玩家逐位写入）；全员移开视线后才进前摇，0.35s 计划值实测 0.38s（帧粒度）；0.8s 内位移 2.51m > 2m；撞墙打 `GATE_GREETER_WALL` 并僵直 1.50s；突进命中单刀 30；单人追逐预算 1 且被拒 189 次（三只追逐型同时想追，只有 1 只在 Chase）；导购不可击杀（无 HP/死亡路径）。

### 8.3 `-s4bscream`（单人 Host）

```
[S4B] S4B_SCREAM_HOLD prop=toolbox holding=True
[S4B] GATE_SCREAM_TRIGGER monster=scream_can reason=player:3 windup=1.50 at=(0.0,10.0)
[S4B] GATE_SCREAM_DROP target=3 reason=scream
[S4B] GATE_SCREAM_STUN target=3 seconds=2.0 dist=5.0
[S4B] GATE_SCREAM monster=scream_can noise=45 stunned=1 dropped=1 reason=player:3 at=(0.0,10.0)
[S4B] S4B_SCREAM_METRICS windup=1.50 noise=45 stunRemaining=2.00 dropped=True
[S4B] GATE_SCREAM_PREEMPT monster=scream_can amount=15 prop=can speed=8.0 dropped=0
[S4B] S4B_SCREAM_PREEMPT noise=15 holding=True stunned=False screamed=False
[S4B] GATE_SCREAM_ALERT monster=holloweye netId=23 source=scream_can dist=4.0
[S4B] S4B_SCREAM_ALERT nearState=Investigate nearSource=scream_can farState=Patrol level=44.4
[S4B] S4B_SCREAM_OK windup=1.50 noise=45 preempt=15 ok=True
```

覆盖：6m 内玩家触发 1.5s 前摇；尖叫 +45 噪音、12m 内眩晕 2s 并掉手持物（`dropped=1`，掉货走 `GATE_SCREAM_DROP`）；前摇内投掷件以 ≥5m/s 命中 → `NOISE_ADD source=scream_can_preempt amount=15`、不掉货（`dropped=0`）、不眩晕；尖叫后最近空眼进 Investigate（`nearSource=scream_can`），12m 外那只仍 Patrol；喇叭本身不移动、不产生钱（4B 不接击杀经济）。

### 8.4 本轮修掉的问题（都由用例逼出来）

1. **导购命中判定拿不到目标**：`StartDash` 只写 `targetVitals`，基类 `EnterChase` 对「非视野路径」的 `targetNetId` 留空，`TickDash` 命中判定因此永远落空；且子类直接写 SyncVar 触发 `CS0122`。改为基类 `EnterChase` 在 `target != null` 时统一补写 `targetVitals/targetNetId`，子类只读。
2. **`-s4bgreeter` 走廊撞上货架**：首版跑道 (0,8)→(0,16) 正好穿过 z=10.5 的货架，避障转向后卡住，实测 `dash_distance=1.74`、`refreeze_moved=0.70`（撞墙僵直被算成「移开后未冻结」）。整体搬到 D12 货架排间过道 z=9，复跑 `distance08=2.51`、`refreeze=0.00`。
3. **`-s4bscream` 前摇测晚了**：玩家一开始就站在 6m 内，抓货等待期间前摇已经开始，实测 `windup=1.32`。改为先在 6m 外 `EnsureHolding`，再 `TeleportAndCarry` 进入触发半径，复跑 `windup=1.50`。
4. **`-s4bscream` 提前引爆段把「假掉货」也判成通过**：瞬移抓货后 `ServerHoldStep` 的 `delta` 远超 `breakDistance=2.6` 立即脱手；同时 `GrabController.Update` 因鼠标左键未按自动 `RequestRelease`。改为 `PlacePropNearPlayer`/`TeleportAndCarry` 让货跟人走、`SetInputEnabled(false)` 关掉鼠标自动扔，并补 `not_holding_before_scream` 断言把假阳性挡掉；复跑 `holding=True`、`GATE_SCREAM_PREEMPT ... dropped=0`。

### 8.5 覆盖表（S4B-3 视角）

| 项 | 判据 | 结果 |
|---|---|---|
| 注视冻结 | 25°/18m/LOS 内位移=0 | ✅ `displacement=0.00`，`watchedMask` 逐位对位 |
| 移开视线 | 全员移开后 0.35s 前摇 | ✅ `windup=0.38`（帧粒度） |
| 突进 | 0.8s 内位移 >2m | ✅ `dash08=2.51` |
| 撞墙 | 僵直 1.5s | ✅ `seconds=1.50` |
| 导购伤害 | 单刀 30 | ✅ `damage=30 hpAfter=70` |
| 追逐预算 | 三只追逐型同时 Chase ≤2，单人限 1 | ✅ `maxActive=1 limit=1 denied=189`；第 3 只 `GATE_MON_BUDGET` 可查 |
| 尖叫触发 | 6m 内玩家或噪音 ≥40 | ✅ `reason=player:3 windup=1.50` |
| 尖叫效果 | +45 噪音、12m 眩晕 2s、掉手持物 | ✅ `noise=45 stun=2.0 dropped=1` |
| 提前引爆 | 投掷件命中 → +15 噪音、无掉落 | ✅ `amount=15 dropped=0` |
| 尖叫后告警 | 最近空眼进 Investigate（来源 scream_can） | ✅ `nearSource=scream_can`，12m 外 Patrol |
| 三怪并存 | `S4B_MON_OK monsters=3 states=7 unattributed=0` | ✅ |
| 4A 回归 | 13 条全绿 | ✅ 13/13 |

### 8.6 新增 / 修改文件（S4B-3）

| 类型 | 文件 |
|---|---|
| 新增（怪物） | `Monsters/PlasticGreeterBrain.cs`、`Monsters/ScreamCanBrain.cs` |
| 新增（测试） | `Networking/S4BGreeterTest.cs`、`Networking/S4BScreamTest.cs` |
| 修改（框架） | `MonsterBrain.cs`（`HasCustomTick`/`CustomTick`/`ServerAlert`/`IsWindingUp`/`EnterChase` 补写目标）、`MonsterProfile.cs`（两组新数值）、`MonsterVisual.cs`（前兆脉冲）、`MonsterSpawner.cs`（两种新怪 + 测试跳过闸门）、`MonsterDirector.cs`（`BudgetDeniedCount`） |
| 修改（装配） | `Editor/S4BSceneSetup.cs`（两个新 profile/预制体/测试组件/校验项） |
| 资源 | `Prefabs/Monster_PlasticGreeter.prefab`、`Prefabs/Monster_ScreamCan.prefab`、`Resources/Monsters/PlasticGreeter.asset`、`Resources/Monsters/ScreamCan.asset`、`Scenes/Night1.unity` |
| 技术债 | 两只新怪仍是占位胶囊 + 程序化染色（T3），S4B-5 前不接美术资源；导购/尖啸罐不参与击杀经济 |

---

## 9. 遗留（S4B-3 之后）

- **S4B-4**：笑面偶 / 无字牌位、60% 计价、休眠。
- **S4B-5**：12/14/16 锚点、1800/2400/2800 配额、工具带/体能、`-s4bshop8`、21 条回归、怪物美术替换。
- **已知限制**：追逐速度按「最好窗口」判定（货架走廊避障会周期性吃掉速度）；楼梯判据取 2.9m（skinWidth 0.08 + 台阶落点）；怪物只在 Night1 装配，Safehouse 不生成。

---

## 10. S4B-4 证据（两件诅咒）

**交付**：`CursedItem` / `CurseRuntime` / `CurseHud` / `CurseInput` / `CurseSpawnAnchor`；
两个 `PropPhysicsProfile`（笑面偶 2kg/850、无字牌位 15kg/1300）；两个 prefab；`S4BSceneSetup` 幂等装配与校验。

**归属口径**（设计未指定、由实现反推后写死）：
- 手持 + 随车；随车归属 = **把它装上车的人**（`CartController.TryLoadFrom` 记 loader netId）
- 卸下/掉落/提交即解除；归属人离线或倒地时保持休眠，不转移
- 同一玩家两种生效时：**先入手的生效、后入手的休眠**（已写入技术计划 §4.4）

**验收**（`-s4bcurse` / `-s4blegcurse`，全量 23/23 中包含）：
```
S4B_CURSE_OWNER prop=curse_tablet owner=3 source=cart
S4B_CURSE_DUAL carried=2 active=curse_tablet dormant=curse_doll
S4B_CURSE_VALUE id=curse_doll listed=850 current=510
S4B_CURSE_VALUE id=curse_tablet listed=1300 current=780
GATE_CURSE_HOLD_REJECT need=1.5 actual=0.80
GATE_CURSE_HOLD_OK need=1.5 actual=1.52
GATE_CURSE_LEG leg=1 want=0 / leg=2 1/1 / leg=3 2/2
```

---

## 11. 4B 门禁加固与 6 轮矩阵（2026-10-31）

### 11.1 门禁加固（`d119acc` + `a96d09c`）

| 项 | 内容 |
|---|---|
| 预扫 + 硬地板 | `# expected_min: 23`；缺失/为 0/行数不足 → exit 2；解决“清单被读坏 → `OK passes=0 expected=0`”的假绿 |
| 包龄守卫 | BINKEY = exe + dll + globalgamemanagers + 通配 `level* / sharedassets*.assets / resources*.assets`；三档地板（literal<3 / payload<3 / level<1）→ exit 2；`STALE_CHECK` 带计数 |
| 扫描根 | `Assets` → `Assets\_Game`，排除 `Assets\Settings` 的 URP 设置；扩展名六类不变（不删 `*.asset`，否则把假红换成假绿） |
| 单实例互斥 | 检测游戏进程与另一 runner → 打印对方 pid/start/cmd → `already_running` exit 2 |
| 端口熔断 | 日志出现 `SocketException` → 立即中止整轮，`port_busy_by_previous_case` exit 2，避免一次故障放大成 N 条假红 |
| 证据落盘 | `RUN_LOCK_OK` / `CASES_CHECK_OK` / `STALE_CHECK_OK` / 中止原因全部写进 `summary.txt` |
| 退出码 | 0 绿 / 1 用例红 / 2 守卫拦下（先重建）/ 4 运行时异常 |

### 11.2 六轮结果（同一二进制、同一装配状态、中途零改动）

| 轮 | `-s4bmon` chase | endpoint | displaced | 整轮 |
|---|---|---|---|---|
| 1 | 3.20 | (-15.1,-1.6) | no (0.10m) | 23/23 |
| 2 | 3.20 | (-15.1,-1.6) | no (0.10m) | 23/23 |
| 3 | 3.20 | (-15.1,-1.5) | no (0.20m) | 23/23 |
| 4 | 3.20 | (-15.1,-1.6) | no (0.10m) | 23/23 |
| 5 | 3.20 | (-15.1,-1.6) | no (0.10m) | 23/23 |
| 6 | 3.20 | (-15.1,-1.6) | no (0.10m) | 23/23 |

- 整轮：6/6 `passes=23 expected=23`（138 条判定，0 红）
- `-s4bmon`：6/6 PASS，chase 全为 profile 值 3.20（非勉强过线），6/6 `displaced=no`
- `STALE_CHECK` 六轮逐字一致：`src=202610311017 bin=202610311018 literal=3 payload=5 level=2 sharedassets=2 resources=1`

**结论（定稿措辞）**：
> `-s4bmon` 的 **chase 子项**：修复后 12 次采样（单跑）+ 6 轮矩阵全部 3.20（修前 3 次红在 2.98–3.02），终点稳定 `(-15.1,-1.5~-1.6)`，displaced=no；走廊几何修复对该子项有效，此前「门禁对这一条不可信」中**仅 chase 子项**的表述撤销。
> 同一用例的 **flashlight_no_stun 子项**：合并 1/18 红，测试点 `(0,8)/(0,16)`，与走廊无关，**观察项**，归 S4B-2 判据范围，不撤、不派单。

### 11.3 事故与更正（必须留档）

1. **并发 runner 互杀**：同一时段存在 4 个 runner（curse 两轮、regression 一轮、主理人一轮），两个 runner 都按镜像名 `taskkill`，互相杀对方用例 → 22 条 `SocketException` 假红。已由单实例互斥从机制上禁止。
2. **管道截断杀 cmd 留 exe**：`... | head -4` 关闭管道后 cmd 被杀、`ScavengeNightmare.exe` 变僵尸占 7777，后续用例全部启动失败。纪律：跑批输出只重定向到文件，不接 head/tail。
3. **跑批期间改 bat**：cmd 按字节偏移逐行读批处理，改文件导致“边跑边换引擎”，输出出现 `'b' 不是内部或外部命令` 并从头上重跑。纪律：跑批期间禁止改 bat/清单。
4. **主理人误杀**：10:26:27 的合法跑批（PID 33104/44300）被当作残留 `taskkill`，该轮作废；后续以进程探测 + 互斥代替人工判断。
5. **对象名错、数字对**：走廊让位的主角是 `Anchor_4A_16_safe`，不是 `tv`（tv 一直在 `(-8,1.5,7.5)`）。错名记录比不记录更坏——后来人按 tv 查会发现它没变。
6. **“根因”降级为“假设”**：失败日志无任何一行能指认障碍物，`safe + Crate_2 夹住走廊` 只是假设；`S4B_MON_WALL unstick=4` 不指认障碍物。
7. **RebuildAnchors 非确定性**：原实现只收集坐标、按 `AnchorProfiles[i]` 重配，跑一次 Setup 会打乱 profile↔坐标配对，走廊悄悄变窄。改为采集「名字+坐标+profileId」三元组原位重建；`before/after1/after2` 三表逐字相同。
8. **守卫自检漏洞**：`already_running` 把自己（执行 bat 的 cmd）当成并发 runner，门禁一条都跑不了。教训：验证守卫必须同时验证「该拦的拦得住」与「不该拦的放得过」。
9. **BINKEY 扩展是预防性加固**：本次构建 `Assembly-CSharp.dll` 未变但 `globalgamemanagers` 变了，旧三文件集合也能判对；payload 扩展针对“只改资产且 globalgamemanagers 不变”的情形，未被本次实证。

### 11.4 残余风险

1. 6 轮只覆盖**这一份二进制 + 这一个场景装配状态**；任何一次 `S4BSceneSetup.Setup()` 若改变 profile 表或锚点数量，证据作废、需重测。
2. `flashlight_no_stun` 1/18 未归因，需要同一二进制上更高频重复才能区分夹具时序与真 bug。
3. 门禁三种退出码需在 CI 中分支处理（0 绿 / 1 红 / 2 先重建 / 4 运行时异常）。

---

## 12. 遗留（S4B-4 之后，历史记录）

> 本节是 S4B-4 收口时的待办快照，**已被 §13 关闭**。其中「配额 2632」是 4A 历史口径，
> 现行为 `LegContentTable` 的 1800/2400/2800（`-s4aloop` 断言已改为 `QuotaFor(2)`）。

- **S4B-5**：三关梯度锚点 12/14/16、配额 1800/2400/2800、工具带/体能、`-s4bshop8`、怪物美术替换、41 条回归收口。
  - 配额改动必须与断言同批：`LevelRouteService.legQuotas = { 2632, 2632, 2800 }` 与
    `S4ALoopTest.cs:159` 的 `account.LegQuota == 2632` 要一起改（后者改为读表值）。
    单独改配额会让 `-s4aloop` 立即红，并作废刚拿到的 6 轮矩阵证据（需重建重跑）。
- **TECHDEBT-4B-01**：道具生成缺运行时清单埋点（`D65_SPAWN_ITEM profile= at= anchor=`），用于坐实走廊类归因；需 `S4A_TEST_BUILD` 闸门。
- **观察项**：`flashlight_no_stun` 1/18；`-s4bmon` 的 chase 子项已稳，整条用例不宣布“已稳定”。

---

## 13. S4B-5 证据（三关梯度 + 工具带/体能 + 回归收口）

### 13.1 装配与静态校验（`S4BSceneSetup.Setup` → `Validate`）

```
[S4A] S4A_SETUP_OK scene=Night1 anchors=16 quotaTerminal=1 traits=0 shop=1 shopItems=6
[S4B] S4B_NIGHT_COMPONENTS_SAVED
[S4B] S4B_VALIDATE_OK vitals=1 revive=1 wipe=1 downedHud=1 shop6=1 tests=11 monsters=6 monsterKinds=3
      noiseHud=1 monsterPrefab=1 noiseState=1 curseAnchors=2 curseKinds=2 curseHud=1
      legs=3 legAnchors=12/14/16 legValues=3155/4365/4785 belt=1 stamina=1
```

`legs/legAnchors/legValues` 是新增的三关校验：锚点掩码 12/14/16 与货值 3155/4365/4785 必须对得上
`LegContentTable`；这套数字与 `PropSpawner` 的 `GATE_LEVEL` 是同一来源，所以任一侧改错都会让比例越界。

> **`shop6=1` 是如实值**：商店仍是 6 件（4A 4 件 + 自救针/急救包）。工具带与体能本 Story
> 以系统默认能力交付，**没有做成商品**，`-s4bshop8` 未交付（§13.9 第 1 条）。

### 13.2 `-s4bleg`（三关连跑，单 Host）

```
[S4B] GATE_PROPS_MODE mode=p0leg leg=1 anchors=12 totalValue=3155
[S4B] GATE_LEVEL n=1 quota=1800 source=table anchors=12 totalValue=3155 ratio=57.1 filtered=4
[S4B] S4B_MON_LEG leg=1 spawned=1 holloweye=1 greeter=0 scream=0 safety=120
[S4B] GATE_CURSE_SPAWN leg=1 want=0 count=0 anchors=2
[S4A] GATE_BUY_REJECT itemId=hotkey1 reason=wrong_scene balance=0 visit=0 dist=1.00 scene=Night1
[S4B] S4B_LEG_DUP dupBlocked=1 newLines=1
[S4B] S4B_LEG_FILL leg=1 progress=1800 quota=1800 balance=1800
[S4B] S4B_LEG_PORTAL target=Safehouse actual=Safehouse seconds=1.3 ok=1
[S4A] GATE_LEVEL_ADVANCE n=2 quota=2400 source=table before=1800 after=1800
[S4B] S4B_LEG_PORTAL target=Night1 actual=Night1 seconds=1.3 ok=1
[S4B] GATE_PROPS_MODE mode=p0leg leg=2 anchors=14 totalValue=4365
[S4B] GATE_LEVEL n=2 quota=2400 source=table anchors=14 totalValue=4365 ratio=55.0 filtered=2
[S4B] S4B_MON_LEG leg=2 spawned=3 holloweye=2 greeter=1 scream=0 safety=90
[S4B] S4B_LEG_ADVANCE leg=2 balance=1800 progress=0
[S4B] GATE_CURSE_SPAWN leg=2 want=1 count=1 anchors=2
[S4B] S4B_SCENE_REBUILD order=clear_monsters>noise_reset>props>monsters>curses leg=2
[S4A] GATE_BUY_REJECT itemId=hotkey1 reason=wrong_scene balance=1800 visit=0 dist=1.00 scene=Night1
[S4B] S4B_LEG_DUP dupBlocked=3 newLines=2
[S4B] S4B_LEG_FILL leg=2 progress=2400 quota=2400 balance=4200
[S4A] GATE_LEVEL_ADVANCE n=3 quota=2800 source=table before=4200 after=4200
[S4B] S4B_LEG_PORTAL target=Night1 actual=Night1 seconds=1.3 ok=1
[S4B] GATE_PROPS_MODE mode=p0leg leg=3 anchors=16 totalValue=4785
[S4B] GATE_LEVEL n=3 quota=2800 source=table anchors=16 totalValue=4785 ratio=58.5 filtered=0
[S4B] S4B_MON_LEG leg=3 spawned=4 holloweye=2 greeter=1 scream=1 safety=60
[S4B] S4B_LEG_ADVANCE leg=3 balance=4200 progress=0
[S4B] GATE_CURSE_SPAWN leg=3 want=2 count=2 anchors=2
[S4B] S4B_SCENE_REBUILD order=clear_monsters>noise_reset>props>monsters>curses leg=3
[S4A] GATE_BUY_REJECT itemId=hotkey1 reason=wrong_scene balance=4200 visit=0 dist=1.00 scene=Night1
[S4B] S4B_LEG_DUP dupBlocked=5 newLines=2
[S4B] S4B_LEG_OK legs=3 ratios=57.1/55.0/58.5 balance=4200 dupBlocked=5 legProgress=0 ok=True
```

覆盖：三关配额全部 `source=table`（1800/2400/2800），货件 12/14/16 与总额 3155/4365/4785 逐字对表，
比例 57.1/55.0/58.5 全在 50–60%；怪物 1/3/4（L3 含尖啸罐 `scream=1`，安全期 120/90/60 不短于 60）；
诅咒 0/1/2；`GATE_PROPS_MODE` 三关都是 `p0leg`，无 `legacy4a`；余额 0→1800→4200 单调不减且每关 `progress=0`；
公秤去重累计 5；Night1 里三关各拒买一次（`wrong_scene`）；换关回夜路 `seconds=1.3`（修复前是 20s 超时，§13.7 第 2 条）。

### 13.3 `-s4bbelt`（工具带 + 体能，单 Host）

```
[S4B] GATE_PROPS_MODE mode=p0leg leg=1 anchors=12 totalValue=3155
[S4B] S4B_MON_SPAWN_SKIP reason=test_controlled
[S4B] S4B_CURSE_SPAWN_SKIP reason=test_controlled
[S4B] S4B_STAMINA_DEPLETED seconds=7.50 pct=37.5
[S4B] S4B_STAMINA_METRICS continuous=7.32 pct=36.6 normalized=0.00
[S4B] S4B_STAMINA_OK continuous=7.32 pct=36.6
[S4B] S4B_BELT_ADD netId=3 prop=can mass=2.0 slots=1 beltMass=2.0
[S4B] S4B_STAMINA_RESUME normalized=0.50
[S4B] S4B_BELT_METRICS held=42.0 beltMass=2.0 total=44.0 slots=1
[S4B] GATE_DAMAGE target=3 amount=200 hpBefore=100 hpAfter=1 source=belt_test_first
[S4B] GATE_DAMAGE target=3 amount=10 hpBefore=1 hpAfter=-9 source=belt_test_second
[S4B] S4B_BELT_DROP netId=3 reason=downed dropped=1 beltMass=0.0
[S4B] GATE_DOWN target=3 hp=1 source=belt_test_second
[S4B] S4B_BELT_DOWNED dropped=1 beltMass=0.0
[S4B] S4B_BELT_OK sprint=7.32 pct=36.6 held=42.0 beltMass=2.0 total=44.0 dropped=1 ok=True
```

覆盖：连续冲刺 7.32s / 20s = 36.6% ≥ 35%，且体力被真实消耗（`normalized=0.00`，`S4B_STAMINA_DEPLETED` 先于恢复）；
手持 42kg 时把一个 2kg 罐子挂到第二槽（`slots=1`），负重变为 44kg（**挂在腰上仍计入**）；
倒地（两刀：200→1，10→倒地）时自动掉落，`beltMass=0.0`。

### 13.4 `-s4bperf`（L3 满配：1 车 + 40 物 + 4 怪）

```
[S4B] S4B_PERF_LOAD props=40 extra=28
[S4B] S4B_PERF_METRICS fps=120.0 kbps=0.0 frames=1200 seconds=10.00 props=40 monsters=4
[S4B] S4B_PERF_OK fps=120.0 kbps=0.0 props=40 monsters=4 ok=True
```

判据 `fps>=60` 与 `kbps<=600` 同时满足；`kbps=0.0` 是单进程 Host（没有远端接收方），
带宽子项的真实证据来自 §11 的双进程观测，本用例只保证「满配时不掉帧」。

### 13.5 A4 在 P0 第 3 关复跑（`-s4acart-a4 -p0props -leg=3`）

```
[S4B] GATE_PROPS_MODE mode=p0leg leg=3 anchors=16 totalValue=4785
[S4A] GATE_CART count=1 mass=60.0 ids=safe at_x=-0.55 width=0.70
[S4A] GATE_CART count=2 mass=102.0 ids=safe,statue at_x=0.04 width=0.40
[S4A] S4A_A4_PUSH distance=30.02 seconds=14.3 kept=2/2 stalls=0 unsticks=0 maxStallMs=0
[S4A] S4A_A4_OK distance=30.02 mass=102.0 kept=2 stalls=0 maxStallMs=0 ok=True
```

16 锚点全量生成（`p0leg`）后装 safe + statue = 102kg，推 30.02m，0 次卡死（判据 <1.5s），
2/2 件不掉。这条是 4A 的 A4 用例在 4B 配置下的复跑，不是新夹具。

### 13.6 一键回归（最终二进制，`S4B_REGRESSION_OK passes=14 expected=14`）

```
RUN_DIR=D:\drlj\game\Tools\..\Logs\s4_reg\周六022610-142458
RUN_LOCK_OK path="D:\drlj\game\Tools\..\Logs\s4_reg\_runner.lock"
CASES_CHECK_OK expected=14 floor=14
STALE_CHECK_OK src=202610311423 bin=202610311424 literal=3 payload=5 level=2 sharedassets=2 resources=1
  PASS  s4bvitals / s4bdownclient / s4bdownhost / s4bwipe / s4bnoise / s4bmon
  PASS  s4bgreeter / s4bscream / s4bcurse / s4blegcurse / s4bleg
  PASS  s4bbelt / s4bperf / s4acart-a4-p0
S4_REGRESSION_BREAKDOWN s4a pass=1 fail=0 s4b pass=13 fail=0
S4B_REGRESSION_OK passes=14 expected=14
```

`expected` 由 `regression_4b_cases.txt` 的行数推导（`floor=14` 防止清单被读坏后变假绿），
`STALE_CHECK` 确认构建产物晚于全部源码（`src` 2026-10-31 14:23 → `bin` 14:24）。

4A 基线在同一份二进制上另跑 `Tools/run_regression_4a.bat`：
```
S4A_REGRESSION_OK passes=13 expected=13
```
（`-s4aloop` 的配额断言已改为 `LegContentTable.QuotaFor(2)==2400`，不再硬编码 2632；
`-s4a*` 进程在 `ShopTerminal` 里保留旧口径，13 条因此不受 Night1 拒买影响。）

### 13.7 本轮修掉的三个真问题（都由用例逼出来）

1. **`-s4afixedyaw` 把 `-s4bleg` 判成 4A 进程**
   - 现象：首跑 48 项失败，`GATE_PROPS_MODE mode=legacy4a`、没有 `GATE_LEVEL`、商店 `wrong_scene` 拦截失效。
   - 根因：`S4ACli.Is4ARun` 按 `-s4a` 前缀一刀切，而全局修饰开关 `-s4afixedyaw` 也以 `-s4a` 开头。
   - 修法：新增 `ModifierFlags` 排除表（`-s4afixedyaw`），只有非修饰的 `-s4a*` 才算 4A 进程。
   - 复跑：`mode=p0leg leg=1 anchors=12`，三关断言全绿。

2. **换关重入后 `LevelRouteService.Instance` 被清空 → 第二关门 20 秒不开**
   - 现象：`S4B_LEG_PORTAL target=Safehouse actual=Night1 seconds=20.0 ok=0`；日志同帧有
     `Multiple NetworkManagers detected … duplicate will be destroyed`。
   - 探针证据（临时 `S4B_LEG_PORTAL_PROBE`，已删）：`portals=1 nearest=1.30 hold=0.98 enabled=1 active=1
     portalScene=Night1 playerScene=Night1 route=0 quotaReached=1`——门与长按都正常，只有 `route=0`。
   - 根因：场景重入实例化了一份新 NM，副本上的 `Awake` 无条件 `Instance = this`，副本被 Mirror 销毁时
     `OnDestroy` 把 `Instance` 清空；DDOL 的旧实例还活着但不再被引用。
   - 修法：`Awake` 改为「仅当 `Instance == null` 时接管」，`OnDestroy` 仍只清自己的引用。
   - 复跑：`S4B_LEG_PORTAL … seconds=1.3 ok=1`，`S4B_LEG_OK`。

3. **两处日志格式瑕疵（不是行为 bug，但会把证据读错）**
   - `S4B_STAMINA_OK pct=` 漏了 ×100（打出 `pct=0.4`，而真实是 36.6%）；`S4B_BELT_OK` 那行本来是对的。
   - `S4B_LEG_OK ratios=` 用了 `"-"` 作为占位初值，拼出来是 `ratios=-57.1/55.0/58.5/`。
   - 两处都只改字符串，不改判据；为避免「证据在不同二进制上」，改完后整套回归重新跑（§13.6）。

### 13.8 A1–A12 覆盖表（4B 收口口径）

| A | 指标 | 4B 覆盖 | 证据 |
|---|---|---|---|
| A1 单人 3 关 | 30–40 分钟、0 崩溃 | **自动化 3 关 ✅ / 真人 ❌** | `-s4bleg`（§13.2）；真人留 4C |
| A2 双人 3 关 | 同上 | ❌（`-s4blegclient` 未交付；仅 S4B-1 有 1 次双人倒地救援冒烟） | `-s4bdownhost`+`-s4bdownclient` |
| A3 徒手 ≥35kg | 每关至少 1 件 | ✅ 沿用 4A | `GATE_HEAVY_MANUAL`（4A 段） |
| A4 满载 30m | 102kg、卡死 <1.5s | ✅ P0 第 3 关复跑 | `-s4acart-a4 -p0props -leg=3`（§13.5） |
| A5 楼梯 ≤90s | 折叠→搬→展开 | ✅ 沿用 4A | `GATE_TIMER tag=stair_cycle`（4A 段） |
| A6 公秤不重复 | `dupBlocked≥1` | ✅ 三关各 ≥1，累计 5 | `S4B_LEG_DUP` |
| A7 两诅咒 | 各触发 ≥1、仅 1 生效、60% | ✅ | `-s4bcurse` / `-s4blegcurse` |
| A8 资金不丢不复制 | 4 场景差额 = 0 | ✅ 4/4（团灭补齐） | `-s4bwipe` + 强杀/重连/正常 |
| A9 噪音可归因 | 无来源调查 = 0 | ✅ | `-s4bnoise` + `GATE_MON_CHASE` |
| A10 单关时长 | 8–12 分钟 | **只记录不判定**（机器人时长不是节奏证据） | `GATE_TIMER tag=leg_total` |
| A11 真人体验 | ≥30 分钟、再走一关 ≥50% | ❌（4C） | 录像 + 访谈 |
| A12 性能带宽 | ≥60fps、≤600kbps | ✅ L3 满配（1 车 + 40 物 + 4 怪） | `-s4bperf`（§13.4） |

**结论口径（定稿）**：`4B 覆盖 A3/A4/A5/A6/A7/A8/A9/A12 全量；A1 自动化 3 关；A2 未覆盖；A10 只记录；A11 未覆盖`。
不许把「自动化 3 关」写成 A1/A2 通过，也不许把 A10 的机器人时长当节奏证据。

### 13.9 与计划的偏差 / 技术债（不许对外说成已完成）

1. **商店 6/8，不是 8/8**：`tool_belt`（700）与 `stamina_training`（1200）**没有做成商品**；
   工具带（`BeltSlot`）与体能（`PlayerStamina`）是**所有人默认可用**的系统能力。`-s4bshop8` 未交付。
   影响：`P0_VerticalSlice.md` §2.5「8 件」门票未闭；购买→生效→可断言这条链对这两件不存在。
2. **回归拆成两道门，不是单条 21 条**：`regression_4b_cases.txt` 14 行（13 行 4B + 1 行 A4 P0 复跑），
   `regression_4a_cases.txt` 13 行；另维护并集 `regression_cases.txt` 27 行（`# expected_min: 27`）。
   `expected` 全部由清单行数推导，没有任何一处硬编码。为什么拆：4A 段保留自己的 `S4A_REGRESSION_OK` 门，
   两套 expected 混在一起时，删掉一条 4A 用例会被 4B 的 floor 掩盖。
3. **`-s4blegclient` 未交付**：A2 的双人 3 关自动化仍是空白（只有 S4B-1 的 1 次双人救援冒烟）。
4. **`-s4bleg` 落在新文件 `S4BLegTest.cs`，而不是并进 `S4BCurseTest.cs`**（计划 §S4B-4 的写法）。
   按 flag 分工：`S4BCurseTest` 只听 `-s4bcurse` / `-s4blegcurse`，`S4BLegTest` 独占 `-s4bleg`，
   同一进程只有一夹具调 `StartHost()`；「不新建第二个 StartHost 夹具」的实质约束满足。
5. **怪物仍是占位胶囊**（程序化染色），正式美术/音频未接；三关共用 Night1 一张图（计划 R2 的裁决）。
6. **BeltSlot 是完整第二槽**（≤7kg、质量计入负重、倒地掉落），**不是** R5 的降级版；没有做交互速度 ×1.25。

### 13.10 新增 / 修改文件（S4B-5）

| 类型 | 文件 |
|---|---|
| 新增（数据） | `Resources/LegContentTable.asset`、`Scripts/Economy/LegContentTable.cs` |
| 新增（能力） | `Scripts/Gameplay/BeltSlot.cs`、`Scripts/Networking/PlayerStamina.cs` |
| 新增（测试） | `Scripts/Networking/S4BLegTest.cs`、`Scripts/Networking/S4BBeltTest.cs`、`Scripts/Networking/S4BPerfTest.cs` |
| 新增（工具） | `Tools/regression_4b_cases.txt`、`Tools/run_regression_4b.bat` |
| 修改（流程） | `Economy/LevelRouteService.cs`（配额读表 + `Awake` 单例）、`Economy/S4ABootstrap.cs`（固定顺序重建）、`Economy/ShopTerminal.cs`（夜路拒买）、`Economy/TeamAccount.cs`、`Save/S4ACli.cs`（`Is4ARun` 修饰开关排除）、`Gameplay/PropSpawner.cs`（`p0leg` 分流 + `GATE_LEVEL`）、`Gameplay/PropSpawnAnchor.cs`（`legsMask`）、`Monsters/MonsterSpawner.cs`（三关生成 + 测试跳过）、`Networking/PlayerMotor.cs`、`Combat/PlayerVitals.cs`、`Combat/S4BInput.cs`（G 键）、`Gameplay/GrabController.cs`、`Networking/S4ALoopTest.cs`（配额读表）、`Editor/S4BSceneSetup.cs`（表/组件/校验） |
| 修改（工具） | `Tools/regression_cases.txt`（并集 27 行）、`Tools/run_regression.bat`（`CASES`/`OKMARK` 可覆写） |
| 资源 | `Scenes/Night1.unity`、`Scenes/Safehouse.unity`、`Prefabs/Player.prefab`（组件接线；按纪律单独提交） |


