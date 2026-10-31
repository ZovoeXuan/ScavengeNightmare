# Sprint 4C 技术实施计划（P0 收口门）

> 目标：把 4B 的「一局压力成立」推进到「P0 可判」：商店 8/8、双人 3 关与全队确认、夜路商店终端清零、R6 四类占位音、真人验收包与 P0 交付报告。
> 依据：`P0_VerticalSlice.md` v1.2、`P0_AcceptanceGate.md` v1.4、`Sprint_4B_TechnicalPlan.md` v1.0、`Sprint_4B_DevLog.md` §13、`design/Items_Characters_Shop_v1.md`、`design/Monster_Roster_v1.md`
> 范围纪律：**只写计划，不写代码、不改代码**。本文件是 Sprint 4C 的唯一技术入口。
>
> ## 本文通过 ≠ P0 通过
>
> P0 的判定权在 `P0_AcceptanceGate.md`：A1/A2/A11 必须真人，A10 必须真人计时，A6/A8/A12 必须自动化。
> 4C 的交付物 = **自动化全绿 + 真人录像 + 5 分钟访谈 CSV + P0 交付报告**。自动化只能覆盖「代码路径走过」，不能替真人签字。
> 对外措辞：4C 完成前只能说 **「一局压力成立，真人门未开」**；4C 完成后也只能说 **「P0 待用户签字」**。

**版本记录**

- v1.0（2026-10-31，首版）：按 P0 v1.2 与 4B 收口缺口拆 5 个 Story；裁决「商店 8 件商品化」「挑件不实现」「三图不做」「全队继续确认」「T12 终端迁移」「素材后补」；新增 4C 用例 4 条与回归门 `S4C_REGRESSION_OK`。

---

## 零、先裁决六件事（否则 4C 一定返工）

| # | 争议 | 4C 的裁决 | 依据 / 代价 |
|---|---|---|---|
| **R1** | 4B 把工具带/体能当成**系统默认能力**，商店只有 6 件；P0 §2.5 要求首发 8 件且「每件都能自动断言」 | **两者改成商品**：`tool_belt` 700（腰带槽 1→2）、`stamina_training` 1200（连续冲刺 5.0s→7.5s）。默认变为「1 槽 / 5.0s」 | P0 §2.5 是门票。代价：4B 的 `S4B_STAMINA_OK pct=36.6` 需重锚定（未购买基线 25.0%，购买后 37.5%），`-s4bbelt` 必须先购买再测 |
| **R2** | 4A 计划 T5 写「挑件提交是 P0 门（4C）必还」；P0 v1.2 已取消挑件 | **4C 不实现挑件**。只做 Q4：P0 交付报告显式引用「取消挑件 = 对 17.6 的有意裁剪」 | `P0_VerticalSlice.md` §七-16、`P0_AcceptanceGate.md` §2.2 Q4。T5 是 4A 历史说法，**以 P0 v1.2 为准** |
| **R3** | 设计稿给 L1/L2/L3 三张新图与配额 2400/2600/3000 | **4C 仍只用 Night1 一张图**，配额维持 1800/2400/2800 | P0 §1 与 `Sprint_4B_TechnicalPlan.md` R2；三图进 P1 |
| **R4** | 4A 的换关是「任意一人长按即推进」，A2 要求全队确认 | **安全屋出口改为逐人确认**：每名在线玩家各自就位长按 1.2s 记一次 ready，全员齐才推进；任何一人离开半径或松手超过 2s 则清空自己的 ready；HUD 显示 `x/n` | `P0_AcceptanceGate.md` §3.3「全队『继续』确认（4A 是房主代决）」 |
| **R5** | Night1 场景里仍残留 `ShopTerminal`，只靠 `wrong_scene` 拒绝（4B 技术债 T12） | **从 Night1 移除**，只保留 Safehouse；`-s4ashop` 改为「Night1 达标 → Safehouse 购买」两段流程；删除 `!S4ACli.Is4ARun` 场景豁免 | P0 §2.4「商店在安全屋」。代价：`-s4ashop` 重写，4A 13 条需重跑 |
| **R6** | 正式美术/音频由用户后续提供；P0 R6 仍要求四类占位音 | **4C 只做占位音**（推车轮速/重物落地/碎裂/诅咒前兆）+ 素材接入规格（附录 B）；正式素材接入不阻塞 4C | `P0_VerticalSlice.md` R6；用户已确认「美术音频后面再提供」 |

> **三项需要用户确认但不影响开工**：R1 商品化是否接受（会改变未购买玩家的默认能力）；真人测试的排期与人数；正式素材的交付时间。默认按上表执行。

---

## 一、现状（4B 收口口径）

### 1.1 已经绿、4C 不再造

| 能力 | 证据 | 备注 |
|---|---|---|
| 4A 13/13 | `S4A_REGRESSION_OK passes=13 expected=13` | 在 4B 最终二进制上复跑 |
| 4B 14/14 | `S4B_REGRESSION_OK passes=14 expected=14` | 4C 每 Story 结束必须保持 |
| 三关梯度 | `S4B_LEG_OK legs=3 ratios=57.1/55.0/58.5` | 配额/锚点/怪/诅咒全读 `LegContentTable` |
| 生存链 / 噪音 / 怪物 / 诅咒 | `-s4bvitals` / `-s4bdown*` / `-s4bwipe` / `-s4bnoise` / `-s4bmon` / `-s4bgreeter` / `-s4bscream` / `-s4bcurse` | 不在 4C 范围 |
| 性能 | `S4B_PERF_OK fps=120 kbps=0` | 带宽另见双进程观测 |

### 1.2 仍缺（4C 的全部工作）

| # | 缺口 | 现状 | 归属 |
|---|---|---|---|
| G1 | 商店 6/8；工具带/体能不是商品 | `ShopCatalog.asset` 6 个引用；`S4BSceneSetup` 断言 `shop6=1` | S4C-1 |
| G2 | 无双人 3 关自动化；换关是单人代决 | 只有 `-s4bdownhost`+`-s4bdownclient` 一次救援冒烟 | S4C-2 |
| G3 | Night1 商店终端未移除 | `ShopTerminal` 用 `wrong_scene` 拒绝；`-s4a*` 有豁免 | S4C-3 |
| G4 | 四类占位音未做 | 只有 `FrictionEvents` 的 0.12s 合成短音；无音频资源 | S4C-4 |
| G5 | 真人验收未做；Q4 未写 | 无 runbook、无访谈模板、无 P0 交付报告 | S4C-5 |

---

## 二、Story 拆分

### S4C-1 商店 8/8：工具带与体能商品化 + `-s4bshop8`

**改什么**

| 文件 | 改动 |
|---|---|
| `Economy/ShopItem.cs` | `ShopEffect` **只许追加** `ToolBelt = 7`、`StaminaTraining = 8`（位序不能插队，老存档 `ownedMask` 会错位） |
| `Editor/S4ASceneSetup.cs` | 目录追加 `tool_belt`（700、`maxOwned=1`、hotkey 7）、`stamina_training`（1200、`maxOwned=1`、hotkey 8）；`ShopCatalog.asset` 变成 8 个引用 |
| `Gameplay/BeltSlot.cs` | 有效槽位 = `1 + (已购 tool_belt ? 1 : 0)`；未购买时第二槽直接拒绝并 `GATE_BELT_REJECT reason=locked` |
| `Networking/PlayerStamina.cs` | 基础容量 5.0s（25%），已购 `stamina_training` → 7.5s（37.5%）；`SprintCapacitySeconds` 不能再是 `const` |
| `Economy/EquippedEffects.cs` | 新增 `HasToolBelt` / `HasStaminaTraining`（读 `ownedMask` 位） |
| `UI/ShopHUD.cs` | 8 行商品、7/8 热键可用；永久/消耗两类视觉可分 |
| `Networking/S4BShop8Test.cs`（新） | 安全屋真实终端，按 7/8 买齐，逐件断言 |

**判据（`S4B_SHOP8_OK`）**

```
S4A_SHOP8_BUY itemId=tool_belt price=700 balanceAfter=...
S4A_SHOP8_BUY itemId=stamina_training price=1200 balanceAfter=...
GATE_BELT_SLOTS before=1 after=2 owned=1
GATE_STAMINA_CAP before=5.0 after=7.5 level=2
S4B_SHOP8_OK items=8 durable=6 balanceDelta=4370 slots=2 capacity=7.5 ok=True
```

- 余额差 = 8 件价格和（400+500+350+900+120+200+700+1200 = **4370**），不得只看余额 > 0。
- 每件都要有效果读数，买了看不出差别 = 不通过（P0 §2.5）。
- `-s4bbelt` 重锚定：未购买基线 `pct=25.0`、`slots=1`；购买后 `pct=37.5`、`slots=2`、`S4B_BELT_OK` 保持。

### S4C-2 全队「继续」确认 + `-s4clegclienthost` / `-s4clegclient`

**改什么**

| 文件 | 改动 |
|---|---|
| `Economy/LevelPortal.cs` | 安全屋分支改为「逐人 ready 锁存」：`Dictionary<netId, ready>`；全员 ready 才调 `ServerAdvance`；离开半径/松手 2s 清空本人 ready；夜路撤离分支保持「任意一人达标即可」 |
| `UI/LevelPortalHud.cs`（新） | 显示 `继续 3/4`；未齐时显示还差谁 |
| `Networking/S4CLegClientTest.cs`（新） | host 预算 3 关，client 自动就位；逐关断言全队确认后才换场景 |

**判据（`S4C_LEGCLIENT_OK`）**

```
S4C_CONTINUE_READY leg=1 netId=19 host=0 ready=1 count=1/2
S4C_CONTINUE_WAIT leg=1 reason=not_all_ready count=1/2 seconds=0.0
S4C_CONTINUE_READY leg=1 netId=18 host=1 ready=1 count=2/2
S4C_CONTINUE_ADVANCE leg=1 count=2/2 from=Safehouse to=Night1
S4C_LEGCLIENT_OK legs=3 dual=1 advances=3 balance=7000 ok=True
```

（客户端先确认、房主后按：这样 C3 的「1/2 不换关」才是真观察。实现见 DevLog 附录 A。）

- 「一人到位就换关」必须判失败（这是 R4 唯一要防的假通过）。
- 双人 3 关只证明代码路径，**不得写成 A2 通过**；A2 仍是真人门。

### S4C-3 T12：夜路商店终端清零 + `-s4ashop` 迁到安全屋

**改什么**

| 文件 | 改动 |
|---|---|
| `Editor/S4ASceneSetup.cs` | 删除 Night1 的 `ShopTerminal` / `ShopHUD` 生成；只在 `Safehouse` 生成 |
| `Economy/ShopTerminal.cs` | 删除 `!S4ACli.Is4ARun` 场景豁免；非 `Safehouse` 一律 `wrong_scene` |
| `Editor/S4ASceneSetup.cs`（Validate） | 新增 `nightShop=0 safehouseShop=1` |
| `Networking/S4AShopTest.cs` | 改为两段：Night1 刷满额 → 长按门 → Safehouse 内购买 4 件 → 断言余额与效果 |

**判据**

```
GATE_SHOP_SCENE night=0 safehouse=1
S4A_SHOP_OK items=4 balance=... owned=4 revoked=...
S4A_SHOP_REJECT scene=Night1 reason=wrong_scene（负向注入仍然要有）
```

- 4A 13 条必须在改后二进制上重跑；`-s4ashop` 是唯一被重写的 4A 用例，需在 DevLog 写明重锚定原因。

### S4C-4 R6 四类占位音 + `-s4baudio`

**改什么**

| 文件 | 改动 |
|---|---|
| `Audio/AudioBus.cs`（新） | 单例 + 8 个 `AudioSource` 池，`Play(AudioCue, position)`；无音频资源时用程序合成兜底 |
| `Audio/AudioCue.cs`（新） | 四类 cue 枚举：`CartWheel`、`HeavyDrop`、`GlassBreak`、`CurseOmen`；频率/时长/波形/音量全在资源上 |
| `Gameplay/CartController.cs` | 推车速度 > 1.5m/s 时按轮速播放 `CartWheel`（音高随速度，0.8–1.2） |
| `Gameplay/GrabController.cs` / 货件落地 | 质量 ≥ 30kg 落地播 `HeavyDrop` |
| 玻璃/易碎破损路径 | `GlassBreak` |
| `Combat`/诅咒组件 | 发笑前 1.5s 播 `CurseOmen` |

**判据（`S4C_AUDIO_OK`）**

```
S4C_AUDIO_PLAY cue=cart_wheel speed=2.4 pitch=1.05
S4C_AUDIO_PLAY cue=heavy_drop mass=42.0
S4C_AUDIO_PLAY cue=glass_break
S4C_AUDIO_PLAY cue=curse_omen lead=1.5
S4C_AUDIO_OK cues=4 distinguishable=4 failures=0 ok=True
```

- 「可区分」= 四个 cue 的波形/基频/时长在日志里有明确不同读数，不是「都播了」。
- 正式素材接入后，同一 `AudioCue` 换 clip，不改调用点（附录 B）。

### S4C-5 真人验收包 + Q4 交付报告

**交付什么**

| 产物 | 内容 |
|---|---|
| `Docs/Playtest_4C_Runbook.md` | 场次安排、设备、录屏、CSV 收集、访谈脚本、A1/A2/A10/A11 判据逐条 |
| `Docs/Playtest_4C_Interview.md` | 5 分钟访谈模板：≥3 个自主意外、≥2 次可归因共同笑、是否愿意再走一关 |
| `Docs/P0_Delivery_Report.md` | 自动化证据索引 + 真人证据 + **Q4 条款** + 失败条件核对 + 已知缺口 |
| `PlaytestRecorder` / `PlaytestMarker` | 复用 D7 现有组件；补 4C 事件（`leg_start` / `leg_end` / `continue_vote` / `run_end`） |

**判据**

- A1/A2：单人、双人各 3 关，30–40 分钟，0 崩溃；录屏 + 日志双证。
- A10：三关分别 6–9 / 8–12 / 10–14 分钟，中位数 8–12；**原始样本全列**，禁止只报中位数。
- A11：≥30 分钟、不看攻略、≥3 个自主意外、≥2 次可归因共同笑、≥50% 选「再走一关」；只能人判。
- Q4：交付报告必须写「取消挑件提交 = 对 17.6 的有意裁剪」，不得写成「未实现功能」，也不得静默消失。
- **AI 不参与 A11 主观判断**；本计划只提供工具与判据。

---

## 三、验收矩阵（4C 收口口径）

| A | 4B 状态 | 4C 目标 | 判定 |
|---|---|---|---|
| A1 单人 3 关 | 自动化 ✅ / 真人 ❌ | 真人 30–40 分钟、0 崩溃 | 真人 + 日志 |
| A2 双人 3 关 | 冒烟 ✅ / 全量 ❌ | 双人真人 3 关 + 全队确认自动化 | 真人 + `S4C_LEGCLIENT_OK` |
| A3–A9 | 全量 ✅ | 不重跑、只保回归 | 自动化 |
| A10 单关时长 | 只记录 | 真人三关计时，中位数 8–12 | 真人计时 |
| A11 真人体验 | ❌ | 录像 + 访谈 + CSV | 只能人判 |
| A12 性能 | ✅ | 保持 `fps≥60`、`kbps≤600` | 自动化 |
| Q4 | 未写 | 交付报告显式引用 | 文档评审 |

**结论口径（定稿）**：`4C 覆盖 A1/A2/A10/A11 的真人证据与 Q4；A3–A9/A12 只保回归，不重复取新证据。`

---

## 四、回归

| 门 | 清单 | 用例数 | 终端标记 |
|---|---|---|---|
| 4A | `Tools/regression_4a_cases.txt` | 13（`-s4ashop` 重写后仍 13） | `S4A_REGRESSION_OK` |
| 4B | `Tools/regression_4b_cases.txt` | 14（`-s4bbelt` 重锚定后仍 14） | `S4B_REGRESSION_OK` |
| 4C | `Tools/regression_4c_cases.txt`（新） | 18（14 条 4B + 4 条新增：shop8/legclient/audio/continue） | `S4C_REGRESSION_OK` |
| 并集 | `Tools/regression_cases.txt` | 31（`# expected_min: 31`） | `S4_REGRESSION_OK` |

- `expected` 全部由清单行数推导，禁止硬编码（`P0_AcceptanceGate.md` §5.2 第 6 条）。
- 4C 新增 4 条与 4B 14 条并集跑，避免「4C 绿了但 4B 红了」。
- 任一既有用例漂移：先回滚，不许改基线让它过；唯一允许重锚定的是 `-s4ashop`（T12 迁移）与 `-s4bbelt`（商品化）。

---

## 五、反向验证（4C 新增，缺一即本轮证据作废）

| # | 注入 | 期望被捕获 |
|---|---|---|
| C1 | 未购买 `tool_belt` 时按 G 试图用第二槽 | 显式拒绝 `GATE_BELT_REJECT reason=locked`；不得静默忽略 |
| C2 | 未购买 `stamina_training` 时连续冲刺 | 5.0s 力竭，`pct=25.0`；不得报 37.5% |
| C3 | 安全屋只有 1/2 人 ready | 不换关 + `S4C_CONTINUE_WAIT`；一人即换关判 FAIL |
| C4 | 在 Night1 调 `ShopTerminal.ServerHandleBuy` | `wrong_scene`；场景里根本没有终端时也不得报 success |
| C5 | 四类 cue 只播同一段波形 | `distinguishable=1` → FAIL |
| C6 | 真人口述数据由开发者代填 | 访谈记录必须来自被测玩家；代填即 A11 FAIL |

---

## 六、风险

| # | 风险 | 影响 | 缓解 |
|---|---|---|---|
| R1 | 商品化把 4B 的 36.6% 体能证据打断 | 4B 回归变红 | 先重锚定 `-s4bbelt` 的「购买前 25% / 购买后 37.5%」，再改默认值；改完立即全跑 |
| R2 | `-s4ashop` 迁场景引入换关时序问题 | 4A 门红 | 复用 `-s4aloop` 已验证的「达标 → 门 → Safehouse」路径，不新造换关 |
| R3 | 全队 ready 锁存与掉线/重连冲突 | 永远凑不齐 | ready 以当前在线玩家集合为准；玩家离开即移除其 ready；断线不进分母 |
| R4 | 占位音在 40 物 + 4 怪下变成性能负担 | A12 掉帧 | 每类 cue 独立冷却 ≥0.15s；同帧最多 2 个并发 |
| R5 | 真人样本不足（<4 人） | A11 无法判定 | runbook 明确最低样本：单人 1 场 + 双人 1 场；不足则本轮不出 A11 结论 |
| R6 | 正式美术素材晚到 | 不阻塞 4C | 占位音用程序合成；模型仍用胶囊，接入规格见附录 B |

---

## 七、技术债（4C 结束仍登记）

| # | 欠账 | 何时还 |
|---|---|---|
| T1 | 正式美术/音频（用户后补） | 素材阶段 |
| T2 | 三张新图与 2400/2600/3000 配额 | P1 |
| T3 | 挑件提交（P0 有意裁剪，Q4 已记录） | P1（与多公秤一起） |
| T4 | 怪物 NavMesh / 门交互 / 完整推车物理 | P1 |
| T5 | 体能仍是客户端本地，无服务端校验 | P1 |
| T6 | 工具带第三格（P0 只到 2 格） | P1 |
| T7 | A11 只有单/双人样本，4 人未验 | P1 |
| T8 | `-s4clegclient` 是同机双进程，不是跨机 | 验收报告如实标注 |

---

## 八、待用户确认（不影响开工）

| # | 事项 | 默认 | 代价 |
|---|---|---|---|
| 1 | 商店商品化（R1） | 接受 | 未购买玩家默认少 1 个腰带槽、少 2.5s 冲刺；4B 证据重锚定 |
| 2 | 真人排期与人数（A1/A2/A11） | 等用户安排 | 样本不足则 4C 只能交自动化与 runbook，不能判 P0 |
| 3 | 正式素材交付时间 | 后补 | 占位音与胶囊先上，不阻塞 |
| 4 | 挑件不实现（R2） | 接受 | 交付报告按 Q4 引用 |

---

## 附录 A · 日志码与开关

**新增日志前缀**：`[S4C]`；新增门码 `GATE_*` 与 `S4C_*`。

`S4B_SHOP8_OK` / `S4C_LEGCLIENT_OK` / `S4C_CONTINUE_OK` / `S4C_AUDIO_OK` / `S4C_VALIDATE_OK` / `S4C_REGRESSION_OK`

| 用途 | 行格式 | 判据 |
|---|---|---|
| 场景商店 | `GATE_SHOP_SCENE night=<n> safehouse=<n>` | `night=0 safehouse=1` |
| 腰带槽 | `GATE_BELT_SLOTS before= after= owned=` | 未购 1、已购 2 |
| 腰带拒绝 | `GATE_BELT_REJECT reason=locked` | 显式拒绝 |
| 体能在档 | `GATE_STAMINA_CAP before= after= level=` | 5.0 → 7.5 |
| 全队确认 | `S4C_CONTINUE_READY leg= netId= count=` / `S4C_CONTINUE_ADVANCE leg= count=` | 全员齐才推进 |
| 音频 | `S4C_AUDIO_PLAY cue= ...` | 四类可区分 |
| 商品化汇总 | `S4B_SHOP8_OK items=8 durable=6 balanceDelta=4370 slots=2 capacity=7.5 ok=True` | 全项 |

**新增开关**：`-s4bshop8`、`-s4clegclienthost` / `-s4clegclient`（配 `-address=127.0.0.1`）、`-s4baudio`、`-s4bcontinue`
**新增工具**：`Tools/regression_4c_cases.txt`、`Tools/run_regression_4c.bat`

---

## 附录 B · 正式素材接入规格（最小清单）

> 用户后续提供素材时按此交付；4C 不阻塞。目录：`Assets/_Game/Audio/`、`Assets/_Game/Art/`。

| 类 | 规格 |
|---|---|
| 音效格式 | `.wav`，44.1kHz / 16bit / 单声道；命名 `sfx_<cue>_<variant>.wav` |
| 音效响度 | 峰值 −6dBFS，四类 cue 之间主观响度差 ≤3dB；循环类（推车轮速）首尾无缝 |
| 四类必备 cue | `sfx_cart_wheel_loop`、`sfx_heavy_drop`、`sfx_glass_break`、`sfx_curse_omen` |
| 模型格式 | `.fbx`（Unity 人体/怪物骨架）或 `.blend` 源文件；单位米、Y 轴向上 |
| 怪物动画 | 每只怪至少 `idle / patrol / chase / attack / stunned` 五段；attack 前摇与 `MonsterProfile.windup` 对时 |
| 贴图 | 2 的幂，2048 上限；URP/Lit 或 Shader Graph |
| 命名/目录 | 一物一目录；`Monster_<name>/`、`Prop_<name>/`；不得直接覆盖现有 prefab，先放 `Art/_Incoming/` |

---

## 附录 C · 与 4B 的接口不变项

- `ShopCatalog` 顺序只许追加（`ownedMask` 位序）。
- `LegContentTable` 数值不变（1800/2400/2800、12/14/16、1/3/4、0/1/2）。
- `GATE_PROPS_MODE mode=p0leg`、`GATE_LEVEL source=table` 不得被 4C 改动影响。
- 4A/4B 的终端标记与 `expected` 推导规则不变。
