# -*- coding: utf-8 -*-
import io
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HTML = os.path.join(ROOT, "ui_design_mockups.html")
SHOT = os.path.join(ROOT, "_build", "shot.html")
OUT = os.path.join(ROOT, "_build", "shots")
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
os.makedirs(OUT, exist_ok=True)

# 截图副本：关闭动画，避免无头浏览器把首帧 opacity:0 截下来
src = io.open(HTML, encoding="utf-8").read()
src = src.replace(
    "</head>",
    "<style>*,*::before,*::after{animation:none!important;transition:none!important}</style></head>",
    1,
)
io.open(SHOT, "w", encoding="utf-8", newline="\n").write(src)

shots = [
    ("00_projects", "page=projects&variant=c&scheme=light&view=stage"),
    ("00b_project", "page=project&variant=c&scheme=light&view=stage"),
    ("01_main", "page=main&variant=c&scheme=light&view=stage"),
    ("02_issues", "page=issues&variant=c&scheme=light&view=stage"),
    ("03_detail", "page=detail&variant=c&scheme=light&view=stage"),
    ("04_import", "page=import&variant=c&scheme=light&view=stage"),
    ("05_export", "page=export&variant=c&scheme=light&view=stage"),
    ("06_report", "page=report&variant=c&scheme=light&view=stage"),
    ("07_settings", "page=settings&variant=c&scheme=light&view=stage"),
    ("07b_m_login", "page=m-login&variant=c&scheme=light&view=stage"),
    ("08_m_home", "page=m-home&variant=c&scheme=light&view=stage"),
    ("09_m_list", "page=m-list&variant=c&scheme=light&view=stage"),
    ("10_m_detail", "page=m-detail&variant=c&scheme=light&view=stage"),
    ("11_m_new", "page=m-new&variant=c&scheme=light&view=stage"),
    ("12_m_scan", "page=m-scan&variant=c&scheme=light&view=stage"),
    ("13_m_sync", "page=m-sync&variant=c&scheme=light&view=stage"),
    ("28_m_projects", "page=m-projects&variant=c&scheme=light&view=stage"),
    ("29_m_project", "page=m-project&variant=c&scheme=light&view=stage"),
    ("30_m_photo", "page=m-photo&variant=c&scheme=light&view=stage"),
    ("14_overview", "page=main&variant=c&scheme=light&view=overview"),
    ("15_flow", "page=main&variant=c&scheme=light&view=flow"),
    ("16_spec", "page=main&variant=c&scheme=light&view=spec"),
    ("17_dlg_new", "page=issues&variant=c&scheme=light&view=stage&dialog=dlg-new"),
    ("18_dlg_bind", "page=settings&variant=c&scheme=light&view=stage&dialog=dlg-bind"),
    ("19_dlg_import", "page=import&variant=c&scheme=light&view=stage&dialog=dlg-import-conflict"),
    ("31_dlg_photo", "page=detail&variant=c&scheme=light&view=stage&dialog=dlg-photo"),
    ("32_dlg_subproject", "page=projects&variant=c&scheme=light&view=stage&dialog=dlg-subproject"),
    ("37_projects_c_dark", "page=projects&variant=c&scheme=dark&view=stage"),
    ("38_report_c_dark", "page=report&variant=c&scheme=dark&view=stage"),
    ("39_m_projects_c_dark", "page=m-projects&variant=c&scheme=dark&view=stage"),
]

only = sys.argv[1:] if len(sys.argv) > 1 else None
for name, query in shots:
    if only and name not in only:
        continue
    url = "file:///E:/Project/Sinan/docs/_build/shot.html?" + query
    png = os.path.join(OUT, name + ".png")
    cmd = [
        EDGE, "--headless", "--disable-gpu", "--hide-scrollbars",
        "--window-size=1920,1200", "--virtual-time-budget=3500",
        "--screenshot=" + png, url,
    ]
    subprocess.run(cmd, capture_output=True, timeout=60)
    ok = os.path.exists(png) and os.path.getsize(png) > 20000
    print("%-16s %s %s" % (name, "OK" if ok else "FAIL", os.path.getsize(png) if os.path.exists(png) else 0))
