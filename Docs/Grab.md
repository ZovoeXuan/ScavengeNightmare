# 抓取与投掷（D4）

## 状态

- 已实现：准星指向高亮 → 按住左键抓取（速度控制，**不 SetParent**）→ 松开投掷（继承玩家速度）
- 数据驱动：质量 / 摩擦 / 弹性 / 抓握加速度 / 投掷冲量全部在 `PropPhysicsProfile` 资产里
- 已自动验证：抓取距离、跟随不脱手、出手速度 = 冲量÷质量、30 m/s 撞墙不穿模
- 待人工验证：手感（重量感 / 力度感），这是 D7 朋友局的核心观察项

## 手感模型

抓取不用挂接（`SetParent`）也不用关节，而是**速度控制**：

```
target  = 相机位置 + 相机前向 × holdDistance
delta   = target − 物件质心
若 |delta| > breakDistance → 强制脱手（被门框 / 墙 / 队友卡住）
期望速度 = clamp(delta × followStiffness, 0..grabMaxSpeed)
实际速度 → 以 grabAcceleration 逼近期望速度
```

这样物件全程保持完整物理模拟：会撞墙、会被卡住、会掉、能被别的物件顶开 —— 对应 D1 冻结的「物体永远有物理反应，禁止磁吸式搬运」。

投掷：

```
出手速度 = 投掷冲量 ÷ 质量      （所以越重扔得越慢，重量感主要来自这里）
最终速度 = 玩家速度 + 相机前向 × 出手速度，并做上限截断
```

负重：持物时玩家移动速度乘 `clamp(1 − 质量/70, 0.45, 1)`，对应 D1 的「跑会被拖累」。

## 物件物理档（`Assets/_Game/Data/Props/`）

| 档位 | 质量 | 投掷冲量 | 出手速度 | 动/静摩擦 | 弹性 | 抓握加速度 |
|---|---|---|---|---|---|---|
| `Prop_can` 罐头 | 2 kg | 26 N·s | 13.0 m/s | 0.45 / 0.50 | 0.10 | 55 |
| `Prop_box` 纸箱 | 9 kg | 42 N·s | 4.7 m/s | 0.60 / 0.65 | 0.02 | 55 |
| `Prop_barrel` 圆桶 | 22 kg | 70 N·s | 3.2 m/s | 0.32 / 0.35 | 0.05 | 55 |
| `Prop_statue` 雕像 | 42 kg | 120 N·s | 2.9 m/s | 0.70 / 0.75 | 0.00 | 55 |

改表即生效（运行时应用，不需要重启工程）。圆桶的动摩擦刻意压低（0.32）+ 角阻尼 0.15，为 D6「滚动体」事件预留手感。

## 场景与预制体

- prefab：`Assets/_Game/Prefabs/Props/{can,box,barrel,statue}.prefab`（Rigidbody + 连续碰撞检测 + GrabbableProp）
- 场景：`PropPen` 下 8 个实例（四种各 2 个，两排），`ThrowWall`（8×3×0.3m，z=-6）用于投掷测试
- 玩家：`Player.prefab` 增加 `GrabController` + `GrabHUD`

## 代码结构

| 文件 | 作用 |
|---|---|
| `Scripts/Gameplay/PropPhysicsProfile.cs` | 物件物理档（ScriptableObject） |
| `Scripts/Gameplay/GrabbableProp.cs` | 应用物理档、运行时物理材质、准星高亮、速度兜底截断 |
| `Scripts/Gameplay/GrabController.cs` | 射线拾取、速度控制跟随、投掷、负重通知；远端玩家复制体不读键鼠 |
| `Scripts/Gameplay/D4AutoTest.cs` | 批处理自动化验证（`-d4test`） |
| `Scripts/UI/GrabHUD.cs` | 中心准星 + 「左键抓取 / 松开投掷」提示 + 当前物件重量 |
| `Scripts/Editor/D4SceneSetup.cs` | 幂等装配：物理档、材质、prefab、场景试验场、校验 |

## 自动化验证

```
ScavengeNightmare.exe -batchmode -logFile Logs/d4_test.log -d4test
```

实测输出（2026-10-27）：

```
D4_GRAB_OK   distance=1.70 expected=1.60
D4_FOLLOW_OK ok=True distance=1.70 propMoved=1.45
D4_THROW_OK  ok=True speed=3.18 playerVelocity=0.00 mass=22.0kg
D4_TUNNEL_OK crossed=False closestSide=-0.40 finalSide=-1.44 speed=30
```

`closestSide=-0.40` 正好等于「墙半厚 0.15 + 胶囊半径 0.25」，即物件贴在墙面停住；整个过程 `crossed=False`，没有出现穿墙帧。

## 踩坑记录

1. **休眠刚体改速度无效**：静止的物件进入 sleep 后直接赋值 `linearVelocity` 不生效，表现为「抓不动 / 扔不出」。抓取和投掷前都必须 `WakeUp()`。这是自动化撞墙测试暴露出来的真实玩法 bug，不只是测试问题。
2. **无头环境必须锁帧**：同 D3。不限帧时固定步物理几乎不推进，`WaitForFixedUpdate` 长时间不返回，撞墙测试只跑了 3 步就误判。`-d4test` 内部强制 60fps。
3. **贴地压地速度污染投掷**：`CharacterController.isGrounded` 时垂直速度被人为设成 -2，若直接计入「继承玩家动量」，每次平地投掷都会被额外往下压。贴地时垂直分量取 0。
4. **编辑期不能应用运行时物理材质**：`PhysicsMaterial` 若在编辑期 new 出来赋给 collider，保存 prefab 会丢引用。`GrabbableProp.ApplyProfile` 在非运行期只写质量，材质留到运行时创建。

## 已知限制

- **无多人所有权**：谁先抓到算谁的，目前只保证「每个客户端只控制自己的玩家」，同一物件被两个客户端抓取会打架。按 D3 遗留结论，物件联网（NetworkIdentity + 所有权切换）排到 D6/D9。
- **无双手 / 共持**：单手持一件，D4 明确不做。
- **无摩擦事件**：窄门卡位、手滑砸脚等 5 条在 D6。
- **投掷无蓄力**：固定冲量，D5 调参时再决定是否加蓄力曲线。
- **镜头无 FOV / 摇晃反馈**：纯骨架，D10 手感迭代再补。
