"""新建问题：只验证表单入口与字段，不保存（零数据污染）。

新建并落库需要清理测试数据；手机端没有删除入口，清理成本高且容易误删。
所以常规回归只覆盖「打开 → 字段齐全 → 取消返回」；
真正的保存链路由 `mobile/test` 的规则测试和桌面端同步测试兜底。
"""

from __future__ import annotations

from ..runner import Context, case


@case('issue.form', module='issue', desc='新建问题表单入口、字段与取消（不保存）', order=150)
def issue_form(ctx: Context) -> None:
    ctx.ensure_shell()
    width, height = ctx.ui.screen
    # FAB 在底栏正中：用底栏 tab 的 bounds 推中心，避免写死分辨率
    tab = ctx.ui.tree.find('台账', contains=False, min_y=int(height * 0.8))
    _, y1, _, y2 = tab.bounds
    ctx.ui.tap(xy=(width // 2, (y1 + y2) // 2))

    ctx.ui.wait_for('新建问题')
    ctx.expect_text('选择或输入机台号')
    ctx.expect_text('计划完成')
    ctx.expect_text('保存并继续')
    ctx.expect_text('取消')

    ctx.ui.tap('取消', contains=False)
    ctx.ensure_shell()
