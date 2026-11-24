using System.Windows.Controls;

namespace Sinan.App.Views;

/// <summary>
/// 项目总览页。全部绑定在 <see cref="ViewModels.OverviewViewModel"/>，
/// 这里不写任何业务 glue。
/// </summary>
public partial class OverviewPage : UserControl
{
    public OverviewPage() => InitializeComponent();
}
