using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sofol.Core.Services;

namespace Sofol.Core.Models;

public enum FolderRuleMode
{
    InheritGlobal = 0,
    MediaByMonthPreset = 1,
    SmartClusterOnlyPreset = 2,
    CustomRules = 3
}

public readonly record struct WatcherDelayPreset(int Value, WatcherDelayUnit Unit, string DisplayText);

/// <summary>
/// Cấu hình theo dõi và bộ quy tắc độc lập cho một thư mục cụ thể.
/// </summary>
public sealed class WatchedFolderProfile
{
    public static readonly IReadOnlyList<WatcherDelayPreset> DelayPresets =
    [
        new(30, WatcherDelayUnit.Seconds, "Chờ 30 giây"),
        new(1, WatcherDelayUnit.Minutes, "Chờ 1 phút"),
        new(5, WatcherDelayUnit.Minutes, "Chờ 5 phút"),
        new(15, WatcherDelayUnit.Minutes, "Chờ 15 phút"),
        new(30, WatcherDelayUnit.Minutes, "Chờ 30 phút"),
        new(1, WatcherDelayUnit.Hours, "Chờ 1 giờ"),
        new(6, WatcherDelayUnit.Hours, "Chờ 6 giờ"),
        new(1, WatcherDelayUnit.Days, "Chờ 1 ngày"),
        new(3, WatcherDelayUnit.Days, "Chờ 3 ngày"),
        new(7, WatcherDelayUnit.Days, "Chờ 7 ngày"),
        new(30, WatcherDelayUnit.Days, "Chờ 30 ngày")
    ];

    public const int DefaultDelayPresetIndex = 7; // Chờ 1 ngày

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string FolderPath { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public FolderRuleMode RuleMode { get; set; } = FolderRuleMode.CustomRules;

    public bool UseCustomDelay { get; set; } = true;

    public int CustomDelayValue { get; set; } = 1;

    public WatcherDelayUnit CustomDelayUnit { get; set; } = WatcherDelayUnit.Days;

    public SmartGroupingSettings? CustomSmartSettings { get; set; }

    public List<OrganizationRule> CustomRules { get; set; } = [];

    public string GetDisplayName()
    {
        string normalized = NormalizePath(FolderPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Thư mục chưa đặt tên";
        }

        string leaf = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(leaf) ? normalized : leaf;
    }

    public string GetIconGlyph()
    {
        string name = GetDisplayName();
        if (name.Equals("Downloads", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE896";
        }

        if (name.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE7F4";
        }

        if (IsPicturesFolder(FolderPath))
        {
            return "\uE91B";
        }

        if (IsVideosFolder(FolderPath))
        {
            return "\uE714";
        }

        if (IsMusicFolder(FolderPath))
        {
            return "\uE8D6";
        }

        if (name.Equals("Documents", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE8A5";
        }

        return "\uE8B7";
    }

    public TimeSpan GetEffectiveDelayTimeSpan(SmartGroupingSettings globalSettings)
    {
        if (!UseCustomDelay)
        {
            return globalSettings.GetAutoWatcherDelayTimeSpan();
        }

        return SmartGroupingSettings.ComputeDelayTimeSpan(CustomDelayValue, CustomDelayUnit);
    }

    public string GetEffectiveDelayDisplayText(SmartGroupingSettings globalSettings)
    {
        if (!UseCustomDelay)
        {
            return globalSettings.GetAutoWatcherDelayDisplayText();
        }

        int val = Math.Max(1, CustomDelayValue);
        return $"{val} {SmartGroupingSettings.GetUnitDisplayName(CustomDelayUnit)}";
    }

    public int GetDelayPresetIndex()
    {
        int value = UseCustomDelay ? Math.Max(1, CustomDelayValue) : 1;
        WatcherDelayUnit unit = UseCustomDelay ? CustomDelayUnit : WatcherDelayUnit.Days;

        for (int i = 0; i < DelayPresets.Count; i++)
        {
            if (DelayPresets[i].Value == value && DelayPresets[i].Unit == unit)
            {
                return i;
            }
        }

        double targetSeconds = SmartGroupingSettings.ComputeDelayTimeSpan(value, unit).TotalSeconds;
        int closestIdx = DefaultDelayPresetIndex;
        double bestDiff = double.MaxValue;
        for (int i = 0; i < DelayPresets.Count; i++)
        {
            double presetSeconds = SmartGroupingSettings.ComputeDelayTimeSpan(DelayPresets[i].Value, DelayPresets[i].Unit).TotalSeconds;
            double diff = Math.Abs(presetSeconds - targetSeconds);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                closestIdx = i;
            }
        }

        return closestIdx;
    }

    public void ApplyDelayPresetIndex(int index)
    {
        int clamped = Math.Clamp(index, 0, DelayPresets.Count - 1);
        WatcherDelayPreset preset = DelayPresets[clamped];
        UseCustomDelay = true;
        CustomDelayValue = preset.Value;
        CustomDelayUnit = preset.Unit;
    }

    public static WatchedFolderProfile CreateForFolder(
        string folderPath,
        SmartGroupingSettings? templateSettings = null,
        IReadOnlyList<OrganizationRule>? templateRules = null)
    {
        string normalized = NormalizePath(folderPath);
        SmartGroupingSettings settings = (templateSettings ?? new SmartGroupingSettings()).Clone();
        settings.EnableKeywordRules = true;

        List<OrganizationRule> rules = templateRules is { Count: > 0 }
            ? templateRules.Select(r => r.Clone()).ToList()
            : ConfigAndHistoryStore.CreateDefaultRules();

        if (IsPicturesFolder(normalized))
        {
            foreach (OrganizationRule rule in rules)
            {
                if (rule.Id == "rule-images" ||
                    rule.TargetFolderName.Equals("Hình ảnh", StringComparison.OrdinalIgnoreCase))
                {
                    rule.IsEnabled = false;
                }
            }

            settings.EnableDateSubfolders = true;
            settings.EnableDirectDateGrouping = true;
            settings.PrefixGroupLocation = PrefixGroupLocationMode.DirectSubfolder;
        }
        else if (IsVideosFolder(normalized))
        {
            foreach (OrganizationRule rule in rules)
            {
                if (rule.Id == "rule-videos" ||
                    rule.TargetFolderName.Equals("Video", StringComparison.OrdinalIgnoreCase))
                {
                    rule.IsEnabled = false;
                }
            }

            settings.EnableDateSubfolders = true;
            settings.EnableDirectDateGrouping = true;
            settings.PrefixGroupLocation = PrefixGroupLocationMode.DirectSubfolder;
        }
        else if (IsMusicFolder(normalized))
        {
            foreach (OrganizationRule rule in rules)
            {
                if (rule.Id == "rule-audio" ||
                    rule.TargetFolderName.Equals("Âm thanh", StringComparison.OrdinalIgnoreCase))
                {
                    rule.IsEnabled = false;
                }
            }

            settings.PrefixGroupLocation = PrefixGroupLocationMode.DirectSubfolder;
        }
        else
        {
            settings.EnableDateSubfolders = false;
            settings.EnableDirectDateGrouping = false;
        }

        int delayVal = templateSettings is not null && templateSettings.AutoWatcherDelayValue > 0
            ? templateSettings.AutoWatcherDelayValue
            : 1;
        WatcherDelayUnit delayUnit = templateSettings is not null
            ? templateSettings.AutoWatcherDelayUnit
            : WatcherDelayUnit.Days;

        return new WatchedFolderProfile
        {
            FolderPath = normalized,
            IsEnabled = true,
            RuleMode = FolderRuleMode.CustomRules,
            UseCustomDelay = true,
            CustomDelayValue = delayVal,
            CustomDelayUnit = delayUnit,
            CustomSmartSettings = settings,
            CustomRules = rules
        };
    }

    public void EnsureStandaloneInitialized(
        SmartGroupingSettings fallbackSettings,
        IReadOnlyList<OrganizationRule> fallbackRules)
    {
        bool needsSettingsOrRules = CustomSmartSettings is null || CustomRules is null || CustomRules.Count == 0;

        if (RuleMode != FolderRuleMode.CustomRules || needsSettingsOrRules)
        {
            if (RuleMode == FolderRuleMode.MediaByMonthPreset)
            {
                WatchedFolderProfile created = CreateForFolder(FolderPath, fallbackSettings, fallbackRules);
                CustomSmartSettings = created.CustomSmartSettings;
                CustomRules = created.CustomRules;
                if (CustomSmartSettings is not null)
                {
                    CustomSmartSettings.EnableDateSubfolders = true;
                    CustomSmartSettings.EnableDirectDateGrouping = true;
                }
            }
            else if (RuleMode == FolderRuleMode.SmartClusterOnlyPreset)
            {
                SmartGroupingSettings s = (CustomSmartSettings ?? fallbackSettings).Clone();
                s.EnableExtensionRules = false;
                s.EnablePrefixSimilarityGrouping = true;
                s.PrefixGroupLocation = PrefixGroupLocationMode.DirectSubfolder;
                s.GroupPrefixInsideCategory = false;
                s.EnableDateSubfolders = false;
                s.EnableDirectDateGrouping = false;
                s.EnableKeywordRules = true;
                CustomSmartSettings = s;

                List<OrganizationRule> rules = (CustomRules is { Count: > 0 } ? CustomRules : fallbackRules)
                    .Select(r => r.Clone())
                    .ToList();
                foreach (OrganizationRule r in rules)
                {
                    if (r.Keywords.Count == 0)
                    {
                        r.IsEnabled = false;
                    }
                }
                CustomRules = rules;
            }
            else
            {
                CustomSmartSettings ??= fallbackSettings.Clone();
                if (CustomRules is null || CustomRules.Count == 0)
                {
                    CustomRules = (fallbackRules.Count > 0 ? fallbackRules : ConfigAndHistoryStore.CreateDefaultRules())
                        .Select(r => r.Clone())
                        .ToList();
                }
            }
        }

        if (!UseCustomDelay)
        {
            CustomDelayValue = Math.Max(1, fallbackSettings.AutoWatcherDelayValue);
            CustomDelayUnit = fallbackSettings.AutoWatcherDelayUnit;
            UseCustomDelay = true;
        }

        if (CustomSmartSettings is not null)
        {
            CustomSmartSettings.EnableKeywordRules = true;
        }

        RuleMode = FolderRuleMode.CustomRules;
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            string full = Path.GetFullPath(path.Trim());
            string? root = Path.GetPathRoot(full);
            if (!string.IsNullOrWhiteSpace(root) &&
                string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public static bool IsRootOrSystemDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        try
        {
            string full = Path.GetFullPath(path.Trim());
            string? root = Path.GetPathRoot(full);
            if (!string.IsNullOrWhiteSpace(root) &&
                string.Equals(
                    full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string windowsDir = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            string normalized = NormalizePath(full);
            if (!string.IsNullOrWhiteSpace(windowsDir) &&
                (normalized.Equals(windowsDir, StringComparison.OrdinalIgnoreCase) ||
                 normalized.StartsWith(windowsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    public static FolderRuleMode DetectRecommendedRuleMode(string folderPath)
    {
        if (IsPicturesFolder(folderPath) || IsVideosFolder(folderPath) || IsMusicFolder(folderPath))
        {
            return FolderRuleMode.MediaByMonthPreset;
        }

        return FolderRuleMode.InheritGlobal;
    }

    public static bool IsPicturesFolder(string? folderPath)
    {
        string normalized = NormalizePath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        string pictures = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        if (!string.IsNullOrWhiteSpace(pictures) && normalized.Equals(pictures, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string leaf = Path.GetFileName(normalized);
        return leaf.Equals("Pictures", StringComparison.OrdinalIgnoreCase) ||
               leaf.Equals("Screenshots", StringComparison.OrdinalIgnoreCase) ||
               leaf.Equals("Camera Roll", StringComparison.OrdinalIgnoreCase) ||
               leaf.Equals("Saved Pictures", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsVideosFolder(string? folderPath)
    {
        string normalized = NormalizePath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        string videos = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        if (!string.IsNullOrWhiteSpace(videos) && normalized.Equals(videos, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string leaf = Path.GetFileName(normalized);
        return leaf.Equals("Videos", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMusicFolder(string? folderPath)
    {
        string normalized = NormalizePath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        string music = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        if (!string.IsNullOrWhiteSpace(music) && normalized.Equals(music, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string leaf = Path.GetFileName(normalized);
        return leaf.Equals("Music", StringComparison.OrdinalIgnoreCase);
    }
}
