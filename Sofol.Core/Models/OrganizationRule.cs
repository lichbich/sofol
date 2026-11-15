using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Sofol.Core.Models;

/// <summary>
/// Đại diện cho một quy tắc phân loại tệp vào thư mục đích.
/// </summary>
public sealed class OrganizationRule : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _iconGlyph = "\uE8B7";
    private string _targetFolderName = string.Empty;
    private List<string> _extensions = [];
    private List<string> _keywords = [];
    private int _priority = 100;
    private bool _isEnabled = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Mã Glyph FluentIcon (Segoe Fluent Icons) dùng để hiển thị trên UI WinUI 3.
    /// </summary>
    public string IconGlyph
    {
        get => _iconGlyph;
        set => SetProperty(ref _iconGlyph, value);
    }

    /// <summary>
    /// Tên thư mục đích tương đối bên trong thư mục được dọn dẹp (ví dụ: "Hình ảnh" hoặc "Tài liệu/PDF").
    /// </summary>
    public string TargetFolderName
    {
        get => _targetFolderName;
        set => SetProperty(ref _targetFolderName, value);
    }

    /// <summary>
    /// Danh sách phần mở rộng tệp (có dấu chấm, chữ thường, ví dụ: ".jpg", ".png").
    /// </summary>
    public List<string> Extensions
    {
        get => _extensions;
        set
        {
            if (SetProperty(ref _extensions, value))
            {
                OnPropertyChanged(nameof(ExtensionsDisplay));
            }
        }
    }

    /// <summary>
    /// Danh sách từ khóa xuất hiện trong tên tệp để tự động phân loại bất kể đuôi tệp.
    /// </summary>
    public List<string> Keywords
    {
        get => _keywords;
        set
        {
            if (SetProperty(ref _keywords, value))
            {
                OnPropertyChanged(nameof(KeywordsDisplay));
            }
        }
    }

    /// <summary>
    /// Thứ tự ưu tiên (số nhỏ hơn được ưu tiên kiểm tra trước).
    /// </summary>
    public int Priority
    {
        get => _priority;
        set => SetProperty(ref _priority, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string ExtensionsDisplay => Extensions.Count == 0
        ? "(Không giới hạn đuôi)"
        : string.Join(", ", Extensions);

    public string KeywordsDisplay => Keywords.Count == 0
        ? "(Không có từ khóa)"
        : string.Join(", ", Keywords);

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

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
