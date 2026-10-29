# P0 验收门与 Sprint 4A 测试计划

> 版本 v1.4 · 2026-10-30 · QA：严守真（quality-lead）
> **v1.4（冻结版）**：按 team-lead 冻结口径收口，不再扩展章节。变更点：① §十一 旧差异结论全部改为「已闭环」（C1/C2/C5/C7 不再写成未决）；② 窄门推车待定项关闭 → B5 断言改为「空车展开可过 → 满载超宽被明确挡住 → 卸货后可过」，删「窄门必折叠」；③ B7 按 HK-14 真实语义改写（4A 只能测「client 断开重连到仍在运行的同一 host」，不含存档恢复）；④ D6.5/D9 采用工程新阈值（0.40/0.60m、≤500kbps）；⑤ 过关额改表驱动并填 1800/2400；⑥ A4 唯一口径 1.5s + 自动脱困（无 5s 分支）；⑦ S1b 改为「删 bootstrap 或强制 no-traits 二选一」；⑧ 清掉全部 `18 锚点 / 勾选 / 6 件商品 / 5s / 1.0s 提交` 残留。
> **v1.3 自纠（保留，防再犯）**：「工程 §5.3 仍是 6 件」与「4A 用 18 件临时池」两处均为旧版内容——6 件是 `ShopCatalog.cs` 的 v1.0 类注释残留（已要求删除），18 是**被删掉的继承锚点数**。教训见 §5.2 第 13 条。
> 依据：`Docs/P0_VerticalSlice.md` v1.2、`Docs/Sprint_4A_TechnicalPlan.md` v1.2、`Docs/M1_Acceptance.md`、`Docs/DevLog.md`（D1–D13）、`Docs/Props_D13.md`、`Docs/Maps_D12.md`、`Docs/Playtest_D7.md`
> 性质：**验收门定义 + 测试计划**。不含实现方案，不含测试代码。
> 标记：`†` = 目标值实测调整｜`◆` = 已锚定不得漂移｜`⧗` = 现存文档均未定，**不臆造**

---

## 零、结论先行

| 项 | 内容 |
|---|---|
| **4A 门当前状态** | **NOT READY（无证据，非 FAIL）**——不是测试没过，是还不能开测 |
| **已识别的最大假通过风险** | 旧 `EvacSettlement.Deliver()` 结算后立刻 `Destroy` → 「重复提交」物理上不可能发生，**看起来 A6 天然通过，实际是根本没验到**。判据必须是「同 `cargoId` 第二次提交被**显式拒绝**且 `dupBlocked ≥ 1`」 |
| **第二处假通过风险** | 工程 `-s4asave-kill` 若只用**进程内** `Environment.Exit(137)`，会跑到托管清理——若实现里存在「退出时统一刷盘」，测试**通过得很漂亮，但通过的是被禁止的实现**（17.4 明令不许退出时刷盘）→ 强杀必须外部 `taskkill /F /PID`，且**两种都跑、结果不一致即 FAIL**（§八 X7） |
| **v1.3 已关闭的旧冲突** | 商店件数、锚点池、D4 基线、长按时长、过关额口径、强杀实现——7 处全部关闭（§十一 11.1）。**其中 2 处是我自己判错**（拿 grep 命中的类注释残留当依据），已记为 §5.2 第 13 条 |
| **唯一新增的设计裁剪** | **P0 取消公秤挑件提交**（规格 v1.2，design 判定原「留一部分冲下一关」是伪选项：提交无税无上限，留货零收益只有风险）。原决策改由「达标即开门 vs 再进去贪一趟」承担 → 门新增 **S8 / Q1 / Q2** 补上它的可验形态 |
| **第三处（v1.2 新增）** | Night1 若继承 `TraitBootstrap`，身份/缺点牌会抽到倍率，**3.68 m/s、42kg 减速、物品平衡全部被污染** → 门里必须有「P0 中性身份已启用」硬断言（4A-S1b） |
| **4A 门性质** | 阶段门。证明**单人最小闭环走通 + 1→2 关资金继承 + 双人最小冒烟 + 钱不丢不复制** |
| **P0 门性质** | 产品交付门。A1–A12 全量 + 7 条失败条件 + 真人 A11。**4A 通过 ≠ P0 通过** |

---

## 一、质量门定义

### 1.1 PASS / CONCERNS / FAIL

| 判定 | 成立条件（全部满足） | 处置 |
|---|---|---|
| **PASS** | ① 适用阻断项 **0 失败**；② 适用 A 项全 PASS 或有用户签字豁免；③ 证据齐全且**第三条独立的人能照报告复现**；④ 反向验证 **3/3 捕获**；⑤ 继承回归全过（阻断级） | 允许进入 4B / 允许对外演示 |
| **CONCERNS** | ① 无阻断项失败；② 命中下列任一：1 项阻断项证据缺失但**已排复测日期** / 落在阈值边缘带 / ≥1 项 Major 未修但已隔离 / 继承回归有**告警级**漂移且已解释 | 可带条件进入，报告须写修复窗口与复测日期。**不得对外演示** |
| **FAIL** | 命中任一：任一阻断项失败 / 证据缺失导致无法判定 / 反向验证未捕获（测试本身失效） | 回修，不进 4B |

**三条硬规矩**

1. **缺证 = 不通过**，不是「默认通过」。M1 因第 5 条无证据就不打 tag，同一标准适用。
2. **阈值边缘带**（落带内记 CONCERNS 而非 PASS）：**A4 单次卡死 1.2–1.5s**｜A10 中位数落在区间外 1 分钟内｜A5 楼梯循环 75–90s｜A12 帧率 55–60fps。
3. **卡死三态不可混记**（v1.2，按规格 2.7）：① 1.5s 内自行脱出 = **PASS**；② 靠脱困（长按 E 1.0s）恢复 = **CONCERNS + 计数**；③ 脱困也无法恢复 = **FAIL**。日志必须带 `rescued=` 区分「没卡」与「卡了但救回来了」。

### 1.2 4A 阶段门 vs P0 完整门

| 维度 | **4A 阶段门** | **P0 完整门** |
|---|---|---|
| 回答的问题 | 钱能不能落盘、能不能把一车货换成钱再花掉、第二个进程能不能一起玩 | 这一局值不值得玩 |
| 覆盖 A 项 | A3 / A4 / A5 / A6 / A8（3/4 场景）/ A12（单人基线）｜A10 **只记录不判定** | A1–A12 **全量** |
| 人数 | 单人（host 单进程）+ **双人最小冒烟 D1–D5** | 单人 + 双人 **完整 3 关**（A2） |
| 关数 | 第 1 关 → 第 2 关（`-s4aloop`，证明资金继承） | 3 关连续 |
| 货物池 | **16 锚点 / 12 件池 / 刷 16 件**（`-anchors=<N>` 缺省 16） | 三关各 12 / 14 / 16 锚点、不同过关额与诅咒（P0 §3.3） |
| 商店 | **4 件具名耐用品**（手套/布带/手电/握力训练） | 8 件全量 |
| 判定方式 | 自动化为主 + 1 段录屏 | 自动化 + 录屏 + **真人访谈**（A11 只能人判） |
| 签字 | 用户审批 4A 结论表 | 用户审批 P0 验收报告 |
| 后续 | → 4B：噪音 + 盘点员 + 2 诅咒 + 团灭场景 | → 4C：3 关连续 + A11 真人局 + A12 双人带宽 |

> 4A 结束时 **D3 四人连机回归必须仍通过**——4A 改 `NetworkManager` / `PropSpawner` / prefab 的风险最高，地基不能被踩塌。

---

## 二、A1–A12 验证矩阵

判定角色：**自动** = 批处理断言｜**QA** = 我据日志/文件评审｜**真人** = 非开发者第二人｜**用户** = 放行签字。

| A | 指标（v1.1 口径） | 判定方式 | 证据 | 4A 覆盖 | 谁判 |
|---|---|---|---|---|---|
| A1 | 单人连续 3 关 | 半自动 + 真人 | 日志 + 录屏 | 不覆盖（4A 只验 1→2 关） | QA + 真人 |
| A2 | 双人连续 3 关 | 半自动 + 真人 | 日志 + 录屏 + 同机/跨机标注 | **不覆盖全量**；4A 只做 D1–D5 冒烟 | QA + 真人 |
| A3 | 徒手路径成立（≥1 件 ≥35kg 不用推车搬出） | 自动事件流 → **4A 降级为录屏人工判定** | 录屏（`via_cart` 事件无钩子，见 §十一 HK-12） | 覆盖（人工） | QA + 真人 |
| A4 | **≥110kg 且单层不堆叠**（保险箱60+雕像42+电视12=114kg），连续推行 ≥30m，**单次卡死 <1.5s** | 自动（位移 + 停滞计时） | `GATE_CART_STALL duration= mass= count= where= rescued=(self\|unstick\|fail)` + 逐秒位移采样 | 覆盖 | 自动 + QA |
| A5 | 折叠→搬车→上 12 级→展开→重装 ≤90s†（**车不上台阶，是折叠搬运**） | 录屏手工计时（无计时钩子） | 录屏 + 时间戳 | 覆盖（人工计时） | QA |
| A6 | `accepted == uniqueCargo` ∧ `actualSum == expectedSum`（读 `CurrentValue`，整型求和，误差 0）∧ **`dupBlocked ≥ 1`** | 自动 | `S4A_DEPOSIT_OK`（带 cargoId/value/balance）+ 钱包差 | 覆盖（核心） | 自动 + QA |
| A7 | 两诅咒各触发 ≥1、同人双持仅 1 生效、未封印 60% 计价 | 自动 + 录屏 | 日志 + HUD 录屏 | 不覆盖（4B） | QA + 真人 |
| A8 | 4 场景余额差额 = 0，**但证明力不等价**（见 §2.1） | 自动 | 钱包文件 + `S4A_SAVE_VERIFY_OK` + 事务回放 | **覆盖 3/4**（团灭归 4B） | 自动 + QA |
| A9 | 噪音可归因，无来源调查 = 0 | 半自动 + 录屏 | 录屏逐帧 + 噪音事件 | 不覆盖（4B） | QA + 真人 |
| A10 | 单关中位数 8–12 分钟† | 自动计时 | 原始样本全列 | **只记录不判定**：① 4A 无怪物 → 追猎窗口缺席，8–12 分钟的主要驱动力不在场；② 4A 单一关配置，无 12/14/16 三档梯度。**且不得作为 P0 节奏证据被引用** | QA |
| A11 | 真人：≥30 分钟、不看攻略、≥3 个自主意外、≥2 次可归因共同笑、≥50% 选「再走一关」 | **只能人判** | 录像 + 5 分钟访谈 + `PlaytestRecorder` CSV | 不覆盖（4C） | 真人（+ 用户） |
| A12 | ≥60fps@1080p、单客户端 ≤600 kbps† | 自动采样 | `NetworkDiagnostics` 帧率/kbps | 部分覆盖（单人基线） | 自动 + QA |

### 2.1 A8 场景证明力（不许把「重连通过」当安慰性勾）

| 场景 | 证明什么 | 强度 | 4A |
|---|---|---|---|
| **强杀（外部 `taskkill /F`）** | **存档不丢的唯一强证据** | 唯一强证据 | 必做 |
| 正常退出 | 正常路径落盘 | 中 | 必做 |
| 重连 | **只证明「客户端无本地账本」**（钱只在服务端 + 磁盘），**不证明存档可恢复** | **弱，不得替代强杀** | 必做 |
| 团灭 | 4A 无怪物/无倒地，概念不存在 | — | 归 4B |

**AI 不参与 A11 的主观判断**（沿用 M1 结论）。

### 2.2 补充验收项（弥补 C1 取消项，P0 门必验）

P0 v1.2 取消了「挑件提交」，原由它承载的「收手 vs 贪心」决策改由**达标即开门 + 再贪一趟**承担（P0 §四）。该决策的唯一可验形态如下，A1–A12 里**没有**对应项，必须单列：

| # | 判据 | 4A 可验 | 归属 |
|---|---|---|---|
| **Q1** | **达标瞬间 `LevelPortal` 必须解锁**（`legProgress ≥ legQuota` → 门开），且 HUD 显示「还需提交 <缺口> 价值」在达标后消失 | 是 | 4A（新增 4A-S8） |
| **Q2** | **再进去贪一趟期间已入账余额不掉**（余额只增不减，与是否继续搜刮无关） | 是 | 4A（新增 4A-S8） |
| **Q3** | **未提交的货在团灭时确实丢失**（车上与手持的货不折算成钱） | 否（无怪物→无团灭） | 4B |
| **Q4** | P0 交付报告必须**显式引用**「取消挑件提交 = 对 17.6 的有意裁剪」，既不得从技术债列表里静默消失，也不得被写成「未实现功能」 | 是（文档项） | P0 交付报告 |

> 若 Q1/Q2 不可验，说明「收手 vs 贪心」这条支柱在 P0 里没有任何证据——那是比挑件提交缺失更严重的问题。

---

## 三、4A 最小闭环门

### 3.1 链路与判据

| 步 | 动作 | PASS 判据 | 证据 | 级别 |
|---|---|---|---|---|
| **4A-S1** | 单人进图（`Night1`，host 单进程） | 场景名 = `Night1`（`-map=sprint4a`；`Supermarket` 只作 D12/D13 回归基线，**不得写进 4A 证据的主场景**）；1 名玩家生成；**16 锚点 / 12 件池 / 刷 16 件货**；**类型构成必须逐类断言**（只断言「刷了 16 件」不算通过，见 §十 #12）；0 异常 | 启动参数 + 场景断言 + `S4A_VALIDATE_OK anchors=16` + 逐类计数 | 阻断 |
| **4A-S1b** | **身份中性** | **二选一且必须日志可见**：`S4ASceneSetup` 删掉 `TraitBootstrap` 根对象 **或** 启动强制 `TraitSettings.ForceNoTraits = true`（**不得要求一个已删 `TraitBootstrap` 的场景再去读它的 flag**）；`S4A_VALIDATE_OK … traits=none`；4 个乘子全 1.0；`Main`/`Supermarket` 的 D8 路径**保持原样** | 中性身份断言行 + 倍率打印 | **阻断** |
| **4A-S2** | 搬货（≥3 件，含 1 件 ≥35kg） | 抓取/负重减速生效（≥35kg 移速明显下降，非空手同速）；拖运无穿模 | 抓取/移速日志 + 录屏 | 阻断 |
| **4A-S3** | 装车 + 推行 | 车可展开；装货成功；**载货 ≤6 件†、载重 120kg†（超载判定容差 +0.5kg）**；卡死 <1.5s 且按三态记录 | `S4A_CART_OK count= mass= ids=` + 位移采样 | 阻断 |
| **4A-S4** | 公秤提交（停车位 → 车 → 长按确认） | **长按 1.2s†（规格 §四/§5.2 与工程 §3.7 已统一）**；**整车提交 = P0 既定设计裁剪（v1.2），不是 4A 临时将就**；逐件计价，`actualSum == expectedSum`（读 `CurrentValue`，误差 0）；`dupBlocked ≥ 1` | `S4A_DEPOSIT_TALLY accepted= uniqueCargo= expectedSum= actualSum= dupBlocked= ok=` | 阻断 |
| **4A-S5** | 永久资金落盘 | 提交后立刻写盘（**不许退出时统一刷盘**）；落盘值 = 内存值；重启读回一致 | 钱包文件 + `S4A_SAVE_OK txn=` + `S4A_SAVE_VERIFY_OK` | 阻断 |
| **4A-S6** | 安全屋购买 | 场景名 = **`Safehouse`**；**4 件且仅 4 件上架**（来源 `P0_VerticalSlice.md` §2.5 的 4A 批次列，见下表）；余额 = 前值 − 价；余额不为负；**每件都能被自动断言**（买了看不出差别 = 不通过） | `S4A_SHOP_OK` + 钱包差 + 截图 | 阻断 |
| **4A-S7** | 进第 2 关（**必须保留，不许降为可选**） | `-s4aloop` 连跑 2 关：`legIndex=2`；**过关额走表**（第 1 关 **1800**、第 2 关 **2400**），`GATE_LEVEL source=table`（出现 `S4A_QUOTA_FALLBACK` 即说明走了 ×0.55 回退，判不合格）；资金带入第 2 关且不清零（`balance` 单调不减）；`dupBlocked ≥ 1` | `S4A_LOOP_OK legs=2 balance= dupBlocked=1` + `GATE_LEVEL n= quota= source= before= after=` | 阻断 |
| **4A-S8** | **达标开门 + 再贪不掉账**（弥补取消挑件提交） | `legProgress ≥ legQuota` 瞬间 `LevelPortal` 解锁；HUD「还需提交 <缺口>」消失；**再进图贪一趟期间已入账余额不掉、不重复入账** | `GATE_QUOTA_GATE leg= progress= quota= portal=unlocked carry_over=` + 余额前后 | 阻断 |

**4A 商店 4 件（来源 P0 §2.5，价格与断言逐件固定）**

| id | 名称 | 类 | 价 | 自动断言 |
|---|---|---|---|---|
| `grip_gloves` | 防滑手套 | 耐用品 | 400 | 抱 42kg 雕像冲刺 3s 不脱手（无手套必脱手） |
| `packing_tape` | 打包布带 | 耐用品 | 500 | 同一急转路径掉货 0 次（无布带 ≥1） |
| `flashlight` | 强光手电 | 耐用品 | 350 | `spotLight.activeInHierarchy` 随 F 切换；**不改变怪物发现距离**（4B 验） |
| `grip_training` | 握力训练 | 永久 | 900 | 42kg 携带速度相对 D11 `-m1weight` 基线 +15%（±3%），且不超携带速度上限 |

> 4B 才上的 4 件（自救针/急救包/工具带/体能训练）分别依赖倒地、受伤、工具槽、耐力系统——**4A 硬上就是假购买**。肩带/背负架/放大镜已**移出 P0**（需肩扛背负姿态系统 / 依赖未鉴定估价）。

### 3.2 双人最小冒烟（4A-D，与 A2 全量区分）

只证明「双人代码路径被走过」，**不**证明双人可玩 3 关（那是 A2 / 4C）。

| 步 | 判据 | 证据 | 级别 |
|---|---|---|---|
| **4A-D1** | host + 1 client 同场：`conns=2 players=2`，两进程都在 `Night1` | `-s4aloop` + `-s4aloopclient` 日志 + 场景断言 | 阻断 |
| **4A-D2** | **同余额**：client 读到的 balance == host 落盘值（客户端无本地账本，全量 SyncVar 下发） | 双端余额行 + 钱包文件 | 阻断 |
| **4A-D3** | **同进度**：`legProgress` 双端一致 | 双端进度行 | 阻断 |
| **4A-D4** | **client 购买生效**：client 发起 → 服务端成交 → 双端余额一致 + 效果在 client 本地可见（如手电 F 开关） | `S4A_SHOP_OK` + 双端余额 + 截图 | 阻断 |
| **4A-D5** | **client 在 `SAVE_LOCKED` 只读模式下仍可提交/购买**（提交与购买经 host 执行），且 HUD 不得出现「无法游玩」类阻断提示 | `S4A_SAVE_LOCKED` + 后续成功提交/购买 | **阻断（v1.2 新增）** |

> D5 的理由：`SAVE_LOCKED` 是同机双开的正常结果（第二个进程拿不到 `.lock`）。若它顺带把 client 的提交/购买也锁了，**双人一开就废**——这是设计与实现的交界处，必须验。

### 3.3 4A 明确不覆盖（写死，防范围膨胀）

| 不做 | 归属 |
|---|---|
| 盘点员七状态、攻击前兆、追逐速度、噪音 0–100 与 HUD 归因 | 4B（A9） |
| 笑面偶 / 无字牌位 / 60% 折价 / 封印 | 4B / P1（A7） |
| 团灭后余额 | 4B（A8 场景 1） |
| 双人完整 3 关、全队「继续」确认（4A 是房主代决） | 4C（A2 / S6） |
| **真刚体侧翻**（v1.1 R1 已降 P1，A 系列无此验收项） | P1；4A 用「重心偏移 → 转向变慢 + 超载甩落倾向」替代 |
| 四难度 / 正式美术音频 / 手柄与键位 | P0 不做 |

### 3.4 执行顺序

```
① 静态校验（S4ASceneSetup.Validate / D12 / D13 / D9StabilitySetup）
② 单人闭环 S1 → S8（`-s4adeposit` / `-s4asave` / `-s4acart` / `-s4ashop` / `-s4aloop`）
③ 双人冒烟 D1–D5（-s4aloop + -s4aloopclient）
④ 边界 B1–B7（每项独立起进程）
⑤ 反向验证 R1–R3（3/3 必须捕获）
⑥ 继承回归 D3/D4/D6/D8/D9/D12/D13
⑦ 真人计时 5 次单人跑（A10 只记录）
⑧ 出判定表 → 用户签字
```

任一步 FAIL 即停，**不跳过前面跑后面**（否则回归问题会被误判成新功能 bug）。

---

## 四、必测失败 / 边界用例

| # | 用例 | 前置 / 步骤 | 预期 | 证据 | 级别 |
|---|---|---|---|---|---|
| **B1** | **重复提交**（同 `cargoId`） | 1 件已确认成功 → 立即重放同 `cargoId` → 第三次长按 | 第 2/3 次**显式拒绝**（`S4A_DEPOSIT_DUP`）且打印**前后余额**；资金只 +1 次；进度只 +1 次；`dupBlocked ≥ 1` | `S4A_DEPOSIT_TALLY`（`accepted == uniqueCargo` ∧ `actualSum == expectedSum` ∧ `dupBlocked ≥ 1`）+ `S4A_DEPOSIT_DUP` + 钱包差 | 阻断（A6） |
| **B2** | **重启 / 强杀 + 三级备份链**（4 子场景） | (a) 正常退出 (b) **外部 `taskkill /F`**（TerminateProcess，不跑托管清理）(c) 购买后 2s 外部强杀 (d) **三级链**：先写坏 `sav` → 验回退 `bak`；**再写坏 `bak`** → 验回退 `bak2`；全坏 → `corrupt-<ts>.sav` 留证 + 新建空档（另跑 `Environment.Exit` 对照，结果须与 b 一致） | **重启后**读回余额 = 已确认事务之和（delta 0），不多不少不半笔；三级链每级都能回退且**旧文件绝不覆盖**；全坏时 `S4A_SAVE_CORRUPT_RECOVERED` 且 HUD 亮红 | `S4A_SAVE_VERIFY_OK txn= dup= balance= sumCommitted=` + `GATE_WALLET expected= actual= delta=` + sha256 对比 | 阻断（A8） |
| **B3** | **购买撤销**（唯一退款路径） | 余额 X → 买 A → 撤销 → 余额应回 X → 再买 A → **连点撤销 3 次** → 离开安全屋 → 再试撤销 | 撤销窗口 = `SafehouseSession.phase == Browsing`；窗口内精确回 X；**连点不得二次退款**（`state` 已 `Revoked`，第 2 次必须被拒）；离开后入口消失**且服务端拒绝**；余额不为负 | `S4A_SHOP_REVOKE` / `_REJECT state=`（码需补）+ 钱包差 + 盘上 `purchases[].state` | 阻断（A8 复制面） |
| **B4** | **满载推车楼梯** | **≥110kg 且单层不堆叠**（114kg / 3 件为首选组合）→ 折叠 → 搬车 → 上 12 级 → 展开 → 重装 → 下楼梯 | 单次 ≤90s†；**件数不设下限**（件数不是 A4/B4 的验收目标）；重装后件数一致或掉落有日志可解释；不穿模；**卡死按三态记**（`rescued=self` 自行脱出 / `rescued=unstick` 脱困救回 / `rescued=fail` 救不回） | `GATE_TIMER tag=stair_cycle ms=` + `GATE_CART_STALL`（带 `rescued=`）+ 录屏 | 阻断（A5） |
| **B5** | **窄门与推车**（口径已定，不要求折叠） | ① 空车展开（≈0.75m）**必须直接推过** 1.2m 窄门；② 装货后有效宽度因外伸 >1.2m → **必须被明确挡住**并提示「卸货 / 调整货物」；③ 卸至可过 → 通过 → 门外重装；④ **折叠只允许**在楼梯、夹层或明确净宽不足处触发 | ①不得强制折叠、不得减速到卡死；②③反馈可预期、无静默卡死；④平直窄门处不误触发折叠；单人全程 ≤120s†；卡死 >1.5s 必须触发脱困且救得回 | `GATE_TIMER tag=unload_narrow ms=` + `GATE_CART_STALL` + 录屏 | 阻断（反证 X3） |
| **B6** | **双击 / 连点购买 + 余额边界** | 余额恰好 = 商品价 → 同帧双击 + 连点 5 次 → 再买一件（不足）→ 已拥有的耐用品再买 | 只成交 1 次；余额归 0 不为负；`insufficient` 被拒；**4A 商品全是耐用品 → 硬上限的唯一形态 = `AlreadyOwned` 二次被拒**（4A 无「每关限量消耗品」） | `S4A_SHOP_OK` 序列 + 拒绝原因 + 钱包差 | 阻断（A8 + 硬上限） |
| **B7** | **客户端断线重连**（4A 真实语义） | 提交后**外部杀客户端进程**或真实断网（KCP 3s 预警 / 4s 断线）→ client 重连到**仍在运行的同一 host** → 校验余额 → 再提交 1 件 | `GATE_WALLET_SYNC match=true`（重连后仍成立）；**断线期间无重复入账**；余额 = 断线前。**注意**：4A 的 host 不重启，故本项**不涉及存档恢复**——「钱不丢」只由 B2b/B2c 强杀证明；「断线续玩后余额一致」属 4B | `GATE_WALLET_SYNC` + 双端余额 + 钱包文件 | 阻断（A8 场景 3，**弱证据**） |

> **B7 语义边界（按 HK-14）**：4A 无断线续玩机制（D9 的「返回大厅」是重开一局，房主迁移 LOCKED），能测的只有「client 断开 → 重连到仍在运行的同一 Host」。**不得把本项当成存档可恢复的证据**，也不得因「续玩后余额一致」在 4A 缺失就判 FAIL。
>
> **B7 撤回 v1.1 的「复用 D9 冻结」**：`-d9dfreeze` 是主线程 `Thread.Sleep`，**进程和内存都活着**，对测**存档**完全无效。测存档只能用杀进程或真断网；冻结注入只能验「连接判定」，不能验「钱还在不在」。

**B1 / B2 / B3 的通过判据一律是「数值差 = 0」，不是「没有报错」。**

---

## 五、反向验证（防假通过）

### 5.1 三处故意注入（全部用现有命令，不假设新接口）

| # | 注入 | 期望被捕获的信号 | 未捕获的判定 | 恢复 |
|---|---|---|---|---|
| **R1** | 故意对同一 `cargoId` 提交两次（**由 `-s4adeposit` 内置一次重复满足**，条件见下） | `S4A_DEPOSIT_DUP` + **前后余额**（余额只 +1 次） | 无 DUP 行 → 去重不存在；有钱包多入账 → 事务不幂等。**判「测试无效」，本轮不得记 PASS** | 重跑 `-s4adeposit` |
| **R2** | 让钱包写盘失败一次：`-s4asave-failio`（写 tmp 前抛一次 IO），**并另跑一次** `-savedir` 指到只读目录 | `S4A_SAVE_FAIL reason=(io_error\|no_space\|nonatomic_fallback\|locked\|schema)`；**内存回滚到事务前**；盘上 sha256 未变；重启后余额 = 事务前 | 余额「加了一部分/双倍」或「静默成功但文件未变」→ 原子性不存在 | 去掉开关重跑 B2 |
| **R3** | 故意制造服务端/客户端余额不同步：`-s4aloop -s4asyncbreak` | `GATE_WALLET_SYNC server= client= match=false` 并触发以服务端为准重同步；**去掉开关后必须 `match=true`** | 探针报 `match=true` 而实际两侧不同 → **探针本身无效，比缺陷更严重**；或抑制开关无效（加了还是 true）→ R3 未真正注入 | 去开关复验 `match=true` |

**R1 的注入条件**（否则不认）：`-s4adeposit` 内置重复必须同时打印 `S4A_DEPOSIT_DUP` **与前后余额**。只打拒绝码证明不了「钱没多入账」。

**R3 已可用**：工程 v1.2 提供 `-s4asyncbreak` + `GATE_WALLET_SYNC server= client= match=`。**判定要点**：抑制开关必须真的让 `match=false`；若加了开关仍是 `true`，说明注入没生效，R3 作废（同 D11 只改 `nm.transport` 不改 `Transport.active` 那类假通过）。

判定：**R1–R3 全部捕获才算「测试有效性」成立**；任一未捕获，门判 FAIL，原因写「测试无效」，不写「功能通过」。

### 5.2 假通过模式清单（本项目真实踩过 + v1.2 新增）

| # | 假通过模式 | 出处 | 禁用规则 |
|---|---|---|---|
| 1 | 只断言「按钮/长按条存在」 | 通用 | 必须**状态迁移 + 数值差 + 落盘一致性**三件套 |
| 2 | 用 `Destroy` 规避重复提交 | `EvacSettlement` 现状 | 断言「第二次被**显式拒绝**」，不接受「物件不存在所以没重复」 |
| 3 | 只改组件字段不改 `Transport.active` | D11（注入 200ms 测出 rtt=24ms） | 网络探针**先断言注入生效**，再测目标指标 |
| 4 | 挪玩家不挪货 → `breakDistance` 首帧判脱手 | D11 重量探针、D13 结算 | 瞬移必须**连货一起挪**，并断言手持仍成立 |
| 5 | 按采样序号对齐两端日志 | D6.5「同刻差 8.67m」错误结论 | 只用 UTC `epoch_ms` 墙钟对齐 |
| 6 | 期望值硬编码 | D13「改表即生效」的教训 | 期望值从**数据表/落盘文件**读 |
| 7 | 断言「总额 > 0」代替逐件求和 | — | 必须逐件求和比对（误差 0）并列出逐件行 |
| 8 | 跑在 `Main`/`Supermarket` 却记成 4A 结果 | D12 明确分离场景 | 每条证据必须打印场景名并断言 = **`Night1`/`Safehouse`** |
| 9 | 只报中位数/最小值 | M1「只测 200ms 一档」 | 计时与性能**原始样本全列** |
| **10** | **用进程内 `Environment.Exit` 冒充外部强杀** | v1.2 新识别 | 强杀必须走 `taskkill /F`；内外部结果不一致 → FAIL（存在退出刷盘） |
| **11** | **把「卡死但被脱困救回」记成 PASS** | v1.2 新识别 | 三态分记，救回 = CONCERNS 并计数 |
| **12** | **在带身份/缺点牌的环境下测 3.68 m/s 与 42kg 减速** | v1.2 新识别 | 必须先断言「中性身份已启用」且 4 个乘子全 1.0 |
| **13** | **拿 grep 命中当口径依据**（v1.3 自纠） | 我用 `grep "6 件"` 命中工程第 113 行的类注释残留 `ShopCatalog.cs｜商品表（6 件）`，据此判"工程仍是 6 件"——**表本身已是 4 件** | 口径必须从**表格本体 / changelog / 完成定义**读，不从注释、摘要行、他人转述读；凡断言某文档"仍是某值"，须同时报出行号与所在小节标题 |

### 5.3 断言最低标准

不满足以下三条的自动用例不计入 4A 证据：

1. ≥1 条**状态迁移断言**（如 `Pending → Committed`，不接受静态存在性）；
2. ≥1 条**数值差断言**（delta 必须打印）；
3. ≥1 条**落盘/服务端权威一致性断言**（内存 vs 文件 vs 客户端，至少两方比对）。

---

## 六、现有回归继承清单

级别：**阻断** = 失败即门 FAIL｜**告警** = 需在报告解释并给重锚定结论。

| 来源 | 命令 / 入口 | 期望标记（现行基线） | 级别 | 备注 |
|---|---|---|---|---|
| D3 | `-d3server` + 4×`-d3client`（客户端 IP/端口参数 `⧗`） | `conns=4 players=4`，异常 0 | 阻断 | **同机 4 进程**，非跨机 |
| D4 | `-d4test`（**必须带 `-feel=light`**） | `D4_GRAB_OK` / `D4_FOLLOW_OK propMoved=1.45` / `D4_THROW_OK speed=4.14` / `D4_TUNNEL_OK closestSide=-0.40` | 阻断 | 基线 = **4.14**（70×1.30÷22）。`3.18` 是 D5 前默认值，**不要混用**。⚠ 工程 §8.1 写 3.18 → 会把通过误判 FAIL（§十一 C3） |
| D5 | `-feel=light` 锁定 + `D5FeelSetup.Validate` `⧗` | `D5_VALIDATE_OK schemes=3 moveSettings=true playerWired=true` | 告警 | 4A 全部跑测**必须锁 light**，否则数字键热切污染数据 |
| D6 | `-d6test` | `D6_ROLLING_OK distance=3.88` / `D6_STUN_OK remaining=0.28` / `D6_FRAGILE_OK broken=True value=0` / `D6_CLAMP_OK` / `D6_NARROWDOOR_OK` | 阻断 | 推车与货碰撞可能扰动滚动/卡位 |
| D6.5 | `-d65host` + `-d65client` | **推车位置差均值 ≤0.40m / 最大 ≤0.60m**（原物件 0.28/0.48；车比货快，滞后按速度线性放大） | 告警 | 工程给的新阈值；超了记告警不判失败（4A 无追猎压力，4B 再定死） |
| D8 | `-d8test`（8 项）+ `-d8host` + 3×`-d8client`（15 项） | `D8_VALIDATE_OK identities=4 cards=5 traits=1 hud=1 bootstrap=1`；冲突率 58.7% | 阻断 | **D8 路径不许被「中性身份」改坏**：Night1 禁 traits，Main/Supermarket 保持原样（4A-S1b 的另一半） |
| D9 | `-d9dlogic` / `-d9dhost`+`-d9dclient` / `-d9dfreeze=N` / `-d9stress=20\|40` | `D9_VALIDATE_OK health=1 stress=1 hud=1 kcpTimeout=4000ms`；硬杀 ~131ms、冻结 ~3988ms；**4A 阈值：≤500kbps（40 物 + 1 车）**，原 20 物 230kbps / 40 物 451kbps 仅作参照 | 告警 | 4A 新增 `S4AGameRoot` + `Cart` 会改压测构成 → 用 500kbps 新阈值，**不要直接拿 451kbps 当 4A 基线** |
| D11 | `-m1weight` / `-m1lathost=200` / `-m1latclient=200` / `D11AcceptanceSetup.Validate` | `M1_WEIGHT_OK spread>=3 slowdown>=25%` / `M1_LAT_HOST_DONE` / `M1_LATENCY_OK grab<=600ms` / `D11_VALIDATE_OK` | 告警 | 负重减速 42% 是手感基线；**且必须在中性身份下测**，否则分母被商品/身份改写 |
| D12 | `D12SceneSetup.Validate` / `-d12smoke` | `D12_VALIDATE_OK spawns=4 doors=2 patrols=5 anchors=18 steps=12 stairTop=3.0m`；`D12_SMOKE_OK` | 阻断 | **期望值一律不变**：4A 新建 `Night1`，`Supermarket` 零改动。P0 的「18→16 锚点」是 P0 终态口径，另算 |
| D13 | `D13PropSetup.Validate` / `-d13accept` | `D13_VALIDATE_OK profiles=20 prefabs=20 registered=20 types[5/5/5/5]`；`D13_ACCEPT_OK countsOk=True valueOk=True` | 告警 | 保留为「裸结算回归」：`EvacSettlement` 留在 `Supermarket`、`Night1` 删除 |
| D7 工具 | `-pttest`（`PlaytestRecorder` CSV） | `session_*.csv` 落盘、F5/F6/F7 可达 | 告警 | A10 用其 `elapsed_s` 反推单关时长 |
| **新增基线** | `S4ASceneSetup.Validate` | `S4A_VALIDATE_OK night1=1 cartAnchor=1 quotaTerminal=1 portal=1 evacRemoved=1 **anchors=16 traits=none**` | 阻断 | 非继承项，Net new。⚠ **`anchors=16` 与 D12 的 `anchors=18` 是两个场景的两个值，必须成对写进报告**（16 只存在于 `Night1`，18 只存在于 `Supermarket`），避免"16 与 18 打架"被判成回归失败 |

**继承原则**：任一阻断级标记失败 → **先回退 4A 改动再定位**，不许「顺手把基线改一下让它过」。**继承清单里没有任何一项需要改期望值**（v1.1 曾误判 D12 需要 18→16，已作废）。

---

## 七、证据归档

### 7.1 目录与命名

```
Builds/Evidence/P0_4A_<YYYYMMDD>/
├── logs/     4A_<场景>_<用例>_<YYYYMMDD-HHmmss>_<角色>.log
├── video/    4A_<链路或用例>_<run编号>_<YYYYMMDD-HHmm>.mp4
├── shots/    4A_<证据点>_<YYYYMMDD-HHmm>.png
├── saves/    4A_<子场景>/team_default.sav{,.bak,.bak2,.corrupt-*}  ← 存档原始件必留
└── report/   P0_4A_Report_<YYYYMMDD>.md
```

实例：`4A_night1_B1_dupsubmit_20261102-143012_host.log`｜`4A_S1-S7_solo_run01_20261102-1430.mp4`｜`4A_saves_B2b_taskkill/team_default.sav`

### 7.2 每条证据必须自带（缺一项即不完整）

| 字段 | 说明 |
|---|---|
| 启动参数 | 完整命令行，含 `-feel=light` / `-map=sprint4a` / `-savedir=` / 注入开关 |
| 场景名 | 断言 = `Night1` 或 `Safehouse`（防跑错沙盒） |
| **身份中性** | 「P0 中性身份已启用」+ 4 个乘子 = 1.0 |
| 构建标识 | git short hash + 是否含注入开关 |
| 运行拓扑 | **同机单进程 / 同机双进程 / 局域网 2 机**，三选一，不许含糊 |
| 人数 / 关卡 | n 人 / legIndex |
| 时间 | UTC `epoch_ms`（跨进程对齐唯一依据） |

### 7.3 验收报告字段

```
# P0 4A 验收报告 · YYYY-MM-DD
执行人：__   构建：<git hash>   拓扑：__   人数：__
启动参数：__
中性身份：启用？ Y/N   四乘子：carryDivisor=__ breakScale=__ grabMax=__ carryPenalty=__

## 1. 门判定
| 项 | 结论 | 证据 |
| S1 / S1b / S2..S8 | | |
| Q1 达标开门 / Q2 再贪不掉账 | | |
| D1..D5（双人冒烟） | | |
| B1..B7 | | |
| R1..R3 | | |

## 2. 反向验证（缺一即 FAIL）
R1 重复提交 → 捕获？ Y/N  S4A_DEPOSIT_DUP + TALLY 的 dupBlocked 与前后余额：__
R2 写盘失败 → 捕获？ Y/N  S4A_SAVE_FAIL reason=__ 内存回滚 + sha256 未变：__
R3 状态不同步 → 捕获？ Y/N  加 `-s4asyncbreak` 后 match=false？去开关后 match=true？

## 3. 卡死三态计数
自行脱出 __ 次｜脱困救回 __ 次（每次都进 CONCERNS 计数）｜救不回 __ 次（>0 即 FAIL）

## 4. A6 判据
`S4A_DEPOSIT_TALLY`：accepted=__ uniqueCargo=__ expectedSum=__ actualSum=__ dupBlocked=__ ok=__

## 5. 继承回归
| 来源 | 命令 | 期望 | 实测 | 级别 | 结论 |（D3/D4/D5/D6/D6.5/D8/D9/D11/D12/D13 + S4A_VALIDATE_OK）
**anchors 成对声明**：`Night1` = 16（`S4A_VALIDATE_OK`）｜`Supermarket` = 18（`D12_VALIDATE_OK`）

## 6. 商店
上架 4 件：`grip_gloves` __ / `packing_tape` __ / `flashlight` __ / `grip_training` __（逐件自动断言是否齐备 __）

## 7. 已知限制（如实标注，不得美化）
- 同机双进程 vs 跨机：__｜延迟档位：仅 __｜4 人带宽外推/实测：__
- 4A = 16 锚点 / 12 件池 / 单一关配置；A10 无怪物→只记录，**不得当作 P0 节奏证据**
- 未覆盖：怪物/噪音/诅咒/四难度/真刚体侧翻
- 15.7 取消挑件提交（对 17.6 的有意裁剪，已由 design 确认，待用户确认）
- 封印系统缺口：__｜双人「继续」为房主代决：__

## 8. 结论
□ PASS  □ CONCERNS（需修：__，复测：__）  □ FAIL（阻断项：__）
QA：严守真   用户签字：______
```

### 7.4 已知限制必填项

| 限制 | 必须写清 |
|---|---|
| 拓扑 | 「同机 2 进程」还是「局域网 2 机」（沿用 M1/R8，不得含糊） |
| 延迟 | 只测了哪几档（现行只覆盖 200ms，400ms POOR 未测） |
| 4 人 | 同机 4 进程，带宽 ×3 外推非实测 |
| 货物池 | 4A = **16 锚点 / 12 件池 / 单一关配置**，不是 P0 的 12/14/16 三档；A10 无怪物 → 单关时长不得用作节奏证据 |
| 范围 | 不验怪物 / 噪音 / 诅咒 / 四难度 / 真刚体侧翻 |
| 规则缺口 | P0 无封印系统；`CURSED_TEAM`/`CURSED_ZONE`/`SEALABLE` 不覆盖；双人「继续」为房主代决 |
| 人工判断 | 手感/好笑/「愿意再走一关」无自动化替代，AI 不参与 |

---

## 八、P0 放行 / 阻断标准

### 8.1 阻断项（任一触发 → FAIL，不讨论手感）

| # | 阻断条件 | 4A 可验 | 处置 |
|---|---|---|---|
| **X1** | **钱会丢或会复制**（重复入账 / 退款双发 / 强杀丢档 / 重连双计） | 是（除团灭） | 数据层不合格，直接回修，本切片不讨论手感 |
| **X2** | **单人不可完成**（A1/A3/A5：重型件搬不出、推车上不了楼梯、闭环走不通） | 是 | 切片存在理由消失 |
| **X3** | **推车成为强制撤离容器**（≥35kg 件不经推车搬不出） | 是（B5 反证） | 违反 17.20 不可妥协项 |
| **X4** | 无预警攻击 / 无解诅咒 / 无解法控制 | 否（4B） | 违反 17.10 公平底线 |
| **X5** | **性能崩**（<45fps 或持续卡死） | 是（单人基线） | 砍物理物件上限或车内货物数，不砍玩法 |
| **X6** | **测试本身无效**（R1–R3 任一未捕获） | 是 | 本轮证据整体作废，重写探针重跑 |
| **X7** | **强杀证据不合格**：用进程内 `Environment.Exit` 冒充外部强杀；或**两种都跑了但结果不一致**（存在 `ProcessExit` 刷盘） | 是 | 违反 17.4「不许退出时统一保存」，回修 |
| **X8** | **无预警卡死**：脱困也救不回（三态第 3 种）出现 >0 次 | 是 | 违反 17.15「不出现无法恢复的死角」 |
| **X9** | **身份污染**：中性身份未生效导致 3.68 m/s / 42kg 减速 / 平衡断言失真 | 是 | 本轮 A3/A4/A5/A12 数据全部作废 |

### 8.2 非阻断但影响 P0 结论（记 CONCERNS）

A10 越界（>14 或 <6 分钟）→ 调锚点与过关额，不动其它系统｜A11「再走一关」<50% → 回 17.1 重审｜A9 无来源调查 ≥1 次 → 4B 必修｜**卡死靠脱困救回 ≥2 次** → 路线或物理需调｜商店件数不齐 → P0 门 CONCERNS。

### 8.3 放行流程

1. QA 出判定表 → **advisory**；
2. 阻断项 0 失败 + 反向验证 3/3 + 继承回归全过 → 提交用户审批；
3. **用户签字后 4A 才算放行**；QA 判定不能代替用户放行；
4. P0 门另起报告，同样走用户签字。

---

## 九、测试钩子（按工程 v1.4 实际契约）

命令与标记以 `Sprint_4A_TechnicalPlan.md` v1.4 及其附录 A 为准。前期 11 项缺口已全部补齐，含 `GATE_CART_STALL rescued=`、`GATE_QUOTA_GATE carry_over=`、16 锚点类型构成表。

| 用途 | 命令 | 标记 | 支撑的门 | 状态 |
|---|---|---|---|---|
| 闭环总跑（2 关） | `-s4aloop` | `S4A_LOOP_OK legs=2 balance= dupBlocked=1` + `GATE_LEVEL` | S7 / S8 / D1 | ✅ |
| 闭环客户端 | `-s4aloopclient -address=` | `GATE_WALLET_SYNC` | D1–D5 / R3 | ✅ |
| 提交 | `-s4adeposit` | `S4A_DEPOSIT` / `_DUP` / `_ZERO` / **`S4A_DEPOSIT_TALLY accepted= uniqueCargo= expectedSum= actualSum= dupBlocked= ok=`** | S4 / B1 / R1 | ✅（TALLY 足以判 A6；建议再补逐件 `cargoId/value` 行便于定位） |
| 存档写读 | `-s4asave` | `S4A_SAVE_OK txn=` | S5 | ✅ |
| 强杀 | `-s4asave-kill` + `-s4asave-slow` + **外部 `taskkill /F /PID`** | 两种都跑、结果须一致 | B2b/B2c | ✅（已改 `taskkill /F`，且明确内外部不一致即不合格） |
| 校验 | `-s4asave-verify` + `-expect=<n>` | `S4A_SAVE_VERIFY_OK txn= dup= balance= sumCommitted=` / `GATE_WALLET expected= actual= delta= path=` | B2 / R2 | ✅（`expected` 外部传入，不许测试自己算） |
| 写盘失败 | `-s4asave-failio` / 只读 `-savedir` | `S4A_SAVE_FAIL reason=(io_error\|no_space\|nonatomic_fallback\|locked\|schema)` | R2 | ✅ 两条制造路径都要跑 |
| 造坏档 | `-s4acorrupt` | `S4A_SAVE_RECOVERED from=` / `S4A_SAVE_CORRUPT_RECOVERED` | B2e / R2 | ✅（需能**依次**造坏 sav → bak 以验 bak2） |
| 推车 | `-s4acart` / `-s4acart-noise` | `S4A_CART_OK` / `S4A_CART_NOISE_OK` / `GATE_CART count= mass= ids=` / `GATE_CART_STALL duration= mass= count= where= rescued=` | S3 / A4 / B4 / B5 | ✅ 三态齐备 |
| 商店 | `-s4ashop` | `S4A_SHOP_OK balance= owned= revoked=` + `GATE_BUY` / `GATE_BUY_REJECT` / `GATE_BUY_UNDO` / `GATE_BUY_UNDO_REJECT purchaseId= state= reason=(window_closed\|already_revoked)` | S6 / B3 / B6 | ✅ |
| 计时 | 随 Story 3 / 5 | `GATE_TIMER tag=(stair_cycle\|unload_narrow) ms=` | A5 / B5 | ✅ |
| 搬运方式 | 随 Story 3 | `GATE_HEAVY_MANUAL cargoId= mass= via_cart=False` | **A3（唯一判据源）** | ✅ |
| 同步注入 | `-s4asyncbreak` | `GATE_WALLET_SYNC server= client= match=false` | R3 | ✅ |
| 建图/校验 | `S4ASceneSetup.Setup/Validate` | `S4A_VALIDATE_OK … anchors=16 traits=none` / `S4A_NEUTRAL_OK` | S1 / S1b | ✅ |
| 性能 | 随闭环 | `S4A_PERF_OK` + `NetworkDiagnostics` | A12 | ✅ |
| 回归总跑 | `run_regression_4a.bat` | `S4A_REGRESSION_OK passes=<n> expected=<m>` | 全部 | ✅（已按我要求补 `expected`，防漏跑空过） |

**三项前期缺口已补齐**

| 已解决 | 处置 |
|---|---|
| `GATE_CART_STALL` 三态 | ✅ 工程改为 `rescued=(self\|unstick\|fail)`，仅实际卡死时打一条 |
| `GATE_QUOTA_GATE` | ✅ 已补：`leg= progress= quota= portal= carry_over=` |
| 16 锚点类型构成 | ✅ 已补：普通6 / 易碎4 / 滚动3 / 重型3，进类型计数断言 |

---

## 十、待确认事项

| # | 事项 | 状态 | 需谁定 |
|---|---|---|---|
| 1 | 4A 是否含「进第 2 关」 | ✅ **已定：含**（S7 为阻断项，`-s4aloop` 连跑 2 关）。但场景方案（双场景 vs 单场景分区）仍待用户拍板，若降级为单场景（工程 T15），S7 判据要改 | 用户 |
| 2 | `-d13accept` 保留还是替换 | ✅ 已答：保留为裸结算回归 | — |
| 3 | D12 期望值 | ✅ 已答：**不变**（`Night1` 另建，`Supermarket` 零改动，`anchors=18`） | — |
| 4 | 满载分母 | ✅ 已答（规格 v1.1）：**`maxLoad=120kg`，A4 实际载荷 ≥110kg 且单层不堆叠**（114kg/3 件），超载容差 **+0.5kg** | — |
| 5 | 满载车过 1.2m 窄门的预期 | ✅ **已定**：空车展开（约 0.75m）应直接通过；货物外伸使有效宽度 >1.2m 时才被明确挡住并提示“卸货/调整”；卸至可过即可通过；不强制折叠 | — |
| 6 | 撤销窗口 | ✅ 已答：`phase == Browsing`（离开安全屋前有效） | — |
| 7 | 过关额口径 | ✅ **已收敛**：工程改为**从数据表读**（`S4A_QUOTA_FALLBACK` 仅在读不到时回退 ×0.55）。A10 与 S8 判据均以表值为准 | — |
| 8 | 长按确认时长 | ✅ **已定：1.2s**（规格与工程 §3.7 已统一） | — |
| 9 | 4A 商店件数 | ✅ **已定：4 件具名**（手套 400 / 布带 500 / 手电 350 / 握力 900）。**v1.2 的"三方不一致"是我 grep 到类注释残留导致的误判，作废** | — |
| 10 | 强杀用什么实现 | ✅ **已定**：外层 `taskkill /F /PID`，且 `Environment.Exit` 与外部强杀**两种都跑、不一致即 FAIL**（工程 §8 与 X7 已一致） | — |
| 11 | **`GATE_CART_STALL` 字段** | ✅ 已定：工程使用 `rescued=(self\|unstick\|fail)`，仅实际卡死时打一条；三态即“自行脱出 / 脱困救回 / 救不回”，不再要求 `none` | — |
| 12 | **16 锚点的类型构成** | ✅ 已定：技术计划 §2.3 已写死并进入 `S4A_VALIDATE_OK` 类型计数断言（普通6 / 易碎4 / 滚动3 / 重型3） | — |
| 13 | **`GATE_QUOTA_GATE`** | ✅ 已定：字段为 `GATE_QUOTA_GATE leg= progress= quota= portal=(locked\|unlocked) carry_over=`；`carry_over` 是“取消挑件”后“达标开门 + 再贪一趟”的唯一证据 | — |
| 14 | **P0 取消挑件提交需用户确认** | 规格 v1.2 标为「对 17.6 的有意裁剪，需用户确认」；`-s4ashop` 不受影响，但 Q4 要求交付报告显式引用 | 用户 |

---

## 十一、与两份上游文档的差异对照（v1.3 核对）

已核对 `P0_VerticalSlice.md` **v1.2** 与 `Sprint_4A_TechnicalPlan.md` **v1.2**（1006 行）。**v1.2 我列的 7 处冲突已全部关闭**，其中 5 处由上游修正、2 处是我自己判错（C5 / C7）。

### 11.1 冲突（C）— 全部关闭

| # | 结果 |
|---|---|
| **C1** | ✅ **已裁（P0 v1.2 正式定案，非未决）**：**P0 与 4A 都只做整车一次提交**。design 判定原「留一部分冲下一关」是伪选项（提交无税无上限、surplus 直接变现，留货零收益只有风险）；「收手 vs 贪心」改由「达标即刻走 vs 再进去贪一趟」承担。**挑件提交的恢复时点 = P1，随多公秤 / 公秤换位一起恢复**（规格 §七-16）。⚠ engineering §8.1b 仍写「T5 在 4C 必还」——**与 team-lead 冻结口径（P1）不一致，以 P1 为准**，请 engineering 收敛。我的动作：4A 按整车提交验（S4），新增 **S8 + Q1/Q2** 补上被取消决策的可验形态，Q4 要求 P0 报告显式引用此裁剪 |
| **C2** | ✅ 已统一 1.2s（工程 §3.7 已改） |
| **C3** | ✅ 已修（工程 v1.2 ⑤ 明确 `3.18` 是 D5 前默认值，light 下 4.14） |
| **C4** | ✅ 已收敛：过关额**从数据表读**，读不到才回退 ×0.55（`S4A_QUOTA_FALLBACK`）。A10/S8 以表值为准 |
| **C5** | ✅ **无冲突**：工程 §5.3 正文 = **4 件**（手套 400 / 布带 500 / 手电 350 / 握力 900），与规格 §2.5 一致。唯一遗留是 `ShopCatalog.cs` 的 v1.0 类注释仍写「商品表（6 件）」，**已要求删除**（不构成口径冲突，仅清理项） |
| **C6** | ✅ 已改：`taskkill /F /PID` 为主，且**两种都跑、不一致即不合格**（= X7）。工程 §8 第 13 条与我同一措辞 |
| **C7** | ✅ **已澄清**：4A = **16 锚点 / 12 件类型池 / 刷 16 件货**；18 是**被删掉的继承锚点数**，不是新池。D9 带宽随之改新阈值 ≤500kbps（见 §六）。A10 结论（只记录不判定）保留，理由 = 无怪物 → 追猎窗口缺席 + 单一关配置无 12/14/16 梯度，**不是"池子不同"** |

### 11.2 钩子核对（v1.3）

我 v1.2 提的 11 项修补，工程 v1.2 已落 10 项：`S4A_DEPOSIT_TALLY`（含 `dupBlocked`）、`S4A_SAVE_FAIL reason=` 五类、`S4A_SAVE_VERIFY_OK … balance= sumCommitted=`、`GATE_WALLET expected= actual= delta= path=` + `-expect=`（期望值外部传入，禁止测试自算）、`GATE_TIMER`、`GATE_HEAVY_MANUAL`、`GATE_CART count= mass= ids=`、`GATE_WALLET_SYNC` + `-s4asyncbreak`、`GATE_BUY_UNDO_REJECT reason=(window_closed|already_revoked)`、`S4A_REGRESSION_OK passes= expected=`。

工程 v1.4 已补齐前期 3 项缺口：`GATE_CART_STALL` 的 `rescued=(self|unstick|fail)`、`GATE_QUOTA_GATE … carry_over=`、16 锚点类型构成表。`GATE_QUOTA_GATE` 是取消挑件提交后“收手 vs 贪心”支柱的唯一证据，优先级最高。

### 11.3 执行纪律（v1.3 增补）

1. **`anchors=16` 与 D12 的 `anchors=18` 是两个场景的两个值**，必须成对写进报告，否则会被误判成回归漂移。
2. **`SAVE_LOCKED` 同机双开只读**：单人验证必须单进程 host；双人时只有 host 写盘、client 无账本。**只读不得阻断 client 游玩与提交**（D5 验收）。
3. **`S4A_REGRESSION_OK` 必须带 `expected=<m>`**，`m` 从用例清单读。
4. **凡断言「某文档仍是某值」，必须报行号 + 小节标题**（v1.3 自纠教训）。

### 11.4 认可、无需改的部分

`TryCommit` 先落盘再改内存｜`cargoId = runId-legIndex-netId` 跨关不冲突｜`txnSeq` 单调 + 以盘为准｜`corrupt-*.sav` 绝不覆盖｜非 NTFS 降级并诚实记 `SAVE_FALLBACK_NONATOMIC`｜规格 2.6 携带速度硬钳制（对未来所有商品免疫）｜规格 2.7 脱困 0.75s×2 与 1.5s 自洽｜工程 §2.6 身份中性的**双保险**（场景删 `TraitBootstrap` + 启动断言 `ForceNoTraits`）——这一点比我要求的更严，采纳｜工程 §8.3「禁止假实现」13 条与本文件 §5.2 十三条同向，建议合并为验收门附则。
