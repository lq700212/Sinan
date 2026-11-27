"""导航小动作：刷新、返回主壳。

刷新按钮会走真实 `AppState.refresh()`，成功后弹 SnackBar；
这条用例同时验证「刷新按钮可点 + 数据层没抛异常」。
"""

from __future__ import annotations

from ..runner import Context, case


@case('nav.refresh', module='nav', desc='刷新本地数据并出现 SnackBar', order=140)
def nav_refresh(ctx: Context) -> None:
    ctx.ensure_shell()
    ctx.ui.tap('刷新本地数据')
    ctx.expect_text('本地数据已刷新。', timeout=8)
    ctx.goto_tab('待办')
