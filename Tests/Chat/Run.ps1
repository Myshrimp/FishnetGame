$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testOutput = Join-Path $projectRoot 'Temp/ChatTests'
[IO.Directory]::CreateDirectory($testOutput) | Out-Null
$sourceRoot = [Security.SecurityElement]::Escape($projectRoot)
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$sourceRoot/Tests/Chat/*.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/FrameSync/Separated/IService.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/FrameSync/Separated/Services/ServiceBase.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/FrameSync/Separated/Services/ChatService.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/FrameSync/Separated/ServerMain.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/FrameSync/Separated/ServerGame.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/FrameSync/Separated/GameMain.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/FrameSync/Separated/GameState.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/FrameSync/Separated/Protocol.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/FrameSync/Separated/Test/TestBase.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/Core/EventBus.cs" />
    <Reference Include="Newtonsoft.Json">
      <HintPath>$sourceRoot/Library/PackageCache/com.unity.nuget.newtonsoft-json@3.2.1/Runtime/AOT/Newtonsoft.Json.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
"@
$projectPath = Join-Path $testOutput 'ChatTests.csproj'
[IO.File]::WriteAllText($projectPath, $project, [Text.UTF8Encoding]::new($false))
dotnet run --project $projectPath --verbosity quiet
exit $LASTEXITCODE
