using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AuthServiceProto.Data;

// Общая EF Core модель. IdentityDbContext добавляет таблицы пользователей, а вызов
// UseOpenIddict() в Program.cs добавляет таблицы клиентов, разрешений и токенов OpenIddict.
public sealed class ApplicationDbContext : IdentityDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
}
