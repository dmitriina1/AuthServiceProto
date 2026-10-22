using AuthServiceProto.Configuration;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthServiceProto.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        // EnsureCreated создаёт таблицы при первом запуске прототипа, но не обновляет
        // существующую схему. После изменения модели локальную БД пересоздают,
        // а рабочую схему обновляют миграциями (EF Core).
        var context = provider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var settings = provider.GetRequiredService<IConfiguration>()
            .GetSection(OpenIddictProvisioningOptions.SectionName)
            .Get<OpenIddictProvisioningOptions>() ?? new OpenIddictProvisioningOptions();

        var scopeManager = provider.GetRequiredService<IOpenIddictScopeManager>();
        foreach (var definition in settings.Scopes)
        {
            if (await scopeManager.FindByNameAsync(definition.Name) is not null)
            {
                continue;
            }

            var descriptor = new OpenIddictScopeDescriptor
            {
                Name = definition.Name,
                DisplayName = definition.DisplayName
            };
            descriptor.Resources.Add(definition.Resource);
            await scopeManager.CreateAsync(descriptor);
        }

        var applicationManager = provider.GetRequiredService<IOpenIddictApplicationManager>();
        foreach (var definition in settings.Clients)
        {
            if (await applicationManager.FindByClientIdAsync(definition.ClientId) is null)
            {
                await applicationManager.CreateAsync(CreateClientDescriptor(definition));
            }
        }
    }

    private static OpenIddictApplicationDescriptor CreateClientDescriptor(ClientDefinition definition)
    {
        // Браузерное приложение не может надёжно хранить client secret, поэтому клиент
        // публичный и вместо секрета обязан использовать PKCE.
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = definition.ClientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = definition.DisplayName
        };

        foreach (var uri in definition.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }
        foreach (var uri in definition.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        descriptor.Permissions.UnionWith([
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.EndSession,
            Permissions.Endpoints.Revocation,
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.ResponseTypes.Code
        ]);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);

        foreach (var requestedScope in definition.Scopes)
        {
            switch (requestedScope)
            {
                case Scopes.OfflineAccess:
                    descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
                    break;
                case Scopes.Email:
                    descriptor.Permissions.Add(Permissions.Scopes.Email);
                    break;
                case Scopes.Profile:
                    descriptor.Permissions.Add(Permissions.Scopes.Profile);
                    break;
                case not Scopes.OpenId:
                    descriptor.Permissions.Add(Permissions.Prefixes.Scope + requestedScope);
                    break;
            }
        }

        return descriptor;
    }
}
