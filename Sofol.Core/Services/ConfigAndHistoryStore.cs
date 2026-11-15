using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sofol.Core.Models;

namespace Sofol.Core.Services;

/// <summary>
/// Quản lý lưu trữ bền vững cấu hình quy tắc (config.json) và lịch sử hoàn tác (history.json).
/// </summary>
public sealed class ConfigAndHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _storageDirectory;
    private readonly string _configPath;
    private readonly string _historyPath;

    public ConfigAndHistoryStore(string? customStorageDirectory = null)
    {
        _storageDirectory = string.IsNullOrWhiteSpace(customStorageDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sofol")
            : customStorageDirectory;

        _configPath = Path.Combine(_storageDirectory, "config.json");
        _historyPath = Path.Combine(_storageDirectory, "history.json");
    }

    public string StorageDirectory => _storageDirectory;

    public AppConfiguration LoadConfiguration()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                string json = File.ReadAllText(_configPath);
                AppConfiguration? loaded = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.SmartSettings ??= new SmartGroupingSettings();
                    if (loaded.Rules is null || loaded.Rules.Count == 0)
                    {
                        loaded.Rules = CreateDefaultRules();
                    }

                    if (loaded.ConfigSchemaVersion < 2)
                    {
                        loaded.SmartSettings.MoveUnmatchedToOthers = false;
                        foreach (OrganizationRule rule in loaded.Rules)
                        {
                            if (rule.Id.StartsWith("rule-", StringComparison.Ordinal))
                            {
                                rule.Keywords.Clear();
                            }
                        }
                    }

                    if (loaded.ConfigSchemaVersion < 3)
                    {
                        if (loaded.SmartSettings.AutoWatcherDelaySeconds > 0 &&
                            loaded.SmartSettings.AutoWatcherDelayValue == 10 &&
                            loaded.SmartSettings.AutoWatcherDelayUnit == WatcherDelayUnit.Seconds)
                        {
                            loaded.SmartSettings.AutoWatcherDelayValue = loaded.SmartSettings.AutoWatcherDelaySeconds;
                        }
                    }

                    if (loaded.ConfigSchemaVersion < AppConfiguration.CurrentConfigSchemaVersion)
                    {
                        loaded.ConfigSchemaVersion = AppConfiguration.CurrentConfigSchemaVersion;
                        SaveConfiguration(loaded);
                    }

                    return loaded;
                }
            }
        }
        catch
        {
            // Nếu tệp cấu hình bị hỏng, tạo mới cấu hình mặc định
        }

        AppConfiguration defaults = CreateDefaultConfiguration();
        SaveConfiguration(defaults);
        return defaults;
    }

    public void SaveConfiguration(AppConfiguration configuration)
    {
        Directory.CreateDirectory(_storageDirectory);
        string json = JsonSerializer.Serialize(configuration, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    public List<OrganizationSession> LoadHistory()
    {
        try
        {
            if (File.Exists(_historyPath))
            {
                string json = File.ReadAllText(_historyPath);
                List<OrganizationSession>? sessions = JsonSerializer.Deserialize<List<OrganizationSession>>(json, JsonOptions);
                if (sessions is not null)
                {
                    return sessions.OrderByDescending(s => s.ExecutedAt).ToList();
                }
            }
        }
        catch
        {
            // Trả về danh sách rỗng nếu chưa đọc được
        }

        return [];
    }

    public void SaveHistory(IReadOnlyList<OrganizationSession> sessions)
    {
        Directory.CreateDirectory(_storageDirectory);
        // Giữ tối đa 100 phiên gần nhất để tệp lịch sử luôn gọn nhẹ
        var trimmed = sessions.OrderByDescending(s => s.ExecutedAt).Take(100).ToList();
        string json = JsonSerializer.Serialize(trimmed, JsonOptions);
        File.WriteAllText(_historyPath, json);
    }

    public List<OrganizationSession> AddSession(OrganizationSession session)
    {
        List<OrganizationSession> history = LoadHistory();
        history.Insert(0, session);
        SaveHistory(history);
        return history;
    }

    public List<OrganizationSession> UpdateSession(OrganizationSession updatedSession)
    {
        List<OrganizationSession> history = LoadHistory();
        int idx = history.FindIndex(s => s.Id == updatedSession.Id);
        if (idx >= 0)
        {
            history[idx] = updatedSession;
        }
        else
        {
            history.Insert(0, updatedSession);
        }

        SaveHistory(history);
        return history;
    }

    public static AppConfiguration CreateDefaultConfiguration()
    {
        string downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        return new AppConfiguration
        {
            ConfigSchemaVersion = AppConfiguration.CurrentConfigSchemaVersion,
            LastTargetFolder = Directory.Exists(downloadsPath) ? downloadsPath : string.Empty,
            SmartSettings = new SmartGroupingSettings(),
            Rules = CreateDefaultRules()
        };
    }

    public static List<OrganizationRule> CreateDefaultRules()
    {
        return
        [
            new OrganizationRule
            {
                Id = "rule-images",
                Name = "Hình ảnh",
                IconGlyph = "\uE91B",
                TargetFolderName = "Hình ảnh",
                Extensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".svg", ".ico", ".heic", ".tiff", ".raw"],
                Keywords = [],
                Priority = 10,
                IsEnabled = true
            },
            new OrganizationRule
            {
                Id = "rule-documents",
                Name = "Tài liệu",
                IconGlyph = "\uE8A5",
                TargetFolderName = "Tài liệu",
                Extensions = [".docx", ".doc", ".pdf", ".txt", ".rtf", ".odt", ".hwp", ".md", ".epub"],
                Keywords = [],
                Priority = 20,
                IsEnabled = true
            },
            new OrganizationRule
            {
                Id = "rule-spreadsheets",
                Name = "Bảng tính & Trình chiếu",
                IconGlyph = "\uE9F9",
                TargetFolderName = "Bảng tính & Trình chiếu",
                Extensions = [".xlsx", ".xls", ".csv", ".pptx", ".ppt", ".ods"],
                Keywords = [],
                Priority = 30,
                IsEnabled = true
            },
            new OrganizationRule
            {
                Id = "rule-installers",
                Name = "Bộ cài & Ứng dụng",
                IconGlyph = "\uE74C",
                TargetFolderName = "Bộ cài & Ứng dụng",
                Extensions = [".exe", ".msi", ".msix", ".appx", ".apk", ".dmg", ".iso"],
                Keywords = [],
                Priority = 40,
                IsEnabled = true
            },
            new OrganizationRule
            {
                Id = "rule-archives",
                Name = "Tệp nén",
                IconGlyph = "\uF012",
                TargetFolderName = "Tệp nén",
                Extensions = [".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz"],
                Keywords = [],
                Priority = 50,
                IsEnabled = true
            },
            new OrganizationRule
            {
                Id = "rule-videos",
                Name = "Video",
                IconGlyph = "\uE714",
                TargetFolderName = "Video",
                Extensions = [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv"],
                Keywords = [],
                Priority = 60,
                IsEnabled = true
            },
            new OrganizationRule
            {
                Id = "rule-audio",
                Name = "Âm thanh",
                IconGlyph = "\uE8D6",
                TargetFolderName = "Âm thanh",
                Extensions = [".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a"],
                Keywords = [],
                Priority = 70,
                IsEnabled = true
            },
            new OrganizationRule
            {
                Id = "rule-devdata",
                Name = "Dữ liệu & Mã nguồn",
                IconGlyph = "\uE943",
                TargetFolderName = "Dữ liệu & Mã nguồn",
                Extensions = [".json", ".xml", ".yaml", ".yml", ".sql", ".cs", ".py", ".js", ".ts", ".html", ".css", ".log"],
                Keywords = [],
                Priority = 80,
                IsEnabled = true
            }
        ];
    }
}
