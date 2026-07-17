$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'

& $msbuild "$root\NativePlugin\bnscleaner.vcxproj" /p:Configuration=Release /p:Platform=x64 /m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item "$root\NativePlugin\build\Releasex64\bnscleaner.dll" "$root\MemoryCleanerApp\Resources\bnscleaner.dll" -Force

dotnet clean "$root\MemoryCleanerApp\MemoryCleanerApp.csproj" -c Release -r win-x64
if (Test-Path "$root\release") {
    Get-ChildItem -LiteralPath "$root\release" -File | Remove-Item -Force
}
dotnet publish "$root\MemoryCleanerApp\MemoryCleanerApp.csproj" -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false -o "$root\release"
exit $LASTEXITCODE
