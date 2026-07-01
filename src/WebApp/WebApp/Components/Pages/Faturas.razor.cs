using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace WebApp.Components.Pages;

public partial class Faturas : ComponentBase
{
    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    private string _userId = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        if (AuthState is not null)
        {
            var state = await AuthState;
            _userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        }
    }
}
