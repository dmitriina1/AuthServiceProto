# AuthServiceProto

Проверка, можно ли заменить Duende IdentityServer на бесплатный
[OpenIddict](https://github.com/openiddict/openiddict-core) без замены ASP.NET Core Identity.

## Что показывает прототип

- регистрация и вход по email/паролю работают через ASP.NET Core Identity;
- вход через Яндекс проходит через OpenIddict Client и создаёт локальную привязку пользователя;
- OpenIddict Server выдаёт токены по Authorization Code + PKCE (`S256`);
- одна браузерная сессия пользователя может получать разные access token для разных сервисов;
- демонстрационные API проверяют подпись токена, его получателя (`audience`) и разрешение (`scope`);
- пользователи, клиенты, scopes и токены хранятся в PostgreSQL.


## Самый простой запуск с Яндексом

Нужны .NET 10 SDK и Docker Desktop. Из PowerShell в папке проекта:

```powershell
Copy-Item .env.local.example .env.local
# Заполнить ClientId и ClientSecret в .env.local
.\scripts\Start-Local.ps1
```

Скрипт поднимает PostgreSQL в Docker, доверяет локальный HTTPS-сертификат .NET и запускает приложение
на `https://localhost:7036`. В кабинете Яндекс OAuth должен быть указан точный callback:

```text
https://localhost:7036/callback/login/yandex
```

Если Яндекс не нужен, `.env.local` можно не создавать. Кнопка появится только когда провайдер включён
и заданы оба значения.

## Запуск всего через Docker Compose и Nginx

Docker собирает Release-образ приложения, затем Compose
запускает PostgreSQL, auth-сервис и Nginx отдельными контейнерами. Из корня проекта выполнить:

```powershell
docker compose up --build -d
```


После запуска откройте в браузере `http://localhost:5230`. Порт `5230` ведёт в Nginx, а Nginx
перенаправляет запросы во внутренний контейнер auth-сервиса.


Чтобы также удалить тестовую БД и начать с чистого состояния:

```powershell
docker compose down --volumes
```

## Что открыть в браузере

- `/` - главная страница;
- `/Identity/Account/Register` - регистрация;
- `/Identity/Account/Login` - вход по email/паролю или через Яндекс;
- `/demo` - два PKCE-запроса для разных условных API;
- `/.well-known/openid-configuration` - OIDC discovery.

`Orders` и `Profile` - не настоящие сервисы. Они нужны, чтобы показать:
токен для одного API не должен давать доступ к другому API.

## Какие здесь ключи и сертификаты

- HTTPS developer certificate шифрует соединение браузера с `https://localhost:7036`.
- Временный signing key OpenIddict подписывает токены, а encryption key защищает auth. code/refresh token.
- ASP.NET Core Data Protection отдельно защищает браузерные cookie.

В проде временные ключи нужно заменить
постоянными управляемыми ключами, иначе перезапуск сделает старые токены недействительными.


## Ограничения прототипа

- ключи подписи/шифрования OpenIddict временные: после перезапуска старые токены недействительны;
- Data Protection использует стандартное локальное хранилище ASP.NET Core;
- схема БД создаётся через `EnsureCreated`: при изменении модели тестовый volume нужно пересоздать;
- подтверждение email, восстановление пароля, SMTP, production TLS, ротация ключей, мониторинг и rate limit
  не реализованы;
- клиенты и их разрешения записываются в БД при первом создании. Если изменить их
  в конфигурации, существующие записи в БД автоматически не обновятся;
- поддержан только вход пользователя через браузер: приложение получает одноразовый код,
  защищённо обменивает его на токены и может обновлять доступ без повторного входа.


# Доказательство что не используется duende:
Выполнить команду:
```
dotnet list AuthServiceProto.csproj package --include-transitive --no-restore |
    Select-String "Duende|IdentityServer4|ApiAuthorization.IdentityServer"
```
