$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$checkFolder = Join-Path $repositoryRoot 'obj\StatisticsSmoke'
New-Item -ItemType Directory -Path $checkFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'StatisticsSmoke.cs.txt') -Destination (Join-Path $checkFolder 'Program.cs') -Force
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="../../MyClinic.csproj" /></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $checkFolder 'StatisticsSmoke.csproj') -Encoding UTF8
Push-Location $repositoryRoot
try {
    dotnet run --project obj/StatisticsSmoke/StatisticsSmoke.csproj -v:q -p:WarningLevel=0
    if ($LASTEXITCODE -ne 0) { throw 'Statistics regression checks failed.' }
}
finally { Pop-Location }
