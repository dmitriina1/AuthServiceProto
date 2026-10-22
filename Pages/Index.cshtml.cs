using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuthServiceProto.Pages;

// Передаёт Razor-странице только флаг видимости локального PKCE-демо.
public sealed class IndexModel(IWebHostEnvironment environment) : PageModel
{
    public bool ShowDevelopmentDemo { get; private set; }

    public void OnGet() => ShowDevelopmentDemo =
        environment.IsDevelopment() || environment.IsEnvironment("Testing");
}
