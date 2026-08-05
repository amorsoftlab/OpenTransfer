$ErrorActionPreference = "Stop"

# Extract current version from csproj
$csproj = "openTransferWPF.csproj"
$currentVersion = "1.0.0" # Fallback

if (Test-Path $csproj) {
    $content = Get-Content $csproj -Raw
    if ($content -match '<Version>(.*?)</Version>') {
        $currentVersion = $matches[1]
    }
} else {
    Write-Host "Error: Cannot find $csproj to read current version." -ForegroundColor Red
    exit 1
}

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "Current Version is: $currentVersion" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Cyan

# Calculate default next version
$versionParts = $currentVersion.Split('.')
$nextVersion = "1.0.0"
if ($versionParts.Length -ge 3) {
    $patch = [int]$versionParts[2] + 1
    $nextVersion = "{0}.{1}.{2}" -f $versionParts[0], $versionParts[1], $patch
}

# Prompt user for new version
$userInput = Read-Host "Enter new version (Press Enter for default: $nextVersion)"
$newVersion = $userInput.Trim()

if ([string]::IsNullOrWhiteSpace($newVersion)) {
    $newVersion = $nextVersion
    Write-Host "WARNING: No version provided! Defaulting to $newVersion" -ForegroundColor Yellow
}

if ($newVersion -notmatch '^\d+\.\d+\.\d+$') {
    Write-Host "Error: Version must be in format X.Y.Z (e.g., 1.2.4)" -ForegroundColor Red
    exit 1
}

$AssemblyVersion = "$newVersion.0"

Write-Host "`nUpdating version from $currentVersion to $newVersion..." -ForegroundColor Cyan

# 1. Update publish_release.ps1
$publishScript = "publish_release.ps1"
if (Test-Path $publishScript) {
    (Get-Content $publishScript) -replace '^\$version\s*=\s*".*"', "`$version = ""$newVersion""" | Set-Content $publishScript
    Write-Host "Updated $publishScript" -ForegroundColor Green
} else {
    Write-Host "Warning: $publishScript not found!" -ForegroundColor Yellow
}

# 2. Update installer.iss
$installerScript = "installer.iss"
if (Test-Path $installerScript) {
    (Get-Content $installerScript) -replace '^#define\s+MyAppVersion\s+".*"', "#define MyAppVersion ""$newVersion""" | Set-Content $installerScript
    Write-Host "Updated $installerScript" -ForegroundColor Green
} else {
    Write-Host "Warning: $installerScript not found!" -ForegroundColor Yellow
}

# 3. Update Services\UpdateService.cs
$updateService = "Services\UpdateService.cs"
if (Test-Path $updateService) {
    (Get-Content $updateService) -replace 'public\s+string\s+CurrentVersion\s*\{\s*get;\s*set;\s*\}\s*=\s*".*";', "public string CurrentVersion { get; set; } = ""$newVersion"";" `
                                 -replace 'public\s+const\s+string\s+CurrentAppVersion\s*=\s*".*";', "public const string CurrentAppVersion = ""$newVersion"";" | Set-Content $updateService
    Write-Host "Updated $updateService" -ForegroundColor Green
} else {
    Write-Host "Warning: $updateService not found!" -ForegroundColor Yellow
}

# 4. Update openTransferWPF.csproj
if (Test-Path $csproj) {
    (Get-Content $csproj) -replace '<Version>.*</Version>', "<Version>$newVersion</Version>" `
                          -replace '<AssemblyVersion>.*</AssemblyVersion>', "<AssemblyVersion>$AssemblyVersion</AssemblyVersion>" | Set-Content $csproj
    Write-Host "Updated $csproj" -ForegroundColor Green
}

Write-Host "`n✅ Version successfully updated to $newVersion across all required files!" -ForegroundColor Cyan
Write-Host "⚠️ Don't forget to update CHANGELOG.md manually!" -ForegroundColor Yellow
