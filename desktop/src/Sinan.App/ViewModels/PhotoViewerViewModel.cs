using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sinan.App.Mvvm;
using Sinan.App.Services;

namespace Sinan.App.ViewModels;

/// <summary>
/// 查看现场照片（设计稿 dlg-photo）：左侧大图，右侧元数据，底部上一张/下一张。
///
/// 【为什么不把图片塞数据库】附件表只存相对路径（根 AGENTS.md 第 6 节 照片/附件），
/// 这里按需从磁盘读并转成 BitmapImage；文件缺失（演示附件、尚未从手机同步）
/// 时显示占位说明，不弹错误框——现场看到"还没同步过来"比看到红叉有用。
///
/// 【大图内存】BitmapImage 用 OnLoad 立即解码并设 DecodePixelWidth 上限，
/// 避免一次把 4000px 原图整个读进内存（现场手机照片常见 3–5MB）。
/// </summary>
public sealed class PhotoViewerViewModel : ObservableObject
{
    private readonly PhotoViewerRequest _request;
    private int _index;
    private ImageSource? _currentImage;
    private string _missingHint = "";

    public PhotoViewerViewModel(PhotoViewerRequest request)
    {
        _request = request;
        _index = Math.Clamp(request.StartIndex, 0, Math.Max(0, request.Photos.Count - 1));

        PrevCommand = new RelayCommand(() => Move(-1), () => CanPrev);
        NextCommand = new RelayCommand(() => Move(1), () => CanNext);

        LoadCurrent();
    }

    public IReadOnlyList<PhotoViewerItem> Photos => _request.Photos;

    public bool HasPhotos => Photos.Count > 0;
    public int Count => Photos.Count;

    /// <summary>弹窗标题副行：问题号 + 机台 + 第几张。</summary>
    public string HeaderSubtitle =>
        $"{_request.IssueCode} · {_request.MachineCode} · {(_index + 1)} / {Math.Max(Count, 1)}";

    public PhotoViewerItem? Current => HasPhotos ? Photos[_index] : null;

    public string FileName => Current?.FileName ?? "—";
    public string Caption => string.IsNullOrWhiteSpace(Current?.Caption) ? "（无说明）" : Current!.Caption;
    public string TakenAtDisplay => Current?.TakenAtDisplay ?? "—";
    public string UploadedBy => string.IsNullOrWhiteSpace(Current?.UploadedBy) ? "—" : Current!.UploadedBy;
    public string SizeDisplay => Current?.SizeDisplay ?? "—";
    public string DimensionText => string.IsNullOrWhiteSpace(Current?.DimensionText) ? "—" : Current!.DimensionText;
    public string IssueCode => _request.IssueCode;
    public string IssueTitle => _request.IssueTitle;
    public string MachineCode => _request.MachineCode;
    public string SubprojectCode => string.IsNullOrWhiteSpace(_request.SubprojectCode)
        ? "待确认" : _request.SubprojectCode;

    /// <summary>当前大图；null 时界面显示占位图标。</summary>
    public ImageSource? CurrentImage
    {
        get => _currentImage;
        private set
        {
            if (!SetProperty(ref _currentImage, value)) return;
            OnPropertyChanged(nameof(HasImage));
        }
    }

    public bool HasImage => _currentImage != null;

    /// <summary>图片缺失原因（演示附件 / 未同步 / 文件被移走），就地说明不弹框。</summary>
    public string MissingHint
    {
        get => _missingHint;
        private set
        {
            if (!SetProperty(ref _missingHint, value)) return;
            OnPropertiesChanged(nameof(HasMissingHint), nameof(ShowDetailHint));
        }
    }

    public bool HasMissingHint => !string.IsNullOrWhiteSpace(_missingHint);

    /// <summary>
    /// 右下角那条"缺文件"红字：只在这条问题**确实有照片记录**但读不到文件时显示。
    /// 完全没有照片时大图区已经写了"还没有现场照片"，再重复一次只是噪音。
    /// </summary>
    public bool ShowDetailHint => HasPhotos && HasMissingHint;

    public bool CanPrev => _index > 0;
    public bool CanNext => _index < Count - 1;

    public RelayCommand PrevCommand { get; }
    public RelayCommand NextCommand { get; }

    private void Move(int delta)
    {
        int next = _index + delta;
        if (next < 0 || next >= Count) return;
        _index = next;
        LoadCurrent();
    }

    private void LoadCurrent()
    {
        var item = Current;
        if (item == null)
        {
            CurrentImage = null;
            MissingHint = "这条问题还没有现场照片。";
            NotifyNav();
            return;
        }

        if (string.IsNullOrWhiteSpace(item.AbsolutePath))
        {
            CurrentImage = null;
            MissingHint = "只有附件记录、没有文件：演示附件或尚未从手机同步到本机。";
            NotifyNav();
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;   // 立即解码，不锁文件句柄
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(item.AbsolutePath, UriKind.Absolute);
            // 大图上限 1600px：与手机端压缩口径一致，避免原图撑爆内存。
            bitmap.DecodePixelWidth = 1600;
            bitmap.EndInit();
            bitmap.Freeze();

            CurrentImage = bitmap;
            MissingHint = "";
        }
        catch (Exception ex)
        {
            CurrentImage = null;
            MissingHint = $"图片读取失败：{ex.Message}";
        }

        NotifyNav();
        RefreshNav();
    }

    private void NotifyNav() =>
        OnPropertiesChanged(nameof(HeaderSubtitle), nameof(Current), nameof(FileName), nameof(Caption),
            nameof(TakenAtDisplay), nameof(UploadedBy), nameof(SizeDisplay), nameof(DimensionText),
            nameof(HasImage), nameof(HasMissingHint));

    /// <summary>上一张/下一张按钮可用性变化时由界面调用（命令 CanExecute 需要显式通知）。</summary>
    public void RefreshNav()
    {
        PrevCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
    }
}
