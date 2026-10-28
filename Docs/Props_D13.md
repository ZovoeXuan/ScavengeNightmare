# D13 · 物件系统化

> 计划原文：物件参数表落地为 ScriptableObject/JSON：类型（易碎 / 滚动 / 重型 / 普通）、价值、质量、摩擦、抓握点；创建 20 种拾荒物占位（Cube/胶囊组合）；撤离结算读取该表。验收：改表即生效、物件类型可辨识。

---

## 一、参数表结构

`PropPhysicsProfile` 在原有质量/摩擦/抓握/投掷参数之上，新增两项：

| 字段 | 作用 |
|---|---|
| `propType` | 四类之一，同时驱动**配色**与**行为组件挂载**，改这一个字段即切换整条链路 |
| `gripOffset` | 抓握点（物件本地坐标偏移），持物时把这个点对准准星 |

抓握点的实际用途：长条物件（电视）横着拿、大件（冰箱/洗衣机/衣柜）抓侧面，而不是一律抓几何中心。

原有字段继续承担各自职责：

| 维度 | 字段 |
|---|---|
| 类型 | `propType` |
| 价值 | `value` |
| 质量 | `mass` |
| 摩擦 | `dynamicFriction` / `staticFriction` |
| 抓握点 | `gripOffset` |
| 易碎阈值 | `isFragile` / `breakSpeed` |

---

## 二、20 种拾荒物（四类各 5 件）

| 类型 | 物件 | 质量 | 价值 |
|---|---|---|---|
| **易碎**（别摔） | 玻璃花瓶 / 玻璃杯 / 酒瓶 / 老电视 / 灯泡箱 | 3 / 1 / 1.5 / 12 / 1 kg | 800 / 120 / 200 / 600 / 90 |
| **滚动**（会跑） | 圆桶 / 西瓜 / 篮球 / 轮胎 / 油桶 | 22 / 5 / 0.6 / 10 / 18 kg | 150 / 45 / 80 / 110 / 180 |
| **重型**（搬不动） | 石雕像 / 大衣柜 / 保险箱 / 洗衣机 / 冰箱 | 42 / 35 / 60 / 55 / 70 kg | 900 / 400 / 1200 / 700 / 850 |
| **普通**（随手拿） | 罐头 / 纸箱 / 米袋 / 工具箱 / 微波炉 | 2 / 9 / 15 / 7 / 14 kg | 20 / 60 / 130 / 160 / 320 |

白盒占位由 Cube / Cylinder / Capsule / Sphere 组合而成，四类各一种配色：

| 类型 | 颜色 | 辨识方式 |
|---|---|---|
| 普通 | 米黄 | 随手拿 |
| 易碎 | 淡蓝 | 别摔 |
| 滚动 | 橙棕 | 会跑 |
| 重型 | 中灰 | 搬不动 |

类型还会自动挂对应行为组件：易碎件 → `FragileProp`，滚动件 → `RollingProp`。重型件则在参数上体现「搬不动」（`grabAcceleration` 32、`grabMaxSpeed` 5.5、`holdDistance` 1.9、`breakDistance` 2.2），滚动件给低摩擦低角阻尼让它真的滚得起来。

---

## 三、一条硬约束：既有 6 件的物理数值不能改

`can / box / barrel / statue / cabinet / vase` 这 6 件是 D4/D6 的老物件，**D4 投掷基线（4.14 m/s）依赖 `barrel.throwImpulse = 70`**，D6 摩擦用例也依赖它们的既有参数。

所以装配脚本对已存在的档**只写 `propType` / `gripOffset` / `value` / `isFragile`**，质量、摩擦、冲量、抓握参数一律不动。新增的 14 件才写完整数值。

`value` 之所以例外，是因为它不是物理参数（D4/D6 不依赖），而它是结算的输入——留 0 会让「改表即生效」无从验证。

回归验证：装配后 **D4 投掷仍是 4.14**（未漂移）。

---

## 四、撤离结算

`EvacSettlement`（带 NetworkIdentity 的场景物体，SyncVar 同步总额）：

- 玩家**带着货**进入撤离区即计入结算，随后物件回收
- 价值一律读 `GrabbableProp.CurrentValue`（易碎件碎后为 0），**不读 `Profile.value`**
- 类型从 `Profile.propType` 读出并写进日志，便于按类型统计

结算组件本身**不含任何硬编码数值**。

---

## 五、验收数据

```
D13_VALIDATE_OK profiles=20 prefabs=20 registered=20 types[Rolling=5 Fragile=5 Normal=5 Heavy=5]
D13_ACCEPT_OK countsOk=True(4==4) valueOk=True(1870==1870)
```

四类各挑一件送进撤离区：

```
D13_DELIVER id=vase   type=Fragile value=800 total=800  count=1
D13_DELIVER id=barrel type=Rolling value=150 total=950  count=2
D13_DELIVER id=statue type=Heavy   value=900 total=1850 count=3
D13_DELIVER id=can    type=Normal  value=20  total=1870 count=4
```

### 「改表即生效」实证

把 `Prop_can.asset` 的 `value` 从 **20 改成 999**，不改任何代码，重新打包运行：

```
D13_DELIVER id=can type=Normal value=999 total=2849 count=4
D13_ACCEPT_OK valueOk=True(2849==2849)
```

总额从 1870 变成 **2849**，结算完全跟随数据表。随后已把 `value` 恢复为 20 并重新打包。

### 回归

装配后 Main 重建，D4 投掷 4.14、D6 五条、D8 八项、D9 七项全过；Supermarket 的 D12 进图冒烟也全过（18 件按锚点刷出、抓到花瓶、楼梯爬升 3.00m）。

---

## 六、验证入口

| 目的 | 命令 |
|---|---|
| 生成物件系统 | `-executeMethod ScavengeNightmare.EditorTools.D13PropSetup.Setup` |
| 校验 | `...D13PropSetup.Validate` → `D13_VALIDATE_OK` |
| 结算验收 | `Builds/Supermarket/ScavengeNightmare.exe -d13accept` |
| 进图冒烟 | `... -d12smoke` |

---

## 七、已知限制

1. **20 件只注册进 Supermarket**，Main 沙盒仍是原来 6 件。Main 是冻结的测试基线，往里塞 14 个新 prefab 会改变 D9 压测的物件构成，风险不值得冒。
2. **易碎件碎裂后价值归零**，但没有「碎掉是否扣分」的设计——当前只是拿不到钱。要不要惩罚属于 D13 之后的经济系统决策。
3. **抓握点目前只影响持物偏移，没有真正做「双手握持」**。大件是否要求两人抬，是后续设计。
4. **没有结算 UI**。总额走 SyncVar 同步，但界面上还没显示，玩家看不到自己赚了多少。
5. **价值数值是首版拍脑袋**，没有经过真人实测校准。
