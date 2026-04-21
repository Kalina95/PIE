using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Notifications;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Application.Services;

public sealed class SlaMonitorService : ISlaMonitorService
{
    private const int MaxEscalationLevel = 5;
    private readonly AppDbContext _db;
    private readonly IOutboxWriter _outboxWriter;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly UserManager<ApplicationUser> _userManager;

    public SlaMonitorService(
        AppDbContext db,
        IOutboxWriter outboxWriter,
        INotificationDispatcher notificationDispatcher,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _outboxWriter = outboxWriter;
        _notificationDispatcher = notificationDispatcher;
        _userManager = userManager;
    }

    public async Task CheckBreachesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // Granica „co najmniej 1 h od ostatniego powiadomienia” — musi dać się przetłumaczyć na SQL (bez TotalHours na TimeSpan).
        var notifyCooldownThresholdUtc = now.AddHours(-1);

        var breached = await _db.Tickets
            .Where(x => !x.IsDeleted)
            .Where(x => x.Status != "Closed" && x.Status != "Resolved")
            .Where(x => x.ResolvedAtUtc == null)
            .Where(x => x.DueResolveAtUtc != null && x.DueResolveAtUtc < now)
            .Where(x => x.EscalationLevel < MaxEscalationLevel)
            .Where(x => x.LastNotifiedAtUtc == null || x.LastNotifiedAtUtc <= notifyCooldownThresholdUtc)
            .ToListAsync(ct);

        if (breached.Count == 0)
            return;

        var admins = await _userManager.GetUsersInRoleAsync("Admin");

        foreach (var ticket in breached)
        {
            ticket.EscalationLevel++;
            ticket.LastNotifiedAtUtc = now;

            _db.TicketEscalations.Add(new TicketEscalation
            {
                TicketId = ticket.Id,
                EscalationLevel = ticket.EscalationLevel,
                TriggeredAtUtc = now,
                Reason = "Przekroczony termin rozwiązania (SLA).",
                AssignedToUserId = ticket.AssignedToUserId,
                NotificationSent = false
            });

            await _outboxWriter.AddAsync("TicketChanged", new TicketLiveUpdateDto
            {
                TicketId = ticket.Id,
                EventType = "SlaBreached",
                Status = ticket.Status,
                Priority = ticket.Priority,
                AssignedToUserId = ticket.AssignedToUserId,
                EscalationLevel = ticket.EscalationLevel,
                ChangedAtUtc = now
            }, ct);

            var subject = $"SLA: {ticket.Number}";
            var body = $"Zgłoszenie {ticket.Number} przekroczyło termin rozwiązania (poziom eskalacji {ticket.EscalationLevel}).";

            foreach (var admin in admins)
            {
                await _notificationDispatcher.DispatchAsync(new NotificationMessage
                {
                    Channel = NotificationChannel.InApp,
                    UserId = admin.Id,
                    Subject = subject,
                    Body = body
                }, ct);
            }

            if (!string.IsNullOrEmpty(ticket.AssignedToUserId) &&
                admins.All(a => a.Id != ticket.AssignedToUserId))
            {
                await _notificationDispatcher.DispatchAsync(new NotificationMessage
                {
                    Channel = NotificationChannel.InApp,
                    UserId = ticket.AssignedToUserId,
                    Subject = subject,
                    Body = body
                }, ct);
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
