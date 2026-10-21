# Publishes a tagged build: GitHub release with the binaries, plus a GitLab release that links to them.
#
# The 62 MB executable is uploaded to GitHub only. The GitLab instance sits behind a proxy that
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
#   RELEASE_SKIP_BUILD set to 1 to reuse an existing artifacts\publish folder

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
if ([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) { Fail 'GITHUB_TOKEN is not available. It must be a masked, protected CI variable, and the tag must be protected (v*).' }

$version = $tag -replace '^v', ''
$repository = Get-Setting 'GITHUB_REPOSITORY' 'tnoahc/RobustCopy'
$githubApi = (Get-Setting 'GITHUB_API_URL' 'https://api.github.com').TrimEnd('/')
$githubUploads = (Get-Setting 'GITHUB_UPLOADS_URL' 'https://uploads.github.com').TrimEnd('/')
$githubGit = Get-Setting 'GITHUB_GIT_URL' "https://github.com/$repository.git"
$publishDir = Join-Path $env:CI_PROJECT_DIR 'artifacts\publish'
$assets = @('RobustCopy.exe', 'RobustCopy.exe.sha256', 'RobustCopy.pdb')

# 1. Build the self-contained executable and its checksum.
if ($env:RELEASE_SKIP_BUILD -ne '1')
{
    dotnet publish (Join-Path $env:CI_PROJECT_DIR 'src\RobustCopy\RobustCopy.csproj') --configuration Release --runtime win-x64 --self-contained true --output $publishDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
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

# 2. Release notes: the README's "What's new in <version>" section, plus the checksum.
$readme = (Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $env:CI_PROJECT_DIR 'README.md')) -replace "`r`n", "`n"
$heading = "### What's new in $version"
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

$fence = '```'
$notes = (
    "Self-contained Windows 10/11 x64 build of RobustCopy $version. No separate .NET installation is required.",
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

$githubHeaders = @{
    Authorization          = "Bearer $env:GITHUB_TOKEN"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
}

# 3. Make sure GitHub has the tag (and the commits it needs). Requires a full clone (GIT_DEPTH: 0).
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

# 4. GitHub release: create as a draft, upload the assets, then publish, so a failed upload never
#    leaves a public release without its files.
$releaseBody = @{ tag_name = $tag; name = "RobustCopy $version"; body = $notes; draft = $true } | ConvertTo-Json
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
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes((@{ draft = $false; make_latest = 'true' } | ConvertTo-Json))) -UseBasicParsing
}
catch
{
    Fail "Publishing the GitHub release failed: $((Get-ErrorDetail $_).Text). It is still a draft with all files attached."
}
Write-Host "Published $($published.html_url)"

# 5. GitLab release that links to the GitHub files (a small request, so the proxy is not an issue).
$gitlabLinks = foreach ($link in $links)
{
    $name = $(if ($link.name -eq 'RobustCopy.pdb') { 'RobustCopy.pdb (debug symbols)' } else { $link.name })
    $linkType = $(if ($link.name -eq 'RobustCopy.exe') { 'package' } else { 'other' })
    [ordered]@{ name = $name; url = $link.url; link_type = $linkType; direct_asset_path = "/$($link.name)" }
}
$gitlabRelease = [ordered]@{
    tag_name    = $tag
    name        = "RobustCopy $version"
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
