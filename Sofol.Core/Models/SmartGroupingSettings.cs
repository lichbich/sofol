using System;
using System.Collections.Generic;

namespace Sofol.Core.Models;

public enum ConflictResolutionMode
{
    AutoRename = 0,
    Skip = 1,
    OverwriteIfNewer = 2
}

public enum AppThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}

public enum WatcherDelayUnit
{
    Seconds = 0,
    Minutes = 1,
    Hours = 2,
    Days = 3,
    Months = 4,
    Years = 5
}

public enum PrefixGroupLocationMode
{
    /// <summary>
    /// Tạo thư mục gom cụm nằm dưới thư mục "Gom theo tên\&lt;Tên_Cụm&gt;".
    /// </summary>
    DedicatedRootSubfolder = 0,

    /// <summary>
    /// Tạo thư mục "&lt;Tên_Cụm&gt;" trực tiếp trong thư mục đang dọn dẹp.
    /// </summary>
    DirectSubfolder = 1
}

/// <summary>
/// Các thiết lập thông minh cho quá trình gom nhóm, độ an toàn và tự động hóa.
/// </summary>
public sealed class SmartGroupingSettings
{
    public bool EnableExtensionRules { get; set; } = true;

    public bool EnableKeywordRules { get; set; } = true;

    public bool EnablePrefixSimilarityGrouping { get; set; } = true;

    public int MinFilesForPrefixGroup { get; set; } = 2;

    public double SimilarityThreshold { get; set; } = 0.80;

    public bool GroupPrefixInsideCategory { get; set; } = false;

    public PrefixGroupLocationMode PrefixGroupLocation { get; set; } = PrefixGroupLocationMode.DedicatedRootSubfolder;

    public string PrefixGroupRootFolderName { get; set; } = "Gom theo tên";

    public bool EnableDateSubfolders { get; set; } = false;

    public bool EnableLargeFileIsolation { get; set; } = false;

    public long LargeFileThresholdMB { get; set; } = 500;

    public string LargeFilesFolderName { get; set; } = "Tệp dung lượng lớn";

    public bool MoveUnmatchedToOthers { get; set; } = false;

    public string OthersFolderName { get; set; } = "Khác";

    public ConflictResolutionMode ConflictBehavior { get; set; } = ConflictResolutionMode.AutoRename;

    public bool IgnoreSubfolders { get; set; } = true;

    public bool EnableAutoWatcher { get; set; } = false;

    public int AutoWatcherDelaySeconds { get; set; } = 86400;

    public int AutoWatcherDelayValue { get; set; } = 1;

    public WatcherDelayUnit AutoWatcherDelayUnit { get; set; } = WatcherDelayUnit.Days;

    public AppThemePreference AppTheme { get; set; } = AppThemePreference.System;

    public TimeSpan GetAutoWatcherDelayTimeSpan()
    {
        int value = Math.Max(1, AutoWatcherDelayValue);
        return AutoWatcherDelayUnit switch
        {
            WatcherDelayUnit.Minutes => TimeSpan.FromMinutes(value),
            WatcherDelayUnit.Hours => TimeSpan.FromHours(value),
            WatcherDelayUnit.Days => TimeSpan.FromDays(value),
            WatcherDelayUnit.Months => TimeSpan.FromDays(value * 30.0),
            WatcherDelayUnit.Years => TimeSpan.FromDays(value * 365.0),
            _ => TimeSpan.FromSeconds(value)
        };
    }

    public string GetAutoWatcherDelayDisplayText()
    {
        int value = Math.Max(1, AutoWatcherDelayValue);
        return $"{value} {GetUnitDisplayName(AutoWatcherDelayUnit)}";
    }

    public static string GetUnitDisplayName(WatcherDelayUnit unit) => unit switch
    {
        WatcherDelayUnit.Minutes => "phút",
        WatcherDelayUnit.Hours => "giờ",
        WatcherDelayUnit.Days => "ngày",
        WatcherDelayUnit.Months => "tháng",
        WatcherDelayUnit.Years => "năm",
        _ => "giây"
    };
}

/// <summary>
/// Toàn bộ cấu hình ứng dụng được lưu bền vững.
/// </summary>
public sealed class AppConfiguration
{
    public const int CurrentConfigSchemaVersion = 4;

    public int ConfigSchemaVersion { get; set; } = 1;

    public string LastTargetFolder { get; set; } = string.Empty;

    public SmartGroupingSettings SmartSettings { get; set; } = new();

    public List<OrganizationRule> Rules { get; set; } = [];

    public string GitHubRepository { get; set; } = "lichbich/sofol";

    public bool AutoCheckUpdatesOnStartup { get; set; } = true;
}
