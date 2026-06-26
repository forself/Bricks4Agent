using System.Text.Json;
using Broker.Helpers;
using Broker.Services;

namespace Broker.Endpoints;

public static class PortalEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var portal = group.MapGroup("/portal");

        portal.MapGet("/auth/status", (HttpContext ctx, PortalAuthService auth) =>
            Results.Ok(ApiResponseHelper.Success(auth.GetStatus(ctx))));

        portal.MapPost("/auth/register", (HttpContext ctx, PortalAuthService auth) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "user_id", out var userId, out var error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "password", out var password, out error))
                return error!;

            var displayName = body.TryGetProperty("display_name", out var dn) && dn.ValueKind == JsonValueKind.String
                ? dn.GetString()
                : null;

            try
            {
                return Results.Ok(ApiResponseHelper.Success(auth.Register(ctx, userId, password, displayName)));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        portal.MapPost("/auth/login", (HttpContext ctx, PortalAuthService auth) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "user_id", out var userId, out var error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "password", out var password, out error))
                return error!;

            try
            {
                var result = auth.Login(ctx, userId, password);
                return result.Authenticated
                    ? Results.Ok(ApiResponseHelper.Success(result))
                    : Results.Json(ApiResponseHelper.Error(result.Message, 401), statusCode: 401);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        portal.MapPost("/auth/logout", (HttpContext ctx, PortalAuthService auth) =>
        {
            auth.Logout(ctx);
            return Results.Ok(ApiResponseHelper.Success(new { ok = true }));
        });

        portal.MapGet("/me", (HttpContext ctx, PortalAuthService auth, HighLevelCoordinator coordinator) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out var session, out var denied))
                return denied;

            var profile = coordinator.GetLineUserProfile(session.UserId);
            var draft = coordinator.GetLineDraft(session.UserId);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                profile = ToProfileDto(profile, session.UserId),
                draft = ToDraftDto(draft)
            }));
        });

        portal.MapPost("/commands", async (
            HttpContext ctx,
            PortalAuthService auth,
            HighLevelCoordinator coordinator,
            HighLevelLineWorkspaceService workspace,
            BrokerArtifactDownloadService downloads,
            CancellationToken cancellationToken) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out var session, out var denied))
                return denied;

            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "message", out var message, out var error))
                return error!;

            var result = await coordinator.ProcessLineMessageAsync(session.UserId, message, cancellationToken);
            var artifacts = workspace.ListArtifacts(session.UserId, 20)
                .Select(item => ToArtifactDto(item, downloads))
                .ToArray();

            return Results.Ok(ApiResponseHelper.Success(new
            {
                user_id = session.UserId,
                message,
                result = ToProcessResultDto(result),
                artifacts
            }));
        });

        portal.MapGet("/results", (
            HttpContext ctx,
            PortalAuthService auth,
            HighLevelInteractionRecorder interactions) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out var session, out var denied))
                return denied;

            var limit = ReadLimit(ctx, 30);
            var items = interactions.ReadLatest("line", session.UserId, limit)
                .Select(ToInteractionDto)
                .ToArray();

            return Results.Ok(ApiResponseHelper.Success(new
            {
                user_id = session.UserId,
                total = items.Length,
                items
            }));
        });

        portal.MapGet("/artifacts", (
            HttpContext ctx,
            PortalAuthService auth,
            HighLevelLineWorkspaceService workspace,
            BrokerArtifactDownloadService downloads) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out var session, out var denied))
                return denied;

            var items = workspace.ListArtifacts(session.UserId, ReadLimit(ctx, 50))
                .Select(item => ToArtifactDto(item, downloads))
                .ToArray();

            return Results.Ok(ApiResponseHelper.Success(new
            {
                user_id = session.UserId,
                total = items.Length,
                items
            }));
        });

        portal.MapGet("/artifacts/{documentId}", (
            HttpContext ctx,
            PortalAuthService auth,
            HighLevelLineWorkspaceService workspace,
            BrokerArtifactDownloadService downloads,
            string documentId) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out var session, out var denied))
                return denied;

            var decoded = Uri.UnescapeDataString(documentId);
            var item = workspace.ReadArtifact(decoded);
            if (item == null || !string.Equals(item.UserId, session.UserId, StringComparison.Ordinal))
                return Results.NotFound(ApiResponseHelper.Error("Artifact not found.", 404));

            return Results.Ok(ApiResponseHelper.Success(new { item = ToArtifactDto(item, downloads) }));
        });
    }

    private static int ReadLimit(HttpContext ctx, int fallback)
        => int.TryParse(ctx.Request.Query["limit"].ToString(), out var parsed)
            ? Math.Clamp(parsed, 1, 100)
            : fallback;

    private static object ToProfileDto(HighLevelUserProfile? profile, string userId)
        => new
        {
            user_id = profile?.UserId ?? userId,
            display_name = profile?.PreferredDisplayName ?? string.Empty,
            user_code = profile?.PreferredUserCode ?? string.Empty,
            access_tier = profile?.AccessTier ?? HighLevelAccessTier.Basic,
            registration_status = profile?.RegistrationStatus ?? string.Empty,
            permissions = (profile?.Permissions ?? HighLevelUserPermissions.CreateDefault())
                .EffectiveForTier(profile?.AccessTier),
            last_interaction_at = profile?.LastInteractionAt,
            last_task_id = profile?.LastTaskId,
            last_plan_id = profile?.LastPlanId,
            pending_draft_id = profile?.PendingDraftId
        };

    private static object? ToDraftDto(HighLevelTaskDraft? draft)
        => draft == null
            ? null
            : new
            {
                draft_id = draft.DraftId,
                task_type = draft.TaskType,
                title = draft.Title,
                summary = draft.Summary,
                requires_project_name = draft.RequiresProjectName,
                project_name = draft.ProjectName,
                expires_at = draft.ExpiresAt,
                proposed_phases = draft.ProposedPhases.Select(phase => new
                {
                    phase_id = phase.PhaseId,
                    title = phase.Title,
                    kind = phase.Kind
                }).ToArray()
            };

    private static object ToProcessResultDto(HighLevelProcessResult result)
        => new
        {
            mode = result.Mode.ToString().ToLowerInvariant(),
            reply = result.Reply,
            follow_up_messages = result.FollowUpMessages ?? [],
            error = result.Error,
            decision_reason = result.DecisionReason,
            history_count = result.HistoryCount,
            draft_cleared = result.DraftCleared,
            draft = ToDraftDto(result.Draft),
            created_task_id = result.CreatedTask?.TaskId,
            created_plan_id = result.CreatedPlan?.PlanId,
            handoff = result.Handoff == null
                ? null
                : new
                {
                    task_id = result.Handoff.TaskId,
                    plan_id = result.Handoff.PlanId,
                    task_type = result.Handoff.TaskType,
                    title = result.Handoff.Title,
                    summary = result.Handoff.Summary
                },
            rag_snippets = result.RagSnippets ?? []
        };

    private static object ToInteractionDto(HighLevelInteractionRecord record)
        => new
        {
            interaction_id = record.InteractionId,
            occurred_at = record.OccurredAt,
            user_message = record.RawInput,
            reply = record.RawReply,
            route_mode = record.RouteMode,
            workflow_action = record.WorkflowAction,
            decision_reason = record.DecisionReason,
            error = record.Error,
            draft_id = record.DraftId,
            task_id = record.TaskId,
            plan_id = record.PlanId
        };

    private static object ToArtifactDto(HighLevelLineArtifactRecord item, BrokerArtifactDownloadService downloads)
    {
        var localDownloadPath = downloads.CreateSignedDownloadPath(item.ArtifactId);
        var downloadUrl = !string.IsNullOrWhiteSpace(item.GoogleDriveDownloadLink)
            ? item.GoogleDriveDownloadLink
            : localDownloadPath;

        return new
        {
            artifact_id = item.ArtifactId,
            document_id = item.DocumentId,
            source = item.Source,
            related_task_type = item.RelatedTaskType,
            related_task_id = item.RelatedTaskId,
            success = item.Success,
            message = item.Message,
            delivery_mode = item.DeliveryMode,
            file_name = item.FileName,
            format = item.Format,
            uploaded_to_google_drive = item.UploadedToGoogleDrive,
            drive_identity_mode = item.DriveIdentityMode,
            drive_share_mode = item.DriveShareMode,
            google_drive_file_id = item.GoogleDriveFileId,
            google_drive_web_view_link = item.GoogleDriveWebViewLink,
            google_drive_download_link = item.GoogleDriveDownloadLink,
            download_url = downloadUrl,
            notification_id = item.NotificationId,
            overall_status = item.OverallStatus,
            created_at = item.CreatedAt
        };
    }
}
