# DMC Remapper installer: copies the add-in for every installed Revit version
# (2022-2026) into %AppData%\Autodesk\Revit\Addins\<version>. Per user, no admin
# rights needed. Run Install.bat (or: powershell -ExecutionPolicy Bypass -File install.ps1)
#   -Uninstall   remove DMC Remapper from all Revit versions
#   -All         install for every version in the package, even if Revit is not detected
# The add-in was formerly called "SmartHost MEP" (SmartHostMEP) and before that "CAD2Revit": their
# manifests and folders are removed on install and uninstall, so an old and the new version are never
# loaded together (same AddInId).
param([switch]$Uninstall, [switch]$All)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$addinsRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'
$changed = @()
$oldNames = @('SmartHostMEP', 'CAD2Revit')

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    Write-Host 'Revit is running. Close all Revit windows first, then run this again.' -ForegroundColor Yellow
    exit 1
}

function Remove-AddIn([string]$dest, [string]$name) {
    $addin = Join-Path $dest "$name.addin"
    $dir = Join-Path $dest $name
    $found = (Test-Path $addin) -or (Test-Path $dir)
    Remove-Item $addin -Force -ErrorAction SilentlyContinue
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    return $found
}

foreach ($v in 2022..2026) {
    $dest = Join-Path $addinsRoot $v
    if ($Uninstall) {
        $removed = $false
        foreach ($n in $oldNames + @('DMCRemapper')) { if (Remove-AddIn $dest $n) { $removed = $true } }
        if ($removed) { $changed += $v }
        continue
    }
    $src = Join-Path $here $v
    if (-not (Test-Path $src)) { continue }
    $revitFound = (Test-Path (Join-Path $env:ProgramFiles "Autodesk\Revit $v")) -or (Test-Path $dest)
    if (-not ($revitFound -or $All)) { continue }

    foreach ($n in $oldNames) { if (Remove-AddIn $dest $n) { Write-Host "Revit ${v}: removed the old $n add-in." } }
    New-Item -ItemType Directory -Force -Path (Join-Path $dest 'DMCRemapper') | Out-Null
    Copy-Item (Join-Path $src 'DMCRemapper.addin') $dest -Force
    Copy-Item (Join-Path $src 'DMCRemapper\*') (Join-Path $dest 'DMCRemapper') -Recurse -Force
    # Files downloaded from the internet are "blocked" by Windows; Revit refuses to load blocked DLLs.
    Get-ChildItem (Join-Path $dest 'DMCRemapper') -Recurse | Unblock-File
    Unblock-File (Join-Path $dest 'DMCRemapper.addin')
    $changed += $v
}

if ($changed.Count -eq 0) {
    if ($Uninstall) { Write-Host 'DMC Remapper was not installed.' }
    else {
        Write-Host 'No Revit 2022-2026 installation was found.' -ForegroundColor Yellow
        Write-Host 'To install anyway, run:  powershell -ExecutionPolicy Bypass -File install.ps1 -All'
    }
    exit 1
}
$verb = if ($Uninstall) { 'Removed from' } else { 'Installed for' }
Write-Host ("$verb Revit " + ($changed -join ', ')) -ForegroundColor Green
if (-not $Uninstall) {
    Write-Host 'Start Revit: a "DMC Remapper" tab with "Place Families" appears.'
    Write-Host 'If Revit asks about loading an unsigned add-in, choose "Always Load".'
}
