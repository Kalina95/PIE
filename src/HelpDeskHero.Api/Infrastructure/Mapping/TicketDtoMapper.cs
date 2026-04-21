using HelpDeskHero.Api.Domain;
using HelpDeskHero.Shared.Contracts.Tickets;

namespace HelpDeskHero.Api.Infrastructure.Mapping;

public static class TicketDtoMapper
{
    public static TicketDto ToDto(Ticket entity, string? assignedDisplayName = null) =>
        new()
        {
            Id = entity.Id,
            Number = entity.Number,
            Title = entity.Title,
            Description = entity.Description,
            Status = entity.Status,
            Priority = entity.Priority,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            RowVersionBase64 = Convert.ToBase64String(entity.RowVersion),
            AssignedToUserId = entity.AssignedToUserId,
            AssignedToDisplayName = assignedDisplayName,
            DueFirstResponseAtUtc = entity.DueFirstResponseAtUtc,
            DueResolveAtUtc = entity.DueResolveAtUtc,
            FirstRespondedAtUtc = entity.FirstRespondedAtUtc,
            ResolvedAtUtc = entity.ResolvedAtUtc,
            EscalationLevel = entity.EscalationLevel
        };
}
