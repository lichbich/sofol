using System;
using System.Collections.Generic;
using System.Linq;

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

    public bool EnableDirectDateGrouping { get; set; } = false;

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

    public bool ShowWindowOnWindowsStartup { get; set; } = false;

    public SmartGroupingSettings Clone() => new()
    {
        EnableExtensionRules = EnableExtensionRules,
        EnableKeywordRules = EnableKeywordRules,
        EnablePrefixSimilarityGrouping = EnablePrefixSimilarityGrouping,
        MinFilesForPrefixGroup = MinFilesForPrefixGroup,
        SimilarityThreshold = SimilarityThreshold,
        GroupPrefixInsideCategory = GroupPrefixInsideCategory,
        PrefixGroupLocation = PrefixGroupLocation,
        PrefixGroupRootFolderName = PrefixGroupRootFolderName,
        EnableDateSubfolders = EnableDateSubfolders,
        EnableDirectDateGrouping = EnableDirectDateGrouping,
        EnableLargeFileIsolation = EnableLargeFileIsolation,
        LargeFileThresholdMB = LargeFileThresholdMB,
        LargeFilesFolderName = LargeFilesFolderName,
        MoveUnmatchedToOthers = MoveUnmatchedToOthers,
        OthersFolderName = OthersFolderName,
        ConflictBehavior = ConflictBehavior,
        IgnoreSubfolders = IgnoreSubfolders,
        EnableAutoWatcher = EnableAutoWatcher,
        AutoWatcherDelaySeconds = AutoWatcherDelaySeconds,
        AutoWatcherDelayValue = AutoWatcherDelayValue,
        AutoWatcherDelayUnit = AutoWatcherDelayUnit,
        AppTheme = AppTheme,
        ShowWindowOnWindowsStartup = ShowWindowOnWindowsStartup
    };

    public TimeSpan GetAutoWatcherDelayTimeSpan() =>
        ComputeDelayTimeSpan(AutoWatcherDelayValue, AutoWatcherDelayUnit);

    public static TimeSpan ComputeDelayTimeSpan(int rawValue, WatcherDelayUnit unit)
    {
        int value = Math.Max(1, rawValue);
        return unit switch
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
    public const int CurrentConfigSchemaVersion = 6;

    public int ConfigSchemaVersion { get; set; } = 1;

    public string LastTargetFolder { get; set; } = string.Empty;

    public SmartGroupingSettings SmartSettings { get; set; } = new();

    public List<OrganizationRule> Rules { get; set; } = [];

    public List<WatchedFolderProfile> WatchedFolders { get; set; } = [];

    public string GitHubRepository { get; set; } = "lichbich/sofol";

    public bool AutoCheckUpdatesOnStartup { get; set; } = true;

    public WatchedFolderProfile? FindWatchedFolder(string? folderPath)
    {
        string normalized = WatchedFolderProfile.NormalizePath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return WatchedFolders.FirstOrDefault(f =>
            string.Equals(
                WatchedFolderProfile.NormalizePath(f.FolderPath),
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    public AppConfiguration BuildEffectiveConfigurationForFolder(string? folderPath)
    {
        WatchedFolderProfile? profile = FindWatchedFolder(folderPath);
        if (profile?.CustomSmartSettings is null)
        {
            return this;
        }

        return new AppConfiguration
        {
            ConfigSchemaVersion = ConfigSchemaVersion,
            LastTargetFolder = profile.FolderPath,
            SmartSettings = profile.CustomSmartSettings.Clone(),
            Rules = profile.CustomRules,
            WatchedFolders = WatchedFolders,
            GitHubRepository = GitHubRepository,
            AutoCheckUpdatesOnStartup = AutoCheckUpdatesOnStartup
        };
    }
}
