# 司南手机端 E2E 回归脚本

手机端常规功能模块的快速回归工具：**真机/模拟器上点界面、读无障碍树、做断言**，
不靠截图 + 肉眼判断。改完手机端界面，先跑对应模块，几十秒内知道有没有把老功能改坏。

> 这是「UI 级真机回归」，不替代 `flutter analyze` / `flutter test`：
> - `flutter test` 管纯规则（日期、状态、归组、统计口径）；
> - 本脚本管「装到设备上以后，页面能不能进、数字对不对、交互有没有断」。

## 为什么不用截图识别

Flutter 在 Android 上把界面文本放进 accessibility 的 `content-desc`，`uiautomator dump`
能完整拿到中文、按钮、卡片多行文案与 bounds。脚本据此做**文本级定位和断言**：

- 单次 dump 约 2.2 秒（`uiautomator` 固有开销），脚本会缓存同一屏结果、只在操作后失效，
  冒烟两条用例从 54 秒降到 18 秒；比「截图 → 视觉模型读图」快且稳定；
- 点击目标用文本定位，不写死坐标；
- 只有**用例失败**时才保存截图和 UI 树，日常跑不产生图片。

## 环境要求

| 项 | 要求 |
|---|---|
| Python | 3.9+（只用标准库，无需 pip install） |
| adb | 自动发现 `E:\Android\Sdk\platform-tools\adb.exe`、`%LOCALAPPDATA%\Android\Sdk\...`、PATH；也可用 `--adb` / 环境变量 `ADB` 指定 |
| 设备 | 模拟器或真机在线（`adb devices` 状态为 `device`） |
| 应用 | 已安装 `com.sinan.sinan_mobile`；没装可加 `--install mobile/build/app/outputs/flutter-apk/app-debug.apk` |
| 账号 | 默认 `engineer / 123456`，可用 `--username` / `--password` 覆盖 |

登录态不持久化是手机端的既有口径：脚本每次运行会自动登录，冷启动模式会真的验证
「启动后停在登录页」。

## 快速开始

```powershell
# 列出全部用例（不连设备）
python tools/mobile_e2e/run.py --list

# 冒烟：登录 + 四个主页面可达（约 20 秒）
python tools/mobile_e2e/run.py --module smoke

# 台账：搜索、倒挂筛选、详情（约 50 秒）
python tools/mobile_e2e/run.py --module ledger

# 多项目：总览、跨页数字一致、分项目、切换、空项目（约 1.5 分钟）
python tools/mobile_e2e/run.py --module projects

# 全部只读用例（约 3.5 分钟）
python tools/mobile_e2e/run.py

# 冷启动登录：force-stop 后重新启动，验证登录页与自动登录（约 35 秒）
python tools/mobile_e2e/run.py --cold --module smoke

# 单条用例 / 前缀
python tools/mobile_e2e/run.py --case ledger.filter
python tools/mobile_e2e/run.py --case projects.

# 机器可读结果
python tools/mobile_e2e/run.py --module smoke --json out/e2e.json
```

退出码：`0` 全部通过（含跳过），`1` 有失败，`2` 环境/参数错误。

## 用例清单

| ID | 模块 | 覆盖 |
|---|---|---|
| `smoke.login` | smoke | 登录页 → 待办；`--cold` 时验证冷启动必须停在登录页 |
| `smoke.tabs` | smoke | 待办 / 项目 / 台账 / 同步 四页可达，顶栏副标题正确 |
| `todo.summary` | todo | 待办 KPI、空状态、同步条「本机缓存 N 条 · M 条待传」格式与不变量 |
| `projects.overview` | projects | 项目 hero 项目号 / 分项目 / 设备 / 问题 / 倒挂，分项目卡片与阶段填写率 |
| `projects.consistency` | projects | 项目页与台账页「问题 / 倒挂」数字同源 |
| `projects.subproject` | projects | 分项目卡片进入详情、问题或空状态、返回 |
| `projects.switch` | projects | 切换总项目后 hero 与规模跟随，再切回原项目 |
| `projects.empty` | projects | 空总项目显示 0 台 0 条、台账空结果、待办空状态 |
| `ledger.search` | ledger | 搜项目号命中全部、搜不存在字符串落到空状态、清空恢复 |
| `ledger.filter` | ledger | 点「倒挂 N」后筛出数必须等于 chip 上的 N，切回全部恢复 |
| `ledger.detail` | ledger | 问题详情四阶段、现场记录、推进阶段、返回列表 |
| `sync.status` | sync | 已配对 / 未配对文案、立即同步、解除配对、队列与规则；回归「同步服务未启用」假文案 |
| `settings.open` | settings | 右上角账号菜单 → 系统设置 → 关于 / 外观 / 数据 / 本地数据库 |
| `nav.refresh` | nav | 刷新本地数据并出现 SnackBar |
| `issue.form` | issue | 新建问题表单入口、机台 / 计划完成 / 保存并继续 / 取消（**不保存，零数据污染**） |

## 行为约定

1. **不写死业务数字**：问题数、倒挂数、分项目数都从当前界面解析，再互相对账。
   换一套种子数据、切到另一个总项目，用例依然成立。
2. **只读优先**：默认 15 条全部只读。新建问题只验证表单，不点保存，避免污染现场库。
3. **自动处理环境噪声**：
   - 已在主壳就跳过登录；停在登录页就自动登录；
   - 弹层 / 账号菜单 / 详情页会先返回主壳；
   - 搜索框聚焦时先收软键盘再点底栏；
   - 台账页残留的搜索词或「倒挂」筛选，会在每个台账用例开头重置为「无搜索 + 全部」。
4. **空项目自动兜底**：如果手机当前停在 0 条问题的总项目，脚本临时切到问题最多的项目
   跑回归，**结束后恢复用户原来的项目**。
5. **失败留证据**：失败用例自动保存 `artifacts/<用例>_<时分秒>/screen.png` 与 `ui.xml`；
   日常通过时不留图片。产物目录已 gitignore。

## 新增用例

在 `e2e/cases/` 下新建模块，用 `@case` 注册（`e2e/cases/__init__.py` 加一行 import）：

```python
from ..runner import Context, case


@case('ledger.sort', module='ledger', desc='台账按倒挂优先排序', order=115)
def ledger_sort(ctx: Context) -> None:
    ctx.goto_tab('台账')
    ctx.reset_ledger()                 # 清搜索 + 切回全部
    tree = ctx.ui.tree                 # 复用最近一次 dump
    cards = tree.find_all(regex=r'P\d{4}\d{3}', clickable=True)
    ctx.expect(bool(cards), '台账没有卡片')
    # 断言第一张卡带「倒挂」……
```

可用的高层 API：

| 方法 | 用途 |
|---|---|
| `ctx.goto_tab('台账')` | 切主页面并等副标题 |
| `ctx.ensure_shell()` | 从详情/弹层/设置页退回主壳 |
| `ctx.current_project_code()` | 读当前总项目号（本次运行内缓存） |
| `ctx.project_rows()` | 打开切换弹层读全部项目规模，自动关闭 |
| `ctx.switch_project('P7392')` | 切总项目并确认 hero 刷新 |
| `ctx.reset_ledger()` | 台账恢复「无搜索 + 全部」 |
| `ctx.parse_hero / parse_ledger_counts / parse_chips` | 解析界面数字 |
| `ctx.expect_text / expect_regex / expect_no_text` | 断言（复用 dump 缓存） |
| `ctx.skip('原因')` | 环境不满足时跳过，不计失败 |
| `ctx.log('…')` | `-v` 时打印步骤 |

底层 `ctx.ui` 还提供 `tap / input_text / clear_text / press_back / swipe / wait_for /
wait_until / screenshot`，用法见 `e2e/ui.py`。

## 参数

| 参数 | 说明 |
|---|---|
| `--list` | 列出用例，不连设备 |
| `--module a,b` | 按模块过滤，可重复 |
| `--case id` | 按用例 id 或前缀过滤，可重复 |
| `--tag readonly` | 按标签过滤 |
| `--mutating` | 允许执行写操作用例（默认跳过） |
| `--cold` | 开始前 force-stop 并重新启动 |
| `--install APK` | 先安装 APK |
| `--device SERIAL` | 多设备时指定一台 |
| `--adb PATH` | 指定 adb |
| `--username / --password` | 覆盖登录账号（默认 `engineer / 123456`） |
| `--json PATH` | 输出 JSON 结果 |
| `-v / --verbose` | 打印每一步动作与失败堆栈 |
| `--stop-on-fail` | 首个失败后停止 |
| `--no-artifacts` | 失败时不保存截图/UI 树 |

## 常见问题

**`找不到 adb`**
用 `--adb` 指定，或设置环境变量 `ADB`，或把 platform-tools 加入 PATH。

**`检测到多台设备`**
用 `--device emulator-5554` 指定；脚本不会猜设备，避免点错机器。

**登录一直失败**
确认设备上装的是 `com.sinan.sinan_mobile`，账号密码正确；`-v` 会打印登录页当前文案，
如果出现「账号或密码不正确」会直接带在超时信息里。

**中文输入报错**
`adb shell input text` 不支持中文。搜索用例只输入项目号/机台号（ASCII）；
需要输入中文时，用界面按钮或另装 ADBKeyboard，本脚本不静默吞掉这个限制。

**点击没反应**
先看失败产物里的 `ui.xml`：Flutter 有时把多个控件合并成一个语义节点，
优先用 `find(regex=...)` 定位；必要时用 `ctx.ui.tap(xy=(x, y))` 兜底。

## 目录结构

```
tools/mobile_e2e/
├─ run.py                 # 命令行入口
├─ README.md
├─ e2e/
│  ├─ adb.py              # adb 发现、shell、截图、安装
│  ├─ ui.py               # uiautomator dump 解析、等待、点击、输入
│  ├─ runner.py           # 用例注册、Context、执行与报告
│  └─ cases/              # smoke / todo / projects / ledger / sync / settings / nav / issue
└─ artifacts/             # 失败截图与 UI 树（gitignore）
```
