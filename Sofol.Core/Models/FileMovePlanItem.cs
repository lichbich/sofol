using System;
using System.Collections.Generic;

namespace Sofol.Core.Models;

public enum RuleBadgeType
{
    SmartNameGroup = 0,
    Keyword = 1,
    Extension = 2,
    LargeFile = 3,
    Others = 4,
    LockedOrDownloading = 5
}

public enum PlanItemStatus
{
    Ready = 0,
    LockedOrDownloading = 1,
    Moved = 2,
    Skipped = 3,
    Failed = 4
}

/// <summary>
/// Đại diện cho một dòng dự kiến sắp xếp trên bảng Preview.
/// </summary>
public sealed class FileMovePlanItem
{
    public string FileName { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Tên thư mục đích tương đối (ví dụ: "Hình ảnh", "Gom theo tên\Book_Exported").
    /// </summary>
    public string ProposedTargetFolder { get; set; } = string.Empty;

    public string ProposedDestinationPath { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public string FormattedSize => FormatByteSize(FileSizeBytes);

    public DateTimeOffset LastModified { get; set; }

    public string LastModifiedDisplay => LastModified.LocalDateTime.ToString("dd/MM/yyyy HH:mm");

    public string MatchedRuleName { get; set; } = string.Empty;

    public string IconGlyph { get; set; } = "\uE8A5";

    public RuleBadgeType RuleBadgeType { get; set; }

    public PlanItemStatus Status { get; set; } = PlanItemStatus.Ready;

    public string StatusText { get; set; } = "Sẵn sàng";

    public bool IsSelected { get; set; } = true;

    public bool CanSelect => Status == PlanItemStatus.Ready;

    public static string FormatByteSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.##} MB";
        double gb = mb / 1024.0;
        return $"{gb:0.##} GB";
    }
}

public sealed class FolderAnalysisResult
{
    public string TargetFolderPath { get; set; } = string.Empty;

    public int TotalFilesScanned { get; set; }

    public int ExistingSubfoldersCount { get; set; }

    public int ReadyToMoveCount { get; set; }

    public int SmartGroupsCount { get; set; }

    public int DistinctDestinationFoldersCount { get; set; }

    public int SkippedOrLockedCount { get; set; }

    public long TotalReadyBytes { get; set; }

    public string FormattedTotalReadySize => FileMovePlanItem.FormatByteSize(TotalReadyBytes);

    public List<FileMovePlanItem> Items { get; set; } = [];
}

public sealed class OrganizationProgress
{
    public int ProcessedCount { get; set; }

    public int TotalCount { get; set; }

    public string CurrentFileName { get; set; } = string.Empty;

    public double Percentage => TotalCount == 0 ? 0 : (double)ProcessedCount / TotalCount * 100.0;
}

public sealed class ExecutionResult
{
    public OrganizationSession? Session { get; set; }

    public int MovedCount { get; set; }

    public int SkippedCount { get; set; }

    public int FailedCount { get; set; }

    public List<string> Warnings { get; set; } = [];
}
