using MyHelper.Core.Models;
using MyHelper.Core.Services;

namespace MyHelper.App.Pages;

public sealed class IndexModel : Microsoft.AspNetCore.Mvc.RazorPages.PageModel
{
    private readonly ISessionManager _sessions;

    public IndexModel(ISessionManager sessions)
    {
        _sessions = sessions;
    }

    public IReadOnlyList<SessionInfoDto> ActiveSessions { get; private set; } = [];

    public void OnGet()
    {
        ActiveSessions = _sessions.GetActiveSessions();
    }
}
