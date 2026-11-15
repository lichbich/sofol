using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Sofol.App.Helpers;

namespace Sofol.App.Services;

public sealed record AppUpdateInfo
{
    public bool HasUpdate { get; init; }
    public string CurrentVersion { get; init; } = InstallationHelper.AppVersion;
    public string LatestVersion { get; init; } = string.Empty;
    public string ReleaseTitle { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string HtmlUrl { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string AssetFileName { get; init; } = string.Empty;
    public long AssetSize { get; init; }
    public DateTime? PublishedAt { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
}

/// <summary>
/// Dịch vụ tự động kiểm tra, tải về và cập nhật ứng dụng từ GitHub Releases.
/// </summary>
public static class UpdateService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static UpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Sofol-App", InstallationHelper.AppVersion));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    public static async Task<AppUpdateInfo> CheckForUpdateAsync(string repository)
    {
        string targetRepo = string.IsNullOrWhiteSpace(repository) || !repository.Contains('/')
            ? "lichbich/sofol"
            : repository.Trim();

        try
        {
            string url = $"https://api.github.com/repos/{targetRepo}/releases/latest";
            using var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new AppUpdateInfo
                {
                    HasUpdate = false,
                    ErrorMessage = string.Empty
                };
            }

            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
            string releaseTitle = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "" : "";
            string releaseNotes = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var htmlElem) ? htmlElem.GetString() ?? "" : "";
            DateTime? publishedAt = root.TryGetProperty("published_at", out var pubElem) && pubElem.TryGetDateTime(out var dt) ? dt : null;

            string cleanRemoteVer = tagName.TrimStart('v', 'V').Trim();
            string cleanCurrentVer = InstallationHelper.AppVersion.TrimStart('v', 'V').Trim();

            bool isNewer = IsVersionNewer(cleanRemoteVer, cleanCurrentVer);

            string downloadUrl = string.Empty;
            string assetFileName = string.Empty;
            long assetSize = 0;

            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                // Ưu tiên tìm file .zip hoặc file setup .exe
                foreach (var asset in assetsElem.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string dUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                    long size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;

                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = dUrl;
                        assetFileName = name;
                        assetSize = size;
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            break; // Ưu tiên zip giải nén cập nhật trực tiếp
                        }
                    }
                }
            }

            return new AppUpdateInfo
            {
                HasUpdate = isNewer,
                LatestVersion = cleanRemoteVer,
                ReleaseTitle = string.IsNullOrWhiteSpace(releaseTitle) ? tagName : releaseTitle,
                ReleaseNotes = releaseNotes,
                HtmlUrl = htmlUrl,
                DownloadUrl = downloadUrl,
                AssetFileName = assetFileName,
                AssetSize = assetSize,
                PublishedAt = publishedAt
            };
        }
        catch
        {
            return new AppUpdateInfo
            {
                HasUpdate = false,
                ErrorMessage = "Không thể kết nối đến máy chủ. Vui lòng kiểm tra lại kết nối mạng của bạn."
            };
        }
    }

    private static bool IsVersionNewer(string remoteVer, string currentVer)
    {
        if (Version.TryParse(remoteVer, out var vRemote) && Version.TryParse(currentVer, out var vCurrent))
        {
            return vRemote > vCurrent;
        }

        return string.Compare(remoteVer, currentVer, StringComparison.OrdinalIgnoreCase) > 0;
    }

    public static async Task DownloadAndApplyUpdateAsync(
        AppUpdateInfo update,
        IProgress<(double percent, string status)> progress)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            throw new InvalidOperationException("Không tìm thấy liên kết tải về cho bản phát hành này.");
        }

        string tempFolder = Path.Combine(Path.GetTempPath(), "Sofol_Update");
        Directory.CreateDirectory(tempFolder);

        string targetInstallDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string downloadedFile = Path.Combine(tempFolder, update.AssetFileName);

        progress.Report((0.10, $"Đang tải xuống bản cập nhật ({update.LatestVersion})..."));

        using (var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            long totalBytes = response.Content.Headers.ContentLength ?? update.AssetSize;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(downloadedFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            byte[] buffer = new byte[16384];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    double pct = 0.10 + (0.70 * ((double)totalRead / totalBytes));
                    string downloadedMb = (totalRead / (1024.0 * 1024.0)).ToString("0.0");
                    string totalMb = (totalBytes / (1024.0 * 1024.0)).ToString("0.0");
                    progress.Report((pct, $"Đang tải: {downloadedMb} MB / {totalMb} MB..."));
                }
            }
        }

        progress.Report((0.85, "Đang chuẩn bị cập nhật..."));
        await Task.Delay(200);

        int currentPid = Process.GetCurrentProcess().Id;
        string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(targetInstallDir, "Sofol.App.exe");

        // Tạo script PowerShell cập nhật độc lập ngoài tiến trình
        string updateScriptPath = Path.Combine(tempFolder, "apply_update.ps1");
        string psScript = @"
param([int]$pidToWait, [string]$archivePath, [string]$destDir, [string]$exeToLaunch)
$ErrorActionPreference = 'SilentlyContinue'

# Đợi tiến trình app cũ đóng hoàn toàn
$proc = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue
if ($proc) {
    $proc.WaitForExit(10000)
}
Start-Sleep -Milliseconds 800

# Giải nén đè lên thư mục cài đặt
if ($archivePath.EndsWith('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
    Expand-Archive -Path $archivePath -DestinationPath $destDir -Force
}
elseif ($archivePath.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
    Start-Process -FilePath $archivePath -Wait
}

Start-Sleep -Milliseconds 500
# Khởi động lại ứng dụng đã cập nhật
Start-Process -FilePath $exeToLaunch -WorkingDirectory $destDir

# Dọn dẹp tệp tạm
Start-Sleep -Seconds 3
Remove-Item -Path (Split-Path $archivePath) -Recurse -Force -ErrorAction SilentlyContinue
";

        await File.WriteAllTextAsync(updateScriptPath, psScript);

        progress.Report((0.95, "Đang khởi động tiến trình cập nhật..."));
        await Task.Delay(300);

        var startInfo = new ProcessStartInfo("powershell.exe",
            $"-ExecutionPolicy Bypass -NoProfile -File \"{updateScriptPath}\" -pidToWait {currentPid} -archivePath \"{downloadedFile}\" -destDir \"{targetInstallDir}\" -exeToLaunch \"{currentExePath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(startInfo);

        // Thoát ứng dụng hiện tại để script thực hiện ghi đè
        Environment.Exit(0);
    }
}
