using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sofol.Core.Models;

namespace Sofol.Core.Services;

/// <summary>
/// Dịch vụ theo dõi một hoặc nhiều thư mục đồng thời theo thời gian thực bằng FileSystemWatcher.
/// Tối ưu tài nguyên chạy ngầm: dùng chung 1 Timer duy nhất và tắt hẳn khi không có tệp chờ,
/// CPU khi nghỉ bằng 0% và chỉ tăng ~64 KB bộ đệm cho mỗi thư mục theo dõi thêm.
/// </summary>
public sealed class FolderWatcherService : IDisposable
{
    private static readonly TimeSpan MaxTimerDueTime = TimeSpan.FromHours(1);

    private sealed class FolderWatchContext : IDisposable
    {
        public string FolderPath { get; }
        public string DisplayName { get; }
        public FileSystemWatcher Watcher { get; }
        public ConcurrentDictionary<string, DateTimeOffset> PendingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, (long Length, DateTime LastWriteUtc)> SkippedSnapshot { get; } = new(StringComparer.OrdinalIgnoreCase);

        private readonly FileSystemEventHandler _onCreated;
        private readonly FileSystemEventHandler _onChanged;
        private readonly RenamedEventHandler _onRenamed;
        private readonly ErrorEventHandler _onError;
        private bool _disposed;

        public FolderWatchContext(
            string folderPath,
            Action<FolderWatchContext, FileSystemEventArgs> onFileEvent,
            Action<FolderWatchContext, RenamedEventArgs> onFileRenamed,
            Action<FolderWatchContext, ErrorEventArgs> onWatcherError)
        {
            FolderPath = WatchedFolderProfile.NormalizePath(folderPath);
            string leaf = Path.GetFileName(FolderPath);
            DisplayName = string.IsNullOrWhiteSpace(leaf) ? FolderPath : leaf;

            Watcher = new FileSystemWatcher(FolderPath)
            {
                Filter = "*",
                IncludeSubdirectories = false,
                InternalBufferSize = 65536,
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size
                             | NotifyFilters.CreationTime
            };

            _onCreated = (_, e) => onFileEvent(this, e);
            _onChanged = (_, e) => onFileEvent(this, e);
            _onRenamed = (_, e) => onFileRenamed(this, e);
            _onError = (_, e) => onWatcherError(this, e);

            Watcher.Created += _onCreated;
            Watcher.Changed += _onChanged;
            Watcher.Renamed += _onRenamed;
            Watcher.Error += _onError;
            Watcher.EnableRaisingEvents = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            try
            {
                Watcher.EnableRaisingEvents = false;
                Watcher.Created -= _onCreated;
                Watcher.Changed -= _onChanged;
                Watcher.Renamed -= _onRenamed;
                Watcher.Error -= _onError;
                Watcher.Dispose();
            }
            catch
            {
                // Bỏ qua lỗi giải phóng watcher
            }

            PendingFiles.Clear();
            SkippedSnapshot.Clear();
        }
    }

    private readonly FileOrganizerEngine _engine;
    private readonly ConfigAndHistoryStore _store;
    private readonly FileLockAndSafetyService _safetyService = new();
    private readonly Dictionary<string, FolderWatchContext> _contexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _processGate = new(1, 1);
    private readonly object _syncRoot = new();

    private Timer? _debounceTimer;
    private CancellationTokenSource? _runCts;
    private AppConfiguration? _activeConfig;
    private bool _isMultiFolderMode;

    public event Action<string>? LogAdded;
    public event Action<OrganizationSession>? SessionCompleted;
    public event Action<bool, string>? StateChanged;
    public event Action? PendingCountsChanged;

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _contexts.Count > 0;
            }
        }
    }

    public int ActiveFolderCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _contexts.Count;
            }
        }
    }

    public string WatchedFolder
    {
        get
        {
            lock (_syncRoot)
            {
                return _contexts.Values.FirstOrDefault()?.FolderPath ?? string.Empty;
            }
        }
    }

    public IReadOnlyList<string> ActiveWatchedFolders
    {
        get
        {
            lock (_syncRoot)
            {
                return _contexts.Values.Select(c => c.FolderPath).ToList();
            }
        }
    }

    public FolderWatcherService(FileOrganizerEngine engine, ConfigAndHistoryStore store)
    {
        _engine = engine;
        _store = store;
    }

    public int GetPendingCount(string? folderPath)
    {
        string normalized = WatchedFolderProfile.NormalizePath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 0;
        }

        lock (_syncRoot)
        {
            return _contexts.TryGetValue(normalized, out FolderWatchContext? ctx)
                ? ctx.PendingFiles.Count
                : 0;
        }
    }

    public void Start(string folderPath, AppConfiguration configuration)
    {
        Stop();

        string normalized = WatchedFolderProfile.NormalizePath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(normalized))
        {
            EmitLog($"Không thể bật Tự động sắp xếp: Thư mục không tồn tại ({folderPath}).");
            return;
        }

        FolderWatchContext? createdCtx = null;
        lock (_syncRoot)
        {
            _isMultiFolderMode = false;
            _activeConfig = configuration;
            _runCts = new CancellationTokenSource();
            _debounceTimer = new Timer(OnTimerTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            createdCtx = new FolderWatchContext(normalized, OnFileEvent, OnFileRenamed, OnWatcherError);
            _contexts[normalized] = createdCtx;
        }

        string delayDisplay = GetDelayDisplayForFolder(normalized, configuration);
        EmitLog($"Đã bắt đầu tự động sắp xếp thư mục: {normalized} (thời gian tự động dọn: {delayDisplay}).");
        StateChanged?.Invoke(true, normalized);

        int existingCount = ScanDirectoryForNewCandidates(createdCtx, isInitialScan: true);
        if (existingCount > 0)
        {
            EmitLog($"[{createdCtx.DisplayName}] Phát hiện {existingCount} tệp hiện có — sẽ tự động dọn sau {delayDisplay}.");
            ScheduleNextTimerTick();
        }
        PendingCountsChanged?.Invoke();
    }

    public void StartConfiguredFolders(AppConfiguration configuration)
    {
        Stop();

        var validFolders = (configuration.WatchedFolders ?? [])
            .Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.FolderPath) && Directory.Exists(f.FolderPath))
            .Select(f => WatchedFolderProfile.NormalizePath(f.FolderPath))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validFolders.Count == 0)
        {
            EmitLog("Không có thư mục hợp lệ nào đang bật để theo dõi tự động.");
            return;
        }

        var startedContexts = new List<FolderWatchContext>();
        lock (_syncRoot)
        {
            _isMultiFolderMode = true;
            _activeConfig = configuration;
            _runCts = new CancellationTokenSource();
            _debounceTimer = new Timer(OnTimerTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            foreach (string folderPath in validFolders)
            {
                try
                {
                    var ctx = new FolderWatchContext(folderPath, OnFileEvent, OnFileRenamed, OnWatcherError);
                    _contexts[folderPath] = ctx;
                    startedContexts.Add(ctx);
                }
                catch (Exception ex)
                {
                    EmitLog($"[Cảnh báo] Không thể theo dõi \"{folderPath}\": {ex.Message}");
                }
            }

            if (_contexts.Count == 0)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = null;
                _runCts?.Dispose();
                _runCts = null;
                return;
            }
        }

        string firstFolder = startedContexts[0].FolderPath;
        EmitLog($"Đã bật tự động sắp xếp cho {startedContexts.Count} thư mục: {string.Join(", ", startedContexts.Select(c => c.DisplayName))}.");
        StateChanged?.Invoke(true, firstFolder);

        bool anyPending = false;
        foreach (FolderWatchContext ctx in startedContexts)
        {
            int existingCount = ScanDirectoryForNewCandidates(ctx, isInitialScan: true);
            if (existingCount > 0)
            {
                anyPending = true;
                string delayDisplay = GetDelayDisplayForFolder(ctx.FolderPath, configuration);
                EmitLog($"[{ctx.DisplayName}] Phát hiện {existingCount} tệp hiện có — sẽ tự động dọn sau {delayDisplay}.");
            }
        }

        if (anyPending)
        {
            ScheduleNextTimerTick();
        }
        PendingCountsChanged?.Invoke();
    }

    public void UpdateConfiguration(AppConfiguration configuration)
    {
        bool shouldStopAll = false;
        var removedContexts = new List<FolderWatchContext>();
        var addedContexts = new List<FolderWatchContext>();
        var existingContexts = new List<FolderWatchContext>();
        bool isRunningNow;
        bool multiMode;

        lock (_syncRoot)
        {
            _activeConfig = configuration;
            isRunningNow = _contexts.Count > 0;
            multiMode = _isMultiFolderMode;

            if (isRunningNow)
            {
                if (multiMode)
                {
                    var desiredPaths = (configuration.WatchedFolders ?? [])
                        .Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.FolderPath) && Directory.Exists(f.FolderPath))
                        .Select(f => WatchedFolderProfile.NormalizePath(f.FolderPath))
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (string currentPath in _contexts.Keys.ToList())
                    {
                        if (!desiredPaths.Contains(currentPath))
                        {
                            removedContexts.Add(_contexts[currentPath]);
                            _contexts.Remove(currentPath);
                        }
                    }

                    foreach (FolderWatchContext ctx in _contexts.Values)
                    {
                        ctx.SkippedSnapshot.Clear();
                        existingContexts.Add(ctx);
                    }

                    foreach (string targetPath in desiredPaths)
                    {
                        if (!_contexts.ContainsKey(targetPath))
                        {
                            try
                            {
                                var newCtx = new FolderWatchContext(targetPath, OnFileEvent, OnFileRenamed, OnWatcherError);
                                _contexts[targetPath] = newCtx;
                                addedContexts.Add(newCtx);
                            }
                            catch (Exception ex)
                            {
                                EmitLog($"[Cảnh báo] Không thể thêm theo dõi \"{targetPath}\": {ex.Message}");
                            }
                        }
                    }

                    if (_contexts.Count == 0)
                    {
                        shouldStopAll = true;
                    }
                }
                else
                {
                    foreach (FolderWatchContext ctx in _contexts.Values)
                    {
                        ctx.SkippedSnapshot.Clear();
                        existingContexts.Add(ctx);
                    }
                }
            }
        }

        foreach (FolderWatchContext removed in removedContexts)
        {
            EmitLog($"[{removed.DisplayName}] Đã tạm dừng theo dõi.");
            removed.Dispose();
        }

        if (shouldStopAll)
        {
            Stop();
            return;
        }

        if (!isRunningNow)
        {
            return;
        }

        foreach (FolderWatchContext added in addedContexts)
        {
            string delayDisplay = GetDelayDisplayForFolder(added.FolderPath, configuration);
            EmitLog($"[{added.DisplayName}] Đã bật theo dõi tự động (chờ {delayDisplay}).");
            int existingCount = ScanDirectoryForNewCandidates(added, isInitialScan: true);
            if (existingCount > 0)
            {
                EmitLog($"[{added.DisplayName}] Phát hiện {existingCount} tệp hiện có — sẽ tự động dọn sau {delayDisplay}.");
            }
        }

        foreach (FolderWatchContext existing in existingContexts)
        {
            ScanDirectoryForNewCandidates(existing, isInitialScan: true);
        }

        ScheduleNextTimerTick();
        if (addedContexts.Count > 0 || removedContexts.Count > 0)
        {
            StateChanged?.Invoke(true, WatchedFolder);
        }
        PendingCountsChanged?.Invoke();
    }

    public void Stop()
    {
        bool wasRunning;
        string stoppedFolder;
        CancellationTokenSource? ctsToDispose;
        List<FolderWatchContext> contextsToDispose;

        lock (_syncRoot)
        {
            wasRunning = _contexts.Count > 0;
            stoppedFolder = _contexts.Values.FirstOrDefault()?.FolderPath ?? string.Empty;

            _debounceTimer?.Dispose();
            _debounceTimer = null;

            ctsToDispose = _runCts;
            _runCts = null;

            contextsToDispose = _contexts.Values.ToList();
            _contexts.Clear();
        }

        foreach (FolderWatchContext ctx in contextsToDispose)
        {
            ctx.Dispose();
        }

        if (ctsToDispose is not null)
        {
            try
            {
                ctsToDispose.Cancel();
            }
            catch
            {
                // Bỏ qua lỗi khi hủy tác vụ nền
            }
            finally
            {
                ctsToDispose.Dispose();
            }
        }

        if (wasRunning)
        {
            EmitLog("Đã dừng chế độ tự động sắp xếp.");
            StateChanged?.Invoke(false, stoppedFolder);
            PendingCountsChanged?.Invoke();
        }
    }

    public async Task<int> ProcessImmediatelyAsync()
    {
        List<FolderWatchContext> snapshot;
        AppConfiguration? config;
        CancellationToken ct;

        lock (_syncRoot)
        {
            snapshot = _contexts.Values.ToList();
            config = _activeConfig;
            ct = _runCts?.Token ?? CancellationToken.None;
        }

        if (snapshot.Count == 0 || config is null)
        {
            return 0;
        }

        int totalMoved = 0;
        await _processGate.WaitAsync(ct);
        try
        {
            foreach (FolderWatchContext ctx in snapshot)
            {
                if (ct.IsCancellationRequested || !Directory.Exists(ctx.FolderPath))
                {
                    continue;
                }

                ctx.SkippedSnapshot.Clear();
                totalMoved += await ProcessPendingForContextAsync(ctx, config, forceImmediate: true, ct);
            }
        }
        finally
        {
            _processGate.Release();
            ScheduleNextTimerTick();
            PendingCountsChanged?.Invoke();
        }

        return totalMoved;
    }

    public async Task<int> ProcessFolderImmediatelyAsync(string folderPath, AppConfiguration? fallbackConfig = null)
    {
        string normalized = WatchedFolderProfile.NormalizePath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(normalized))
        {
            return 0;
        }

        FolderWatchContext? activeCtx;
        AppConfiguration? config;
        CancellationToken ct;

        lock (_syncRoot)
        {
            _contexts.TryGetValue(normalized, out activeCtx);
            config = _activeConfig ?? fallbackConfig;
            ct = _runCts?.Token ?? CancellationToken.None;
        }

        if (config is null)
        {
            return 0;
        }

        await _processGate.WaitAsync(ct);
        try
        {
            if (activeCtx is not null)
            {
                activeCtx.SkippedSnapshot.Clear();
                return await ProcessPendingForContextAsync(activeCtx, config, forceImmediate: true, ct);
            }

            return await AnalyzeAndExecuteFolderOnceAsync(normalized, config, ct);
        }
        finally
        {
            _processGate.Release();
            ScheduleNextTimerTick();
            PendingCountsChanged?.Invoke();
        }
    }

    private void OnFileEvent(FolderWatchContext ctx, FileSystemEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.FullPath) || !File.Exists(e.FullPath))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool isNew = ctx.PendingFiles.TryAdd(e.FullPath, now);
        if (!isNew)
        {
            ctx.PendingFiles[e.FullPath] = now;
        }

        ctx.SkippedSnapshot.TryRemove(e.FullPath, out _);

        if (isNew)
        {
            // Xóa snapshot tổng của thư mục này để các tệp cũ có thể ghép cụm Smart Grouping cùng tệp vừa thêm
            ctx.SkippedSnapshot.Clear();
            string delayDisplay = GetCurrentDelayDisplay(ctx.FolderPath);
            EmitLog($"[{ctx.DisplayName}] Phát hiện tệp mới: {Path.GetFileName(e.FullPath)} — sẽ tự động dọn sau {delayDisplay}.");
            PendingCountsChanged?.Invoke();
        }

        ScheduleNextTimerTick();
    }

    private void OnFileRenamed(FolderWatchContext ctx, RenamedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.OldFullPath))
        {
            ctx.PendingFiles.TryRemove(e.OldFullPath, out _);
            ctx.SkippedSnapshot.TryRemove(e.OldFullPath, out _);
        }

        if (string.IsNullOrWhiteSpace(e.FullPath) || !File.Exists(e.FullPath))
        {
            ScheduleNextTimerTick();
            PendingCountsChanged?.Invoke();
            return;
        }

        ctx.PendingFiles[e.FullPath] = DateTimeOffset.UtcNow;
        ctx.SkippedSnapshot.Clear();

        string delayDisplay = GetCurrentDelayDisplay(ctx.FolderPath);
        EmitLog($"[{ctx.DisplayName}] Phát hiện tệp mới/đổi tên: {Path.GetFileName(e.FullPath)} — sẽ tự động dọn sau {delayDisplay}.");
        ScheduleNextTimerTick();
        PendingCountsChanged?.Invoke();
    }

    private void OnWatcherError(FolderWatchContext ctx, ErrorEventArgs e)
    {
        Exception? ex = e.GetException();
        EmitLog($"[{ctx.DisplayName}] [Cảnh báo] Kết nối sự kiện hệ thống tệp tạm gián đoạn: {ex?.Message ?? "Không rõ nguyên nhân"}. Hệ thống đang quét lại thư mục.");

        if (Directory.Exists(ctx.FolderPath))
        {
            ScanDirectoryForNewCandidates(ctx, isInitialScan: false);
            ScheduleNextTimerTick();
            PendingCountsChanged?.Invoke();
        }
    }

    private void ScheduleNextTimerTick()
    {
        lock (_syncRoot)
        {
            if (_debounceTimer is null || _contexts.Count == 0 || _activeConfig is null)
            {
                return;
            }

            DateTimeOffset? earliestReadyAt = null;
            foreach (FolderWatchContext ctx in _contexts.Values)
            {
                if (ctx.PendingFiles.IsEmpty)
                {
                    continue;
                }

                TimeSpan folderDelay = GetDelayForFolder(ctx.FolderPath, _activeConfig);
                foreach (DateTimeOffset detectedAt in ctx.PendingFiles.Values)
                {
                    DateTimeOffset readyAt = detectedAt.Add(folderDelay);
                    if (earliestReadyAt is null || readyAt < earliestReadyAt.Value)
                    {
                        earliestReadyAt = readyAt;
                    }
                }
            }

            if (earliestReadyAt is null)
            {
                // Khi tất cả hàng đợi đều rỗng, tắt hẳn Timer để mức CPU khi chạy ngầm bằng 0
                _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                return;
            }

            TimeSpan remaining = earliestReadyAt.Value - DateTimeOffset.UtcNow;
            TimeSpan dueTime;
            if (remaining <= TimeSpan.Zero)
            {
                dueTime = TimeSpan.FromMilliseconds(200);
            }
            else if (remaining < TimeSpan.FromMilliseconds(500))
            {
                dueTime = TimeSpan.FromMilliseconds(500);
            }
            else if (remaining > MaxTimerDueTime)
            {
                dueTime = MaxTimerDueTime;
            }
            else
            {
                dueTime = remaining + TimeSpan.FromMilliseconds(150);
            }

            _debounceTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
        }
    }

    private int ScanDirectoryForNewCandidates(FolderWatchContext ctx, bool isInitialScan)
    {
        if (!Directory.Exists(ctx.FolderPath))
        {
            return 0;
        }

        int newCount = 0;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string delayDisplay = GetCurrentDelayDisplay(ctx.FolderPath);

        try
        {
            foreach (string filePath in Directory.EnumerateFiles(ctx.FolderPath, "*", SearchOption.TopDirectoryOnly))
            {
                if (ctx.PendingFiles.ContainsKey(filePath))
                {
                    continue;
                }

                try
                {
                    var fi = new FileInfo(filePath);
                    if (!fi.Exists)
                    {
                        continue;
                    }

                    if (ctx.SkippedSnapshot.TryGetValue(filePath, out var snap) &&
                        snap.Length == fi.Length &&
                        snap.LastWriteUtc == fi.LastWriteTimeUtc)
                    {
                        continue;
                    }

                    if (ctx.PendingFiles.TryAdd(filePath, now))
                    {
                        newCount++;
                        if (!isInitialScan)
                        {
                            EmitLog($"[{ctx.DisplayName}] Phát hiện tệp mới: {fi.Name} — sẽ tự động dọn sau {delayDisplay}.");
                        }
                    }
                }
                catch
                {
                    // Bỏ qua tệp vừa bị xóa hoặc chưa đọc được metadata trong lúc di chuyển
                }
            }
        }
        catch
        {
            // Bỏ qua lỗi đọc thư mục tạm thời
        }

        return newCount;
    }

    private async void OnTimerTick(object? state)
    {
        if (!_processGate.Wait(0))
        {
            return;
        }

        try
        {
            List<FolderWatchContext> snapshot;
            AppConfiguration? config;
            CancellationToken ct;

            lock (_syncRoot)
            {
                if (_contexts.Count == 0 || _activeConfig is null)
                {
                    return;
                }

                snapshot = _contexts.Values.ToList();
                config = _activeConfig;
                ct = _runCts?.Token ?? CancellationToken.None;
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            foreach (FolderWatchContext ctx in snapshot)
            {
                if (ct.IsCancellationRequested || ctx.PendingFiles.IsEmpty || !Directory.Exists(ctx.FolderPath))
                {
                    continue;
                }

                await ProcessPendingForContextAsync(ctx, config, forceImmediate: false, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Dịch vụ vừa được dừng trong lúc xử lý
        }
        catch (Exception ex)
        {
            EmitLog($"[Lỗi Tự động sắp xếp] {ex.Message}");
        }
        finally
        {
            _processGate.Release();
            ScheduleNextTimerTick();
            PendingCountsChanged?.Invoke();
        }
    }

    private async Task<int> ProcessPendingForContextAsync(
        FolderWatchContext ctx,
        AppConfiguration globalConfig,
        bool forceImmediate,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(ctx.FolderPath))
        {
            return 0;
        }

        TimeSpan delay = GetDelayForFolder(ctx.FolderPath, globalConfig);
        List<string> candidatePaths;
        if (forceImmediate)
        {
            candidatePaths = Directory.EnumerateFiles(ctx.FolderPath, "*", SearchOption.TopDirectoryOnly).ToList();
        }
        else
        {
            DateTimeOffset cutoff = DateTimeOffset.UtcNow.Subtract(delay);
            candidatePaths = ctx.PendingFiles
                .Where(kv => kv.Value <= cutoff)
                .Select(kv => kv.Key)
                .ToList();
        }

        if (candidatePaths.Count == 0)
        {
            return 0;
        }

        var readySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in candidatePaths)
        {
            if (File.Exists(path))
            {
                readySet.Add(path);
            }
            else
            {
                ctx.PendingFiles.TryRemove(path, out _);
                ctx.SkippedSnapshot.TryRemove(path, out _);
            }
        }

        if (readySet.Count == 0)
        {
            return 0;
        }

        AppConfiguration effectiveConfig = globalConfig.BuildEffectiveConfigurationForFolder(ctx.FolderPath);
        FolderAnalysisResult analysis = await _engine.AnalyzeFolderAsync(ctx.FolderPath, effectiveConfig, cancellationToken);
        var itemsToMove = new List<FileMovePlanItem>();

        foreach (FileMovePlanItem item in analysis.Items)
        {
            if (!readySet.Contains(item.SourcePath))
            {
                continue;
            }

            if (item.Status == PlanItemStatus.LockedOrDownloading)
            {
                var fi = new FileInfo(item.SourcePath);
                if (fi.Exists && _safetyService.IsPassiveIgnoredFile(fi))
                {
                    ctx.PendingFiles.TryRemove(item.SourcePath, out _);
                    TryRecordSkippedSnapshot(ctx, item.SourcePath);
                }
                else
                {
                    ctx.PendingFiles[item.SourcePath] = DateTimeOffset.UtcNow - delay + TimeSpan.FromSeconds(5);
                }

                continue;
            }

            ctx.PendingFiles.TryRemove(item.SourcePath, out _);

            if (item.Status == PlanItemStatus.Ready && item.IsSelected)
            {
                itemsToMove.Add(item);
            }
            else
            {
                TryRecordSkippedSnapshot(ctx, item.SourcePath);
            }
        }

        if (itemsToMove.Count == 0)
        {
            return 0;
        }

        ExecutionResult execution = await _engine.ExecutePlanAsync(
            ctx.FolderPath,
            itemsToMove,
            effectiveConfig.SmartSettings.ConflictBehavior,
            cancellationToken: cancellationToken);

        foreach (FileMovePlanItem item in itemsToMove)
        {
            if (item.Status == PlanItemStatus.Failed && File.Exists(item.SourcePath))
            {
                ctx.PendingFiles[item.SourcePath] = DateTimeOffset.UtcNow - delay + TimeSpan.FromSeconds(5);
            }
        }

        if (execution.Session is not null && execution.MovedCount > 0)
        {
            _store.AddSession(execution.Session);

            string details = string.Join(", ",
                execution.Session.Records.Take(3).Select(r => $"{r.FileName} → {r.RelativeDestinationFolder}"));
            if (execution.Session.Records.Count > 3)
            {
                details += $" (+{execution.Session.Records.Count - 3} tệp khác)";
            }

            EmitLog($"[{ctx.DisplayName}] [Tự động] Đã sắp xếp {execution.MovedCount} tệp: {details}");
            SessionCompleted?.Invoke(execution.Session);
        }
        else if (execution.SkippedCount > 0)
        {
            EmitLog($"[{ctx.DisplayName}] [Tự động] Đã kiểm tra {itemsToMove.Count} tệp nhưng bỏ qua do trùng tên tại đích.");
        }

        return execution.MovedCount;
    }

    private async Task<int> AnalyzeAndExecuteFolderOnceAsync(
        string folderPath,
        AppConfiguration globalConfig,
        CancellationToken cancellationToken)
    {
        AppConfiguration effectiveConfig = globalConfig.BuildEffectiveConfigurationForFolder(folderPath);
        FolderAnalysisResult analysis = await _engine.AnalyzeFolderAsync(folderPath, effectiveConfig, cancellationToken);
        List<FileMovePlanItem> itemsToMove = analysis.Items
            .Where(i => i.Status == PlanItemStatus.Ready && i.IsSelected)
            .ToList();

        if (itemsToMove.Count == 0)
        {
            return 0;
        }

        ExecutionResult execution = await _engine.ExecutePlanAsync(
            folderPath,
            itemsToMove,
            effectiveConfig.SmartSettings.ConflictBehavior,
            cancellationToken: cancellationToken);

        if (execution.Session is not null && execution.MovedCount > 0)
        {
            _store.AddSession(execution.Session);
            string displayName = Path.GetFileName(folderPath);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = folderPath;
            }

            string details = string.Join(", ",
                execution.Session.Records.Take(3).Select(r => $"{r.FileName} → {r.RelativeDestinationFolder}"));
            if (execution.Session.Records.Count > 3)
            {
                details += $" (+{execution.Session.Records.Count - 3} tệp khác)";
            }

            EmitLog($"[{displayName}] [Dọn ngay] Đã sắp xếp {execution.MovedCount} tệp: {details}");
            SessionCompleted?.Invoke(execution.Session);
        }

        return execution.MovedCount;
    }

    private static void TryRecordSkippedSnapshot(FolderWatchContext ctx, string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Exists)
            {
                ctx.SkippedSnapshot[filePath] = (fi.Length, fi.LastWriteTimeUtc);
            }
        }
        catch
        {
            // Bỏ qua nếu tệp không còn truy cập được
        }
    }

    private static TimeSpan GetDelayForFolder(string folderPath, AppConfiguration globalConfig)
    {
        WatchedFolderProfile? profile = globalConfig.FindWatchedFolder(folderPath);
        return profile is not null
            ? profile.GetEffectiveDelayTimeSpan(globalConfig.SmartSettings)
            : globalConfig.SmartSettings.GetAutoWatcherDelayTimeSpan();
    }

    private static string GetDelayDisplayForFolder(string folderPath, AppConfiguration globalConfig)
    {
        WatchedFolderProfile? profile = globalConfig.FindWatchedFolder(folderPath);
        return profile is not null && profile.UseCustomDelay
            ? profile.GetEffectiveDelayDisplayText(globalConfig.SmartSettings)
            : globalConfig.SmartSettings.GetAutoWatcherDelayDisplayText();
    }

    private string GetCurrentDelayDisplay(string folderPath)
    {
        lock (_syncRoot)
        {
            return _activeConfig is not null
                ? GetDelayDisplayForFolder(folderPath, _activeConfig)
                : "10 giây";
        }
    }

    private void EmitLog(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogAdded?.Invoke(line);
    }

    public void Dispose()
    {
        Stop();
    }
}
