#!/usr/bin/env python
"""司南手机端 E2E · 命令行入口。

常用：
    python tools/mobile_e2e/run.py --list
    python tools/mobile_e2e/run.py --module smoke
    python tools/mobile_e2e/run.py --module projects,ledger
    python tools/mobile_e2e/run.py --case projects.switch
    python tools/mobile_e2e/run.py                 # 跑全部只读用例
    python tools/mobile_e2e/run.py --cold          # 先 force-stop 再跑
    python tools/mobile_e2e/run.py --json out.json # 额外输出机器可读结果

设计目标：单模块几十秒内出结果；失败自动留截图和 UI 树；不改业务代码、不装第三方库。
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

from e2e import adb as adb_module  # noqa: E402
from e2e.cases import *  # noqa: F401,F403,E402 - 导入即注册用例
from e2e.runner import (  # noqa: E402
    REGISTRY,
    dump_results_json,
    run_cases,
    select_cases,
)
from e2e.ui import UiError  # noqa: E402


def _split(values: list[str] | None) -> list[str] | None:
    """把 `--module a,b --module c` 拆成 ['a','b','c']。"""
    if not values:
        return None
    result: list[str] = []
    for value in values:
        result.extend(part.strip() for part in value.split(',') if part.strip())
    return result or None


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description='司南手机端 E2E 回归（基于 adb + uiautomator，不依赖截图读图）',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument('--adb', help='adb 可执行文件路径；默认自动发现')
    parser.add_argument('--device', help='设备 serial；只有一台时自动选择')
    parser.add_argument('--package', default='com.sinan.sinan_mobile')
    parser.add_argument('--activity', default='com.sinan.sinan_mobile/.MainActivity')
    parser.add_argument('--install', metavar='APK', help='先安装指定 APK（会 -r 覆盖）')
    parser.add_argument('--list', action='store_true', help='只列出用例，不连设备')
    parser.add_argument('--module', action='append', help='模块，可逗号分隔或重复')
    parser.add_argument('--case', action='append', dest='cases', help='用例 id 或前缀，可逗号分隔')
    parser.add_argument('--tag', action='append', help='标签过滤，如 readonly')
    parser.add_argument('--mutating', action='store_true', help='允许执行写操作用例')
    parser.add_argument('--cold', action='store_true', help='开始前 force-stop 并重新启动应用')
    parser.add_argument('--stop-on-fail', action='store_true', help='首个失败后停止')
    parser.add_argument('--no-artifacts', action='store_true', help='失败时不保存截图/UI 树')
    parser.add_argument('--json', metavar='PATH', help='把结果写成 JSON')
    parser.add_argument('--verbose', '-v', action='store_true', help='打印每一步动作与堆栈')
    parser.add_argument('--username', default='engineer', help='登录账号（默认演示工程师）')
    parser.add_argument('--password', default='123456', help='登录密码（默认演示密码）')
    return parser


def print_cases(cases) -> None:
    print('%-24s %-10s %-12s %-9s %s' % ('ID', 'MODULE', 'TAGS', 'MUTATING', 'DESC'))
    print('-' * 100)
    for item in cases:
        print('%-24s %-10s %-12s %-9s %s' % (
            item.id,
            item.module,
            ','.join(item.tags),
            'yes' if item.mutating else 'no',
            item.desc,
        ))


def main(argv: list[str] | None = None) -> int:
    # Windows 控制台默认 GBK：stdout/stderr 都强制 UTF-8，
    # 否则中文步骤日志和异常信息会变成乱码。
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, 'reconfigure'):
            try:
                stream.reconfigure(encoding='utf-8', errors='replace')
            except Exception:
                pass

    args = build_parser().parse_args(argv)

    if args.list:
        print_cases(select_cases(
            modules=_split(args.module),
            ids=_split(args.cases),
            tags=_split(args.tag),
        ))
        return 0

    cases = select_cases(
        modules=_split(args.module),
        ids=_split(args.cases),
        tags=_split(args.tag),
    )
    if not cases:
        print('没有匹配的用例。用 --list 查看全部用例。')
        return 2

    try:
        adb = adb_module.Adb(args.adb, args.device)
        if args.install:
            print('安装 APK：%s' % args.install, flush=True)
            adb.install(args.install)
            # 覆盖安装会杀掉旧进程；先拉起到登录页并给系统一点时间，
            # 避免脚本的第一次点击撞上应用重建（登录态丢失、点击落空）。
            adb.start_app(args.package, args.activity, wait=1.5)
        # 触发一次设备发现，先给出清晰报错
        print('使用 adb：%s' % adb.adb_path, flush=True)
        adb.serial  # noqa: B018 - 属性访问即校验
    except adb_module.AdbError as exc:
        print('ADB 初始化失败：%s' % exc, flush=True)
        return 2

    artifacts_dir = HERE / 'artifacts'
    try:
        results = run_cases(
            cases,
            adb=adb,
            options=args,
            artifacts_dir=artifacts_dir,
            verbose=args.verbose,
            stop_on_fail=args.stop_on_fail,
        )
    except (adb_module.AdbError, UiError) as exc:
        print('执行中断：%s' % exc, flush=True)
        return 2

    if args.json:
        dump_results_json(results, args.json)
        print('JSON 结果：%s' % args.json, flush=True)

    return 1 if any(item.status == 'fail' for item in results) else 0


if __name__ == '__main__':
    raise SystemExit(main())
