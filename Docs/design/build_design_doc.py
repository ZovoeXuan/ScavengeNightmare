#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 design 目录下的 Markdown 底稿合成一份可检索的可视化设计文档。"""
import html
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent

FILES = [
    ("overview", "总览与已实装系统", None),
    ("monster", "怪物图鉴（15 种）", ROOT / "Monster_Roster_v1.md"),
    ("map", "地图设计（3 图 + 安全屋）", ROOT / "Maps_v1.md"),
    ("items", "物品 / 角色 / 商店", ROOT / "Items_Characters_Shop_v1.md"),
]

OVERVIEW = """## 项目总览

《拾荒惊魂》是 1–4 人合作的**物理拾荒恐怖喜剧**。核心动词是「捡、搬、装、推、称、跑」。

- 引擎：Unity 6.3 + Mirror，服务端权威
- 当前阶段：Sprint 4A 已收口（13 条自动化用例连续三轮 13/13），4B 怪物系统启动
- 单关时长目标：8–12 分钟；三关连续 30–40 分钟

### 已实装并可验收的系统

| 系统 | 关键判据 | 实测 |
|---|---|---|
| 公秤入账与去重 | accepted == uniqueCargo、dupBlocked ≥ 1 | balance=2650 |
| 原子存档（三级备份） | 强杀后可恢复、balance == SumFromTxns() | 33 条断言全绿 |
| 折叠推车 | 102 kg 推行 30 m 不掉货 | 30.03 m / kept=2 |
| 推车上楼梯 | 折叠→抱车→爬 3.00 m ≤ 90 s | 69.1 s |
| 窄门三态 | 空车/载货/折叠通过，载货折叠被拒 | lost=0、foldRejected=1 |
| 徒手搬重货（A3） | 35 kg 不走车，cartLoads=0 | viaCart=False |
| 同板质量比闸门 | 42:1 拒 / 6:1 收 | rejected=True |
| 商店五拒绝分支 | 余额恒等式仍成立 | identity=1 |
| 负重硬上限 | ≥15 kg ≤ 3.135 m/s | heavyRatio=1.150 |
| 换关闭环 | 夜路 → 安全屋 → 夜路 | legs=2、sync=match |

### v1.1 复核结论（已并入正文）

- **怪物**：7 种 10 处速度越出 ZONE 实测区间，已修正；8 条系统冲突（噪音听觉半径、模仿者 HUD、窄门判据、楼梯影与折叠车、手电距离、夜秤随机数、尸体 cargoId、P0 口径）已裁定。
- **地图**：7 处坐标硬伤已修，含 L1 出生→公秤 29.50 m 不达标（现 31.0 m）、两层平台与楼梯不接、楼梯水平投影 3.6 m 算错。
- **数值**：投掷系数 0.55 → 0.35（原值会一击秒杀 60 HP 杂鱼）；经济 sink/source 由 0.32–0.47 修正到 0.61–0.76；过关额统一为实装值 2632。

### 文档导航

- **怪物图鉴**：15 种原创怪物，含 HP/速度/伤害/克制/掉落/验收断言
- **地图设计**：3 张夜路地图 + 安全屋，含坐标、ASCII 平面图、锚点、节奏表
- **物品 / 角色 / 商店**：20 件真实物件数值、现有 4 件商店道具、新增 11 件、统一伤害公式

### 参考与原创声明

参考资料为 Lethal Company / R.E.P.O. / ZONE L.O.S.T. 的**机制**（深度分层、视线约束、失聪、噪音放大、囤积、精英召唤、三档状态机数值区间）。所有怪物名称、外观、数值表、地图坐标与物件数值均为本项目原创，不复制任何具体角色、形象、文案或数值。
"""


def inline(text: str) -> str:
    text = html.escape(text, quote=False)
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"`(.+?)`", r"<code>\1</code>", text)
    return text


def md_to_html(md: str) -> str:
    lines = md.splitlines()
    out = []
    i = 0
    in_code = False
    in_list = False
    para = []

    def flush_para():
        if para:
            out.append("<p>" + inline(" ".join(para).strip()) + "</p>")
            para.clear()

    def close_list():
        nonlocal in_list
        if in_list:
            out.append("</ul>")
            in_list = False

    while i < len(lines):
        line = lines[i]
        stripped = line.strip()

        if stripped.startswith("```"):
            flush_para()
            close_list()
            if in_code:
                out.append("</pre>")
                in_code = False
            else:
                out.append("<pre>")
                in_code = True
            i += 1
            continue

        if in_code:
            out.append(html.escape(line, quote=False))
            i += 1
            continue

        if not stripped:
            flush_para()
            close_list()
            i += 1
            continue

        if stripped.startswith("|") and i + 1 < len(lines) and re.match(r"^\|[\s:|-]+\|$", lines[i + 1].strip()):
            flush_para()
            close_list()
            header = [c.strip() for c in stripped.strip("|").split("|")]
            i += 2
            rows = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            out.append("<div class='table-wrap'><table>")
            out.append("<thead><tr>" + "".join(f"<th>{inline(h)}</th>" for h in header) + "</tr></thead>")
            out.append("<tbody>")
            for r in rows:
                out.append("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in r) + "</tr>")
            out.append("</tbody></table></div>")
            continue

        m = re.match(r"^(#{1,6})\s+(.*)$", stripped)
        if m:
            flush_para()
            close_list()
            level = min(len(m.group(1)) + 1, 6)
            out.append(f"<h{level}>{inline(m.group(2))}</h{level}>")
            i += 1
            continue

        if stripped.startswith("- "):
            flush_para()
            if not in_list:
                out.append("<ul>")
                in_list = True
            out.append("<li>" + inline(stripped[2:]) + "</li>")
            i += 1
            continue

        if re.match(r"^---+$", stripped):
            flush_para()
            close_list()
            out.append("<hr/>")
            i += 1
            continue

        flush_para() if False else None
        para.append(stripped)
        i += 1

    flush_para()
    close_list()
    if in_code:
        out.append("</pre>")
    return "\n".join(out)


sections = []
for key, title, path in FILES:
    if path is None:
        body = md_to_html(OVERVIEW)
    else:
        body = md_to_html(path.read_text(encoding="utf-8"))
    sections.append(f'<section class="panel" id="panel-{key}" data-key="{key}">{body}</section>')

tabs = "\n".join(
    f'<button class="tab{" active" if idx == 0 else ""}" data-target="{key}">{html.escape(title)}</button>'
    for idx, (key, title, _) in enumerate(FILES)
)

doc = f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>拾荒惊魂 · 整合设计文档 v1</title>
<style>
  :root {{
    --bg: #f7f6f3; --panel: #ffffff; --ink: #22211f; --muted: #6b6862;
    --line: #e2ded6; --accent: #9a3412; --accent-soft: #fbeee7; --chip: #efece6;
  }}
  * {{ box-sizing: border-box; }}
  body {{ margin: 0; background: var(--bg); color: var(--ink);
    font-family: -apple-system, "Segoe UI", "Microsoft YaHei", system-ui, sans-serif;
    font-size: 15px; line-height: 1.7; }}
  header {{ position: sticky; top: 0; z-index: 10; background: var(--panel);
    border-bottom: 1px solid var(--line); padding: 14px 24px 0; }}
  .title {{ font-size: 18px; font-weight: 600; margin: 0 0 4px; }}
  .meta {{ color: var(--muted); font-size: 13px; margin-bottom: 12px; }}
  .search {{ width: 100%; max-width: 420px; padding: 8px 12px; margin-bottom: 12px;
    border: 1px solid var(--line); border-radius: 8px; background: #fff; font-size: 14px; }}
  .tabs {{ display: flex; flex-wrap: wrap; gap: 6px; }}
  .tab {{ border: 1px solid var(--line); background: #fff; color: var(--muted);
    padding: 8px 14px; border-radius: 8px 8px 0 0; border-bottom: none; cursor: pointer;
    font-size: 14px; }}
  .tab.active {{ background: var(--accent-soft); color: var(--accent); font-weight: 600; }}
  main {{ max-width: 1080px; margin: 0 auto; padding: 20px 24px 80px; }}
  .panel {{ display: none; background: var(--panel); border: 1px solid var(--line);
    border-radius: 0 10px 10px 10px; padding: 24px 28px; }}
  .panel.active {{ display: block; }}
  h1 {{ font-size: 22px; margin: 4px 0 12px; }}
  h2 {{ font-size: 18px; margin: 26px 0 10px; padding-bottom: 6px; border-bottom: 1px solid var(--line); }}
  h3 {{ font-size: 16px; margin: 20px 0 8px; color: var(--accent); }}
  h4 {{ font-size: 15px; margin: 16px 0 6px; }}
  .table-wrap {{ overflow-x: auto; margin: 12px 0 18px; }}
  table {{ border-collapse: collapse; width: 100%; font-size: 13.5px; }}
  th, td {{ border: 1px solid var(--line); padding: 7px 10px; text-align: left; vertical-align: top; }}
  th {{ background: var(--chip); font-weight: 600; }}
  tr:nth-child(even) td {{ background: #fbfaf8; }}
  pre {{ background: #1f1e1c; color: #f2efe9; padding: 14px 16px; border-radius: 8px;
    overflow-x: auto; font-size: 12.5px; line-height: 1.5; }}
  code {{ background: var(--chip); padding: 1px 5px; border-radius: 4px; font-size: 13px; }}
  ul {{ margin: 8px 0 14px; }}
  li {{ margin: 3px 0; }}
  p {{ margin: 8px 0; }}
  hr {{ border: none; border-top: 1px solid var(--line); margin: 22px 0; }}
  .hit {{ background: #fdf3c8; }}
</style>
</head>
<body>
<header>
  <p class="title">拾荒惊魂 · 整合设计文档 v1</p>
  <p class="meta">怪物 15 种 · 地图 3 张 + 安全屋 · 物件 20 件 · 商店 15 件　|　状态：设计底稿，待制作人审查　|　生成自 Docs/design 下三份 Markdown 底稿</p>
  <input class="search" id="q" placeholder="搜索：输入关键词过滤标题与表格行（如 推车 / 楼梯 / 伤害 / 冷雾）"/>
  <div class="tabs">{tabs}</div>
</header>
<main>
{chr(10).join(sections)}
</main>
<script>
  const tabs = document.querySelectorAll('.tab');
  const panels = document.querySelectorAll('.panel');
  tabs.forEach(t => t.addEventListener('click', () => {{
    tabs.forEach(x => x.classList.remove('active'));
    panels.forEach(p => p.classList.remove('active'));
    t.classList.add('active');
    document.getElementById('panel-' + t.dataset.target).classList.add('active');
  }}));

  const q = document.getElementById('q');
  q.addEventListener('input', () => {{
    const term = q.value.trim().toLowerCase();
    document.querySelectorAll('.hit').forEach(e => e.classList.remove('hit'));
    if (!term) return;
    document.querySelectorAll('.panel.active h1, .panel.active h2, .panel.active h3, .panel.active h4, .panel.active tr, .panel.active li').forEach(el => {{
      if (el.textContent.toLowerCase().includes(term)) {{
        el.classList.add('hit');
      }}
    }});
  }});
</script>
</body>
</html>
"""

out = ROOT / "ScavengeNightmare_DesignDoc.html"
out.write_text(doc, encoding="utf-8")
print("written", out)
