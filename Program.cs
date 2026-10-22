using AuthServiceProto;
using AuthServiceProto.Configuration;
using AuthServiceProto.Data;
using AuthServiceProto.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Не задана строка подключения DefaultConnection.");
var authServer = builder.Configuration.GetRequiredSection(AuthServerOptions.SectionName)
    .Get<AuthServerOptions>()
    ?? throw new InvalidOperationException("Не найдена секция AuthServer.");
var identitySecurity = builder.Configuration.GetSection(IdentitySecurityOptions.SectionName)
    .Get<IdentitySecurityOptions>() ?? new IdentitySecurityOptions();
var provisioning = builder.Configuration.GetSection(OpenIddictProvisioningOptions.SectionName)
    .Get<OpenIddictProvisioningOptions>() ?? new OpenIddictProvisioningOptions();

// Issuer - публичный адрес auth-сервиса, записывается в токены, чтобы API могли
// проверить, кто их выпустил. Значение должно совпадать с адресом, который видит клиент.
if (!Uri.TryCreate(authServer.Issuer, UriKind.Absolute, out var issuer))
{
    throw new InvalidOperationException("AuthServer:Issuer должен быть абсолютным URL.");
}

// PostgreSQL хранит две группы таблиц: пользователей ASP.NET Core Identity и данные
// OpenIddict (клиенты, scopes, выданные разрешения и токены).
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure());
    options.UseOpenIddict();
});

builder.Services
    .AddDefaultIdentity<IdentityUser>(options =>
    {
        // Identity отвечает за локальный email/пароль, хеш пароля, блокировку и связь с Яндексом.
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = identitySecurity.PasswordRequiredLength;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(identitySecurity.LockoutMinutes);
        options.Lockout.MaxFailedAccessAttempts = identitySecurity.MaxFailedAccessAttempts;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Основная cookie хранит вход пользователя в браузере. External cookie существует
// только во время возврата от Яндекса к локальной учётной записи Identity.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.ExternalScheme, options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// В текущей версии (прототипе) подтверждение почты и восстановление пароля не нужны. Но нужна заглушка для
// встроенной страницы внешнего входа Identity, которая ожидает email-сервис в DI.
builder.Services.AddSingleton<PrototypeEmailSender>();
builder.Services.AddSingleton<IEmailSender>(services => services.GetRequiredService<PrototypeEmailSender>());
builder.Services.AddSingleton<IEmailSender<IdentityUser>>(
    services => services.GetRequiredService<PrototypeEmailSender>());

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddRazorPages();

// OpenIddict используется сразу в четырёх ролях:
// - Core хранит данные протокола в PostgreSQL;
// - Client выполняет вход через Яндекс;
// - Server заменяет Duende и выдаёт токены;
// - Validation проверяет access token в двух демонстрационных API.
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
            .UseDbContext<ApplicationDbContext>();
    })
    .AddClient(options =>
    {
        options.AllowAuthorizationCodeFlow();
        options.SetRedirectionEndpointUris("callback/login/yandex");
        options.AddEphemeralEncryptionKey()
            .AddEphemeralSigningKey();

        var aspNetCore = options.UseAspNetCore()
            .EnableRedirectionEndpointPassthrough();

        // OpenIddict по умолчанию запрещает OAuth по HTTP. Исключение нужно только для
        // локального Compose на localhost, а проверка Яндекса выполняется через HTTPS-профиль.
        if (issuer.Scheme == Uri.UriSchemeHttp)
        {
            aspNetCore.DisableTransportSecurityRequirement();
        }

        options.UseSystemNetHttp()
            .SetProductInformation(typeof(Program).Assembly);

        var yandex = builder.Configuration.GetSection("ExternalProviders:Yandex")
            .Get<ExternalProviderOptions>() ?? new ExternalProviderOptions();
        if (yandex.Enabled)
        {
            if (string.IsNullOrWhiteSpace(yandex.ClientId) || string.IsNullOrWhiteSpace(yandex.ClientSecret))
            {
                throw new InvalidOperationException(
                    "Яндекс включён, но ExternalProviders:Yandex:ClientId/ClientSecret не заполнены.");
            }

            options.UseWebProviders()
                .AddYandex(provider => provider
                    .SetClientId(yandex.ClientId)
                    .SetClientSecret(yandex.ClientSecret)
                    .SetRedirectUri("callback/login/yandex")
                    .AddScopes("login:info", "login:email"));
        }
    })
    .AddServer(options =>
    {
        options.SetIssuer(issuer);
        options.SetAuthorizationEndpointUris("connect/authorize")
            .SetTokenEndpointUris("connect/token")
            .SetEndSessionEndpointUris("connect/logout")
            .SetRevocationEndpointUris("connect/revocation");

        // Разрешаем вход через код авторизации. PKCE обязателен, а из методов
        // его проверки оставляем только S256.
        options.AllowAuthorizationCodeFlow()
            .AllowRefreshTokenFlow();
        options.RequireProofKeyForCodeExchange();
        options.Configure(configuration =>
            configuration.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));

        var serviceScopes = provisioning.Scopes.Select(scope => scope.Name).ToArray();
        options.RegisterScopes([Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, .. serviceScopes]);

        options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(5));
        options.SetIdentityTokenLifetime(TimeSpan.FromMinutes(5));
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(authServer.AccessTokenMinutes));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(authServer.RefreshTokenDays));

        // Токен доступа выдаётся как подписанный JWT: содержимое видно клиенту,
        // но изменить его незаметно нельзя.
        options.DisableAccessTokenEncryption();

        // Временные ключи упрощают локальный запуск, но меняются при каждом перезапуске.
        // При внедрении их заменяют постоянными сертификатами.
        options.AddEphemeralEncryptionKey()
            .AddEphemeralSigningKey();

        var aspNetCore = options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough();
        if (issuer.Scheme == Uri.UriSchemeHttp)
        {
            aspNetCore.DisableTransportSecurityRequirement();
        }
    })
    .AddValidation(options =>
    {
        // API запущены в одном процессе с сервером OpenIddict,
        // поэтому проверка токенов использует его настройки напрямую. В отдельных
        // API адрес сервера авторизации и параметры проверки нужно настраивать.
        options.UseLocalServer();
        options.EnableTokenEntryValidation();
        options.UseAspNetCore();
    });

// После проверки подлинности токена каждый API также убеждается, что токен выдан
// именно для него и разрешает нужную операцию.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthConstants.OrdersPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            context.User.HasAudience(AuthConstants.OrdersResource) &&
            context.User.HasScope(AuthConstants.OrdersScope));
    });
    options.AddPolicy(AuthConstants.ProfilePolicy, policy =>
    {
        policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            context.User.HasAudience(AuthConstants.ProfileResource) &&
            context.User.HasScope(AuthConstants.ProfileScope));
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
}

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; base-uri 'none'; frame-ancestors 'none'; " +
        "form-action 'self' https://oauth.yandex.ru https://passport.yandex.ru; " +
        "object-src 'none'; img-src 'self' data:; script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; connect-src 'self'";
    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllers();
app.MapRazorPages().WithStaticAssets();

// При первом запуске прототип создаёт таблицы и настройки OpenIddict.
// После изменения модели локальную БД нужно пересоздать (в проде использовать миграции).
await DatabaseInitializer.InitializeAsync(app.Services);
await app.RunAsync();
