using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Telegram;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed class TelegramService(
    ISettingService settingService,
    TelegramApi api,
    TelegramBotRuntime runtime,
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IEventBus eventBus,
    IActivityLogService activityLog) : ITelegramService
{
    public async Task<TelegramStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetTelegramAsync(cancellationToken);
        var state = runtime.Snapshot;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chats = await db.TelegramChats.AsNoTracking()
            .OrderBy(c => c.IsAllowed)
            .ThenByDescending(c => c.LastMessageAt)
            .Select(c => new TelegramChatDto(
                c.Id, c.ChatId, c.DisplayName, c.Username, c.IsAllowed, c.ActiveProjectId,
                c.ActiveProject != null ? c.ActiveProject.Name : null, c.CreatedAt, c.LastMessageAt))
            .ToListAsync(cancellationToken);

        return new TelegramStatusDto(
            settings.Enabled, settings.HasBotToken, settings.BotTokenMasked,
            state.Status, state.BotUsername, state.Error, state.LastPollAt, chats);
    }

    public async Task<TelegramStatusDto> UpdateSettingsAsync(UpdateTelegramSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var token = request.ClearBotToken ? null : request.BotToken?.Trim();
        string? botUsername = null;
        if (!string.IsNullOrEmpty(token))
        {
            if (!TelegramApi.IsValidTokenFormat(token))
                throw new AppValidationException("Format bot token tidak valid. Salin token lengkap dari @BotFather, contoh 123456789:AAE…");
            try
            {
                botUsername = (await api.GetMeAsync(token, cancellationToken)).Username;
            }
            catch (TelegramApiException ex)
            {
                throw new AppValidationException(ex.IsUnauthorized
                    ? "Token ditolak Telegram. Salin ulang token dari @BotFather."
                    : $"Token tidak dapat diverifikasi: {ex.Message}");
            }
        }

        await settingService.UpdateTelegramAsync(request with { BotToken = token }, cancellationToken);
        runtime.Reload();
        activityLog.Info("telegram", request.Enabled
            ? $"Setting Telegram disimpan{(botUsername is null ? "" : $" (bot @{botUsername})")}."
            : "Bot Telegram dinonaktifkan.");
        return await GetStatusAsync(cancellationToken);
    }

    public async Task<TelegramChatDto?> AllowChatAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.TelegramChats.Include(c => c.ActiveProject).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (chat is null) return null;

        if (!chat.IsAllowed)
        {
            chat.IsAllowed = true;
            chat.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            eventBus.Publish(AppEvent.Create(AppEventTypes.TelegramChanged, new { chat.Id, action = "allowed" }));
            activityLog.Success("telegram", $"Chat Telegram {chat.DisplayName} (ID {chat.ChatId}) diizinkan mengirim prompt.");
            await NotifyAsync(chat, TelegramMessages.Allowed(chat.ActiveProject?.Name), cancellationToken);
        }

        return ToDto(chat);
    }

    public async Task<bool> DeleteChatAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.TelegramChats.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (chat is null) return false;

        db.TelegramChats.Remove(chat);
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.TelegramChanged, new { chat.Id, action = "deleted" }));
        if (chat.IsAllowed)
        {
            activityLog.Info("telegram", $"Akses chat Telegram {chat.DisplayName} (ID {chat.ChatId}) dicabut.");
            await NotifyAsync(chat, TelegramMessages.Revoked, cancellationToken);
        }
        return true;
    }

    // Best effort: the change is saved even when the bot cannot reach the chat (bot disabled, offline).
    private async Task NotifyAsync(TelegramChat chat, string html, CancellationToken cancellationToken)
    {
        try
        {
            await api.SendAsync(chat.ChatId, html, cancellationToken: cancellationToken);
        }
        catch (TelegramApiException ex)
        {
            activityLog.Warn("telegram", $"Pesan ke chat Telegram {chat.DisplayName} tidak terkirim: {ex.Message}");
        }
    }

    private static TelegramChatDto ToDto(TelegramChat c) => new(
        c.Id, c.ChatId, c.DisplayName, c.Username, c.IsAllowed, c.ActiveProjectId, c.ActiveProject?.Name, c.CreatedAt, c.LastMessageAt);
}
