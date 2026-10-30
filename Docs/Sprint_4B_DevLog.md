# Sprint 4B · 开发日志与验收覆盖表

> 范围：Story 1 玩家生存（HP / 倒地 / 救援 / 团灭）｜Story 2 噪音+空眼｜Story 3 导购+尖啸罐｜Story 4 两件诅咒｜Story 5 三关梯度+剩余商品+回归
> 起始：2026-10-30　基线提交：`992f63e`（Sprint 4B 技术计划）
> 工程：`D:/drlj/game`（Unity 6.3 + Mirror）

---

## 1. 结论摘要

| 项 | 状态 |
|---|---|
| S4B-1 生存链 | **完成**：HP / 单次伤害保护 / 倒地 / 掉手持物 / 自由自救 / 队友救援 / 团灭 |
| 新增自动化 | 3 条：`-s4bvitals`、`-s4bwipe`、`-s4bdownhost`+`-s4bdownclient` |
| 4A 回归 | **13/13 全绿**（`S4A_REGRESSION_OK passes=13 expected=13`） |
| 商店 | 6/8 上架（新增自救针/急救包）；工具带/体能归 S4B-5 |
| 已知阻塞项 | 无 |
| 未覆盖 | 怪物伤害来源（S4B-2/3）、诅咒（S4B-4）、三关梯度（S4B-5）、A10/A11 真人 |

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

## 6. 遗留 / 下一 Story

- **S4B-2**：`NoiseBus.AudibleAt`（4/10/16m 本地听觉）、`NoiseNetworkState` + `NoiseHUD`、空眼七状态、追逐预算 `MonsterDirector`。
- **S4B-3**：塑料导购视线冻结、尖啸罐提前引爆。
- **S4B-4**：笑面偶 / 无字牌位、60% 计价、休眠。
- **S4B-5**：12/14/16 锚点、1800/2400/2800、工具带/体能、`-s4bshop8`、21 条回归。
- **本轮未验**：`medkit` 对队友 60 HP 的自动断言（需要第二个存活玩家；留给 S4B-2/3 的双人用例）；A8 报告的措辞更新；A10/A11 真人。
- **已知限制**：`-s4bdown` 是同机双进程，不是跨机；`-s4bwipe` 是单人团灭，多人团灭走 `TeamWipeDetector` 同一路径但未单独跑双人。
