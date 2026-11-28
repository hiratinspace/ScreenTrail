using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using ScreenTrail.Api.Auth;
using ScreenTrail.Api.Data;
using ScreenTrail.Api.Tenancy;

namespace ScreenTrail.Api.Endpoints;

public sealed record SeatUsage(int Used, int Total);

public sealed record AdminDevice(Guid Id, string Name, DateTimeOffset ActivatedAt, DateTimeOffset? LastSeenAt, DateTimeOffset? RevokedAt);

public sealed record AdminTechnician(Guid Id, string Email, string DisplayName, DateTimeOffset? DisabledAt, IReadOnlyList<AdminDevice> Devices);

/// <param name="Status"><c>pending</c>, <c>accepted</c> or <c>expired</c>.</param>
public sealed record AdminInvite(Guid Id, string Email, string DisplayName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, string Status);

public sealed record TechniciansResponse(SeatUsage Seats, IReadOnlyList<AdminTechnician> Technicians, IReadOnlyList<AdminInvite> Invites);

public sealed record CreateInviteRequest(string? Email, string? DisplayName);

/// <param name="Code">The one copy in the clear. Shown to the owner once; the database keeps its hash.</param>
public sealed record CreatedInvite(Guid Id, string Email, string Code, DateTimeOffset ExpiresAt);

/// <summary>
/// A whole policy, every field required. Nullable so that a field left out is refused rather than read
/// as false: a page that forgot <c>localOnlyLocked</c> must not unlock it (INV-11).
/// </summary>
/// <param name="ConfirmAllWindows">INV-5: capture outside the remote tools is opt-in and confirmed.</param>
public sealed record UpdatePolicyRequest(
    int? RetentionDays,
    bool? LocalOnly,
    bool? LocalOnlyLocked,
    bool? CaptureAllWindows,
    bool? ConfirmAllWindows,
    string? UpdateChannel,
    bool? UpdateChannelLocked,
    int? UpdateCheckHours,
    bool? UpdateChecksUnderLocalOnly);

public sealed record DownloadResponse(string? Url, string Message);

/// <summary>
/// The owner's routes (ST-099), all behind <see cref="OwnerPolicy"/>. Every query is filtered by the
/// session's tenant and by nothing a request can say: an id in a path is looked up inside that tenant,
/// and one from another tenant is a 404 like one that does not exist.
/// </summary>
public static class AdminEndpoint
{
    public const int RetentionMin = 1;
    public const int RetentionMax = 365;
    public const int UpdateHoursMax = 168;

    /// <summary>What the owner is told when inviting a technician they switched off.</summary>
    public const string DisabledTechnicianMessage = "That technician is switched off in your organisation, so they cannot be invited again.";

    public static RouteGroupBuilder MapAdmin(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/session", (AdminCaller owner) => Results.Ok(AdminSessionResponse.For(owner)))
            .WithName("AdminSession")
            .WithSummary("The signed-in owner, their tenant, and the session's CSRF value.");

        group.MapGet("/technicians", async (AdminCaller owner, ScreenTrailContext db, TimeProvider time, CancellationToken ct) =>
        {
            var users = await db.Users.AsNoTracking()
                .Where(u => u.TenantId == owner.TenantId)
                .OrderBy(u => u.DisplayName)
                .Select(u => new { u.Id, u.Email, u.DisplayName, u.DisabledAt })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var devices = await db.Devices.AsNoTracking()
                .Where(d => d.TenantId == owner.TenantId)
                .Select(d => new { d.UserId, Row = new AdminDevice(d.Id, d.Name, d.ActivatedAt, d.LastSeenAt, d.RevokedAt) })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var invites = await db.Invites.AsNoTracking()
                .Where(i => i.TenantId == owner.TenantId)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var now = time.GetUtcNow();
            var byUser = devices.ToLookup(d => d.UserId, d => d.Row);
            return Results.Ok(new TechniciansResponse(
                new SeatUsage(devices.Count(d => d.Row.RevokedAt is null), owner.Tenant.Seats),
                [.. users.Select(u => new AdminTechnician(u.Id, u.Email, u.DisplayName, u.DisabledAt, [.. byUser[u.Id].OrderBy(d => d.ActivatedAt)]))],
                [.. invites
                    .OrderByDescending(i => i.CreatedAt)
                    .Select(i => new AdminInvite(i.Id, i.Email, i.DisplayName, i.CreatedAt, i.ExpiresAt, i.AcceptedAt, i.AcceptedAt is not null ? "accepted" : i.ExpiresAt <= now ? "expired" : "pending"))]));
        })
        .WithName("AdminTechnicians")
        .WithSummary("The tenant's technicians, their devices, the seats in use and the invites issued.");

        group.MapPost("/invites", async (CreateInviteRequest request, AdminCaller owner, ScreenTrailContext db, TimeProvider time, CancellationToken ct) =>
        {
            var email = request.Email?.Trim().ToLowerInvariant();
            if (!LooksLikeAnAddress(email))
            {
                return Results.BadRequest(new { error = "bad_email", message = "Enter the technician's work email address." });
            }

            var name = string.IsNullOrWhiteSpace(request.DisplayName) ? email : request.DisplayName.Trim();
            if (name.Length > 200)
            {
                return Results.BadRequest(new { error = "bad_display_name", message = "The name can be up to 200 characters." });
            }

            // A code for a technician the owner switched off would only be refused at activation.
            if (await db.Users.AnyAsync(u => u.TenantId == owner.TenantId && u.Email == email && u.DisabledAt != null, ct).ConfigureAwait(false))
            {
                return Results.Json(new { error = "technician_disabled", message = DisabledTechnicianMessage }, statusCode: StatusCodes.Status409Conflict);
            }

            // The same code, hash and 72 hours as the operator's --invite. No mail provider yet, so the
            // code goes back to the owner, once, to hand on (ADR-0009).
            var issued = await Invites.CreateAsync(db, owner.TenantId, email, name, time, ct).ConfigureAwait(false);
            return Results.Json(new CreatedInvite(issued.Id, email, issued.Code, issued.ExpiresAt), statusCode: StatusCodes.Status201Created);
        })
        .WithName("AdminCreateInvite")
        .WithSummary("Issues a 72-hour, one-device invite code for a technician, returned once.");

        group.MapDelete("/invites/{id:guid}", async (Guid id, AdminCaller owner, ScreenTrailContext db, CancellationToken ct) =>
        {
            // Only while nobody has used it: the row goes, so its code is one nobody issued. The tenant is
            // in the predicate, so another tenant's invite is the same as no invite.
            var withdrawn = await db.Invites
                .Where(i => i.Id == id && i.TenantId == owner.TenantId && i.AcceptedAt == null)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);
            if (withdrawn > 0)
            {
                return Results.NoContent();
            }

            return await db.Invites.AnyAsync(i => i.Id == id && i.TenantId == owner.TenantId, ct).ConfigureAwait(false)
                ? Results.Json(new { error = "invite_used", message = "That invite has been used. Revoke the device it activated instead." }, statusCode: StatusCodes.Status409Conflict)
                : Results.NotFound(new { error = "invite_unknown", message = "There is no such invite in your organisation." });
        })
        .WithName("AdminWithdrawInvite")
        .WithSummary("Withdraws an invite nobody has used yet. Its code stops working at once.");

        group.MapPost("/technicians/{id:guid}/disable", async (Guid id, AdminCaller owner, ScreenTrailContext db, TimeProvider time, CancellationToken ct) =>
        {
            // One transaction, after the tenant's row lock (TenantLock, as activation takes it), so an
            // activation of this technician either finishes first and its device is revoked here, or
            // starts after and finds them switched off.
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            _ = await TenantLock.TakeAsync(db, owner.TenantId, ct).ConfigureAwait(false);
            var user = await db.Users
                .Where(u => u.Id == id && u.TenantId == owner.TenantId)
                .Select(u => new { u.Email })
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (user is null)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return Results.NotFound(new { error = "technician_unknown", message = "There is no such technician in your organisation." });
            }

            var now = time.GetUtcNow();
            _ = await db.Users
                .Where(u => u.Id == id && u.TenantId == owner.TenantId && u.DisabledAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.DisabledAt, now), ct)
                .ConfigureAwait(false);
            _ = await db.Devices
                .Where(d => d.UserId == id && d.TenantId == owner.TenantId && d.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(d => d.RevokedAt, now), ct)
                .ConfigureAwait(false);
            _ = await db.Invites
                .Where(i => i.TenantId == owner.TenantId && i.Email == user.Email && i.AcceptedAt == null)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("AdminDisableTechnician")
        .WithSummary("Switches a technician off: their devices are revoked and their unused invites withdrawn.");

        group.MapPost("/devices/{id:guid}/revoke", async (Guid id, AdminCaller owner, ScreenTrailContext db, TimeProvider time, CancellationToken ct) =>
        {
            var now = time.GetUtcNow();
            var revoked = await db.Devices
                .Where(d => d.Id == id && d.TenantId == owner.TenantId && d.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(d => d.RevokedAt, now), ct)
                .ConfigureAwait(false);
            if (revoked > 0)
            {
                return Results.NoContent();
            }

            // Already revoked is done; another tenant's device is the same as no device.
            return await db.Devices.AnyAsync(d => d.Id == id && d.TenantId == owner.TenantId, ct).ConfigureAwait(false)
                ? Results.NoContent()
                : Results.NotFound(new { error = "device_unknown", message = "There is no such device in your organisation." });
        })
        .WithName("AdminRevokeDevice")
        .WithSummary("Revokes one of the tenant's devices. Its token stops working on its next request.");

        group.MapGet("/policy", async (AdminCaller owner, ScreenTrailContext db, CancellationToken ct) =>
            Results.Ok(PolicyEndpoint.Describe(await Policies.LatestAsync(db, owner.TenantId, ct).ConfigureAwait(false))))
        .WithName("AdminGetPolicy")
        .WithSummary("The tenant's policy, exactly as devices receive it.");

        group.MapPut("/policy", async (UpdatePolicyRequest request, AdminCaller owner, ScreenTrailContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (request is not
                {
                    RetentionDays: { } retention,
                    LocalOnly: { } localOnly,
                    LocalOnlyLocked: { } localOnlyLocked,
                    CaptureAllWindows: { } allWindows,
                    UpdateChannel: { } channel,
                    UpdateChannelLocked: { } channelLocked,
                    UpdateCheckHours: { } hours,
                    UpdateChecksUnderLocalOnly: { } underLocalOnly,
                })
            {
                return Results.BadRequest(new { error = "incomplete_policy", message = "Send every policy field. A field left out is not read as off." });
            }

            if (retention is < RetentionMin or > RetentionMax)
            {
                return Results.BadRequest(new { error = "bad_retention", message = $"Keep raw data for {RetentionMin} to {RetentionMax} days." });
            }

            if (channel is not ("stable" or "pilot"))
            {
                return Results.BadRequest(new { error = "bad_update_channel", message = "The update channel is stable or pilot." });
            }

            if (hours is < 0 or > UpdateHoursMax)
            {
                return Results.BadRequest(new { error = "bad_update_hours", message = $"Check for updates every 1 to {UpdateHoursMax} hours, or 0 for never." });
            }

            // INV-5: every window, not just the remote tools, is an opt-in somebody confirmed. Turning it
            // on needs the confirmation; leaving it on, once it is on, does not ask again.
            var current = await Policies.LatestAsync(db, owner.TenantId, ct).ConfigureAwait(false);
            if (allWindows && current?.CaptureAllWindows != true && request.ConfirmAllWindows != true)
            {
                return Results.BadRequest(new { error = "all_windows_unconfirmed", message = "Capturing every window needs confirming. Tick the confirmation and save again." });
            }

            try
            {
                var saved = await Policies.SetAsync(db, owner.TenantId, retention, localOnly, localOnlyLocked, allWindows, new Policies.UpdateSettings(channel, channelLocked, hours, underLocalOnly), time, ct).ConfigureAwait(false);
                return Results.Ok(PolicyEndpoint.Describe(saved));
            }
            catch (PolicyVersionConflictException)
            {
                return Results.Json(new { error = "policy_conflict", message = "Another change to the policy was saved at the same moment. Reload the policy and save again." }, statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("AdminSetPolicy")
        .WithSummary("Sets the tenant's policy as a new version. Devices pick it up within the hour.");

        // The Integrations page's routes, connect and disconnect included, are IntegrationsEndpoint.MapAdminIntegrations.
        group.MapGet("/download", (AdminOptions options) =>
        {
            var url = options.DownloadUri();
            return Results.Ok(url is null
                ? new DownloadResponse(null, "No client download is published for this deployment yet. Ask your ScreenTrail contact for the installer.")
                : new DownloadResponse(url.ToString(), "Install ScreenTrail on each technician's Windows PC, then activate it with their invite code."));
        })
        .WithName("AdminDownload")
        .WithSummary("Where technicians download the Windows client, when the deployment has one.");

        return group;
    }

    private static bool LooksLikeAnAddress([NotNullWhen(true)] string? email) =>
        email is { Length: >= 3 and <= 320 }
        && email.IndexOf('@', StringComparison.Ordinal) is var at and > 0
        && at < email.Length - 1
        && email.IndexOf('@', at + 1) < 0
        && !email.Any(char.IsWhiteSpace);
}
