# Script tự động biên dịch bản phát hành (Release) và đóng gói cho Sofol
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$version = "1.1.0"
$distDir = Join-Path $scriptDir "dist"
$packageDir = Join-Path $distDir "Sofol-v$version-win-x64"
$zipPath = Join-Path $distDir "Sofol-v$version-win-x64.zip"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "     BẮT ĐẦU BIÊN DỊCH VÀ ĐÓNG GÓI SOFOL v$version" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Dọn dẹp thư mục dist cũ
if (Test-Path $distDir) {
    Write-Host "--> Dọn dẹp thư mục dist cũ..." -ForegroundColor Yellow
    Remove-Item -Path $distDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

# 2. Biên dịch Release Unpackaged Self-Contained x64
Write-Host "`n--> Đang biên dịch Release cho win-x64 (Self-Contained)..." -ForegroundColor Yellow
$projectPath = Join-Path $scriptDir "Sofol.App\Sofol.App.csproj"
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o $packageDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Biên dịch thất bại!"
    exit 1
}

# 3. Tạo file Cài đặt Sofol (Setup).exe trong thư mục gói
$appExe = Join-Path $packageDir "Sofol.App.exe"
$setupExe = Join-Path $packageDir "Cài đặt Sofol (Setup).exe"
Copy-Item -Path $appExe -Destination $setupExe -Force
Write-Host "--> Đã tạo file kích hoạt cài đặt: 'Cài đặt Sofol (Setup).exe'" -ForegroundColor Green

# 4. Nén thành file zip phân phối (dành cho In-App Auto-Update)
Write-Host "`n--> Đang nén thành $zipPath..." -ForegroundColor Yellow
Compress-Archive -Path "$packageDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

$zipItem = Get-Item $zipPath
$zipSizeMB = [Math]::Round($zipItem.Length / (1024 * 1024), 2)
$zipHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash

# 5. Đóng gói bộ cài đặt độc lập SofolSetup.exe bằng Inno Setup (dành cho Web Landing Page tải trực tiếp)
$isccPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
$hasSetupExe = $false
$setupExePath = Join-Path $distDir "SofolSetup.exe"

if (Test-Path $isccPath) {
    Write-Host "`n--> Đang đóng gói SofolSetup.exe bằng Inno Setup 6..." -ForegroundColor Yellow
    $issPath = Join-Path $scriptDir "installer.iss"
    & $isccPath $issPath | Out-Null
    if (Test-Path $setupExePath) {
        $hasSetupExe = $true
        $setupItem = Get-Item $setupExePath
        $setupSizeMB = [Math]::Round($setupItem.Length / (1024 * 1024), 2)
        $setupHash = (Get-FileHash -Path $setupExePath -Algorithm SHA256).Hash
        Write-Host "--> Đã tạo file cài đặt trực tiếp web: SofolSetup.exe ($setupSizeMB MB)" -ForegroundColor Green
    }
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "             ĐÓNG GÓI THÀNH CÔNG v$version!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "1. File Zip cập nhật tự động (In-App Auto-Update):"
Write-Host "   - Đường dẫn : $zipPath"
Write-Host "   - Dung lượng: $zipSizeMB MB"
Write-Host "   - SHA256    : $zipHash"

if ($hasSetupExe) {
    Write-Host "`n2. File cài đặt độc lập cho Landing Page (Tải trực tiếp .exe):"
    Write-Host "   - Đường dẫn : $setupExePath"
    Write-Host "   - Dung lượng: $setupSizeMB MB"
    Write-Host "   - SHA256    : $setupHash"
}
Write-Host "==========================================================" -ForegroundColor Green
