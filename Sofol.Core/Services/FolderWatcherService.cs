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
/// Dịch vụ theo dõi thư mục theo thời gian thực bằng FileSystemWatcher.
/// Tối ưu tài nguyên chạy ngầm: khi không có tệp chờ xử lý, bộ đếm sẽ ngủ hoàn toàn
/// và chỉ thức dậy khi nhân Windows phát sự kiện tệp mới.
/// </summary>
public sealed class FolderWatcherService : IDisposable
{
    private static readonly TimeSpan MaxTimerDueTime = TimeSpan.FromHours(1);

    private readonly FileOrganizerEngine _engine;
    private readonly ConfigAndHistoryStore _store;
    private readonly FileLockAndSafetyService _safetyService = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pendingFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (long Length, DateTime LastWriteUtc)> _skippedSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _processGate = new(1, 1);
    private readonly object _syncRoot = new();

    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private CancellationTokenSource? _runCts;
    private AppConfiguration? _activeConfig;
    private string _watchedFolder = string.Empty;

    public event Action<string>? LogAdded;
    public event Action<OrganizationSession>? SessionCompleted;
    public event Action<bool, string>? StateChanged;

    public bool IsRunning => _watcher is not null && _watcher.EnableRaisingEvents;

    public string WatchedFolder => _watchedFolder;

    public FolderWatcherService(FileOrganizerEngine engine, ConfigAndHistoryStore store)
    {
        _engine = engine;
        _store = store;
    }

    public void Start(string folderPath, AppConfiguration configuration)
    {
        Stop();

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            EmitLog($"Không thể bật Tự động sắp xếp: Thư mục không tồn tại ({folderPath}).");
            return;
        }

        string delayDisplay = configuration.SmartSettings.GetAutoWatcherDelayDisplayText();

        lock (_syncRoot)
        {
            _watchedFolder = folderPath;
            _activeConfig = configuration;
            _pendingFiles.Clear();
            _skippedSnapshot.Clear();
            _runCts = new CancellationTokenSource();

            _watcher = new FileSystemWatcher(folderPath)
            {
                Filter = "*",
                IncludeSubdirectories = false,
                InternalBufferSize = 65536,
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size
                             | NotifyFilters.CreationTime
            };

            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;

            // Khởi tạo Timer ở trạng thái ngủ; chỉ đánh thức khi thực sự có tệp đang chờ
            _debounceTimer = new Timer(OnTimerTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        EmitLog($"Đã bắt đầu tự động sắp xếp thư mục: {folderPath} (độ trễ an toàn: {delayDisplay}).");
        StateChanged?.Invoke(true, folderPath);

        int existingCount = ScanDirectoryForNewCandidates(folderPath, isInitialScan: true);
        if (existingCount > 0)
        {
            EmitLog($"Phát hiện {existingCount} tệp hiện có trong thư mục — sẽ tự động dọn sau {delayDisplay}.");
            ScheduleNextTimerTick();
        }
    }

    public void UpdateConfiguration(AppConfiguration configuration)
    {
        string? runningFolder = null;
        lock (_syncRoot)
        {
            _activeConfig = configuration;
            _skippedSnapshot.Clear();
            if (_watcher is not null && _watcher.EnableRaisingEvents && !string.IsNullOrWhiteSpace(_watchedFolder))
            {
                runningFolder = _watchedFolder;
            }
        }

        if (runningFolder is not null)
        {
            ScanDirectoryForNewCandidates(runningFolder, isInitialScan: true);
            ScheduleNextTimerTick();
        }
    }

    public void Stop()
    {
        bool wasRunning;
        string stoppedFolder;
        CancellationTokenSource? ctsToDispose;

        lock (_syncRoot)
        {
            wasRunning = _watcher is not null;
            stoppedFolder = _watchedFolder;

            _debounceTimer?.Dispose();
            _debounceTimer = null;

            ctsToDispose = _runCts;
            _runCts = null;

            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileEvent;
                _watcher.Changed -= OnFileEvent;
                _watcher.Renamed -= OnFileRenamed;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
                _watcher = null;
            }

            _pendingFiles.Clear();
            _skippedSnapshot.Clear();
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
        }
    }

    public async Task<int> ProcessImmediatelyAsync()
    {
        string folderPath;
        AppConfiguration? config;
        CancellationToken ct;

        lock (_syncRoot)
        {
            folderPath = _watchedFolder;
            config = _activeConfig;
            ct = _runCts?.Token ?? CancellationToken.None;
        }

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath) || config is null)
        {
            return 0;
        }

        await _processGate.WaitAsync(ct);
        try
        {
            _skippedSnapshot.Clear();
            return await ProcessPendingInternalAsync(folderPath, config, forceImmediate: true, ct);
        }
        finally
        {
            _processGate.Release();
            ScheduleNextTimerTick();
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.FullPath) || !File.Exists(e.FullPath))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool isNew = _pendingFiles.TryAdd(e.FullPath, now);
        if (!isNew)
        {
            _pendingFiles[e.FullPath] = now;
        }

        _skippedSnapshot.TryRemove(e.FullPath, out _);

        if (isNew)
        {
            // Xóa snapshot tổng để các tệp cũ có thể ghép cụm Smart Grouping cùng tệp vừa thêm
            _skippedSnapshot.Clear();
            string delayDisplay = GetCurrentDelayDisplay();
            EmitLog($"Phát hiện tệp mới: {Path.GetFileName(e.FullPath)} — sẽ tự động dọn sau {delayDisplay}.");
        }

        ScheduleNextTimerTick();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.OldFullPath))
        {
            _pendingFiles.TryRemove(e.OldFullPath, out _);
            _skippedSnapshot.TryRemove(e.OldFullPath, out _);
        }

        if (string.IsNullOrWhiteSpace(e.FullPath) || !File.Exists(e.FullPath))
        {
            ScheduleNextTimerTick();
            return;
        }

        _pendingFiles[e.FullPath] = DateTimeOffset.UtcNow;
        _skippedSnapshot.Clear();

        string delayDisplay = GetCurrentDelayDisplay();
        EmitLog($"Phát hiện tệp mới/đổi tên: {Path.GetFileName(e.FullPath)} — sẽ tự động dọn sau {delayDisplay}.");
        ScheduleNextTimerTick();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Exception? ex = e.GetException();
        EmitLog($"[Cảnh báo] Kết nối sự kiện hệ thống tệp tạm gián đoạn: {ex?.Message ?? "Không rõ nguyên nhân"}. Hệ thống đang quét lại thư mục.");

        string folderPath;
        lock (_syncRoot)
        {
            folderPath = _watchedFolder;
        }

        if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
        {
            ScanDirectoryForNewCandidates(folderPath, isInitialScan: false);
            ScheduleNextTimerTick();
        }
    }

    private void ScheduleNextTimerTick()
    {
        lock (_syncRoot)
        {
            if (_debounceTimer is null || _watcher is null || _activeConfig is null)
            {
                return;
            }

            if (_pendingFiles.IsEmpty)
            {
                // Khi hàng đợi rỗng, tắt hẳn Timer để mức CPU khi chạy ngầm bằng 0
                _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                return;
            }

            TimeSpan delay = _activeConfig.SmartSettings.GetAutoWatcherDelayTimeSpan();
            DateTimeOffset earliestReadyAt = _pendingFiles.Values.Min().Add(delay);
            TimeSpan remaining = earliestReadyAt - DateTimeOffset.UtcNow;

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

    private int ScanDirectoryForNewCandidates(string folderPath, bool isInitialScan)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return 0;
        }

        int newCount = 0;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string delayDisplay = GetCurrentDelayDisplay();

        try
        {
            foreach (string filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
            {
                if (_pendingFiles.ContainsKey(filePath))
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

                    if (_skippedSnapshot.TryGetValue(filePath, out var snap) &&
                        snap.Length == fi.Length &&
                        snap.LastWriteUtc == fi.LastWriteTimeUtc)
                    {
                        continue;
                    }

                    if (_pendingFiles.TryAdd(filePath, now))
                    {
                        newCount++;
                        if (!isInitialScan)
                        {
                            EmitLog($"Phát hiện tệp mới: {fi.Name} — sẽ tự động dọn sau {delayDisplay}.");
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
            string folderPath;
            AppConfiguration? config;
            CancellationToken ct;

            lock (_syncRoot)
            {
                if (_watcher is null || _activeConfig is null || string.IsNullOrWhiteSpace(_watchedFolder))
                {
                    return;
                }

                folderPath = _watchedFolder;
                config = _activeConfig;
                ct = _runCts?.Token ?? CancellationToken.None;
            }

            if (ct.IsCancellationRequested || !Directory.Exists(folderPath))
            {
                return;
            }

            await ProcessPendingInternalAsync(folderPath, config, forceImmediate: false, ct);
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
        }
    }

    private async Task<int> ProcessPendingInternalAsync(
        string folderPath,
        AppConfiguration config,
        bool forceImmediate,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folderPath))
        {
            return 0;
        }

        TimeSpan delay = config.SmartSettings.GetAutoWatcherDelayTimeSpan();
        List<string> candidatePaths;
        if (forceImmediate)
        {
            candidatePaths = Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly).ToList();
        }
        else
        {
            DateTimeOffset cutoff = DateTimeOffset.UtcNow.Subtract(delay);
            candidatePaths = _pendingFiles
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
                _pendingFiles.TryRemove(path, out _);
                _skippedSnapshot.TryRemove(path, out _);
            }
        }

        if (readySet.Count == 0)
        {
            return 0;
        }

        FolderAnalysisResult analysis = await _engine.AnalyzeFolderAsync(folderPath, config, cancellationToken);
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
                    // Tệp tải dở (.crdownload, .part, .tmp) hoặc tệp hệ thống (desktop.ini):
                    // lưu dấu vết và đưa Timer về nghỉ hoàn toàn cho đến khi có sự kiện đổi tên/ghi mới.
                    _pendingFiles.TryRemove(item.SourcePath, out _);
                    TryRecordSkippedSnapshot(item.SourcePath);
                }
                else
                {
                    // Tệp thường đang bị ứng dụng khác khóa tạm thời: hẹn kiểm tra lại sau 5 giây.
                    _pendingFiles[item.SourcePath] = DateTimeOffset.UtcNow - delay + TimeSpan.FromSeconds(5);
                }

                continue;
            }

            _pendingFiles.TryRemove(item.SourcePath, out _);

            if (item.Status == PlanItemStatus.Ready && item.IsSelected)
            {
                itemsToMove.Add(item);
            }
            else
            {
                TryRecordSkippedSnapshot(item.SourcePath);
            }
        }

        if (itemsToMove.Count == 0)
        {
            return 0;
        }

        ExecutionResult execution = await _engine.ExecutePlanAsync(
            folderPath,
            itemsToMove,
            config.SmartSettings.ConflictBehavior,
            cancellationToken: cancellationToken);

        foreach (FileMovePlanItem item in itemsToMove)
        {
            if (item.Status == PlanItemStatus.Failed && File.Exists(item.SourcePath))
            {
                // Hẹn kiểm tra lại sau 5 giây nếu tệp bị khóa đúng lúc di chuyển
                _pendingFiles[item.SourcePath] = DateTimeOffset.UtcNow - delay + TimeSpan.FromSeconds(5);
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

            EmitLog($"[Tự động] Đã sắp xếp {execution.MovedCount} tệp: {details}");
            SessionCompleted?.Invoke(execution.Session);
        }
        else if (execution.SkippedCount > 0)
        {
            EmitLog($"[Tự động] Đã kiểm tra {itemsToMove.Count} tệp nhưng bỏ qua do trùng tên tại đích.");
        }

        return execution.MovedCount;
    }

    private void TryRecordSkippedSnapshot(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Exists)
            {
                _skippedSnapshot[filePath] = (fi.Length, fi.LastWriteTimeUtc);
            }
        }
        catch
        {
            // Bỏ qua nếu tệp không còn truy cập được
        }
    }

    private string GetCurrentDelayDisplay()
    {
        lock (_syncRoot)
        {
            return _activeConfig?.SmartSettings.GetAutoWatcherDelayDisplayText() ?? "10 giây";
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
