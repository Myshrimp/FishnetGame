$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testOutput = Join-Path $projectRoot 'Temp/NetRpcChannelTests'
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
    <Compile Include="$sourceRoot/Tests/Networking/*.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/Networking/NetChannel.cs" Condition="Exists('$sourceRoot/Assets/Scripts/Networking/NetChannel.cs')" />
    <Compile Include="$sourceRoot/Assets/Scripts/Networking/INetObjectBridge.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/Networking/NetBehaviour.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/Networking/NetObject.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/Networking/NetRpcAttribute.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/Networking/NetSerializer.cs" />
    <Compile Include="$sourceRoot/Assets/Scripts/Networking/NetVar.cs" />
  </ItemGroup>
</Project>
"@
$projectPath = Join-Path $testOutput 'NetRpcChannelTests.csproj'
[IO.File]::WriteAllText($projectPath, $project, [Text.UTF8Encoding]::new($false))
dotnet run --project $projectPath --verbosity quiet
exit $LASTEXITCODE
