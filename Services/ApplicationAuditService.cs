using System.Security.Claims;
using System.Text.Json;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public record AuditLogFilter(string? Application, string? Actor, string? Municipality, string? EventType,
    DateTime? From, DateTime? To, int Page = 1);
public record AuditLogOptions(List<string> Actors, List<string> Municipalities, List<string> EventTypes);
public record AuditLogResult(List<ApplicationAuditLog> Records, AuditLogOptions Options, int TotalCount,
    int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed class ApplicationAuditService(IServiceScopeFactory scopes)
{
    public const int PageSize = 50;

    public static void Add(ApplicationDbContext db, Application application, ApplicationUser actor,
        string actorRole, string eventType, string summary, string? previousStatus = null,
        string? newStatus = null, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        db.ApplicationAuditLogs.Add(new ApplicationAuditLog
        {
            ApplicationId = application.Id,
            ApplicationNumber = application.ApplicationNumber,
            ActorUserId = actor.Id,
            ActorDisplayName = string.IsNullOrWhiteSpace(actor.FullName) ? actor.Email ?? "Unknown user" : actor.FullName,
            ActorRole = actorRole,
            Municipality = application.Municipality ?? actor.Municipality ?? "",
            EventType = eventType,
            OccurredAtUtc = DateTime.UtcNow,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Summary = summary,
            MetadataJson = metadata == null || metadata.Count == 0 ? null : JsonSerializer.Serialize(metadata)
        });
    }

    public async Task<AuditLogResult> ListAsync(ClaimsPrincipal principal, AuditLogFilter filter)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.GetUserAsync(principal);
        if (admin == null || !await users.IsInRoleAsync(admin, "DEDATAdmin"))
            throw new UnauthorizedAccessException();

        var db = services.GetRequiredService<ApplicationDbContext>();
        var all = db.ApplicationAuditLogs.AsNoTracking();
        var rows = all;
        if (!string.IsNullOrWhiteSpace(filter.Application))
        {
            var value = filter.Application.Trim().ToLowerInvariant();
            rows = rows.Where(item => item.ApplicationNumber.ToLower().Contains(value));
        }
        if (!string.IsNullOrWhiteSpace(filter.Actor)) rows = rows.Where(item => item.ActorDisplayName == filter.Actor);
        if (!string.IsNullOrWhiteSpace(filter.Municipality)) rows = rows.Where(item => item.Municipality == filter.Municipality);
        if (!string.IsNullOrWhiteSpace(filter.EventType)) rows = rows.Where(item => item.EventType == filter.EventType);
        if (filter.From.HasValue) rows = rows.Where(item => item.OccurredAtUtc >= filter.From.Value.Date);
        if (filter.To.HasValue)
        {
            var until = filter.To.Value.Date.AddDays(1);
            rows = rows.Where(item => item.OccurredAtUtc < until);
        }

        var total = await rows.CountAsync();
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)));
        var records = await rows.OrderByDescending(item => item.OccurredAtUtc).ThenByDescending(item => item.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();
        var actors = await all.Select(item => item.ActorDisplayName).Distinct().OrderBy(value => value).ToListAsync();
        var municipalities = await all.Where(item => item.Municipality != "").Select(item => item.Municipality)
            .Distinct().OrderBy(value => value).ToListAsync();
        var eventTypes = await all.Select(item => item.EventType).Distinct().OrderBy(value => value).ToListAsync();
        return new(records, new(actors, municipalities, eventTypes), total, page, PageSize);
    }
}
