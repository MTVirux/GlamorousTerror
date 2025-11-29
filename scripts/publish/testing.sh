#!/bin/bash

# Get the latest testing tag from the repository
latestTag=$(git tag -l "testing_*" | sort -V | tail -n 1)

if [ -z "$latestTag" ]; then
    echo "No existing testing tags found. Creating initial tag testing_1.0.0.0"
    newTag="testing_1.0.0.0"
    version="1.0.0.0"
else
    echo "Latest testing tag: $latestTag"
    
    # Remove the "testing_" prefix to get the version
    version="${latestTag#testing_}"
    
    # Split the version by periods
    IFS='.' read -ra parts <<< "$version"
    
    # Increment the last portion
    lastIndex=$((${#parts[@]} - 1))
    parts[$lastIndex]=$((${parts[$lastIndex]} + 1))
    
    # Join back together
    version=$(IFS='.'; echo "${parts[*]}")
    newTag="testing_$version"
fi

echo "New testing tag: $newTag"
echo "Version: $version"

# Get the repository root (parent of scripts folder)
scriptDir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repoRoot="$(dirname "$(dirname "$scriptDir}")"

# Update version in Glamourer.csproj
echo "Updating Glamourer.csproj..."
csprojPath="$repoRoot/Glamourer/Glamourer.csproj"
sed -i "s/<FileVersion>[0-9.]*<\/FileVersion>/<FileVersion>$version<\/FileVersion>/" "$csprojPath"
sed -i "s/<AssemblyVersion>[0-9.]*<\/AssemblyVersion>/<AssemblyVersion>$version<\/AssemblyVersion>/" "$csprojPath"

# Update version in Glamourer.json
echo "Updating Glamourer.json..."
glamourerJsonPath="$repoRoot/Glamourer/Glamourer.json"
jq --arg version "$version" '.AssemblyVersion = $version' "$glamourerJsonPath" > tmp.$$.json && mv tmp.$$.json "$glamourerJsonPath"

# Update version in repo.json
echo "Updating repo.json..."
repoJsonPath="$repoRoot/repo.json"
jq --arg version "$version" '.[0].TestingAssemblyVersion = $version' "$repoJsonPath" > tmp.$$.json && mv tmp.$$.json "$repoJsonPath"

# Commit the version changes
echo "Committing version changes..."
git add "$csprojPath" "$glamourerJsonPath" "$repoJsonPath"
git commit -m "Bump testing version to $version"
git push origin main

# Create and push the new tag
echo "Creating and pushing tag..."
git tag "$newTag"
git push origin "$newTag"

echo "Successfully created and pushed testing tag: $newTag"
