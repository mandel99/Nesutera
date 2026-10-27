param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $Publish,
    [string] $PublishDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src/Pharaoh.Desktop/Pharaoh.Desktop.csproj'
$buildArguments = @('build', $projectPath, '-c', $Configuration, '--nologo')
if ($Publish) {
    $outputDirectory = if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
        Join-Path $repositoryRoot 'artifacts/desktop/windows'
    } elseif ([IO.Path]::IsPathRooted($PublishDirectory)) {
        [IO.Path]::GetFullPath($PublishDirectory)
    } else {
        Join-Path $repositoryRoot $PublishDirectory
    }
    $buildArguments = @('publish', $projectPath, '-c', $Configuration,
        '--self-contained', 'false', '-o', $outputDirectory, '--nologo')
}
elseif (-not [string]::IsNullOrWhiteSpace($PublishDirectory)) {
    throw 'PublishDirectory requires -Publish.'
}

& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw "Desktop build failed with exit code $LASTEXITCODE." }
