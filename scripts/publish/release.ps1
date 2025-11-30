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

# Update version in Glamourer.csproj
Write-Host "Updating Glamourer.csproj..."
$csprojPath = Join-Path $repoRoot "Glamourer\Glamourer.csproj"
$csproj = Get-Content $csprojPath -Raw
$csproj = $csproj -replace '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$newTag</FileVersion>"
$csproj = $csproj -replace '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$newTag</AssemblyVersion>"
Set-Content -Path $csprojPath -Value $csproj -NoNewline

# Update version in GlamorousTerror.json
Write-Host "Updating GlamorousTerror.json..."
$glamourerJsonPath = Join-Path $repoRoot "Glamourer\GlamorousTerror.json"
$glamourerJson = Get-Content $glamourerJsonPath -Raw | ConvertFrom-Json
$glamourerJson.AssemblyVersion = $newTag
$glamourerJson | ConvertTo-Json -Depth 10 | Set-Content -Path $glamourerJsonPath

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
git add $csprojPath $glamourerJsonPath $repoJsonPath
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
