using HelpDeskHero.Shared.Contracts.Tickets;
using HelpDeskHero.UI.Services.Auth;
using Microsoft.AspNetCore.SignalR.Client;

namespace HelpDeskHero.UI.Services.Realtime;

public sealed class TicketsRealtimeClient : ITicketsRealtimeClient
{
    private readonly IConfiguration _configuration;
    private readonly TokenStore _tokenStore;
    private HubConnection? _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TicketsRealtimeClient(IConfiguration configuration, TokenStore tokenStore)
    {
        _configuration = configuration;
        _tokenStore = tokenStore;
    }

    public event Func<TicketLiveUpdateDto, Task>? TicketChanged;

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var token = await _tokenStore.GetAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
                return;

            if (_connection is { State: HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting })
                return;

            if (_connection is not null)
                await _connection.DisposeAsync();

            var baseUrl = (_configuration["Api:BaseUrl"] ?? "").TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return;

            _connection = new HubConnectionBuilder()
                .WithUrl($"{baseUrl}/hubs/tickets", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                })
                .WithAutomaticReconnect()
                .Build();

            _connection.On<TicketLiveUpdateDto>("TicketChanged", async dto =>
            {
                if (TicketChanged is not null)
                    await TicketChanged.Invoke(dto);
            });

            await _connection.StartAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task JoinDashboardAsync()
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("JoinDashboard");
    }

    public async Task JoinTicketAsync(int ticketId)
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("JoinTicket", ticketId);
    }

    public async Task LeaveTicketAsync(int ticketId)
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("LeaveTicket", ticketId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
