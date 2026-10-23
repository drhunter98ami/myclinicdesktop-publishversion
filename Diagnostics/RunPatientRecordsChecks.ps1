$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$checkFolder = Join-Path $repositoryRoot 'obj\PatientRecordsSmoke'
New-Item -ItemType Directory -Path $checkFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PatientRecordsSmoke.cs.txt') -Destination (Join-Path $checkFolder 'Program.cs') -Force
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0-windows</TargetFramework><UseWPF>true</UseWPF><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><ProjectReference Include="../../MyClinic.csproj"/></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $checkFolder 'PatientRecordsSmoke.csproj') -Encoding UTF8
Push-Location $repositoryRoot
try {
    dotnet run --project obj/PatientRecordsSmoke/PatientRecordsSmoke.csproj -v:q -p:WarningLevel=0
    if ($LASTEXITCODE -ne 0) { throw 'Patient records checks failed.' }
} finally { Pop-Location }
