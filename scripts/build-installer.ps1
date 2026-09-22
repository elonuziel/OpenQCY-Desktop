[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.2.0',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [string]$TargetPlatformMinVersion = '10.0.22000.0',

    [string]$OutputBaseFilename = 'OpenQCY-Desktop-Setup',

    [string]$PublishDirectory,

    [string]$InstallerDirectory,

    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
if (-not $PublishDirectory) {
    $PublishDirectory = Join-Path $artifactsRoot 'publish'
}
if (-not $InstallerDirectory) {
    $InstallerDirectory = Join-Path $artifactsRoot 'installer'
}
$installerScript = Join-Path $repositoryRoot 'installer\OpenQCY.iss'

function Reset-GeneratedDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $resolvedArtifactsRoot = [IO.Path]::GetFullPath($artifactsRoot).TrimEnd('\') + '\'
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedArtifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside artifacts: $resolvedPath"
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $resolvedPath -Force | Out-Null
}

function Find-InnoCompiler {
    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $candidates = @(
        (Join-Path $programFilesX86 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    if (-not $candidates) {
        throw 'Inno Setup was not found. Install it with: winget install --id JRSoftware.InnoSetup -e'
    }

    return $candidates | Select-Object -First 1
}

if (-not (Test-Path -LiteralPath $InstallerDirectory)) {
    New-Item -ItemType Directory -Path $InstallerDirectory -Force | Out-Null
}
$targetInstallerFile = Join-Path $InstallerDirectory "$OutputBaseFilename.exe"
if (Test-Path -LiteralPath $targetInstallerFile) {
    Remove-Item -LiteralPath $targetInstallerFile -Force
}

Push-Location $repositoryRoot
try {
    if ($SkipPublish) {
        $publishedExecutable = Join-Path $PublishDirectory 'OpenQCY.Desktop.exe'
        if (-not (Test-Path -LiteralPath $publishedExecutable)) {
            throw "Published application was not found: $publishedExecutable"
        }
    }
    else {
        Reset-GeneratedDirectory -Path $PublishDirectory

        dotnet restore OpenQCY.Desktop.csproj -r $RuntimeIdentifier -p:PublishReadyToRun=true
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $LASTEXITCODE"
        }

        $publishArguments = @(
            'publish',
            'OpenQCY.Desktop.csproj',
            '-c', $Configuration,
            '-p:Platform=x64',
            '-r', $RuntimeIdentifier,
            '--self-contained', 'true',
            '-p:WindowsAppSDKSelfContained=true',
            "-p:Version=$Version",
            "-p:TargetPlatformMinVersion=$TargetPlatformMinVersion",
            '--no-restore',
            '-o', $PublishDirectory
        )
        & dotnet @publishArguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }
    }

    $minVersion = $TargetPlatformMinVersion
    if ($minVersion -match '^(\d+\.\d+\.\d+)\.\d+$') {
        $minVersion = $Matches[1]
    }

    $iscc = Find-InnoCompiler
    $innoArguments = @(
        "/DAppVersion=$Version",
        "/DSourceDir=$PublishDirectory",
        "/DOutputDir=$InstallerDirectory",
        "/DOutputBaseFilename=$OutputBaseFilename",
        "/DMinVersion=$minVersion",
        $installerScript
    )
    & $iscc @innoArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

$installer = Join-Path $InstallerDirectory "$OutputBaseFilename.exe"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Installer was not generated: $installer"
}

$hash = Get-FileHash -LiteralPath $installer -Algorithm SHA256
Write-Host "Installer: $installer"
Write-Host "SHA-256:  $($hash.Hash)"
