# SmartHost MEP installer: copies the add-in for every installed Revit version
# (2022-2026) into %AppData%\Autodesk\Revit\Addins\<version>. Per user, no admin
# rights needed. Run Install.bat (or: powershell -ExecutionPolicy Bypass -File install.ps1)
#   -Uninstall   remove SmartHost MEP from all Revit versions
#   -All         install for every version in the package, even if Revit is not detected
# The add-in was formerly called "CAD2Revit": its old manifest and folder are removed on install
# and uninstall, so the old and new versions are never loaded together (same AddInId).
param([switch]$Uninstall, [switch]$All)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$addinsRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'
$changed = @()
$oldName = 'CAD2Revit'

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
        $removedOld = Remove-AddIn $dest $oldName
        $removedNew = Remove-AddIn $dest 'SmartHostMEP'
        if ($removedOld -or $removedNew) { $changed += $v }
        continue
    }
    $src = Join-Path $here $v
    if (-not (Test-Path $src)) { continue }
    $revitFound = (Test-Path (Join-Path $env:ProgramFiles "Autodesk\Revit $v")) -or (Test-Path $dest)
    if (-not ($revitFound -or $All)) { continue }

    if (Remove-AddIn $dest $oldName) { Write-Host "Revit ${v}: removed the old $oldName add-in." }
    New-Item -ItemType Directory -Force -Path (Join-Path $dest 'SmartHostMEP') | Out-Null
    Copy-Item (Join-Path $src 'SmartHostMEP.addin') $dest -Force
    Copy-Item (Join-Path $src 'SmartHostMEP\*') (Join-Path $dest 'SmartHostMEP') -Recurse -Force
    # Files downloaded from the internet are "blocked" by Windows; Revit refuses to load blocked DLLs.
    Get-ChildItem (Join-Path $dest 'SmartHostMEP') -Recurse | Unblock-File
    Unblock-File (Join-Path $dest 'SmartHostMEP.addin')
    $changed += $v
}

if ($changed.Count -eq 0) {
    if ($Uninstall) { Write-Host 'SmartHost MEP was not installed.' }
    else {
        Write-Host 'No Revit 2022-2026 installation was found.' -ForegroundColor Yellow
        Write-Host 'To install anyway, run:  powershell -ExecutionPolicy Bypass -File install.ps1 -All'
    }
    exit 1
}
$verb = if ($Uninstall) { 'Removed from' } else { 'Installed for' }
Write-Host ("$verb Revit " + ($changed -join ', ')) -ForegroundColor Green
if (-not $Uninstall) {
    Write-Host 'Start Revit: a "SmartHost MEP" tab with "Place Families" appears.'
    Write-Host 'If Revit asks about loading an unsigned add-in, choose "Always Load".'
}
