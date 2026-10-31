# P0 · 交付报告（4C 收口版）

> 日期：2026-10-31　版本：v1.0-draft（自动化段已填，真人段待用户执行后回填）
> 依据：`P0_VerticalSlice.md` v1.2、`P0_AcceptanceGate.md` v1.4、`Sprint_4C_TechnicalPlan.md`
> 判定权：**P0 的放行只能由用户签字**。本报告是材料，不是签字。

## 零、结论口径（先钉死措辞）

- 当前可说：**「4C 自动化全绿 + 一局压力成立；A1/A2/A10/A11 真人门未开」**。
- 不可说：「P0 通过」「A2 通过」「一局完整可交付」。
- 自动化只证明**代码路径走过**，不证明「好玩 / 没人笑 / 时长合适」。
- 本轮唯一允许的「完成」表述：**P0 待用户签字**（签字前置条件见 §4）。

---

## 一、自动化证据

### 1.1 回归门

| 门 | 清单 | 用例数 | 终端标记 | 结果 |
|---|---|---|---|---|
| 4A | `Tools/regression_4a_cases.txt` | 13 | `S4A_REGRESSION_OK` | ✅ `passes=13 expected=13` |
| 4B | `Tools/regression_4b_cases.txt` | 15 | `S4B_REGRESSION_OK` | ✅ `passes=15 expected=15` |
| 4C | `Tools/regression_4c_cases.txt` | 18 | `S4C_REGRESSION_OK` | ✅ `passes=18 expected=18`（`RUN_DIR=...\Logs\s4_reg\周六022610-222756`；`STALE_CHECK_OK src=202610312225 bin=202610312227`） |
| 并集 | `Tools/regression_cases.txt` | 31（`# expected_min: 31`） | `S4_REGRESSION_OK` | ✅ `passes=31 expected=31`（`RUN_DIR=...\Logs\s4_reg\周六022610-223530`；`S4_REGRESSION_BREAKDOWN s4a pass=14 fail=0 s4b pass=15 fail=0 s4c pass=2 fail=0`；`s4baudio` PASS） |

> `expected` 由清单行数推导，不硬编码；`STALE_CHECK_OK` 保证跑的是「源码时间戳之后构建」的二进制。

### 1.2 4C 新增能力与证据

| Story | 能力 | 证据行 |
|---|---|---|
| S4C-1 | 商店 8/8（工具带 700 / 体能 1200 商品化） | `S4B_SHOP8_OK items=8 durable=6 balanceDelta=4370 slots=2 capacity=7.5 ok=True` |
| S4C-1 | 4B 证据重锚定（未购买 25.0% → 购买后 37.5%） | `S4B_STAMINA_DEPLETED seconds=7.50 pct=37.5` / `S4B_BELT_OK sprint=7.32 pct=36.6` |
| S4C-2 | 全队「继续」确认（三关双进程） | `S4C_LEGCLIENT_OK legs=3 dual=1 advances=3 balance=7000 ok=True` |
| S4C-3 | 夜路商店终端清零（T12） | `S4C_SHOP_TERMINAL_REMOVED scene=Night1 count=1` / `nightShop=0 safehouseShop=1` |
| S4C-4 | 四类占位音，互相可分 | `S4C_AUDIO_OK cues=4 distinguishable=4 failures=0 ok=True` |
| 装配 | 场景静态校验 | `S4A_SETUP_OK anchors=16 shopItems=8` / `S4B_VALIDATE_OK tests=14` |

### 1.3 反向验证（缺一即本轮证据作废）

| # | 注入 | 期望 | 实测 |
|---|---|---|---|
| C1 | 未购买 `tool_belt` 用第二槽 | `GATE_BELT_REJECT reason=locked` | ✅ `-s4bbelt` 购买前探针：第 1 件成功、第 2 件拒绝，`S4C_BELT_LOCKED_OK slots=1 count=1 second=rejected` |
| C2 | 未购买 `stamina_training` 连跑 | `pct=25.0`，不得报 37.5 | ✅ `S4C_SHOP8` 前置 `capacity=5.0 trained=0` |
| C3 | 安全屋 1/2 ready | 不换关 + `S4C_CONTINUE_WAIT` | ✅ 双方 `LONE` 行 + `WAIT seconds=0.0..4.0` |
| C4 | Night1 无终端买货 | `wrong_scene` | ✅ `S4B_LEG_WRONG_SCENE_PROBE reason=no_terminal_in_night` |
| C5 | 四 cue 同波形 | `distinguishable=1` → FAIL | ✅ `-s4caudioflat` → `S4C_AUDIO_FAIL`（exit=1） |
| C6 | 代填访谈 | A11 FAIL | 人工项，见 §2 |

### 1.4 正式试玩包（`-map=p0`）

| 项 | 期望 | 实测 |
|---|---|---|
| 场景列表 | Night1 + Safehouse（换关按名加载） | ✅ `BUILD_P0 formal=true test_define=off scenes=Night1,Safehouse output=Builds/P0/ScavengeNightmare.exe` |
| 测试宏 | 不带 `S4A_TEST_BUILD`（构建日志不得出现 `BUILD_DEFINES_ADDED`） | ✅ 日志无该行；`BUILD_OK ... scene=P0` |
| 跳过校验入口 | 正式包内恒拒绝 | ✅ `GrabController.ServerForceGrab` 的 `#else` 分支只打 `S4A_FORCEGRAB_DENIED reason=not_test_build` 并返回 false |
| 冒烟启动 | 进 Night1、进程存活、无异常 | ✅ `S4A_ROUTE_BOOT scene=Night1`；20s 存活、Responding=True；除「Steam 未启动」外无异常 |

> 包路径 `Builds/P0/ScavengeNightmare.exe`。真人验收一律用这个包，**不要**用 `Builds/Sprint4A`（那是带测试宏的回归包）。

---

## 二、真人证据（待填，由用户执行后回填）

执行清单：[`Playtest_4C_Runbook.md`](Playtest_4C_Runbook.md)　访谈模板：[`Playtest_4C_Interview.md`](Playtest_4C_Interview.md)

| A | 判据 | 结果 | 证据（文件 + 时间码） |
|---|---|---|---|
| A1 | 单人 3 关、30–40 分钟、0 崩溃 | __ | __ |
| A2 | 双人 3 关、全队确认每关成立 | __ | __ |
| A10 | 三关时长 6–9 / 8–12 / 10–14（原始全列） | 第 1 关 __ / 第 2 关 __ / 第 3 关 __ | __ |
| A11 | ≥30 分钟、≥3 自主意外、≥2 共同笑、≥50% 再走一关 | 意外 __ / 共同笑 __ / 再走 __% | 访谈表 + 录屏 |
| A12 | 双人 fps≥60、单客户端 ≤600kbps | __ | `NetworkDiagnostics` |

> 空着就是没跑。**不许用自动化结果填这一栏**（那属于伪造证据）。
>
> 若未执行：本报告结论固定为 **「P0 待用户签字 · 真人门未开」**。

---

## 三、Q4 条款（必写，不得省略）

**取消「挑件提交」= 对 17.6 的有意裁剪。**

- 依据：`P0_VerticalSlice.md` v1.2 §七-16 与 `P0_AcceptanceGate.md` §2.2 Q4。
- 裁剪形态：公秤改为**整车/整批提交**（4A-S4 已定稿），不再要求玩家逐件挑选。
- 决策替代：原本由「挑件」承载的「收手 vs 贪心」，改由 **达标即开门 + 再贪一趟** 承担，可验形态 = Q1（达标瞬间开门 + 缺口提示消失）与 Q2（再贪期间余额不掉）。
- **措辞纪律**：
  - ✅ 写「有意裁剪，替代机制为 Q1/Q2」；
  - ❌ 不得写「挑件提交未实现」（这是功能缺失的说法，与事实不符）；
  - ❌ 不得从技术债列表里静默删除。
- 用户确认状态：**待用户确认**（沿用 `P0_AcceptanceGate.md` §十 #14）。

---

## 四、阻断项核对（任一失败即 P0 FAIL）

| # | 阻断条件 | 本轮状态 | 证据 |
|---|---|---|---|
| X1 | 钱会丢或会复制 | 自动化未见；真人段待验 | `S4A_DEPOSIT_OK` / `S4A_SAVE_OK` / `S4C_LEGCLIENT_OK balance=7000` |
| X2 | 单人不可完成 | 自动化路径可完成（`-s4aloop` / `-s4acart*`）；真人 A1 待验 | 4A 13 条 |
| X3 | 推车成为强制撤离容器 | 4A 已证徒手可搬 35kg（`S4A_CART_HEAVY_OK`） | ✅ |
| X4 | 无预警攻击 / 无解诅咒 | 4B 诅咒前兆 1.5s 已实现且有 `cue=curse_omen lead=1.5` | ✅ 代码路径 |
| X5 | 性能崩 | `S4B_PERF_OK fps=120`；双人帧率待测 | 部分 |
| X6 | 测试本身无效（C1–C3 未捕获） | C1/C3/C4/C5 均已捕获 | ✅ |
| X7 | 强杀证据不合格 | 4A 强杀证据保留（`Tools/s4a_save_suite.sh`） | ✅ |
| X8 | 卡死三态第 3 种 >0 | 自动化 0；真人待记 | 部分 |
| X9 | 身份污染 | 所有夹具 `TraitSettings.ForceNoTraits=true`，`traits=none` 前置行在 | ✅ |

---

## 五、已知缺口（如实列，不得美化）

| # | 缺口 | 影响 | 归属 |
|---|---|---|---|
| G1 | 正式美术 / 音频未接入（占位音仅四类） | 不影响 P0 判定，影响观感 | 待用户提供素材 |
| G2 | 只有一张 Night1 图，L1/L2/L3 共用（配额 1800/2400/2800） | A10 时长可能偏短 | P1 三图 |
| G3 | 无封印系统；`CURSED_TEAM` / `CURSED_ZONE` / `SEALABLE` 不覆盖 | 规则缺口 | P1 |
| G4 | ~~`regression_4c_cases.txt` / `run_regression_4c.bat` 未建~~ → **已建**（18 行，`S4C_REGRESSION_OK passes=18 expected=18`） | 无（4C 独立门已成立） | 已收口 |
| G5 | ~~C1（第二槽 `reason=locked` 显式拒绝）无独立断言~~ → **已补**（`-s4bbelt` 购买前探针 + `S4C_BELT_LOCKED_OK`，不占门行） | 无（C1–C6 全部有证据） | 已收口 |
| G6 | 推车轮声仅服务端播放 | 客户端听不见 | 正式素材阶段一并收 |
| G7 | 双人仅同机/虚拟局域网，未做跨机公网；延迟只覆盖 ~200ms 档 | A12 外推，非实测 | 记录口径 |
| G8 | 未验四难度、真刚体侧翻 | 范围外 | — |
| G9 | 「挑件提交」按 Q4 裁剪（见 §3） | 有意裁剪，非缺失 | 已确认口径 |

---

## 六、签字栏

```
自动化：□ 4A 13/13  □ 4B 15/15  □ 4C 18/18  □ 并集 31/31  □ C1–C6
真人：  □ A1  □ A2  □ A10  □ A11（+ 访谈表原件）
文档：  □ Q4 条款（§3）  □ 已知缺口（§5）
结论：  □ PASS   □ CONCERNS（需修：______，复测：______）   □ FAIL（阻断：______）
QA（AI 侧材料汇总）：__________      用户签字：__________      日期：______
```

> QA 汇总为 **advisory**；用户签字前，P0 不算放行（`P0_AcceptanceGate.md` §8.3）。
