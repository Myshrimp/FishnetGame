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

# Unity 6 renamed PhysicMaterial. Adapt only the disposable validation copy;
# the production project and source tests remain on their declared 2022.3 API.
$legacyTest = Join-Path $validationRoot 'Assets/Tests/Editor/NetworkProjectilePredictionTests.cs'
if (Test-Path -LiteralPath $legacyTest) {
    $source = [IO.File]::ReadAllText($legacyTest)
    $source = $source.Replace('PhysicMaterialCombine', 'PhysicsMaterialCombine')
    $source = $source.Replace('PhysicMaterial', 'PhysicsMaterial')
    [IO.File]::WriteAllText($legacyTest, $source, [Text.UTF8Encoding]::new($false))
}

$resultPath = Join-Path $validationRoot "$Label.xml"
$logPath = Join-Path $validationRoot "$Label.log"
$arguments = @(
    '-batchmode', '-nographics', '-projectPath', $validationRoot,
    '-runTests', '-testPlatform', 'EditMode',
    '-testResults', $resultPath, '-logFile', $logPath
)
if (![string]::IsNullOrWhiteSpace($TestFilter)) {
    $arguments += @('-testFilter', $TestFilter)
}
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
