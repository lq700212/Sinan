using System;
using System.IO;
using System.Text;

namespace Sinan.App;

/// <summary>
/// 自写滚动文件日志（不引 log4net，desktop/AGENTS.md 第 1 节技术栈约定）。
///
/// 目录：%LOCALAPPDATA%\Sinan\logs\；按天一个文件 sinan-yyyyMMdd.log，
/// 单文件超过 4MB 时改名 .1 继续写，最多保留 5 个历史文件。
/// 日志只记关键边沿（启动/登录/导入/导出/异常），不记每条 SQL，避免现场无从下手。
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 4 * 1024 * 1024;
    private const int KeepFiles = 5;
    private static readonly object Gate = new();
    private static string _dir = "";

    /// <summary>由组合根在数据库初始化后调用，保证日志与数据库同根。</summary>
    public static void Initialize(string logsDir)
    {
        _dir = logsDir;
        Directory.CreateDirectory(_dir);
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        if (string.IsNullOrWhiteSpace(_dir)) return;

        try
        {
            lock (Gate)
            {
                var file = Path.Combine(_dir, $"sinan-{DateTime.Now:yyyyMMdd}.log");
                Roll(file);
                var builder = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(level).Append("] ")
                    .AppendLine(message);
                if (ex != null)
                {
                    builder.AppendLine(ex.ToString());
                }
                File.AppendAllText(file, builder.ToString(), new UTF8Encoding(false));
            }
        }
        catch
        {
            // 日志自身失败不能反过来拖垮业务：磁盘满/权限不足时静默放弃。
        }
    }

    private static void Roll(string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.Length < MaxBytes) return;

        for (var i = KeepFiles - 1; i >= 1; i--)
        {
            var from = $"{file}.{i}";
            var to = $"{file}.{i + 1}";
            if (!File.Exists(from)) continue;
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }
        if (File.Exists(file + ".1")) File.Delete(file + ".1");
        File.Move(file, file + ".1");
    }
}
