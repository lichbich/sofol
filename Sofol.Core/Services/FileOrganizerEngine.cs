using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sofol.Core.Models;

namespace Sofol.Core.Services;

/// <summary>
/// Lõi điều phối phân tích (Preview), thực thi di chuyển tệp an toàn và Hoàn tác (Undo).
/// </summary>
public sealed class FileOrganizerEngine
{
    private readonly FileLockAndSafetyService _safetyService;
    private readonly SmartNameGroupingService _smartGroupingService;

    public FileOrganizerEngine()
        : this(new FileLockAndSafetyService(), new SmartNameGroupingService())
    {
    }

    public FileOrganizerEngine(
        FileLockAndSafetyService safetyService,
        SmartNameGroupingService smartGroupingService)
    {
        _safetyService = safetyService;
        _smartGroupingService = smartGroupingService;
    }

    public Task<FolderAnalysisResult> AnalyzeFolderAsync(
        string folderPath,
        AppConfiguration config,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => AnalyzeFolderInternal(folderPath, config, cancellationToken), cancellationToken);
    }

    private FolderAnalysisResult AnalyzeFolderInternal(
        string folderPath,
        AppConfiguration config,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Thư mục không tồn tại: {folderPath}");
        }

        var dirInfo = new DirectoryInfo(folderPath);
        int subfolderCount = dirInfo.EnumerateDirectories().Count();

        var allFiles = dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var safeFiles = new List<FileInfo>();
        var unsafeItems = new List<FileMovePlanItem>();

        foreach (FileInfo file in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (isSafe, skipReason) = _safetyService.CheckFileSafety(file);
            if (!isSafe)
            {
                unsafeItems.Add(new FileMovePlanItem
                {
                    FileName = file.Name,
                    Extension = file.Extension.ToLowerInvariant(),
                    SourcePath = file.FullName,
                    ProposedTargetFolder = "(Giữ nguyên tại chỗ)",
                    ProposedDestinationPath = file.FullName,
                    FileSizeBytes = file.Length,
                    LastModified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
                    MatchedRuleName = "Bảo vệ an toàn",
                    IconGlyph = "\uE72E",
                    RuleBadgeType = RuleBadgeType.LockedOrDownloading,
                    Status = PlanItemStatus.LockedOrDownloading,
                    StatusText = skipReason,
                    IsSelected = false
                });
            }
            else
            {
                safeFiles.Add(file);
            }
        }

        SmartGroupingSettings smartSettings = config.SmartSettings;
        Dictionary<string, SmartGroupMatch> smartGroups = _smartGroupingService.DetectSmartGroups(safeFiles, smartSettings);

        List<OrganizationRule> activeRules = config.Rules
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ToList();

        var readyItems = new List<FileMovePlanItem>();

        foreach (FileInfo file in safeFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileMovePlanItem planItem = BuildPlanItemForSafeFile(
                folderPath,
                file,
                smartSettings,
                smartGroups,
                activeRules);

            readyItems.Add(planItem);
        }

        var combinedItems = readyItems
            .Concat(unsafeItems)
            .OrderByDescending(i => i.Status == PlanItemStatus.Ready)
            .ThenBy(i => i.RuleBadgeType)
            .ThenBy(i => i.ProposedTargetFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var readySelected = combinedItems.Where(i => i.Status == PlanItemStatus.Ready && i.IsSelected).ToList();

        return new FolderAnalysisResult
        {
            TargetFolderPath = folderPath,
            TotalFilesScanned = allFiles.Count,
            ExistingSubfoldersCount = subfolderCount,
            ReadyToMoveCount = readySelected.Count,
            SmartGroupsCount = combinedItems
                .Where(i => i.RuleBadgeType == RuleBadgeType.SmartNameGroup && i.Status == PlanItemStatus.Ready)
                .Select(i => i.ProposedTargetFolder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            DistinctDestinationFoldersCount = readySelected
                .Select(i => i.ProposedTargetFolder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            SkippedOrLockedCount = unsafeItems.Count,
            TotalReadyBytes = readySelected.Sum(i => i.FileSizeBytes),
            Items = combinedItems
        };
    }

    private static FileMovePlanItem BuildPlanItemForSafeFile(
        string rootFolderPath,
        FileInfo file,
        SmartGroupingSettings settings,
        IReadOnlyDictionary<string, SmartGroupMatch> smartGroups,
        IReadOnlyList<OrganizationRule> activeRules)
    {
        string ext = file.Extension.ToLowerInvariant();
        string fileNameNoExt = Path.GetFileNameWithoutExtension(file.Name);
        long thresholdBytes = Math.Max(1, settings.LargeFileThresholdMB) * 1024L * 1024L;

        bool hasSmartGroup = smartGroups.TryGetValue(file.FullName, out SmartGroupMatch? smartGroup);

        string targetRelativeFolder;
        string finalMatchedRule;
        string finalIcon;
        RuleBadgeType finalBadge;

        // Ưu tiên 1: Quy tắc có Từ khóa (hoặc kết hợp Từ khóa + Đuôi tệp) của người dùng
        OrganizationRule? matchedKeywordRule = FindMatchingKeywordRule(ext, fileNameNoExt, settings, activeRules);
        if (matchedKeywordRule is not null)
        {
            string baseTarget = matchedKeywordRule.TargetFolderName.Trim();
            if (settings.GroupPrefixInsideCategory && hasSmartGroup && smartGroup is not null)
            {
                targetRelativeFolder = Path.Combine(baseTarget, smartGroup.GroupFolderName);
            }
            else if (settings.EnableDateSubfolders)
            {
                targetRelativeFolder = Path.Combine(baseTarget, file.LastWriteTime.ToString("yyyy-MM"));
            }
            else
            {
                targetRelativeFolder = baseTarget;
            }

            finalMatchedRule = matchedKeywordRule.Name;
            finalIcon = matchedKeywordRule.IconGlyph;
            finalBadge = RuleBadgeType.Keyword;
        }
        // Ưu tiên 2: Gom cụm tên thông minh (Smart Grouping)
        else if (hasSmartGroup && smartGroup is not null)
        {
            var (categoryFolder, _, _, _) = ResolveLargeOrExtensionCategory(
                file,
                ext,
                fileNameNoExt,
                thresholdBytes,
                settings,
                activeRules);

            if (settings.GroupPrefixInsideCategory && !string.IsNullOrWhiteSpace(categoryFolder))
            {
                targetRelativeFolder = Path.Combine(categoryFolder, smartGroup.GroupFolderName);
            }
            else if (settings.PrefixGroupLocation == PrefixGroupLocationMode.DedicatedRootSubfolder &&
                     !string.IsNullOrWhiteSpace(settings.PrefixGroupRootFolderName))
            {
                targetRelativeFolder = Path.Combine(settings.PrefixGroupRootFolderName.Trim(), smartGroup.GroupFolderName);
            }
            else
            {
                targetRelativeFolder = smartGroup.GroupFolderName;
            }

            finalMatchedRule = smartGroup.MatchDescription;
            finalIcon = "\uE8B7";
            finalBadge = RuleBadgeType.SmartNameGroup;
        }
        else
        {
            // Ưu tiên 3 & 4: Tệp dung lượng lớn -> Quy tắc chỉ theo Đuôi tệp
            var (categoryFolder, ruleName, iconGlyph, badgeType) = ResolveLargeOrExtensionCategory(
                file,
                ext,
                fileNameNoExt,
                thresholdBytes,
                settings,
                activeRules);

            if (!string.IsNullOrWhiteSpace(categoryFolder))
            {
                targetRelativeFolder = settings.EnableDateSubfolders
                    ? Path.Combine(categoryFolder, file.LastWriteTime.ToString("yyyy-MM"))
                    : categoryFolder;

                finalMatchedRule = ruleName;
                finalIcon = iconGlyph;
                finalBadge = badgeType;
            }
            // Ưu tiên 5: Không khớp bất kỳ quy tắc nào
            else if (settings.MoveUnmatchedToOthers)
            {
                string othersName = string.IsNullOrWhiteSpace(settings.OthersFolderName) ? "Khác" : settings.OthersFolderName.Trim();
                targetRelativeFolder = settings.EnableDateSubfolders
                    ? Path.Combine(othersName, file.LastWriteTime.ToString("yyyy-MM"))
                    : othersName;

                finalMatchedRule = "Nhóm mặc định";
                finalIcon = "\uE8A5";
                finalBadge = RuleBadgeType.Others;
            }
            else if (settings.EnableDirectDateGrouping)
            {
                targetRelativeFolder = file.LastWriteTime.ToString("yyyy-MM");
                finalMatchedRule = $"Theo Năm-Tháng ({targetRelativeFolder})";
                finalIcon = "\uE787";
                finalBadge = RuleBadgeType.Extension;
            }
            else
            {
                return new FileMovePlanItem
                {
                    FileName = file.Name,
                    Extension = ext,
                    SourcePath = file.FullName,
                    ProposedTargetFolder = "(Giữ nguyên tại chỗ)",
                    ProposedDestinationPath = file.FullName,
                    FileSizeBytes = file.Length,
                    LastModified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
                    MatchedRuleName = "Không khớp quy tắc",
                    IconGlyph = "\uE8A5",
                    RuleBadgeType = RuleBadgeType.Others,
                    Status = PlanItemStatus.Skipped,
                    StatusText = "Giữ nguyên (không di chuyển)",
                    IsSelected = false
                };
            }
        }

        string proposedDestPath = Path.Combine(rootFolderPath, targetRelativeFolder, file.Name);

        return new FileMovePlanItem
        {
            FileName = file.Name,
            Extension = ext,
            SourcePath = file.FullName,
            ProposedTargetFolder = targetRelativeFolder,
            ProposedDestinationPath = proposedDestPath,
            FileSizeBytes = file.Length,
            LastModified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            MatchedRuleName = finalMatchedRule,
            IconGlyph = finalIcon,
            RuleBadgeType = finalBadge,
            Status = PlanItemStatus.Ready,
            StatusText = "Sẵn sàng di chuyển",
            IsSelected = true
        };
    }

    private static OrganizationRule? FindMatchingKeywordRule(
        string ext,
        string fileNameNoExt,
        SmartGroupingSettings settings,
        IReadOnlyList<OrganizationRule> activeRules)
    {
        foreach (OrganizationRule rule in activeRules)
        {
            if (rule.Keywords.Count == 0)
            {
                continue;
            }

            if (MatchesRule(rule, ext, fileNameNoExt, settings))
            {
                return rule;
            }
        }

        return null;
    }

    private static (string? Folder, string RuleName, string IconGlyph, RuleBadgeType Badge) ResolveLargeOrExtensionCategory(
        FileInfo file,
        string ext,
        string fileNameNoExt,
        long largeThresholdBytes,
        SmartGroupingSettings settings,
        IReadOnlyList<OrganizationRule> activeRules)
    {
        if (settings.EnableLargeFileIsolation && file.Length >= largeThresholdBytes)
        {
            string largeFolder = string.IsNullOrWhiteSpace(settings.LargeFilesFolderName)
                ? "Tệp dung lượng lớn"
                : settings.LargeFilesFolderName.Trim();
            return (largeFolder, $"Tệp lớn (>= {settings.LargeFileThresholdMB} MB)", "\uE7C3", RuleBadgeType.LargeFile);
        }

        if (settings.EnableExtensionRules)
        {
            foreach (OrganizationRule rule in activeRules)
            {
                if (rule.Keywords.Count > 0 || rule.Extensions.Count == 0)
                {
                    continue;
                }

                if (MatchesRule(rule, ext, fileNameNoExt, settings))
                {
                    return (rule.TargetFolderName.Trim(), rule.Name, rule.IconGlyph, RuleBadgeType.Extension);
                }
            }
        }

        return (null, string.Empty, "\uE8A5", RuleBadgeType.Others);
    }

    private static bool MatchesRule(
        OrganizationRule rule,
        string ext,
        string fileNameNoExt,
        SmartGroupingSettings settings)
    {
        if (string.IsNullOrWhiteSpace(rule.TargetFolderName))
        {
            return false;
        }

        bool hasExtensions = rule.Extensions.Count > 0;
        bool hasKeywords = rule.Keywords.Count > 0;

        if (!hasExtensions && !hasKeywords)
        {
            return false;
        }

        if (hasExtensions && hasKeywords)
        {
            if (!settings.EnableExtensionRules && !settings.EnableKeywordRules)
            {
                return false;
            }

            bool extMatches = rule.Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
            bool keywordMatches = rule.Keywords.Any(kw => fileNameNoExt.Contains(kw, StringComparison.OrdinalIgnoreCase));
            return extMatches && keywordMatches;
        }

        if (hasKeywords)
        {
            return settings.EnableKeywordRules &&
                   rule.Keywords.Any(kw => fileNameNoExt.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        return settings.EnableExtensionRules &&
               rule.Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    public Task<ExecutionResult> ExecutePlanAsync(
        string folderPath,
        IEnumerable<FileMovePlanItem> items,
        ConflictResolutionMode conflictMode,
        IProgress<OrganizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ExecutePlanInternal(folderPath, items, conflictMode, progress, cancellationToken), cancellationToken);
    }

    private ExecutionResult ExecutePlanInternal(
        string folderPath,
        IEnumerable<FileMovePlanItem> items,
        ConflictResolutionMode conflictMode,
        IProgress<OrganizationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var selected = items
            .Where(i => i.IsSelected && i.Status == PlanItemStatus.Ready)
            .ToList();

        var result = new ExecutionResult();
        var session = new OrganizationSession
        {
            SourceFolderPath = folderPath,
            ExecutedAt = DateTimeOffset.Now
        };

        var trackedCreatedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < selected.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileMovePlanItem item = selected[i];
            progress?.Report(new OrganizationProgress
            {
                ProcessedCount = i + 1,
                TotalCount = selected.Count,
                CurrentFileName = item.FileName
            });

            if (!File.Exists(item.SourcePath))
            {
                item.Status = PlanItemStatus.Skipped;
                item.StatusText = "Tệp nguồn không còn tồn tại";
                result.SkippedCount++;
                continue;
            }

            if (_safetyService.IsFileLocked(item.SourcePath))
            {
                item.Status = PlanItemStatus.LockedOrDownloading;
                item.StatusText = "Bị khóa trước khi di chuyển";
                result.SkippedCount++;
                result.Warnings.Add($"Bỏ qua tệp đang bị khóa: {item.FileName}");
                continue;
            }

            string? destDir = Path.GetDirectoryName(item.ProposedDestinationPath);
            if (string.IsNullOrWhiteSpace(destDir))
            {
                item.Status = PlanItemStatus.Failed;
                item.StatusText = "Đường dẫn đích không hợp lệ";
                result.FailedCount++;
                continue;
            }

            try
            {
                EnsureDirectoryTreeTracked(folderPath, destDir, trackedCreatedFolders);

                var (finalDestPath, shouldSkip, overwrite, note) = _safetyService.ResolveDestinationPath(
                    item.SourcePath,
                    item.ProposedDestinationPath,
                    conflictMode);

                if (shouldSkip || string.IsNullOrWhiteSpace(finalDestPath))
                {
                    item.Status = PlanItemStatus.Skipped;
                    item.StatusText = note;
                    result.SkippedCount++;
                    continue;
                }

                File.Move(item.SourcePath, finalDestPath, overwrite);

                item.Status = PlanItemStatus.Moved;
                item.StatusText = note;
                item.ProposedDestinationPath = finalDestPath;
                result.MovedCount++;

                session.Records.Add(new FileMoveRecord
                {
                    FileName = item.FileName,
                    OriginalPath = item.SourcePath,
                    DestinationPath = finalDestPath,
                    FileSizeBytes = item.FileSizeBytes,
                    MatchedRuleName = item.MatchedRuleName
                });
                session.TotalBytesMoved += item.FileSizeBytes;
            }
            catch (Exception ex)
            {
                item.Status = PlanItemStatus.Failed;
                item.StatusText = $"Lỗi: {ex.Message}";
                result.FailedCount++;
                result.Warnings.Add($"{item.FileName}: {ex.Message}");
            }
        }

        session.MovedFileCount = session.Records.Count;
        session.CreatedFolders = trackedCreatedFolders
            .OrderByDescending(p => p.Length)
            .ToList();

        if (session.Records.Count > 0)
        {
            result.Session = session;
        }

        return result;
    }

    public Task<UndoResult> UndoSessionAsync(OrganizationSession session, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => UndoSessionInternal(session, cancellationToken), cancellationToken);
    }

    private UndoResult UndoSessionInternal(OrganizationSession session, CancellationToken cancellationToken)
    {
        var undoResult = new UndoResult();

        if (session.IsUndone)
        {
            undoResult.Success = false;
            undoResult.Messages.Add("Phiên này đã được hoàn tác trước đó.");
            return undoResult;
        }

        for (int i = session.Records.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileMoveRecord record = session.Records[i];
            if (!File.Exists(record.DestinationPath))
            {
                undoResult.FailedOrMissingCount++;
                undoResult.Messages.Add($"Không tìm thấy tệp để khôi phục: {record.DestinationPath}");
                continue;
            }

            try
            {
                string? originalDir = Path.GetDirectoryName(record.OriginalPath);
                if (!string.IsNullOrWhiteSpace(originalDir) && !Directory.Exists(originalDir))
                {
                    Directory.CreateDirectory(originalDir);
                }

                var (finalOriginalPath, shouldSkip, overwrite, _) = _safetyService.ResolveDestinationPath(
                    record.DestinationPath,
                    record.OriginalPath,
                    ConflictResolutionMode.AutoRename);

                if (shouldSkip || string.IsNullOrWhiteSpace(finalOriginalPath))
                {
                    undoResult.FailedOrMissingCount++;
                    continue;
                }

                File.Move(record.DestinationPath, finalOriginalPath, overwrite);
                undoResult.RestoredCount++;
            }
            catch (Exception ex)
            {
                undoResult.FailedOrMissingCount++;
                undoResult.Messages.Add($"Lỗi khôi phục {record.FileName}: {ex.Message}");
            }
        }

        // Dọn dẹp các thư mục con rỗng mới tạo trong phiên (theo thứ tự từ sâu nhất ra ngoài)
        foreach (string folder in session.CreatedFolders.OrderByDescending(f => f.Length))
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder, false);
                }
            }
            catch
            {
                // Bỏ qua nếu thư mục đang được Explorer giữ khóa
            }
        }

        session.IsUndone = true;
        session.UndoneAt = DateTimeOffset.Now;
        undoResult.Success = undoResult.RestoredCount > 0 || undoResult.FailedOrMissingCount == 0;
        return undoResult;
    }

    private static void EnsureDirectoryTreeTracked(
        string rootFolderPath,
        string targetDirectory,
        HashSet<string> createdFolders)
    {
        if (Directory.Exists(targetDirectory))
        {
            return;
        }

        string normalizedRoot = Path.GetFullPath(rootFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string current = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var missingStack = new Stack<string>();
        while (!string.IsNullOrWhiteSpace(current) &&
               !Directory.Exists(current) &&
               current.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(current, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            missingStack.Push(current);
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent))
            {
                break;
            }
            current = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        while (missingStack.Count > 0)
        {
            string dirToCreate = missingStack.Pop();
            Directory.CreateDirectory(dirToCreate);
            createdFolders.Add(dirToCreate);
        }
    }
}
