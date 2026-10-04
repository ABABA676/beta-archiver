<#
    Тесты «Беты». Собирает проект, если .exe ещё нет, и прогоняет самопроверку:
    упаковка → сигнатура 7z → список → распаковка → SHA-256 совпал → 7z t →
    переименованное расширение → уровни сжатия → прогресс → понятная ошибка.

      .\Тесты.ps1                 → использует обычную сборку
      .\Тесты.ps1 -SelfContained  → использует «толстый» один файл
#>
[CmdletBinding()]
param([switch]$SelfContained)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'Билд\Бета.exe'

if (-not (Test-Path $exe)) {
    Write-Host 'Сборки нет — собираем…' -ForegroundColor Cyan
    & (Join-Path $root 'build.ps1') -SelfContained:$SelfContained
}

$outLog = Join-Path $env:TEMP ('beta_tests_' + [Guid]::NewGuid().ToString('N').Substring(0, 6) + '.txt')
$errLog = $outLog + '.err'
$proc = Start-Process -FilePath $exe -ArgumentList '--selftest' -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput $outLog -RedirectStandardError $errLog
Get-Content -LiteralPath $outLog -Encoding UTF8 | ForEach-Object { Write-Host $_ }
if ((Test-Path $errLog) -and (Get-Item $errLog).Length -gt 0) {
    Write-Host '--- stderr ---' -ForegroundColor Yellow
    Get-Content -LiteralPath $errLog -Encoding UTF8 | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
}
if ($proc.ExitCode -ne 0) { throw "Тесты не прошли (код $($proc.ExitCode)). Лог: $outLog" }
Write-Host 'Тесты пройдены.' -ForegroundColor Green
