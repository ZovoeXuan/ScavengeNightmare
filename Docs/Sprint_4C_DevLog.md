# Sprint 4C · 开发日志（阶段一：S4C-1 / S4C-3）

> 范围：S4C-1 商店 8/8（工具带/体能商品化）、S4C-3 夜路商店终端清零（T12）
> 起始：2026-10-31　计划：[`Sprint_4C_TechnicalPlan.md`](Sprint_4C_TechnicalPlan.md)
> 状态：**4C 未收口**。S4C-2 已完成（见附录 A）；S4C-4（四类占位音）、S4C-5（真人验收包 + Q4）待做。

---

## 1. 结论摘要

| 项 | 状态 |
|---|---|
| S4C-1 商店 8/8 | **完成**：`tool_belt` 700 / `stamina_training` 1200 已商品化；`S4B_SHOP8_OK items=8 durable=6 balanceDelta=4370 slots=2 capacity=7.5` |
| S4C-1 重锚定 | **完成**：默认 1 槽 / 5.0s；购买后 2 槽 / 7.5s。`-s4bbelt` 先真实购买再测，仍 `sprint=7.32 pct=36.6` |
| S4C-3 夜路终端 | **完成**：`S4C_SHOP_TERMINAL_REMOVED scene=Night1 count=1`；`nightShop=0 safehouseShop=1` |
| S4C-3 商店用例迁移 | **完成**：`-s4ashop` 改为「Night1 → Safehouse → 买 4 件」，`S4A_SHOP_OK` 保持 |
| S4C-2 全队「继续」 | **完成**（附录 A）：`S4C_LEGCLIENT_OK legs=3 dual=1 advances=3 balance=7000 ok=True`；C3 1/2 不换关两侧都有证据 |
| 4B 回归 | **15/15**：`S4B_REGRESSION_OK passes=15 expected=15` |
| 4A 回归 | **13/13**：`S4A_REGRESSION_OK passes=13 expected=13` |
| 未完成 | S4C-4 / S4C-5；A1/A2/A10/A11 真人门仍未开；Q4 未写 |

---

## 2. S4C-1 证据

### 2.1 静态校验

```
[S4A] S4A_SETUP_OK scene=Night1 anchors=16 quotaTerminal=1 traits=0 shop=0 shopItems=8
[S4B] S4B_VALIDATE_OK vitals=1 revive=1 wipe=1 downedHud=1 shop8=1 tests=12 monsters=6 monsterKinds=3
      noiseHud=1 monsterPrefab=1 noiseState=1 curseAnchors=2 curseKinds=2 curseHud=1
      legs=3 legAnchors=12/14/16 legValues=3155/4365/4785 belt=1 stamina=1
      nightShop=0 safehouseShop=1 shopItems=8
```

### 2.2 `-s4bshop8`（安全屋真实终端，买齐 8 件）

```
[S4C] S4C_SHOP8_SCENE from=Night1 to=Safehouse
[S4C] S4C_SHOP8_START balance=5000 owned=0 visit=0
[S4A] GATE_BUY itemId=grip_gloves      price=400  balanceAfter=4600
[S4A] GATE_BUY itemId=packing_tape    price=500  balanceAfter=4100
[S4A] GATE_BUY itemId=flashlight      price=350  balanceAfter=3750
[S4A] GATE_BUY itemId=grip_training   price=900  balanceAfter=2850
[S4A] GATE_BUY itemId=self_injector   price=120  balanceAfter=2730
[S4A] GATE_BUY itemId=medkit          price=200  balanceAfter=2530
[S4A] GATE_BUY itemId=tool_belt       price=700  balanceAfter=1830
[S4A] GATE_BUY itemId=stamina_training price=1200 balanceAfter=630
[S4C] GATE_BELT_SLOTS before=1 after=2 owned=1
[S4C] GATE_STAMINA_CAP before=5.0 after=7.5 trained=1
[S4C] S4C_SHOP8_EFFECT grabBreak=1.50 cartSlip=2.00 flashlight=18.0 carryBoost=1.15
      injector=1 medkit=1 slots=2 capacity=7.5
[S4B] S4B_SHOP8_OK items=8 durable=6 balanceDelta=4370 slots=2 capacity=7.5 ok=True
```

判据不是「余额减少了」，而是**每件都读出效果**：四件 4A 商品看倍率（1.50 / 2.00 / 18.0 / 1.15），两件消耗品看库存（1 / 1），工具带看槽位（1→2），体能看容量（5.0→7.5）。余额差 4370 与八件单价之和逐字一致。

### 2.3 4B 证据重锚定（`-s4bbelt`）

工具带/体能从默认能力改成商品后，4B 的 37.5% 证据不能凭空保留，用例必须先真实购买：

```
[S4C] GATE_STAMINA_CAP base=5.0 capacity=5.0 trained=0     ← 默认档
[S4C] GATE_BELT_SLOTS before=1 after=? owned=0
[S4C] GATE_BELT_SLOTS before=1 after=2 owned=1
[S4C] GATE_STAMINA_CAP before=5.0 after=7.5 trained=1
[S4B] S4B_STAMINA_DEPLETED seconds=7.50 pct=37.5
[S4B] S4B_BELT_OK sprint=7.32 pct=36.6 held=42.0 beltMass=2.0 total=44.0 dropped=1 ok=True
```

### 2.4 商品表（8 件，顺序只许追加）

| # | id | 价 | maxOwned | 效果 | 自动读数 |
|---|---|---|---|---|---|
| 1 | `grip_gloves` | 400 | 1 | `GrabBreak 1.5` | `grabBreak=1.50` |
| 2 | `packing_tape` | 500 | 1 | `CartSlip 2.0` | `cartSlip=2.00` |
| 3 | `flashlight` | 350 | 1 | `Flashlight 18` | `flashlight=18.0` |
| 4 | `grip_training` | 900 | 1 | `CarryPenalty 1.15` | `carryBoost=1.15` |
| 5 | `self_injector` | 120 | 1 | `SelfInjector` | `injector=1` |
| 6 | `medkit` | 200 | 2 | `Medkit 60` | `medkit=1` |
| 7 | `tool_belt` | 700 | 1 | `ToolBelt` | `slots=2` |
| 8 | `stamina_training` | 1200 | 1 | `StaminaTraining` | `capacity=7.5` |

---

## 3. S4C-3 证据（T12）

```
[S4C] S4C_SHOP_TERMINAL_REMOVED scene=Night1 count=1
[S4C] S4C_SHOP_SCENE from=Night1 to=Safehouse
[S4C] S4C_SHOP_SCENE_OK scene=Safehouse terminal=1 window=1
[S4A] S4A_SHOP_OK balance=600 owned=1 revoked=7 visit=8 identity=1 ok=True
```

- 夜路场景里不再有 `ShopTerminal`，`ShopTerminal` 的 `!S4ACli.Is4ARun` 场景豁免一并删除；`-s4a*` 也必须进安全屋才能买。
- 负向注入保留：`-s4bleg` 每次进夜路都放一个临时终端探针，仍走真实 `ServerHandleBuy`：

```
[S4B] S4B_LEG_WRONG_SCENE_PROBE reason=no_terminal_in_night
[S4A] GATE_BUY_REJECT itemId=hotkey1 reason=wrong_scene balance=0 visit=0 dist=1.00 scene=Night1
```

---

## 4. 本轮修掉的问题

1. **回归假红：缺中性身份前置行**
   - 现象：`s4bshop8` 逻辑已过（harness 打印 `acceptance_marker_present=yes`），仍判 `FAIL reason=neutral_precondition_missing`。
   - 根因：门禁要求每个用例先打 `S4A_NEUTRAL_OK traits=none feel=...`，新夹具漏了。
   - 修法：补该行；重跑后 `PASS s4bshop8`。

2. **编辑器校验把 `shopItems` 读成 0**
   - 现象：`S4B_VALIDATE_OK ... shopItems=0`（前面 `Count>=8` 检查已通过）。
   - 根因：`EditorSceneManager.OpenScene(Night1)` 卸载了先前 `LoadAssetAtPath` 拿到的目录实例，旧引用变成 Unity 假 null。
   - 修法：在切场景后再 `LoadAssetAtPath` 一次；同时新增 `nightShop=0 / safehouseShop=1` 两侧计数。

3. **终端移除后 `-s4bleg` 的 wrong_scene 变成静默 SKIP**
   - 现象：`S4B_LEG_WRONG_SCENE_SKIP reason=no_terminal_in_night`，负向注入等于没验。
   - 修法：测试临时生成一个 `ShopTerminal_NightProbe`，调真实守卫、打拒绝行、立即销毁。

---

## 5. 回归

| 门 | 结果 | 说明 |
|---|---|---|
| 4B | `S4B_REGRESSION_OK passes=15 expected=15` | `RUN_DIR=...\Logs\s4_reg\周六022610-152239`；`CASES_CHECK_OK expected=15 floor=15`；`STALE_CHECK_OK src=202610311521 bin=202610311522` |
| 4A | `S4A_REGRESSION_OK passes=13 expected=13` | `RUN_DIR=...\Logs\s4a_reg\周六022610-153312`；`-s4ashop` 已迁到安全屋 |

清单变更：`regression_4b_cases.txt` 14 → **15**（新增 `s4bshop8`）；并集 `regression_cases.txt` 27 → **28**；`expected` 仍由行数推导。

---

## 6. 与计划的偏差 / 仍欠

1. **4C 门尚未建**：`regression_4c_cases.txt` / `run_regression_4c.bat` 等 S4C-2/4 用例齐了再建（计划是 18 行）；本轮 `s4bshop8` 暂挂 4B 门，marker 用 `S4B_SHOP8_OK`。
2. **C1/C2 反向注入还不是独立用例**：未购买时 1 槽 / 5.0s 已由 `-s4bshop8` 的前置读数覆盖；「第二槽 `reason=locked` 显式拒绝」只有代码路径，没有自动断言。
3. **S4C-2 未做**：`-s4blegclient` 与全队「继续」确认仍是空白；A2 未覆盖。
4. **S4C-4 未做**：四类占位音仍只有 `FrictionEvents` 的一个短音。
5. **S4C-5 未做**：真人 runbook、访谈模板、P0 交付报告（含 Q4）未写；A1/A2/A10/A11 仍未开。
6. 正式美术/音频由用户后补，本轮不接（附录 B 规格已在计划里）。

---

## 7. 新增 / 修改文件

| 类型 | 文件 |
|---|---|
| 新增（商品） | `Data/Shop/tool_belt.asset`、`Data/Shop/stamina_training.asset` |
| 新增（测试） | `Networking/S4BShop8Test.cs` |
| 新增（文档） | `Docs/Sprint_4C_TechnicalPlan.md`、`Docs/Sprint_4C_DevLog.md` |
| 修改（商店） | `Economy/ShopItem.cs`（枚举追加）、`Economy/EquippedEffects.cs`、`Economy/ShopTerminal.cs`（删场景豁免）、`Resources/ShopCatalog.asset`（8 件） |
| 修改（能力） | `Gameplay/BeltSlot.cs`（有效槽位 + `reason=locked`）、`Networking/PlayerStamina.cs`（5.0/7.5 档） |
| 修改（装配） | `Editor/S4ASceneSetup.cs`（8 件、移除夜路终端、校验）、`Editor/S4BSceneSetup.cs`（`shop8_test`、夜路/安全屋计数） |
| 修改（用例） | `Networking/S4AShopTest.cs`（迁安全屋）、`Networking/S4BBeltTest.cs`（购买后测）、`Networking/S4BLegTest.cs`（wrong_scene 探针） |
| 修改（工具） | `Tools/regression_4b_cases.txt`（15）、`Tools/regression_cases.txt`（28） |
| 场景 | `Scenes/Night1.unity`、`Scenes/Safehouse.unity`（装配改动，单独提交） |

---

## 8. 下一步

1. ~~S4C-2：全队「继续」确认 + `-s4blegclient`（含 C3 反向注入）~~ → **已完成，见附录 A**。
2. S4C-4：四类占位音 + `-s4baudio`（含 C5 可区分断言）。
3. S4C-5：真人 runbook / 访谈模板 / P0 交付报告（Q4）。
4. 用例齐后建 `run_regression_4c.bat`（18 行）并补 C1/C2 独立断言。

---

# 附录 A · 阶段二（S4C-2：双人三关 + 全队确认）

> 日期：2026-10-31　夹具：`Networking/S4CLegClientTest.cs`
> 跑法：Host `-s4clegclienthost`，等 `S4A_BOOT_OK spawned=1` 后再起 Client `-s4clegclient -address=127.0.0.1`
> 证据目录：`Logs\s4c_manual4`（两边 `exit=0`）

## A.1 正向：全员确认才换关（三关）

```
[S4C] S4C_LEGCLIENT_DUAL players=2
[S4C] S4C_CONTINUE_READY leg=1 netId=19 host=0 ready=1 count=1/2
[S4C] S4C_CONTINUE_READY leg=1 netId=18 host=1 ready=1 count=2/2
[S4C] S4C_CONTINUE_ADVANCE leg=1 count=2/2 from=Safehouse to=Night1
[S4C] S4C_LEGCLIENT_STEP leg=1 advanced=True legIndex=2 balance=1800 advanceLog=True
[S4C] S4C_CONTINUE_ADVANCE leg=2 count=2/2 from=Safehouse to=Night1
[S4C] S4C_LEGCLIENT_STEP leg=2 advanced=True legIndex=3 balance=4200 advanceLog=True
[S4C] S4C_CONTINUE_ADVANCE leg=3 count=2/2 from=Safehouse to=Night1
[S4C] S4C_LEGCLIENT_STEP leg=3 advanced=True legIndex=4 balance=7000 advanceLog=True
[S4C] S4C_LEGCLIENT_OK legs=3 dual=1 advances=3 balance=7000 ok=True
```

客户端侧：

```
[S4C] S4C_LEGCLIENT_CLIENT_STEP leg=1 advances=1 seconds=3.0 legIndex=1
[S4C] S4C_LEGCLIENT_CLIENT_STEP leg=2 advances=2 seconds=3.0 legIndex=2
[S4C] S4C_LEGCLIENT_CLIENT_STEP leg=3 advances=3 seconds=3.0 legIndex=3
[S4C] S4C_LEGCLIENT_CLIENT_OK legs=3 advances=3 ok=True
```

## A.2 反向验证 C3：1/2 不换关

- Host 侧：`S4C_CONTINUE_WAIT leg=N reason=not_all_ready count=1/2` 从 `seconds=0.0` 连到 `4.0`，`S4C_LEGCLIENT_LONE ... stayed=True waitLog=True seconds=2.5`，三关均成立。
- Client 侧：`S4C_LEGCLIENT_CLIENT_LONE leg=N ready=1/2 scene=Safehouse stayed=True seconds=2.0`，三关均成立。

两条证据同时存在才说明「门确实在等第二个人」，而不是「观测窗太短没来得及换关」。

## A.3 本轮定位并修掉的三个真缺陷

1. **客户端账本随场景卸载被销毁**
   - 证据：`S4C_DIAG side=client ... acctNull=1 ... ready=-1/-1`，客户端读到的是已销毁实例。
   - 根因：客户端 GameRoot 由 `NetworkClient.SpawnPrefab()` 直接实例化到当前场景，换关随场景卸载；`S4ABootstrap` 只给服务端做了 DDOL。
   - 修法：`TeamAccount.OnStartClient()` 里 `DontDestroyOnLoad(gameObject)`（host 幂等）。修后 `acctNull=0 acctNetId=2 acctClient=1`。

2. **客户端传送不生效（服务端永远看到出生点）**
   - 根因：旧 `Teleport` 先禁用 `NetworkTransformReliable` 再改位置。玩家是客户端权威（`syncDirection=ClientToServer`），禁用 NT 等于把「位置变化推给服务端」的通道一起关掉。
   - 修法：只写 `transform.position`，不碰 NT。

3. **房主自己的按住不 latch（count 停在 1/2）**
   - 证据：`S4C_DIAG side=host hostPlayer=1 pos=(0.0,1.0,5.5) dist=11.5`，以及坏路径下 `pos=(-6.0,-3752.6,-15.2)`（掉出地面）。
   - 根因：房主是服务端权威，站位交给坡度/重力，实测从入口锚点滑到 `(0,0)`（离门 6m）甚至掉出地图；`Teleport` 只挪一次，之后又自己滑走。
   - 修法：按住期间每帧钉回 `SafehouseStand`；并每秒打 `S4C_DIAG`（新增 `held` / `portalReady` / `portalPlayers`，用来区分「门没 latch」与「复制没到」）。

## A.4 纪律边界（不得换算成真人门）

双进程自动化只证明**代码路径**：C3 的 1/2 不换关、全员 2/2 才换关。它**不是** A2（双人真人继续确认）的证据，也不构成 P0 通过。A2 仍需真人按 A2 清单跑。

