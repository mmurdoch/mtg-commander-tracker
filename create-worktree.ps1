param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Name,

    # Branch to base the new branch on; defaults to the current HEAD.
    [string]$BaseBranch = 'HEAD'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (git -C $PSScriptRoot rev-parse --show-toplevel).Trim()
$repoName = Split-Path $repoRoot -Leaf
$dirName = $Name -replace '[\\/]', '-'
$worktreePath = Join-Path (Split-Path $repoRoot -Parent) "$repoName-$dirName"

git check-ref-format --branch $Name *> $null
if ($LASTEXITCODE -ne 0) {
    throw "'$Name' is not a valid branch name."
}

git -C $repoRoot show-ref --verify --quiet "refs/heads/$Name"
if ($LASTEXITCODE -eq 0) {
    throw "Branch '$Name' already exists."
}

if (Test-Path $worktreePath) {
    throw "Path '$worktreePath' already exists."
}

git -C $repoRoot worktree add -b $Name $worktreePath $BaseBranch
if ($LASTEXITCODE -ne 0) {
    throw "git worktree add failed."
}

Write-Host "Created branch '$Name' and worktree at '$worktreePath'."
