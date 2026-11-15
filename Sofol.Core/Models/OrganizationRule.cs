using System;
using System.Collections.Generic;
using System.Linq;

namespace Sofol.Core.Models;

/// <summary>
/// Đại diện cho một quy tắc phân loại tệp vào thư mục đích.
/// </summary>
public sealed class OrganizationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mã Glyph FluentIcon (Segoe Fluent Icons) dùng để hiển thị trên UI WinUI 3.
    /// </summary>
    public string IconGlyph { get; set; } = "\uE8B7";

    /// <summary>
    /// Tên thư mục đích tương đối bên trong thư mục được dọn dẹp (ví dụ: "Hình ảnh" hoặc "Tài liệu/PDF").
    /// </summary>
    public string TargetFolderName { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách phần mở rộng tệp (có dấu chấm, chữ thường, ví dụ: ".jpg", ".png").
    /// </summary>
    public List<string> Extensions { get; set; } = [];

    /// <summary>
    /// Danh sách từ khóa xuất hiện trong tên tệp để tự động phân loại bất kể đuôi tệp.
    /// </summary>
    public List<string> Keywords { get; set; } = [];

    /// <summary>
    /// Thứ tự ưu tiên (số nhỏ hơn được ưu tiên kiểm tra trước).
    /// </summary>
    public int Priority { get; set; } = 100;

    public bool IsEnabled { get; set; } = true;

    public string ExtensionsDisplay => Extensions.Count == 0
        ? "(Không giới hạn đuôi)"
        : string.Join(", ", Extensions);

    public string KeywordsDisplay => Keywords.Count == 0
        ? "(Không có từ khóa)"
        : string.Join(", ", Keywords);

    public static List<string> ParseCommaSeparatedExtensions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split([',', ';', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ext => ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> ParseCommaSeparatedKeywords(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
