using System;
using System.Collections.Generic;
using System.IO;
using Sofol.Core.Models;

namespace Sofol.Core.Services;

/// <summary>
/// Kiểm tra trạng thái an toàn của tệp (đang tải dở, tệp tạm, tệp hệ thống, tệp đang bị khóa)
/// và xử lý xung đột tên tệp khi di chuyển.
/// </summary>
public sealed class FileLockAndSafetyService
{
    private static readonly HashSet<string> DownloadingExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload",
        ".part",
        ".tmp",
        ".temp",
        ".opdownload",
        ".download",
        ".partial",
        ".!ut",
        ".bc!"
    };

    private static readonly HashSet<string> IgnoredSystemFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini",
        "thumbs.db",
        ".ds_store",
        "icon\r"
    };

    public bool IsPassiveIgnoredFile(FileInfo fileInfo)
    {
        if (IgnoredSystemFiles.Contains(fileInfo.Name) ||
            DownloadingExtensions.Contains(fileInfo.Extension) ||
            fileInfo.Name.StartsWith("~$", StringComparison.OrdinalIgnoreCase) ||
            fileInfo.Name.StartsWith("~WRL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (fileInfo.Attributes & FileAttributes.Hidden) != 0 &&
               (fileInfo.Attributes & FileAttributes.System) != 0;
    }

    public (bool IsSafe, string SkipReason) CheckFileSafety(FileInfo fileInfo)
    {
        if (!fileInfo.Exists)
        {
            return (false, "Tệp không còn tồn tại");
        }

        string fileName = fileInfo.Name;
        string extension = fileInfo.Extension;

        if (IgnoredSystemFiles.Contains(fileName))
        {
            return (false, "Tệp cấu hình hệ thống (được bảo vệ)");
        }

        if (fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("~WRL", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Tệp tạm của Microsoft Office");
        }

        if (DownloadingExtensions.Contains(extension))
        {
            return (false, "Tệp đang tải dở từ trình duyệt");
        }

        if ((fileInfo.Attributes & FileAttributes.Hidden) != 0 &&
            (fileInfo.Attributes & FileAttributes.System) != 0)
        {
            return (false, "Tệp ẩn của hệ thống");
        }

        if (IsFileLocked(fileInfo.FullName))
        {
            return (false, "Tệp đang được mở hoặc sử dụng bởi ứng dụng khác");
        }

        return (true, string.Empty);
    }

    public bool IsFileLocked(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public (string? FinalDestinationPath, bool ShouldSkip, bool Overwrite, string Note) ResolveDestinationPath(
        string sourcePath,
        string desiredDestinationPath,
        ConflictResolutionMode mode)
    {
        if (string.Equals(sourcePath, desiredDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            return (null, true, false, "Đã nằm đúng thư mục đích");
        }

        if (!File.Exists(desiredDestinationPath))
        {
            return (desiredDestinationPath, false, false, "Đã di chuyển");
        }

        switch (mode)
        {
            case ConflictResolutionMode.Skip:
                return (null, true, false, "Trùng tên tại đích (đã bỏ qua)");

            case ConflictResolutionMode.OverwriteIfNewer:
                DateTime srcWrite = File.GetLastWriteTimeUtc(sourcePath);
                DateTime dstWrite = File.GetLastWriteTimeUtc(desiredDestinationPath);
                if (srcWrite > dstWrite)
                {
                    return (desiredDestinationPath, false, true, "Đã ghi đè bản cũ hơn");
                }
                return (null, true, false, "Tệp đích mới hơn hoặc giống hệt (đã bỏ qua)");

            case ConflictResolutionMode.AutoRename:
            default:
                string directory = Path.GetDirectoryName(desiredDestinationPath) ?? string.Empty;
                string nameWithoutExt = Path.GetFileNameWithoutExtension(desiredDestinationPath);
                string ext = Path.GetExtension(desiredDestinationPath);

                int index = 1;
                string candidate;
                do
                {
                    candidate = Path.Combine(directory, $"{nameWithoutExt} ({index}){ext}");
                    index++;
                } while (File.Exists(candidate));

                return (candidate, false, false, $"Đã đổi tên tránh trùng ({Path.GetFileName(candidate)})");
        }
    }
}
