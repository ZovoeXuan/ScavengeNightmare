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
| 新增自动化 | 3 条（S4B-1）：`-s4bvitals`、`-s4bwipe`、`-s4bdownhost`+`-s4bdownclient`；3 条（S4B-2）：`-s4bnoise`、`-s4bmon` |
| 4A 回归 | **13/13 全绿**（`S4A_REGRESSION_OK passes=13 expected=13`） |
| 商店 | 6/8 上架（新增自救针/急救包）；工具带/体能归 S4B-5 |
| 已知阻塞项 | 无 |
| 未覆盖 | 怪物主动伤害由 AI 自己打（S4B-2 已验单刀 50）、塑料导购/蝙蝠、诅咒（S4B-4）、三关梯度（S4B-5）、A10/A11 真人 |

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

## 8. 遗留（S4B-2 之后）

- **S4B-3**：塑料导购视线冻结、尖啸罐提前引爆、双人追逐预算实测。
- **S4B-4**：笑面偶 / 无字牌位、60% 计价、休眠。
- **S4B-5**：12/14/16 锚点、1800/2400/2800 配额、工具带/体能、`-s4bshop8`、21 条回归、怪物美术替换。
- **已知限制**：追逐速度按「最好窗口」判定（货架走廊避障会周期性吃掉速度）；楼梯判据取 2.9m（skinWidth 0.08 + 台阶落点）；怪物只在 Night1 装配，Safehouse 不生成。
