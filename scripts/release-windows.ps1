#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^v[0-9A-Za-z][0-9A-Za-z._-]*$')]
    [string]$Tag,

    [Parameter(Mandatory = $true, Position = 1)]
    [string]$ReleaseNotesPath,

    [string]$TargetCommit = 'HEAD',
    [string]$UnityPath,
    [string]$Repository,
    [string]$ApkPath,
    [string]$WindowsZipPath,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$temporaryGitHubToken = $false

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArguments)

    $output = & git @GitArguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArguments -join ' ') failed with exit code $LASTEXITCODE."
    }

    return ($output | Out-String).Trim()
}

function Invoke-NativeQuiet {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $FilePath @Arguments 2>$null)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
    }
}

function Resolve-GitHubCli {
    $command = Get-Command gh -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $installedPath = 'C:\Program Files\GitHub CLI\gh.exe'
    if (Test-Path -LiteralPath $installedPath -PathType Leaf) {
        return $installedPath
    }

    throw 'GitHub CLI was not found. Install it with: winget install --id GitHub.cli'
}

function Enable-GitHubAuthentication {
    param([string]$GitHubCli)

    if ($env:GH_TOKEN) {
        return $false
    }

    $tokenResult = Invoke-NativeQuiet $GitHubCli @('auth', 'token')
    if ($tokenResult.ExitCode -eq 0 -and $tokenResult.Output) {
        $env:GH_TOKEN = ($tokenResult.Output | Select-Object -First 1).Trim()
        return $true
    }

    $credentialInput = "protocol=https`nhost=github.com`n`n"
    $credentialLines = $credentialInput | git credential fill
    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub CLI is not authenticated and Git Credential Manager returned no credential.'
    }

    $passwordLine = $credentialLines |
        Where-Object { $_ -like 'password=*' } |
        Select-Object -First 1
    if (-not $passwordLine) {
        throw 'GitHub CLI is not authenticated. Run: gh auth login'
    }

    $env:GH_TOKEN = $passwordLine.Substring(9)
    return $true
}

function Resolve-RepositoryName {
    param([string]$ExplicitRepository)

    if ($ExplicitRepository) {
        return $ExplicitRepository
    }

    $remoteUrl = Invoke-Git remote get-url origin
    if ($remoteUrl -match 'github\.com[:/](?<name>[^/]+/[^/]+?)(?:\.git)?$') {
        return $Matches.name
    }

    throw 'Could not derive owner/repository from the origin remote. Pass -Repository owner/name.'
}

function Resolve-UnityEditor {
    param([string]$ExplicitPath)

    if ($ExplicitPath) {
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    if ($env:UNITY_EDITOR_PATH -and (Test-Path -LiteralPath $env:UNITY_EDITOR_PATH -PathType Leaf)) {
        return (Resolve-Path -LiteralPath $env:UNITY_EDITOR_PATH).Path
    }

    $projectVersionFile = Join-Path $repoRoot 'ProjectSettings\ProjectVersion.txt'
    $versionLine = Select-String -LiteralPath $projectVersionFile -Pattern '^m_EditorVersion:\s*(.+)$' |
        Select-Object -First 1
    if (-not $versionLine) {
        throw 'Could not read the Unity version from ProjectSettings/ProjectVersion.txt.'
    }

    $version = $versionLine.Matches[0].Groups[1].Value.Trim()
    $unityRoot = Split-Path -Parent (Split-Path -Parent $repoRoot)
    $candidates = @(
        (Join-Path $unityRoot "Editor\$version\Editor\Unity.exe"),
        (Join-Path ${env:ProgramFiles} "Unity\Hub\Editor\$version\Editor\Unity.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Unity $version was not found. Pass -UnityPath or set UNITY_EDITOR_PATH."
}

function Get-CommitHash {
    param([string]$Revision)

    return Invoke-Git rev-parse "$Revision`^{commit}"
}

function Get-LocalTagCommit {
    param([string]$ReleaseTag)

    $result = Invoke-NativeQuiet 'git' @('rev-list', '-n', '1', $ReleaseTag)
    if ($result.ExitCode -ne 0) {
        return $null
    }

    return ($result.Output | Out-String).Trim()
}

function Remove-SafeBuildOutput {
    param([string]$Path)

    $buildRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'Builds')).TrimEnd('\') + '\'
    $resolvedTarget = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedTarget.StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear an output outside the Builds directory: $resolvedTarget"
    }

    if (Test-Path -LiteralPath $resolvedTarget) {
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}

Push-Location $repoRoot
try {
    $notes = (Resolve-Path -LiteralPath $ReleaseNotesPath).Path
    if ([IO.Path]::GetExtension($notes) -ne '.md') {
        throw 'Release notes must be a Markdown (.md) file.'
    }

    $targetHash = Get-CommitHash $TargetCommit
    $headHash = Get-CommitHash 'HEAD'

    if (-not $SkipBuild -and $targetHash -ne $headHash) {
        throw 'Building a non-HEAD target is not supported. Use -SkipBuild with existing artifacts.'
    }

    if (-not $SkipBuild -or $targetHash -eq $headHash) {
        $workingTreeChanges = Invoke-Git status --porcelain
        if ($workingTreeChanges) {
            throw "The working tree is not clean. Commit or preserve changes before releasing:`n$workingTreeChanges"
        }
    }

    if ($SkipBuild) {
        if (-not $ApkPath) {
            throw '-SkipBuild requires -ApkPath.'
        }
        $apk = (Resolve-Path -LiteralPath $ApkPath).Path
        $windowsZip = if ($WindowsZipPath) {
            (Resolve-Path -LiteralPath $WindowsZipPath).Path
        }
        else {
            $null
        }
    }
    else {
        $editor = Resolve-UnityEditor $UnityPath
        $buildProfile = Join-Path $repoRoot 'Assets\Settings\Build Profiles\JumpingNinja.asset'
        if (-not (Test-Path -LiteralPath $buildProfile -PathType Leaf)) {
            throw "Android build profile not found: $buildProfile"
        }

        $buildDirectory = Join-Path $repoRoot 'Builds'
        $logDirectory = Join-Path $repoRoot 'Logs'
        New-Item -ItemType Directory -Force -Path $buildDirectory, $logDirectory | Out-Null
        $apk = Join-Path $buildDirectory "JumpingNinja-$Tag.apk"
        $androidLog = Join-Path $logDirectory "Release-$Tag-Android.log"
        $androidArguments = @(
            '-batchmode',
            '-nographics',
            '-quit',
            '-projectPath', "`"$repoRoot`"",
            '-activeBuildProfile', "`"$buildProfile`"",
            '-build', "`"$apk`"",
            '-logFile', "`"$androidLog`""
        )

        Write-Host "Building Android APK for $Tag with $editor"
        $androidProcess = Start-Process -FilePath $editor -ArgumentList $androidArguments -Wait -PassThru -WindowStyle Hidden
        if ($androidProcess.ExitCode -ne 0) {
            throw "Unity Android build failed with exit code $($androidProcess.ExitCode). See $androidLog"
        }
        if (-not (Select-String -LiteralPath $androidLog -SimpleMatch 'Build Finished, Result: Success' -Quiet)) {
            throw "Unity exited without a successful Android build marker. See $androidLog"
        }

        $windowsOutputDirectory = Join-Path $buildDirectory "JumpingNinja-$Tag-Windows"
        $windowsExecutable = Join-Path $windowsOutputDirectory 'Jumping Ninja.exe'
        $windowsZip = Join-Path $buildDirectory "JumpingNinja-$Tag-Windows.zip"
        $windowsLog = Join-Path $logDirectory "Release-$Tag-Windows.log"
        Remove-SafeBuildOutput $windowsOutputDirectory
        Remove-SafeBuildOutput $windowsZip
        $windowsArguments = @(
            '-batchmode',
            '-nographics',
            '-quit',
            '-projectPath', "`"$repoRoot`"",
            '-buildTarget', 'StandaloneWindows64',
            '-executeMethod', 'JumpingNinjaEditor.WindowsReleaseBuilder.Build',
            '-windowsOutputPath', "`"$windowsExecutable`"",
            '-logFile', "`"$windowsLog`""
        )

        Write-Host "Building Windows player for $Tag"
        $windowsProcess = Start-Process -FilePath $editor -ArgumentList $windowsArguments -Wait -PassThru -WindowStyle Hidden
        if ($windowsProcess.ExitCode -ne 0) {
            throw "Unity Windows build failed with exit code $($windowsProcess.ExitCode). See $windowsLog"
        }
        if (-not (Select-String -LiteralPath $windowsLog -SimpleMatch 'JUMPING_NINJA_WINDOWS_BUILD_OK' -Quiet)) {
            throw "Unity exited without a successful Windows build marker. See $windowsLog"
        }

        Compress-Archive -Path (Join-Path $windowsOutputDirectory '*') -DestinationPath $windowsZip -CompressionLevel Optimal
    }

    $apkItem = Get-Item -LiteralPath $apk
    if ($apkItem.Length -le 0) {
        throw "APK is empty: $apk"
    }
    $apkHash = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash
    $releaseArtifacts = @(
        [pscustomobject]@{
            Path = $apkItem.FullName
            Label = "Jumping Ninja $Tag Android APK"
            Name = 'Android APK'
            Size = $apkItem.Length
            Hash = $apkHash
        }
    )
    if ($windowsZip) {
        $windowsItem = Get-Item -LiteralPath $windowsZip
        if ($windowsItem.Length -le 0) {
            throw "Windows ZIP is empty: $windowsZip"
        }
        $releaseArtifacts += [pscustomobject]@{
            Path = $windowsItem.FullName
            Label = "Jumping Ninja $Tag Windows x64"
            Name = 'Windows x64 ZIP'
            Size = $windowsItem.Length
            Hash = (Get-FileHash -LiteralPath $windowsZip -Algorithm SHA256).Hash
        }
    }

    $localTagCommit = Get-LocalTagCommit $Tag
    if ($localTagCommit -and $localTagCommit -ne $targetHash) {
        throw "Local tag $Tag points to $localTagCommit instead of $targetHash."
    }

    $remoteTagLines = & git ls-remote --tags origin "refs/tags/$Tag" "refs/tags/$Tag`^{}"
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect remote tags.'
    }
    if ($remoteTagLines) {
        $remoteTagCommit = (($remoteTagLines | Select-Object -Last 1) -split '\s+')[0]
        if ($remoteTagCommit -ne $targetHash) {
            throw "Remote tag $Tag already points to $remoteTagCommit instead of $targetHash."
        }
    }

    $gh = Resolve-GitHubCli
    $temporaryGitHubToken = Enable-GitHubAuthentication $gh
    $repo = Resolve-RepositoryName $Repository

    $existingRelease = Invoke-NativeQuiet $gh @('release', 'view', $Tag, '--repo', $repo)
    if ($existingRelease.ExitCode -eq 0) {
        throw "GitHub Release $Tag already exists; refusing to overwrite it."
    }

    if ($targetHash -eq $headHash) {
        $branch = Invoke-Git branch --show-current
        if (-not $branch) {
            throw 'HEAD is detached. Pass a committed target with -TargetCommit or switch to a branch.'
        }
        Invoke-Git push origin $branch | Out-Null
    }

    if (-not $localTagCommit) {
        Invoke-Git tag -a $Tag $targetHash -m "Jumping Ninja $Tag" | Out-Null
    }
    if (-not $remoteTagLines) {
        Invoke-Git push origin $Tag | Out-Null
    }

    $releaseArguments = @(
        'release', 'create', $Tag,
        '--repo', $repo,
        '--verify-tag',
        '--title', "Jumping Ninja $Tag",
        '--notes-file', $notes
    )
    foreach ($artifact in $releaseArtifacts) {
        $releaseArguments += "$($artifact.Path)#$($artifact.Label)"
    }
    if ($Tag -match '-') {
        $releaseArguments += '--prerelease'
    }

    & $gh @releaseArguments
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub Release creation failed with exit code $LASTEXITCODE. The tag is kept so the command can be retried."
    }

    $releaseJson = & $gh release view $Tag --repo $repo --json url,assets,isDraft,isPrerelease
    if ($LASTEXITCODE -ne 0) {
        throw 'Release was created, but verification failed.'
    }

    Write-Host "Release complete: $Tag"
    foreach ($artifact in $releaseArtifacts) {
        Write-Host "$($artifact.Name): $($artifact.Path) ($($artifact.Size) bytes)"
        Write-Host "SHA-256: $($artifact.Hash)"
    }
    Write-Output $releaseJson
}
finally {
    if ($temporaryGitHubToken) {
        Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue
    }
    Pop-Location
}
