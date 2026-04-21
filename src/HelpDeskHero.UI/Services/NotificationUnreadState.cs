using HelpDeskHero.UI.Services.Api;

namespace HelpDeskHero.UI.Services;

/// <summary>
/// Współdzielony licznik nieprzeczytanych powiadomień (menu boczne + strona powiadomień).
/// </summary>
public sealed class NotificationUnreadState
{
    private readonly NotificationApiClient _api;

    public NotificationUnreadState(NotificationApiClient api)
    {
        _api = api;
    }

    public int UnreadCount { get; private set; }

    public event Action? Changed;

    /// <summary>Ustawia licznik na podstawie już pobranej listy (bez dodatkowego żądania HTTP).</summary>
    public void SetUnreadCount(int count)
    {
        if (count == UnreadCount)
            return;

        UnreadCount = count;
        Changed?.Invoke();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var items = await _api.GetMineAsync(cancellationToken);
        var count = items.Count(x => !x.IsRead);
        if (count == UnreadCount)
            return;

        UnreadCount = count;
        Changed?.Invoke();
    }
}
