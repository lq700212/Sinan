"""司南手机端 E2E 脚本 · 用例注册、上下文与执行器。

一个用例就是一个普通 Python 函数，通过 `@case(...)` 注册：

    @case('ledger.search', module='ledger', desc='台账搜索与空结果')
    def ledger_search(ctx):
        ctx.goto_tab('台账')
        ...

`Context` 提供界面高层动作（登录、切页、切项目、断言），让用例读起来接近
人工操作步骤；底层细节（dump、等待、坐标）都封装在 e2e/ui.py。
"""

from __future__ import annotations

import json
import os
import re
import time
import traceback
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Optional

from .adb import Adb
from .ui import UiError, UiNode, UiSession, UiTree

# 四个主页面顶栏副标题：比标题更唯一，用来确认页面真的切过去了。
TAB_SUBTITLES = {
    '待办': '我的阶段任务 · 今天先处理',
    '项目': '分项目 · 机台 · 风险清单',
    '台账': '搜索 · 筛选 · 推进阶段',
    '同步': '本机待传 · 配对 · 同步规则',
}

PROJECT_CODE_RE = re.compile(r'^P\d{4}\b')


class SkipCase(Exception):
    """用例主动跳过（环境不满足，例如只有一个总项目时无法测切换）。"""


@dataclass
class Case:
    id: str
    module: str
    desc: str
    fn: Callable[['Context'], None]
    order: int = 100
    mutating: bool = False
    tags: tuple[str, ...] = field(default_factory=tuple)


REGISTRY: dict[str, Case] = {}


def case(
    case_id: str,
    *,
    module: str,
    desc: str,
    order: int = 100,
    mutating: bool = False,
    tags: tuple[str, ...] = ('readonly',),
):
    """注册用例。id 全局唯一，支持用前缀过滤（如 `projects.`）。"""

    def decorator(fn: Callable[['Context'], None]):
        if case_id in REGISTRY:
            raise ValueError('用例 id 重复：%s' % case_id)
        REGISTRY[case_id] = Case(
            id=case_id,
            module=module,
            desc=desc,
            fn=fn,
            order=order,
            mutating=mutating,
            tags=tags,
        )
        return fn

    return decorator


@dataclass
class ProjectRow:
    code: str
    subproject_count: int
    machine_count: int
    issue_count: int
    is_current: bool

    @property
    def metric(self) -> str:
        return '%d 个分项目 · %d 台设备 · %d 条问题' % (
            self.subproject_count,
            self.machine_count,
            self.issue_count,
        )


@dataclass
class CaseResult:
    case_id: str
    module: str
    desc: str
    status: str
    duration: float
    error: str = ''
    artifacts: str = ''

    @property
    def emoji(self) -> str:
        return {'pass': 'PASS', 'fail': 'FAIL', 'skip': 'SKIP'}.get(self.status, '????')


class Context:
    """用例上下文：设备、UI 会话、配置与断言辅助。"""

    def __init__(
        self,
        adb: Adb,
        ui: UiSession,
        options,
        *,
        artifacts_dir: Path,
        verbose: bool = False,
    ) -> None:
        self.adb = adb
        self.ui = ui
        self.options = options
        self.artifacts_dir = artifacts_dir
        self.verbose = verbose
        self.case: Optional[Case] = None
        self.expect_timeout = 12.0
        self.steps: list[str] = []
        self._project_code_cache: Optional[str] = None

    # ---------- 日志 ----------

    def log(self, message: str) -> None:
        self.steps.append(message)
        if self.verbose:
            print('        · %s' % message, flush=True)

    # ---------- 基础状态 ----------

    @property
    def package(self) -> str:
        return self.options.package

    @property
    def activity(self) -> str:
        return self.options.activity

    def _is_login(self, tree: UiTree) -> bool:
        return tree.has('工号登录') or tree.has('进入现场端')

    def _has_bottom_nav(self, tree: UiTree) -> bool:
        h = self.ui.screen[1]
        return bool(tree.find_all('待办', contains=False, min_y=int(h * 0.8)))

    def _has_overlay(self, tree: UiTree) -> bool:
        # 项目切换弹层、账号菜单、系统设置页都带这些独有文案
        return (
            tree.has('切换总项目')
            or tree.has('关闭菜单')
            or (tree.has('系统设置') and tree.has('退出登录'))
        )

    def relogin_if_needed(self) -> None:
        """确保回到已登录的主壳；登录页会自动登录，菜单/弹层会自动关闭。"""
        self.ensure_shell()

    def login(self, username: Optional[str] = None, password: Optional[str] = None) -> None:
        """在登录页输入账号密码并进入主壳。幂等：已登录直接返回。"""
        username = username or self.options.username
        password = password or self.options.password
        tree = self.ui.dump()
        if not self._is_login(tree):
            return

        edits = [n for n in tree.nodes() if 'EditText' in n.cls and n.visible]
        if not edits:
            raise UiError('登录页找不到输入框。当前界面：\n%s' % tree.preview())

        account = edits[0]
        if account.text.strip() != username:
            self.ui.tap(node=account)
            self.ui.clear_text()
            self.ui.input_text(username)

        password_node = next((n for n in edits if n.password), edits[-1])
        self.ui.tap(node=password_node)
        self.ui.clear_text()
        self.ui.input_text(password)
        self.ui.press_enter()

        # 登录成功后主壳首帧可能还在建库，给足时间；失败时 wait_for 会把错误提示一起打出来
        self.ui.wait_for('我的阶段任务', timeout=25)
        self.log('登录成功：%s' % username)

    def ensure_shell(self) -> UiTree:
        """确保回到主壳（四个底栏 tab 可见）。弹层/详情/设置页会自动退回。"""
        for _ in range(6):
            tree = self.ui.dump()
            if self._is_login(tree):
                self.login()
                continue
            if self._has_overlay(tree):
                self.ui.press_back()
                continue
            if self._has_bottom_nav(tree):
                return tree
            # 键盘占掉了底栏：先收键盘，别把主壳 back 到桌面
            if self.ui.keyboard_shown():
                self.ui.hide_keyboard()
                continue
            # 不在司南界面（桌面/其它应用）：按 back 只会越退越远，必须主动拉起应用。
            # 覆盖安装后设备可能停在桌面，脚本要能自己恢复。
            if self.adb.foreground_package() != self.package:
                self.log('前台不是 %s，主动启动应用' % self.package)
                self.adb.start_app(self.package, self.activity, wait=1.5)
                self.ui.invalidate()
                continue
            # 详情/新建等二级页：返回主壳
            self.ui.press_back()
        raise UiError('连续 6 次仍未回到主壳。当前界面：\n%s' % self.ui.describe())

    def goto_tab(self, tab: str) -> UiTree:
        """切到底部四个主页面之一，并等待副标题确认。

        先看缓存树：如果上一屏就是主壳且没有弹层，直接点底栏，
        省掉一次 2 秒级的 uiautomator dump。
        """
        if tab not in TAB_SUBTITLES:
            raise ValueError('未知 tab：%s' % tab)
        cached = self.ui.cached_tree
        # 已经停在目标页：直接复用，避免重复点击底栏（每次点击后都要重 dump）。
        # 必须排除弹层，否则弹层背后的页面会被误判为"已在该页"。
        if (
            cached is not None
            and not self._has_overlay(cached)
            and cached.has(TAB_SUBTITLES[tab])
        ):
            return cached
        need_shell = (
            cached is None
            or self._is_login(cached)
            or self._has_overlay(cached)
            or not self._has_bottom_nav(cached)
        )
        if need_shell:
            self.ensure_shell()
        self.ui.tap_tab(tab)
        self.ui.wait_for(TAB_SUBTITLES[tab], timeout=12)
        self.log('切到「%s」' % tab)
        return self.ui.tree

    # ---------- 项目 ----------

    def _hero_code(self, tree: UiTree) -> Optional[str]:
        nodes = tree.find_all('现场记录 · 离线优先 · 桌面端为主库')
        if not nodes:
            return None
        match = PROJECT_CODE_RE.match(nodes[0].raw_label)
        return match.group(0) if match else None

    def current_project_code(self) -> str:
        """读取当前总项目号（项目页 hero 首行）。结果在本次运行内缓存。"""
        if self._project_code_cache:
            return self._project_code_cache
        self.goto_tab('项目')
        code = self._hero_code(self.ui.tree)
        if not code:
            raise UiError('项目页 hero 中读不到项目号。当前界面：\n%s' % self.ui.describe())
        self._project_code_cache = code
        return code

    def project_hero(self) -> str:
        """返回项目页 hero 的原始多行文本。"""
        self.goto_tab('项目')
        node = self.ui.find('现场记录 · 离线优先 · 桌面端为主库')
        return node.raw_label

    def reset_ledger(self) -> UiTree:
        """把台账恢复到「无搜索词 + 全部筛选」。

        IndexedStack 会保留页面状态：上一次跑失败留下的搜索词或「倒挂」筛选，
        会让下一次运行读到的「筛出 N 条」不是全量。每个台账用例开头统一重置。
        """
        tree = self.ui.tree
        edits = tree.find_all(cls='EditText')
        if edits and edits[0].text.strip():
            self.ui.tap(node=edits[0])
            self.ui.clear_text()
            self.ui.hide_keyboard()
            tree = self.ui.dump()

        chips = self.parse_chips(tree)
        total = chips.get('全部')
        if total is None:
            return tree
        self.ui.tap('全部 %d' % total, contains=False)
        self.ui.wait_until(
            lambda current: (self.parse_ledger_counts(current)[0] == total),
            description='台账恢复全部 %d 条' % total,
        )
        return self.ui.tree

    def project_rows(self) -> list[ProjectRow]:
        """打开切换弹层，读出全部总项目，然后关闭弹层。"""
        self.goto_tab('项目')
        self.ui.tap('切换', contains=False)
        self.ui.wait_for('切换总项目')
        tree = self.ui.tree
        rows: list[ProjectRow] = []
        for node in tree.find_all(clickable=True):
            label = node.raw_label
            match = PROJECT_CODE_RE.match(label)
            if not match or '个分项目' not in label:
                continue
            metric = re.search(
                r'(\d+) 个分项目 · (\d+) 台设备 · (\d+) 条问题', label
            )
            if not metric:
                continue
            rows.append(ProjectRow(
                code=match.group(0),
                subproject_count=int(metric.group(1)),
                machine_count=int(metric.group(2)),
                issue_count=int(metric.group(3)),
                is_current='当前' in label,
            ))
        self.ui.tap('关闭', contains=False)
        self.ui.wait_for('切换总项目', gone=True)
        self.log('读到 %d 个总项目：%s' % (len(rows), ', '.join(r.code for r in rows)))
        return rows

    def ensure_data_project(self) -> Optional[str]:
        """如果当前项目是空的，临时切到问题最多的项目；返回需要恢复的原项目号。

        空项目下台账、搜索、筛选都没有可测数据，直接跑会产生一堆"跳过"。
        统一在开跑前处理一次，结束后由 run_cases 恢复用户原来的项目。
        """
        current = self.current_project_code()
        rows = self.project_rows()
        current_row = next((row for row in rows if row.code == current), None)
        if current_row is not None and current_row.issue_count > 0:
            return None
        data_rows = [row for row in rows if row.issue_count > 0]
        if not data_rows:
            self.log('所有总项目都是 0 条问题，跳过了数据项目准备')
            return None
        target = max(data_rows, key=lambda row: row.issue_count)
        self.switch_project(target.code)
        self.log('原项目 %s 为空，临时切到 %s 跑回归' % (current, target.code))
        return current

    def switch_project(self, code: str) -> None:
        """切到指定总项目，并确认 hero 已刷新。"""
        self.goto_tab('项目')
        current = self.current_project_code()
        if current == code:
            self.log('已经在 %s，无需切换' % code)
            return

        self.ui.tap('切换', contains=False)
        self.ui.wait_for('切换总项目')
        row_node: Optional[UiNode] = None
        for node in self.ui.tree.find_all(clickable=True):
            label = node.raw_label
            match = PROJECT_CODE_RE.match(label)
            if match and match.group(0) == code and '个分项目' in label:
                row_node = node
                break
        if row_node is None:
            raise UiError('切换弹层里找不到总项目 %s' % code)
        self.ui.tap(node=row_node)
        self.ui.wait_for('切换总项目', gone=True)
        self.ui.wait_until(
            lambda tree: self._hero_code(tree) == code,
            description='项目页显示 %s' % code,
            timeout=15,
        )
        self._project_code_cache = code
        self.log('已切换 %s → %s' % (current, code))

    # ---------- 断言 ----------

    def expect(self, condition: bool, message: str) -> None:
        if not condition:
            raise AssertionError(message)

    def expect_text(self, value: str, *, contains: bool = True, timeout: Optional[float] = None) -> UiNode:
        # 刚 dump 过的同一屏直接复用，避免每次断言都吃一次 2 秒的 dump
        cached = self.ui.cached_tree
        if cached is not None:
            matches = cached.find_all(value, contains=contains)
            if matches:
                return matches[0]
        return self.ui.wait_for(
            value, contains=contains, timeout=timeout or self.expect_timeout
        )

    def expect_regex(self, pattern: str, *, timeout: Optional[float] = None) -> UiNode:
        # 与 expect_text 一致：同一屏的缓存直接复用，省掉一次 2 秒 dump
        cached = self.ui.cached_tree
        if cached is not None:
            matches = cached.find_all(regex=pattern)
            if matches:
                return matches[0]
        return self.ui.wait_for(
            regex=pattern, timeout=timeout or self.expect_timeout
        )

    def expect_no_text(self, value: str, *, contains: bool = True) -> None:
        tree = self.ui.cached_tree or self.ui.dump()
        self.expect(
            not tree.has(value, contains=contains),
            '界面上不应出现「%s」，但找到了。当前界面：\n%s' % (value, tree.preview()),
        )

    def expect_gone(self, value: str, *, contains: bool = True, timeout: Optional[float] = None) -> None:
        self.ui.wait_for(
            value, contains=contains, gone=True, timeout=timeout or self.expect_timeout
        )

    def skip(self, reason: str) -> None:
        raise SkipCase(reason)

    # ---------- 解析 ----------

    @staticmethod
    def parse_hero(label: str) -> dict:
        """把项目页 hero 文本解析成 {code, subprojects, machines, issues, inverted}。

        不写死 36/31 这类业务数字，全部从当前界面解析，保证切项目后依然成立。
        """
        normalized = label.replace('\n', ' ')
        code_match = PROJECT_CODE_RE.match(label)
        result = {
            'code': code_match.group(0) if code_match else None,
            'subprojects': None,
            'machines': None,
            'issues': None,
            'inverted': None,
        }
        patterns = {
            'subprojects': r'(\d+)\s+分项目',
            'machines': r'(\d+)\s+台\s+设备',
            'issues': r'(\d+)\s+条\s+问题',
            'inverted': r'(\d+)\s+条\s+倒挂',
        }
        for key, pattern in patterns.items():
            match = re.search(pattern, normalized)
            if match:
                result[key] = int(match.group(1))
        return result

    @staticmethod
    def parse_ledger_counts(tree: UiTree) -> tuple[Optional[int], Optional[int]]:
        """从台账页「筛出 N 条 / 共 M 条」解析 (N, M)。"""
        shown = total = None
        for node in tree.nodes():
            label = node.raw_label
            match = re.search(r'筛出\s*(\d+)\s*条', label)
            if match:
                shown = int(match.group(1))
            match = re.search(r'共\s*(\d+)\s*条', label)
            if match:
                total = int(match.group(1))
        return shown, total

    @staticmethod
    def parse_chips(tree: UiTree) -> dict[str, int]:
        """解析台账筛选 chip：「全部 36」「倒挂 5」…（只取按钮节点）。"""
        chips: dict[str, int] = {}
        for node in tree.nodes():
            if 'Button' not in node.cls:
                continue
            label = node.label
            match = re.fullmatch(r'(全部|倒挂|文本|有照片|逾期)\s+(\d+)', label)
            if match:
                chips[match.group(1)] = int(match.group(2))
        return chips


# ---------------------------------------------------------------------------
# 执行器
# ---------------------------------------------------------------------------


def select_cases(
    *,
    modules: Optional[list[str]] = None,
    ids: Optional[list[str]] = None,
    tags: Optional[list[str]] = None,
) -> list[Case]:
    """按 module / id 前缀 / tag 过滤，按 order 再按 id 排序。"""
    cases = sorted(REGISTRY.values(), key=lambda c: (c.order, c.id))
    if modules:
        wanted = {m.strip() for m in modules if m.strip()}
        cases = [c for c in cases if c.module in wanted]
    if ids:
        prefixes = tuple(i.strip() for i in ids if i.strip())
        cases = [c for c in cases if c.id.startswith(prefixes)]
    if tags:
        wanted_tags = {t.strip() for t in tags if t.strip()}
        cases = [c for c in cases if wanted_tags.intersection(c.tags)]
    return cases


def run_cases(
    cases: list[Case],
    *,
    adb: Adb,
    options,
    artifacts_dir: Path,
    verbose: bool = False,
    stop_on_fail: bool = False,
) -> list[CaseResult]:
    """顺序执行用例。单个失败只记录，不中断整轮（可用 --stop-on-fail 改变）。"""
    ui = UiSession(adb, log=(lambda m: print('        · ' + m, flush=True)) if verbose else None)
    ctx = Context(adb, ui, options, artifacts_dir=artifacts_dir, verbose=verbose)

    # 全局准备：确认设备、按需冷启动、确保登录
    print('设备：%s' % adb.serial, flush=True)
    print('屏幕：%dx%d' % ui.screen, flush=True)
    if getattr(options, 'cold', False):
        print('冷启动：force-stop + am start …', flush=True)
        adb.stop_app(options.package)
        time.sleep(1)
        adb.start_app(options.package, options.activity, wait=1.5)
        ui.invalidate()
    else:
        # 非冷启动先确保已登录，让首个用例不用自己处理登录页；
        # --cold 时故意留给 smoke.login 验证「冷启动必须停在登录页」。
        ctx.relogin_if_needed()

    # 空项目没有可测数据：临时切到有数据的项目，跑完恢复。
    # 冷启动 + smoke.login 时要保留登录页，等它验证完再准备。
    restore_project: Optional[str] = None
    prepared = False
    results: list[CaseResult] = []
    try:
        for index, item in enumerate(cases, start=1):
            if not prepared and (not getattr(options, 'cold', False) or item.id != 'smoke.login'):
                try:
                    restore_project = ctx.ensure_data_project()
                except Exception as exc:  # 准备失败不应阻断整轮，交给具体用例报错
                    print('项目准备失败（继续执行）：%s' % exc, flush=True)
                prepared = True

            prefix = '[%d/%d] %s' % (index, len(cases), item.id)
            print('%s  %s' % (prefix, item.desc), flush=True)
            started = time.time()
            ctx.case = item
            ctx.steps = []
            status = 'pass'
            error = ''
            artifact = ''

            try:
                if item.mutating and not getattr(options, 'mutating', False):
                    raise SkipCase('写操作用例，需显式 --mutating')
                item.fn(ctx)
            except SkipCase as exc:
                status = 'skip'
                error = str(exc)
            except Exception as exc:  # noqa: BLE001 - 用例失败要完整记录，不能吞
                status = 'fail'
                error = '%s: %s' % (type(exc).__name__, exc)
                artifact = _save_artifacts(ui, item.id, artifacts_dir, options)
                if verbose:
                    traceback.print_exc()

            duration = time.time() - started
            result = CaseResult(
                case_id=item.id,
                module=item.module,
                desc=item.desc,
                status=status,
                duration=duration,
                error=error,
                artifacts=artifact,
            )
            results.append(result)
            _print_result(result)

            if status == 'fail' and stop_on_fail:
                print('已按 --stop-on-fail 中止。', flush=True)
                break
    finally:
        if restore_project:
            try:
                ctx.switch_project(restore_project)
                print('已恢复原项目 %s' % restore_project, flush=True)
            except Exception as exc:
                print('警告：恢复原项目 %s 失败：%s' % (restore_project, exc), flush=True)

    _print_summary(results)
    return results


def _print_result(result: CaseResult) -> None:
    if result.status == 'pass':
        print('        PASS  %.2fs' % result.duration, flush=True)
    elif result.status == 'skip':
        print('        SKIP  %s' % result.error, flush=True)
    else:
        print('        FAIL  %.2fs' % result.duration, flush=True)
        for line in (result.error or '').splitlines():
            print('              %s' % line, flush=True)
        if result.artifacts:
            print('        产物：%s' % result.artifacts, flush=True)


def _print_summary(results: list[CaseResult]) -> None:
    passed = sum(1 for r in results if r.status == 'pass')
    failed = [r for r in results if r.status == 'fail']
    skipped = sum(1 for r in results if r.status == 'skip')
    total_time = sum(r.duration for r in results)
    print('', flush=True)
    print('=' * 62, flush=True)
    print(
        '结果：PASS %d / FAIL %d / SKIP %d，共 %d 条，用时 %.1fs'
        % (passed, len(failed), skipped, len(results), total_time),
        flush=True,
    )
    if failed:
        print('失败用例：', flush=True)
        for item in failed:
            print('  - %s  %s' % (item.case_id, item.error.splitlines()[0]), flush=True)
    print('=' * 62, flush=True)


def _save_artifacts(ui: UiSession, case_id: str, artifacts_dir: Path, options) -> str:
    """失败时保存截图 + UI 树，方便复现；默认开启，可用 --no-artifacts 关闭。"""
    if getattr(options, 'no_artifacts', False):
        return ''
    stamp = time.strftime('%H%M%S')
    folder = artifacts_dir / ('%s_%s' % (case_id.replace('.', '_'), stamp))
    try:
        folder.mkdir(parents=True, exist_ok=True)
        screenshot = folder / 'screen.png'
        tree_file = folder / 'ui.xml'
        try:
            ui.screenshot(str(screenshot))
        except Exception as exc:  # 截图失败不影响原始错误
            (folder / 'screenshot_error.txt').write_text(str(exc), encoding='utf-8')
        try:
            ui.save_tree(str(tree_file))
        except Exception as exc:
            (folder / 'tree_error.txt').write_text(str(exc), encoding='utf-8')
        return str(folder)
    except Exception as exc:  # 产物写入失败也不能覆盖原用例错误
        return '（产物写入失败：%s）' % exc


def dump_results_json(results: list[CaseResult], path: str) -> None:
    payload = {
        'generatedAt': time.strftime('%Y-%m-%d %H:%M:%S'),
        'summary': {
            'pass': sum(1 for r in results if r.status == 'pass'),
            'fail': sum(1 for r in results if r.status == 'fail'),
            'skip': sum(1 for r in results if r.status == 'skip'),
        },
        'cases': [
            {
                'id': r.case_id,
                'module': r.module,
                'desc': r.desc,
                'status': r.status,
                'duration': round(r.duration, 2),
                'error': r.error,
                'artifacts': r.artifacts,
            }
            for r in results
        ],
    }
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding='utf-8')
