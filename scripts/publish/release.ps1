# Get the latest tag from the repository
$latestTag = git describe --tags --abbrev=0 2>$null

if (-not $latestTag) {
    Write-Host "No existing tags found. Creating initial tag 1.0.0.0"
    $newTag = "1.0.0.0"
} else {
    Write-Host "Latest tag: $latestTag"
    
    # Split the tag by periods
    $parts = $latestTag -split '\.'
    
    # Increment the last portion
    $lastIndex = $parts.Length - 1
    $parts[$lastIndex] = [int]$parts[$lastIndex] + 1
    
    # Join back together
    $newTag = $parts -join '.'
}

Write-Host "New tag: $newTag"

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

# Update version in Glamourer.json
Write-Host "Updating Glamourer.json..."
$glamourerJsonPath = Join-Path $repoRoot "Glamourer\Glamourer.json"
$glamourerJson = Get-Content $glamourerJsonPath -Raw | ConvertFrom-Json
$glamourerJson.AssemblyVersion = $newTag
$glamourerJson | ConvertTo-Json -Depth 10 | Set-Content -Path $glamourerJsonPath

# Update version in repo.json
Write-Host "Updating repo.json..."
$repoJsonPath = Join-Path $repoRoot "repo.json"
$repoJson = Get-Content $repoJsonPath -Raw | ConvertFrom-Json
$repoJson[0].AssemblyVersion = $newTag
$repoJson[0].TestingAssemblyVersion = $newTag
$repoJson | ConvertTo-Json -Depth 10 | Set-Content -Path $repoJsonPath

# Commit the version changes
Write-Host "Committing version changes..."
git add $csprojPath $glamourerJsonPath $repoJsonPath
git commit -m "Bump version to $newTag"
git push origin main

# Create and push the new tag
Write-Host "Creating and pushing tag..."
git tag $newTag
git push origin $newTag

Write-Host "Successfully created and pushed tag: $newTag"
