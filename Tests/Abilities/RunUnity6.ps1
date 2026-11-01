param(
    [string]$UnityPath = 'E:/Editors/UnityEditor/6000.4.8f1/Editor/Unity.exe',
    [string]$Label = 'ability-integration',
    [string]$TestFilter = 'PlayerStunPredictionTests.ChargeAbilityCommandActivatesAndStunCancelsItImmediately'
)

$ErrorActionPreference = 'Stop'
if ($Label -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid result label.' }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validationRoot = Join-Path $projectRoot 'Temp/AbilityValidation'
if (Test-Path -LiteralPath $validationRoot) {
    Remove-Item -LiteralPath $validationRoot -Recurse -Force
}
[IO.Directory]::CreateDirectory((Join-Path $validationRoot 'Assets')) | Out-Null

foreach ($folder in @('Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $folder) -Destination $validationRoot -Recurse -Force
}
foreach ($folder in @('FishNet', 'Scripts', 'Tests')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "Assets/$folder") -Destination (Join-Path $validationRoot 'Assets') -Recurse -Force
}

# Unity 6 renamed PhysicMaterial. The production project targets 2022.3, so omit only
# the incompatible legacy test file from this auxiliary compiler/test pass.
$legacyTest = Join-Path $validationRoot 'Assets/Tests/Editor/NetworkProjectilePredictionTests.cs'
if (Test-Path -LiteralPath $legacyTest) {
    Remove-Item -LiteralPath $legacyTest -Force
}

$resultPath = Join-Path $validationRoot "$Label.xml"
$logPath = Join-Path $validationRoot "$Label.log"
$arguments = @(
    '-batchmode', '-nographics', '-projectPath', $validationRoot,
    '-runTests', '-testPlatform', 'EditMode',
    '-testFilter', $TestFilter,
    '-testResults', $resultPath, '-logFile', $logPath
)
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
if (!(Test-Path -LiteralPath $resultPath)) {
    Get-Content -LiteralPath $logPath -Tail 100
    throw "Unity did not produce test results. Exit code: $($process.ExitCode)"
}
[xml]$results = Get-Content -LiteralPath $resultPath -Raw
$run = $results.'test-run'
Write-Output "Tests: $($run.total); passed: $($run.passed); failed: $($run.failed)"
foreach ($failure in $results.SelectNodes('//test-case[@result="Failed"]')) {
    Write-Output "$($failure.fullname): $($failure.failure.message.InnerText)"
}
Write-Output "Results: $resultPath"
if ([int]$run.total -lt 1 -or $run.result -ne 'Passed' -or $process.ExitCode -ne 0) { exit 1 }
