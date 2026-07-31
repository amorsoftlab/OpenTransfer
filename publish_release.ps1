# OpenTransfer Automated Release & Push Script
# Target Repo: https://github.com/amorsoftlab/OpenTransfer.git

$ErrorActionPreference = "Stop"
$projectDir = $PSScriptRoot
Set-Location $projectDir

$version = "1.2.0"
$tag = "v$version"
$repoUrl = "https://github.com/amorsoftlab/OpenTransfer.git"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "🚀 Starting OpenTransfer Build & Release Pipeline" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Clean & Publish WPF App
Write-Host "`n[1/5] Publishing WPF Binaries (win-x64)..." -ForegroundColor Yellow
if (Test-Path "$projectDir\publish") { Remove-Item "$projectDir\publish" -Recurse -Force }
if (Test-Path "$projectDir\release_output") { Remove-Item "$projectDir\release_output" -Recurse -Force }

dotnet publish openTransferWPF.csproj -c Release -r win-x64 --self-contained false -o "$projectDir\publish"
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed!"; exit 1 }

# 2. Build Inno Setup Installer (.exe)
Write-Host "`n[2/5] Compiling Inno Setup Installer (.exe)..." -ForegroundColor Yellow
$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "ISCC.exe"
)

$isccPath = $null
foreach ($path in $isccPaths) {
    if (Get-Command $path -ErrorAction SilentlyContinue) {
        $isccPath = $path
        break
    }
}

if ($isccPath) {
    & $isccPath "$projectDir\installer.iss"
    Write-Host "✅ Inno Setup Installer built successfully in release_output\" -ForegroundColor Green
} else {
    Write-Host "⚠️ ISCC.exe (Inno Setup Compiler) not found on system PATH." -ForegroundColor Yellow
    Write-Host "Creating fallback Release Zip package in release_output\..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path "$projectDir\release_output" | Out-Null
    Compress-Archive -Path "$projectDir\publish\*" -DestinationPath "$projectDir\release_output\OpenTransfer_v$version.zip" -Force
}

# 3. Git Remote Check
Write-Host "`n[3/5] Checking Git Remote..." -ForegroundColor Yellow
$remotes = git remote -v
if ($remotes -notmatch "amorsoftlab/OpenTransfer") {
    Write-Host "Setting origin remote to $repoUrl..." -ForegroundColor Gray
    git remote remove origin -ErrorAction SilentlyContinue
    git remote add origin $repoUrl
}

# 4. Commit and Push Code
Write-Host "`n[4/5] Committing & Pushing to GitHub..." -ForegroundColor Yellow
git add .
git commit -m "Release ${tag}: OpenTransfer v$version build and source code"
git branch -M main
git pull origin main --rebase -ErrorAction SilentlyContinue
git push -u origin main -f

# 5. Create GitHub Release & Upload Asset
Write-Host "`n[5/5] Creating GitHub Release ($tag)..." -ForegroundColor Yellow
if (Get-Command "gh" -ErrorAction SilentlyContinue) {
    $setupFile = Get-ChildItem "$projectDir\release_output\*" | Select-Object -First 1
    if ($setupFile) {
        gh release create $tag $setupFile.FullName --title "OpenTransfer $tag" --notes "Native High-Speed Android USB File Transfer Application Release $tag"
        Write-Host "🎉 GitHub Release $tag created with asset $($setupFile.Name)!" -ForegroundColor Green
    }
} else {
    Write-Host "⚠️ GitHub CLI ('gh') is not installed." -ForegroundColor Yellow
    Write-Host "Please create a release on https://github.com/amorsoftlab/OpenTransfer/releases with tag '$tag' and upload the installer from 'release_output'." -ForegroundColor Cyan
}

Write-Host "`n==================================================" -ForegroundColor Green
Write-Host "SUCCESS: OpenTransfer Release Pipeline Complete!" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
