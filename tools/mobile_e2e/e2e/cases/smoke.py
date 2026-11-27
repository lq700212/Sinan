"""冒烟：登录与四个主页面可达。

这两条是所有其它用例的前置。任何一次改动后先跑 `--module smoke`，
30 秒内就能确认「装得上、登得进、四个 tab 不白屏」。
"""

from __future__ import annotations

from ..runner import TAB_SUBTITLES, Context, case


@case('smoke.login', module='smoke', desc='登录进入待办（配合 --cold 验证冷启动）', order=10)
def smoke_login(ctx: Context) -> None:
    if ctx.options.cold:
        # 冷启动由 run.py 统一执行；这里要求确实停在登录页，防止"假冷启动"。
        ctx.expect_text('工号登录')
    ctx.relogin_if_needed()
    ctx.goto_tab('待办')
    ctx.expect_text('我的阶段任务')
    ctx.expect_text('本机待传')
    for tab in TAB_SUBTITLES:
        ctx.expect_text(tab, contains=False)


@case('smoke.tabs', module='smoke', desc='待办/项目/台账/同步 四页可达', order=20)
def smoke_tabs(ctx: Context) -> None:
    for tab, subtitle in TAB_SUBTITLES.items():
        ctx.goto_tab(tab)
        ctx.expect_text(subtitle)
    ctx.goto_tab('待办')
