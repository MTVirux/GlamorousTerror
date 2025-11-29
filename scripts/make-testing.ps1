# Get the latest testing tag from the repository
$latestTag = git tag -l "testing_*" | Sort-Object -Descending | Select-Object -First 1

if (-not $latestTag) {
    Write-Host "No existing testing tags found. Creating initial tag testing_1.0.0.0"
    $newTag = "testing_1.0.0.0"
    $version = "1.0.0.0"
} else {
    Write-Host "Latest testing tag: $latestTag"
    
    # Remove the "testing_" prefix to get the version
    $version = $latestTag -replace '^testing_', ''
    
    # Split the version by periods
    $parts = $version -split '\.'
    
    # Increment the last portion
    $lastIndex = $parts.Length - 1
    $parts[$lastIndex] = [int]$parts[$lastIndex] + 1
    
    # Join back together
    $version = $parts -join '.'
    $newTag = "testing_$version"
}

Write-Host "New testing tag: $newTag"
Write-Host "Version: $version"

# Update version in Glamourer.csproj
Write-Host "Updating Glamourer.csproj..."
$csprojPath = ".\Glamourer\Glamourer.csproj"
$csproj = Get-Content $csprojPath -Raw
$csproj = $csproj -replace '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$version</FileVersion>"
$csproj = $csproj -replace '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$version</AssemblyVersion>"
Set-Content -Path $csprojPath -Value $csproj -NoNewline

# Update version in Glamourer.json
Write-Host "Updating Glamourer.json..."
$glamourerJsonPath = ".\Glamourer\Glamourer.json"
$glamourerJson = Get-Content $glamourerJsonPath -Raw | ConvertFrom-Json
$glamourerJson.AssemblyVersion = $version
$glamourerJson | ConvertTo-Json -Depth 10 | Set-Content -Path $glamourerJsonPath

# Update version in repo.json
Write-Host "Updating repo.json..."
$repoJsonPath = ".\repo.json"
$repoJson = Get-Content $repoJsonPath -Raw | ConvertFrom-Json
$repoJson[0].TestingAssemblyVersion = $version
$repoJson | ConvertTo-Json -Depth 10 | Set-Content -Path $repoJsonPath

# Commit the version changes
Write-Host "Committing version changes..."
git add $csprojPath $glamourerJsonPath $repoJsonPath
git commit -m "Bump testing version to $version"
git push origin main

# Create and push the new tag
Write-Host "Creating and pushing tag..."
git tag $newTag
git push origin $newTag

Write-Host "Successfully created and pushed testing tag: $newTag"
