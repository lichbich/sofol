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
        Directory.CreateDirectory(Path.Combine(folderPath, "Thu_muc_cu"));
        Directory.CreateDirectory(Path.Combine(folderPath, "Du_an_mau"));

        // Hình ảnh mẫu
        CreateDummyFile(folderPath, "Hinh_nen_thien_nhien_4k.jpg", 2400);
        CreateDummyFile(folderPath, "Anh_chup_ky_niem_gia_dinh.jpg", 1800);
        CreateDummyFile(folderPath, "Avatar_mau_thiet_ke.webp", 920);
        CreateDummyFile(folderPath, "Banner_quang_cao_su_kien.png", 1450);
        CreateDummyFile(folderPath, "Screenshot_2026_11_15.png", 640);

        // Cụm tệp giống tên (mô phỏng tính năng Gom cụm thông minh của Sofol)
        CreateDummyFile(folderPath, "Bao_cao_doanh_thu.xlsx", 1200);
        CreateDummyFile(folderPath, "Bao_cao_doanh_thu (1).xlsx", 1240);
        CreateDummyFile(folderPath, "Bao_cao_doanh_thu (2).xlsx", 1290);

        CreateDummyFile(folderPath, "Hop_dong_dich_vu_2026 (1).pdf", 850);
        CreateDummyFile(folderPath, "Hop_dong_dich_vu_2026 (2).pdf", 890);
        CreateDummyFile(folderPath, "Hop_dong_dich_vu_2026.docx", 920);

        // Tài liệu văn phòng & Bảng tính
        CreateDummyFile(folderPath, "Ke_hoach_cong_tac_nam_2026.docx", 1100);
        CreateDummyFile(folderPath, "Tai_lieu_huong_dan_su_dung.pdf", 3200);
        CreateDummyFile(folderPath, "Danh_sach_nhan_su_mau.xlsx", 540);
        CreateDummyFile(folderPath, "Bai_thuyet_trinh_du_an.pptx", 4200);

        // Cụm hóa đơn tương đồng tên
        CreateDummyFile(folderPath, "Hoa_don_tien_dien_thang_09.pdf", 410);
        CreateDummyFile(folderPath, "Hoa_don_tien_dien_thang_10.pdf", 435);

        // Video & Âm thanh
        CreateDummyFile(folderPath, "Video_huong_dan_cai_dat.mp4", 8400);
        CreateDummyFile(folderPath, "Nhac_khong_loi_thu_gian.mp3", 3500);

        // Bộ cài đặt & Tệp nén sao lưu
        CreateDummyFile(folderPath, "Setup_PhanMemTienIch_v2.0.exe", 15400);
        CreateDummyFile(folderPath, "Du_lieu_sao_luu_he_thong.zip", 5800);
        CreateDummyFile(folderPath, "Cau_hinh_ung_dung_mau.json", 320);

        // Tệp đang tải dở của trình duyệt (Sofol sẽ tự động bỏ qua an toàn)
        CreateDummyFile(folderPath, "Goi_du_lieu_lon_dang_tai.zip.crdownload", 4096);

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
