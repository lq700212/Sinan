"""项目模块：总览、跨页数字一致、分项目、切换、空项目。

多总项目是上一轮新增的能力，这里重点守住三条：
1. 项目页与台账页的「问题/倒挂」数字必须同源；
2. 切换后 hero 与各页数据必须真的换项目，不是只改标题；
3. 空项目必须显示 0 台 0 条与空状态，不能残留上一个项目的数据。
"""

from __future__ import annotations

import re

from ..runner import Context, case


@case('projects.overview', module='projects', desc='项目总览 hero、KPI 与分项目卡片', order=40)
def projects_overview(ctx: Context) -> None:
    label = ctx.project_hero()
    stats = ctx.parse_hero(label)
    ctx.expect(stats['code'] is not None, 'hero 缺少项目号：%r' % label)
    for key, name in (
        ('subprojects', '分项目'),
        ('machines', '设备'),
        ('issues', '问题'),
        ('inverted', '倒挂'),
    ):
        ctx.expect(stats[key] is not None, 'hero 缺少「%s」数字：%r' % (name, label))
    ctx.expect(stats['subprojects'] >= 1, '至少应有 1 个分项目')
    ctx.expect(
        stats['issues'] >= stats['inverted'],
        '问题总数 %d 不应小于倒挂 %d' % (stats['issues'], stats['inverted']),
    )
    ctx.expect_text('阶段填写率')
    ctx.expect_text('最近现场照片')
    ctx.expect_text('切换')
    ctx.expect_regex(r'P\d{4}-0[1-3]')


@case('projects.consistency', module='projects', desc='项目页与台账页数字同源', order=50)
def projects_consistency(ctx: Context) -> None:
    stats = ctx.parse_hero(ctx.project_hero())
    ctx.goto_tab('台账')
    tree = ctx.reset_ledger()
    chips = ctx.parse_chips(tree)
    ctx.expect('全部' in chips, '台账缺少「全部」筛选 chip')
    ctx.expect('倒挂' in chips, '台账缺少「倒挂」筛选 chip')
    ctx.expect(
        chips['全部'] == stats['issues'],
        '项目页问题 %d 条 ≠ 台账「全部」%d 条（按项目过滤不同源）'
        % (stats['issues'], chips['全部']),
    )
    ctx.expect(
        chips['倒挂'] == stats['inverted'],
        '项目页倒挂 %d 条 ≠ 台账「倒挂」%d 条'
        % (stats['inverted'], chips['倒挂']),
    )
    shown, total = ctx.parse_ledger_counts(tree)
    ctx.expect(
        shown == total == stats['issues'],
        '台账「筛出 %s / 共 %s」与项目页 %d 条不一致' % (shown, total, stats['issues']),
    )


@case('projects.subproject', module='projects', desc='分项目卡片进入详情并返回', order=60)
def projects_subproject(ctx: Context) -> None:
    ctx.goto_tab('项目')
    code = ctx.current_project_code()
    card = ctx.ui.find(regex=code + r'-0[1-3]')
    ctx.ui.tap(node=card)
    ctx.expect_text('返回')
    tree = ctx.ui.tree
    ctx.expect(
        tree.has('该分项目暂无问题') or tree.has_regex(code + r'\d{3}'),
        '分项目页既无问题卡也无空状态：\n%s' % tree.preview(),
    )
    ctx.ui.press_back()
    ctx.goto_tab('项目')
    ctx.expect_regex(r'阶段填写率')


@case('projects.switch', module='projects', desc='切换总项目后数据跟随，再切回', order=70)
def projects_switch(ctx: Context) -> None:
    current = ctx.current_project_code()
    rows = ctx.project_rows()
    if len(rows) < 2:
        ctx.skip('本机只有 %d 个总项目，无法测切换' % len(rows))
    target = next((row for row in rows if row.code != current), None)
    if target is None:
        ctx.skip('切换弹层里没有第二个总项目')

    try:
        ctx.switch_project(target.code)
        ctx.expect(
            ctx.current_project_code() == target.code,
            '切换后 hero 未显示 %s' % target.code,
        )
        hero_stats = ctx.parse_hero(ctx.project_hero())
        ctx.expect(
            hero_stats['issues'] == target.issue_count,
            '切换后项目页问题 %s 条 ≠ 弹层里的 %d 条'
            % (hero_stats['issues'], target.issue_count),
        )
        ctx.expect(
            hero_stats['machines'] == target.machine_count,
            '切换后项目页设备 %s 台 ≠ 弹层里的 %d 台'
            % (hero_stats['machines'], target.machine_count),
        )
    finally:
        try:
            ctx.switch_project(current)
        except Exception as exc:  # 清理失败只记录，避免覆盖原始断言错误
            ctx.log('切回 %s 失败：%s' % (current, exc))

    ctx.expect(
        ctx.current_project_code() == current,
        '未能切回原项目 %s' % current,
    )


@case('projects.empty', module='projects', desc='空总项目显示 0 台 0 条与空状态', order=80)
def projects_empty(ctx: Context) -> None:
    current = ctx.current_project_code()
    rows = ctx.project_rows()
    empty = next(
        (row for row in rows if row.issue_count == 0 and row.code != current), None
    )
    if empty is None:
        ctx.skip('本机没有空总项目，跳过空数据隔离检查')

    try:
        ctx.switch_project(empty.code)
        stats = ctx.parse_hero(ctx.project_hero())
        ctx.expect(stats['issues'] == 0, '空项目问题数应为 0，实际 %s' % stats['issues'])
        ctx.expect(stats['machines'] == 0, '空项目设备数应为 0，实际 %s' % stats['machines'])
        ctx.expect(stats['inverted'] == 0, '空项目倒挂数应为 0，实际 %s' % stats['inverted'])

        ctx.goto_tab('台账')
        shown, total = ctx.parse_ledger_counts(ctx.ui.tree)
        ctx.expect(
            shown == 0 and total == 0,
            '空项目台账应为 0/0，实际 %s/%s' % (shown, total),
        )
        ctx.expect_text('没有匹配的问题')

        ctx.goto_tab('待办')
        ctx.expect_text('暂无待处理问题')
    finally:
        try:
            ctx.switch_project(current)
        except Exception as exc:
            ctx.log('切回 %s 失败：%s' % (current, exc))

    ctx.expect(ctx.current_project_code() == current, '未能切回原项目 %s' % current)
