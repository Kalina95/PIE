using System.Text;
using System.Security.Claims;
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Mapping;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Services;
using HelpDeskHero.Shared.Contracts.Common;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class TicketsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly INotificationQueue _notificationQueue;
    private readonly ISlaCalculator _slaCalculator;
    private readonly ITicketAssignmentService _ticketAssignment;
    private readonly IOutboxWriter _outboxWriter;

    public TicketsController(
        AppDbContext db,
        AuditService audit,
        INotificationQueue notificationQueue,
        ISlaCalculator slaCalculator,
        ITicketAssignmentService ticketAssignment,
        IOutboxWriter outboxWriter)
    {
        _db = db;
        _audit = audit;
        _notificationQueue = notificationQueue;
        _slaCalculator = slaCalculator;
        _ticketAssignment = ticketAssignment;
        _outboxWriter = outboxWriter;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<TicketDto>>> GetAll([FromQuery] TicketQueryDto query, CancellationToken ct)
    {
        var q = _db.Tickets.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            q = q.Where(x =>
                x.Number.Contains(query.Search) ||
                x.Title.Contains(query.Search) ||
                x.Description.Contains(query.Search));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            q = q.Where(x => x.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Priority))
        {
            q = q.Where(x => x.Priority == query.Priority);
        }

        q = query.SortBy switch
        {
            "Title" => query.Desc ? q.OrderByDescending(x => x.Title) : q.OrderBy(x => x.Title),
            "Priority" => query.Desc ? q.OrderByDescending(x => x.Priority) : q.OrderBy(x => x.Priority),
            _ => query.Desc ? q.OrderByDescending(x => x.CreatedAtUtc) : q.OrderBy(x => x.CreatedAtUtc)
        };

        var totalCount = await q.CountAsync(ct);

        var items = await q
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(t => new TicketDto
            {
                Id = t.Id,
                Number = t.Number,
                Title = t.Title,
                Description = t.Description,
                Status = t.Status,
                Priority = t.Priority,
                CreatedAtUtc = t.CreatedAtUtc,
                UpdatedAtUtc = t.UpdatedAtUtc,
                RowVersionBase64 = Convert.ToBase64String(t.RowVersion),
                AssignedToUserId = t.AssignedToUserId,
                AssignedToDisplayName = _db.Users
                    .Where(u => u.Id == t.AssignedToUserId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefault(),
                DueFirstResponseAtUtc = t.DueFirstResponseAtUtc,
                DueResolveAtUtc = t.DueResolveAtUtc,
                FirstRespondedAtUtc = t.FirstRespondedAtUtc,
                ResolvedAtUtc = t.ResolvedAtUtc,
                EscalationLevel = t.EscalationLevel
            })
            .ToListAsync(ct);

        return Ok(new PagedResultDto<TicketDto>
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            Items = items
        });
    }

    [HttpGet("export")]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] string? status,
        [FromQuery] string? priority,
        CancellationToken ct)
    {
        var query = _db.Tickets.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        if (!string.IsNullOrWhiteSpace(priority))
            query = query.Where(x => x.Priority == priority);

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Number,
                x.Title,
                x.Status,
                x.Priority,
                x.CreatedAtUtc
            })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("Id,Number,Title,Status,Priority,CreatedAtUtc");

        foreach (var row in rows)
        {
            var title = row.Title.Replace("\"", "\"\"", StringComparison.Ordinal);
            sb.AppendLine($"{row.Id},{row.Number},\"{title}\",{row.Status},{row.Priority},{row.CreatedAtUtc:O}");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"tickets-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    [HttpGet("deleted")]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<ActionResult<IReadOnlyList<TicketDto>>> GetDeleted(CancellationToken ct)
    {
        var items = await _db.Tickets
            .IgnoreQueryFilters()
            .Where(x => x.IsDeleted)
            .OrderByDescending(x => x.DeletedAtUtc)
            .Select(t => new TicketDto
            {
                Id = t.Id,
                Number = t.Number,
                Title = t.Title,
                Description = t.Description,
                Status = t.Status,
                Priority = t.Priority,
                CreatedAtUtc = t.CreatedAtUtc,
                UpdatedAtUtc = t.UpdatedAtUtc,
                RowVersionBase64 = Convert.ToBase64String(t.RowVersion),
                AssignedToUserId = t.AssignedToUserId,
                AssignedToDisplayName = _db.Users
                    .Where(u => u.Id == t.AssignedToUserId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefault(),
                DueFirstResponseAtUtc = t.DueFirstResponseAtUtc,
                DueResolveAtUtc = t.DueResolveAtUtc,
                FirstRespondedAtUtc = t.FirstRespondedAtUtc,
                ResolvedAtUtc = t.ResolvedAtUtc,
                EscalationLevel = t.EscalationLevel
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TicketDto>> GetById(int id, CancellationToken ct)
    {
        var entity = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
            return NotFound();

        return Ok(await ToDtoAsync(entity, ct));
    }

    [HttpPost]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<ActionResult<TicketDto>> Create(CreateTicketDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            return BadRequest(new
            {
                code = "validation_error",
                errors = new Dictionary<string, string[]>
                {
                    ["Title"] = ["Title is required."]
                }
            });
        }

        if (string.IsNullOrWhiteSpace(dto.Description))
        {
            return BadRequest(new
            {
                code = "validation_error",
                errors = new Dictionary<string, string[]>
                {
                    ["Description"] = ["Description is required."]
                }
            });
        }

        var nextNumber = $"HDH-{DateTime.UtcNow:yyyyMMddHHmmss}";

        var entity = new Ticket
        {
            Number = nextNumber,
            Title = dto.Title.Trim(),
            Description = dto.Description.Trim(),
            Priority = dto.Priority,
            Status = "New",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty
        };

        _db.Tickets.Add(entity);
        await _slaCalculator.ApplySlaAsync(entity, ct);
        await _ticketAssignment.AssignAsync(entity, ct);

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("Create", "Ticket", entity.Id.ToString(), new { entity.Number, entity.Title }, ct);
        _notificationQueue.EnqueueTicketCreated(entity.Id);

        await _outboxWriter.AddAsync("TicketChanged", TicketLiveUpdateFactory.FromTicket(entity, "Created"), ct);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, await ToDtoAsync(entity, ct));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<IActionResult> Update(int id, UpdateTicketDto dto, CancellationToken ct)
    {
        var entity = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
            return NotFound();

        var originalRowVersion = Convert.FromBase64String(dto.RowVersionBase64);
        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = originalRowVersion;

        var oldPriority = entity.Priority;

        entity.Title = dto.Title.Trim();
        entity.Description = dto.Description.Trim();
        entity.Status = dto.Status;
        entity.Priority = dto.Priority;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        if (dto.Priority != oldPriority)
            await _slaCalculator.ApplySlaAsync(entity, ct);

        if (dto.Status is "Resolved" or "Closed")
        {
            entity.ResolvedAtUtc ??= DateTime.UtcNow;
        }
        else
        {
            entity.ResolvedAtUtc = null;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("Update", "Ticket", entity.Id.ToString(), new { entity.Number, entity.Title }, ct);

        await _outboxWriter.AddAsync("TicketChanged", TicketLiveUpdateFactory.FromTicket(entity, "Updated"), ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<IActionResult> SoftDelete(int id, CancellationToken ct)
    {
        var entity = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
            return NotFound();

        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedByUserId = User.Identity?.Name;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("SoftDelete", "Ticket", entity.Id.ToString(), new { entity.Number, entity.Title }, ct);

        await _outboxWriter.AddAsync("TicketChanged", TicketLiveUpdateFactory.FromTicket(entity, "Deleted"), ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{id:int}/restore")]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<IActionResult> Restore(int id, CancellationToken ct)
    {
        var ticket = await _db.Tickets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (ticket is null)
            return NotFound();

        if (!ticket.IsDeleted)
            return BadRequest(new { message = "Ticket is not deleted." });

        ticket.IsDeleted = false;
        ticket.DeletedAtUtc = null;
        ticket.DeletedByUserId = null;
        ticket.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("Restore", "Ticket", ticket.Id.ToString(), new { ticket.Number, ticket.Title }, ct);

        await _outboxWriter.AddAsync("TicketChanged", TicketLiveUpdateFactory.FromTicket(ticket, "Restored"), ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private async Task<TicketDto> ToDtoAsync(Ticket entity, CancellationToken ct)
    {
        string? display = null;
        if (!string.IsNullOrEmpty(entity.AssignedToUserId))
        {
            display = await _db.Users
                .Where(u => u.Id == entity.AssignedToUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        return TicketDtoMapper.ToDto(entity, display);
    }
}
