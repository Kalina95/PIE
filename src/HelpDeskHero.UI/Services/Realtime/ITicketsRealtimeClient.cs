using HelpDeskHero.Shared.Contracts.Tickets;

namespace HelpDeskHero.UI.Services.Realtime;

public interface ITicketsRealtimeClient : IAsyncDisposable
{
    event Func<TicketLiveUpdateDto, Task>? TicketChanged;

    Task EnsureConnectedAsync(CancellationToken cancellationToken = default);

    Task JoinDashboardAsync();

    Task JoinTicketAsync(int ticketId);

    Task LeaveTicketAsync(int ticketId);
}
