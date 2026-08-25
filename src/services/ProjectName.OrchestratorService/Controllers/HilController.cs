// Controllers/HilController.cs
// Development HIL approval surface. The orchestrator command uses ResolveLatest()
// when no explicit request id is supplied.
using Microsoft.AspNetCore.Mvc;
using ProjectName.OrchestratorService.Loop;

namespace ProjectName.OrchestratorService.Controllers;

[ApiController]
[Route("hil")]
public sealed class HilController(IHilChannel hilChannel, IHostEnvironment env) : ControllerBase
{
    [HttpPost("approve")]
    public IActionResult Approve([FromQuery] string? id = null)
    {
        if (!env.IsDevelopment()) return NotFound();

        if (string.IsNullOrWhiteSpace(id))
        {
            if (!hilChannel.ResolveLatest(approved: true))
                return Conflict(new { status = "no_pending_approval" });
            return Ok(new { resolved = "approved", target = "latest" });
        }

        hilChannel.Resolve(id, approved: true);
        return Ok(new { id, resolved = "approved" });
    }

    [HttpPost("deny")]
    public IActionResult Deny([FromQuery] string? id = null)
    {
        if (!env.IsDevelopment()) return NotFound();

        if (string.IsNullOrWhiteSpace(id))
        {
            if (!hilChannel.ResolveLatest(approved: false))
                return Conflict(new { status = "no_pending_approval" });
            return Ok(new { resolved = "denied", target = "latest" });
        }

        hilChannel.Resolve(id, approved: false);
        return Ok(new { id, resolved = "denied" });
    }
}
