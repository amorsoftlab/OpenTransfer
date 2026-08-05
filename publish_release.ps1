# OpenTransfer Automated Release & Push Script
# Target Repo: https://github.com/amorsoftlab/OpenTransfer.git

$ErrorActionPreference = "Stop"
$projectDir = $PSScriptRoot
Set-Location $projectDir

$version = "1.2.4"
$tag = "v$version"
$repoUrl = "https://github.com/amorsoftlab/OpenTransfer.git"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "🚀 Starting OpenTransfer Build & Release Pipeline" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Clean & Publish WPF App
Write-Host "`n[1/5] Publishing WPF Binaries (win-x64)..." -ForegroundColor Yellow
if (Test-Path "$projectDir\publish") { Remove-Item "$projectDir\publish" -Recurse -Force }
if (Test-Path "$projectDir\output") { Remove-Item "$projectDir\output" -Recurse -Force }

dotnet publish openTransferWPF.csproj -c Release -r win-x64 --self-contained false -o "$projectDir\publish"
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed!"; exit 1 }

# Sync Version into installer.iss
if (Test-Path "$projectDir\installer.iss") {
    (Get-Content "$projectDir\installer.iss") -replace '#define MyAppVersion ".*"', "#define MyAppVersion ""$version""" | Set-Content "$projectDir\installer.iss"
}

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
    Write-Host "✅ Inno Setup Installer built successfully in output\" -ForegroundColor Green
} else {
    Write-Host "⚠️ ISCC.exe (Inno Setup Compiler) not found on system PATH." -ForegroundColor Yellow
    Write-Host "Creating fallback Release Zip package in output\..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path "$projectDir\output" | Out-Null
    Compress-Archive -Path "$projectDir\publish\*" -DestinationPath "$projectDir\output\OpenTransfer_v$version.zip" -Force
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
Write-Host "`n[4/5] Syncing & Pushing Source Code to GitHub..." -ForegroundColor Yellow
git add -A
$status = git status --porcelain
if ($status) {
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    git commit -m "Update ${tag} ($timestamp): Sync source code & build output"
    Write-Host "✅ Committed latest source code changes." -ForegroundColor Green
} else {
    Write-Host "Working tree clean, all source files are up to date." -ForegroundColor Gray
}
git branch -M main
Write-Host "Pushing main branch to $repoUrl..." -ForegroundColor Gray
git push -u origin main -f

# Push Git Release Tag
Write-Host "Pushing git release tag $tag to origin..." -ForegroundColor Gray
git tag -f $tag
git push origin $tag -f

# 5. Create GitHub Release & Upload Asset
Write-Host "`n[5/5] Creating GitHub Release ($tag)..." -ForegroundColor Yellow
if (Get-Command "gh" -ErrorAction SilentlyContinue) {
    $setupFile = Get-ChildItem "$projectDir\output\*" | Select-Object -First 1
    if ($setupFile) {
        try {
            gh release create $tag $setupFile.FullName --title "OpenTransfer $tag" --notes "Native High-Speed Android USB File Transfer Application Release $tag" 2>$null
            Write-Host "🎉 GitHub Release $tag created with asset $($setupFile.Name)!" -ForegroundColor Green
        } catch {
            try {
                gh release upload $tag $setupFile.FullName --clobber
                Write-Host "🎉 GitHub Release $tag updated with asset $($setupFile.Name)!" -ForegroundColor Green
            } catch {
                Write-Host "⚠️ Could not auto-upload via gh CLI. Tag $tag is pushed to GitHub." -ForegroundColor Yellow
                Write-Host "You can upload $($setupFile.Name) at https://github.com/amorsoftlab/OpenTransfer/releases/tag/$tag" -ForegroundColor Cyan
            }
        }
    }
} else {
    Write-Host "⚠️ GitHub CLI ('gh') is not installed." -ForegroundColor Yellow
    Write-Host "Tag $tag has been pushed to GitHub." -ForegroundColor Green
    Write-Host "Upload the installer from 'output' at https://github.com/amorsoftlab/OpenTransfer/releases" -ForegroundColor Cyan
}

Write-Host "`n==================================================" -ForegroundColor Green
Write-Host "SUCCESS: OpenTransfer Release Pipeline Complete!" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
