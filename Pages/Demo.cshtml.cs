using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuthServiceProto.Pages;

// Не публикует тестовый PKCE-клиент за пределами Development.
public sealed class DemoModel(IWebHostEnvironment environment) : PageModel
{
    public IActionResult OnGet() => environment.IsDevelopment() ? Page() : NotFound();
}
