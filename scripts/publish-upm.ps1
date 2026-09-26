<#
.SYNOPSIS
    Packages Ktory for Unity Package Manager (UPM) and updates the 'upm' branch with generated .meta files.
.DESCRIPTION
    Maintains a clean 'main' branch without .meta files. Bundles Ktory.Core seamlessly into the com.ktory.unity
    package on the 'upm' branch so Unity developers only need a single dependency:
    "com.ktory.unity": "https://github.com/Kutinana/Ktory.git#upm"
.PARAMETER Push
    If specified, automatically pushes the upm branch to origin.
#>
param(
    [switch]$Push,
    [string]$BranchName = "upm",
    [string]$RemoteName = "origin"
)

$ErrorActionPreference = "Stop"

$repoRoot = (git rev-parse --show-toplevel).Trim()
Set-Location $repoRoot

function Get-DeterministicGuid([string]$relPath) {
    # Normalize slashes to forward slashes for cross-platform deterministic hashing
    $normalized = $relPath.Replace("\", "/").ToLowerInvariant()
    $md5 = [System.Security.Cryptography.MD5]::Create()
    $hash = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($normalized))
    return -join ($hash | ForEach-Object { "{0:x2}" -f $_ })
}

function Generate-MetaFile([string]$targetPath, [string]$relPath) {
    $metaPath = "$targetPath.meta"
    $guid = Get-DeterministicGuid $relPath

    if (Test-Path -Path $targetPath -PathType Container) {
        $content = @"
fileFormatVersion: 2
guid: $guid
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"@
    }
    else {
        $ext = [System.IO.Path]::GetExtension($targetPath).ToLowerInvariant()
        $importer = switch ($ext) {
            ".cs" {
@"
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"@
            }
            ".asmdef" {
@"
AssemblyDefinitionImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"@
            }
            default {
@"
TextScriptImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"@
            }
        }

        $content = @"
fileFormatVersion: 2
guid: $guid
$importer
"@
    }

    Set-Content -Path $metaPath -Value $content -Encoding utf8 -NoNewline:$false
}

Write-Host ">>> Preparing temporary staging directory for unified com.ktory.unity package..."
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ktory-upm-" + [System.Guid]::NewGuid().ToString("N"))
$tempIndex = Join-Path ([System.IO.Path]::GetTempPath()) ("ktory-git-idx-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir | Out-Null

try {
    # 1. Copy package.json from src/Ktory.Unity to package root
    Copy-Item -Path "src/Ktory.Unity/package.json" -Destination (Join-Path $tempDir "package.json")

    # 2. Copy Editor folder
    Copy-Item -Path "src/Ktory.Unity/Editor" -Destination (Join-Path $tempDir "Editor") -Recurse

    # 3. Copy Unity Runtime integration and assembly boundaries (Core remains engine-independent).
    $targetRuntime = Join-Path $tempDir "Runtime"
    New-Item -ItemType Directory -Path $targetRuntime | Out-Null
    # Runtime/Core may be present as an ignored local generation. Always regenerate it from the unique Core source.
    Get-ChildItem "src/Ktory.Unity/Runtime" | Where-Object { $_.Name -notin @("Core", "Core.meta") } | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $targetRuntime -Recurse
    }

    # 4. Copy Core C# source folders into Runtime/Core
    $targetCore = Join-Path $targetRuntime "Core"
    New-Item -ItemType Directory -Path $targetCore | Out-Null
    $coreFolders = @("Ast", "Common", "Desugar", "Parser", "Runtime")
    foreach ($folder in $coreFolders) {
        $srcPath = Join-Path "src/Ktory.Core" $folder
        if (Test-Path $srcPath) {
            Copy-Item -Path $srcPath -Destination (Join-Path $targetCore $folder) -Recurse
        }
    }

    # 5. Copy root documentation/license
    Copy-Item "src/Ktory.Unity/Samples~" -Destination (Join-Path $tempDir "Samples~") -Recurse
    Copy-Item "src/Ktory.Unity/Tests" -Destination (Join-Path $tempDir "Tests") -Recurse
    # Maintain the guide in docs; adapt repository-relative sample links for the generated package.
    $debuggingGuide = Get-Content "docs/ktory-unity-debugging.md" -Raw -Encoding utf8
    $debuggingGuide = $debuggingGuide.Replace("(../src/Ktory.Unity/", "(")
    Set-Content -Path (Join-Path $tempDir "DEBUGGING.md") -Value $debuggingGuide -Encoding utf8 -NoNewline
    if (Test-Path "README.md") {
        $packageReadme = Get-Content "README.md" -Raw -Encoding utf8
        $packageReadme = $packageReadme.Replace("(docs/ktory-unity-debugging.md)", "(DEBUGGING.md)")
        $packageReadme = $packageReadme.Replace("(docs/", "(https://github.com/Kutinana/Ktory/blob/main/docs/")
        $packageReadme = $packageReadme.Replace("(src/", "(https://github.com/Kutinana/Ktory/blob/main/src/")
        Set-Content -Path (Join-Path $tempDir "README.md") -Value $packageReadme -Encoding utf8 -NoNewline
    }
    if (Test-Path "LICENSE") { Copy-Item "LICENSE" -Destination (Join-Path $tempDir "LICENSE") }

    # 6. Clean UPM .gitignore
    $upmGitIgnore = @"
bin/
obj/
*.user
*.suo
.DS_Store
Thumbs.db
"@
    Set-Content -Path (Join-Path $tempDir ".gitignore") -Value $upmGitIgnore -Encoding utf8

    Write-Host ">>> Generating deterministic Unity .meta files for unified package..."
    $items = Get-ChildItem -Path $tempDir -Recurse | Where-Object { 
        -not $_.Name.EndsWith(".meta") -and -not $_.Name.StartsWith(".git")
    }

    foreach ($item in $items) {
        $fullPath = $item.FullName
        $relPath = $fullPath.Substring($tempDir.Length).TrimStart("\", "/").Replace("\", "/")
        Generate-MetaFile -targetPath $fullPath -relPath $relPath
    }

    Write-Host ">>> Updating branch '$BranchName' via Git plumbing..."
    $currentHead = (git rev-parse HEAD).Trim()

    # Create / update branch using isolated index outside of tempDir
    $prevIndex = $env:GIT_INDEX_FILE
    $env:GIT_INDEX_FILE = $tempIndex

    try {
        git --work-tree=$tempDir add -f -A
        $tree = (git write-tree).Trim()
        $commitMessage = "chore(upm): publish unified com.ktory.unity from $currentHead"

        # Check if branch exists
        & git show-ref --verify --quiet "refs/heads/$BranchName"
        if ($LASTEXITCODE -eq 0) {
            $parentCommit = (git rev-parse "refs/heads/$BranchName").Trim()
            $commit = (git commit-tree $tree -p $parentCommit -m $commitMessage).Trim()
        } else {
            $commit = (git commit-tree $tree -m $commitMessage).Trim()
        }

        git update-ref "refs/heads/$BranchName" $commit
        Write-Host ">>> Successfully updated branch '$BranchName' to commit $commit"
    }
    finally {
        if ($prevIndex) {
            $env:GIT_INDEX_FILE = $prevIndex
        } else {
            Remove-Item env:GIT_INDEX_FILE -ErrorAction SilentlyContinue
        }
        Remove-Item -Path $tempIndex -Force -ErrorAction SilentlyContinue
    }

    if ($Push) {
        Write-Host ">>> Pushing '$BranchName' to $RemoteName..."
        git push $RemoteName $BranchName -f
    }
}
finally {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ">>> Done! Unity manifest.json now needs ONLY ONE dependency:"
Write-Host "    `"com.ktory.unity`": `"https://github.com/Kutinana/Ktory.git#$BranchName`""
