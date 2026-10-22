namespace AuthServiceProto.Configuration;

public sealed class AuthServerOptions
{
    public const string SectionName = "AuthServer";

    // Публичный адрес auth-сервиса, который записывается в выпущенные токены.
    public required string Issuer { get; init; }

    // Через сколько минут истекает access token для API.
    public int AccessTokenMinutes { get; init; } = 5;

    // Через сколько дней пользователь должен пройти полный вход заново.
    public int RefreshTokenDays { get; init; } = 14;
}

public sealed class IdentitySecurityOptions
{
    public const string SectionName = "Identity";

    // Минимальная длина локального пароля, остальные требования задаются в Program.cs.
    public int PasswordRequiredLength { get; init; } = 8;

    // Длительность блокировки после серии неудачных входов.
    public int LockoutMinutes { get; init; } = 15;

    // Сколько неверных паролей можно ввести до блокировки.
    public int MaxFailedAccessAttempts { get; init; } = 5;
}

public sealed class OpenIddictProvisioningOptions
{
    public const string SectionName = "OpenIddictProvisioning";

    // Разрешения для вызова демонстрационных API.
    public List<ScopeDefinition> Scopes { get; init; } = [];

    // Приложения, которым разрешено запрашивать токены у auth-сервиса.
    public List<ClientDefinition> Clients { get; init; } = [];
}

public sealed class ScopeDefinition
{
    // Техническое имя разрешения в OAuth-запросе, например orders_api.
    public required string Name { get; init; }

    // Человекочитаемое название для администрирования и будущего consent-экрана.
    public required string DisplayName { get; init; }

    // API, для которого будет предназначен access token с этим scope.
    public required string Resource { get; init; }
}

public sealed class ClientDefinition
{
    // Уникальный идентификатор приложения-клиента.
    public required string ClientId { get; init; }

    // Понятное название клиента для администрирования.
    public required string DisplayName { get; init; }

    // Точные адреса, куда auth-сервис может вернуть authorization code.
    public List<string> RedirectUris { get; init; } = [];

    // Точные адреса возврата после выхода.
    public List<string> PostLogoutRedirectUris { get; init; } = [];

    // Набор разрешений, которые этот клиент может запросить.
    public List<string> Scopes { get; init; } = [];
}

public sealed class ExternalProviderOptions
{
    // Показывать ли кнопку внешнего провайдера.
    public bool Enabled { get; init; }

    // Идентификатор приложения из кабинета Яндекс OAuth.
    public string ClientId { get; init; } = string.Empty;

    // Секрет приложения из кабинета Яндекс OAuth; хранится вне Git.
    public string ClientSecret { get; init; } = string.Empty;
}
