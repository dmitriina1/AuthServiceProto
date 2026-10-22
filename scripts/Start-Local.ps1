[CmdletBinding()]
param(
    [switch]$SkipCertificateTrust
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

Push-Location $projectRoot
try {
    $localEnvironmentFile = Join-Path $projectRoot '.env.local'
    if (Test-Path -LiteralPath $localEnvironmentFile) {
        # Минимальный parser понимает только KEY=VALUE. Кавычки, многострочные значения
        # и подстановка переменных не поддерживаются.
        foreach ($line in Get-Content -LiteralPath $localEnvironmentFile) {
            $trimmedLine = $line.Trim()
            if ($trimmedLine.Length -eq 0 -or $trimmedLine.StartsWith('#')) {
                continue
            }

            $separator = $trimmedLine.IndexOf('=')
            if ($separator -le 0) {
                throw "Некорректная строка в .env.local: '$trimmedLine'."
            }

            $name = $trimmedLine.Substring(0, $separator).Trim()
            $value = $trimmedLine.Substring($separator + 1).Trim()
            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }

    docker version --format '{{.Server.Version}}' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker Desktop не запущен или команда docker недоступна.'
    }

    dotnet --version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Не найден .NET SDK. Для проекта требуется .NET 10 SDK.'
    }

    if (-not $SkipCertificateTrust) {
        dotnet dev-certs https --trust
        if ($LASTEXITCODE -ne 0) {
            throw 'Не удалось создать/добавить в доверенные локальный HTTPS-сертификат.'
        }
    }

    # В Docker запускается только БД; ASP.NET Core остаётся обычным локальным процессом.
    docker compose -f compose.development.yaml up -d --wait
    if ($LASTEXITCODE -ne 0) {
        throw 'Не удалось запустить локальный PostgreSQL.'
    }

    # Переменная действует только внутри этого процесса и его дочернего dotnet run.
    $env:ConnectionStrings__DefaultConnection =
        'Host=localhost;Port=55432;Database=authservice;Username=authservice;Password=authservice-development-only'

    Write-Host ''
    Write-Host 'После запуска откройте: https://localhost:7036/' -ForegroundColor Green
    Write-Host 'Остановка приложения: Ctrl+C' -ForegroundColor DarkGray
    Write-Host 'Остановка PostgreSQL: docker compose -f compose.development.yaml down' -ForegroundColor DarkGray
    Write-Host ''

    dotnet run --launch-profile https
    if ($LASTEXITCODE -ne 0) {
        throw 'AuthService завершился с ошибкой.'
    }
}
finally {
    Pop-Location
}
