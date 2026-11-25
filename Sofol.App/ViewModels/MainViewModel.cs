using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Sofol.App.Helpers;
using Sofol.App.Services;
using Sofol.Core.Models;
using Sofol.Core.Services;

namespace Sofol.App.ViewModels;

public enum PreviewFilterCategory
{
    All = 0,
    SmartGroups = 1,
    Ready = 2,
    LockedOrSkipped = 3
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ConfigAndHistoryStore _store;
    private readonly FileOrganizerEngine _engine;
    private readonly FolderWatcherService _watcher;

    private DispatcherQueue? _dispatcher;
    private AppConfiguration _config;
    private bool _isInitializing;
    private string? _selectedScopeFolderPath;

    public MainViewModel()
    {
        _store = new ConfigAndHistoryStore();
        _engine = new FileOrganizerEngine();
        _watcher = new FolderWatcherService(_engine, _store);
        _config = _store.LoadConfiguration();

        _watcher.LogAdded += OnWatcherLogAdded;
        _watcher.SessionCompleted += OnWatcherSessionCompleted;
        _watcher.StateChanged += OnWatcherStateChanged;
        _watcher.PendingCountsChanged += () => RunOnUi(RefreshWatchedFolderItemsDisplay);

        SyncFieldsFromConfig();
        ReloadHistoryCollection();
    }

    public string StorageDirectory => _store.StorageDirectory;

    public IReadOnlyList<string> DelayPresetLabels { get; } =
        WatchedFolderProfile.DelayPresets.Select(p => p.DisplayText).ToList();

    public ObservableCollection<PlanItemViewModel> AllPreviewItems { get; } = [];

    public ObservableCollection<PlanItemViewModel> FilteredPreviewItems { get; } = [];

    public ObservableCollection<OrganizationRule> Rules { get; } = [];

    public ObservableCollection<OrganizationSession> HistorySessions { get; } = [];

    public ObservableCollection<string> WatcherLogs { get; } = [];

    public ObservableCollection<WatchedFolderItemViewModel> WatchedFolderItems { get; } = [];

    public ObservableCollection<FolderRuleTabViewModel> FolderRuleTabs { get; } = [];

    [ObservableProperty]
    private string _targetFolderPath = string.Empty;

    [ObservableProperty]
    private string _targetFolderDisplayName = "Chưa chọn thư mục";

    partial void OnTargetFolderPathChanged(string value)
    {
        UpdateTargetFolderDisplayName(value);
        UpdateDashboardFolderRuleState();
    }

    [ObservableProperty]
    private bool _hasNoWatchedFolders = true;

    [ObservableProperty]
    private bool _isTargetFolderWatched;

    [ObservableProperty]
    private bool _canPinTargetFolderToWatcher;

    [ObservableProperty]
    private string _selectedRuleTabFolderName = "Mặc định";

    [ObservableProperty]
    private string _selectedRuleTabFolderPath = string.Empty;

    [ObservableProperty]
    private string _targetFolderRuleButtonText = "Quy tắc";

    [ObservableProperty]
    private bool _isSmartGroupingAdvancedOpen;

    [RelayCommand]
    private void ToggleSmartGroupingAdvanced()
    {
        IsSmartGroupingAdvancedOpen = !IsSmartGroupingAdvancedOpen;
    }

    [ObservableProperty]
    private bool _enableFolderMonthGrouping;

    partial void OnEnableFolderMonthGroupingChanged(bool value)
    {
        if (_isInitializing) return;
        SmartGroupingSettings s = GetEditableRuleSmartSettings();
        s.EnableDateSubfolders = value;
        s.EnableDirectDateGrouping = value;
        SaveConfigAndReanalyze();
        RefreshWatchedFolderItemsDisplay();
    }

    [ObservableProperty]
    private string _trayFolderTitle = "Chưa chọn thư mục";

    [ObservableProperty]
    private string _trayFolderSubtitle = "Hãy thêm thư mục theo dõi";

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value) => ApplyPreviewFilter();

    [ObservableProperty]
    private int _selectedFilterIndex;

    partial void OnSelectedFilterIndexChanged(int value) => ApplyPreviewFilter();

    [ObservableProperty]
    private int _totalFilesScanned;

    [ObservableProperty]
    private int _existingSubfoldersCount;

    [ObservableProperty]
    private int _smartGroupsCount;

    [ObservableProperty]
    private int _distinctDestinationFoldersCount;

    [ObservableProperty]
    private int _skippedOrLockedCount;

    [ObservableProperty]
    private int _selectedReadyCount;

    [ObservableProperty]
    private string _formattedTotalReadySize = "0 B";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _isInfoBarOpen;

    [ObservableProperty]
    private string _infoBarTitle = string.Empty;

    [ObservableProperty]
    private string _infoBarMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private bool _hasNoPreviewItems = true;

    [ObservableProperty]
    private bool _hasNoHistory = true;

    [ObservableProperty]
    private bool _hasNoWatcherLogs = true;

    [ObservableProperty]
    private bool _canUndoLatestSession;

    [ObservableProperty]
    private string _latestUndoSummary = string.Empty;

    // Thiết lập quy tắc cho tab thư mục đang chọn
    [ObservableProperty]
    private bool _enablePrefixSimilarityGrouping;

    partial void OnEnablePrefixSimilarityGroupingChanged(bool value)
    {
        if (_isInitializing) return;
        GetEditableRuleSmartSettings().EnablePrefixSimilarityGrouping = value;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private bool _enableExtensionRules;

    partial void OnEnableExtensionRulesChanged(bool value)
    {
        if (_isInitializing) return;
        GetEditableRuleSmartSettings().EnableExtensionRules = value;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private bool _enableKeywordRules;

    partial void OnEnableKeywordRulesChanged(bool value)
    {
        if (_isInitializing) return;
        GetEditableRuleSmartSettings().EnableKeywordRules = value;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private double _minFilesForPrefixGroup = 2;

    partial void OnMinFilesForPrefixGroupChanged(double value)
    {
        if (_isInitializing) return;
        GetEditableRuleSmartSettings().MinFilesForPrefixGroup = Math.Clamp((int)value, 2, 20);
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private double _similarityThresholdPercent = 80;

    partial void OnSimilarityThresholdPercentChanged(double value)
    {
        if (_isInitializing) return;
        GetEditableRuleSmartSettings().SimilarityThreshold = Math.Clamp(value / 100.0, 0.50, 0.98);
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private bool _groupPrefixInsideCategory;

    partial void OnGroupPrefixInsideCategoryChanged(bool value)
    {
        if (_isInitializing) return;
        GetEditableRuleSmartSettings().GroupPrefixInsideCategory = value;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private bool _useDedicatedSmartRootFolder = true;

    partial void OnUseDedicatedSmartRootFolderChanged(bool value)
    {
        if (_isInitializing) return;
        GetEditableRuleSmartSettings().PrefixGroupLocation = value
            ? PrefixGroupLocationMode.DedicatedRootSubfolder
            : PrefixGroupLocationMode.DirectSubfolder;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private string _prefixGroupRootFolderName = "Gom theo tên";

    partial void OnPrefixGroupRootFolderNameChanged(string value)
    {
        if (_isInitializing) return;
        GetEditableRuleSmartSettings().PrefixGroupRootFolderName = string.IsNullOrWhiteSpace(value) ? "Gom theo tên" : value.Trim();
        SaveConfigAndReanalyze();
    }

    // Thiết lập chung toàn ứng dụng (SettingsPage)
    [ObservableProperty]
    private bool _enableDateSubfolders;

    partial void OnEnableDateSubfoldersChanged(bool value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.EnableDateSubfolders = value;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private bool _enableLargeFileIsolation;

    partial void OnEnableLargeFileIsolationChanged(bool value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.EnableLargeFileIsolation = value;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private double _largeFileThresholdMB = 500;

    partial void OnLargeFileThresholdMBChanged(double value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.LargeFileThresholdMB = Math.Max(10, (long)value);
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private bool _moveUnmatchedToOthers = true;

    partial void OnMoveUnmatchedToOthersChanged(bool value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.MoveUnmatchedToOthers = value;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private string _othersFolderName = "Khác";

    partial void OnOthersFolderNameChanged(string value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.OthersFolderName = string.IsNullOrWhiteSpace(value) ? "Khác" : value.Trim();
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private int _selectedConflictIndex;

    partial void OnSelectedConflictIndexChanged(int value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.ConflictBehavior = (ConflictResolutionMode)Math.Clamp(value, 0, 2);
        foreach (WatchedFolderProfile profile in _config.WatchedFolders)
        {
            if (profile.CustomSmartSettings is not null)
            {
                profile.CustomSmartSettings.ConflictBehavior = _config.SmartSettings.ConflictBehavior;
            }
        }
        SaveConfigurationOnly();
    }

    [ObservableProperty]
    private double _autoWatcherDelayValue = 10;

    partial void OnAutoWatcherDelayValueChanged(double value)
    {
        if (_isInitializing) return;
        int clamped = double.IsNaN(value) ? 1 : Math.Clamp((int)Math.Round(value), 1, 3650);
        if (Math.Abs(value - clamped) > 0.001)
        {
            AutoWatcherDelayValue = clamped;
            return;
        }

        _config.SmartSettings.AutoWatcherDelayValue = clamped;
        _config.SmartSettings.AutoWatcherDelaySeconds = (int)Math.Clamp(
            _config.SmartSettings.GetAutoWatcherDelayTimeSpan().TotalSeconds,
            1,
            int.MaxValue);
        SaveConfigurationOnly();
        UpdateWatcherDisplayProperties();
    }

    [ObservableProperty]
    private int _selectedWatcherDelayUnitIndex;

    partial void OnSelectedWatcherDelayUnitIndexChanged(int value)
    {
        if (_isInitializing || value < 0) return;
        _config.SmartSettings.AutoWatcherDelayUnit = (WatcherDelayUnit)Math.Clamp(value, 0, 5);
        _config.SmartSettings.AutoWatcherDelaySeconds = (int)Math.Clamp(
            _config.SmartSettings.GetAutoWatcherDelayTimeSpan().TotalSeconds,
            1,
            int.MaxValue);
        SaveConfigurationOnly();
        UpdateWatcherDisplayProperties();
    }

    [ObservableProperty]
    private bool _isWatcherRunning;

    [ObservableProperty]
    private string _watcherStatusTitle = "Dịch vụ Tự động sắp xếp đang tắt";

    [ObservableProperty]
    private string _watcherStatusText = "Đang tắt";

    [ObservableProperty]
    private string _watcherBadgeText = "○ ĐANG TẠM DỪNG";

    [ObservableProperty]
    private string _watcherCompactBadgeText = "○ Đang tạm dừng";

    [ObservableProperty]
    private string _watcherCompactDelayText = "Theo dõi tự động theo lịch từng thư mục";

    [ObservableProperty]
    private string _watcherCompactActionText = "Bật tự động";

    [ObservableProperty]
    private string _watcherActionButtonGlyph = "\uE768";

    [ObservableProperty]
    private int _selectedThemeIndex;

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_isInitializing || value < 0) return;
        _config.SmartSettings.AppTheme = (AppThemePreference)Math.Clamp(value, 0, 2);
        SaveConfigurationOnly();
        App.ApplyTheme(value);
    }

    [ObservableProperty]
    private bool _runAtWindowsStartup;

    partial void OnRunAtWindowsStartupChanged(bool value)
    {
        if (_isInitializing) return;
        InstallationHelper.SetRunAtStartup(value);
    }

    [ObservableProperty]
    private bool _showWindowOnWindowsStartup;

    partial void OnShowWindowOnWindowsStartupChanged(bool value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.ShowWindowOnWindowsStartup = value;
        SaveConfigurationOnly();
    }

    // Cập nhật ứng dụng từ GitHub Releases
    [ObservableProperty]
    private string _gitHubRepository = "lichbich/sofol";

    partial void OnGitHubRepositoryChanged(string value)
    {
        if (_isInitializing) return;
        _config.GitHubRepository = string.IsNullOrWhiteSpace(value) || value.Equals("trong/Sofol", StringComparison.OrdinalIgnoreCase)
            ? "lichbich/sofol"
            : value.Trim();
        SaveConfigurationOnly();
    }

    [ObservableProperty]
    private bool _autoCheckUpdatesOnStartup = true;

    partial void OnAutoCheckUpdatesOnStartupChanged(bool value)
    {
        if (_isInitializing) return;
        _config.AutoCheckUpdatesOnStartup = value;
        SaveConfigurationOnly();
    }

    public string CurrentAppVersion => InstallationHelper.AppVersion;

    [ObservableProperty]
    private bool _isCheckingForUpdate;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private double _updateProgressPercent;

    [ObservableProperty]
    private string _updateProgressStatus = string.Empty;

    [ObservableProperty]
    private AppUpdateInfo? _availableUpdate;

    [ObservableProperty]
    private bool _isUpdateInfoBarOpen;

    [ObservableProperty]
    private InfoBarSeverity _updateInfoBarSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string _updateInfoBarTitle = string.Empty;

    [ObservableProperty]
    private string _updateInfoBarMessage = string.Empty;

    [ObservableProperty]
    private bool _hasUpdateAvailable;

    [ObservableProperty]
    private string _currentVersionBadgeText = $"v{InstallationHelper.AppVersion} (Bản mới nhất)";

    [ObservableProperty]
    private bool _isGlobalUpdateBannerOpen;

    [ObservableProperty]
    private string _globalUpdateBannerText = string.Empty;

    public async Task CheckForUpdatesAsync(bool silent = false)
    {
        if (IsCheckingForUpdate || IsDownloadingUpdate)
        {
            return;
        }

        IsCheckingForUpdate = true;
        if (!silent)
        {
            IsUpdateInfoBarOpen = true;
            UpdateInfoBarSeverity = InfoBarSeverity.Informational;
            UpdateInfoBarTitle = "Đang kiểm tra...";
            UpdateInfoBarMessage = "Đang kiểm tra xem có phiên bản mới hơn không...";
        }

        try
        {
            AppUpdateInfo info = await UpdateService.CheckForUpdateAsync(GitHubRepository);
            AvailableUpdate = info;

            if (!string.IsNullOrEmpty(info.ErrorMessage))
            {
                if (!silent)
                {
                    IsUpdateInfoBarOpen = true;
                    UpdateInfoBarSeverity = InfoBarSeverity.Error;
                    UpdateInfoBarTitle = "Không thể kiểm tra bản cập nhật";
                    UpdateInfoBarMessage = info.ErrorMessage;
                }
                HasUpdateAvailable = false;
            }
            else if (info.HasUpdate)
            {
                HasUpdateAvailable = true;
                CurrentVersionBadgeText = $"v{CurrentAppVersion} (Có bản mới v{info.LatestVersion})";
                GlobalUpdateBannerText = $"Đã có phiên bản Sofol v{info.LatestVersion} mới sẵn sàng cập nhật.";
                IsGlobalUpdateBannerOpen = true;
                IsUpdateInfoBarOpen = true;
                UpdateInfoBarSeverity = InfoBarSeverity.Success;
                UpdateInfoBarTitle = $"Đã có phiên bản mới v{info.LatestVersion}!";
                UpdateInfoBarMessage = string.IsNullOrWhiteSpace(info.ReleaseNotes)
                    ? "Đã có bản cập nhật mới. Bạn có thể bấm nút Cập nhật ngay bên dưới."
                    : $"Nội dung mới: {info.ReleaseNotes}";
            }
            else
            {
                HasUpdateAvailable = false;
                IsGlobalUpdateBannerOpen = false;
                CurrentVersionBadgeText = $"v{CurrentAppVersion} (Bản mới nhất)";
                if (!silent)
                {
                    IsUpdateInfoBarOpen = true;
                    UpdateInfoBarSeverity = InfoBarSeverity.Informational;
                    UpdateInfoBarTitle = "Bạn đang dùng bản mới nhất";
                    UpdateInfoBarMessage = $"Bạn đang sử dụng phiên bản v{CurrentAppVersion} mới nhất.";
                }
            }
        }
        catch
        {
            if (!silent)
            {
                IsUpdateInfoBarOpen = true;
                UpdateInfoBarSeverity = InfoBarSeverity.Error;
                UpdateInfoBarTitle = "Không thể kiểm tra bản cập nhật";
                UpdateInfoBarMessage = "Không thể kết nối đến máy chủ. Vui lòng kiểm tra lại kết nối mạng của bạn.";
            }
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    public async Task ApplyUpdateAsync()
    {
        if (AvailableUpdate is null || !AvailableUpdate.HasUpdate || IsDownloadingUpdate)
        {
            return;
        }

        IsDownloadingUpdate = true;
        UpdateProgressPercent = 0;
        UpdateProgressStatus = "Đang chuẩn bị tải về...";

        var progress = new Progress<(double percent, string status)>(p =>
        {
            _dispatcher?.TryEnqueue(() =>
            {
                UpdateProgressPercent = p.percent;
                UpdateProgressStatus = p.status;
            });
        });

        try
        {
            await Task.Run(() => UpdateService.DownloadAndApplyUpdateAsync(AvailableUpdate, progress));
        }
        catch (Exception ex)
        {
            IsDownloadingUpdate = false;
            IsUpdateInfoBarOpen = true;
            UpdateInfoBarSeverity = InfoBarSeverity.Error;
            UpdateInfoBarTitle = "Cập nhật thất bại";
            UpdateInfoBarMessage = ex.Message;
        }
    }

    public void AttachDispatcher(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(TargetFolderPath) && Directory.Exists(TargetFolderPath))
        {
            await AnalyzeFolderAsync();
        }

        if (_config.SmartSettings.EnableAutoWatcher && !_watcher.IsRunning)
        {
            bool hasValidWatched = _config.WatchedFolders.Any(f => f.IsEnabled && Directory.Exists(f.FolderPath));
            if (hasValidWatched)
            {
                _watcher.StartConfiguredFolders(_config);
            }
            else
            {
                _config.SmartSettings.EnableAutoWatcher = false;
                SaveConfigurationOnly();
            }
        }

        RefreshWatchedFolderItemsDisplay();
        UpdateWatcherDisplayProperties();
        UpdateDashboardFolderRuleState();

        if (AutoCheckUpdatesOnStartup)
        {
            _ = CheckForUpdatesAsync(silent: true);
        }
    }

    private void SyncFieldsFromConfig()
    {
        _isInitializing = true;
        try
        {
            TargetFolderPath = _config.LastTargetFolder;
            GitHubRepository = string.IsNullOrWhiteSpace(_config.GitHubRepository) || _config.GitHubRepository.Equals("trong/Sofol", StringComparison.OrdinalIgnoreCase)
                ? "lichbich/sofol"
                : _config.GitHubRepository;
            AutoCheckUpdatesOnStartup = _config.AutoCheckUpdatesOnStartup;

            SmartGroupingSettings s = _config.SmartSettings;
            EnableDateSubfolders = s.EnableDateSubfolders;
            EnableLargeFileIsolation = s.EnableLargeFileIsolation;
            LargeFileThresholdMB = s.LargeFileThresholdMB;
            MoveUnmatchedToOthers = s.MoveUnmatchedToOthers;
            OthersFolderName = s.OthersFolderName;
            SelectedConflictIndex = (int)s.ConflictBehavior;
            AutoWatcherDelayValue = Math.Max(1, s.AutoWatcherDelayValue);
            SelectedWatcherDelayUnitIndex = (int)s.AutoWatcherDelayUnit;
            SelectedThemeIndex = (int)s.AppTheme;
            RunAtWindowsStartup = InstallationHelper.IsRunAtStartupEnabled();
            ShowWindowOnWindowsStartup = s.ShowWindowOnWindowsStartup;

            RebuildWatchedFolderItems();
            RebuildFolderRuleTabs();
            SyncRuleEditorFieldsFromActiveTabCore();
        }
        finally
        {
            _isInitializing = false;
        }

        UpdateTargetFolderDisplayName(TargetFolderPath);
        UpdateDashboardFolderRuleState();
        UpdateWatcherDisplayProperties();
    }

    private void RebuildWatchedFolderItems()
    {
        WatchedFolderItems.Clear();
        foreach (WatchedFolderProfile profile in _config.WatchedFolders)
        {
            var vm = new WatchedFolderItemViewModel(profile, HandleWatchedFolderItemChanged);
            vm.SyncFromModel(_config, _watcher.IsRunning, _watcher.GetPendingCount(profile.FolderPath));
            WatchedFolderItems.Add(vm);
        }

        HasNoWatchedFolders = WatchedFolderItems.Count == 0;
    }

    private void RefreshWatchedFolderItemsDisplay()
    {
        foreach (WatchedFolderItemViewModel vm in WatchedFolderItems)
        {
            vm.SyncFromModel(_config, _watcher.IsRunning, _watcher.GetPendingCount(vm.FolderPath));
        }

        RefreshFolderRuleTabBadges();
        HasNoWatchedFolders = WatchedFolderItems.Count == 0;
        UpdateWatcherDisplayProperties();
        UpdateDashboardFolderRuleState();
    }

    private void RebuildFolderRuleTabs()
    {
        FolderRuleTabs.Clear();

        if (_config.WatchedFolders.Count == 0)
        {
            _selectedScopeFolderPath = null;
            FolderRuleTabs.Add(new FolderRuleTabViewModel
            {
                FolderPath = null,
                DisplayName = "Mặc định",
                IconGlyph = "\uE8B7",
                ActiveRuleCountText = _config.Rules.Count(r => r.IsEnabled).ToString(),
                IsSelected = true
            });
            return;
        }

        bool foundSelected = !string.IsNullOrWhiteSpace(_selectedScopeFolderPath) &&
                             _config.WatchedFolders.Any(f => IsSameFolder(f.FolderPath, _selectedScopeFolderPath));
        if (!foundSelected)
        {
            _selectedScopeFolderPath = _config.WatchedFolders[0].FolderPath;
        }

        foreach (WatchedFolderProfile profile in _config.WatchedFolders)
        {
            int activeCount = (profile.CustomRules.Count > 0 ? profile.CustomRules : _config.Rules).Count(r => r.IsEnabled);
            FolderRuleTabs.Add(new FolderRuleTabViewModel
            {
                FolderPath = profile.FolderPath,
                DisplayName = profile.GetDisplayName(),
                IconGlyph = profile.GetIconGlyph(),
                ActiveRuleCountText = activeCount.ToString(),
                IsSelected = IsSameFolder(profile.FolderPath, _selectedScopeFolderPath)
            });
        }
    }

    private void RefreshFolderRuleTabBadges()
    {
        foreach (FolderRuleTabViewModel tab in FolderRuleTabs)
        {
            if (string.IsNullOrWhiteSpace(tab.FolderPath))
            {
                tab.ActiveRuleCountText = _config.Rules.Count(r => r.IsEnabled).ToString();
            }
            else
            {
                WatchedFolderProfile? profile = _config.FindWatchedFolder(tab.FolderPath);
                if (profile is not null)
                {
                    int activeCount = (profile.CustomRules.Count > 0 ? profile.CustomRules : _config.Rules).Count(r => r.IsEnabled);
                    tab.ActiveRuleCountText = activeCount.ToString();
                }
            }
        }
    }

    public void SelectRuleTab(FolderRuleTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        _selectedScopeFolderPath = tab.FolderPath;
        foreach (FolderRuleTabViewModel item in FolderRuleTabs)
        {
            item.IsSelected = ReferenceEquals(item, tab);
        }

        SyncRuleEditorFieldsFromActiveTab();
    }

    private WatchedFolderProfile? GetSelectedScopeProfile() =>
        string.IsNullOrWhiteSpace(_selectedScopeFolderPath)
            ? null
            : _config.FindWatchedFolder(_selectedScopeFolderPath);

    private SmartGroupingSettings GetEditableRuleSmartSettings()
    {
        WatchedFolderProfile? profile = GetSelectedScopeProfile();
        if (profile is not null)
        {
            profile.EnsureStandaloneInitialized(_config.SmartSettings, _config.Rules);
            return profile.CustomSmartSettings!;
        }

        return _config.SmartSettings;
    }

    private List<OrganizationRule> GetEditableRulesList()
    {
        WatchedFolderProfile? profile = GetSelectedScopeProfile();
        if (profile is not null)
        {
            profile.EnsureStandaloneInitialized(_config.SmartSettings, _config.Rules);
            return profile.CustomRules;
        }

        return _config.Rules;
    }

    private void SyncRuleEditorFieldsFromActiveTab()
    {
        bool prevInit = _isInitializing;
        _isInitializing = true;
        try
        {
            SyncRuleEditorFieldsFromActiveTabCore();
        }
        finally
        {
            _isInitializing = prevInit;
        }
    }

    private void SyncRuleEditorFieldsFromActiveTabCore()
    {
        WatchedFolderProfile? profile = GetSelectedScopeProfile();
        if (profile is null)
        {
            SelectedRuleTabFolderName = "Mặc định";
            SelectedRuleTabFolderPath = "Chưa có thư mục theo dõi nào — hãy bấm '+ Thêm thư mục...' để tạo riêng.";
            ApplySmartSettingsToRuleControls(_config.SmartSettings);
            ReloadRulesCollection(_config.Rules);
            return;
        }

        profile.EnsureStandaloneInitialized(_config.SmartSettings, _config.Rules);
        SelectedRuleTabFolderName = profile.GetDisplayName();
        SelectedRuleTabFolderPath = profile.FolderPath;
        ApplySmartSettingsToRuleControls(profile.CustomSmartSettings!);
        ReloadRulesCollection(profile.CustomRules);
    }

    private void ApplySmartSettingsToRuleControls(SmartGroupingSettings s)
    {
        EnablePrefixSimilarityGrouping = s.EnablePrefixSimilarityGrouping;
        EnableExtensionRules = s.EnableExtensionRules;
        EnableKeywordRules = s.EnableKeywordRules;
        EnableFolderMonthGrouping = s.EnableDateSubfolders || s.EnableDirectDateGrouping;
        MinFilesForPrefixGroup = s.MinFilesForPrefixGroup;
        SimilarityThresholdPercent = Math.Round(s.SimilarityThreshold * 100.0);
        GroupPrefixInsideCategory = s.GroupPrefixInsideCategory;
        UseDedicatedSmartRootFolder = s.PrefixGroupLocation == PrefixGroupLocationMode.DedicatedRootSubfolder;
        PrefixGroupRootFolderName = s.PrefixGroupRootFolderName;
    }

    private void ReloadRulesCollection(IEnumerable<OrganizationRule>? sourceRules = null)
    {
        IEnumerable<OrganizationRule> list = sourceRules ?? GetEditableRulesList();
        Rules.Clear();
        foreach (OrganizationRule rule in list.OrderBy(r => r.Priority))
        {
            Rules.Add(rule);
        }
    }

    private void ReloadHistoryCollection()
    {
        HistorySessions.Clear();
        foreach (OrganizationSession session in _store.LoadHistory())
        {
            HistorySessions.Add(session);
        }

        HasNoHistory = HistorySessions.Count == 0;
        OrganizationSession? latestUndoable = HistorySessions.FirstOrDefault(s => !s.IsUndone && s.MovedFileCount > 0);
        CanUndoLatestSession = latestUndoable is not null;
        LatestUndoSummary = latestUndoable is null
            ? "Chưa có phiên nào cần hoàn tác"
            : $"Hoàn tác phiên gần nhất ({latestUndoable.MovedFileCount} tệp lúc {latestUndoable.ExecutedAt.LocalDateTime:HH:mm})";
    }

    private void UpdateDashboardFolderRuleState()
    {
        bool hasValidTarget = !string.IsNullOrWhiteSpace(TargetFolderPath) && Directory.Exists(TargetFolderPath);
        WatchedFolderProfile? profile = _config.FindWatchedFolder(TargetFolderPath);

        IsTargetFolderWatched = profile is not null;
        CanPinTargetFolderToWatcher = hasValidTarget && profile is null;

        int activeCount = (profile is not null && profile.CustomRules.Count > 0 ? profile.CustomRules : _config.Rules)
            .Count(r => r.IsEnabled);
        TargetFolderRuleButtonText = $"Quy tắc ({activeCount})";
    }

    public async Task SetTargetFolderAndAnalyzeAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            ShowInfoBar("Thư mục không hợp lệ", "Đường dẫn bạn chọn không tồn tại trên hệ thống.", InfoBarSeverity.Warning);
            return;
        }

        TargetFolderPath = folderPath;
        _config.LastTargetFolder = folderPath;
        SaveConfigurationOnly();
        await AnalyzeFolderAsync();
        UpdateWatcherDisplayProperties();
    }

    [RelayCommand]
    public async Task AnalyzeFolderAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetFolderPath) || !Directory.Exists(TargetFolderPath))
        {
            AllPreviewItems.Clear();
            FilteredPreviewItems.Clear();
            HasNoPreviewItems = true;
            TotalFilesScanned = 0;
            ExistingSubfoldersCount = 0;
            SmartGroupsCount = 0;
            DistinctDestinationFoldersCount = 0;
            SkippedOrLockedCount = 0;
            SelectedReadyCount = 0;
            FormattedTotalReadySize = "0 B";
            UpdateDashboardFolderRuleState();
            return;
        }

        IsBusy = true;
        ProgressText = "Đang quét và phân tích cấu trúc tệp...";
        ProgressValue = 0;

        try
        {
            _config.LastTargetFolder = TargetFolderPath;
            SaveConfigurationOnly();
            UpdateDashboardFolderRuleState();

            AppConfiguration effectiveConfig = _config.BuildEffectiveConfigurationForFolder(TargetFolderPath);
            FolderAnalysisResult result = await _engine.AnalyzeFolderAsync(TargetFolderPath, effectiveConfig);

            AllPreviewItems.Clear();
            foreach (FileMovePlanItem item in result.Items)
            {
                AllPreviewItems.Add(new PlanItemViewModel(item, RecalculateSelectionStats));
            }

            TotalFilesScanned = result.TotalFilesScanned;
            ExistingSubfoldersCount = result.ExistingSubfoldersCount;
            SmartGroupsCount = result.SmartGroupsCount;
            SkippedOrLockedCount = result.SkippedOrLockedCount;

            ApplyPreviewFilter();
            RecalculateSelectionStats();
        }
        catch (Exception ex)
        {
            ShowInfoBar("Không thể phân tích thư mục", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
        }
    }

    private void RecalculateSelectionStats()
    {
        var readySelected = AllPreviewItems
            .Where(vm => vm.CanSelect && vm.IsSelected)
            .ToList();

        SelectedReadyCount = readySelected.Count;
        DistinctDestinationFoldersCount = readySelected
            .Select(vm => vm.ProposedTargetFolder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        FormattedTotalReadySize = FileMovePlanItem.FormatByteSize(readySelected.Sum(vm => vm.Model.FileSizeBytes));
    }

    private void ApplyPreviewFilter()
    {
        FilteredPreviewItems.Clear();
        string query = SearchQuery?.Trim() ?? string.Empty;
        var mode = (PreviewFilterCategory)Math.Clamp(SelectedFilterIndex, 0, 3);

        foreach (PlanItemViewModel item in AllPreviewItems)
        {
            bool matchesMode = mode switch
            {
                PreviewFilterCategory.SmartGroups => item.RuleBadgeType == RuleBadgeType.SmartNameGroup,
                PreviewFilterCategory.Ready => item.Status == PlanItemStatus.Ready,
                PreviewFilterCategory.LockedOrSkipped => item.Status is PlanItemStatus.LockedOrDownloading or PlanItemStatus.Skipped,
                _ => true
            };

            if (!matchesMode)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(query))
            {
                bool matchText =
                    item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.ProposedTargetFolder.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.MatchedRuleName.Contains(query, StringComparison.OrdinalIgnoreCase);

                if (!matchText)
                {
                    continue;
                }
            }

            FilteredPreviewItems.Add(item);
        }

        HasNoPreviewItems = FilteredPreviewItems.Count == 0;
    }

    [RelayCommand]
    public void SelectAllReady()
    {
        foreach (PlanItemViewModel vm in FilteredPreviewItems)
        {
            if (vm.CanSelect)
            {
                vm.IsSelected = true;
            }
        }
        RecalculateSelectionStats();
    }

    [RelayCommand]
    public void UnselectAll()
    {
        foreach (PlanItemViewModel vm in FilteredPreviewItems)
        {
            if (vm.CanSelect)
            {
                vm.IsSelected = false;
            }
        }
        RecalculateSelectionStats();
    }

    [RelayCommand]
    public async Task OrganizeNowAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(TargetFolderPath) || !Directory.Exists(TargetFolderPath))
        {
            return;
        }

        List<FileMovePlanItem> itemsToProcess = AllPreviewItems
            .Select(vm => vm.Model)
            .Where(m => m.IsSelected && m.Status == PlanItemStatus.Ready)
            .ToList();

        if (itemsToProcess.Count == 0)
        {
            ShowInfoBar("Chưa có tệp nào được chọn", "Hãy tích chọn ít nhất 1 tệp sẵn sàng để sắp xếp.", InfoBarSeverity.Warning);
            return;
        }

        IsBusy = true;
        ProgressValue = 0;

        var progress = new Progress<OrganizationProgress>(p =>
        {
            ProgressValue = p.Percentage;
            ProgressText = $"Đang di chuyển ({p.ProcessedCount}/{p.TotalCount}): {p.CurrentFileName}";
        });

        try
        {
            AppConfiguration effectiveConfig = _config.BuildEffectiveConfigurationForFolder(TargetFolderPath);
            ExecutionResult exec = await _engine.ExecutePlanAsync(
                TargetFolderPath,
                itemsToProcess,
                effectiveConfig.SmartSettings.ConflictBehavior,
                progress);

            foreach (PlanItemViewModel vm in AllPreviewItems)
            {
                vm.SyncFromModel();
            }

            if (exec.Session is not null)
            {
                _store.AddSession(exec.Session);
                ReloadHistoryCollection();
            }

            string message = $"Đã sắp xếp gọn gàng {exec.MovedCount} tệp vào {DistinctDestinationFoldersCount} thư mục.";
            if (exec.SkippedCount > 0 || exec.FailedCount > 0)
            {
                message += $" (Bỏ qua: {exec.SkippedCount}, Lỗi: {exec.FailedCount})";
            }

            ShowInfoBar(
                "Sắp xếp hoàn tất!",
                message,
                exec.FailedCount > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);

            IsBusy = false;
            await AnalyzeFolderAsync();
        }
        catch (Exception ex)
        {
            ShowInfoBar("Lỗi trong quá trình sắp xếp", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            ProgressText = string.Empty;
        }
    }

    [RelayCommand]
    public async Task UndoLatestSessionAsync()
    {
        OrganizationSession? latest = HistorySessions.FirstOrDefault(s => !s.IsUndone && s.MovedFileCount > 0);
        if (latest is not null)
        {
            await UndoSessionAsync(latest);
        }
    }

    public async Task UndoSessionAsync(OrganizationSession session)
    {
        if (IsBusy || session.IsUndone)
        {
            return;
        }

        IsBusy = true;
        ProgressText = "Đang hoàn tác các tệp về vị trí ban đầu...";

        try
        {
            UndoResult undo = await _engine.UndoSessionAsync(session);
            _store.UpdateSession(session);
            ReloadHistoryCollection();

            if (undo.Success)
            {
                ShowInfoBar(
                    "Hoàn tác thành công",
                    $"Đã khôi phục {undo.RestoredCount} tệp về vị trí ban đầu và dọn các thư mục rỗng.",
                    InfoBarSeverity.Success);
            }
            else
            {
                ShowInfoBar(
                    "Hoàn tác hoàn tất với lưu ý",
                    $"Khôi phục {undo.RestoredCount} tệp, không tìm thấy/lỗi {undo.FailedOrMissingCount} tệp.",
                    InfoBarSeverity.Warning);
            }

            IsBusy = false;
            if (IsSameFolder(session.SourceFolderPath, TargetFolderPath))
            {
                await AnalyzeFolderAsync();
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar("Lỗi khi hoàn tác", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
        }
    }

    [RelayCommand]
    public async Task CreateDemoSandboxAsync()
    {
        string demoPath = DemoSandboxGenerator.CreateSampleMessyFolder();
        ShowInfoBar(
            "Đã tạo thư mục Test mẫu",
            $"Đã sinh 21 tệp mô phỏng thư mục Downloads thực tế tại: {demoPath}",
            InfoBarSeverity.Informational);
        await SetTargetFolderAndAnalyzeAsync(demoPath);
    }

    public async Task SelectSpecialFolderAsync(string specialKey)
    {
        string? path = ResolveSpecialFolderPath(specialKey);
        if (!string.IsNullOrWhiteSpace(path))
        {
            await SetTargetFolderAndAnalyzeAsync(path);
        }
    }

    public async Task SelectSpecialFolderAsync(Environment.SpecialFolder specialFolder)
    {
        string path = specialFolder == Environment.SpecialFolder.UserProfile
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : Environment.GetFolderPath(specialFolder);

        await SetTargetFolderAndAnalyzeAsync(path);
    }

    public static string? ResolveSpecialFolderPath(string key) => key switch
    {
        "Downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "Pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        _ => null
    };

    public void OpenTargetFolderInExplorer()
    {
        if (!string.IsNullOrWhiteSpace(TargetFolderPath) && Directory.Exists(TargetFolderPath))
        {
            OpenFolderInExplorer(TargetFolderPath);
        }
    }

    public static void OpenFolderInExplorer(string folderPath)
    {
        if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
    }

    public void OpenStorageDirectoryInExplorer()
    {
        Directory.CreateDirectory(_store.StorageDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _store.StorageDirectory,
            UseShellExecute = true
        });
    }

    // Quản lý nhiều thư mục tự động sắp xếp
    [RelayCommand]
    public Task PinTargetFolderToWatcherAsync() => AddWatchedFolderAsync(TargetFolderPath, selectInRulesTab: false);

    [RelayCommand]
    public void OpenTargetFolderRules() => NavigateToFolderRules(TargetFolderPath);

    public Task AddSpecialWatchedFolderAsync(string key)
    {
        string? path = ResolveSpecialFolderPath(key);
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            ShowInfoBar("Không tìm thấy thư mục", $"Thư mục hệ thống \"{key}\" không tồn tại trên máy.", InfoBarSeverity.Warning);
            return Task.CompletedTask;
        }

        return AddWatchedFolderAsync(path, selectInRulesTab: false);
    }

    public Task AddWatchedFolderAsync(string folderPath, bool selectInRulesTab = false)
    {
        string normalized = WatchedFolderProfile.NormalizePath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(normalized))
        {
            ShowInfoBar("Thư mục không hợp lệ", "Đường dẫn bạn chọn không tồn tại trên hệ thống.", InfoBarSeverity.Warning);
            return Task.CompletedTask;
        }

        if (WatchedFolderProfile.IsRootOrSystemDirectory(normalized))
        {
            ShowInfoBar(
                "Không thể theo dõi thư mục hệ thống",
                "Vì an toàn hệ thống, vui lòng không thêm thư mục gốc ổ đĩa hoặc thư mục Windows.",
                InfoBarSeverity.Warning);
            return Task.CompletedTask;
        }

        WatchedFolderProfile? existing = _config.FindWatchedFolder(normalized);
        if (existing is not null)
        {
            if (selectInRulesTab)
            {
                _selectedScopeFolderPath = existing.FolderPath;
                RebuildFolderRuleTabs();
                SyncRuleEditorFieldsFromActiveTab();
            }

            if (!existing.IsEnabled)
            {
                existing.IsEnabled = true;
                SaveConfigurationOnly();
                RefreshWatchedFolderItemsDisplay();
                ShowInfoBar("Đã bật lại thư mục", $"Thư mục \"{existing.GetDisplayName()}\" đã có trong danh sách và được bật lại.", InfoBarSeverity.Informational);
            }
            else
            {
                ShowInfoBar("Thư mục đã có trong danh sách", $"\"{existing.GetDisplayName()}\" đang nằm trong danh sách theo dõi.", InfoBarSeverity.Informational);
            }
            return Task.CompletedTask;
        }

        WatchedFolderProfile profile = WatchedFolderProfile.CreateForFolder(
            normalized,
            _config.SmartSettings,
            _config.Rules);

        _config.WatchedFolders.Add(profile);
        if (selectInRulesTab || _config.WatchedFolders.Count == 1)
        {
            _selectedScopeFolderPath = profile.FolderPath;
        }

        SaveConfigurationOnly();

        RebuildWatchedFolderItems();
        RebuildFolderRuleTabs();
        SyncRuleEditorFieldsFromActiveTab();
        UpdateWatcherDisplayProperties();
        UpdateDashboardFolderRuleState();

        if (WatchedFolderProfile.IsPicturesFolder(normalized) || WatchedFolderProfile.IsVideosFolder(normalized))
        {
            ShowInfoBar(
                $"Đã thêm \"{profile.GetDisplayName()}\"",
                "Đã tạo sẵn bộ quy tắc riêng: bật Gom theo Năm-Tháng và tắt nhóm tự trùng tên thư mục (Hình ảnh/Video). Bạn có thể xem và chỉnh trong tab Thiết lập Quy tắc.",
                InfoBarSeverity.Success);
        }
        else
        {
            ShowInfoBar(
                $"Đã thêm \"{profile.GetDisplayName()}\"",
                "Đã tạo sẵn tab quy tắc riêng cho thư mục này.",
                InfoBarSeverity.Success);
        }

        return Task.CompletedTask;
    }

    public void RemoveWatchedFolder(WatchedFolderItemViewModel item)
    {
        _config.WatchedFolders.RemoveAll(f => IsSameFolder(f.FolderPath, item.FolderPath));
        if (IsSameFolder(_selectedScopeFolderPath, item.FolderPath))
        {
            _selectedScopeFolderPath = _config.WatchedFolders.FirstOrDefault()?.FolderPath;
        }

        SaveConfigurationOnly();
        RebuildWatchedFolderItems();
        RebuildFolderRuleTabs();
        SyncRuleEditorFieldsFromActiveTab();
        UpdateWatcherDisplayProperties();
        UpdateDashboardFolderRuleState();
    }

    private void HandleWatchedFolderItemChanged(WatchedFolderItemViewModel item)
    {
        SaveConfigurationOnly();
        RefreshWatchedFolderItemsDisplay();
        UpdateDashboardFolderRuleState();

        if (IsSameFolder(TargetFolderPath, item.FolderPath))
        {
            _ = AnalyzeFolderAsync();
        }
    }

    public Task PreviewWatchedFolderAsync(WatchedFolderItemViewModel item) =>
        OpenFolderPreviewAsync(item.FolderPath);

    public async Task OpenFolderPreviewAsync(string folderPath)
    {
        string normalized = WatchedFolderProfile.NormalizePath(folderPath);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            _selectedScopeFolderPath = normalized;
            RebuildFolderRuleTabs();
            SyncRuleEditorFieldsFromActiveTab();
        }

        await SetTargetFolderAndAnalyzeAsync(folderPath);
        App.NavigateMainWindow("dashboard");
    }

    [RelayCommand]
    public async Task OpenCurrentRuleFolderPreviewAsync()
    {
        string? folderPath = !string.IsNullOrWhiteSpace(_selectedScopeFolderPath)
            ? _selectedScopeFolderPath
            : TargetFolderPath;

        if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
        {
            await SetTargetFolderAndAnalyzeAsync(folderPath);
            App.NavigateMainWindow("dashboard");
        }
    }

    [RelayCommand]
    public void NavigateBackToWorkspace()
    {
        App.NavigateMainWindow("watcher");
    }

    public async Task RestoreFolderContextFromHistoryAsync(string folderPath, bool isRulesPage)
    {
        string normalized = WatchedFolderProfile.NormalizePath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _selectedScopeFolderPath = normalized;
        TargetFolderPath = normalized;
        _config.LastTargetFolder = normalized;
        SaveConfigurationOnly();
        RebuildFolderRuleTabs();
        SyncRuleEditorFieldsFromActiveTab();
        UpdateDashboardFolderRuleState();

        if (!isRulesPage && Directory.Exists(normalized))
        {
            await AnalyzeFolderAsync();
        }
    }

    [RelayCommand]
    public async Task CreateDemoSandboxInWorkspaceAsync()
    {
        string demoPath = DemoSandboxGenerator.CreateSampleMessyFolder();
        await AddWatchedFolderAsync(demoPath, selectInRulesTab: false);
        ShowInfoBar(
            "Đã tạo thư mục Test mẫu (DemoSandbox)",
            "Đã sinh 21 tệp mô phỏng và thêm vào Không gian dọn dẹp. Hãy bấm trực tiếp vào DemoSandbox để xem trước!",
            InfoBarSeverity.Success);
    }

    public async Task RunSingleWatchedFolderNowAsync(WatchedFolderItemViewModel item)
    {
        if (IsBusy)
        {
            return;
        }

        if (!Directory.Exists(item.FolderPath))
        {
            ShowInfoBar("Thư mục không tồn tại", $"Không tìm thấy thư mục: {item.FolderPath}", InfoBarSeverity.Warning);
            return;
        }

        int movedCount = await _watcher.ProcessFolderImmediatelyAsync(item.FolderPath, _config);
        RefreshWatchedFolderItemsDisplay();

        if (movedCount > 0)
        {
            ShowInfoBar(
                $"Đã dọn \"{item.DisplayName}\"",
                $"Đã sắp xếp gọn {movedCount} tệp trong {item.DisplayName}.",
                InfoBarSeverity.Success);
        }
        else
        {
            ShowInfoBar(
                $"\"{item.DisplayName}\" đã gọn gàng",
                "Không có tệp sẵn sàng nào cần sắp xếp thêm lúc này.",
                InfoBarSeverity.Informational);
        }
    }

    public void NavigateToFolderRules(string? folderPath)
    {
        string normalized = WatchedFolderProfile.NormalizePath(folderPath);
        if (!string.IsNullOrWhiteSpace(normalized) && Directory.Exists(normalized))
        {
            if (_config.FindWatchedFolder(normalized) is null && !WatchedFolderProfile.IsRootOrSystemDirectory(normalized))
            {
                _config.WatchedFolders.Add(WatchedFolderProfile.CreateForFolder(
                    normalized,
                    _config.SmartSettings,
                    _config.Rules));
                SaveConfigurationOnly();
                RebuildWatchedFolderItems();
                UpdateWatcherDisplayProperties();
                UpdateDashboardFolderRuleState();
            }

            _selectedScopeFolderPath = normalized;
            TargetFolderPath = normalized;
            _config.LastTargetFolder = normalized;
            SaveConfigurationOnly();
            RebuildFolderRuleTabs();
            SyncRuleEditorFieldsFromActiveTab();
            UpdateDashboardFolderRuleState();
        }

        App.NavigateMainWindow("rules");
    }

    // Quản lý danh sách Rules trong tab thư mục đang chọn
    public void SaveRuleChanges(OrganizationRule rule, bool isNew, bool applyToAllFolders = false)
    {
        List<OrganizationRule> targetRules = GetEditableRulesList();
        if (isNew)
        {
            targetRules.Add(rule);
            if (applyToAllFolders)
            {
                foreach (WatchedFolderProfile profile in _config.WatchedFolders)
                {
                    profile.EnsureStandaloneInitialized(_config.SmartSettings, _config.Rules);
                    if (!ReferenceEquals(profile.CustomRules, targetRules))
                    {
                        profile.CustomRules.Add(rule.Clone());
                    }
                }
                if (!ReferenceEquals(_config.Rules, targetRules))
                {
                    _config.Rules.Add(rule.Clone());
                }
            }
        }
        else
        {
            int idx = targetRules.FindIndex(r => r.Id == rule.Id);
            if (idx >= 0)
            {
                targetRules[idx] = rule;
            }
        }

        SaveConfigurationOnly();
        ReloadRulesCollection(targetRules);
        RefreshWatchedFolderItemsDisplay();
        _ = AnalyzeFolderAsync();
    }

    public void SaveQuickRuleToScope(OrganizationRule rule, string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            _config.Rules.Add(rule.Clone());
            foreach (WatchedFolderProfile profile in _config.WatchedFolders)
            {
                profile.EnsureStandaloneInitialized(_config.SmartSettings, _config.Rules);
                profile.CustomRules.Add(rule.Clone());
            }
        }
        else
        {
            WatchedFolderProfile? profile = _config.FindWatchedFolder(folderPath);
            if (profile is null)
            {
                _config.Rules.Add(rule);
            }
            else
            {
                profile.EnsureStandaloneInitialized(_config.SmartSettings, _config.Rules);
                profile.CustomRules.Add(rule);
            }
        }

        SaveConfigurationOnly();
        RefreshWatchedFolderItemsDisplay();
        SyncRuleEditorFieldsFromActiveTab();
        _ = AnalyzeFolderAsync();
    }

    public void ToggleRuleEnabled(OrganizationRule rule, bool isEnabled)
    {
        rule.IsEnabled = isEnabled;
        SaveConfigurationOnly();
        RefreshWatchedFolderItemsDisplay();
        _ = AnalyzeFolderAsync();
    }

    public void SetAllRulesEnabled(bool isEnabled)
    {
        List<OrganizationRule> targetRules = GetEditableRulesList();
        foreach (OrganizationRule rule in targetRules)
        {
            rule.IsEnabled = isEnabled;
        }

        SaveConfigurationOnly();
        ReloadRulesCollection(targetRules);
        RefreshWatchedFolderItemsDisplay();
        _ = AnalyzeFolderAsync();
    }

    public void DeleteRule(OrganizationRule rule)
    {
        List<OrganizationRule> targetRules = GetEditableRulesList();
        targetRules.RemoveAll(r => r.Id == rule.Id);
        SaveConfigurationOnly();
        ReloadRulesCollection(targetRules);
        RefreshWatchedFolderItemsDisplay();
        _ = AnalyzeFolderAsync();
    }

    [RelayCommand]
    public void RestoreDefaultRules()
    {
        WatchedFolderProfile? profile = GetSelectedScopeProfile();

        if (profile is not null)
        {
            WatchedFolderProfile fresh = WatchedFolderProfile.CreateForFolder(profile.FolderPath);
            profile.CustomSmartSettings = fresh.CustomSmartSettings;
            profile.CustomRules = fresh.CustomRules;
            SaveConfigurationOnly();
            SyncRuleEditorFieldsFromActiveTab();
            RefreshWatchedFolderItemsDisplay();
            ShowInfoBar(
                $"Đã đặt lại quy tắc cho \"{profile.GetDisplayName()}\"",
                "Các quy tắc và tùy chọn gom nhóm của thư mục này đã được khôi phục về mặc định phù hợp.",
                InfoBarSeverity.Informational);
        }
        else
        {
            _config.Rules = ConfigAndHistoryStore.CreateDefaultRules();
            SaveConfigurationOnly();
            ReloadRulesCollection(_config.Rules);
            RefreshWatchedFolderItemsDisplay();
            ShowInfoBar("Đã khôi phục quy tắc mặc định", "Bộ quy tắc chuẩn Windows 11 đã được thiết lập lại.", InfoBarSeverity.Informational);
        }

        _ = AnalyzeFolderAsync();
    }

    [RelayCommand]
    public void ToggleWatcher()
    {
        SetWatcherEnabled(!_watcher.IsRunning);
    }

    public void SetWatcherEnabled(bool enable)
    {
        if (!enable)
        {
            _watcher.Stop();
            _config.SmartSettings.EnableAutoWatcher = false;
            SaveConfigurationOnly();
            RefreshWatchedFolderItemsDisplay();
            return;
        }

        if (_watcher.IsRunning)
        {
            RefreshWatchedFolderItemsDisplay();
            return;
        }

        var enabledFolders = _config.WatchedFolders
            .Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.FolderPath) && Directory.Exists(f.FolderPath))
            .ToList();

        if (enabledFolders.Count == 0)
        {
            ShowInfoBar(
                "Chưa có thư mục theo dõi đang bật",
                "Hãy thêm ít nhất 1 thư mục hợp lệ và bật công tắc của thư mục đó trước khi khởi động.",
                InfoBarSeverity.Warning);
            RefreshWatchedFolderItemsDisplay();
            return;
        }

        _config.SmartSettings.EnableAutoWatcher = true;
        SaveConfigurationOnly();
        _watcher.StartConfiguredFolders(_config);
        RefreshWatchedFolderItemsDisplay();
    }

    [RelayCommand]
    public Task RunWatcherNowAsync() => RunQuickOrganizeAsync();

    public async Task<string> RunQuickOrganizeAsync()
    {
        if (IsBusy)
        {
            return "Đang bận xử lý tác vụ khác, vui lòng chờ giây lát.";
        }

        if (_watcher.IsRunning)
        {
            int movedCount = await _watcher.ProcessImmediatelyAsync();
            RefreshWatchedFolderItemsDisplay();
            if (movedCount > 0)
            {
                string msg = $"Đã tự động sắp xếp gọn {movedCount} tệp trong {_watcher.ActiveFolderCount} thư mục đang theo dõi.";
                ShowInfoBar("Đã dọn dẹp ngay tức thì", msg, InfoBarSeverity.Success);
                return msg;
            }

            const string noneMsg = "Không có tệp sẵn sàng nào cần dọn thêm lúc này.";
            ShowInfoBar("Không có tệp cần dọn", noneMsg, InfoBarSeverity.Informational);
            return noneMsg;
        }

        var enabledFolders = _config.WatchedFolders
            .Where(f => f.IsEnabled && Directory.Exists(f.FolderPath))
            .ToList();

        if (enabledFolders.Count > 0)
        {
            int totalMoved = 0;
            foreach (WatchedFolderProfile folder in enabledFolders)
            {
                totalMoved += await _watcher.ProcessFolderImmediatelyAsync(folder.FolderPath, _config);
            }

            RefreshWatchedFolderItemsDisplay();
            if (totalMoved > 0)
            {
                string msg = $"Đã sắp xếp gọn {totalMoved} tệp trên {enabledFolders.Count} thư mục.";
                ShowInfoBar("Đã dọn dẹp xong!", msg, InfoBarSeverity.Success);
                return msg;
            }

            const string cleanMsg = "Các thư mục theo dõi đều đã gọn gàng.";
            ShowInfoBar("Không có tệp cần dọn", cleanMsg, InfoBarSeverity.Informational);
            return cleanMsg;
        }

        if (string.IsNullOrWhiteSpace(TargetFolderPath) || !Directory.Exists(TargetFolderPath))
        {
            const string missingMsg = "Vui lòng chọn hoặc thêm thư mục cần dọn dẹp trước.";
            ShowInfoBar("Chưa chọn thư mục hợp lệ", missingMsg, InfoBarSeverity.Warning);
            return missingMsg;
        }

        await AnalyzeFolderAsync();
        int readyCount = AllPreviewItems.Count(i => i.CanSelect && i.IsSelected);
        if (readyCount > 0)
        {
            await OrganizeNowAsync();
            return string.IsNullOrWhiteSpace(InfoBarMessage)
                ? $"Đã hoàn tất sắp xếp {readyCount} tệp."
                : InfoBarMessage;
        }

        const string noMatchMsg = "Tất cả tệp phù hợp đã gọn gàng hoặc chưa khớp quy tắc đang bật.";
        ShowInfoBar("Không có tệp cần dọn", noMatchMsg, InfoBarSeverity.Informational);
        return noMatchMsg;
    }

    [RelayCommand]
    public void ClearWatcherLogs()
    {
        WatcherLogs.Clear();
        HasNoWatcherLogs = true;
    }

    private void SaveConfigurationOnly()
    {
        _store.SaveConfiguration(_config);
        _watcher.UpdateConfiguration(_config);
    }

    private async void SaveConfigAndReanalyze()
    {
        SaveConfigurationOnly();
        await AnalyzeFolderAsync();
    }

    public void ShowInfoBar(string title, string message, InfoBarSeverity severity)
    {
        InfoBarTitle = title;
        InfoBarMessage = message;
        InfoBarSeverity = severity;
        IsInfoBarOpen = true;
    }

    private void OnWatcherLogAdded(string logLine)
    {
        RunOnUi(() =>
        {
            WatcherLogs.Insert(0, logLine);
            while (WatcherLogs.Count > 200)
            {
                WatcherLogs.RemoveAt(WatcherLogs.Count - 1);
            }
            HasNoWatcherLogs = WatcherLogs.Count == 0;
        });
    }

    private void OnWatcherSessionCompleted(OrganizationSession session)
    {
        RunOnUi(async () =>
        {
            ReloadHistoryCollection();
            RefreshWatchedFolderItemsDisplay();
            if (IsSameFolder(session.SourceFolderPath, TargetFolderPath))
            {
                await AnalyzeFolderAsync();
            }
        });
    }

    private void OnWatcherStateChanged(bool isRunning, string folderPath)
    {
        RunOnUi(() =>
        {
            IsWatcherRunning = isRunning;
            RefreshWatchedFolderItemsDisplay();
        });
    }

    private void UpdateWatcherDisplayProperties()
    {
        bool running = _watcher.IsRunning;
        IsWatcherRunning = running;

        int totalConfigured = _config.WatchedFolders.Count;
        var enabledProfiles = _config.WatchedFolders
            .Where(f => f.IsEnabled && Directory.Exists(f.FolderPath))
            .ToList();
        int activeCount = _watcher.ActiveFolderCount;

        if (running)
        {
            IReadOnlyList<string> activeFolders = _watcher.ActiveWatchedFolders;
            string namesPreview = FormatFolderNamesSummary(activeFolders.Select(p =>
                _config.FindWatchedFolder(p)?.GetDisplayName() ?? Path.GetFileName(p)));

            WatcherStatusTitle = $"Đang tự động sắp xếp {activeCount} thư mục";
            WatcherBadgeText = $"● ĐANG BẬT ({activeCount} THƯ MỤC)";
            WatcherCompactBadgeText = $"● Đang bật ({activeCount} thư mục)";
            WatcherCompactActionText = "Tạm dừng";
            WatcherActionButtonGlyph = "\uE71A";
            WatcherStatusText = $"Đang theo dõi: {namesPreview}.";
            WatcherCompactDelayText = $"Đang theo dõi: {namesPreview}";

            TrayFolderTitle = activeCount == 1
                ? (_config.FindWatchedFolder(activeFolders[0])?.GetDisplayName() ?? activeFolders[0])
                : $"Đang bảo vệ {activeCount} thư mục";
            TrayFolderSubtitle = namesPreview;
        }
        else
        {
            WatcherStatusTitle = "Dịch vụ Tự động sắp xếp đang tắt";
            WatcherBadgeText = "○ ĐANG TẠM DỪNG";
            WatcherCompactBadgeText = "○ Đang tạm dừng";
            WatcherCompactActionText = "Bật tự động";
            WatcherActionButtonGlyph = "\uE768";

            if (totalConfigured == 0)
            {
                WatcherStatusText = "Chưa có thư mục nào trong danh sách. Hãy bấm nút thêm nhanh bên dưới.";
                WatcherCompactDelayText = "Chưa có thư mục theo dõi";
                TrayFolderTitle = TargetFolderDisplayName;
                TrayFolderSubtitle = string.IsNullOrWhiteSpace(TargetFolderPath) ? "Chưa chọn thư mục" : TargetFolderPath;
            }
            else if (enabledProfiles.Count == 0)
            {
                WatcherStatusText = $"Đang tạm dừng ({totalConfigured} thư mục trong danh sách nhưng đang tắt công tắc).";
                WatcherCompactDelayText = "Các thư mục theo dõi đang tắt";
                TrayFolderTitle = $"Có {totalConfigured} thư mục (đang tắt)";
                TrayFolderSubtitle = "Mở Sofol để bật ít nhất 1 thư mục";
            }
            else
            {
                string namesPreview = FormatFolderNamesSummary(enabledProfiles.Select(f => f.GetDisplayName()));
                WatcherStatusText = $"Đang tạm dừng ({enabledProfiles.Count} thư mục sẵn sàng: {namesPreview}).";
                WatcherCompactDelayText = $"Sẵn sàng theo dõi: {namesPreview}";
                TrayFolderTitle = enabledProfiles.Count == 1
                    ? enabledProfiles[0].GetDisplayName()
                    : $"Sẵn sàng theo dõi {enabledProfiles.Count} thư mục";
                TrayFolderSubtitle = namesPreview;
            }
        }
    }

    private static string FormatFolderNamesSummary(IEnumerable<string> names)
    {
        var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (list.Count == 0)
        {
            return "Chưa có";
        }
        if (list.Count <= 3)
        {
            return string.Join(", ", list);
        }
        return $"{string.Join(", ", list.Take(3))} (+{list.Count - 3})";
    }

    private void UpdateTargetFolderDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            TargetFolderDisplayName = "Chưa chọn thư mục";
            return;
        }

        string trimmed = value.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string folderName = Path.GetFileName(trimmed);
        TargetFolderDisplayName = string.IsNullOrWhiteSpace(folderName) ? value.Trim() : folderName;
    }

    private static bool IsSameFolder(string? a, string? b)
    {
        string na = WatchedFolderProfile.NormalizePath(a);
        string nb = WatchedFolderProfile.NormalizePath(b);
        return !string.IsNullOrWhiteSpace(na) &&
               string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }

    public void Shutdown()
    {
        _watcher.Dispose();
    }
}
