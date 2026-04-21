# notatki

## Czym jest projekt (jednym zdaniem)

**System helpdesk w przeglądarce:** zgłoszenia (tickety), role (Admin / Agent / User), JWT, REST API, frontend **Blazor WebAssembly**, baza **SQL Server**, w tle **Hangfire** (zadania cykliczne), **SignalR** (odświeżanie listy i szczegółów w czasie rzeczywistym), **SLA** (terminy, auto-przypisanie do agenta, eskalacja przy przekroczeniu terminu rozwiązania), **outbox** (niezawodne powiadamianie UI po zapisie w bazie).

---

## Struktura repozytorium (co gdzie jest)

```
PIE/
├── src/
│   ├── HelpDeskHero.Shared/     # Wspólne DTO/kontrakty (TicketDto, auth, paginacja…)
│   ├── HelpDeskHero.Api/        # Backend: ASP.NET Core Web API, EF Core, Identity, Hangfire, SignalR
│   └── HelpDeskHero.UI/        # Frontend: Blazor WASM, HttpClient + token, klient SignalR
├── tests/
│   ├── HelpDeskHero.Api.IntegrationTests/   # Testy integracyjne API (WebApplicationFactory, in-memory DB)
│   └── HelpDeskHero.UI.Tests/              # bUnit (np. lista ticketów)
├── docker/                      # Dockerfile API i UI (nginx dla WASM)
├── docker-compose.yml           # SQL Server + API + UI (+ opcjonalnie RabbitMQ w profilu tools)
├── tools/                       # Osobne narzędzia (load generator, worker RabbitMQ) — nie są rdzeniem aplikacji
└── .env.example                 # Przykładowe zmienne dla Dockera
```

W repozytorium **może nie być pliku `.sln`** — projekty budujesz wskazując `.csproj`. Jeśli używasz solution, ścieżki z `dotnet sln add` muszą wskazywać istniejący plik solution (np. `.sln` lub `.slnx` zgodnie z Twoim plikiem).

---

## Technologie (stack)

| Warstwa | Technologia |
|--------|-------------|
| Runtime | .NET 10 (`net10.0`) |
| API | ASP.NET Core, Swagger (Development / Docker) |
| ORM | Entity Framework Core + SQL Server |
| Auth | ASP.NET Core Identity + **JWT** (Bearer), refresh token |
| Realtime | **SignalR** — hub `/hubs/tickets` (JWT z query `access_token` dla WebSocketów z WASM) |
| Tło | **Hangfire** + SQL Server (kolejka jobów, dashboard `/hangfire`) |
| Frontend | **Blazor WebAssembly**, `Microsoft.AspNetCore.SignalR.Client` |
| Testy API | xUnit + `WebApplicationFactory`, środowisko `Testing`, EF InMemory |
| Testy UI | bUnit |

---

## Uruchomienie lokalne (bez Dockera)

### Wymagania

- .NET SDK 10.x  
- **SQL Server** (LocalDB lub pełna instancja) — connection string w `HelpDeskHero.Api/appsettings.Development.json`  
- Dla HTTPS dev: zaufany certyfikat (`dotnet dev-certs https --trust`)

### Porty (domyślnie z `launchSettings`)

- **API:** `https://localhost:5001`, `http://localhost:5000`  
- **UI:** `https://localhost:7045`, `http://localhost:5045`

### UI — adres API

`HelpDeskHero.UI/wwwroot/appsettings.json` → `Api:BaseUrl` (np. `https://localhost:5001`) musi wskazywać **publiczny** adres API widziany z przeglądarki.

### Kolejność

1. Upewnij się, że baza jest dostępna (connection string).  
2. Uruchom API (migracje i seed lecą przy starcie poza `Testing`):  
   `dotnet run --project src/HelpDeskHero.Api/HelpDeskHero.Api.csproj`  
3. Uruchom UI:  
   `dotnet run --project src/HelpDeskHero.UI/HelpDeskHero.UI.csproj`  
4. Swagger (Development): np. `https://localhost:5001/swagger`

### Konto testowe (seed — `SeedData.cs`)

Po pierwszym uruchomieniu (migracje + seed):

| Użytkownik | Hasło   | Role |
|------------|---------|------|
| `admin`    | `Admin1234` | Admin, Agent |
| `agent`    | `Agent1234` | Agent |
| `user`     | `User1234`  | User |

Polityki **SLA** (Low / Medium / High / Critical) są dokładane w seedzie (`TicketSlaPolicy`).

---

## Docker Compose

Z katalogu głównego repozytorium:

```bash
docker compose up --build
```

- **UI:** http://localhost:8888  
- **API + Swagger:** http://localhost:5000/swagger  
- **Hangfire:** http://localhost:5000/hangfire  
- **SQL Server:** localhost:1433 (domyślne hasło SA jak w `docker-compose.yml` / `.env.example`)

Zmienne: skopiuj `.env.example` → `.env` i dostosuj porty / `JWT_KEY` / `SQL_SA_PASSWORD`.  
**API_PUBLIC_URL** w UI musi być zgodny z adresem API w przeglądarce (np. `http://localhost:5000`).

**RabbitMQ** (tylko pod narzędzia z `tools/`):  
`docker compose --profile tools up -d rabbitmq`  
Główna aplikacja **nie wymaga** RabbitMQ do działania helpdesku.

---

## Główne funkcje biznesowe (co się dzieje w systemie)

### Zgłoszenia (tickety)

- CRUD przez `TicketsController`, paginacja, filtry, eksport CSV.  
- **Soft delete** + **kosz** + **przywracanie**.  
- **Audit** zdarzeń (kto/co).

### SLA i przypisanie

- Przy **utworzeniu** ticketu: wyliczenie `DueFirstResponseAtUtc` / `DueResolveAtUtc` wg priorytetu (`SlaCalculator` + tabele `TicketSlaPolicy`).  
- **Auto-przypisanie** do agenta z najmniejszą liczbą aktywnych zgłoszeń (`TicketAssignmentService`).  
- Przy **zmianie priorytetu**: ponowne przeliczenie SLA od `CreatedAtUtc`.

### Outbox + SignalR

- Po istotnych zmianach zapis do **`OutboxMessages`** w tej samej transakcji co ticket (lub tuż po zapisie).  
- **`OutboxProcessorService`** (HostedService, ~co 5 s) wysyła do hubów SignalR zdarzenie `TicketChanged`.  
- Klient Blazor łączy się z `/hubs/tickets`, grupy `dashboard` i `ticket:{id}` — **lista i szczegóły** mogą się odświeżać bez ręcznego F5.  
- W środowisku **`Testing`** procesor outbox jest wyłączony (stabilne testy).

### Eskalacja SLA (Hangfire)

- Recurring job **`sla-monitor`** (co **5 minut**): `ISlaMonitorService.CheckBreachesAsync`.  
- Warunek: ticket aktywny (nie Closed/Resolved), `DueResolveAtUtc` w przeszłości, poziom eskalacji &lt; 5, cooldown od ostatniego powiadomienia (~**1 h**).  
- Skutek: wyższy `EscalationLevel`, wpis `TicketEscalations`, powiadomienia in-app (admini + agent), outbox `SlaBreached`.

### Powiadomienia

- Kanały: in-app (baza `UserNotifications`), e-mail i webhook (szkielety/konfiguracja).  
- Tworzenie ticketu może kolejkować job Hangfire (`EnqueueTicketCreated` → powiadomienia dla ról).  
- **Licznik nieprzeczytanych** w menu: `NotificationUnreadState` + odświeżanie m.in. po SignalR (`NavMenu`).

### Pliki

- Załączniki ticketów: `LocalFileStorage` + konfiguracja `FileStorage:RootPath` (w Dockerze wolumen).

---

## API — ważniejsze endpointy (orientacyjnie)

| Obszar | Przykład |
|--------|----------|
| Auth | `/api/auth/login`, refresh, wylogowanie |
| Tickety | `/api/tickets`, `/api/tickets/{id}`, create/update/delete/restore |
| Komentarze / załączniki | dedykowane kontrolery pod ticketId |
| Powiadomienia | `/api/notifications/mine`, oznaczanie przeczytanych |
| Audyt | `/api/audit` (Admin) |
| Admin | użytkownicy / role — wg `AdminController` |

Szczegóły: **Swagger** na uruchomionym API.

---

## Bezpieczeństwo i CORS

- **JWT** w nagłówku `Authorization: Bearer` dla HTTP API.  
- **SignalR (WASM):** token dodatkowo może iść w query `access_token` na ścieżkach `/hubs` (`OnMessageReceived` w JWT).  
- **CORS:** lista originów z konfiguracji `Cors:Origins` (domyślnie localhost UI dev; w Dockerze env `Cors__Origins__0` itd.).  
- Polityki: `CanManageTickets` (Admin, Agent), `AdminOnly`, `CanViewAudit`, itd.

---

## Testy

```bash
dotnet test tests/HelpDeskHero.Api.IntegrationTests/HelpDeskHero.Api.IntegrationTests.csproj
dotnet test tests/HelpDeskHero.UI.Tests/HelpDeskHero.UI.Tests.csproj
```

Integracja: in-memory DB, seed ról/użytkowników w `CustomWebApplicationFactory`, m.in. scenariusze ticketów i outbox.

---

## Migracje EF

Tworzenie migracji (z katalogu repo, ścieżka do projektu API):

```bash
dotnet ef migrations add NazwaMigracji --project src/HelpDeskHero.Api/HelpDeskHero.Api.csproj --startup-project src/HelpDeskHero.Api/HelpDeskHero.Api.csproj
```

Aplikacja przy starcie (nie-Testing) wywołuje `Database.MigrateAsync()` w seedzie.

---

## Typowe problemy (debug przed obroną)

### Certyfikat deweloperski HTTPS

```
The ASP.NET Core developer certificate is not trusted.
```

Naprawa:

```bash
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

### Zablokowany plik przy `dotnet build` (Windows)

Jeśli działa `HelpDeskHero.Api.exe` / inny proces trzyma DLL — zatrzymaj proces (np. zatrzymaj debugowanie, `taskkill` na PID) i zbuduj ponownie.

### Hangfire / SLA job — błąd LINQ

Zapytanie z `.TotalHours` na różnicy dat w LINQ do EF mogło nie przejść translacji SQL — w kodzie jest poprawka na warunek z datą (`LastNotifiedAtUtc <= now.AddHours(-1)`).

---

## Utworzenie projektu .NET (historia / komendy szkoleniowe)

### Utworzenie solution

`dotnet new sln -n HelpDeskHero` — tworzy plik rozwiązania grupujący projekty.

### Utworzenie projektów (przykład)

```bash
dotnet new classlib -n HelpDeskHero.Shared -o .\src\HelpDeskHero.Shared -f net10.0
dotnet new webapi -n HelpDeskHero.Api -o .\src\HelpDeskHero.Api -f net10.0
dotnet new blazorwasm -n HelpDeskHero.UI -o .\src\HelpDeskHero.UI -f net10.0
```

### Dodanie projektów do solution

```bash
dotnet sln HelpDeskHero.sln add .\src\HelpDeskHero.Shared\HelpDeskHero.Shared.csproj
dotnet sln HelpDeskHero.sln add .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj
dotnet sln HelpDeskHero.sln add .\src\HelpDeskHero.UI\HelpDeskHero.UI.csproj
```

(Użyj faktycznej nazwy pliku solution, jeśli masz `.slnx` lub inną.)

### Referencje

```bash
dotnet add .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj reference .\src\HelpDeskHero.Shared\HelpDeskHero.Shared.csproj
dotnet add .\src\HelpDeskHero.UI\HelpDeskHero.UI.csproj reference .\src\HelpDeskHero.Shared\HelpDeskHero.Shared.csproj
```

### NuGet (przykładowe pakiety API)

NuGet to menedżer pakietów .NET — biblioteki dodaje się przez `dotnet add package …`.


## Wnioski
1. C# .NET vs Java SpringBoot
   2. Wydaje mi się że C# + .NET w porównaniu do Java + SpringBoot jest prostszy w modularyzacji aplikacji.
   3. .NET wydaje się być bardziej zamkniętym ekosystemem.
   4. SpringBoot daje większą dowolność w narzędziach 
5. VisualStudio jest mało czytelne, szybko przeszedłem na Ridera
6. Projekt mógłby być podzielony na mikroserwisy ponieważ:
   7. mając serwis do zarządzania użytkownikami możemy go wyjąć i włożyć do innego systemu.
   8. wtedy ten system ticketowy może trzymać referencje do użytkownika a niekoniecznie dane użytkowników.
   9. łatwiej wtedy też migrować do dojżalszych systemów zarządzania użytkownikami np. keycloak
   10. pod względem architektonicznym wydzieloną encję user trudniej jest zabrudzić logiką biznesową gdy jest w innym serwisie.
11. bardzo mi się podoba definicja controllerów w asp.net i parametryzacja
