# 开发日志

> 格式：每天三行——做了什么 / 验收是否通过 / 遗留问题。遗留问题写进次日提示词开头。

## D1 · 2026-10-27 · 工程脚手架

- **做了什么**：用 Unity 6000.3.27f1 的 3D URP 模板建立 `D:\drlj\game`；生成 `Assets/_Game/{Scripts,Scenes,Data,Art,Audio,UI}`；创建 `Main.unity`（Main Camera + Directional Light + URP Camera Data）并登记到 Build Settings；配置 Git + LFS + `.gitignore` + README。
- **验收**：通过。`D1_VALIDATE_OK pipeline=PC_RPAsset scene=Assets/_Game/Scenes/Main.unity`；Unity 批处理退出码 0；空场景可打开。
- **遗留问题**：
  1. 本机 Git 未配置 `user.name` / `user.email`，本次提交使用临时身份 `Qoder <qoder@local>`，未写入全局配置；首次推送 GitHub 前建议替换为本人身份。
  2. 里程碑 M4/M5 写“3 图 5 怪”，与范围冻结清单“更多地图不做、保持 1 张到 M5”冲突，需在 D5 前确认以哪份为准。
  3. 冻结清单未显式列出“基础 HUD / 大厅 UI / 微恐氛围（灯光+音效）”，建议补进必做或应该做，避免 D16/D32 时才补需求。
