param([string]$ReleaseDirectory = (Join-Path $PSScriptRoot '..\artifacts\release'))
$ErrorActionPreference = 'Stop'
if (Get-Process QuickStartLauncher -ErrorAction SilentlyContinue) { throw 'Đóng QuickStart trước khi smoke test.' }
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $workspace ('artifacts\smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path (Join-Path $testRoot 'config') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $ReleaseDirectory 'QuickStartLauncher.exe') -Destination $testRoot
New-Item -ItemType File -Path (Join-Path $testRoot 'portable.flag') | Out-Null
$profileId = [Guid]::NewGuid().ToString()
$config = @{
 version=3; activeProfileId=$profileId
 settings=@{theme='dark';startup=@{mode='off';profileIds=@();delaySeconds=0;countdownSeconds=0};closeWhenDone=$true;closeDelayMs=1200;keepOpenOnError=$true}
 profiles=@(@{id=$profileId;name='Smoke';icon='';actions=@(@{kind='delay';id=[Guid]::NewGuid().ToString();name='Smoke delay';enabled=$true;skipOnAutostart=$false;delayAfterMs=0;ms=3500})})
}
$config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $testRoot 'config\config.json') -Encoding utf8
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$nativeRun = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Run"); $previous = $nativeRun.GetValue("QuickStartLauncher", $null); $nativeRun.Dispose()
$exists = $null -ne $previous
try {
 $exe = Join-Path $testRoot 'QuickStartLauncher.exe'
 $watch = [Diagnostics.Stopwatch]::StartNew()
 $first = Start-Process -FilePath $exe -ArgumentList '--profile','Smoke' -WindowStyle Hidden -PassThru
 $deadline = (Get-Date).AddSeconds(8)
 do { Start-Sleep -Milliseconds 50; $first.Refresh() } until ($first.MainWindowHandle -ne 0 -or $first.HasExited -or (Get-Date) -gt $deadline)
 if ($first.HasExited -or $first.MainWindowHandle -eq 0) { throw 'Launcher không tạo được cửa sổ.' }
 $second = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
 if (!$second.WaitForExit(3000) -or $second.ExitCode -ne 0) { throw 'Single-instance activation thất bại.' }
 if (!$first.WaitForExit(12000) -or $first.ExitCode -ne 0) { throw 'CLI profile không tự đóng thành công.' }
 $elapsed = $watch.ElapsedMilliseconds
 $off = Start-Process -FilePath $exe -ArgumentList '--autostart' -WindowStyle Hidden -PassThru
 if (!$off.WaitForExit(3000) -or $off.ExitCode -ne 0) { throw '--autostart off không thoát.' }
 $saved = Get-Content -Raw -LiteralPath (Join-Path $testRoot 'config\config.json') | ConvertFrom-Json
 if ($saved.profiles[0].name -ne 'Smoke') { throw 'Portable config không được bảo toàn.' }
 @("PASS Release process/window", "PASS --profile", "PASS single instance", "PASS closeWhenDone", "PASS --autostart off", "PASS portable storage", "Total profile lifecycle: $elapsed ms") | Set-Content (Join-Path $testRoot 'results.txt')
 Get-Content (Join-Path $testRoot 'results.txt')
 Get-ChildItem (Join-Path $testRoot 'logs') -File | Get-Content
 Write-Output "Evidence: $testRoot"
}
finally {
 foreach ($owned in @($first, $second, $off)) { if ($null -ne $owned -and !$owned.HasExited) { $owned.Kill(); $owned.WaitForExit() } }
 if ($exists) { Set-ItemProperty -LiteralPath $runKey -Name QuickStartLauncher -Value $previous }
 else { Remove-ItemProperty -LiteralPath $runKey -Name QuickStartLauncher -ErrorAction SilentlyContinue }
}
