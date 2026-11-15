using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sofol.Core.Models;
using Sofol.Core.Services;

namespace Sofol.Tests;

public sealed class FileOrganizerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileOrganizerEngine _engine = new();

    public FileOrganizerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "SofolTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task AnalyzeFolder_GroupsExtensionsAndSmartNameClusters_AndSkipsDownloadingFile()
    {
        string sandbox = DemoSandboxGenerator.CreateSampleMessyFolder(Path.Combine(_tempRoot, "Downloads"));
        AppConfiguration config = ConfigAndHistoryStore.CreateDefaultConfiguration();

        FolderAnalysisResult analysis = await _engine.AnalyzeFolderAsync(sandbox, config);

        Assert.Equal(2, analysis.ExistingSubfoldersCount);
        Assert.True(analysis.TotalFilesScanned >= 20);

        // Kiểm tra file .crdownload bị đánh dấu LockedOrDownloading và không được chọn
        FileMovePlanItem? crDownload = analysis.Items.FirstOrDefault(i => i.FileName.EndsWith(".crdownload"));
        Assert.NotNull(crDownload);
        Assert.Equal(PlanItemStatus.LockedOrDownloading, crDownload!.Status);
        Assert.False(crDownload.IsSelected);

        // Kiểm tra cụm Bao_cao_doanh_thu
        var reportItems = analysis.Items
            .Where(i => i.FileName.StartsWith("Bao_cao_doanh_thu", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(3, reportItems.Count);
        Assert.All(reportItems, item =>
        {
            Assert.Equal(RuleBadgeType.SmartNameGroup, item.RuleBadgeType);
            Assert.Equal(Path.Combine("Gom theo tên", "Bao_cao_doanh_thu"), item.ProposedTargetFolder);
        });

        // Kiểm tra cụm Hop_dong_dich_vu_2026 (.pdf và .docx)
        var contractGroup = analysis.Items
            .Where(i => i.FileName.StartsWith("Hop_dong_dich_vu_2026", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(3, contractGroup.Count);
        Assert.All(contractGroup, item => Assert.Equal(RuleBadgeType.SmartNameGroup, item.RuleBadgeType));
        Assert.Equal(contractGroup[0].ProposedTargetFolder, contractGroup[1].ProposedTargetFolder);

        // Kiểm tra cụm tên tương đồng (Hoa_don_tien_dien_thang_09 & Hoa_don_tien_dien_thang_10)
        var invoicePair = analysis.Items
            .Where(i => i.FileName.StartsWith("Hoa_don_tien_dien_thang_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(2, invoicePair.Count);
        Assert.All(invoicePair, item => Assert.Equal(RuleBadgeType.SmartNameGroup, item.RuleBadgeType));

        // Kiểm tra phân loại các định dạng phổ biến
        Assert.Equal("Hình ảnh", GetItem(analysis, "Screenshot_2026_11_15.png").ProposedTargetFolder);
        Assert.Equal("Hình ảnh", GetItem(analysis, "Avatar_mau_thiet_ke.webp").ProposedTargetFolder);
        Assert.Equal("Hình ảnh", GetItem(analysis, "Hinh_nen_thien_nhien_4k.jpg").ProposedTargetFolder);
        Assert.Equal("Bộ cài & Ứng dụng", GetItem(analysis, "Setup_PhanMemTienIch_v2.0.exe").ProposedTargetFolder);
        Assert.Equal("Tệp nén", GetItem(analysis, "Du_lieu_sao_luu_he_thong.zip").ProposedTargetFolder);
        Assert.Equal("Dữ liệu & Mã nguồn", GetItem(analysis, "Cau_hinh_ung_dung_mau.json").ProposedTargetFolder);
        Assert.Equal("Video", GetItem(analysis, "Video_huong_dan_cai_dat.mp4").ProposedTargetFolder);
        Assert.Equal("Âm thanh", GetItem(analysis, "Nhac_khong_loi_thu_gian.mp3").ProposedTargetFolder);
        Assert.Equal("Bảng tính & Trình chiếu", GetItem(analysis, "Danh_sach_nhan_su_mau.xlsx").ProposedTargetFolder);
        Assert.Equal("Tài liệu", GetItem(analysis, "Ke_hoach_cong_tac_nam_2026.docx").ProposedTargetFolder);
    }

    [Fact]
    public async Task ExecutePlanAndUndo_RestoresAllFilesAndCleansCreatedFolders()
    {
        string sandbox = DemoSandboxGenerator.CreateSampleMessyFolder(Path.Combine(_tempRoot, "UndoCase"));
        AppConfiguration config = ConfigAndHistoryStore.CreateDefaultConfiguration();

        FolderAnalysisResult before = await _engine.AnalyzeFolderAsync(sandbox, config);
        ExecutionResult exec = await _engine.ExecutePlanAsync(
            sandbox,
            before.Items,
            ConflictResolutionMode.AutoRename);

        Assert.NotNull(exec.Session);
        Assert.Equal(before.ReadyToMoveCount, exec.MovedCount);
        Assert.Equal(0, exec.FailedCount);

        // File đang tải dở vẫn phải nằm ở gốc
        Assert.True(File.Exists(Path.Combine(sandbox, "Goi_du_lieu_lon_dang_tai.zip.crdownload")));
        // Thư mục con có sẵn không bị ảnh hưởng
        Assert.True(Directory.Exists(Path.Combine(sandbox, "Thu_muc_cu")));
        Assert.True(Directory.Exists(Path.Combine(sandbox, "Du_an_mau")));
        // File đã được dời vào đúng thư mục đích
        Assert.True(File.Exists(Path.Combine(sandbox, "Gom theo tên", "Bao_cao_doanh_thu", "Bao_cao_doanh_thu (1).xlsx")));
        Assert.True(File.Exists(Path.Combine(sandbox, "Tệp nén", "Du_lieu_sao_luu_he_thong.zip")));

        // Thực hiện Hoàn tác
        UndoResult undo = await _engine.UndoSessionAsync(exec.Session!);

        Assert.True(undo.Success);
        Assert.Equal(exec.MovedCount, undo.RestoredCount);
        Assert.True(exec.Session!.IsUndone);

        // File đã quay lại gốc
        Assert.True(File.Exists(Path.Combine(sandbox, "Bao_cao_doanh_thu (1).xlsx")));
        Assert.True(File.Exists(Path.Combine(sandbox, "Du_lieu_sao_luu_he_thong.zip")));
        // Thư mục rỗng mới tạo đã được dọn sạch
        Assert.False(Directory.Exists(Path.Combine(sandbox, "Tệp nén")));
        Assert.False(Directory.Exists(Path.Combine(sandbox, "Gom theo tên")));
        // Thư mục có sẵn vẫn còn nguyên
        Assert.True(Directory.Exists(Path.Combine(sandbox, "Thu_muc_cu")));
        Assert.True(Directory.Exists(Path.Combine(sandbox, "Du_an_mau")));
    }

    [Fact]
    public async Task ExecutePlan_HandlesFileConflictWithAutoRename()
    {
        string folder = Path.Combine(_tempRoot, "ConflictCase");
        Directory.CreateDirectory(folder);
        string targetDir = Path.Combine(folder, "Hình ảnh");
        Directory.CreateDirectory(targetDir);

        await File.WriteAllTextAsync(Path.Combine(targetDir, "photo.png"), "existing");
        await File.WriteAllTextAsync(Path.Combine(folder, "photo.png"), "incoming");

        AppConfiguration config = ConfigAndHistoryStore.CreateDefaultConfiguration();
        FolderAnalysisResult analysis = await _engine.AnalyzeFolderAsync(folder, config);

        ExecutionResult result = await _engine.ExecutePlanAsync(
            folder,
            analysis.Items,
            ConflictResolutionMode.AutoRename);

        Assert.Equal(1, result.MovedCount);
        Assert.True(File.Exists(Path.Combine(targetDir, "photo.png")));
        Assert.True(File.Exists(Path.Combine(targetDir, "photo (1).png")));
        Assert.Equal("incoming", await File.ReadAllTextAsync(Path.Combine(targetDir, "photo (1).png")));
    }

    [Fact]
    public async Task AnalyzeFolder_RespectsLockedFile()
    {
        string folder = Path.Combine(_tempRoot, "LockedCase");
        Directory.CreateDirectory(folder);
        string lockedPath = Path.Combine(folder, "open_document.docx");
        await File.WriteAllTextAsync(lockedPath, "locked");

        AppConfiguration config = ConfigAndHistoryStore.CreateDefaultConfiguration();

        using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        FolderAnalysisResult analysis = await _engine.AnalyzeFolderAsync(folder, config);

        FileMovePlanItem item = Assert.Single(analysis.Items);
        Assert.Equal(PlanItemStatus.LockedOrDownloading, item.Status);
        Assert.False(item.IsSelected);
    }

    [Fact]
    public void ConfigAndHistoryStore_PersistsRulesAndSessions()
    {
        string storeDir = Path.Combine(_tempRoot, "Store");
        var store = new ConfigAndHistoryStore(storeDir);

        AppConfiguration loaded = store.LoadConfiguration();
        Assert.NotEmpty(loaded.Rules);

        loaded.Rules.Add(new OrganizationRule
        {
            Name = "Kế toán",
            TargetFolderName = "Kế toán",
            Extensions = [".csv", ".xlsx"],
            Keywords = ["thuế"],
            Priority = 5
        });
        store.SaveConfiguration(loaded);

        AppConfiguration reloaded = store.LoadConfiguration();
        Assert.Contains(reloaded.Rules, r => r.Name == "Kế toán");

        var session = new OrganizationSession
        {
            SourceFolderPath = _tempRoot,
            MovedFileCount = 3,
            TotalBytesMoved = 1024
        };
        store.AddSession(session);

        var history = store.LoadHistory();
        Assert.Single(history);
        Assert.Equal(session.Id, history[0].Id);
    }

    [Fact]
    public async Task AnalyzeFolder_CustomRuleWithExtensionAndKeyword_MatchesOnlyFilesSatisfyingBoth_AndLeavesUnmatchedUntouched()
    {
        string folder = Path.Combine(_tempRoot, "CustomBookRule");
        Directory.CreateDirectory(folder);

        await File.WriteAllTextAsync(Path.Combine(folder, "Book_Exported.docx"), "b0");
        await File.WriteAllTextAsync(Path.Combine(folder, "Book_Exported (1).docx"), "b1");
        await File.WriteAllTextAsync(Path.Combine(folder, "Book_Exported (2).docx"), "b2");
        await File.WriteAllTextAsync(Path.Combine(folder, "Book_Summary.pdf"), "pdf-with-book");
        await File.WriteAllTextAsync(Path.Combine(folder, "Introduction.docx"), "other-docx");
        await File.WriteAllTextAsync(Path.Combine(folder, "WorldFirst.docx"), "other-docx2");
        await File.WriteAllTextAsync(Path.Combine(folder, "wallpaper.jpg"), "img");

        AppConfiguration config = ConfigAndHistoryStore.CreateDefaultConfiguration();
        foreach (OrganizationRule r in config.Rules)
        {
            r.IsEnabled = false;
        }

        config.Rules.Add(new OrganizationRule
        {
            Name = "Book",
            TargetFolderName = "Book test",
            Extensions = [".docx"],
            Keywords = ["Book"],
            Priority = 15,
            IsEnabled = true
        });

        FolderAnalysisResult analysis = await _engine.AnalyzeFolderAsync(folder, config);

        // Chỉ 3 file .docx có chữ "Book" được chọn và đi thẳng vào "Book test" (ưu tiên trước Smart Grouping)
        Assert.Equal(3, analysis.ReadyToMoveCount);
        foreach (string name in new[] { "Book_Exported.docx", "Book_Exported (1).docx", "Book_Exported (2).docx" })
        {
            FileMovePlanItem item = GetItem(analysis, name);
            Assert.True(item.IsSelected);
            Assert.Equal(PlanItemStatus.Ready, item.Status);
            Assert.Equal("Book test", item.ProposedTargetFolder);
            Assert.Equal("Book", item.MatchedRuleName);
        }

        // Các file không khớp đồng thời .docx VÀ "Book" phải giữ nguyên tại chỗ & không được tích chọn
        foreach (string name in new[] { "Book_Summary.pdf", "Introduction.docx", "WorldFirst.docx", "wallpaper.jpg" })
        {
            FileMovePlanItem item = GetItem(analysis, name);
            Assert.False(item.IsSelected);
            Assert.Equal(PlanItemStatus.Skipped, item.Status);
            Assert.Equal("(Giữ nguyên tại chỗ)", item.ProposedTargetFolder);
        }
    }

    [Fact]
    public void ConfigAndHistoryStore_MigratesV1ConfigWithoutTouchingCustomRules()
    {
        string storeDir = Path.Combine(_tempRoot, "MigrateStore");
        Directory.CreateDirectory(storeDir);

        string legacyJson = """
        {
          "LastTargetFolder": "C:\\Users\\Test\\Downloads",
          "SmartSettings": {
            "MoveUnmatchedToOthers": true
          },
          "Rules": [
            {
              "Id": "rule-documents",
              "Name": "Tài liệu",
              "TargetFolderName": "Tài liệu",
              "Extensions": [".docx"],
              "Keywords": ["báo cáo", "guide"],
              "Priority": 20,
              "IsEnabled": false
            },
            {
              "Id": "custom-book",
              "Name": "Book",
              "TargetFolderName": "Book test",
              "Extensions": [".docx"],
              "Keywords": ["Book"],
              "Priority": 15,
              "IsEnabled": true
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(storeDir, "config.json"), legacyJson);

        var store = new ConfigAndHistoryStore(storeDir);
        AppConfiguration migrated = store.LoadConfiguration();

        Assert.Equal(AppConfiguration.CurrentConfigSchemaVersion, migrated.ConfigSchemaVersion);
        Assert.False(migrated.SmartSettings.MoveUnmatchedToOthers);

        OrganizationRule docRule = migrated.Rules.Single(r => r.Id == "rule-documents");
        Assert.Empty(docRule.Keywords);
        Assert.False(docRule.IsEnabled);

        OrganizationRule customBook = migrated.Rules.Single(r => r.Id == "custom-book");
        Assert.Contains("Book", customBook.Keywords);
        Assert.True(customBook.IsEnabled);
    }

    [Fact]
    public void SmartGroupingSettings_ComputesDelayTimeSpanAndDisplayTextForAllUnits()
    {
        var settings = new SmartGroupingSettings
        {
            AutoWatcherDelayValue = 5,
            AutoWatcherDelayUnit = WatcherDelayUnit.Seconds
        };
        Assert.Equal(TimeSpan.FromSeconds(5), settings.GetAutoWatcherDelayTimeSpan());
        Assert.Equal("5 giây", settings.GetAutoWatcherDelayDisplayText());

        settings.AutoWatcherDelayUnit = WatcherDelayUnit.Minutes;
        Assert.Equal(TimeSpan.FromMinutes(5), settings.GetAutoWatcherDelayTimeSpan());
        Assert.Equal("5 phút", settings.GetAutoWatcherDelayDisplayText());

        settings.AutoWatcherDelayUnit = WatcherDelayUnit.Hours;
        Assert.Equal(TimeSpan.FromHours(5), settings.GetAutoWatcherDelayTimeSpan());
        Assert.Equal("5 giờ", settings.GetAutoWatcherDelayDisplayText());

        settings.AutoWatcherDelayUnit = WatcherDelayUnit.Days;
        Assert.Equal(TimeSpan.FromDays(5), settings.GetAutoWatcherDelayTimeSpan());
        Assert.Equal("5 ngày", settings.GetAutoWatcherDelayDisplayText());

        settings.AutoWatcherDelayUnit = WatcherDelayUnit.Months;
        Assert.Equal(TimeSpan.FromDays(150), settings.GetAutoWatcherDelayTimeSpan());
        Assert.Equal("5 tháng", settings.GetAutoWatcherDelayDisplayText());

        settings.AutoWatcherDelayUnit = WatcherDelayUnit.Years;
        Assert.Equal(TimeSpan.FromDays(1825), settings.GetAutoWatcherDelayTimeSpan());
        Assert.Equal("5 năm", settings.GetAutoWatcherDelayDisplayText());
    }

    [Fact]
    public async Task FolderWatcherService_AutoOrganizesExistingAndNewlyAddedFilesAfterDelay()
    {
        string watchDir = Path.Combine(_tempRoot, "WatchedSandbox");
        Directory.CreateDirectory(watchDir);

        string existingPhoto = Path.Combine(watchDir, "existing_photo.png");
        await File.WriteAllTextAsync(existingPhoto, "existing-img");

        string storeDir = Path.Combine(_tempRoot, "WatcherStore");
        var store = new ConfigAndHistoryStore(storeDir);
        AppConfiguration config = ConfigAndHistoryStore.CreateDefaultConfiguration();
        config.SmartSettings.AutoWatcherDelayValue = 1;
        config.SmartSettings.AutoWatcherDelayUnit = WatcherDelayUnit.Seconds;

        using var watcher = new FolderWatcherService(_engine, store);
        var existingMovedTcs = new TaskCompletionSource<OrganizationSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newMovedTcs = new TaskCompletionSource<OrganizationSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        int sessionCount = 0;

        watcher.SessionCompleted += session =>
        {
            int idx = System.Threading.Interlocked.Increment(ref sessionCount);
            if (idx == 1)
            {
                existingMovedTcs.TrySetResult(session);
            }
            else if (idx == 2)
            {
                newMovedTcs.TrySetResult(session);
            }
        };

        watcher.Start(watchDir, config);

        // Tệp đã có sẵn trước khi bật Watcher cũng phải được tự động gom sau độ trễ
        OrganizationSession firstSession = await existingMovedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, firstSession.MovedFileCount);
        Assert.False(File.Exists(existingPhoto));
        Assert.True(File.Exists(Path.Combine(watchDir, "Hình ảnh", "existing_photo.png")));

        // Thêm tệp mới trong khi Watcher đang chạy
        string newPhoto = Path.Combine(watchDir, "cat.png");
        await File.WriteAllTextAsync(newPhoto, "cat-img");

        OrganizationSession secondSession = await newMovedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, secondSession.MovedFileCount);
        Assert.False(File.Exists(newPhoto));
        Assert.True(File.Exists(Path.Combine(watchDir, "Hình ảnh", "cat.png")));
    }

    private static FileMovePlanItem GetItem(FolderAnalysisResult result, string fileName) =>
        result.Items.Single(i => string.Equals(i.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
