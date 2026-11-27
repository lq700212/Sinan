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

    /// <summary>项目号规则：P + 4 位数字（P7391 / P7392…）。新增总项目时按这个校验。</summary>
    [GeneratedRegex(@"^P\d{4}$", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectCodePattern();

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

    /// <summary>严格规则（默认 P7391）：P7391 + 3 位机台号 + 可选 -序号。P73913118 会判为 false。</summary>
    public static bool IsStrictMachineCode(string? code) => IsStrictMachineCode(code, "P7391");

    /// <summary>
    /// 严格规则（按总项目号）：{projectCode} + 3 位机台号 + 可选 -序号。
    /// 多总项目后机台号前缀必须跟当前项目一致，避免把 P7392 的机台建到 P7391 下。
    /// </summary>
    public static bool IsStrictMachineCode(string? code, string? projectCode)
    {
        var text = Normalize(code).ToUpperInvariant();
        var prefix = Normalize(projectCode).ToUpperInvariant();
        if (text.Length == 0 || !ProjectCodePattern().IsMatch(prefix)) return false;
        if (!text.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rest = text[prefix.Length..];
        return Regex.IsMatch(rest, @"^\d{3}(-\d+)?$");
    }

    /// <summary>宽松规则：只要像机台号就允许导入，避免把"多一位"的笔误直接拒掉。</summary>
    public static bool LooksLikeMachineCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && LooseMachineCode().IsMatch(Normalize(code));

    /// <summary>项目号格式：P + 4 位数字。</summary>
    public static bool IsProjectCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && ProjectCodePattern().IsMatch(Normalize(code));

    /// <summary>
    /// 从机台号推导项目号：P7392106 → P7392。
    /// 手机端上行"新建问题"只带机台号时，服务端靠这个把问题落到正确总项目，
    /// 不能再默认塞进第一个项目。格式不像机台号时返回空串。
    /// </summary>
    public static string ProjectCodeOfMachine(string? code)
    {
        var text = MachineBaseCode(code).ToUpperInvariant();
        if (text.Length < 5 || text[0] != 'P') return "";
        var candidate = text[..5];
        return IsProjectCode(candidate) ? candidate : "";
    }

    public static string MachineBaseCode(string? code)
    {
        var text = Normalize(code);
        var dash = text.IndexOf('-');
        return dash > 0 ? text[..dash] : text;
    }

    /// <summary>P7391106 → 106；用于机台排序（默认 P7391 前缀）。</summary>
    public static int? MachineSeq(string? code) => MachineSeq(code, "P7391");

    /// <summary>
    /// 按项目号取机台序号：P7392106 + P7392 → 106。
    /// 保留旧口径：多一位的笔误（P73913118 → 3118）照样取数，供"待确认"排序；
    /// 是否合法由 <see cref="IsStrictMachineCode(string?, string?)"/> 单独判定，不混在序号提取里。
    /// </summary>
    public static int? MachineSeq(string? code, string? projectCode)
    {
        var text = MachineBaseCode(code).ToUpperInvariant();
        var prefix = Normalize(projectCode).ToUpperInvariant();
        if (prefix.Length == 0 || text.Length <= prefix.Length) return null;
        if (!text.StartsWith(prefix, StringComparison.Ordinal)) return null;
        return int.TryParse(text[prefix.Length..], out var seq) ? seq : null;
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
