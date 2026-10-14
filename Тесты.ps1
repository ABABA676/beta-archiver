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

# Пересобираем всегда, если попросили -SelfContained: иначе флаг молча игнорировался бы
# при уже существующем exe, и пользователь получил бы не тот файл, о котором думает.
# И ещё: если исходник новее exe — тоже пересобираем. Иначе тесты идут по СТАРОМУ
# бинарнику: правишь Engine.cs, видишь «зелёные тесты», а проверял старый код.
$newestSrc = Get-ChildItem -LiteralPath (Join-Path $root 'Код') -Recurse -File `
                -Include *.cs, *.csproj -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1
$needBuild = $SelfContained -or -not (Test-Path $exe)
if (-not $needBuild -and $newestSrc) {
    $needBuild = $newestSrc.LastWriteTime -gt (Get-Item -LiteralPath $exe).LastWriteTime
}
if ($needBuild) {
    if ($SelfContained) { $why = '-SelfContained' }
    elseif (-not (Test-Path $exe)) { $why = 'сборки нет' }
    else { $why = "исходник новее exe ($($newestSrc.Name))" }
    Write-Host "Собираем: $why" -ForegroundColor Cyan
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
