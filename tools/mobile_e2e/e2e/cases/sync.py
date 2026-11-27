"""同步模块：只读检查真实配对状态与队列文案。

不在脚本里做真实配对：配对码是桌面端一次性生成的，脚本无法自己拿到；
强行写死会变成"假绿"。这里只守住「界面不撒谎」：
- 已配对必须出现「立即同步 / 解除配对」；
- 未配对必须出现配对入口；
- 任何时候不得出现已删掉的「同步服务未启用」。
"""

from __future__ import annotations

from ..runner import Context, case


@case('sync.status', module='sync', desc='同步页配对状态、队列与规则文案', order=120)
def sync_status(ctx: Context) -> None:
    ctx.goto_tab('同步')
    ctx.expect_no_text('同步服务未启用')

    tree = ctx.ui.tree
    paired = tree.has('已配对')
    unpaired = tree.has('未配对')
    ctx.expect(paired or unpaired, '同步页应明确显示已配对或未配对：\n%s' % tree.preview())
    ctx.expect_text('上行队列')
    ctx.expect_text('同步规则')
    ctx.expect_regex(r'本机缓存 \d+ 条问题')

    if paired:
        ctx.expect_text('立即同步')
        ctx.expect_text('解除配对')
        ctx.expect_regex(r'上次同步|尚未同步')
    else:
        ctx.expect_text('桌面端地址')
        ctx.expect_text('配对码')

    ctx.log('同步页状态：%s' % ('已配对' if paired else '未配对'))
