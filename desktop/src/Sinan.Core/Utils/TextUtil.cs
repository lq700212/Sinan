using System.Text;
using System.Text.RegularExpressions;

namespace Sinan.Core.Utils;

/// <summary>
/// 文本归一化与人员拆分。原表里全角斜杠、顿号、中文逗号混用，
/// 匹配/拆分前统一 NFKC，但写入数据库时仍保留原文。
/// </summary>
public static partial class TextUtil
{
    [GeneratedRegex(@"^P7391\d{3}(-\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex StrictMachineCode();

    [GeneratedRegex(@"^P\d{4}\d{3,4}(-\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LooseMachineCode();

    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var normalized = value.Normalize(NormalizationForm.FormKC);
        return normalized.Replace("\u200B", "").Replace("\uFEFF", "").Trim();
    }

    /// <summary>多人责任人拆分：张工/李工、张工／李工、张工、李工 都拆开。</summary>
    public static List<string> SplitOwners(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();
        return value
            .Replace('／', '/')
            .Replace('、', '/')
            .Replace('，', '/')
            .Replace(',', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && x != "—" && x != "-")
            .Distinct()
            .ToList();
    }

    /// <summary>严格规则：P7391 + 3 位机台号 + 可选 -序号。P73913118 会判为 false。</summary>
    public static bool IsStrictMachineCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && StrictMachineCode().IsMatch(Normalize(code));

    /// <summary>宽松规则：只要像机台号就允许导入，避免把"多一位"的笔误直接拒掉。</summary>
    public static bool LooksLikeMachineCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && LooseMachineCode().IsMatch(Normalize(code));

    public static string MachineBaseCode(string? code)
    {
        var text = Normalize(code);
        var dash = text.IndexOf('-');
        return dash > 0 ? text[..dash] : text;
    }

    /// <summary>P7391106 → 106；用于机台排序。</summary>
    public static int? MachineSeq(string? code)
    {
        var text = MachineBaseCode(code);
        if (text.Length < 8) return null;
        return int.TryParse(text[5..], out var seq) ? seq : null;
    }

    public static string Initial(string? name)
    {
        var text = Normalize(name);
        return text.Length == 0 ? "?" : text[..1];
    }

    public static string Truncate(string? text, int max, string suffix = "…")
    {
        var value = text ?? "";
        return value.Length <= max ? value : value[..max] + suffix;
    }
}
