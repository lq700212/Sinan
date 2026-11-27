"""设置模块：账号菜单 → 系统设置 → 分组标题齐全。

入口不写死用户名（现场可能换人），按「右上角 Button」定位：
刷新按钮在它左边，x 起点更小，用 min_x 排除。
"""

from __future__ import annotations

from ..runner import Context, case


@case('settings.open', module='settings', desc='账号菜单进入系统设置', order=130)
def settings_open(ctx: Context) -> None:
    ctx.ensure_shell()
    width, height = ctx.ui.screen
    account = ctx.ui.find(cls='Button', min_x=int(width * 0.8), max_y=int(height * 0.2))
    ctx.ui.tap(node=account)

    ctx.expect_text('系统设置')
    ctx.ui.tap('系统设置', contains=False)
    for title in ('关于', '外观', '数据', '本地数据库'):
        ctx.expect_text(title)

    ctx.ui.press_back()
    ctx.ensure_shell()
