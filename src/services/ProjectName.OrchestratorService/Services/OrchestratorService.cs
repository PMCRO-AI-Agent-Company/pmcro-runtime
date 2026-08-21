// src/services/ProjectName.OrchestratorService/Services/OrchestratorService.cs
using Grpc.Core;
using ProjectName.OrchestratorService.Loop;
using Microsoft.Extensions.Logging;

namespace ProjectName.OrchestratorService.Services;

public sealed class OrchestratorService(
    PmcroLoop loop,
    ISubjectAgentRegistry registry,
    ILogger<OrchestratorService> logger)
    : ProjectName.OrchestratorService.Orchestrator.OrchestratorBase
{
    public override async Task<CycleResponse> RunCycle(CycleRequest request, ServerCallContext context)
    {
        // Trail IDs embed the subject agent name so trail folders are self-describing
        // on disk (e.g. filesystem-agent_20260703-141205_a3f9c1e2) instead of a bare GUID.
        var trailId = string.IsNullOrEmpty(request.TrailId)
            ? $"{request.SubjectAgent}_{DateTime.UtcNow:yyyyMMdd-HHmmss}_{Guid.NewGuid().ToString("N")[..8]}"
            : request.TrailId;

        logger.LogInformation("[gRPC] Orchestrator.RunCycle: {Intent}", request.SeedIntent);

        try
        {
            var subjectAgentInstance = registry.Resolve(request.SubjectAgent)
                ?? registry.Resolve("filesystem-agent")
                ?? throw new InvalidOperationException(
                    $"No AIAgent registered for subjectAgent='{request.SubjectAgent}'.");

            var result = await loop.RunAsync(
                request.SeedIntent,
                trailId,
                request.Project,
                request.SubjectAgent,
                subjectAgentInstance
            );

            return new CycleResponse
            {
                Ok = true,
                TrailId = trailId,
                Disposition = result.Disposition.ToString().ToUpperInvariant(),
                FinalOutput = result.FinalOutput,
                CycleNumber = result.CycleNumber,
                Error = result.HaltReason ?? "", // Mapping HaltReason to Error for gRPC
                // THE SUCCESSION LAW: proto3 strings can't be null, so an absent Baton
                // is an empty string on the wire — CycleController's chain loop already
                // treats string.IsNullOrWhiteSpace as "stop", so this round-trips cleanly.
                NextSeedIntent = result.NextSeedIntent ?? ""
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[gRPC] RunCycle Failed");
            return new CycleResponse
            {
                Ok = false,
                Error = ex.Message,
                TrailId = trailId
            };
        }
    }
}