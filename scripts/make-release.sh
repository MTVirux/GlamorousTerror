#!/bin/bash

# Get the latest tag from the repository
latestTag=$(git describe --tags --abbrev=0 2>/dev/null)

if [ -z "$latestTag" ]; then
    echo "No existing tags found. Creating initial tag 1.0.0.0"
    newTag="1.0.0.0"
else
    echo "Latest tag: $latestTag"
    
    # Split the tag by periods
    IFS='.' read -ra parts <<< "$latestTag"
    
    # Increment the last portion
    lastIndex=$((${#parts[@]} - 1))
    parts[$lastIndex]=$((${parts[$lastIndex]} + 1))
    
    # Join back together
    newTag=$(IFS='.'; echo "${parts[*]}")
fi

echo "New tag: $newTag"

# Update version in Glamourer.csproj
echo "Updating Glamourer.csproj..."
csprojPath="./Glamourer/Glamourer.csproj"
sed -i "s/<FileVersion>[0-9.]*<\/FileVersion>/<FileVersion>$newTag<\/FileVersion>/" "$csprojPath"
sed -i "s/<AssemblyVersion>[0-9.]*<\/AssemblyVersion>/<AssemblyVersion>$newTag<\/AssemblyVersion>/" "$csprojPath"

# Update version in Glamourer.json
echo "Updating Glamourer.json..."
glamourerJsonPath="./Glamourer/Glamourer.json"
jq --arg version "$newTag" '.AssemblyVersion = $version' "$glamourerJsonPath" > tmp.$$.json && mv tmp.$$.json "$glamourerJsonPath"

# Update version in repo.json
echo "Updating repo.json..."
repoJsonPath="./repo.json"
jq --arg version "$newTag" '.[0].AssemblyVersion = $version | .[0].TestingAssemblyVersion = $version' "$repoJsonPath" > tmp.$$.json && mv tmp.$$.json "$repoJsonPath"

# Commit the version changes
echo "Committing version changes..."
git add "$csprojPath" "$glamourerJsonPath" "$repoJsonPath"
git commit -m "Bump version to $newTag"
git push origin main

# Create and push the new tag
echo "Creating and pushing tag..."
git tag "$newTag"
git push origin "$newTag"

echo "Successfully created and pushed tag: $newTag"
