using System;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Sofol.Core.Models;

namespace Sofol.App.ViewModels;

public sealed partial class FolderRuleTabViewModel : ObservableObject
{
    public string? FolderPath { get; init; }

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _iconGlyph = "\uE8B7";

    [ObservableProperty]
    private string _activeRuleCountText = "0";

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class WatchedFolderItemViewModel : ObservableObject
{
    private readonly Action<WatchedFolderItemViewModel> _onChanged;
    private bool _isSyncing;

    public WatchedFolderProfile Model { get; }

    public WatchedFolderItemViewModel(
        WatchedFolderProfile model,
        Action<WatchedFolderItemViewModel> onChanged)
    {
        Model = model;
        _onChanged = onChanged;
    }

    public string FolderPath => Model.FolderPath;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _iconGlyph = "\uE8B7";

    [ObservableProperty]
    private bool _isEnabled = true;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_isSyncing)
        {
            return;
        }

        Model.IsEnabled = value;
        _onChanged(this);
    }

    [ObservableProperty]
    private int _delayPresetIndex = WatchedFolderProfile.DefaultDelayPresetIndex;

    partial void OnDelayPresetIndexChanged(int value)
    {
        if (_isSyncing || value < 0)
        {
            return;
        }

        Model.ApplyDelayPresetIndex(value);
        _onChanged(this);
    }

    [ObservableProperty]
    private bool _isActivelyWatching;

    [ObservableProperty]
    private string _statusBadgeText = "○ Đang tạm dừng";

    [ObservableProperty]
    private string _activeRulesSummaryText = string.Empty;

    public void SyncFromModel(AppConfiguration globalConfig, bool isServiceRunning, int pendingCount)
    {
        _isSyncing = true;
        try
        {
            DisplayName = Model.GetDisplayName();
            IconGlyph = Model.GetIconGlyph();
            IsEnabled = Model.IsEnabled;
            DelayPresetIndex = Model.GetDelayPresetIndex();

            bool exists = !string.IsNullOrWhiteSpace(Model.FolderPath) && Directory.Exists(Model.FolderPath);
            IsActivelyWatching = exists && Model.IsEnabled && isServiceRunning;

            if (!exists)
            {
                StatusBadgeText = "⚠ Không tồn tại";
            }
            else if (!Model.IsEnabled)
            {
                StatusBadgeText = "○ Đang tắt";
            }
            else if (!isServiceRunning)
            {
                StatusBadgeText = "○ Tạm dừng";
            }
            else if (pendingCount > 0)
            {
                StatusBadgeText = $"● {pendingCount} tệp chờ";
            }
            else
            {
                StatusBadgeText = "● Đang theo dõi";
            }

            SmartGroupingSettings effectiveSmart = Model.CustomSmartSettings ?? globalConfig.SmartSettings;
            var effectiveRules = Model.CustomRules.Count > 0 ? Model.CustomRules : globalConfig.Rules;
            int enabledCount = effectiveRules.Count(r => r.IsEnabled);

            bool hasMonthGrouping = effectiveSmart.EnableDateSubfolders || effectiveSmart.EnableDirectDateGrouping;
            ActiveRulesSummaryText = hasMonthGrouping
                ? $"Gom theo Năm-Tháng • {enabledCount} quy tắc bật"
                : $"Đang bật {enabledCount} quy tắc";
        }
        finally
        {
            _isSyncing = false;
        }
    }
}
