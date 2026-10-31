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
| 新增自动化 | 3 条（S4B-1）：`-s4bvitals`、`-s4bwipe`、`-s4bdownhost`+`-s4bdownclient`；3 条（S4B-2）：`-s4bnoise`、`-s4bmon`；2 条（S4B-3）：`-s4bgreeter`、`-s4bscream` |
| 4A 回归 | **13/13 全绿**（`S4A_REGRESSION_OK passes=13 expected=13`） |
| 商店 | 6/8 上架（新增自救针/急救包）；工具带/体能归 S4B-5 |
| 已知阻塞项 | 无 |
| 未覆盖 | 蝙蝠、诅咒（S4B-4）、三关梯度（S4B-5）、A10/A11 真人 |

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

## 12. 遗留（S4B-4 之后）

- **S4B-5**：三关梯度锚点 12/14/16、配额 1800/2400/2800、工具带/体能、`-s4bshop8`、怪物美术替换、41 条回归收口。
  - 配额改动必须与断言同批：`LevelRouteService.legQuotas = { 2632, 2632, 2800 }` 与
    `S4ALoopTest.cs:159` 的 `account.LegQuota == 2632` 要一起改（后者改为读表值）。
    单独改配额会让 `-s4aloop` 立即红，并作废刚拿到的 6 轮矩阵证据（需重建重跑）。
- **TECHDEBT-4B-01**：道具生成缺运行时清单埋点（`D65_SPAWN_ITEM profile= at= anchor=`），用于坐实走廊类归因；需 `S4A_TEST_BUILD` 闸门。
- **观察项**：`flashlight_no_stun` 1/18；`-s4bmon` 的 chase 子项已稳，整条用例不宣布“已稳定”。
