// src/ProjectName.OrchestratorApi/Controllers/CopilotController.cs
// CopilotKit-compatible backend surface.
//
// SUPERSEDED (2026-07-11, ARCH-AGUI-001): this hand-rolled OpenAI-completions
// shim predates Microsoft's official AG-UI hosting package. The real AG-UI
// protocol endpoint (SSE run_started/text_message_*/run_finished events,
// which CopilotKit's runtime natively consumes via an HttpAgent) is now
// mapped at POST {OrchestratorService base URL}/agui — see
// src/services/ProjectName.OrchestratorService/Program.cs. That endpoint
// talks to the real "Orchestrator" AIAgent (full PMCRO cycle via
// run_pmcro_cycle), not just a bare Ollama passthrough like this controller.
//
// This controller is left in place (not deleted) pending confirmation — it
// lives in a different project (OrchestratorApi, a thin HTTP facade) than the
// new endpoint (OrchestratorService, which owns the AIAgent), so there's no
// naming/route conflict. Point any new frontend at /agui instead of /copilot/chat.
//
// CopilotKit's React frontend talks to a backend that exposes an OpenAI-compatible
// chat completion contract. This controller implements that contract (mirroring the
// CopilotKit runtime's expectations) on top of the already-registered Ollama
// IChatClient keyed "model-orchestrator" (see ServiceDefaults.OllamaExtensions).
//
//   GET  /copilot/info        -> backend metadata (CopilotKit runtime handshake)
//   GET  /copilot/v1/models   -> model list (OpenAI-compatible)
//   POST /copilot/chat        -> chat completions (streaming + non-streaming)
//
// This satisfies the "chat/agent surface reachable" check (C003) without requiring
// a dedicated (non-existent) CopilotKit .NET server NuGet package.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;

namespace ProjectName.OrchestratorApi.Controllers;

[ApiController]
[Route("copilot")]
[Produces("application/json")]
public class CopilotController(IKeyedServiceProvider keyedServices, ILogger<CopilotController> logger) : ControllerBase
{
    private IChatClient? GetOllama() =>
        keyedServices.GetKeyedService<IChatClient>(ProjectName.ServiceDefaults.OllamaExtensions.Keys.Orchestrator);

    [HttpGet("info")]
    public IActionResult Info() => Ok(new
    {
        backend = "pmcro-colony",
        version = "v5.1",
        capabilities = new[] { "chat", "trail-replay" },
        model = "model-orchestrator"
    });

    [HttpGet("v1/models")]
    public IActionResult Models() => Ok(new
    {
        @object = "list",
        data = new[] { new { id = "model-orchestrator", @object = "model", owned_by = "pmcro" } }
    });

    public sealed record ChatRequest(
        string? model,
        List<ChatMessageDto>? messages,
        bool stream = false);

    public sealed record ChatMessageDto(string role, string content);

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest req, CancellationToken ct)
    {
        var client = GetOllama();
        if (client is null)
        {
            logger.LogWarning("[CopilotController] model-orchestrator (Ollama) not registered — /copilot/chat unavailable");
            return StatusCode(503, new { error = "model-orchestrator (Ollama) not registered" });
        }

        var messages = (req.messages ?? new List<ChatMessageDto>())
            .Select(m => new ChatMessage(m.role == "assistant" ? ChatRole.Assistant : ChatRole.User, m.content))
            .ToList();

        if (!req.stream)
        {
            var resp = await client.GetResponseAsync(messages, cancellationToken: ct);
            return Ok(new
            {
                @object = "chat.completion",
                model = "model-orchestrator",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = resp.Text }, finish_reason = "stop" } }
            });
        }

        // Streaming response (Server-Sent Events, OpenAI-compatible shape).
        Response.ContentType = "text/event-stream";
        await foreach (var update in client.GetStreamingResponseAsync(messages, cancellationToken: ct))
        {
            if (update.Text is { Length: > 0 })
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    @object = "chat.completion.chunk",
                    model = "model-orchestrator",
                    choices = new[] { new { index = 0, delta = new { role = "assistant", content = update.Text }, finish_reason = (string?)null } }
                });
                await Response.WriteAsync("data: " + json + "\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        await Response.WriteAsync("data: [DONE]\n\n", ct);
        return new EmptyResult();
    }
}