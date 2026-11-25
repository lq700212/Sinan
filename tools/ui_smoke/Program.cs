using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace UiSmoke;

/// <summary>
/// 司南 UI 后台冒烟：启动/附加 → 静默登录 → 后台截图 → 逐页导航截图。
///
/// 安全边界（为什么这样做）：
/// - PasswordBox 的 UIA ValuePattern 被 WPF 明确禁止 SetValue，密码只能靠键盘消息；
/// - 键盘消息必须落到 WPF 的焦点元素上，所以用 AttachThreadInput + SetFocus 把 Win32 焦点
///   交给目标窗口，但**不调用 SetForegroundWindow**，前台窗口保持不变；
/// - 启动进程时 Windows 会自动激活窗口，因此记录启动前的前台句柄，启动后立即还原；
/// - 截图走 PrintWindow，不激活、不移动鼠标。
///
/// 这是开发/验收工具，账号密码用种子演示账号（engineer / 123456）。
/// </summary>
internal static class Program
{
    private static nint _foregroundBefore;

    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        Directory.CreateDirectory(options.ShotsDir);
        _foregroundBefore = GetForegroundWindow();
        Console.WriteLine($"[env] foreground before = {_foregroundBefore} '{Title(_foregroundBefore)}'");
        Console.WriteLine($"[env] exe = {options.ExePath}");
        Console.WriteLine($"[env] shots = {Path.GetFullPath(options.ShotsDir)}");

        Process? launched = null;
        try
        {
            var process = options.Launch
                ? Launch(options, out launched)
                : AttachExisting();

            var loginHwnd = WaitForWindow(process,
                hwnd => Title(hwnd).Contains("登录", StringComparison.Ordinal), 20);
            RestoreForeground();

            if (loginHwnd != 0)
            {
                Console.WriteLine($"[login] window = {loginHwnd} '{Title(loginHwnd)}'");
                Login(process, loginHwnd, options);
            }
            else
            {
                Console.WriteLine("[login] no login window; assume already signed in");
            }

            var mainHwnd = WaitForWindow(process,
                hwnd => hwnd != 0 &&
                        !Title(hwnd).Contains("登录", StringComparison.Ordinal) &&
                        Title(hwnd).Length > 0, 25);
            RestoreForeground();
            if (mainHwnd == 0)
            {
                Console.WriteLine("[fail] main window not found");
                return 2;
            }

            Console.WriteLine($"[main] window = {mainHwnd} '{Title(mainHwnd)}'");
            Thread.Sleep(1200);
            RestoreForeground();

            var mainShot = Path.Combine(options.ShotsDir, "00-main.png");
            var info = Shoot(mainHwnd, mainShot);
            Console.WriteLine($"[shot] {info}");

            if (options.Dump)
            {
                if (options.DumpPage.Length > 0)
                {
                    var nav = TryFindById(AutomationElement.FromHandle(mainHwnd)!, options.DumpPage);
                    if (nav != null) { Activate(nav); Thread.Sleep(1200); }
                }
                DumpTree(AutomationElement.FromHandle(mainHwnd)!);
                return 0;
            }

            if (!options.NoPages)
            {
                NavigatePages(process, mainHwnd, options);
                ShootExportMode(mainHwnd, options);
                NavigateDetailAndSubproject(mainHwnd, options);
                ShootDialogs(process, mainHwnd, options);
            }

            if (options.WriteTest)
                RunWriteTest(process, mainHwnd, options);

            if (!options.NoPages && !options.NoDark)
                RunDarkRound(mainHwnd, options);

            ReportRoundCornerAudit();
            Console.WriteLine("[done] UI smoke finished");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[fail] {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            RestoreForeground();
            if (launched is { HasExited: false } && !options.KeepOpen)
            {
                try
                {
                    launched.CloseMainWindow();
                    if (!launched.WaitForExit(5000)) launched.Kill(entireProcessTree: true);
                    Console.WriteLine("[exit] Sinan closed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[exit] close failed: {ex.Message}");
                }
            }
            RestoreForeground();
        }
    }

    // ------------------------------------------------------------------
    // 启动 / 附加
    // ------------------------------------------------------------------

    private static Process Launch(Options options, out Process launched)
    {
        if (options.Restart)
        {
            foreach (var old in Process.GetProcessesByName("Sinan"))
            {
                try { old.Kill(entireProcessTree: true); old.WaitForExit(3000); } catch { }
            }
        }

        Console.WriteLine("[launch] starting Sinan...");
        var start = new ProcessStartInfo(options.ExePath) { UseShellExecute = true };
        string? previousDataDir = null;
        if (!string.IsNullOrEmpty(options.DataDir))
        {
            // 独立库：写入验证会真的建/删问题，绝不能落在用户主库上。
            // UseShellExecute=true 时不能直接改 ProcessStartInfo.Environment，
            // 用父进程环境变量过渡（子进程继承），启动后立刻还原。
            Directory.CreateDirectory(options.DataDir);
            previousDataDir = Environment.GetEnvironmentVariable("SINAN_DATA_DIR");
            Environment.SetEnvironmentVariable("SINAN_DATA_DIR", Path.GetFullPath(options.DataDir));
            Console.WriteLine($"[launch] SINAN_DATA_DIR = {Path.GetFullPath(options.DataDir)}");
        }
        try
        {
            launched = Process.Start(start) ?? throw new InvalidOperationException("Sinan 启动失败。");
        }
        finally
        {
            if (previousDataDir != null || !string.IsNullOrEmpty(options.DataDir))
                Environment.SetEnvironmentVariable("SINAN_DATA_DIR", previousDataDir);
        }
        RestoreForeground();
        return launched;
    }

    private static Process AttachExisting()
    {
        var process = Process.GetProcessesByName("Sinan").FirstOrDefault()
                      ?? throw new InvalidOperationException("Sinan 未运行；加 --launch 参数启动。");
        Console.WriteLine($"[attach] pid = {process.Id}");
        return process;
    }

    private static nint WaitForWindow(Process process, Func<nint, bool> predicate, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.HasExited) return 0;
            var hwnd = process.MainWindowHandle;
            if (hwnd != 0 && predicate(hwnd)) return hwnd;
            Thread.Sleep(200);
        }
        return 0;
    }

    private static void RestoreForeground()
    {
        if (_foregroundBefore == 0) return;
        if (GetForegroundWindow() == _foregroundBefore) return;
        SetForegroundWindow(_foregroundBefore);
        Thread.Sleep(80);
    }

    // ------------------------------------------------------------------
    // 静默登录
    // ------------------------------------------------------------------

    private static void Login(Process process, nint hwnd, Options options)
    {
        var window = AutomationElement.FromHandle(hwnd)
                     ?? throw new InvalidOperationException("登录窗 UIA 根不可用。");

        // 登录页品牌位（56×56 磁勺）只能在这个窗口期目检：登录成功后窗口即销毁。
        Console.WriteLine($"[shot] {Shoot(hwnd, Path.Combine(options.ShotsDir, "00-login.png"))}");

        // 登录窗句柄出现不代表控件树已构建完毕：UIA 首次查询可能早于 WPF 完成模板套用，
        // 直接 FindById 会偶发"找不到元素"。这里给 6 秒重试窗口，每 200ms 重取一次根。
        var employee = WaitForById(hwnd, "EmployeeNoBox", 6);
        var password = WaitForById(hwnd, "PasswordInput", 6);

        ((ValuePattern)employee.GetCurrentPattern(ValuePattern.Pattern)).SetValue(options.EmployeeNo);
        Console.WriteLine($"[login] employee = {options.EmployeeNo}");

        var current = GetCurrentThreadId();
        var target = ThreadOf(hwnd);
        var attached = AttachThreadInput(current, target, true);
        Console.WriteLine($"[login] attach thread {current} -> {target} = {attached}");
        try
        {
            SetFocus(hwnd);
            Thread.Sleep(250);

            // 启动时焦点固定在工号框（LoginWindow.Loaded 里 Focus()），Tab 一次到密码框。
            // 如果密码框尚未获得键盘焦点，再多按几次 Tab（最多 4 次），避免依赖固定顺序。
            var focused = password.Current.HasKeyboardFocus;
            for (var i = 0; i < 4 && !focused; i++)
            {
                PressTab(hwnd);
                Thread.Sleep(150);
                focused = password.Current.HasKeyboardFocus;
            }
            Console.WriteLine($"[login] password focused = {focused}");

            foreach (var ch in options.Password)
            {
                PostMessage(hwnd, WmChar, ch, 0);
                Thread.Sleep(45);
            }
            Thread.Sleep(250);
        }
        finally
        {
            AttachThreadInput(current, target, false);
        }

        // 按钮的 Name/IsEnabled 在跨进程 UIA 里是延迟填充的：刚遍历到按钮时可能
        // 还是空壳（Name=''、enabled=unknown）。轮询到"能看到文字且可用"再点。
        AutomationElement? button = null;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            var root = AutomationElement.FromHandle(hwnd);
            button = root == null ? null : FindButton(root, "进入");
            if (button != null && button.Current.IsEnabled &&
                !string.IsNullOrEmpty(button.Current.Name))
                break;
            button = null;
            Thread.Sleep(300);
        }
        Console.WriteLine($"[login] button = '{button?.Current.Name}' enabled = {button?.Current.IsEnabled}");
        if (button == null)
            throw new InvalidOperationException("登录按钮不可用：密码可能未输入到密码框。");

        ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Console.WriteLine("[login] invoked");

        // 主窗显示会主动激活；立即把前台还给用户原窗口。
        Thread.Sleep(800);
        RestoreForeground();
    }

    private static void PressTab(nint hwnd)
    {
        PostMessage(hwnd, WmKeyDown, 0x09, 0x000F0001);
        PostMessage(hwnd, WmKeyUp, 0x09, unchecked((nint)0xC00F0001));
    }

    // ------------------------------------------------------------------
    // 逐页导航
    // ------------------------------------------------------------------

    private static readonly (string Id, string Name, string File)[] Pages =
    {
        ("overview", "项目总览", "01-overview.png"),
        ("dashboard", "工作台", "02-dashboard.png"),
        ("issues", "问题台账", "03-issues.png"),
        ("exchange", "数据交换", "04-exchange.png"),
        ("report", "统计报表", "05-reports.png"),
        ("settings", "系统设置", "06-settings.png")
    };

    private static void NavigatePages(Process process, nint mainHwnd, Options options)
    {
        var previousShot = Path.Combine(options.ShotsDir, "00-main.png");
        var currentPage = "overview"; // 登录后默认落总览；首屏与"再点总览"字节相同是正确行为
        foreach (var (id, name, file) in Pages)
        {
            // 每轮重新取根与元素：WPF 换页后旧 AutomationElement 引用会失效，
            // 早期版本在循环外抓一次 clickable，导致部分页面点不动（截图停在工作台）。
            var window = AutomationElement.FromHandle(mainHwnd);
            if (window == null)
            {
                Console.WriteLine("[nav] main window root unavailable");
                return;
            }

            var target = TryFindById(window, id);
            if (target == null)
            {
                Console.WriteLine($"[nav] tab '{name}' (id={id}) not found, skipped");
                continue;
            }

            try
            {
                Activate(target);
                Thread.Sleep(900);
                RestoreForeground();
                process.Refresh();
                var path = Path.Combine(options.ShotsDir, file);
                var info = Shoot(mainHwnd, path);
                var same = id != currentPage && File.Exists(previousShot) &&
                           new FileInfo(previousShot).Length == new FileInfo(path).Length &&
                           FilesEqual(previousShot, path);
                Console.WriteLine($"[nav] {name} -> {info}{(same ? "  [WARN] 与上一页字节相同，可能未切换" : "")}");
                previousShot = path;
                currentPage = id;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[nav] {name} failed: {ex.Message}");
            }
        }
    }

    private static AutomationElement? TryFindById(AutomationElement root, string id) =>
        root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id));

    /// <summary>
    /// 数据交换页在桌面端是一个入口、两种模式（设计稿是 import / export 两个整页）。
    /// 这里补一张"导出模式"截图：先回数据交换，再点 ModeExportButton。
    /// </summary>
    private static void ShootExportMode(nint mainHwnd, Options options)
    {
        try
        {
            var window = AutomationElement.FromHandle(mainHwnd);
            if (window == null) return;
            var tab = TryFindById(window, "exchange");
            if (tab == null) return;
            Activate(tab);
            Thread.Sleep(800);

            window = AutomationElement.FromHandle(mainHwnd);
            if (window == null) return;
            var mode = TryFindById(window, "ModeExportButton");
            if (mode == null)
            {
                Console.WriteLine("[nav] 导出模式按钮未找到");
                return;
            }
            Activate(mode);
            Thread.Sleep(700);
            RestoreForeground();
            Console.WriteLine($"[nav] 数据交换·导出 -> {Shoot(mainHwnd, Path.Combine(options.ShotsDir, "04b-exchange-export.png"))}");

            // 切回导入模式，避免后续暗色轮播把 04-exchange 拍成导出页。
            window = AutomationElement.FromHandle(mainHwnd);
            var back = window == null ? null : TryFindById(window, "ModeImportButton");
            if (back != null) { Activate(back); Thread.Sleep(500); RestoreForeground(); }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[nav] 导出模式截图失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // 详情页 / 分项目页 / 弹窗
    // ------------------------------------------------------------------

    /// <summary>
    /// 详情页与分项目页不在左侧导航里，进入方式是从台账选行、从总览点卡片。
    /// 这里复用真实的用户路径，不用"直接构造 ViewModel 跳到页面"的捷径——
    /// 捷径能出图，但证明不了这条路径本身没坏。
    /// </summary>
    private static void NavigateDetailAndSubproject(nint mainHwnd, Options options, string suffix = "")
    {
        var window = AutomationElement.FromHandle(mainHwnd);
        if (window == null) return;

        // ---- 详情页：切换台账 → 打开选中行（台账默认选中首行） ----
        Activate(TryFindById(window, "issues")!);
        Thread.Sleep(900);
        var open = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "OpenSelectedIssueButton");
        if (open == null || !open.Current.IsEnabled)
        {
            // 没有默认选中时，手工选第一行；ListView 的虚拟化在 UIA 里可能看不到行，
            // 所以这条只是兜底，不作为主路径。
            var list = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "IssueTable");
            var row = list?.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            if (row != null)
            {
                ((SelectionItemPattern)row.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                Thread.Sleep(500);
                open = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "OpenSelectedIssueButton");
            }
        }
        if (open == null || !open.Current.IsEnabled)
        {
            Console.WriteLine("[nav] detail skipped: 台账没有可打开的选中行");
        }
        else
        {
            Activate(open);
            Thread.Sleep(1200);
            RestoreForeground();
            Console.WriteLine($"[nav] 问题详情 -> {Shoot(mainHwnd, Path.Combine(options.ShotsDir, $"07-detail{suffix}.png"))}");
            ClickBack(mainHwnd);
        }

        // ---- 分项目页：切回总览 → 点第一张分项目卡 ----
        var overview = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "overview");
        if (overview == null) return;
        Activate(overview);
        Thread.Sleep(900);
        var card = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "open-subproject-P7391-01");
        if (card == null)
        {
            Console.WriteLine("[nav] subproject skipped: 找不到 P7391-01 卡片");
            return;
        }
        Activate(card);
        Thread.Sleep(1200);
        RestoreForeground();
        Console.WriteLine($"[nav] 分项目 -> {Shoot(mainHwnd, Path.Combine(options.ShotsDir, $"08-subproject{suffix}.png"))}");
        ClickBack(mainHwnd);
    }

    private static void ClickBack(nint mainHwnd)
    {
        var back = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "BackButton");
        if (back == null)
        {
            Console.WriteLine("[nav] back button not found");
            return;
        }
        Activate(back);
        Thread.Sleep(900);
    }

    /// <summary>
    /// 弹窗截图：新建问题与开始导出。弹窗是独立 HWND，按进程 + 标题定位，
    /// 截完用 WindowPattern.Close 关闭，不按 Esc（Esc 在部分按钮聚焦时会先清空输入）。
    /// </summary>
    private static void ShootDialogs(Process process, nint mainHwnd, Options options)
    {
        ShootDialog(process, mainHwnd, "NewIssueButton", "新建问题", "09-dialog-new.png", options);

        var exchange = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "exchange");
        if (exchange != null)
        {
            Activate(exchange);
            Thread.Sleep(900);
            // 导出按钮只在"导出模式"下出现：数据交换是一个入口两种模式，
            // 不先切模式会找不到 ExportButton，README 的导出弹窗图就会缺。
            var mode = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "ModeExportButton");
            if (mode != null)
            {
                Activate(mode);
                Thread.Sleep(700);
            }
            var export = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "ExportButton");
            if (export != null)
            {
                Activate(export);
                var hwnd = WaitForDialog(process, "导出", 6);
                if (hwnd != 0)
                {
                    RestoreForeground();
                    Console.WriteLine($"[nav] 导出弹窗 -> {Shoot(hwnd, Path.Combine(options.ShotsDir, "10-dialog-export.png"))}");
                    CloseDialog(hwnd);
                }
                else
                {
                    Console.WriteLine("[nav] 导出弹窗未出现");
                }
            }
            // 截完切回导入模式，保持后续截图与手工使用的默认状态一致。
            var back = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "ModeImportButton");
            if (back != null)
            {
                Activate(back);
                Thread.Sleep(500);
            }
        }
    }

    private static void ShootDialog(
        Process process, nint mainHwnd, string triggerId, string titlePart, string file, Options options)
    {
        var trigger = TryFindById(AutomationElement.FromHandle(mainHwnd)!, triggerId);
        if (trigger == null)
        {
            Console.WriteLine($"[nav] dialog skipped: 找不到 {triggerId}");
            return;
        }
        Activate(trigger);
        var hwnd = WaitForDialog(process, titlePart, 6);
        if (hwnd == 0)
        {
            Console.WriteLine($"[nav] dialog '{titlePart}' 未出现；本进程可见顶层窗口：");
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out var pid);
                if ((int)pid == process.Id && IsWindowVisible(h))
                    Console.WriteLine($"  hwnd={h} class='{ClassName(h)}' title='{Title(h)}'");
                return true;
            }, 0);
            return;
        }
        RestoreForeground();
        Console.WriteLine($"[nav] {titlePart}弹窗 -> {Shoot(hwnd, Path.Combine(options.ShotsDir, file))}");
        CloseDialog(hwnd);
        Thread.Sleep(600);
    }

    /// <summary>
    /// 等弹窗出现。弹窗由命令在 UI 线程上创建，跨进程没有可靠的"创建完成"信号，
    /// 用轮询 + 超时最稳；短 sleep 一次命中失败会白丢一张截图。
    /// </summary>
    private static nint WaitForDialog(Process process, string titlePart, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var hwnd = FindDialogHwnd(process, titlePart);
            if (hwnd != 0) return hwnd;
            Thread.Sleep(200);
        }
        return 0;
    }

    /// <summary>
    /// 按进程与标题找弹窗。MainWindowHandle 只会返回主窗，UIA 的桌面根又依赖
    /// 目标进程的 UIA provider（模态弹窗期间可能取不到），所以直接 EnumWindows：
    /// 只认本进程的可见顶层窗口，标题模糊匹配动态文字不参与。
    /// </summary>
    private static nint FindDialogHwnd(Process process, string titlePart)
    {
        nint found = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if ((int)pid != process.Id) return true;
            if (!IsWindowVisible(hwnd)) return true;
            var title = Title(hwnd);
            if (title.Contains(titlePart, StringComparison.Ordinal))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, 0);
        return found;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    private static void CloseDialog(nint hwnd)
    {
        var element = AutomationElement.FromHandle(hwnd);
        if (element != null &&
            element.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern))
        {
            ((WindowPattern)pattern).Close();
            return;
        }
        PostMessage(hwnd, 0x0010 /* WM_CLOSE */, 0, 0);
    }

    // ------------------------------------------------------------------
    // 端到端写入（需配合 --data-dir 使用独立临时库）
    // ------------------------------------------------------------------

    /// <summary>
    /// 走一遍"新建 → 落库 → 详情页 → 删除二次确认 → 软删除"，
    /// 覆盖只读冒烟碰不到的命令链（SaveCommand / DeleteCommand / 导航）。
    /// 必须在 --data-dir 指向的临时库上跑，不碰用户主库。
    /// </summary>
    private static void RunWriteTest(Process process, nint mainHwnd, Options options)
    {
        Console.WriteLine("[e2e] 开始写入验证");
        var title = "[冒烟] 端到端写入验证";

        // ---- 1. 新建问题 ----
        var trigger = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "NewIssueButton");
        if (trigger == null) { Console.WriteLine("[e2e] FAIL: 找不到新建按钮"); return; }
        Activate(trigger);

        var dialogHwnd = WaitForDialog(process, "新建问题", 8);
        if (dialogHwnd == 0) { Console.WriteLine("[e2e] FAIL: 新建弹窗未出现"); return; }
        var dialog = AutomationElement.FromHandle(dialogHwnd);
        if (dialog == null) { Console.WriteLine("[e2e] FAIL: 弹窗 UIA 根不可用"); return; }

        SetText(dialog, "MachineCodeBox", "P7391102");
        SetText(dialog, "TitleBox", title);
        SetText(dialog, "SolutionBox", "临时脚本创建，验证完立即删除");
        var seq = GetText(dialog, "SeqNoBox");
        Console.WriteLine($"[e2e] 序号 = '{seq}'");
        RestoreForeground();
        Console.WriteLine($"[e2e] 表单 -> {Shoot(dialogHwnd, Path.Combine(options.ShotsDir, "11-e2e-form.png"))}");

        var save = TryFindById(dialog, "NewIssueSaveButton");
        if (save == null) { Console.WriteLine("[e2e] FAIL: 找不到创建按钮"); return; }
        Activate(save);

        // 保存后弹窗关闭、主窗跳到详情页；这一步要等导航完成，不能只看弹窗消失。
        var saved = WaitForText(mainHwnd, title, 12);
        if (!saved) { Console.WriteLine("[e2e] FAIL: 保存后详情页没有出现新问题标题"); return; }
        Thread.Sleep(800);
        RestoreForeground();
        Console.WriteLine($"[e2e] 已创建 -> {Shoot(mainHwnd, Path.Combine(options.ShotsDir, "12-e2e-created.png"))}");

        // ---- 2. 推进阶段：弹窗里把"方案确定"设为已完成 ----
        var openStage = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "OpenStageDialogButton");
        if (openStage == null)
        {
            Console.WriteLine("[e2e] WARN: 找不到填写阶段按钮，跳过阶段推进");
        }
        else
        {
            Activate(openStage);
            var stageHwnd = WaitForDialog(process, "更新阶段", 8);
            if (stageHwnd == 0)
            {
                Console.WriteLine("[e2e] FAIL: 阶段弹窗未出现");
                return;
            }
            var stage = AutomationElement.FromHandle(stageHwnd);
            if (stage == null) { Console.WriteLine("[e2e] FAIL: 阶段弹窗 UIA 根不可用"); return; }

            // 状态是 ListBox（设计规范要求用列表语义而不是自绘单选），选"已完成"。
            var stateList = TryFindById(stage, "StageStateList");
            var doneItem = stateList?.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>()
                .FirstOrDefault(e => e.Current.Name.Contains("已完成", StringComparison.Ordinal));
            if (doneItem == null)
            {
                Console.WriteLine("[e2e] FAIL: 阶段状态里找不到「已完成」；实际项：");
                if (stateList != null)
                    foreach (AutomationElement item in stateList.FindAll(
                                 TreeScope.Descendants, Condition.TrueCondition))
                        Console.WriteLine($"  {item.Current.ControlType.ProgrammaticName} " +
                                          $"name='{item.Current.Name}' id='{item.Current.AutomationId}'");
                else
                    Console.WriteLine("  StageStateList 未找到");
                CloseDialog(stageHwnd);
                return;
            }
            ((SelectionItemPattern)doneItem.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Thread.Sleep(300);
            SetText(stage, "StageOwnerBox", "张工");
            SetText(stage, "StageDueBox", "11.20");
            SetText(stage, "StageActualBox", "11.18");
            Thread.Sleep(300);

            // 先故意不写变更原因试存一次：产品规则是"改计划日必须写原因"，
            // 这条校验必须真的拦住，而不是存进去再补日志。
            var probeSave = TryFindById(AutomationElement.FromHandle(stageHwnd)!, "StageSaveButton");
            if (probeSave != null)
            {
                Activate(probeSave);
                Thread.Sleep(1200);
                var blocked = FindDialogHwnd(process, "更新阶段") != 0;
                var hint = HasPartialText(stageHwnd, "变更原因");
                Console.WriteLine($"[e2e] 不写原因保存：弹窗仍在={blocked} 有错误提示={hint}");
                RestoreForeground();
                Console.WriteLine($"[e2e] 被拦下 -> {Shoot(stageHwnd, Path.Combine(options.ShotsDir, "15b-e2e-stage-blocked.png"))}");
                if (!blocked || !hint)
                {
                    Console.WriteLine("[e2e] FAIL: 「改计划日必填原因」没有生效");
                    CloseDialog(stageHwnd);
                    return;
                }
            }

            // 补上原因再存。
            SetText(stage, "StageReasonBox", "冒烟验证：原计划空白，补计划日");
            Thread.Sleep(300);
            RestoreForeground();
            Console.WriteLine($"[e2e] 阶段表单 -> {Shoot(stageHwnd, Path.Combine(options.ShotsDir, "15-e2e-stage.png"))}");

            var stageSave = TryFindById(AutomationElement.FromHandle(stageHwnd)!, "StageSaveButton");
            if (stageSave == null) { Console.WriteLine("[e2e] FAIL: 找不到保存并留痕"); return; }
            Activate(stageSave);

            // 先确认弹窗真的关了：保存被校验拦下时弹窗还在，后面的断言会全部打在弹窗上。
            var closed = false;
            for (var i = 0; i < 25 && !closed; i++)
            {
                Thread.Sleep(200);
                closed = FindDialogHwnd(process, "更新阶段") == 0;
            }
            if (!closed)
            {
                Console.WriteLine("[e2e] FAIL: 阶段弹窗没有关闭（保存可能被校验拦下）");
                Console.WriteLine($"[e2e] 拦下时 -> {Shoot(stageHwnd, Path.Combine(options.ShotsDir, "15b-e2e-stage-blocked.png"))}");
                CloseDialog(stageHwnd);
                return;
            }

            // 摘要里的「阶段 x/4」是唯一可靠的进度口径；不能用页面里任意「已完成」，
            // 因为四个阶段卡片的下拉选项里本来就含这个词（会假绿）。
            var advanced = WaitForText(mainHwnd, "阶段 1/4", 8);
            Console.WriteLine($"[e2e] 阶段已保存，摘要变为「阶段 1/4」= {advanced}");
            RestoreForeground();
            Console.WriteLine($"[e2e] 阶段推进后 -> {Shoot(mainHwnd, Path.Combine(options.ShotsDir, "16-e2e-stage-saved.png"))}");
            if (!advanced) { Console.WriteLine("[e2e] FAIL: 阶段保存后进度没有变化"); return; }
        }

        // ---- 3. 删除（二次确认要逐字抄问题号）----
        var code = "P7391-" + (int.TryParse(seq, out var n) ? n.ToString("D3") : seq);
        Console.WriteLine($"[e2e] 准备删除 {code}");

        var openDelete = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "OpenDeleteDialogButton");
        if (openDelete == null) { Console.WriteLine("[e2e] FAIL: 找不到删除按钮"); return; }
        Activate(openDelete);

        var deleteHwnd = WaitForDialog(process, "删除问题", 8);
        if (deleteHwnd == 0) { Console.WriteLine("[e2e] FAIL: 删除弹窗未出现"); return; }
        var deleteDialog = AutomationElement.FromHandle(deleteHwnd);
        if (deleteDialog == null) { Console.WriteLine("[e2e] FAIL: 删除弹窗 UIA 根不可用"); return; }

        var confirm = TryFindById(deleteDialog, "DeleteConfirmButton");
        Console.WriteLine($"[e2e] 未输入前的确认按钮 enabled = {confirm?.Current.IsEnabled}");
        SetText(deleteDialog, "DeleteConfirmBox", code);
        Thread.Sleep(600);
        confirm = TryFindById(AutomationElement.FromHandle(deleteHwnd)!, "DeleteConfirmButton");
        Console.WriteLine($"[e2e] 输入问题号后 enabled = {confirm?.Current.IsEnabled}");
        RestoreForeground();
        Console.WriteLine($"[e2e] 删除确认 -> {Shoot(deleteHwnd, Path.Combine(options.ShotsDir, "13-e2e-delete.png"))}");

        if (confirm == null || !confirm.Current.IsEnabled)
        {
            Console.WriteLine($"[e2e] FAIL: 输入 {code} 后确认按钮仍不可用");
            CloseDialog(deleteHwnd);
            return;
        }
        Activate(confirm);

        // 软删除成功后详情页应退回台账，标题不再出现在主窗。
        Thread.Sleep(2500);
        var stillThere = HasText(mainHwnd, title);
        Console.WriteLine($"[e2e] 删除后主窗仍含标题 = {stillThere}");
        RestoreForeground();
        Console.WriteLine($"[e2e] 删除后 -> {Shoot(mainHwnd, Path.Combine(options.ShotsDir, "14-e2e-deleted.png"))}");
        Console.WriteLine(stillThere ? "[e2e] FAIL: 删除后标题仍在主窗" : "[e2e] PASS: 新建 → 详情 → 删除全链路通过");
    }

    private static void SetText(AutomationElement root, string id, string value)
    {
        var element = FindById(root, id);
        ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
    }

    private static string GetText(AutomationElement root, string id)
    {
        var element = FindById(root, id);
        return element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
            ? ((ValuePattern)pattern).Current.Value
            : "";
    }

    /// <summary>轮询主窗 UIA 树里是否出现这段文字（详情页标题、台账行都算）。</summary>
    private static bool WaitForText(nint mainHwnd, string text, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (HasText(mainHwnd, text)) return true;
            Thread.Sleep(400);
        }
        return false;
    }

    private static bool HasText(nint mainHwnd, string text)
    {
        return HasTextIn(mainHwnd, text);
    }

    private static bool HasTextIn(nint hwnd, string text)
    {
        var root = AutomationElement.FromHandle(hwnd);
        if (root == null) return false;
        var found = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, text));
        return found != null;
    }

    /// <summary>
    /// 模糊匹配文字。WPF 里一段话可能被拆成多个 Run（含中文引号时尤其明显），
    /// 精确 Name 匹配会漏；校验提示这类断言只关心片段出现。
    /// </summary>
    private static bool HasPartialText(nint hwnd, string part)
    {
        var root = AutomationElement.FromHandle(hwnd);
        if (root == null) return false;
        foreach (AutomationElement element in root.FindAll(
                     TreeScope.Descendants,
                     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
        {
            if (element.Current.Name.Contains(part, StringComparison.Ordinal)) return true;
        }
        return false;
    }


    /// <summary>
    /// 暗色主题必须重建控件树才能正确取色（游离元素收不到资源字典替换通知），
    /// 但这里测的是"用户在界面上点开关"的真实路径，不重建——若截到"深底浅卡"
    /// 说明产品的换肤实现有问题，正是该暴露的。跑完切回浅色，不污染下一次启动。
    /// </summary>
    private static void RunDarkRound(nint mainHwnd, Options options)
    {
        var toggle = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "ThemeToggleButton");
        if (toggle == null)
        {
            Console.WriteLine("[dark] toggle not found, skipped");
            return;
        }
        Activate(toggle);
        Thread.Sleep(1500);

        foreach (var (id, name, file) in Pages)
        {
            var target = TryFindById(AutomationElement.FromHandle(mainHwnd)!, id);
            if (target == null) continue;
            Activate(target);
            Thread.Sleep(900);
            RestoreForeground();
            var darkFile = file.Replace(".png", "-dark.png");
            Console.WriteLine($"[dark] {name} -> {Shoot(mainHwnd, Path.Combine(options.ShotsDir, darkFile))}");
        }

        // 暗色下再走一遍详情与分项目，深色下的表格/卡片最容易被默认白底穿透。
        NavigateDetailAndSubproject(mainHwnd, options, "-dark");

        // 切回浅色：主题是持久化配置，留下暗色会影响交付截图与下次启动。
        var toggleAgain = TryFindById(AutomationElement.FromHandle(mainHwnd)!, "ThemeToggleButton");
        if (toggleAgain != null)
        {
            Activate(toggleAgain);
            Thread.Sleep(1200);
        }
    }

    private static bool FilesEqual(string a, string b)
    {
        var left = File.ReadAllBytes(a);
        var right = File.ReadAllBytes(b);
        return left.Length == right.Length && left.AsSpan().SequenceEqual(right);
    }

    private static void Activate(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
            return;
        }
        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
        {
            ((SelectionItemPattern)select).Select();
            return;
        }
        if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
        {
            ((TogglePattern)toggle).Toggle();
            return;
        }
        throw new InvalidOperationException("元素没有可用的 Invoke/Select/Toggle 模式。");
    }

    private static void DumpTree(AutomationElement root)
    {
        Console.WriteLine("[dump] interactive elements:");
        var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        foreach (AutomationElement element in all)
        {
            var type = element.Current.ControlType.ProgrammaticName;
            if (type is "ControlType.Button" or "ControlType.ListItem" or "ControlType.TabItem"
                or "ControlType.Edit" or "ControlType.CheckBox")
            {
                var r = element.Current.BoundingRectangle;
                Console.WriteLine(
                    $"  {type,-22} name='{element.Current.Name}' id='{element.Current.AutomationId}' " +
                    $"enabled={element.Current.IsEnabled} bounds={r.Left:0},{r.Top:0},{r.Width:0}x{r.Height:0}");
            }
        }
    }

    // ------------------------------------------------------------------
    // 截图 / 自检
    // ------------------------------------------------------------------

    private static string Shoot(nint hwnd, string path)
    {
        GetWindowRect(hwnd, out var rect);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) throw new InvalidOperationException("窗口尺寸无效。");

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        bool ok;
        try
        {
            ok = PrintWindow(hwnd, hdc, 2); // PW_RENDERFULLCONTENT
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
        bitmap.Save(path, ImageFormat.Png);

        // 非空判定：20x20 网格采样，不同颜色太少说明只截到纯色（常见于最小化/未渲染）。
        var colors = new HashSet<int>();
        for (var y = 0; y < height; y += Math.Max(1, height / 20))
        for (var x = 0; x < width; x += Math.Max(1, width / 20))
            colors.Add(bitmap.GetPixel(x, y).ToArgb());

        var state = colors.Count >= 8 ? "ok" : "SUSPECT(blank?)";
        return $"{Path.GetFileName(path)} {width}x{height} colors={colors.Count} print={ok} {state}";
    }

    private static void ReportRoundCornerAudit()
    {
        try
        {
            var logs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sinan", "logs");
            if (!Directory.Exists(logs)) return;
            var latest = Directory.GetFiles(logs, "*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
            if (latest == null) return;

            var lines = File.ReadAllLines(latest, Encoding.UTF8)
                .Where(l => l.Contains("圆角自检", StringComparison.Ordinal))
                .ToList();
            var warns = lines.Count(l => l.Contains("WARN", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"[audit] round-corner lines={lines.Count} warn={warns} log={Path.GetFileName(latest)}");
            foreach (var line in lines.TakeLast(3)) Console.WriteLine($"  {line}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[audit] skipped: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // UIA / Win32
    // ------------------------------------------------------------------

    private static AutomationElement FindById(AutomationElement root, string id) =>
        root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id))
        ?? throw new InvalidOperationException($"找不到元素 {id}。");

    /// <summary>
    /// 在限定时间内轮询查找元素。WPF 窗口从 CreateWindow 到控件树可被 UIA 枚举之间
    /// 有一段空窗，登录页在跨进程启动时尤其明显；固定一次查找会随机失败。
    /// </summary>
    private static AutomationElement WaitForById(nint hwnd, string id, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var window = AutomationElement.FromHandle(hwnd);
            if (window != null)
            {
                var found = window.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, id));
                if (found != null) return found;
            }
            Thread.Sleep(200);
        }
        throw new InvalidOperationException($"等待元素 {id} 超时（{timeoutSeconds}s）。");
    }

    private static AutomationElement? FindButton(AutomationElement root, string namePart)
    {
        var all = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        foreach (AutomationElement element in all)
        {
            if (element.Current.Name.Contains(namePart, StringComparison.Ordinal)) return element;
        }
        return null;
    }

    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;

    private static string Title(nint hwnd)
    {
        if (hwnd == 0) return "";
        var buffer = new StringBuilder(512);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string ClassName(nint hwnd)
    {
        if (hwnd == 0) return "";
        var buffer = new StringBuilder(256);
        GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool value);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder name, int count);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private static uint ThreadOf(nint hwnd) => GetWindowThreadProcessId(hwnd, out _);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    // ------------------------------------------------------------------

    private sealed record Options(
        string ExePath,
        string ShotsDir,
        string EmployeeNo,
        string Password,
        bool Launch,
        bool Restart,
        bool KeepOpen,
        bool NoPages,
        bool NoDark,
        bool Dump,
        string DumpPage,
        bool WriteTest,
        string DataDir)
    {
        public static Options Parse(string[] args)
        {
            var repo = FindRepoRoot();
            var options = new Options(
                ExePath: Path.Combine(repo, "desktop", "src", "Sinan.App",
                    "bin", "Debug", "net10.0-windows", "Sinan.exe"),
                ShotsDir: Path.Combine(Path.GetTempPath(), "sinan-ui-shots"),
                EmployeeNo: "engineer",
                Password: "123456",
                Launch: false,
                Restart: false,
                KeepOpen: false,
                NoPages: false,
                NoDark: false,
                Dump: false,
                DumpPage: "",
                WriteTest: false,
                DataDir: "");

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--launch": options = options with { Launch = true }; break;
                    case "--restart": options = options with { Restart = true }; break;
                    case "--keep-open": options = options with { KeepOpen = true }; break;
                    case "--no-pages": options = options with { NoPages = true }; break;
                    case "--no-dark": options = options with { NoDark = true }; break;
                    case "--dump": options = options with { Dump = true }; break;
                    case "--dump-page": options = options with { Dump = true, DumpPage = args[++i] }; break;
                    case "--write-test": options = options with { WriteTest = true }; break;
                    case "--data-dir": options = options with { DataDir = args[++i] }; break;
                    case "--exe": options = options with { ExePath = args[++i] }; break;
                    case "--shots": options = options with { ShotsDir = args[++i] }; break;
                    case "--employee": options = options with { EmployeeNo = args[++i] }; break;
                    case "--password": options = options with { Password = args[++i] }; break;
                    default:
                        throw new ArgumentException($"未知参数：{args[i]}");
                }
            }
            return options;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "desktop", "src")))
                dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
