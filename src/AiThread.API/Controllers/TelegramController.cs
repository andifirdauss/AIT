using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

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
        ApiResponse<TelegramStatusDto>.Ok(await telegramService.UpdateSettingsAsync(request, cancellationToken), "Setting Telegram tersimpan.");

    [HttpPost("chats/{id:guid}/allow")]
    public async Task<ActionResult<ApiResponse<TelegramChatDto>>> Allow(Guid id, CancellationToken cancellationToken) =>
        await telegramService.AllowChatAsync(id, cancellationToken) is { } chat
            ? ApiResponse<TelegramChatDto>.Ok(chat, "Chat diizinkan.")
            : NotFound(ApiResponse<TelegramChatDto>.Fail("Chat tidak ditemukan."));

    [HttpDelete("chats/{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken cancellationToken) =>
        await telegramService.DeleteChatAsync(id, cancellationToken)
            ? ApiResponse<bool>.Ok(true, "Chat dihapus.")
            : NotFound(ApiResponse<bool>.Fail("Chat tidak ditemukan."));
}
