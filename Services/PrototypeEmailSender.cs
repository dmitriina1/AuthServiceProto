using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace AuthServiceProto.Services;

// В прототипе нет SMTP и подтверждения почты. Реализация нужна только потому, что
// встроенные страницы ASP.NET Core Identity ожидают оба email-интерфейса в DI.
public sealed class PrototypeEmailSender : IEmailSender, IEmailSender<IdentityUser>
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;

    public Task SendConfirmationLinkAsync(
        IdentityUser user,
        string email,
        string confirmationLink) => Task.CompletedTask;

    public Task SendPasswordResetLinkAsync(
        IdentityUser user,
        string email,
        string resetLink) => Task.CompletedTask;

    public Task SendPasswordResetCodeAsync(
        IdentityUser user,
        string email,
        string resetCode) => Task.CompletedTask;
}
