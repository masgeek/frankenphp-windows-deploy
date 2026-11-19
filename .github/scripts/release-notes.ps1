<#
.SYNOPSIS
  Builds user-facing release notes for a tag and writes them to a markdown file.

.DESCRIPTION
  Collects the PRs merged between the previous version tag and -Version and sorts them:
    * User-facing changes (breaking, features, fixes, performance, other) go under
      "What's new", written from each PR's "Release note" section (falls back to a
      cleaned-up PR title).
    * CI, docs, dependency and "Release note: none" PRs are collapsed under "Maintenance".
  Followed by a Download block (SHA-256 + attestation verify command) when -Zip is given.

  Needs: git history/tags (fetch-depth: 0) and an authenticated `gh` (GH_TOKEN).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Repo,
    [Parameter(Mandatory)][string]$OutFile,
    [string]$Zip
)

$ErrorActionPreference = 'Stop'
# gh and git emit UTF-8; Windows defaults to an OEM code page, which garbles characters like arrows.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

# ---- PRs in range -----------------------------------------------------------
$prev = git describe --tags --abbrev=0 --match '[0-9]*.[0-9]*.[0-9]*' "$Version^" 2>$null
if ($LASTEXITCODE -ne 0) { $prev = $null }
$range = if ($prev) { "$prev..$Version" } else { $Version }

$numbers = [System.Collections.Generic.List[int]]::new()
foreach ($subject in (git log $range --format=%s)) {
    $n = $null
    if ($subject -match '^Merge pull request #(\d+)') { $n = [int]$Matches[1] }
    elseif ($subject -match '\(#(\d+)\)\s*$') { $n = [int]$Matches[1] }
    if ($n -and -not $numbers.Contains($n)) { $numbers.Add($n) }
}

# ---- helpers ----------------------------------------------------------------
$typeToCategory = @{
    feat = 'feature'; fix = 'fix'; perf = 'performance'; revert = 'fix'
    ci = 'maintenance'; build = 'maintenance'; docs = 'maintenance'; deps = 'maintenance'
    chore = 'maintenance'; refactor = 'maintenance'; test = 'maintenance'; style = 'maintenance'
}
$labelToCategory = @{
    breaking = 'breaking'; feature = 'feature'; fix = 'fix'; performance = 'performance'
    ci = 'maintenance'; documentation = 'maintenance'; dependencies = 'maintenance'
}
$titlePattern = '^[^\p{L}]*(?<type>[A-Za-z]+)(\([^)]*\))?(?<bang>!)?:\s*(?<rest>\S.*)$'

function Get-CleanTitle([string]$title) {
    $m = [regex]::Match($title, $titlePattern)
    $text = if ($m.Success -and $typeToCategory.ContainsKey($m.Groups['type'].Value.ToLower())) { $m.Groups['rest'].Value } else { $title }
    $text = $text.Trim()
    if ($text.Length -gt 0) { $text = $text.Substring(0, 1).ToUpper() + $text.Substring(1) }
    return $text
}

function Get-ReleaseNote([string]$body) {
    if (-not $body) { return $null }
    $clean = [regex]::Replace($body, '(?s)<!--.*?-->', '')
    $m = [regex]::Match($clean, '(?ms)^#{1,6}[ \t]*Release notes?[ \t]*\r?\n(.*?)(?=^#{1,6}[ \t]|\z)')
    if (-not $m.Success) { return $null }
    $text = $m.Groups[1].Value.Trim()
    if ($text) { return $text } else { return $null }
}

# ---- classify ---------------------------------------------------------------
$userFacing = [ordered]@{ breaking = @(); feature = @(); fix = @(); performance = @(); other = @() }
$maintenance = [System.Collections.Generic.List[string]]::new()

foreach ($n in $numbers) {
    $json = gh pr view $n --repo $Repo --json number,title,body,labels,url,author 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $json) { continue }  # not a PR number (e.g. an issue ref in a commit subject)
    $pr = $json | ConvertFrom-Json

    $labels = @($pr.labels | ForEach-Object { $_.name })
    if ($labels -contains 'bot-release' -or $labels -contains 'skip-changelog' -or $pr.title -match '^Release:') { continue }

    $link = '([#{0}]({1}))' -f $pr.number, $pr.url
    $category = $null
    foreach ($l in $labels) { if ($labelToCategory.ContainsKey($l)) { $category = $labelToCategory[$l]; break } }
    $m = [regex]::Match($pr.title, $titlePattern)
    if (-not $category -and $m.Success -and $typeToCategory.ContainsKey($m.Groups['type'].Value.ToLower())) {
        $category = $typeToCategory[$m.Groups['type'].Value.ToLower()]
    }
    if ($m.Success -and $m.Groups['bang'].Success) { $category = 'breaking' }
    if (-not $category) { $category = if ($pr.author.login -like '*renovate*') { 'maintenance' } else { 'other' } }

    $note = Get-ReleaseNote $pr.body
    if ($note -and $note -match '^\W*none\W*$') { $category = 'maintenance'; $note = $null }

    if ($category -eq 'maintenance' -and -not $note) {
        $maintenance.Add(('- {0} {1}' -f (Get-CleanTitle $pr.title), $link))
        continue
    }
    if ($category -eq 'maintenance') { $category = 'other' }  # author wrote a note: show it

    # Wrap in @() so a single entry stays an array (a lone string would be indexed per character).
    $lines = @(if ($note) {
        $note -split '\r?\n' | ForEach-Object { ($_ -replace '^\s*[-*]\s*', '').Trim() } | Where-Object { $_ }
    } else { Get-CleanTitle $pr.title })
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $suffix = if ($i -eq $lines.Count - 1) { " $link" } else { '' }
        $userFacing[$category] += ('- {0}{1}' -f $lines[$i], $suffix)
    }
}

# ---- compose ----------------------------------------------------------------
$sections = [ordered]@{
    breaking    = [char]::ConvertFromUtf32(0x26A0) + [char]::ConvertFromUtf32(0xFE0F) + ' Breaking changes'
    feature     = [char]::ConvertFromUtf32(0x2728) + ' Features'
    fix         = [char]::ConvertFromUtf32(0x1F41B) + ' Fixes'
    performance = [char]::ConvertFromUtf32(0x26A1) + ' Performance'
    other       = 'Other changes'
}

$out = [System.Collections.Generic.List[string]]::new()
$out.Add("## What's new")
$any = $false
foreach ($key in $sections.Keys) {
    if ($userFacing[$key].Count -eq 0) { continue }
    $any = $true
    $out.Add('')
    $out.Add("### $($sections[$key])")
    $out.AddRange([string[]]$userFacing[$key])
}
if (-not $any) {
    $out.Add('')
    $out.Add('Maintenance and internal improvements only - no user-facing changes in this release.')
}

if ($Zip) {
    $sha = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLower()
    $out.Add('')
    $out.Add('### Download')
    $out.Add(('**{0}** - Windows 10/11 x64, self-contained (no .NET runtime required).' -f $Zip))
    $out.Add('')
    $out.Add(('SHA-256: `{0}`' -f $sha))
    $out.Add('')
    $out.Add('Verify the build provenance:')
    $out.Add('```')
    $out.Add(('gh attestation verify {0} --repo {1}' -f $Zip, $Repo))
    $out.Add('```')
}

if ($maintenance.Count -gt 0) {
    $out.Add('')
    $out.Add('<details>')
    $out.Add(('<summary>Maintenance ({0})</summary>' -f $maintenance.Count))
    $out.Add('')
    $out.AddRange([string[]]$maintenance)
    $out.Add('')
    $out.Add('</details>')
}

$out.Add('')
if ($prev) { $out.Add(('**Full Changelog**: https://github.com/{0}/compare/{1}...{2}' -f $Repo, $prev, $Version)) }
else { $out.Add(('**Full Changelog**: https://github.com/{0}/commits/{1}' -f $Repo, $Version)) }

[System.IO.File]::WriteAllText($OutFile, (($out -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote release notes for $Version ($($numbers.Count) PR(s) considered) to $OutFile"
