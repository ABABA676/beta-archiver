<#
    Сборка «Беты» — один настоящий .exe

      .\build.ps1                 → лёгкая сборка (~0,37 МБ, нужен .NET 8 Desktop Runtime)
      .\build.ps1 -SelfContained  → один файл (~150 МБ), работает на любом компьютере
      .\build.ps1 -NoUiTest       → пропустить проверку окна (быстрее, ничего не показывается)

    После сборки автоматически прогоняется самопроверка (--selftest).
    Проверка окна (--uitest) окно на экране НЕ показывает, но если она не нужна —
    пропускайте её через -NoUiTest.
#>
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$NoUiTest,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'Код\Бета.csproj'
$outDir = Join-Path $root 'Билд'
$exe = Join-Path $outDir 'Бета.exe'

Write-Host '==> Сборка' -ForegroundColor Cyan
& dotnet publish $proj -c $Configuration -r win-x64 "--self-contained:$($SelfContained.IsPresent)" `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:GenerateDocumentationFile=false `
    -o $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish упал (код $LASTEXITCODE)" }

if (-not (Test-Path $exe)) { throw "Сборка прошла, но $exe не найден" }
$size = [math]::Round((Get-Item $exe).Length / 1MB, 2)
Write-Host "==> Готово: $exe ($size МБ)" -ForegroundColor Green

Write-Host '==> Проверки' -ForegroundColor Cyan
# GUI-приложение (WinExe): перенаправление через *> не работает — нужен Start-Process -Wait -RedirectStandardOutput.
$failed = 0
$modes = if ($NoUiTest) { '--selftest' } else { '--selftest', '--uitest' }
foreach ($mode in $modes) {
    $stamp = [Guid]::NewGuid().ToString('N').Substring(0, 6)
    $outLog = Join-Path $env:TEMP "beta_$($mode.TrimStart('-'))`_$stamp.txt"
    $errLog = $outLog + '.err'
    Write-Host "-- $mode" -ForegroundColor DarkGray
    $proc = Start-Process -FilePath $exe -ArgumentList $mode -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $outLog -RedirectStandardError $errLog
    if (Test-Path $outLog) { Get-Content -LiteralPath $outLog -Encoding UTF8 | ForEach-Object { Write-Host $_ } }
    if ((Test-Path $errLog) -and (Get-Item $errLog).Length -gt 0) {
        Write-Host '--- stderr ---' -ForegroundColor Yellow
        Get-Content -LiteralPath $errLog -Encoding UTF8 | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
    }
    if ($proc.ExitCode -ne 0) { $failed++; Write-Host "-- $mode — код $($proc.ExitCode), лог $outLog" -ForegroundColor Red }
}
if ($failed -gt 0) { throw "Проверки не прошли ($failed). Подробности в логах в %TEMP%." }

Write-Host '==> Все проверки зелёные. Запуск:' -NoNewline -ForegroundColor Green
Write-Host " `"$exe`"" -ForegroundColor White
