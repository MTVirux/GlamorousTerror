# Get the latest testing tag from the repository
git fetch --tags

# Check if local branch is up to date with remote
$currentBranch = git rev-parse --abbrev-ref HEAD
$localCommit = git rev-parse "@"
$remoteCommit = git rev-parse "@{u}"

if ($localCommit -ne $remoteCommit) {
    Write-Error "Local branch '$currentBranch' is not up to date with remote. Please pull the latest changes before publishing."
    exit 1
}

Write-Host "Local branch is up to date with remote."

$latestTestingTag = git tag -l "testing_*" | ForEach-Object {
    $version = $_ -replace '^testing_', ''
    [PSCustomObject]@{
        Tag = $_
        Version = [Version]$version
    }
} | Sort-Object Version -Descending | Select-Object -First 1 -ExpandProperty Tag

if (-not $latestTestingTag) {
    Write-Error "No testing tags found. Please create and test a testing release before publishing a production release."
    exit 1
}

Write-Host "Latest testing tag: $latestTestingTag"

# Extract version from testing tag (remove 'testing_' prefix)
$newTag = $latestTestingTag -replace '^testing_', ''

Write-Host "New release tag: $newTag"
Write-Host "Using version from testing tag: $latestTestingTag"

# Get the repository root (parent of scripts folder)
$scriptDir = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptDir

# Auto-detect project identifiers so this script works for any repo
Write-Host "Auto-detecting project files..."

# Build list of submodule absolute paths to ignore when scanning
$submoduleAbs = @()
$gitmodulesPath = Join-Path $repoRoot '.gitmodules'
if (Test-Path $gitmodulesPath) {
    $gm = Get-Content $gitmodulesPath -ErrorAction SilentlyContinue
    foreach ($line in $gm) {
        if ($line -match '^\s*path\s*=\s*(.+)$') {
            $p = $matches[1].Trim()
            $abs = (Join-Path $repoRoot $p).Replace('/','\\')
            $submoduleAbs += $abs
        }
    }
}
Write-Host "Excluding submodules: $($submoduleAbs -join ', ')"

# Find a solution file (if present)
$slnCandidates = Get-ChildItem -Path $repoRoot -Filter *.sln -Recurse -ErrorAction SilentlyContinue
$sln = $slnCandidates | Where-Object {
    $full = $_.FullName
    if ($full -match '\\(bin|obj)\\') { return $false }
    foreach ($sm in $submoduleAbs) { if ($full.StartsWith($sm, [System.StringComparison]::InvariantCultureIgnoreCase)) { return $false } }
    return $true
} | Select-Object -First 1
if ($sln) {
    $SolutionName = $sln.Name
    Write-Host "Found solution: $SolutionName"
} else {
    $SolutionName = $null
    Write-Host "No .sln found; will update project file directly."
}

# Find a project file (.csproj)
$csprojCandidates = Get-ChildItem -Path $repoRoot -Filter *.csproj -Recurse -ErrorAction SilentlyContinue
$csprojFile = $csprojCandidates | Where-Object {
    $full = $_.FullName
    if ($full -match '\\(bin|obj)\\') { return $false }
    foreach ($sm in $submoduleAbs) { if ($full.StartsWith($sm, [System.StringComparison]::InvariantCultureIgnoreCase)) { return $false } }
    return $true
} | Select-Object -First 1
if (-not $csprojFile) {
    Write-Error "No .csproj file found in repository."
    exit 1
}
$CsprojName = $csprojFile.Name
$ProjectDirFull = $csprojFile.Directory.FullName
$ProjectDir = Split-Path $ProjectDirFull -Leaf
Write-Host "Using project dir: $ProjectDir; csproj: $CsprojName"

# Find a project JSON file (look in project folder then repo root) that contains AssemblyVersion
$JsonName = $null
$projJsons = Get-ChildItem -Path $ProjectDirFull -Filter *.json -ErrorAction SilentlyContinue
foreach ($j in $projJsons) {
    $content = Get-Content $j.FullName -Raw -ErrorAction SilentlyContinue
    if ($content -and $content -match 'AssemblyVersion') { $JsonName = $j.Name; break }
}
if (-not $JsonName) {
    $rootJsons = Get-ChildItem -Path $repoRoot -Filter *.json -ErrorAction SilentlyContinue
    foreach ($j in $rootJsons) {
        if ($j.Name -ieq 'repo.json') { continue }
        $content = Get-Content $j.FullName -Raw -ErrorAction SilentlyContinue
        if ($content -and $content -match 'AssemblyVersion') { $JsonName = $j.Name; break }
    }
}
if (-not $JsonName) { $JsonName = 'ProjectInfo.json'; Write-Host "No project json found; defaulting to $JsonName" } else { Write-Host "Using project json: $JsonName" }

# Use detected absolute csproj path
$csprojPath = $csprojFile.FullName
Write-Host "Updating $CsprojName at $csprojPath..."
$csproj = Get-Content $csprojPath -Raw
$csproj = $csproj -replace '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$newTag</FileVersion>"
$csproj = $csproj -replace '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$newTag</AssemblyVersion>"
$csproj = $csproj -replace '<Version>[\d\.]+</Version>', "<Version>$newTag</Version>"
Set-Content -Path $csprojPath -Value $csproj -NoNewline

# Determine project json path and update
$projectJsonPath = if (Test-Path (Join-Path $ProjectDirFull $JsonName)) { Join-Path $ProjectDirFull $JsonName } else { Join-Path $repoRoot $JsonName }
Write-Host "Updating $JsonName at $projectJsonPath..."
$projectJson = Get-Content $projectJsonPath -Raw | ConvertFrom-Json
$projectJson.AssemblyVersion = $newTag
$projectJson | ConvertTo-Json -Depth 10 | Set-Content -Path $projectJsonPath

# Update LastUpdate in repo.json
Write-Host "Updating repo.json..."
$repoJsonPath = Join-Path $repoRoot "repo.json"
$repoJsonRaw = Get-Content $repoJsonPath -Raw
$repoJson = $repoJsonRaw | ConvertFrom-Json
# Ensure repoJson is always an array
if ($repoJson -isnot [System.Collections.IEnumerable] -or $repoJson -is [string]) {
    $repoJson = @($repoJson)
}
$timestamp = [int][double]::Parse((Get-Date -UFormat %s))
$repoJson[0].AssemblyVersion = $newTag
$repoJson[0].LastUpdate = $timestamp
$repoJsonJson = $repoJson | ConvertTo-Json -Depth 10
$trimmed = $repoJsonJson.Trim()
$nl = [Environment]::NewLine
if ($trimmed.StartsWith('{')) {
    $repoJsonJson = '[' + $nl + $repoJsonJson + $nl + ']'
}
Set-Content -Path $repoJsonPath -Value $repoJsonJson

# Commit the version changes
Write-Host "Committing version changes..."
git add $csprojPath $projectJsonPath $repoJsonPath
git commit -m "[CI] Update release version to $newTag"

# Push the commit first
Write-Host "Pushing version changes to main..."
git push origin main

# Verify the commit is on remote with retry logic
Write-Host "Verifying commit on remote..."
$maxAttempts = 90  # 3 minutes at 2 seconds per attempt
$attempt = 0
$verified = $false

while ($attempt -lt $maxAttempts) {
    git fetch origin main
    $localCommit = git rev-parse HEAD
    $remoteCommit = git rev-parse origin/main
    
    if ($localCommit -eq $remoteCommit) {
        $verified = $true
        break
    }
    
    $attempt++
    Write-Host "Waiting for commit to sync... (Attempt $attempt/$maxAttempts)"
    Start-Sleep -Seconds 2
}

if (-not $verified) {
    Write-Error "Failed to verify commit on remote after 3 minutes. Local and remote are out of sync."
    exit 1
}

Write-Host "Commit verified on remote. Creating and pushing tag..."
git tag $newTag
git push origin $newTag

Write-Host "Successfully created and pushed tag: $newTag"
