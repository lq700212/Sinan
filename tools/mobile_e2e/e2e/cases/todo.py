"""待办页：KPI 与同步条必须来自真实状态。

不写死「30 条未闭环」这类数字，只校验格式与不变量：
- 未闭环数必须 ≤ 本机缓存总数；
- 同步条必须显示「本机缓存 N 条 · M 条待传」；
- 待传数不能是负数。
"""

from __future__ import annotations

import re

from ..runner import Context, case


@case('todo.summary', module='todo', desc='待办 KPI、空状态与同步条格式', order=30)
def todo_summary(ctx: Context) -> None:
    ctx.goto_tab('待办')
    ctx.expect_text('今天先处理')
    ctx.expect_text('今日已记录')
    ctx.expect_text('本机待传')
    ctx.expect_text('计划倒挂')

    tree = ctx.ui.tree
    sync_node = tree.find(regex=r'本机缓存 \d+ 条 · \d+ 条待传')
    match = re.search(r'本机缓存 (\d+) 条 · (\d+) 条待传', sync_node.raw_label)
    ctx.expect(match is not None, '待办同步条格式不符合预期：%r' % sync_node.raw_label)
    cached, pending = int(match.group(1)), int(match.group(2))
    ctx.expect(cached >= 0 and pending >= 0, '缓存/待传条数不能为负')

    # 未闭环数 ≤ 缓存总数；这条能抓住「按项目过滤后总数没跟着变」的回归
    open_node = tree.find(regex=r'\d+ 条未闭环')
    open_match = re.search(r'(\d+) 条未闭环', open_node.raw_label)
    if open_match:
        open_count = int(open_match.group(1))
        ctx.expect(
            open_count <= cached,
            '未闭环 %d 条不应大于本机缓存 %d 条' % (open_count, cached),
        )

    # 有数据时必须有「今天先处理」列表或明确空状态，不能两者都没有
    has_list = tree.has('去处理 ›')
    has_empty = tree.has('暂无待处理问题')
    ctx.expect(has_list or has_empty, '待办列表既无问题卡也无空状态')
