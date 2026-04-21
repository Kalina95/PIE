# HelpDeskHero

System helpdesk w przegl─ůdarce: zg┼éoszenia (tickety), role (Admin / Agent / User), JWT, REST API, frontend **Blazor WebAssembly**, baza **SQL Server**, w tle **Hangfire** (zadania cykliczne), **SignalR** (live updates), **SLA** (terminy, auto-przypisanie, eskalacja) oraz wzorzec **Outbox** dla niezawodnych powiadomie┼ä.

---

## Wnioski

Wykonano **7 z 8 tutoriali**. Kolejka (RabbitMQ) zosta┼éa wystawiona jako **osobny serwis** (`tools/HelpDeskHero.TicketQueueWorker`) ÔÇö g┼é├│wna aplikacja helpdesk dzia┼éa niezale┼╝nie i nie wymaga jej do dzia┼éania.

- C# + .NET w por├│wnaniu do Java + Spring Boot wydaje si─Ö prostszy w modularyzacji aplikacji.
- .NET jest bardziej zamkni─Ötym ekosystemem; Spring Boot daje wi─Öksz─ů dowolno┼Ť─ç w doborze narz─Ödzi.
- Visual Studio jest ma┼éo czytelne ÔÇö szybko przeszed┼éem na **JetBrains Rider**.
- Projekt m├│g┼éby zosta─ç podzielony na mikroserwisy ÔÇö wydzielenie serwisu u┼╝ytkownik├│w pozwoli┼éoby na ┼éatwiejsz─ů migracj─Ö do dojrzalszych rozwi─ůza┼ä (np. Keycloak) i zapobiega┼éoby zabrudzeniu encji `User` logik─ů biznesow─ů.
- Bardzo czytelna definicja kontroler├│w w ASP.NET Core i ich parametryzacja przez atrybuty.

---

## Stos technologiczny

| Warstwa | Technologia |
|---|---|
| Runtime | .NET 10 |
| API | ASP.NET Core Web API, Swagger |
| ORM | Entity Framework Core + SQL Server |
| Auth | ASP.NET Core Identity + JWT (Bearer) + Refresh Token |
| Realtime | SignalR ÔÇö hub `/hubs/tickets` |
| Kolejka zada┼ä | Hangfire + SQL Server, dashboard `/hangfire` |
| Frontend | Blazor WebAssembly |
| Testy API | xUnit + WebApplicationFactory + EF InMemory |
| Testy UI | bUnit |
| Infrastruktura | Docker Compose (SQL Server + API + UI/nginx) |

---

## Struktura repozytorium

```
PIE/
ÔöťÔöÇÔöÇ src/
Ôöé   ÔöťÔöÇÔöÇ HelpDeskHero.Shared/     # Wsp├│lne DTO i kontrakty (auth, tickety, paginacjaÔÇŽ)
Ôöé   ÔöťÔöÇÔöÇ HelpDeskHero.Api/        # Backend: ASP.NET Core, EF Core, Identity, Hangfire, SignalR
Ôöé   ÔööÔöÇÔöÇ HelpDeskHero.UI/         # Frontend: Blazor WASM, klient SignalR
ÔöťÔöÇÔöÇ tests/
Ôöé   ÔöťÔöÇÔöÇ HelpDeskHero.Api.IntegrationTests/
Ôöé   ÔööÔöÇÔöÇ HelpDeskHero.UI.Tests/
ÔöťÔöÇÔöÇ tools/
Ôöé   ÔöťÔöÇÔöÇ HelpDeskHero.TicketLoadGenerator/   # Generator obci─ů┼╝enia (RabbitMQ)
Ôöé   ÔööÔöÇÔöÇ HelpDeskHero.TicketQueueWorker/     # Worker kolejki (RabbitMQ)
ÔöťÔöÇÔöÇ docker/                      # Dockerfile dla API i UI (nginx)
ÔöťÔöÇÔöÇ docker-compose.yml
ÔööÔöÇÔöÇ .env.example                 # Przyk┼éadowe zmienne ┼Ťrodowiskowe
```

---

## Uruchomienie ÔÇö Docker (zalecane)

```bash
docker compose up --build
```

| Us┼éuga | Adres |
|---|---|
| UI | http://localhost:8888 |
| API + Swagger | http://localhost:5000/swagger |
| Hangfire | http://localhost:5000/hangfire |
| SQL Server | localhost:1433 |

**Opcjonalnie ÔÇö RabbitMQ** (wymagany tylko przez narz─Ödzia z `tools/`):

```bash
docker compose --profile tools up -d rabbitmq
```

### Konfiguracja ┼Ťrodowiska

Skopiuj `.env.example` Ôćĺ `.env` i dostosuj warto┼Ťci:

```bash
cp .env.example .env
```

Kluczowe zmienne: `JWT_KEY`, `SQL_SA_PASSWORD`, `API_PUBLIC_URL` (adres API widoczny z przegl─ůdarki).

---

## Uruchomienie lokalne (bez Dockera)

**Wymagania:** .NET SDK 10.x, SQL Server (LocalDB lub pe┼éna instancja)

```bash
# 1. Uruchom API (migracje i seed wykonaj─ů si─Ö automatycznie przy starcie)
dotnet run --project src/HelpDeskHero.Api/HelpDeskHero.Api.csproj

# 2. Uruchom UI
dotnet run --project src/HelpDeskHero.UI/HelpDeskHero.UI.csproj
```

Domy┼Ťlne porty: API `https://localhost:5001`, UI `https://localhost:7045`.  
Adres API dla UI ustawiasz w `src/HelpDeskHero.UI/wwwroot/appsettings.json` Ôćĺ `Api:BaseUrl`.

---

## Konta testowe (seed)

| Login | Has┼éo | Rola |
|---|---|---|
| `admin` | `Admin1234` | Admin, Agent |
| `agent` | `Agent1234` | Agent |
| `user` | `User1234` | User |

---

## G┼é├│wne funkcje

### Zg┼éoszenia (tickety)
- CRUD z paginacj─ů, filtrami i eksportem CSV
- Soft delete, kosz i przywracanie
- Audit log zdarze┼ä (kto, co, kiedy)

### SLA i przypisanie
- Automatyczne wyliczenie `DueFirstResponseAtUtc` / `DueResolveAtUtc` wg priorytetu przy tworzeniu ticketu
- Auto-przypisanie do agenta z najmniejsz─ů liczb─ů aktywnych zg┼éosze┼ä
- Przeliczenie SLA przy zmianie priorytetu

### Outbox + SignalR
- Zapis do `OutboxMessages` w tej samej transakcji co zmiana ticketu
- `OutboxProcessorService` (co ~5 s) wysy┼éa zdarzenia `TicketChanged` do hub├│w SignalR
- Klient Blazor subskrybuje grupy `dashboard` i `ticket:{id}` ÔÇö od┼Ťwie┼╝anie bez F5

### Eskalacja SLA (Hangfire)
- Recurring job `sla-monitor` co 5 minut
- Warunek: ticket aktywny, `DueResolveAtUtc` przekroczony, cooldown ~1 h
- Skutek: wy┼╝szy `EscalationLevel`, wpis eskalacji, powiadomienia in-app

### Powiadomienia
- Kana┼éy: in-app (baza `UserNotifications`), e-mail, webhook
- Licznik nieprzeczytanych w menu, od┼Ťwie┼╝any przez SignalR

### Za┼é─ůczniki
- `LocalFileStorage` + konfiguracja `FileStorage:RootPath` (w Dockerze ÔÇö wolumen)

---

## Testy

```bash
dotnet test tests/HelpDeskHero.Api.IntegrationTests/HelpDeskHero.Api.IntegrationTests.csproj
dotnet test tests/HelpDeskHero.UI.Tests/HelpDeskHero.UI.Tests.csproj
```

---

## Migracje EF Core

```bash
dotnet ef migrations add NazwaMigracji \
  --project src/HelpDeskHero.Api/HelpDeskHero.Api.csproj \
  --startup-project src/HelpDeskHero.Api/HelpDeskHero.Api.csproj
```

Migracje s─ů aplikowane automatycznie przy starcie API (poza ┼Ťrodowiskiem `Testing`).

---

## Certyfikat deweloperski HTTPS

Je┼Ťli przegl─ůdarka zg┼éasza b┼é─ůd certyfikatu przy uruchomieniu lokalnym:

```bash
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

---

## Screeny z aplikacji

**Logowanie**
![Logowanie](docs/screenshot-login.png)

**Pulpit**
![Pulpit](docs/screenshot-pulpit.png)

**Lista zgłoszeń**
![Zgłoszenia](docs/screenshot-zgloszenia.png)

**Nowe zgłoszenie**
![Nowe zgłoszenie](docs/screenshot-nowe-zgloszenie.png)

**Kosz — usunięte zgłoszenia**
![Kosz](docs/screenshot-kosz.png)

**Powiadomienia SLA**
![Powiadomienia](docs/screenshot-powiadomienia.png)

**Panel administratora**
![Panel administratora](docs/screenshot-panel-admina.png)

**Audyt zdarzeń**
![Audyt zdarzeń](docs/screenshot-audyt.png)

**Hangfire Dashboard**
![Hangfire](docs/screenshot-hangfire.png)

