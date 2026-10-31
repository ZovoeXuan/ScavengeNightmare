# Sprint 4C · 开发日志（S4C-1 / 2 / 3 / 4 / 5）

> 范围：S4C-1 商店 8/8（工具带/体能商品化）、S4C-3 夜路商店终端清零（T12）、S4C-2 全队继续、S4C-4 四类占位音、S4C-5 真人验收包
> 起始：2026-10-31　计划：[`Sprint_4C_TechnicalPlan.md`](Sprint_4C_TechnicalPlan.md)
> 状态：**4C 自动化收口**（4C 门 18/18）。A1/A2/A10/A11 真人门仍未开，P0 待用户签字。

---

## 1. 结论摘要

| 项 | 状态 |
|---|---|
| S4C-1 商店 8/8 | **完成**：`tool_belt` 700 / `stamina_training` 1200 已商品化；`S4B_SHOP8_OK items=8 durable=6 balanceDelta=4370 slots=2 capacity=7.5` |
| S4C-1 重锚定 | **完成**：默认 1 槽 / 5.0s；购买后 2 槽 / 7.5s。`-s4bbelt` 先真实购买再测，仍 `sprint=7.32 pct=36.6` |
| S4C-3 夜路终端 | **完成**：`S4C_SHOP_TERMINAL_REMOVED scene=Night1 count=1`；`nightShop=0 safehouseShop=1` |
| S4C-3 商店用例迁移 | **完成**：`-s4ashop` 改为「Night1 → Safehouse → 买 4 件」，`S4A_SHOP_OK` 保持 |
| S4C-2 全队「继续」 | **完成**（附录 A）：`S4C_LEGCLIENT_OK legs=3 dual=1 advances=3 balance=7000 ok=True`；C3 1/2 不换关两侧都有证据 |
| S4C-4 四类占位音 | **完成**（附录 B）：`S4C_AUDIO_OK cues=4 distinguishable=4 ok=True`；C5 注入实测 `distinguishable=1 → FAIL` |
| S4C-5 真人验收包 | **完成**（附录 C）：runbook / 访谈模板 / P0 交付报告三件 + 记录器四类事件 |
| 4C 门 | **18/18**：`S4C_REGRESSION_OK passes=18 expected=18`（含 `s4baudio`） |
| 4B 回归 | **15/15**：`S4B_REGRESSION_OK passes=15 expected=15` |
| 4A 回归 | **13/13**：`S4A_REGRESSION_OK passes=13 expected=13` |
| 并集回归 | **31/31**：`S4_REGRESSION_OK passes=31 expected=31`（`s4a 14 / s4b 15 / s4c 2`，无漂移） |
| 未完成 | A1/A2/A10/A11 真人门；正式美术/音频（用户后补） |

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

4. **`s4baudio` 假红：新夹具漏打中性身份前置行**
   - 现象：`NOTE s4baudio acceptance_marker_present=yes` 仍判 `FAIL reason=neutral_precondition_missing`；手工单跑 exit=0，看不出来。
   - 根因：`S4CAudioTest.Start()` 只设了 `ForceNoTraits` / `SuppressManual`，没打印 `S4A_NEUTRAL_OK`。
   - 修法：按 `S4ALoop`/`S4BMonster` 等夹具同款补一行 `[S4A] S4A_NEUTRAL_OK traits=none feel=...`；重建后 `PASS s4baudio`。

---

## 5. 回归

| 门 | 结果 | 说明 |
|---|---|---|
| 4B | `S4B_REGRESSION_OK passes=15 expected=15` | `RUN_DIR=...\Logs\s4_reg\周六022610-152239`；`CASES_CHECK_OK expected=15 floor=15`；`STALE_CHECK_OK src=202610311521 bin=202610311522` |
| 4A | `S4A_REGRESSION_OK passes=13 expected=13` | `RUN_DIR=...\Logs\s4a_reg\周六022610-153312`；`-s4ashop` 已迁到安全屋 |
| 4C | `S4C_REGRESSION_OK passes=18 expected=18` | `RUN_DIR=...\Logs\s4_reg\周六022610-174732`；`STALE_CHECK_OK src=202610311745 bin=202610311746`；`s4baudio` PASS |
| 并集 | `S4_REGRESSION_OK passes=31 expected=31` | `RUN_DIR=...\Logs\s4_reg\周六022610-175450`；`CASES_CHECK_OK expected=31 floor=31`；`STALE_CHECK_OK src=202610311745 bin=202610311746`；`BREAKDOWN s4a 14 / s4b 15 / s4c 2` |

清单变更：`regression_4b_cases.txt` 14 → **15**（新增 `s4bshop8`）；新增 `regression_4c_cases.txt`（18）；并集 `regression_cases.txt` 28 → **31**（`# expected_min: 31`）；`expected` 仍由行数推导。

---

## 6. 与计划的偏差 / 仍欠

1. ~~**4C 门尚未建**~~ → **已建**（18 行，见附录 C）。
2. ~~**C1/C2 反向注入还不是独立用例**~~ → C2 已由 `-s4bshop8` 前置读数覆盖；C1 的显式 `reason=locked` 仍无独立断言（缺口 G5）。
3. ~~S4C-2 未做~~ → **已完成，见附录 A**；A2 仍只有代码路径证据。
4. ~~S4C-4 未做~~ → **已完成，见附录 B**；正式素材仍待用户后补（T1）。
5. ~~**S4C-5 未做**~~ → **已完成，见附录 C**；A1/A2/A10/A11 真人门仍未开。
6. 正式美术/音频由用户后补，本轮不接（附录 B 规格已在计划里）。

---

## 7. 新增 / 修改文件

| 类型 | 文件 |
|---|---|
| 新增（商品） | `Data/Shop/tool_belt.asset`、`Data/Shop/stamina_training.asset` |
| 新增（测试） | `Networking/S4BShop8Test.cs`、`Networking/S4CLegClientTest.cs`、`Networking/S4CAudioTest.cs` |
| 新增（音频） | `Audio/AudioCue.cs`、`Audio/AudioBus.cs` |
| 新增（验收包） | `Docs/Playtest_4C_Runbook.md`、`Docs/Playtest_4C_Interview.md`、`Docs/P0_Delivery_Report.md` |
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
2. ~~S4C-4：四类占位音 + `-s4baudio`（含 C5 可区分断言）~~ → **已完成，见附录 B**。
3. ~~S4C-5：真人 runbook / 访谈模板 / P0 交付报告（Q4）~~ → **已完成，见附录 C**。
4. ~~用例齐后建 `run_regression_4c.bat`（18 行）~~ → **已完成**；C1 独立断言仍未补（缺口 G5）。

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

---

# 附录 B · 阶段三（S4C-4：R6 四类占位音）

> 日期：2026-10-31　夹具：`Networking/S4CAudioTest.cs`（`-s4baudio`）
> 新增：`Audio/AudioCue.cs`（四条波形定义）、`Audio/AudioBus.cs`（合成 + 冷却 + 播放日志）
> 钩子：`Cart/CartController.EmitNoise`、`Gameplay/GrabbableProp.PlayHeavyDrop`（经 `NetworkedProp.RpcHeavyDrop` 广播）、`Gameplay/FragileProp.ApplyBreak`、`Curses/CurseRuntime.TickLaughs` 前兆起点
> 证据目录：`Logs/s4c_audio5.log`（正向）、`Logs/s4c_audioflat.log`（C5 注入）

## B.1 正向（`-s4baudio`，exit=0）

```
[S4C] S4C_AUDIO_PLAY cue=heavy_drop pitch=1.00 freq=90 dur=0.22 wave=thump mass=60.0
[S4C] S4C_AUDIO_PLAY cue=glass_break pitch=1.00 freq=900 dur=0.30 wave=noise speed=6.2
[S4C] S4C_AUDIO_PLAY cue=curse_omen pitch=1.00 freq=320 dur=0.50 wave=sweep lead=1.5
[S4C] S4C_AUDIO_PLAY cue=cart_wheel pitch=0.91 freq=180 dur=0.18 wave=sine speed=1.9
[S4C] S4C_AUDIO_METRICS cues=4 distinguishable=4 heavyMass=60.0 glassSpeed=6.2
      omenLead=1.50 omenToLaugh=1.50 wheelSpeed=1.9 flat=0
[S4C] S4C_AUDIO_OK cues=4 distinguishable=4 failures=0 ok=True
```

- 四类都是**真实触发**，不是直接调 AudioBus：重货（safe 60kg）自由落体、易碎件从 2.5m 砸碎、笑面偶前兆、推车过 1.5m/s。
- 轮声 pitch=0.91 落在计划要求的 0.8–1.2；前兆 `lead=1.50` 与「前兆→发笑」实测间隔 `1.50` 对齐（`omenToLaugh`）。
- 负向：1kg 罐头同高度落地**不许**触发 heavy_drop（`light_prop_played_heavy_drop` 未出现）。

## B.2 C5 反向注入（`-s4baudio -s4caudioflat`，exit=1）

```
[S4C] S4C_AUDIO_METRICS cues=4 distinguishable=1 ... flat=1
[S4C] S4C_AUDIO_FAIL cues=4 distinguishable=1 failures=1
```

- 注入把四类 cue 压成同一段波形，四类**都播了**但指纹去重只有 1 → 用例必须红。判据取日志原文的 `freq/dur/wave` 去重，不读 AudioBus 自己的计数器。

## B.3 实现口径 / 取舍

| 项 | 决定 | 理由 |
|---|---|---|
| 素材 | 程序合成，不落任何 clip 资源 | 用户后补正式音频（T1）；D31 只换 `AudioCueInfo` 与合成函数，调用点不动 |
| 播放位置 | 服务端判定后广播（重物/前兆），碎裂走既有 `RpcShatter` 四端各播 | 客户端物件是 kinematic，收不到碰撞；不广播的话双人局客户端整局听不到 |
| 冷却 | 每类 cue 独立 0.15s | 风险表 R4：40 物 + 4 怪时不许刷爆 |
| 轮声 | 服务端播放 | 与 `CartController` 的服务端权威边界一致；**客户端听不到轮声**是已知剩余项，正式素材阶段随「客户端音效广播」一起收 |

## B.4 纪律边界

占位音通过 ≠ A11（真人体验）通过。四类 cue 只证明「有可区分的声音」，是否「听得懂、会因此调整行为」仍只能由真人访谈判定。

---

# 附录 C · 阶段四（S4C-5：真人验收包 + 4C 门 + P0 交付报告）

> 日期：2026-10-31
> 新增：`Docs/Playtest_4C_Runbook.md`、`Docs/Playtest_4C_Interview.md`、`Docs/P0_Delivery_Report.md`
> 新增：`Tools/regression_4c_cases.txt`（18 行）、`Tools/run_regression_4c.bat`
> 代码：`Gameplay/PlaytestRecorder.cs` 四类 4C 事件 + 三处落点

## C.1 记录器事件（CSV 只有房主进程写）

| type | 落点 | detail 形状 |
|---|---|---|
| `leg_start` | `LevelRouteService.AfterSceneChanged`（夜路分支，`SetQuota` 之后） | `leg=.. scene=.. quota=..` |
| `leg_end` | `TeamAccount.SetLegProgress` 首次越过额度（`GATE_QUOTA_GATE` 之后） | `leg=.. progress=.. quota=.. carry_over=..` |
| `continue_vote` | `LevelPortal` 逐人 latch 后 | `leg=.. netId=.. count=N/M` |
| `run_end` | `TeamAccount.ServerEndRun` 末尾（写后立即 `Flush()`） | `reason=.. leg=.. balance=..` |

- 全部 `if (!started) return;`：非房主进程（客户端）不写盘，避免两份 CSV 交错。
- `leg_end` 只在 `wasReached=false → newReached=true` 那一拍写一次：额度达标后被反复 `SetLegProgress` 不会刷屏。
- `continue_vote` 用 `netId` 而不是名字：两名玩家同名时也能分开。

## C.2 真人验收包三件

| 文件 | 用途 | 关键判据 |
|---|---|---|
| `Playtest_4C_Runbook.md` | 出包/启动参数/键位/三场安排 | 禁带任何 `-s4a*/s4b*/s4c*` 夹具参数；A1/A2/A11 逐条 |
| `Playtest_4C_Interview.md` | 5 问访谈（Q1–Q5） | Q1 再来一关 ≥50% / Q2 ≥3 自主意外 / Q3 ≥2 共同笑；代填即 A11 FAIL（C6） |
| `P0_Delivery_Report.md` | 自动化证据 + 真人段 + Q4 + X1–X9 + 缺口 | 结论只允许「P0 待用户签字」；Q4 写「有意裁剪」 |

## C.3 4C 门（18 行）与覆盖面

```
CASES_CHECK_OK expected=18 floor=18
STALE_CHECK_OK src=202610311745 bin=202610311746
PASS s4baudio
S4C_REGRESSION_BREAKDOWN s4a pass=1 fail=0 s4b pass=15 fail=0 s4c pass=2 fail=0
S4C_REGRESSION_OK passes=18 expected=18
```

- 18 = 4B 继承 15 + 4C 新增 3（`s4clegclienthost`/`s4clegclient` 对 + `s4baudio`）；`s4bshop8` 归 4C-1。
- 4C 门的意义是**防 4B 漂移**：4C 改了 `BeltSlot`/`PlayerStamina`/`ShopCatalog`/`TeamAccount`/`CartController`/`CurseRuntime`/易碎件，任一 4B 用例变红都算 4C 回归。
- C1–C6 不占行（runner 只接受 PASS）：C5 走 `-s4baudio -s4caudioflat` 手工注入；C3 内含于 `s4clegclient` 对；C1 仍只有前置读数，显式 `reason=locked` 未独立断言（缺口 G5）。

## C.4 纪律边界

自动化全绿只到「代码路径 + 4B 不漂移」。A1（单人 30–40 分钟）、A2（双人全队确认）、A10（三关时长）、A11（真人体验 + 访谈）**一门未开**，`P0_Delivery_Report.md` 的真人段留空即代表未跑，不得用自动化结果填写。

