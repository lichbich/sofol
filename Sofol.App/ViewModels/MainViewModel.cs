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

    public MainViewModel()
    {
        _store = new ConfigAndHistoryStore();
        _engine = new FileOrganizerEngine();
        _watcher = new FolderWatcherService(_engine, _store);
        _config = _store.LoadConfiguration();

        _watcher.LogAdded += OnWatcherLogAdded;
        _watcher.SessionCompleted += OnWatcherSessionCompleted;
        _watcher.StateChanged += OnWatcherStateChanged;

        SyncFieldsFromConfig();
        ReloadHistoryCollection();
    }

    public string StorageDirectory => _store.StorageDirectory;

    public ObservableCollection<PlanItemViewModel> AllPreviewItems { get; } = [];

    public ObservableCollection<PlanItemViewModel> FilteredPreviewItems { get; } = [];

    public ObservableCollection<OrganizationRule> Rules { get; } = [];

    public ObservableCollection<OrganizationSession> HistorySessions { get; } = [];

    public ObservableCollection<string> WatcherLogs { get; } = [];

    [ObservableProperty]
    private string _targetFolderPath = string.Empty;

    [ObservableProperty]
    private string _targetFolderDisplayName = "Chưa chọn thư mục";

    partial void OnTargetFolderPathChanged(string value)
    {
        UpdateTargetFolderDisplayName(value);
    }

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

    // Thiết lập đồng bộ với AppConfiguration
    [ObservableProperty]
    private bool _enablePrefixSimilarityGrouping;

    partial void OnEnablePrefixSimilarityGroupingChanged(bool value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.EnablePrefixSimilarityGrouping = value;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private bool _enableExtensionRules;

    partial void OnEnableExtensionRulesChanged(bool value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.EnableExtensionRules = value;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private bool _enableKeywordRules;

    partial void OnEnableKeywordRulesChanged(bool value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.EnableKeywordRules = value;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private double _minFilesForPrefixGroup = 2;

    partial void OnMinFilesForPrefixGroupChanged(double value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.MinFilesForPrefixGroup = Math.Clamp((int)value, 2, 20);
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private double _similarityThresholdPercent = 80;

    partial void OnSimilarityThresholdPercentChanged(double value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.SimilarityThreshold = Math.Clamp(value / 100.0, 0.50, 0.98);
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private bool _groupPrefixInsideCategory;

    partial void OnGroupPrefixInsideCategoryChanged(bool value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.GroupPrefixInsideCategory = value;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private bool _useDedicatedSmartRootFolder = true;

    partial void OnUseDedicatedSmartRootFolderChanged(bool value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.PrefixGroupLocation = value
            ? PrefixGroupLocationMode.DedicatedRootSubfolder
            : PrefixGroupLocationMode.DirectSubfolder;
        SaveConfigAndReanalyze();
    }

    [ObservableProperty]
    private string _prefixGroupRootFolderName = "Gom theo tên";

    partial void OnPrefixGroupRootFolderNameChanged(string value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.PrefixGroupRootFolderName = string.IsNullOrWhiteSpace(value) ? "Gom theo tên" : value.Trim();
        SaveConfigAndReanalyze();
    }

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
        if (_isInitializing) return;
        _config.SmartSettings.AutoWatcherDelayUnit = (WatcherDelayUnit)Math.Clamp(value, 0, 5);
        _config.SmartSettings.AutoWatcherDelaySeconds = (int)Math.Clamp(
            _config.SmartSettings.GetAutoWatcherDelayTimeSpan().TotalSeconds,
            1,
            int.MaxValue);
        SaveConfigurationOnly();
        UpdateWatcherDisplayProperties();
    }

    [ObservableProperty]
    private string _watcherDelayUnitSuffixText = "giây";

    [ObservableProperty]
    private string _watcherDelaySummaryText = "Đang áp dụng: Chờ 10 giây sau khi phát hiện tệp mới";

    [ObservableProperty]
    private bool _isWatcherRunning;

    [ObservableProperty]
    private string _watcherStatusTitle = "Dịch vụ Tự động sắp xếp đang tắt";

    [ObservableProperty]
    private string _watcherStatusText = "Đang tắt";

    [ObservableProperty]
    private string _watcherBadgeText = "○ ĐANG TẠM DỪNG";

    [ObservableProperty]
    private string _watcherActionButtonText = "Bật tự động sắp xếp";

    [ObservableProperty]
    private string _watcherActionButtonGlyph = "\uE768";

    [ObservableProperty]
    private string _watcherCompactBadgeText = "○ Đang tạm dừng";

    [ObservableProperty]
    private string _watcherCompactDelayText = "Chờ 10 giây sau khi có tệp mới";

    [ObservableProperty]
    private string _watcherCompactActionText = "Bật tự động";

    [ObservableProperty]
    private int _selectedThemeIndex;

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_isInitializing) return;
        _config.SmartSettings.AppTheme = (AppThemePreference)Math.Clamp(value, 0, 2);
        SaveConfigurationOnly();
        App.ApplyTheme(value);
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
            if (_config.SmartSettings.EnableAutoWatcher && !_watcher.IsRunning)
            {
                _watcher.Start(TargetFolderPath, _config);
            }
        }
        else if (_config.SmartSettings.EnableAutoWatcher)
        {
            _config.SmartSettings.EnableAutoWatcher = false;
            SaveConfigurationOnly();
        }

        UpdateWatcherDisplayProperties();

        if (AutoCheckUpdatesOnStartup)
        {
            _ = Task.Run(() => CheckForUpdatesAsync(silent: true));
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
            EnablePrefixSimilarityGrouping = s.EnablePrefixSimilarityGrouping;
            EnableExtensionRules = s.EnableExtensionRules;
            EnableKeywordRules = s.EnableKeywordRules;
            MinFilesForPrefixGroup = s.MinFilesForPrefixGroup;
            SimilarityThresholdPercent = Math.Round(s.SimilarityThreshold * 100.0);
            GroupPrefixInsideCategory = s.GroupPrefixInsideCategory;
            UseDedicatedSmartRootFolder = s.PrefixGroupLocation == PrefixGroupLocationMode.DedicatedRootSubfolder;
            PrefixGroupRootFolderName = s.PrefixGroupRootFolderName;
            EnableDateSubfolders = s.EnableDateSubfolders;
            EnableLargeFileIsolation = s.EnableLargeFileIsolation;
            LargeFileThresholdMB = s.LargeFileThresholdMB;
            MoveUnmatchedToOthers = s.MoveUnmatchedToOthers;
            OthersFolderName = s.OthersFolderName;
            SelectedConflictIndex = (int)s.ConflictBehavior;
            AutoWatcherDelayValue = Math.Max(1, s.AutoWatcherDelayValue);
            SelectedWatcherDelayUnitIndex = (int)s.AutoWatcherDelayUnit;
            SelectedThemeIndex = (int)s.AppTheme;

            ReloadRulesCollection();
        }
        finally
        {
            _isInitializing = false;
        }

        UpdateTargetFolderDisplayName(TargetFolderPath);
        UpdateWatcherDisplayProperties();
    }

    private void ReloadRulesCollection()
    {
        Rules.Clear();
        foreach (OrganizationRule rule in _config.Rules.OrderBy(r => r.Priority))
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

        if (_watcher.IsRunning && !string.Equals(_watcher.WatchedFolder, folderPath, StringComparison.OrdinalIgnoreCase))
        {
            _watcher.Start(folderPath, _config);
        }
        else
        {
            UpdateWatcherDisplayProperties();
        }
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
            return;
        }

        IsBusy = true;
        ProgressText = "Đang quét và phân tích cấu trúc tệp...";
        ProgressValue = 0;

        try
        {
            _config.LastTargetFolder = TargetFolderPath;
            SaveConfigurationOnly();

            FolderAnalysisResult result = await _engine.AnalyzeFolderAsync(TargetFolderPath, _config);

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
            ExecutionResult exec = await _engine.ExecutePlanAsync(
                TargetFolderPath,
                itemsToProcess,
                _config.SmartSettings.ConflictBehavior,
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

            // Quét lại để cập nhật bảng Preview phản ánh thư mục sau khi dọn
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
            if (string.Equals(session.SourceFolderPath, TargetFolderPath, StringComparison.OrdinalIgnoreCase))
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

    public async Task SelectSpecialFolderAsync(Environment.SpecialFolder specialFolder)
    {
        string path = specialFolder == Environment.SpecialFolder.UserProfile
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : Environment.GetFolderPath(specialFolder);

        await SetTargetFolderAndAnalyzeAsync(path);
    }

    public void OpenTargetFolderInExplorer()
    {
        if (!string.IsNullOrWhiteSpace(TargetFolderPath) && Directory.Exists(TargetFolderPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = TargetFolderPath,
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

    public void SaveRuleChanges(OrganizationRule rule, bool isNew)
    {
        if (isNew)
        {
            _config.Rules.Add(rule);
        }
        else
        {
            int idx = _config.Rules.FindIndex(r => r.Id == rule.Id);
            if (idx >= 0)
            {
                _config.Rules[idx] = rule;
            }
        }

        SaveConfigurationOnly();
        ReloadRulesCollection();
        _ = AnalyzeFolderAsync();
    }

    public void ToggleRuleEnabled(OrganizationRule rule, bool isEnabled)
    {
        rule.IsEnabled = isEnabled;
        SaveConfigurationOnly();
        _ = AnalyzeFolderAsync();
    }

    public void SetAllRulesEnabled(bool isEnabled)
    {
        foreach (OrganizationRule rule in _config.Rules)
        {
            rule.IsEnabled = isEnabled;
        }

        SaveConfigurationOnly();
        ReloadRulesCollection();
        _ = AnalyzeFolderAsync();
    }

    public void DeleteRule(OrganizationRule rule)
    {
        _config.Rules.RemoveAll(r => r.Id == rule.Id);
        SaveConfigurationOnly();
        ReloadRulesCollection();
        _ = AnalyzeFolderAsync();
    }

    [RelayCommand]
    public void RestoreDefaultRules()
    {
        _config.Rules = ConfigAndHistoryStore.CreateDefaultRules();
        SaveConfigurationOnly();
        ReloadRulesCollection();
        ShowInfoBar("Đã khôi phục quy tắc mặc định", "Bộ quy tắc chuẩn Windows 11 đã được thiết lập lại.", InfoBarSeverity.Informational);
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
            UpdateWatcherDisplayProperties();
            return;
        }

        if (_watcher.IsRunning)
        {
            UpdateWatcherDisplayProperties();
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetFolderPath) || !Directory.Exists(TargetFolderPath))
        {
            ShowInfoBar("Chưa chọn thư mục hợp lệ", "Vui lòng chọn thư mục cần theo dõi trước khi bật.", InfoBarSeverity.Warning);
            UpdateWatcherDisplayProperties();
            return;
        }

        _config.SmartSettings.EnableAutoWatcher = true;
        SaveConfigurationOnly();
        _watcher.Start(TargetFolderPath, _config);
    }

    [RelayCommand]
    public Task RunWatcherNowAsync() => RunQuickOrganizeAsync();

    public async Task<string> RunQuickOrganizeAsync()
    {
        if (IsBusy)
        {
            return "Đang bận xử lý tác vụ khác, vui lòng chờ giây lát.";
        }

        if (string.IsNullOrWhiteSpace(TargetFolderPath) || !Directory.Exists(TargetFolderPath))
        {
            const string missingMsg = "Vui lòng chọn thư mục cần dọn dẹp trước.";
            ShowInfoBar("Chưa chọn thư mục hợp lệ", missingMsg, InfoBarSeverity.Warning);
            return missingMsg;
        }

        if (_watcher.IsRunning)
        {
            int movedCount = await _watcher.ProcessImmediatelyAsync();
            if (movedCount > 0)
            {
                string msg = $"Đã tự động sắp xếp gọn {movedCount} tệp trong thư mục hiện tại.";
                ShowInfoBar("Đã dọn dẹp ngay tức thì", msg, InfoBarSeverity.Success);
                return msg;
            }

            const string noneMsg = "Không có tệp sẵn sàng nào cần dọn thêm lúc này.";
            ShowInfoBar("Không có tệp cần dọn", noneMsg, InfoBarSeverity.Informational);
            return noneMsg;
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
            if (string.Equals(session.SourceFolderPath, TargetFolderPath, StringComparison.OrdinalIgnoreCase))
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
            UpdateWatcherDisplayProperties();
        });
    }

    private void UpdateWatcherDisplayProperties()
    {
        SmartGroupingSettings settings = _config.SmartSettings;
        string unitSuffix = SmartGroupingSettings.GetUnitDisplayName(settings.AutoWatcherDelayUnit);
        string delayDisplay = settings.GetAutoWatcherDelayDisplayText();

        WatcherDelayUnitSuffixText = unitSuffix;
        WatcherDelaySummaryText = $"Đang áp dụng: Chờ {delayDisplay} sau khi phát hiện tệp mới";
        WatcherCompactDelayText = $"Chờ {delayDisplay} sau khi có tệp mới";

        bool running = _watcher.IsRunning;
        IsWatcherRunning = running;

        if (running)
        {
            string activeFolder = string.IsNullOrWhiteSpace(_watcher.WatchedFolder) ? TargetFolderPath : _watcher.WatchedFolder;
            WatcherStatusTitle = "Dịch vụ Tự động sắp xếp đang hoạt động";
            WatcherBadgeText = "● ĐANG BẬT TỰ ĐỘNG SẮP XẾP";
            WatcherCompactBadgeText = "● Đang bật tự động";
            WatcherActionButtonText = "Dừng tự động sắp xếp";
            WatcherCompactActionText = "Dừng tự động";
            WatcherActionButtonGlyph = "\uE71A";
            WatcherStatusText = $"Đang theo dõi tại \"{activeFolder}\" (thời gian tự động dọn: {delayDisplay}).";
        }
        else
        {
            WatcherStatusTitle = "Dịch vụ Tự động sắp xếp đang tắt";
            WatcherBadgeText = "○ ĐANG TẠM DỪNG";
            WatcherCompactBadgeText = "○ Đang tạm dừng";
            WatcherActionButtonText = "Bật tự động sắp xếp";
            WatcherCompactActionText = "Bật tự động";
            WatcherActionButtonGlyph = "\uE768";
            WatcherStatusText = string.IsNullOrWhiteSpace(TargetFolderPath)
                ? "Chưa chọn thư mục theo dõi. Hãy chọn thư mục ở bên dưới trước khi bật."
                : $"Đang tạm dừng. Khi bật, Sofol sẽ tự động sắp xếp thư mục \"{TargetFolderPath}\" (chờ {delayDisplay}).";
        }
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
