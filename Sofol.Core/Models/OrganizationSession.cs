using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace Sofol.Core.Models;

/// <summary>
/// Lưu trữ thông tin một tệp đã được di chuyển để phục vụ hoàn tác.
/// </summary>
public sealed class FileMoveRecord
{
    public string FileName { get; set; } = string.Empty;

    public string OriginalPath { get; set; } = string.Empty;

    public string DestinationPath { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public string MatchedRuleName { get; set; } = string.Empty;

    [JsonIgnore]
    public string FormattedSize => FileMovePlanItem.FormatByteSize(FileSizeBytes);

    [JsonIgnore]
    public string DestinationFileName => string.IsNullOrWhiteSpace(DestinationPath)
        ? FileName
        : Path.GetFileName(DestinationPath);

    [JsonIgnore]
    public bool WasRenamed => !string.Equals(FileName, DestinationFileName, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string RelativeDestinationFolder
    {
        get
        {
            string? sourceDir = Path.GetDirectoryName(OriginalPath);
            string? destDir = Path.GetDirectoryName(DestinationPath);
            if (string.IsNullOrWhiteSpace(destDir))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(sourceDir))
            {
                return destDir;
            }

            string rel = Path.GetRelativePath(sourceDir, destDir);
            return rel == "." ? "(Thư mục gốc)" : rel;
        }
    }

    [JsonIgnore]
    public string RelativeDestinationPath
    {
        get
        {
            string? sourceDir = Path.GetDirectoryName(OriginalPath);
            if (string.IsNullOrWhiteSpace(DestinationPath))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(sourceDir)
                ? DestinationPath
                : Path.GetRelativePath(sourceDir, DestinationPath);
        }
    }
}

/// <summary>
/// Lưu trữ toàn bộ thông tin một phiên dọn dẹp để người dùng có thể Hoàn tác (Undo) về nguyên trạng.
/// </summary>
public sealed class OrganizationSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset ExecutedAt { get; set; } = DateTimeOffset.Now;

    public string ExecutedAtDisplay => ExecutedAt.LocalDateTime.ToString("dd/MM/yyyy HH:mm:ss");

    public string SourceFolderPath { get; set; } = string.Empty;

    public int MovedFileCount { get; set; }

    public long TotalBytesMoved { get; set; }

    public string FormattedTotalSize => FileMovePlanItem.FormatByteSize(TotalBytesMoved);

    public bool IsUndone { get; set; }

    public DateTimeOffset? UndoneAt { get; set; }

    public string StatusDisplay => IsUndone
        ? $"Đã hoàn tác ({UndoneAt?.LocalDateTime:dd/MM/yyyy HH:mm})"
        : "Có thể hoàn tác";

    public List<string> CreatedFolders { get; set; } = [];

    public List<FileMoveRecord> Records { get; set; } = [];

    [JsonIgnore]
    public int CreatedFoldersCount => CreatedFolders.Count;

    [JsonIgnore]
    public List<string> RelativeCreatedFolders => CreatedFolders
        .Select(folder => string.IsNullOrWhiteSpace(SourceFolderPath)
            ? folder
            : Path.GetRelativePath(SourceFolderPath, folder))
        .Where(rel => !string.IsNullOrWhiteSpace(rel) && rel != ".")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(rel => rel, StringComparer.OrdinalIgnoreCase)
        .ToList();

    [JsonIgnore]
    public string RuleBreakdownSummary
    {
        get
        {
            if (Records.Count == 0)
            {
                return "Không có dữ liệu chi tiết tệp";
            }

            var groups = Records
                .GroupBy(
                    r => string.IsNullOrWhiteSpace(r.MatchedRuleName) ? "Khác" : r.MatchedRuleName.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key} ({g.Count()})");

            return string.Join(" • ", groups);
        }
    }
}

public sealed class UndoResult
{
    public bool Success { get; set; }

    public int RestoredCount { get; set; }

    public int FailedOrMissingCount { get; set; }

    public List<string> Messages { get; set; } = [];
}
