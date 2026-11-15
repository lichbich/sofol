using System;
using System.IO;

namespace Sofol.Core.Services;

/// <summary>
/// Tạo thư mục Test Sandbox mô phỏng đúng các tệp lộn xộn điển hình (như thư mục Downloads thực tế)
/// để người dùng trải nghiệm ngay mà không làm xáo trộn dữ liệu thật.
/// </summary>
public static class DemoSandboxGenerator
{
    public static string CreateSampleMessyFolder(string? targetPath = null)
    {
        string folderPath = string.IsNullOrWhiteSpace(targetPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sofol",
                "DemoSandbox")
            : targetPath;

        if (Directory.Exists(folderPath))
        {
            Directory.Delete(folderPath, recursive: true);
        }

        Directory.CreateDirectory(folderPath);

        // Thư mục con có sẵn (sẽ được giữ nguyên)
        Directory.CreateDirectory(Path.Combine(folderPath, "wallpaper"));
        Directory.CreateDirectory(Path.Combine(folderPath, "New folder"));

        // Hình ảnh
        CreateDummyFile(folderPath, "Phòng ngủ tối giản ấm cúng với góc làm việc.png", 2400);
        CreateDummyFile(folderPath, "d087849d34bf5b988afcf57c1fc3e3f3205a74fd_full.jpg", 1800);
        CreateDummyFile(folderPath, "Picture of file_kYoitdrlle28D.webp", 920);
        CreateDummyFile(folderPath, "cinema-vector-illustration-png_242108.jpg", 1450);
        CreateDummyFile(folderPath, "unnamed.png", 640);

        // Cụm file giống tên (Book_Exported) & cặp file đa ngôn ngữ cùng tên khác đuôi
        CreateDummyFile(folderPath, "Book_Exported.docx", 1200);
        CreateDummyFile(folderPath, "Book_Exported (1).docx", 1240);
        CreateDummyFile(folderPath, "Book_Exported (2).docx", 1290);

        CreateDummyFile(folderPath, "01.참관일지_양식_세종학당 및 한국어학당 수업 참관일지 (1).docx", 850);
        CreateDummyFile(folderPath, "01.참관일지_양식_세종학당 및 한국어학당 수업 참관일지 (1).hwp", 790);

        // Tài liệu & Bảng tính
        CreateDummyFile(folderPath, "UI_UX_Design_Task_Estimation_Guide.docx", 1100);
        CreateDummyFile(folderPath, "Báo cáo dự án_2026S2_0910.pdf", 3200);
        CreateDummyFile(folderPath, "Lịch trình du lịch Hoa Binh.xlsx", 540);
        CreateDummyFile(folderPath, "WorldFirst.docx", 420);

        // Cụm file tương đồng tên (Hóa đơn)
        CreateDummyFile(folderPath, "Hoa_don_thang_09_2026.pdf", 410);
        CreateDummyFile(folderPath, "Hoa_don_thang_10_2026.pdf", 435);

        // Bộ cài, nén, code & video
        CreateDummyFile(folderPath, "CPIDNGSocks_v1.0.4.apk", 5100);
        CreateDummyFile(folderPath, "OBS-Studio-32.1.2-Windows-x64-Installer.exe", 15400);
        CreateDummyFile(folderPath, "MonaServer_Win64.zip", 2800);
        CreateDummyFile(folderPath, "docugen-676bf-default-rtdb-gmm-task-export.json", 320);
        CreateDummyFile(folderPath, "Recording 2026-05-11 131107.mp4", 8400);

        // File đang tải dở của trình duyệt (bắt buộc bỏ qua an toàn)
        CreateDummyFile(folderPath, "Windows11_Update_Package.zip.crdownload", 4096);

        return folderPath;
    }

    private static void CreateDummyFile(string folder, string fileName, int byteSize)
    {
        string path = Path.Combine(folder, fileName);
        byte[] data = new byte[byteSize];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(path, data);
    }
}
