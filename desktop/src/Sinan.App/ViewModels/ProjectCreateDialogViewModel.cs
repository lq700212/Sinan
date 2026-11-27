using System;
using System.Globalization;
using System.Linq;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Utils;

namespace Sinan.App.ViewModels;

/// <summary>
/// 新建总项目：项目号 + 名称 + 客户。
///
/// 规则：
/// - 项目号必须 P + 4 位数字（P7392），且不能与已有项目重复；
/// - 名称必填；客户可空（内部调试项目没有客户）；
/// - 新建后自动建 01/02/03 三个分项目，机台与问题留给导入/现场新建。
/// 校验全部就地报错，不关闭弹窗、不弹二级提示（desktop/AGENTS.md 第 5 节）。
/// </summary>
public sealed class ProjectCreateDialogViewModel : ObservableObject
{
    private readonly ProjectCreateRequest _request;
    private string _code;
    private string _name = "安装调试问题闭环";
    private string _customer = "";
    private string _errorMessage = "";

    public ProjectCreateDialogViewModel(ProjectCreateRequest request)
    {
        _request = request;
        _code = string.IsNullOrWhiteSpace(request.SuggestedCode)
            ? NextCode(request.ExistingCodes)
            : request.SuggestedCode;

        CreateCommand = new RelayCommand(Create);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? Created;
    public event EventHandler? Cancelled;

    public string Code
    {
        get => _code;
        set
        {
            if (SetProperty(ref _code, value)) ClearError();
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value)) ClearError();
        }
    }

    public string Customer
    {
        get => _customer;
        set => SetProperty(ref _customer, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value)) return;
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string CodeHint => "P + 4 位数字，例如 P7392；保存后自动创建 01 / 02 / 03 三个分项目。";

    public string NameHint => "例如：P7392 安装调试问题闭环。";

    public string CustomerHint => "可空；内部项目可写“调试组自建”。";

    public RelayCommand CreateCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>校验通过后的结果；校验失败返回 null 且 ErrorMessage 就地显示。</summary>
    public ProjectCreateResult? BuildResult()
    {
        var code = TextUtil.Normalize(Code).ToUpperInvariant();
        if (!TextUtil.IsProjectCode(code))
        {
            ErrorMessage = "项目号格式不对：应为 P + 4 位数字，例如 P7392。";
            return null;
        }

        if (_request.ExistingCodes.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = $"项目号 {code} 已存在，请换一个。";
            return null;
        }

        var name = TextUtil.Normalize(Name);
        if (name.Length == 0)
        {
            ErrorMessage = "项目名称不能为空。";
            return null;
        }

        return new ProjectCreateResult
        {
            Code = code,
            Name = name,
            Customer = TextUtil.Normalize(Customer)
        };
    }

    private void Create()
    {
        var result = BuildResult();
        if (result == null) return;
        Result = result;
        Created?.Invoke(this, EventArgs.Empty);
    }

    public ProjectCreateResult? Result { get; private set; }

    private void ClearError()
    {
        if (HasError) ErrorMessage = "";
    }

    /// <summary>
    /// 建议下一个项目号：P7391 之后给 P7392；优先取已有 P#### 的最大值 +1，
    /// 不按"项目条数"算，避免删除/跳号后撞号。
    /// </summary>
    public static string NextCode(System.Collections.Generic.IEnumerable<string> existing)
    {
        var max = existing
            .Where(TextUtil.IsProjectCode)
            .Select(c => int.TryParse(c.Trim().ToUpperInvariant()[1..], NumberStyles.None,
                CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(7391)
            .Max();
        return "P" + (max + 1).ToString(CultureInfo.InvariantCulture);
    }
}
