using System;
using System.Windows;

namespace Sinan.App.Services;

public enum ThemeKind
{
    Light,
    Dark
}

/// <summary>
/// 明/暗主题切换。
///
/// 机制：App.xaml 的 MergedDictionaries 固定为 [0]=主题键板、[1]=控件样式。
/// 切主题只整体替换索引 0 的字典，控件样式里的 DynamicResource 自动跟随，
/// 页面代码不出现任何色值（AGENTS.md 第 8 节：只允许引用主题键）。
///
/// 持久化走 <see cref="UiPrefsStore"/>（与数据库同根 ui.json）；读取失败一律回落浅色。
/// </summary>
public static class ThemeManager
{
    private const int ThemeDictionaryIndex = 0;

    public static ThemeKind Current { get; private set; } = ThemeKind.Light;

    public static event EventHandler? Changed;

    /// <summary>组合根在创建主窗之前调用：初始化偏好、读上次选择、应用键板。</summary>
    public static void Initialize(string dataRoot)
    {
        UiPrefsStore.Initialize(dataRoot);
        Current = UiPrefsStore.Current.Theme == "dark" ? ThemeKind.Dark : ThemeKind.Light;
        Apply(Current);
    }

    public static void Toggle()
    {
        Apply(Current == ThemeKind.Light ? ThemeKind.Dark : ThemeKind.Light);
        UiPrefsStore.Current.Theme = Current == ThemeKind.Dark ? "dark" : "light";
        UiPrefsStore.Save();
    }

    public static void Apply(ThemeKind kind)
    {
        var app = Application.Current;
        if (app == null) return;

        var dictionaries = app.Resources.MergedDictionaries;
        var source = new Uri($"Themes/Sinan.{kind}.xaml", UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = source };

        // 先构造新字典再整体替换：直接改 Source 会让控件短暂取不到键，出现白底闪烁。
        if (dictionaries.Count > ThemeDictionaryIndex)
            dictionaries[ThemeDictionaryIndex] = dictionary;
        else
            dictionaries.Insert(0, dictionary);

        Current = kind;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
