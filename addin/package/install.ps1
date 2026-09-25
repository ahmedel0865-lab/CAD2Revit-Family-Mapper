# CAD2Revit installer: copies the add-in for every installed Revit version
# (2022-2026) into %AppData%\Autodesk\Revit\Addins\<version>. Per user, no admin
# rights needed. Run Install.bat (or: powershell -ExecutionPolicy Bypass -File install.ps1)
#   -Uninstall   remove CAD2Revit from all Revit versions
#   -All         install for every version in the package, even if Revit is not detected
param([switch]$Uninstall, [switch]$All)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$addinsRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'
$changed = @()

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    Write-Host 'Revit is running. Close all Revit windows first, then run this again.' -ForegroundColor Yellow
    exit 1
}

foreach ($v in 2022..2026) {
    $dest = Join-Path $addinsRoot $v
    if ($Uninstall) {
        $addin = Join-Path $dest 'CAD2Revit.addin'
        $dir = Join-Path $dest 'CAD2Revit'
        if ((Test-Path $addin) -or (Test-Path $dir)) {
            Remove-Item $addin -Force -ErrorAction SilentlyContinue
            Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
            $changed += $v
        }
        continue
    }
    $src = Join-Path $here $v
    if (-not (Test-Path $src)) { continue }
    $revitFound = (Test-Path (Join-Path $env:ProgramFiles "Autodesk\Revit $v")) -or (Test-Path $dest)
    if (-not ($revitFound -or $All)) { continue }

    New-Item -ItemType Directory -Force -Path (Join-Path $dest 'CAD2Revit') | Out-Null
    Copy-Item (Join-Path $src 'CAD2Revit.addin') $dest -Force
    Copy-Item (Join-Path $src 'CAD2Revit\*') (Join-Path $dest 'CAD2Revit') -Recurse -Force
    # Files downloaded from the internet are "blocked" by Windows; Revit refuses to load blocked DLLs.
    Get-ChildItem (Join-Path $dest 'CAD2Revit') -Recurse | Unblock-File
    Unblock-File (Join-Path $dest 'CAD2Revit.addin')
    $changed += $v
}

if ($changed.Count -eq 0) {
    if ($Uninstall) { Write-Host 'CAD2Revit was not installed.' }
    else {
        Write-Host 'No Revit 2022-2026 installation was found.' -ForegroundColor Yellow
        Write-Host 'To install anyway, run:  powershell -ExecutionPolicy Bypass -File install.ps1 -All'
    }
    exit 1
}
$verb = if ($Uninstall) { 'Removed from' } else { 'Installed for' }
Write-Host ("$verb Revit " + ($changed -join ', ')) -ForegroundColor Green
if (-not $Uninstall) {
    Write-Host 'Start Revit: a "CAD2Revit" tab with "List Blocks" and "Place Families" appears.'
    Write-Host 'If Revit asks about loading an unsigned add-in, choose "Always Load".'
}
