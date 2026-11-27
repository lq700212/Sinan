"""台账模块：搜索、筛选、详情。

搜索与筛选的数字全部从界面读取，再和「筛出 N 条 / 共 M 条」对账：
- 搜项目号：当前项目下所有问题都应命中，筛出数 = 总数；
- 搜不存在的字符串：必须落到空状态；
- 清空：必须恢复总数；
- 点「倒挂 N」：筛出数必须等于 chip 上的 N。
"""

from __future__ import annotations

import re

from ..runner import Context, case


def _edit_search(ctx: Context) -> None:
    """点中台账页唯一的搜索框（EditText）。"""
    edit = ctx.ui.find(cls='EditText')
    ctx.ui.tap(node=edit)


def _shown_is(ctx: Context, tree, expected: int) -> bool:
    """「筛出 N 条」是否等于 expected；None 不能当 0。"""
    shown, _ = ctx.parse_ledger_counts(tree)
    return shown is not None and shown == expected


@case('ledger.search', module='ledger', desc='台账搜索命中、空结果与清空恢复', order=90)
def ledger_search(ctx: Context) -> None:
    ctx.goto_tab('台账')
    ctx.reset_ledger()
    _, total = ctx.parse_ledger_counts(ctx.ui.tree)
    ctx.expect(total is not None, '台账页读不到「共 N 条」')
    if total == 0:
        ctx.skip('当前项目台账为 0 条，无法测搜索')

    code = ctx.current_project_code()
    ctx.goto_tab('台账')

    # 1) 搜项目号：当前项目下每条问题的机台号都带项目号
    _edit_search(ctx)
    ctx.ui.input_text(code)
    ctx.ui.wait_until(
        lambda tree: _shown_is(ctx, tree, total),
        description='搜索 %s 后筛出 %d 条' % (code, total),
    )
    ctx.log('搜索 %s：%d/%d 条' % (code, total, total))

    # 2) 搜一个必然不存在的串：必须出现空状态
    _edit_search(ctx)
    ctx.ui.clear_text()
    ctx.ui.input_text('ZZZ999')
    ctx.expect_text('没有匹配的问题')
    shown, _ = ctx.parse_ledger_counts(ctx.ui.tree)
    ctx.expect(shown == 0, '空结果时「筛出」应为 0，实际 %s' % shown)
    ctx.log('搜索 ZZZ999：0 条 + 空状态')

    # 3) 清空：恢复总数
    _edit_search(ctx)
    ctx.ui.clear_text()
    ctx.ui.wait_until(
        lambda tree: _shown_is(ctx, tree, total),
        description='清空搜索后恢复 %d 条' % total,
    )
    ctx.ui.hide_keyboard()
    ctx.ensure_shell()


@case('ledger.filter', module='ledger', desc='倒挂筛选数字与 chip 对账', order=100)
def ledger_filter(ctx: Context) -> None:
    ctx.goto_tab('台账')
    ctx.reset_ledger()
    chips = ctx.parse_chips(ctx.ui.tree)
    if '倒挂' not in chips:
        ctx.skip('台账没有「倒挂」筛选 chip')
    inverted = chips['倒挂']
    total = chips['全部']
    ctx.expect(
        inverted <= total,
        '倒挂 %d 条不应大于全部 %d 条' % (inverted, total),
    )
    if inverted == 0:
        ctx.skip('当前项目倒挂为 0 条，无法验证倒挂筛选')

    ctx.ui.tap('倒挂 %d' % inverted, contains=False)
    ctx.ui.wait_until(
        lambda tree: _shown_is(ctx, tree, inverted),
        description='倒挂筛选后筛出 %d 条' % inverted,
    )
    shown, grand_total = ctx.parse_ledger_counts(ctx.ui.tree)
    ctx.expect(
        grand_total == total,
        '筛选后「共 %s 条」应保持 %d 条' % (grand_total, total),
    )
    ctx.log('倒挂筛选：%d/%d 条' % (shown, total))

    ctx.ui.tap('全部 %d' % total, contains=False)
    ctx.ui.wait_until(
        lambda tree: _shown_is(ctx, tree, total),
        description='切回全部后恢复 %d 条' % total,
    )


@case('ledger.detail', module='ledger', desc='问题详情四阶段、返回列表', order=110)
def ledger_detail(ctx: Context) -> None:
    ctx.goto_tab('台账')
    ctx.reset_ledger()
    cards = ctx.ui.tree.find_all(regex=r'P\d{4}\d{3}', clickable=True)
    if not cards:
        ctx.skip('台账没有可点击的问题卡')
    ctx.ui.tap(node=cards[0])
    ctx.expect_text('四阶段')
    ctx.expect_text('现场记录')
    ctx.expect_text('推进阶段')
    ctx.expect_text('返回')
    tree = ctx.ui.tree
    ctx.expect(
        tree.has_regex(r'P\d{4}\d{3}'),
        '详情页应显示机台号：\n%s' % tree.preview(),
    )
    ctx.ui.press_back()
    ctx.goto_tab('台账')
    ctx.expect_regex(r'筛出 \d+ 条')
