#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把设计文档里的核心表格导出成 Excel，供程序直接建表。"""
import pathlib
import re

from openpyxl import Workbook
from openpyxl.styles import Alignment, Font, PatternFill
from openpyxl.utils import get_column_letter

ROOT = pathlib.Path(__file__).resolve().parent
OUT = ROOT / "ScavengeNightmare_DesignTables_v1.2.xlsx"

HEAD_FILL = PatternFill("solid", fgColor="9A3412")
HEAD_FONT = Font(color="FFFFFF", bold=True)


def parse_tables(md: str):
    lines = md.splitlines()
    tables = []
    i = 0
    while i < len(lines):
        s = lines[i].strip()
        if s.startswith("|") and i + 1 < len(lines) and re.match(r"^\|[\s:|-]+\|$", lines[i + 1].strip()):
            header = [c.strip() for c in s.strip("|").split("|")]
            i += 2
            rows = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            tables.append((header, rows))
        else:
            i += 1
    return tables


def nearest_heading(md: str, table_index: int):
    """粗略定位表格最近的标题，用作 sheet 名备注。"""
    headings = re.findall(r"^#{2,4}\s+(.+)$", md, flags=re.M)
    return headings[min(table_index, len(headings) - 1)] if headings else f"table-{table_index}"


def clean(cell: str) -> str:
    return re.sub(r"\*\*|`", "", cell)


wb = Workbook()
wb.remove(wb.active)

docs = [
    ("怪物", "Monster_Roster_v1.md"),
    ("地图", "Maps_v1.md"),
    ("物品商店", "Items_Characters_Shop_v1.md"),
]

for sheet_prefix, filename in docs:
    md = (ROOT / filename).read_text(encoding="utf-8")
    tables = parse_tables(md)
    for idx, (header, rows) in enumerate(tables):
        title = nearest_heading(md, idx)[:24]
        name = f"{sheet_prefix}-{idx + 1:02d}-{re.sub(r'[\\/*?:\[\]]', '', title)}"[:31]
        ws = wb.create_sheet(name)
        ws.append([clean(h) for h in header])
        for r in rows:
            ws.append([clean(c) for c in r])

        for col in range(1, len(header) + 1):
            c = ws.cell(row=1, column=col)
            c.fill = HEAD_FILL
            c.font = HEAD_FONT
            c.alignment = Alignment(vertical="center")

        for col in range(1, len(header) + 1):
            width = max(len(str(ws.cell(row=r, column=col).value or "")) for r in range(1, min(len(rows) + 2, 60)))
            ws.column_dimensions[get_column_letter(col)].width = min(max(width * 1.6 + 2, 8), 46)

        ws.freeze_panes = "A2"
        ws.auto_filter.ref = f"A1:{get_column_letter(len(header))}{len(rows) + 1}"

wb.save(OUT)
print("written", OUT, "sheets", len(wb.sheetnames))
