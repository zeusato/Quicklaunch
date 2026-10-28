param([string]$ReleaseDirectory = (Join-Path $PSScriptRoot '..\artifacts\publish'))
$ErrorActionPreference = 'Stop'
if (Get-Process QuickStartLauncher -ErrorAction SilentlyContinue) { throw 'Close QuickStart before running this test.' }
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $workspace ('artifacts\autostart-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$runKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
$previous = $runKey.GetValue('QuickStartLauncher', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
$previousKind = if ($null -ne $previous) { $runKey.GetValueKind('QuickStartLauncher') } else { $null }
$results = @()
try {
    foreach ($duplicate in @($false, $true)) {
        $caseName = if ($duplicate) { 'duplicate' } else { 'single' }
        $caseRoot = Join-Path $testRoot $caseName
        New-Item -ItemType Directory -Path (Join-Path $caseRoot 'config'), (Join-Path $caseRoot 'cache') -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $ReleaseDirectory 'QuickStartLauncher.exe') -Destination $caseRoot
        New-Item -ItemType File -Path (Join-Path $caseRoot 'portable.flag') | Out-Null
        # Avoid update requests if a regression unexpectedly opens the launcher.
        Set-Content -LiteralPath (Join-Path $caseRoot 'cache\update-check.txt') -Value 'test'
        $profileId = [Guid]::NewGuid().ToString()
        $config = @{
            version = 3; activeProfileId = $profileId
            settings = @{ theme = 'dark'; startup = @{ mode = 'runProfiles'; profileIds = @($profileId); delaySeconds = 6; countdownSeconds = 1 }; closeWhenDone = $true; closeDelayMs = 100; keepOpenOnError = $true }
            profiles = @(@{ id = $profileId; name = 'Autostart test'; icon = ''; actions = @(@{ kind = 'delay'; id = [Guid]::NewGuid().ToString(); name = 'Safe delay'; enabled = $true; skipOnAutostart = $false; delayAfterMs = 0; ms = 100 }) })
        }
        $config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $caseRoot 'config\config.json') -Encoding utf8
        $first = $null; $second = $null
        try {
            $exe = Join-Path $caseRoot 'QuickStartLauncher.exe'
            $first = Start-Process -FilePath $exe -ArgumentList '--autostart' -WindowStyle Hidden -PassThru
            if ($duplicate) {
                # Wait for the first process to register the portable executable, then
                # allow the activation listener to start, while still inside its delay.
                $deadline = (Get-Date).AddSeconds(10)
                while ($runKey.GetValue('QuickStartLauncher') -ne ('"' + $exe + '" --autostart')) {
                    if ($first.HasExited -or (Get-Date) -gt $deadline) { throw 'First startup did not register.' }
                    Start-Sleep -Milliseconds 50
                }
                Start-Sleep -Milliseconds 1000
                $second = Start-Process -FilePath $exe -ArgumentList '--autostart' -WindowStyle Hidden -PassThru
                if (!$second.WaitForExit(5000) -or $second.ExitCode -ne 0) { throw 'Second startup failed to forward.' }
            }
            $exited = $first.WaitForExit(15000)
            $log = (Get-ChildItem -LiteralPath (Join-Path $caseRoot 'logs') -File | Get-Content) -join "`n"
            $runs = ([regex]::Matches($log, 'Starting profile .*autostart=true')).Count
            $done = ([regex]::Matches($log, 'Delay .* completed after')).Count
            $ok = $exited -and $first.ExitCode -eq 0 -and $runs -eq 1 -and $done -eq 1
            $results += [pscustomobject]@{ Case = $caseName; Passed = $ok; Exited = $exited; AutostartRuns = $runs; CompletedActions = $done }
        }
        finally {
            foreach ($owned in @($first, $second)) {
                if ($null -ne $owned -and !$owned.HasExited) { $owned.Kill(); $owned.WaitForExit() }
            }
        }
    }
}
finally {
    if ($null -ne $previous) { $runKey.SetValue('QuickStartLauncher', $previous, $previousKind) }
    else { $runKey.DeleteValue('QuickStartLauncher', $false) }
    $runKey.Dispose()
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'results.json') -Encoding utf8
$results | Format-Table -AutoSize
Write-Output "Evidence: $testRoot"
if ($results.Passed -contains $false) { throw 'Autostart regression detected.' }
