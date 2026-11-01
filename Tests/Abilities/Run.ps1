$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sources = @(
    (Join-Path $PSScriptRoot 'PlatformStubs.cs'),
    (Join-Path $PSScriptRoot 'AbilityRuntimeTests.cs')
)
$productionRoot = Join-Path $projectRoot 'Assets/Scripts/Abilities'
if (Test-Path -LiteralPath $productionRoot) {
    $sources += Get-ChildItem -LiteralPath $productionRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName
}

try {
    Add-Type -Path $sources -ErrorAction Stop
} catch {
    Write-Output $_.Exception.Message
    exit 1
}

exit [AbilityRuntimeTests]::Run()
