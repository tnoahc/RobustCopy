# Publishes a tagged build: GitHub release with the binaries, plus a GitLab release that links to them.
#
# RobustCopy has two release lines, chosen by the tag's major version:
#   v1.x.y  RobustCopy 1, the .NET WPF app in src\RobustCopy (dotnet publish)
#   v2.x.y  RobustCopy 2, the Rust app in rust\ (cargo build), and any later major version
# A -preview.N suffix (v2.1.0-preview.1) publishes a GitHub pre-release. The tag's version must match
# the project's version (<Version> in RobustCopy.csproj, or version in Cargo.toml).
#
# The executables are uploaded to GitHub only. The GitLab instance sits behind a proxy that
# cannot accept uploads that large, so the GitLab release stores links instead of files.
#
# Required environment (provided by GitLab CI, except GITHUB_TOKEN):
#   CI_COMMIT_TAG, CI_PROJECT_DIR, CI_API_V4_URL, CI_PROJECT_ID, CI_JOB_TOKEN
#   GITHUB_TOKEN       fine-grained token with Contents: read and write on GITHUB_REPOSITORY
# Optional overrides (used by local dry runs):
#   GITHUB_REPOSITORY  default tnoahc/RobustCopy
#   GITHUB_API_URL     default https://api.github.com
#   GITHUB_UPLOADS_URL default https://uploads.github.com
#   GITHUB_GIT_URL     default https://github.com/<GITHUB_REPOSITORY>.git
#   CARGO_TARGET_DIR   where cargo writes the Rust build, default rust\target
#   RELEASE_SKIP_BUILD set to 1 to reuse an existing artifacts\publish folder
#   RELEASE_DRY_RUN    set to 1 to build and print the release notes without publishing anything
#                      (CI_COMMIT_TAG is still required; CI_PROJECT_DIR defaults to the repository root)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Fail([string] $message)
{
    Write-Host "ERROR: $message"
    exit 1
}

function Get-Setting([string] $name, [string] $default)
{
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value)) { return $default }
    return $value
}

function Get-ErrorDetail($errorRecord)
{
    $status = 0
    if ($errorRecord.Exception.Response) { $status = [int]$errorRecord.Exception.Response.StatusCode }
    return @{ Status = $status; Text = "HTTP $status $($errorRecord.Exception.Message) $($errorRecord.ErrorDetails.Message)" }
}

$tag = $env:CI_COMMIT_TAG
if ([string]::IsNullOrWhiteSpace($tag)) { Fail 'CI_COMMIT_TAG is not set; releases only run for tag pipelines.' }
$dryRun = $env:RELEASE_DRY_RUN -eq '1'
if (-not $dryRun -and [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) { Fail 'GITHUB_TOKEN is not available. It must be a masked, protected CI variable, and the tag must be protected (v*).' }

$tagMatch = [regex]::Match($tag, '^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-preview\.(?<preview>\d+))?$')
if (-not $tagMatch.Success) { Fail "$tag is not a release tag; use v1.2.3 or v1.2.3-preview.4." }
$version = $tag -replace '^v', ''
$baseVersion = "$($tagMatch.Groups['major'].Value).$($tagMatch.Groups['minor'].Value).$($tagMatch.Groups['patch'].Value)"
$isPreview = $tagMatch.Groups['preview'].Success
$isRust = [int]$tagMatch.Groups['major'].Value -ge 2
$displayVersion = $(if ($isPreview) { "$baseVersion Preview $($tagMatch.Groups['preview'].Value)" } else { $version })

$root = Get-Setting 'CI_PROJECT_DIR' (Split-Path -Parent $PSScriptRoot)
$repository = Get-Setting 'GITHUB_REPOSITORY' 'tnoahc/RobustCopy'
$githubApi = (Get-Setting 'GITHUB_API_URL' 'https://api.github.com').TrimEnd('/')
$githubUploads = (Get-Setting 'GITHUB_UPLOADS_URL' 'https://uploads.github.com').TrimEnd('/')
$githubGit = Get-Setting 'GITHUB_GIT_URL' "https://github.com/$repository.git"
$publishDir = Join-Path $root 'artifacts\publish'
$assets = @('RobustCopy.exe', 'RobustCopy.exe.sha256', 'RobustCopy.pdb')

# 1. Check that the tag matches the version of the app it releases.
if ($isRust)
{
    $cargoToml = Join-Path $root 'rust\Cargo.toml'
    $projectVersion = [regex]::Match((Get-Content -Raw -LiteralPath $cargoToml), '(?m)^version\s*=\s*"([^"]+)"').Groups[1].Value
    $versionSource = 'rust\Cargo.toml'
}
else
{
    $csproj = Join-Path $root 'src\RobustCopy\RobustCopy.csproj'
    $projectVersion = (Select-Xml -LiteralPath $csproj -XPath '//Version' | Select-Object -First 1).Node.InnerText
    $versionSource = 'src\RobustCopy\RobustCopy.csproj'
}
if ($projectVersion -ne $baseVersion) { Fail "$tag does not match version $projectVersion in $versionSource." }
Write-Host "Releasing RobustCopy $displayVersion ($(if ($isRust) { 'Rust' } else { 'WPF' }) app)"

# 2. Build the executable and its checksum.
if ($env:RELEASE_SKIP_BUILD -ne '1')
{
    if ($isRust)
    {
        cargo build --release --locked --manifest-path $cargoToml
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        $targetDir = Get-Setting 'CARGO_TARGET_DIR' (Join-Path $root 'rust\target')
        New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
        foreach ($file in 'RobustCopy.exe', 'RobustCopy.pdb')
        {
            Copy-Item -LiteralPath (Join-Path $targetDir "release\$file") -Destination $publishDir -Force
        }
    }
    else
    {
        dotnet publish (Join-Path $root 'src\RobustCopy\RobustCopy.csproj') --configuration Release --runtime win-x64 --self-contained true --output $publishDir
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}

$exe = Join-Path $publishDir 'RobustCopy.exe'
if (-not (Test-Path -LiteralPath $exe)) { Fail "$exe was not produced." }
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash
# Same format as the README: "HASH  RobustCopy.exe" (uppercase hex, two spaces).
[IO.File]::WriteAllText((Join-Path $publishDir 'RobustCopy.exe.sha256'), "$hash  RobustCopy.exe`n")
Write-Host "SHA-256: $hash"
foreach ($asset in $assets)
{
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $asset))) { Fail "$asset is missing from $publishDir." }
}

# 3. Release notes: the README's "What's new in <version>" section, plus the checksum.
#    A preview's section is titled with its display name, for example "What's new in 2.1.0 Preview 1".
$readme = (Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'README.md')) -replace "`r`n", "`n"
$heading = "### What's new in $displayVersion"
$start = $readme.IndexOf($heading)
$whatsNew = ''
if ($start -ge 0)
{
    $rest = $readme.Substring($start + $heading.Length)
    $end = [regex]::Match($rest, '(?m)^#{2,3} ')
    $whatsNew = $(if ($end.Success) { $rest.Substring(0, $end.Index) } else { $rest }).Trim()
    # Promote the bold group labels to headings, as on the GitHub release page.
    $whatsNew = [regex]::Replace($whatsNew, '(?m)^\*\*(.+)\*\*[ \t]*$', '### $1')
}
else
{
    Write-Host "README has no '$heading' section; using generic release notes."
}

$intro = $(if ($isRust) { "Windows 10/11 x64 build of RobustCopy $displayVersion, the Rust rewrite. A single executable with no .NET or other runtime to install." }
    else { "Self-contained Windows 10/11 x64 build of RobustCopy $displayVersion. No separate .NET installation is required." })
if ($isPreview) { $intro = "**Pre-release** for testing; the latest stable release remains the recommended download.`n`n$intro" }

$fence = '```'
$notes = (
    $intro,
    '',
    $whatsNew,
    '',
    '### SHA-256',
    '',
    ($fence + 'text'),
    "$hash  RobustCopy.exe",
    $fence,
    '',
    'Verify with `Get-FileHash .\RobustCopy.exe -Algorithm SHA256`.'
) -join "`n"
$notes = [regex]::Replace($notes, "`n{3,}", "`n`n")

# GitHub marks one release as Latest. Give it to the highest stable version, so a 1.x maintenance
# release never displaces 2.x, and previews never take it. Requires the tags (GIT_DEPTH: 0 fetches them).
$stableVersions = @(git tag --list 'v*' | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } | ForEach-Object { [version]($_ -replace '^v', '') })
$highestStable = $stableVersions + [version]$baseVersion | Sort-Object | Select-Object -Last 1
$makeLatest = $(if (-not $isPreview -and [version]$baseVersion -ge $highestStable) { 'true' } else { 'false' })

if ($dryRun)
{
    Write-Host "Dry run: would publish '$tag' as 'RobustCopy $displayVersion' (prerelease: $isPreview, latest: $makeLatest) with $($assets -join ', ')."
    Write-Host '--- Release notes ---'
    Write-Host $notes
    exit 0
}

$githubHeaders = @{
    Authorization          = "Bearer $env:GITHUB_TOKEN"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
}

# 4. Make sure GitHub has the tag (and the commits it needs). Requires a full clone (GIT_DEPTH: 0).
$tagRef = "refs/tags/$tag"
git rev-parse -q --verify $tagRef | Out-Null
if ($LASTEXITCODE -ne 0)
{
    git fetch --quiet origin "+${tagRef}:${tagRef}"
    if ($LASTEXITCODE -ne 0) { Fail "Could not fetch $tagRef from GitLab." }
}
$basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("x-access-token:$env:GITHUB_TOKEN"))
Write-Host "Pushing $tag to GitHub ($repository)"
git -c credential.helper= -c "http.extraHeader=Authorization: Basic $basic" push --quiet $githubGit "${tagRef}:${tagRef}"
if ($LASTEXITCODE -ne 0) { Fail "Pushing $tag to GitHub failed. If GitHub already has a different $tag, delete it there first." }

# 5. GitHub release: create as a draft, upload the assets, then publish, so a failed upload never
#    leaves a public release without its files.
$releaseBody = @{ tag_name = $tag; name = "RobustCopy $displayVersion"; body = $notes; draft = $true; prerelease = $isPreview } | ConvertTo-Json
try
{
    $release = Invoke-RestMethod -Method Post -Uri "$githubApi/repos/$repository/releases" -Headers $githubHeaders `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($releaseBody)) -UseBasicParsing
}
catch
{
    $detail = Get-ErrorDetail $_
    if ($detail.Status -eq 422) { Fail "GitHub rejected the release; a release for $tag probably already exists. $($detail.Text)" }
    Fail "Creating the GitHub release failed: $($detail.Text)"
}
Write-Host "Created draft GitHub release $($release.id)"

$links = @()
foreach ($asset in $assets)
{
    $path = Join-Path $publishDir $asset
    $contentType = $(if ($asset.EndsWith('.sha256')) { 'text/plain' } else { 'application/octet-stream' })
    Write-Host "Uploading $asset to GitHub"
    try
    {
        $uploaded = Invoke-RestMethod -Method Post -Uri "$githubUploads/repos/$repository/releases/$($release.id)/assets?name=$([Uri]::EscapeDataString($asset))" `
            -Headers $githubHeaders -ContentType $contentType -InFile $path -UseBasicParsing
    }
    catch
    {
        Fail "Uploading $asset failed: $((Get-ErrorDetail $_).Text). The GitHub release was left as a draft; delete it before re-running."
    }
    $links += [ordered]@{ name = $asset; url = $uploaded.browser_download_url }
}

try
{
    $published = Invoke-RestMethod -Method Patch -Uri "$githubApi/repos/$repository/releases/$($release.id)" -Headers $githubHeaders `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes((@{ draft = $false; make_latest = $makeLatest } | ConvertTo-Json))) -UseBasicParsing
}
catch
{
    Fail "Publishing the GitHub release failed: $((Get-ErrorDetail $_).Text). It is still a draft with all files attached."
}
Write-Host "Published $($published.html_url)"

# 6. GitLab release that links to the GitHub files (a small request, so the proxy is not an issue).
$gitlabLinks = foreach ($link in $links)
{
    $name = $(if ($link.name -eq 'RobustCopy.pdb') { 'RobustCopy.pdb (debug symbols)' } else { $link.name })
    $linkType = $(if ($link.name -eq 'RobustCopy.exe') { 'package' } else { 'other' })
    [ordered]@{ name = $name; url = $link.url; link_type = $linkType; direct_asset_path = "/$($link.name)" }
}
$gitlabRelease = [ordered]@{
    tag_name    = $tag
    name        = "RobustCopy $displayVersion"
    description = "$notes`n`nThe files are hosted on the [GitHub release]($($published.html_url))."
    assets      = @{ links = @($gitlabLinks) }
}
try
{
    Invoke-RestMethod -Method Post -Uri "$env:CI_API_V4_URL/projects/$env:CI_PROJECT_ID/releases" -Headers @{ 'JOB-TOKEN' = $env:CI_JOB_TOKEN } `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($gitlabRelease | ConvertTo-Json -Depth 5))) -UseBasicParsing | Out-Null
}
catch
{
    $detail = Get-ErrorDetail $_
    if ($detail.Status -eq 409) { Fail "A GitLab release for $tag already exists. The GitHub release was published: $($published.html_url)" }
    Fail "Creating the GitLab release failed: $($detail.Text). The GitHub release was published: $($published.html_url)"
}
Write-Host "Created GitLab release $tag linking to the GitHub files."
