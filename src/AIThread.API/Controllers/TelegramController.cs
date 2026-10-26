using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// Telegram card of the Setting page: bot token, on/off and the chats allowed to send prompts.
[ApiController]
[Route("api/telegram")]
public class TelegramController(ITelegramService telegramService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<TelegramStatusDto>>> Get(CancellationToken cancellationToken) =>
        ApiResponse<TelegramStatusDto>.Ok(await telegramService.GetStatusAsync(cancellationToken));

    [HttpPut]
    public async Task<ActionResult<ApiResponse<TelegramStatusDto>>> Update(UpdateTelegramSettingsRequest request, CancellationToken cancellationToken) =>
        ApiResponse<TelegramStatusDto>.Ok(await telegramService.UpdateSettingsAsync(request, cancellationToken), Lang.T("Setting Telegram tersimpan.", "Telegram settings saved."));

    [HttpPost("chats/{id:guid}/allow")]
    public async Task<ActionResult<ApiResponse<TelegramChatDto>>> Allow(Guid id, CancellationToken cancellationToken) =>
        await telegramService.AllowChatAsync(id, cancellationToken) is { } chat
            ? ApiResponse<TelegramChatDto>.Ok(chat, Lang.T("Chat diizinkan.", "Chat allowed."))
            : NotFound(ApiResponse<TelegramChatDto>.Fail(Lang.T("Chat tidak ditemukan.", "Chat not found.")));

    [HttpDelete("chats/{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken cancellationToken) =>
        await telegramService.DeleteChatAsync(id, cancellationToken)
            ? ApiResponse<bool>.Ok(true, Lang.T("Chat dihapus.", "Chat deleted."))
            : NotFound(ApiResponse<bool>.Fail(Lang.T("Chat tidak ditemukan.", "Chat not found.")));
}
