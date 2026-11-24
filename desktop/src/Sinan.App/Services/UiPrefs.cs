using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sinan.App.Services;

/// <summary>
/// 本机界面偏好（主题、记住的工号等）。与数据库同根：%LOCALAPPDATA%\Sinan\ui.json。
///
/// 为什么不用 Properties.Settings：那套机制把配置写进用户隔离的 AppData 深处，
/// 现场排障时找不到文件；这里就是一个可读的 JSON，出问题删掉即回默认值。
/// </summary>
public sealed class UiPrefs
{
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "light";

    [JsonPropertyName("rememberEmployeeNo")]
    public bool RememberEmployeeNo { get; set; }

    [JsonPropertyName("lastEmployeeNo")]
    public string LastEmployeeNo { get; set; } = "";

    [JsonPropertyName("lastSyncPort")]
    public int LastSyncPort { get; set; } = 5180;
}

public static class UiPrefsStore
{
    private static readonly object Gate = new();
    private static string _path = "";
    private static UiPrefs _current = new();

    public static UiPrefs Current => _current;

    public static void Initialize(string dataRoot)
    {
        _path = Path.Combine(dataRoot, "ui.json");
        _current = Load();
    }

    public static void Save()
    {
        if (string.IsNullOrWhiteSpace(_path)) return;
        try
        {
            lock (Gate)
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(_path, JsonSerializer.Serialize(_current,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"保存界面偏好失败：{ex.Message}");
        }
    }

    private static UiPrefs Load()
    {
        try
        {
            if (!File.Exists(_path)) return new UiPrefs();
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<UiPrefs>(stream) ?? new UiPrefs();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"读取界面偏好失败，使用默认值：{ex.Message}");
            return new UiPrefs();
        }
    }
}
