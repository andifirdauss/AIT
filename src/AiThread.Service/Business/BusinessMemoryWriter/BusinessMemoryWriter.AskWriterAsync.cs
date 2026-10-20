using System.Text;
using AiThread.Common.Entities;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Skills;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Business;

public sealed partial class BusinessMemoryWriter
{
    private async Task<WriterAnswer> AskWriterAsync(
        string model, string projectName, BusinessPage? page, PageTarget target, IReadOnlyList<ThreadDigest> threads,
        IReadOnlyList<ImageAttachment> images, CancellationToken cancellationToken)
    {
        string Input(bool withImages)
        {
            var input = new StringBuilder();
            input.Append("# Project\n").Append(projectName).Append("\n\n");
            input.Append("# Page\n").Append($"{page?.Module ?? target.Module} — {page?.Title ?? target.Title}\n\n");
            input.Append("# Current page\n").Append(page is null ? "(new page)" : page.Content).Append("\n\n");
            input.Append("# New information from the developer threads\n");
            foreach (var thread in threads) input.Append(thread.Text).Append('\n');
            if (images.Count > 0)
            {
                input.Append(withImages
                    ? $"\n# Screenshots\n{images.Count} image(s) from these threads are attached, numbered Image 1 to Image {images.Count} in order.\n"
                    : "\n# Screenshots\nThe threads had screenshots, but they cannot be shown to you. Do not reference new images.\n");
            }
            return input.ToString();
        }

        ChatCompletionResult reply;
        try
        {
            reply = await AskAsync(Input(images.Count > 0), images);
        }
        catch (NineRouterException ex) when (images.Count > 0 && ex.StatusCode is 400 or 413 or 415 or 422)
        {
            // The model or route does not take images: write from the text alone.
            logger.LogInformation(ex, "Business writer model refused images; retrying without them.");
            reply = await AskAsync(Input(false), null);
        }

        var json = SkillAi.ParseObject(reply.Content);
        var content = SkillAi.Text(json?["content"]);
        if (content is null || content.Length < 20)
            throw new OrchestrationException(reply.FinishReason == "length"
                ? "jawaban model terpotong (halaman terlalu panjang untuk batas token)"
                : "model tidak menjawab JSON berisi halaman");
        if (content.Length > BusinessPageStore.MaxContentChars) throw new OrchestrationException("halaman hasil model terlalu panjang");

        return new WriterAnswer(content, SkillAi.Text(json?["summary"]), SkillAi.Text(json?["labels"]), SkillAi.Text(json?["changeNote"]));

        Task<ChatCompletionResult> AskAsync(string input, IReadOnlyList<ImageAttachment>? attached) => router.ChatAsync(new ChatCompletionRequest
        {
            Model = model,
            Messages = [ChatMessages.System(AgentPrompts.BusinessWriter), ChatMessages.User(input, attached)],
            Temperature = 0.2,
            MaxTokens = WriterMaxTokens
        }, cancellationToken: cancellationToken);
    }
}
