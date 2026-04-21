using System.Text;
using System.Text.Json;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "HDH_")
    .AddCommandLine(args)
    .Build();

var rabbitSection = configuration.GetSection("RabbitMQ");
var hostName = rabbitSection["HostName"] ?? "localhost";
var port = rabbitSection.GetValue("Port", 5672);
var virtualHost = rabbitSection["VirtualHost"] ?? "/";
var userName = rabbitSection["UserName"] ?? "hdh";
var password = rabbitSection["Password"] ?? "hdh";
var queueName = rabbitSection["QueueName"] ?? "helpdesk.ticket.create";

var minSec = configuration.GetValue("MinIntervalSeconds", 10);
var maxSec = configuration.GetValue("MaxIntervalSeconds", 20);

if (maxSec < minSec || minSec < 1)
{
    Console.Error.WriteLine("MinIntervalSeconds / MaxIntervalSeconds są niepoprawne.");
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
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

var factory = new ConnectionFactory
{
    HostName = hostName,
    Port = port,
    VirtualHost = virtualHost,
    UserName = userName,
    Password = password
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

    Console.WriteLine($"RabbitMQ: {hostName}:{port}, kolejka: {queueName}");
    Console.WriteLine($"Odstępy: {minSec}–{maxSec} s (Ctrl+C kończy).");
    Console.WriteLine();

    var rnd = Random.Shared;

    try
    {
        while (!cts.Token.IsCancellationRequested)
        {
            var dto = TicketSamples.RandomTicket(rnd);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto, json));

            var props = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent
            };

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: queueName,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: cts.Token);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Wysłano do kolejki — {dto.Title}");

            var delay = TimeSpan.FromSeconds(rnd.Next(minSec, maxSec + 1));
            await Task.Delay(delay, cts.Token);
        }
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

file static class TicketSamples
{
    private static readonly string[] Priorities = ["Low", "Medium", "High"];

    private static readonly string[] Titles =
    [
        "Problem z dostępem", "Błąd aplikacji", "Prośba o instalację", "Wolna sieć",
        "Nie działa drukarka", "Reset hasła", "Nowy sprzęt", "Zgłoszenie testowe"
    ];

    private static readonly string[] Areas =
    [
        "ERP", "poczta", "VPN", "Wi‑Fi", "drukarka", "skaner", "laptop", "telefon"
    ];

    private static readonly string[] Openings =
    [
        "Użytkownik zgłasza,", "Od rana występuje,", "Pilne:", "Intermittent:",
        "Po aktualizacji", "Na stanowisku w biurze"
    ];

    private static readonly string[] Details =
    [
        "nie można się zalogować.", "aplikacja się zawiesza.", "brak odpowiedzi z serwera.",
        "komunikat o błędzie 500.", "timeout połączenia.", "urządzenie nie jest widoczne w sieci."
    ];

    private static readonly string[] Closings =
    [
        "Proszę o kontakt.", "Potrzebny restart usługi?", "Załączam zrzut ekranu w kolejnej wiadomości.",
        "Dotyczy jednego stanowiska.", "Powtarza się co kilka godzin."
    ];

    public static CreateTicketDto RandomTicket(Random rnd)
    {
        var title = $"{Pick(rnd, Titles)} — {Pick(rnd, Areas)} [{rnd.Next(1000, 9999)}]";
        var description = $"{Pick(rnd, Openings)} {Pick(rnd, Details)} {Pick(rnd, Closings)}";
        var priority = Pick(rnd, Priorities);
        return new CreateTicketDto
        {
            Title = title,
            Description = description,
            Priority = priority
        };
    }

    private static string Pick(Random rnd, string[] items) => items[rnd.Next(items.Length)];
}
