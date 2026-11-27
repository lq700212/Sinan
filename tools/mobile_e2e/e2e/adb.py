"""司南手机端 E2E 脚本 · ADB 最小封装。

设计原则：
- 只依赖 Python 标准库，Windows / macOS / Linux 通用，不装 appium、不装 pytest；
- 所有命令都经过 `_run`，统一超时、UTF-8 解码与错误信息；
- 截图用 `exec-out screencap` 直接取二进制，避免 PowerShell 重定向损坏 PNG；
- 设备不写死，按「显式参数 → 环境变量 → 常见安装路径 → PATH」顺序发现。
"""

from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path
from typing import Iterable, Optional


class AdbError(RuntimeError):
    """ADB 命令失败（非 0 退出码或设备不可用）。"""


def resolve_adb(explicit: Optional[str] = None) -> str:
    """按优先级定位 adb.exe / adb。

    显式传入的路径必须存在；其余候选不存在就跳过。找不到时给出可操作的报错，
    而不是抛一个看不懂的 FileNotFoundError。
    """
    candidates: list[str] = []
    if explicit:
        candidates.append(explicit)
    env = os.environ.get('ADB')
    if env:
        candidates.append(env)

    local_appdata = os.environ.get('LOCALAPPDATA', '')
    home = str(Path.home())
    candidates.extend([
        r'E:\Android\Sdk\platform-tools\adb.exe',
        os.path.join(local_appdata, 'Android', 'Sdk', 'platform-tools', 'adb.exe'),
        os.path.join(home, 'AppData', 'Local', 'Android', 'Sdk', 'platform-tools', 'adb.exe'),
        os.path.join(home, 'Library', 'Android', 'sdk', 'platform-tools', 'adb'),
        os.path.join(home, 'Android', 'Sdk', 'platform-tools', 'adb'),
        '/usr/lib/android-sdk/platform-tools/adb',
        '/opt/android-sdk/platform-tools/adb',
    ])

    for candidate in candidates:
        if candidate and os.path.isfile(candidate):
            return str(Path(candidate))

    found = shutil.which('adb')
    if found:
        return found

    raise AdbError(
        '找不到 adb。请用 --adb 指定路径，或设置环境变量 ADB，'
        '或把 platform-tools 加入 PATH。'
    )


class Adb:
    """单设备 ADB 会话。

    只封装 E2E 需要的动作：shell、exec-out、设备发现、启停应用、截图、安装。
    `serial` 为空时自动选择唯一在线设备；多设备会要求显式指定，防止点错机器。
    """

    def __init__(self, adb_path: Optional[str] = None, serial: Optional[str] = None) -> None:
        self.adb_path = resolve_adb(adb_path)
        self._serial = serial or None
        self._screen: Optional[tuple[int, int]] = None

    # ---------- 基础执行 ----------

    def _base(self) -> list[str]:
        cmd = [self.adb_path]
        if self._serial:
            cmd.extend(['-s', self._serial])
        return cmd

    def _run(self, args: Iterable[str], timeout: float = 60, binary: bool = False):
        cmd = self._base() + [str(a) for a in args]
        try:
            proc = subprocess.run(
                cmd,
                capture_output=True,
                timeout=timeout,
                creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == 'win32' else 0,
            )
        except subprocess.TimeoutExpired as exc:
            raise AdbError('ADB 命令超时（%.0fs）：%s' % (timeout, ' '.join(cmd))) from exc
        except OSError as exc:
            raise AdbError('无法执行 ADB（%s）：%s' % (self.adb_path, exc)) from exc

        if proc.returncode != 0:
            stderr = proc.stderr.decode('utf-8', 'replace').strip()
            stdout = proc.stdout.decode('utf-8', 'replace').strip()
            raise AdbError(
                'ADB 命令失败（exit=%d）：%s\n%s\n%s'
                % (proc.returncode, ' '.join(cmd), stdout, stderr)
            )
        return proc.stdout if binary else proc.stdout.decode('utf-8', 'replace')

    def raw(self, *args: str, timeout: float = 60) -> str:
        return self._run(args, timeout=timeout)  # type: ignore[return-value]

    def shell(self, *args: str, timeout: float = 60) -> str:
        return self._run(['shell'] + list(args), timeout=timeout)  # type: ignore[return-value]

    def exec_out(self, *args: str, timeout: float = 60) -> bytes:
        return self._run(['exec-out'] + list(args), timeout=timeout, binary=True)  # type: ignore[return-value]

    # ---------- 设备 ----------

    def devices(self) -> list[tuple[str, str]]:
        """返回 [(serial, state)]；state 通常是 device / offline / unauthorized。"""
        out = self.raw('devices')
        result: list[tuple[str, str]] = []
        for line in out.splitlines():
            line = line.strip()
            if not line or line.startswith('List of devices'):
                continue
            parts = re.split(r'\s+', line)
            if len(parts) >= 2:
                result.append((parts[0], parts[1]))
        return result

    @property
    def serial(self) -> str:
        """惰性确定设备 serial。只有一个在线设备时自动选中。"""
        if self._serial:
            # 每次使用前确认它还在线，避免拔线后报一堆看不懂的错
            online = [s for s, state in self.devices() if state == 'device']
            if self._serial not in online:
                raise AdbError(
                    '设备 %s 不在线（当前在线：%s）。请检查模拟器/数据线。'
                    % (self._serial, ', '.join(online) or '无')
                )
            return self._serial

        online = [s for s, state in self.devices() if state == 'device']
        if not online:
            raise AdbError('没有在线的 Android 设备。请先启动模拟器或连接手机（adb devices）。')
        if len(online) > 1:
            raise AdbError(
                '检测到多台设备：%s。请用 --device 指定一台。' % ', '.join(online)
            )
        self._serial = online[0]
        return self._serial

    def screen_size(self) -> tuple[int, int]:
        """返回 (宽, 高) 像素。优先 Override size（模拟器改过分辨率时）。"""
        if self._screen:
            return self._screen
        out = self.shell('wm', 'size')
        override = re.search(r'Override size:\s*(\d+)x(\d+)', out)
        physical = re.search(r'Physical size:\s*(\d+)x(\d+)', out)
        match = override or physical
        if not match:
            raise AdbError('无法解析屏幕尺寸：%r' % out)
        self._screen = (int(match.group(1)), int(match.group(2)))
        return self._screen

    # ---------- 应用管理 ----------

    def is_installed(self, package: str) -> bool:
        out = self.shell('pm', 'path', package)
        return 'package:' in out

    def install(self, apk: str, reinstall: bool = True) -> None:
        args = ['install']
        if reinstall:
            args.append('-r')
        args.append(apk)
        out = self.raw(*args, timeout=300)
        if 'Success' not in out:
            raise AdbError('安装 APK 失败：%s\n%s' % (apk, out.strip()))

    def stop_app(self, package: str) -> None:
        self.shell('am', 'force-stop', package)

    def start_app(self, package: str, activity: Optional[str] = None, wait: float = 2.0) -> None:
        """启动应用。

        指定 activity 时用 `am start -W` 等首帧；否则用 monkey 走 LAUNCHER intent。
        `-W` 在冷启动时能避免后续 dump 抓到启动前的空树。
        """
        if activity:
            self.shell('am', 'start', '-W', '-n', activity, timeout=90)
        else:
            self.shell(
                'monkey', '-p', package, '-c', 'android.intent.category.LAUNCHER', '1',
                timeout=60,
            )
        if wait > 0:
            time.sleep(wait)

    def foreground_package(self) -> str:
        """当前前台应用的包名；解析不到返回空串。

        只用于 `ensure_shell` 的兜底：在桌面上按 back 是没用的，必须先拉起应用。
        不同 Android 版本字段名不同（mCurrentFocus / mFocusedApp），两种都试；
        解析失败不抛异常，让调用方继续走原来的返回逻辑。
        """
        try:
            out = self.shell('dumpsys', 'window', timeout=30)
        except AdbError:
            return ''
        for line in out.splitlines():
            if 'mCurrentFocus' not in line and 'mFocusedApp' not in line:
                continue
            match = re.search(r'([A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/', line)
            if match:
                return match.group(1)
        return ''

    def screencap(self, path: str) -> str:
        """截图并写到本地。返回文件路径。"""
        data = self.exec_out('screencap', '-p', timeout=30)
        if not data.startswith(b'\x89PNG'):
            raise AdbError('截图失败：返回内容不是 PNG（%d 字节）' % len(data))
        Path(path).parent.mkdir(parents=True, exist_ok=True)
        Path(path).write_bytes(data)
        return path

    def keyevent(self, code: int | str) -> None:
        self.shell('input', 'keyevent', str(code))
