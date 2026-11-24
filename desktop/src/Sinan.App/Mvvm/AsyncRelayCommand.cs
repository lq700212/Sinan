using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Sinan.App.Mvvm;

/// <summary>
/// 异步命令：Excel 导入、导出、数据库读写等超过 50ms 的操作必须走它，
/// 避免 UI 线程假死（AGENTS.md 第 7 节：UI 线程禁做超过 50ms 的 IO）。
///
/// 执行期间 IsRunning=true，命令自动 CanExecute=false，按钮置灰；
/// 异常不吞：交给调用方在 execute 内部处理并给出用户可读提示，这里只兜底记录。
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _running;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute())
    {
    }

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// 全局错误出口：命令是 async void 边界，异常若继续向上抛会直接终止进程。
    /// 组合根订阅本事件统一弹可读提示；命令内部已经处理过的异常不要再抛。
    /// </summary>
    public static event EventHandler<Exception>? UnhandledError;

    public bool IsRunning
    {
        get => _running;
        private set
        {
            if (_running == value) return;
            _running = value;
            RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute(object? parameter) =>
        !_running && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        IsRunning = true;
        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            // 命令是 async void 边界：这里必须收口，绝不能再 throw（会杀进程）。
            AppLog.Error("异步命令执行失败", ex);
            UnhandledError?.Invoke(this, ex);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
