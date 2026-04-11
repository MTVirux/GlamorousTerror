function Acquire-BuildLock {
    param(
        [string]$RepoRoot,
        [int]$TimeoutSeconds = 300
    )

    if (-not $RepoRoot) {
        $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    }

    $lockPath = Join-Path $RepoRoot 'build.lock'
    $sw = [Diagnostics.Stopwatch]::StartNew()

    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        try {
            $fs = New-Object System.IO.FileStream($lockPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            $global:BuildLockHandle = $fs
            Write-Host "Acquired build lock: $lockPath"
            return $true
        } catch {
            Start-Sleep -Seconds 1
        }
    }

    return $false
}

function Release-BuildLock {
    if ($null -ne $global:BuildLockHandle) {
        try {
            $path = $global:BuildLockHandle.Name
            $global:BuildLockHandle.Close()
            Remove-Item -Path $path -ErrorAction SilentlyContinue
            $global:BuildLockHandle = $null
            Write-Host "Released build lock: $path"
        } catch {
            Write-Warning "Error releasing build lock: $_"
        }
    }
}
