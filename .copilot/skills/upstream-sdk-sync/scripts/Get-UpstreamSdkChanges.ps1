#requires -Version 7.0
<#
.SYNOPSIS
  Lists upstream SDK changes between a recorded baseline commit and the upstream
  branch tip, classified by file category, for the upstream-sdk-sync skill.

.DESCRIPTION
  Uses the GitHub compare API (via `gh`) to compute the net file changes under the
  SDK path since the baseline, plus the intervening commit log. Output is a single
  JSON object on stdout that the agent parses to build a sync plan.

  When no baseline is known, emits mode="needs-baseline" with the current branch
  tip and recent SDK-touching commits so the operator can pick a starting point.

.PARAMETER Baseline
  Upstream commit SHA the port currently corresponds to. If omitted, read from the
  checkpoint file's lastSyncedCommit field.

.PARAMETER Repo
  Upstream repository in owner/name form. Default: microsoft/mxc.

.PARAMETER Branch
  Upstream branch to compare against. Default: main.

.PARAMETER Path
  Repo-relative path that scopes "SDK" changes. Default: sdk.

.PARAMETER CheckpointPath
  Path to the sync checkpoint JSON. Default: .upstream-sync.json in the current dir.

.EXAMPLE
  pwsh ./Get-UpstreamSdkChanges.ps1
  pwsh ./Get-UpstreamSdkChanges.ps1 -Baseline 161598fd08a4
#>
[CmdletBinding()]
param(
    [string]$Baseline,
    [string]$Repo = 'microsoft/mxc',
    [string]$Branch = 'main',
    [string]$Path = 'sdk',
    [string]$CheckpointPath = '.upstream-sync.json'
)

$ErrorActionPreference = 'Stop'

function Write-Json($obj) { $obj | ConvertTo-Json -Depth 8 -Compress }

function Get-Category([string]$file) {
    switch -Regex ($file) {
        '^sdk/src/.+\.ts$'                  { return 'src' }
        '^sdk/tests/unit/test-helpers\.ts$' { return 'test-helper' }
        '^sdk/tests/unit/.+\.test\.ts$'     { return 'unit-test' }
        '^sdk/tests/unit/.+'                { return 'build/config' }
        '^sdk/tests/integration/.+'         { return 'integration-test' }
        '^sdk/.+\.md$'                       { return 'doc' }
        '^sdk/.+\.(json|npmrc|gitignore)$'  { return 'build/config' }
        '^sdk/.*tsconfig.*'                  { return 'build/config' }
        default                              { return 'other' }
    }
}

# Resolve baseline from checkpoint when not supplied.
if (-not $Baseline -and (Test-Path $CheckpointPath)) {
    try {
        $cp = Get-Content $CheckpointPath -Raw | ConvertFrom-Json
        if ($cp.lastSyncedCommit) { $Baseline = $cp.lastSyncedCommit }
    } catch {
        Write-Json @{ mode = 'error'; message = "Checkpoint file at $CheckpointPath is not valid JSON: $($_.Exception.Message)" }
        exit 1
    }
}

# Confirm gh is available before any API call.
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Json @{ mode = 'error'; message = 'GitHub CLI (gh) not found on PATH. Install from https://cli.github.com/ and run `gh auth login`.' }
    exit 1
}

if (-not $Baseline) {
    # No baseline known: surface the current tip and recent SDK-touching commits.
    $headRaw  = gh api "repos/$Repo/commits/$Branch" 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Json @{ mode = 'error'; message = "gh api failed: $headRaw" }; exit 1 }
    $head = $headRaw | ConvertFrom-Json
    $recentRaw = gh api "repos/$Repo/commits?path=$Path&sha=$Branch&per_page=10" 2>&1
    $recent = if ($LASTEXITCODE -eq 0) { $recentRaw | ConvertFrom-Json } else { @() }
    Write-Json @{
        mode          = 'needs-baseline'
        repo          = $Repo
        branch        = $Branch
        path          = $Path
        currentHead   = $head.sha
        currentDate   = $head.commit.author.date
        recentCommits = @($recent | ForEach-Object {
            @{ sha = $_.sha; date = $_.commit.author.date; subject = ($_.commit.message -split "`n")[0] }
        })
    }
    exit 0
}

# Compute net diff + commit log since baseline.
$cmpRaw = gh api "repos/$Repo/compare/$Baseline...$Branch" 2>&1
if ($LASTEXITCODE -ne 0) { Write-Json @{ mode = 'error'; message = "gh api compare failed: $cmpRaw" }; exit 1 }
$cmp = $cmpRaw | ConvertFrom-Json

$sdkFiles = @($cmp.files | Where-Object { $_.filename -like "$Path/*" } | ForEach-Object {
    @{
        filename  = $_.filename
        status    = $_.status
        additions = $_.additions
        deletions = $_.deletions
        category  = (Get-Category $_.filename)
    }
})

$commits = @($cmp.commits | ForEach-Object {
    @{ sha = $_.sha; date = $_.commit.author.date; subject = ($_.commit.message -split "`n")[0] }
})

$headSha = if ($cmp.commits.Count -gt 0) { $cmp.commits[-1].sha } else { $Baseline }

Write-Json @{
    mode         = 'diff'
    repo         = $Repo
    branch       = $Branch
    path         = $Path
    baseline     = $Baseline
    head         = $headSha
    status       = $cmp.status
    aheadBy      = $cmp.ahead_by
    behindBy     = $cmp.behind_by
    totalCommits = $cmp.total_commits
    truncated    = ($cmp.total_commits -gt @($cmp.commits).Count)
    sdkFileCount = @($sdkFiles).Count
    commits      = $commits
    sdkFiles     = $sdkFiles
}
