# Read startup registration and QuickStart data; only write the diagnostic report.
param(
    [string]$ExecutablePath,
    [string]$ReportPath = (Join-Path $PSScriptRoot 'QuickStart-startup-report.txt')
)
$ErrorActionPreference = 'Stop'
$report = [Collections.Generic.List[string]]::new()
function Add-Line([string]$value) { $report.Add($value) }
Add-Line ('QuickStart startup diagnosis - ' + (Get-Date -Format o))
Add-Line ('Windows: ' + [Environment]::OSVersion.VersionString)
$registeredExe = $null
foreach ($hive in @('HKCU:', 'HKLM:')) {
    foreach ($suffix in @('Software\Microsoft\Windows\CurrentVersion\Run', 'Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run')) {
        $keyPath = "$hive\$suffix"
        $key = Get-Item -LiteralPath $keyPath -ErrorAction SilentlyContinue
        if ($null -eq $key) { continue }
        try {
            foreach ($name in $key.GetValueNames()) {
                $value = [string]$key.GetValue($name)
                if ($name -match 'QuickStart' -or $value -match 'QuickStartLauncher') {
                    Add-Line ("Registry: {0} [{1}] = {2}" -f $keyPath, $name, $value)
                    if ($hive -eq 'HKCU:' -and $name -eq 'QuickStartLauncher') {
                        if ($value -match '^"([^"]+)"') { $registeredExe = $Matches[1] }
                        elseif ($value -match '^(.*?\.exe)(?:\s|$)') { $registeredExe = $Matches[1] }
                        Add-Line ('Has --autostart: ' + ($value -match '(?:^|\s)--autostart(?:\s|$)'))
                    }
                }
            }
        } finally { $key.Dispose() }
    }
    foreach ($kind in @('Run', 'Run32', 'StartupFolder')) {
        $keyPath = "$hive\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\$kind"
        $key = Get-Item -LiteralPath $keyPath -ErrorAction SilentlyContinue
        if ($null -eq $key) { continue }
        try {
            foreach ($name in $key.GetValueNames()) {
                if ($name -match 'QuickStart') {
                    $bytes = $key.GetValue($name)
                    if ($bytes -is [byte[]]) { Add-Line ("StartupApproved: {0} [{1}] = {2}" -f $keyPath, $name, [BitConverter]::ToString($bytes)) }
                }
            }
        } finally { $key.Dispose() }
    }
}
if (!$registeredExe) { Add-Line 'No recognized HKCU QuickStartLauncher Run entry.' }
if (!$ExecutablePath -and (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'QuickStartLauncher.exe'))) {
    $ExecutablePath = Join-Path $PSScriptRoot 'QuickStartLauncher.exe'
}
if (!$ExecutablePath) { $ExecutablePath = $registeredExe }
$roots = @((Join-Path $env:LOCALAPPDATA 'QuickStart'))
foreach ($exe in @($ExecutablePath, $registeredExe) | Where-Object { $_ } | Select-Object -Unique) {
    Add-Line ("Executable: {0}; exists={1}" -f $exe, (Test-Path -LiteralPath $exe -PathType Leaf))
    if (Test-Path -LiteralPath $exe -PathType Leaf) {
        Add-Line ('File version: ' + (Get-Item -LiteralPath $exe).VersionInfo.FileVersion)
        $exeRoot = Split-Path -Parent $exe
        $portable = Test-Path -LiteralPath (Join-Path $exeRoot 'portable.flag')
        Add-Line ('Portable mode: ' + $portable)
        if ($portable) { $roots += $exeRoot }
    }
}
try {
    $shell = New-Object -ComObject WScript.Shell
    foreach ($folder in @([Environment]::GetFolderPath('Startup'), [Environment]::GetFolderPath('CommonStartup'))) {
        foreach ($file in Get-ChildItem -LiteralPath $folder -File -ErrorAction SilentlyContinue) {
            if ($file.Extension -eq '.lnk') {
                $shortcut = $shell.CreateShortcut($file.FullName)
                if ($shortcut.TargetPath -match 'QuickStart' -or $file.Name -match 'QuickStart') {
                    Add-Line ("Startup shortcut: {0} -> {1} {2}" -f $file.FullName, $shortcut.TargetPath, $shortcut.Arguments)
                }
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
            } elseif ($file.Name -match 'QuickStart') { Add-Line ('Startup file: ' + $file.FullName) }
        }
    }
} catch { Add-Line ('Shortcut inspection error: ' + $_.Exception.Message) }
finally { if ($null -ne $shell) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) } }
foreach ($root in $roots | Select-Object -Unique) {
    $configPath = Join-Path $root 'config\config.json'
    Add-Line ("Config: {0}; exists={1}" -f $configPath, (Test-Path -LiteralPath $configPath))
    if (Test-Path -LiteralPath $configPath) {
        try {
            $config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
            Add-Line ('Startup settings: ' + ($config.settings.startup | ConvertTo-Json -Compress -Depth 5))
            foreach ($profile in $config.profiles) {
                $eligible = @($profile.actions | Where-Object { $_.enabled -and !$_.skipOnAutostart }).Count
                Add-Line ("Profile: {0}; id={1}; selected={2}; eligibleActions={3}" -f $profile.name, $profile.id, ($config.settings.startup.profileIds -contains $profile.id), $eligible)
            }
        } catch { Add-Line ('Config read error: ' + $_.Exception.Message) }
    }
    foreach ($log in Get-ChildItem -LiteralPath (Join-Path $root 'logs') -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 3) {
        Add-Line ('Log: ' + $log.FullName)
        foreach ($line in Get-Content -LiteralPath $log.FullName -Tail 100) { Add-Line $line }
    }
}
$report | Set-Content -LiteralPath $ReportPath -Encoding utf8
Write-Output ('Report saved: ' + [IO.Path]::GetFullPath($ReportPath))
