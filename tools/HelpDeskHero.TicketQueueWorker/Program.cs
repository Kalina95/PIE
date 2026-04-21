using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "HDH_")
    .AddCommandLine(args)
    .Build();

var baseUrl = configuration["ApiBaseUrl"] ?? "https://localhost:5001";
var apiUserName = configuration["UserName"] ?? "admin";
var password = configuration["Password"] ?? "";
var deviceName = configuration["DeviceName"] ?? "TicketQueueWorker";

var rabbitSection = configuration.GetSection("RabbitMQ");
var hostName = rabbitSection["HostName"] ?? "localhost";
var port = rabbitSection.GetValue("Port", 5672);
var virtualHost = rabbitSection["VirtualHost"] ?? "/";
var rabbitUser = rabbitSection["UserName"] ?? "hdh";
var rabbitPass = rabbitSection["Password"] ?? "hdh";
var queueName = rabbitSection["QueueName"] ?? "helpdesk.ticket.create";

if (string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine("Ustaw hasło API w appsettings.json lub zmienną HDH_Password.");
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
};

var handler = new HttpClientHandler();
if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var bu) &&
    string.Equals(bu.Host, "localhost", StringComparison.OrdinalIgnoreCase))
{
    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
}

using var http = new HttpClient(handler) { BaseAddress = new Uri(EnsureTrailingSlash(baseUrl)) };
var session = new AuthSession(http, json, apiUserName, password, deviceName);

var factory = new ConnectionFactory
{
    HostName = hostName,
    Port = port,
    VirtualHost = virtualHost,
    UserName = rabbitUser,
    Password = rabbitPass
};

try
{
    await using var connection = await factory.CreateConnectionAsync(cts.Token);
    await using var channel = await connection.CreateChannelAsync(cancellationToken: cts.Token);

    await channel.QueueDeclareAsync(
        queue: queueName,
        durable: true,
        exclusive: false,
        autoDelete: false,
        arguments: null,
        cancellationToken: cts.Token);

    await channel.BasicQosAsync(0, 1, false, cts.Token);

    await session.EnsureLoggedInAsync(cts.Token);

    var consumer = new AsyncEventingBasicConsumer(channel);
    consumer.ReceivedAsync += (_, ea) => HandleMessageAsync(channel, ea, http, session, json, cts.Token);

    await channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer, cancellationToken: cts.Token);

    Console.WriteLine($"API: {baseUrl} (użytkownik: {apiUserName})");
    Console.WriteLine($"RabbitMQ: {hostName}:{port}, kolejka: {queueName}");
    Console.WriteLine("Oczekiwanie na wiadomości (Ctrl+C kończy).");
    Console.WriteLine();

    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
    }
    catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
    {
        Console.WriteLine("Zakończono.");
    }

    return 0;
}
catch (BrokerUnreachableException ex)
{
    Console.Error.WriteLine($"Brak połączenia z RabbitMQ ({hostName}:{port}).");
    Console.Error.WriteLine("Uruchom brokera z katalogu głównego repozytorium PIE:");
    Console.Error.WriteLine("  docker compose up -d");
    Console.Error.WriteLine("albo zainstaluj RabbitMQ i ustaw RabbitMQ:HostName / Port w appsettings.json.");
    Console.Error.WriteLine("(Docker: uruchom Docker Desktop, potem ponów powyższe polecenie).");
    Console.Error.WriteLine("Szczegóły: " + ex.Message);
    return 1;
}

static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

static async Task HandleMessageAsync(
    IChannel channel,
    BasicDeliverEventArgs ea,
    HttpClient http,
    AuthSession session,
    JsonSerializerOptions json,
    CancellationToken ct)
{
    try
    {
        var text = Encoding.UTF8.GetString(ea.Body.ToArray());
        var dto = JsonSerializer.Deserialize<CreateTicketDto>(text, json);
        if (dto is null)
        {
            Console.Error.WriteLine("[worker] Pusty lub niepoprawny JSON — ack bez retry.");
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        await session.EnsureValidTokenAsync(ct);

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/tickets")
        {
            Content = JsonContent.Create(dto, options: json)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        using var resp = await http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var ticket = await resp.Content.ReadFromJsonAsync<TicketDto>(json, ct);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Utworzono {ticket?.Number ?? "?"} — {dto.Title}");
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        var code = (int)resp.StatusCode;
        var err = await resp.Content.ReadAsStringAsync(ct);
        if (code >= 500)
        {
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] API {code}, requeue: {err}");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: ct);
            return;
        }

        Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] API {code} (bez requeue): {err}");
        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: CancellationToken.None);
        throw;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] {ex.Message}");
        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: ct);
    }
}

internal sealed class AuthSession
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json;
    private readonly string _userName;
    private readonly string _password;
    private readonly string _deviceName;

    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _accessExpiresAt;

    public AuthSession(HttpClient http, JsonSerializerOptions json, string userName, string password, string deviceName)
    {
        _http = http;
        _json = json;
        _userName = userName;
        _password = password;
        _deviceName = deviceName;
    }

    public string? AccessToken => _accessToken;

    public async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        var login = new LoginRequestDto
        {
            UserName = _userName,
            Password = _password,
            DeviceName = _deviceName
        };

        using var resp = await _http.PostAsJsonAsync("api/auth/login", login, _json, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Logowanie nieudane ({(int)resp.StatusCode}): {body}");
        }

        var tokens = await resp.Content.ReadFromJsonAsync<TokenResponseDto>(_json, ct)
            ?? throw new InvalidOperationException("Brak treści odpowiedzi logowania.");
        ApplyTokens(tokens);
    }

    public async Task EnsureValidTokenAsync(CancellationToken ct)
    {
        if (_accessToken is null)
        {
            await EnsureLoggedInAsync(ct);
            return;
        }

        if (DateTimeOffset.UtcNow < _accessExpiresAt.AddMinutes(-2))
            return;

        var refresh = new RefreshRequestDto
        {
            RefreshToken = _refreshToken ?? "",
            DeviceName = _deviceName
        };

        using var resp = await _http.PostAsJsonAsync("api/auth/refresh", refresh, _json, ct);
        if (!resp.IsSuccessStatusCode)
        {
            await EnsureLoggedInAsync(ct);
            return;
        }

        var tokens = await resp.Content.ReadFromJsonAsync<TokenResponseDto>(_json, ct)
            ?? throw new InvalidOperationException("Brak treści odpowiedzi odświeżania.");
        ApplyTokens(tokens);
    }

    private void ApplyTokens(TokenResponseDto dto)
    {
        _accessToken = dto.AccessToken;
        _refreshToken = dto.RefreshToken;
        _accessExpiresAt = new DateTimeOffset(DateTime.SpecifyKind(dto.AccessTokenExpiresAtUtc, DateTimeKind.Utc));
    }
}
