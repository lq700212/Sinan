"""司南手机端 E2E 脚本 · UI 树读取与操作。

Flutter 在 Android 上把语义文本放进 accessibility 的 `content-desc`，
普通文本放进 `text`。所以匹配时同时看这两个属性，不能只查 text。

所有等待都基于「重新 dump → 匹配」的轮询，不用固定 sleep 猜时间：
- 页面切换、弹层出现、SnackBar 消失都能自动适应；
- 单次 dump 在模拟器上通常 0.1–2 秒，比截图 + 视觉识别快得多。
"""

from __future__ import annotations

import re
import subprocess
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Iterable, Optional

from .adb import Adb, AdbError

_BOUNDS_RE = re.compile(r'\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]')


class UiError(RuntimeError):
    """UI 操作失败（找不到元素、等待超时、输入不支持等）。"""


@dataclass
class UiNode:
    """无障碍树中的一个节点。"""

    attrs: dict[str, str]
    children: list['UiNode'] = field(default_factory=list)
    parent: Optional['UiNode'] = None

    # ---------- 属性 ----------

    @property
    def text(self) -> str:
        return self.attrs.get('text', '')

    @property
    def desc(self) -> str:
        return self.attrs.get('content-desc', '')

    @property
    def label(self) -> str:
        """用于展示/精确匹配的单值：text 优先，其次 content-desc。"""
        return (self.text or self.desc).strip()

    @property
    def raw_label(self) -> str:
        """保留换行的原始语义文本（content-desc 优先），用于解析多行卡片。"""
        return (self.desc or self.text).strip()

    @property
    def cls(self) -> str:
        return self.attrs.get('class', '')

    @property
    def clickable(self) -> bool:
        return self.attrs.get('clickable') == 'true'

    @property
    def password(self) -> bool:
        return self.attrs.get('password') == 'true'

    @property
    def enabled(self) -> bool:
        return self.attrs.get('enabled') != 'false'

    @property
    def bounds(self) -> tuple[int, int, int, int]:
        match = _BOUNDS_RE.search(self.attrs.get('bounds', ''))
        if not match:
            return (0, 0, 0, 0)
        return tuple(int(g) for g in match.groups())  # type: ignore[return-value]

    @property
    def center(self) -> tuple[int, int]:
        x1, y1, x2, y2 = self.bounds
        return ((x1 + x2) // 2, (y1 + y2) // 2)

    @property
    def visible(self) -> bool:
        x1, y1, x2, y2 = self.bounds
        return x2 > x1 and y2 > y1

    def matches(
        self,
        value: Optional[str] = None,
        *,
        contains: bool = True,
        regex: Optional[re.Pattern[str]] = None,
    ) -> bool:
        """同时匹配 text 与 content-desc。

        精确模式按整串或单行比较：Flutter 卡片是一整段多行 content-desc，
        精确点某个短标签（如底栏“待办”）时需要按行拆开看。
        """
        if regex is not None:
            return bool(regex.search(self.text) or regex.search(self.desc))
        if value is None:
            return True
        haystacks = (self.text, self.desc)
        if contains:
            return any(value in s for s in haystacks)
        return any(value == s.strip() or value in s.splitlines() for s in haystacks)

    def clickable_target(self) -> 'UiNode':
        """返回可点击的自身或最近祖先；都没有则返回自己（点中心仍有概率生效）。"""
        node: Optional[UiNode] = self
        while node is not None:
            if node.clickable and node.visible:
                return node
            node = node.parent
        return self

    def walk(self) -> Iterable['UiNode']:
        yield self
        for child in self.children:
            yield from child.walk()


@dataclass
class UiTree:
    """一次 uiautomator dump 的只读快照。"""

    root: UiNode
    raw: str
    screen: tuple[int, int]

    def nodes(self) -> list[UiNode]:
        return list(self.root.walk())

    def find_all(
        self,
        value: Optional[str] = None,
        *,
        contains: bool = True,
        regex: Optional[str] = None,
        cls: Optional[str] = None,
        clickable: Optional[bool] = None,
        min_y: Optional[int] = None,
        max_y: Optional[int] = None,
        min_x: Optional[int] = None,
        max_x: Optional[int] = None,
        visible_only: bool = True,
    ) -> list[UiNode]:
        pattern = re.compile(regex) if regex else None
        result: list[UiNode] = []
        for node in self.nodes():
            if visible_only and not node.visible:
                continue
            if cls and cls not in node.cls:
                continue
            if clickable is not None and node.clickable != clickable:
                continue
            x1, y1, x2, y2 = node.bounds
            if min_y is not None and y1 < min_y:
                continue
            if max_y is not None and y2 > max_y:
                continue
            if min_x is not None and x1 < min_x:
                continue
            if max_x is not None and x2 > max_x:
                continue
            if not node.matches(value, contains=contains, regex=pattern):
                continue
            result.append(node)
        return result

    def find(
        self,
        value: Optional[str] = None,
        *,
        contains: bool = True,
        regex: Optional[str] = None,
        cls: Optional[str] = None,
        clickable: Optional[bool] = None,
        min_y: Optional[int] = None,
        max_y: Optional[int] = None,
        min_x: Optional[int] = None,
        max_x: Optional[int] = None,
        nth: int = 0,
        visible_only: bool = True,
    ) -> UiNode:
        matches = self.find_all(
            value,
            contains=contains,
            regex=regex,
            cls=cls,
            clickable=clickable,
            min_y=min_y,
            max_y=max_y,
            min_x=min_x,
            max_x=max_x,
            visible_only=visible_only,
        )
        if not matches:
            raise UiError(self._not_found_message(value, regex))
        if nth == -1:
            return matches[-1]
        if nth >= len(matches):
            raise UiError(
                '找到 %d 个「%s」，但要求第 %d 个。候选：\n%s'
                % (len(matches), value or regex, nth, self.preview(matches))
            )
        return matches[nth]

    def has(self, value: str, *, contains: bool = True) -> bool:
        return bool(self.find_all(value, contains=contains))

    def has_regex(self, pattern: str) -> bool:
        return bool(self.find_all(regex=pattern))

    def labels(self) -> list[str]:
        """所有非空语义文本（保留换行），按树顺序。"""
        return [n.raw_label for n in self.nodes() if n.raw_label]

    def preview(self, nodes: Optional[Iterable[UiNode]] = None, limit: int = 25) -> str:
        items = list(nodes) if nodes is not None else self.nodes()
        lines: list[str] = []
        for node in items:
            label = node.raw_label.replace('\n', ' / ')
            if not label:
                continue
            lines.append('  [%s] %s | %s' % (node.cls.split('.')[-1], node.bounds, label[:120]))
            if len(lines) >= limit:
                lines.append('  …（仅显示前 %d 条）' % limit)
                break
        return '\n'.join(lines) or '  （空树）'

    def _not_found_message(self, value: Optional[str], regex: Optional[str]) -> str:
        target = ('正则 %s' % regex) if regex else ('「%s」' % value)
        return '界面中找不到 %s。当前可见文本：\n%s' % (target, self.preview())


class UiSession:
    """绑定一台设备的 UI 会话：dump、等待、点击、输入、截图。

    `tree` 带缓存；任何点击/输入/返回都会 invalidate，下一次访问自动重新 dump。
    需要连续断言同一屏内容时直接多次读 `tree`，不会重复 dump。
    """

    REMOTE_DUMP = '/sdcard/sinan_e2e_dump.xml'

    def __init__(self, adb: Adb, *, log: Optional[Callable[[str], None]] = None) -> None:
        self.adb = adb
        self._log = log or (lambda _msg: None)
        self._tree: Optional[UiTree] = None
        self.after_tap_delay = 0.25
        self.screen = adb.screen_size()

    # ---------- dump ----------

    def dump(self, *, force: bool = True) -> UiTree:
        if not force and self._tree is not None:
            return self._tree

        last_error: Optional[Exception] = None
        raw = ''
        for attempt in range(3):
            try:
                self.adb.shell('uiautomator', 'dump', self.REMOTE_DUMP, timeout=45)
                raw_bytes = self.adb.exec_out('cat', self.REMOTE_DUMP, timeout=30)
                raw = raw_bytes.decode('utf-8', 'replace')
                start = raw.find('<?xml')
                end = raw.rfind('</hierarchy>')
                if start < 0 or end < 0:
                    raise UiError('dump 内容缺少 XML 根节点（第 %d 次）' % (attempt + 1))
                xml_text = raw[start:end + len('</hierarchy>')]
                root = self._parse(xml_text)
                self._tree = UiTree(root=root, raw=xml_text, screen=self.screen)
                return self._tree
            except (ET.ParseError, UiError) as exc:
                last_error = exc
                time.sleep(0.6)

        raise UiError('uiautomator dump 连续失败：%s\n原始内容前 500 字：\n%s'
                      % (last_error, raw[:500]))

    def _parse(self, xml_text: str) -> UiNode:
        root_element = ET.fromstring(xml_text)
        screen = self.screen

        def build(element: ET.Element, parent: Optional[UiNode]) -> UiNode:
            node = UiNode(attrs=dict(element.attrib), parent=parent)
            for child in element:
                if child.tag == 'node':
                    node.children.append(build(child, node))
            return node

        top = root_element.find('node')
        if top is None:
            raise UiError('dump XML 没有 node 节点')
        return build(top, None)

    def invalidate(self) -> None:
        self._tree = None

    @property
    def tree(self) -> UiTree:
        return self.dump(force=self._tree is None)

    @property
    def cached_tree(self) -> Optional[UiTree]:
        """最近一次 dump 的缓存；可能为 None（刚操作完）。不触发新 dump。"""
        return self._tree

    # ---------- 查找与等待 ----------

    def find(self, value: Optional[str] = None, **kwargs) -> UiNode:
        return self.tree.find(value, **kwargs)

    def find_all(self, value: Optional[str] = None, **kwargs) -> list[UiNode]:
        return self.tree.find_all(value, **kwargs)

    def wait_for(
        self,
        value: Optional[str] = None,
        *,
        contains: bool = True,
        regex: Optional[str] = None,
        timeout: float = 12.0,
        interval: float = 0.35,
        gone: bool = False,
        cls: Optional[str] = None,
        clickable: Optional[bool] = None,
        min_y: Optional[int] = None,
        max_y: Optional[int] = None,
        nth: int = 0,
    ) -> Optional[UiNode]:
        """轮询等待元素出现/消失。超时抛 UiError，并附带当前界面文本。"""
        deadline = time.time() + timeout
        last_tree: Optional[UiTree] = None
        while True:
            tree = self.dump()
            last_tree = tree
            matches = tree.find_all(
                value,
                contains=contains,
                regex=regex,
                cls=cls,
                clickable=clickable,
                min_y=min_y,
                max_y=max_y,
            )
            condition = not matches if gone else bool(matches)
            if condition:
                if gone:
                    return None
                if nth == -1:
                    return matches[-1]
                if nth >= len(matches):
                    raise UiError(
                        '等待「%s」时找到 %d 个，但要求第 %d 个。'
                        % (value or regex, len(matches), nth)
                    )
                return matches[nth]
            if time.time() >= deadline:
                target = ('正则 %s' % regex) if regex else ('「%s」' % value)
                action = '消失' if gone else '出现'
                detail = last_tree.preview() if last_tree else '（无快照）'
                raise UiError(
                    '等待 %s %s超时（%.1fs）。当前界面：\n%s'
                    % (target, action, timeout, detail)
                )
            time.sleep(interval)

    def wait_until(
        self,
        predicate: Callable[[UiTree], bool],
        *,
        description: str,
        timeout: float = 12.0,
        interval: float = 0.35,
    ) -> UiTree:
        deadline = time.time() + timeout
        while True:
            tree = self.dump()
            if predicate(tree):
                return tree
            if time.time() >= deadline:
                raise UiError('等待「%s」超时（%.1fs）。当前界面：\n%s'
                              % (description, timeout, tree.preview()))
            time.sleep(interval)

    # ---------- 操作 ----------

    def tap(
        self,
        value: Optional[str] = None,
        *,
        node: Optional[UiNode] = None,
        xy: Optional[tuple[int, int]] = None,
        contains: bool = True,
        regex: Optional[str] = None,
        nth: int = 0,
        **find_kwargs,
    ) -> UiNode:
        if xy is not None:
            x, y = xy
            self._log('点击坐标 (%d, %d)' % (x, y))
            self.adb.shell('input', 'tap', str(x), str(y))
            self._after_action()
            # 坐标点击没有节点可返回，造一个占位便于调用方统一处理
            return UiNode(attrs={'bounds': '[%d,%d][%d,%d]' % (x, y, x, y)})

        target = node or self.find(
            value, contains=contains, regex=regex, nth=nth, **find_kwargs
        )
        clickable = target.clickable_target()
        x, y = clickable.center
        h = self.screen[1]
        if y < 0 or y > h:
            raise UiError(
                '目标「%s」中心在屏幕外（%d, %d），请先滚动。'
                % (target.raw_label[:60] or target.cls, x, y)
            )
        self._log('点击「%s」(%d, %d)' % ((target.raw_label or target.cls).replace('\n', ' / ')[:60], x, y))
        self.adb.shell('input', 'tap', str(x), str(y))
        self._after_action()
        return target

    def tap_tab(self, label: str) -> None:
        """点底部导航。同名标题也在顶栏，所以限定在屏幕底部 20% 区域。

        搜索框聚焦时软键盘会盖住底栏，先收键盘再点，否则点击会落到键盘上。
        """
        if self.keyboard_shown():
            self.hide_keyboard()
        h = self.screen[1]
        self.tap(label, contains=False, min_y=int(h * 0.8))

    def _after_action(self) -> None:
        self.invalidate()
        if self.after_tap_delay > 0:
            time.sleep(self.after_tap_delay)

    # ---------- 文本输入 ----------

    def input_text(self, text: str) -> None:
        """向当前焦点输入 ASCII 文本。

        `adb shell input text` 不支持中文，遇到中文直接给出明确报错，
        避免脚本静默输入失败后断言莫名其妙。
        """
        if not text:
            return
        if not all(32 <= ord(ch) <= 126 for ch in text):
            raise UiError(
                'adb input text 只支持 ASCII，收到含非 ASCII 的文本：%r。'
                '中文输入需要 ADBKeyboard 或改用界面按钮。' % text
            )
        payload = text.replace(' ', '%s')
        self.adb.shell('input', 'text', payload)
        self.invalidate()
        time.sleep(0.15)

    def clear_text(self, max_len: int = 80) -> None:
        """清空当前输入框。

        优先 Ctrl+A 全选后删除；部分系统不支持 `input keycombination`，
        这时退回「把光标移到末尾 + 连发 DEL」。两条路都保留，避免脚本在
        不同模拟器镜像上直接崩掉。
        """
        try:
            self.adb.shell('input', 'keycombination', '113', '29')
            time.sleep(0.1)
            self.adb.shell('input', 'keyevent', '67')
        except AdbError:
            pass
        # 兜底：无论上面是否生效，都把常见长度的字符逐个删掉
        self.adb.shell('input', 'keyevent', '123')  # MOVE_END
        self.adb.shell('input', 'keyevent', *(['67'] * max_len))
        self.invalidate()
        time.sleep(0.15)

    def keyboard_shown(self) -> bool:
        """软键盘是否显示。用于决定 back 是收键盘还是退页面。"""
        try:
            out = self.adb.shell('dumpsys', 'input_method')
        except AdbError:
            return False
        return 'mInputShown=true' in out

    def hide_keyboard(self) -> None:
        """只在键盘确实显示时按 back，避免把主壳退到桌面。"""
        if self.keyboard_shown():
            self.press_back(wait=0.35)

    def press_back(self, *, wait: float = 0.5) -> None:
        self._log('按返回键')
        self.adb.shell('input', 'keyevent', '4')
        self.invalidate()
        time.sleep(wait)

    def press_enter(self) -> None:
        self.adb.shell('input', 'keyevent', '66')
        self.invalidate()
        time.sleep(0.2)

    # ---------- 手势 ----------

    def swipe(self, x1: int, y1: int, x2: int, y2: int, duration: int = 300) -> None:
        self.adb.shell('input', 'swipe', str(x1), str(y1), str(x2), str(y2), str(duration))
        self.invalidate()
        time.sleep(0.35)

    def swipe_up(self, *, ratio: float = 0.55) -> None:
        w, h = self.screen
        self.swipe(w // 2, int(h * 0.75), w // 2, int(h * (0.75 - ratio)), duration=280)

    def swipe_down(self, *, ratio: float = 0.55) -> None:
        w, h = self.screen
        self.swipe(w // 2, int(h * 0.35), w // 2, int(h * (0.35 + ratio)), duration=280)

    def scroll_until(
        self,
        value: str,
        *,
        contains: bool = True,
        max_swipes: int = 4,
        **find_kwargs,
    ) -> UiNode:
        """向下滑动直到找到目标；找不到抛错。用于长列表。"""
        for index in range(max_swipes + 1):
            matches = self.find_all(value, contains=contains, **find_kwargs)
            if matches:
                return matches[0]
            if index < max_swipes:
                self.swipe_up(ratio=0.45)
        raise UiError('向下滑动 %d 次仍找不到「%s」。' % (max_swipes, value))

    # ---------- 诊断产物 ----------

    def screenshot(self, path: str) -> str:
        return self.adb.screencap(path)

    def save_tree(self, path: str) -> str:
        Path(path).parent.mkdir(parents=True, exist_ok=True)
        Path(path).write_text(self.dump().raw, encoding='utf-8')
        return path

    def describe(self) -> str:
        return self.tree.preview()
