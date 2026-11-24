# -*- coding: utf-8 -*-
"""把 pages/dialogs/css 片段拼进 ui_design_mockups.html（可重复执行）。"""
import io
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAIN = os.path.join(ROOT, "ui_design_mockups.html")
BUILD = os.path.join(ROOT, "_build")


def read(name):
    with io.open(os.path.join(BUILD, name), encoding="utf-8") as f:
        return f.read()


html = io.open(MAIN, encoding="utf-8").read()

# ---------- 1. CSS ----------
css = read("extra.css")
markers = (
    "/* ==========================================================================\n   司南 · 单方案 C",
    "/* ==========================================================================\n   本次项目基因校准",
)
start = -1
for m in markers:
    start = html.find(m)
    if start != -1:
        break
if start != -1:
    end = html.find("</style>", start)
    html = html[:start] + css + "\n" + html[end:]
else:
    idx = html.rfind("</style>")
    html = html[:idx] + "\n" + css + "\n" + html[idx:]

# ---------- 2. 页面 ----------
pages = "\n\n".join(read(n) for n in (
    "p7.html", "p1.html", "p2.html", "p3.html", "p4.html", "p5.html", "p8.html", "p9.html"
))
stage_open = html.find('<div class="stage" id="stage">')
stage_close = html.find("<!-- ==================== 总览视图", stage_open)
assert stage_open > 0 and stage_close > stage_open, "stage 边界未找到"
html = html[:stage_open] + '<div class="stage" id="stage">\n\n' + pages + "\n\n    </div>\n\n    " + html[stage_close:]


# ---------- 3. 弹窗（按 div 配平找到 modal-layer 结束） ----------
def find_block_end(text, open_tag_start):
    """从 <div ...> 起始位置开始，返回配平后的结束下标（</div> 之后）。"""
    i = open_tag_start
    depth = 0
    while True:
        nxt_open = text.find("<div", i)
        nxt_close = text.find("</div>", i)
        if nxt_close == -1:
            raise RuntimeError("div 不配平")
        if nxt_open != -1 and nxt_open < nxt_close:
            depth += 1
            i = nxt_open + 4
        else:
            depth -= 1
            i = nxt_close + 6
            if depth == 0:
                return i


modal_start = html.find('<div class="modal-layer" id="modalLayer">')
assert modal_start > 0, "modal-layer 未找到"
modal_end = find_block_end(html, modal_start)
dialogs = read("p6.html").rstrip()
assert dialogs.endswith("</div>")
html = html[:modal_start] + dialogs + html[modal_end:]

# ---------- 4. 单方案 C：壳层与方案名已写死在主 HTML，无需再替换 ----------

# ---------- 5. 默认浅色已由 HTML 标签与 CSS Token 决定，无需字符串替换 ----------

io.open(MAIN, "w", encoding="utf-8", newline="\n").write(html)
print("assembled:", MAIN, len(html), "chars")
print("pages:", html.count('class="page"'), "dialogs:", html.count('class="dlg-modal"'))
