# Путеводитель по коду

## Как проходит обычный вход

```text
Login/Register -> ASP.NET Core Identity -> браузерная cookie
              -> /connect/authorize -> authorization code
              -> /connect/token + PKCE verifier -> access/id/refresh token
```

Identity хранит пользователя и пароль. OpenIddict реализует OAuth 2.0/OpenID Connect и выдаёт токены.
Это разные задачи, поэтому обе библиотеки нужны одновременно.

## Как проходит вход через Яндекс

1. Страница входа запускает OpenIddict Client.
2. Браузер переходит на Яндекс; пароль Яндекса наш сервис не видит.
3. Яндекс возвращает браузер на `/callback/login/yandex`.
4. OpenIddict проверяет ответ, а `ExternalAuthenticationController` создаёт временную Identity cookie.
5. Встроенная страница Identity связывает пару `Yandex + внешний ID` с локальным пользователем.

Совпавшего email недостаточно для автоматического объединения аккаунтов: надёжной привязкой является
идентификатор пользователя, выданный конкретным провайдером.

## Основные файлы

- `Program.cs` - PostgreSQL, Identity, OpenIddict и HTTP pipeline.
- `Configuration/AuthServiceOptions.cs` - пояснённые поля конфигурации.
- `Data/ApplicationDbContext.cs` - общая EF Core модель Identity и OpenIddict.
- `Data/DatabaseInitializer.cs` - создаёт схему прототипа и демонстрационные clients/scopes.
- `Controllers/AuthorizationController.cs` - `/connect/authorize`, `/connect/token` и logout.
- `Controllers/ExternalAuthenticationController.cs` - возврат после Яндекса.
- `Controllers/DemoApiController.cs` - два защищённых учебных API.
- `Areas/Identity/Pages/Account` - изменённые страницы входа и регистрации.
- `Pages/Demo.*` и `wwwroot/js/demo.js` - браузерная демонстрация PKCE.

## Зачем два демонстрационных API

Они показывают разницу между аутентификацией и авторизацией. Валидная подпись доказывает, что токен
выпустил наш auth-сервис, но этого мало. Orders API дополнительно требует `aud=orders-api` и
`scope=orders_api`, а Profile API требует собственную пару значений. Поэтому токен одного сервиса
не становится универсальным пропуском во все сервисы.

## Pages, Views и Areas

- `Pages` - основной интерфейс на Razor Pages.
- `Views` - одна MVC View для подтверждения выхода из OIDC-сессии.
- `Areas/Identity` - стандартное место ASP.NET Core Identity для переопределённых страниц аккаунта.
- `_ViewImports.cshtml` подключает пространства имён и Tag Helpers для вложенных страниц.
- `_ViewStart.cshtml` выбирает общий layout.


## Что делает Compose

`compose.yaml` поднимает три контейнера: PostgreSQL -> auth-приложение -> Nginx. Nginx доступен на
`http://localhost:5230`. `compose.development.yaml` используется только `Start-Local.ps1`: в этом режиме
в Docker работает одна БД, а приложение запускается через `dotnet run` с локальным HTTPS.
