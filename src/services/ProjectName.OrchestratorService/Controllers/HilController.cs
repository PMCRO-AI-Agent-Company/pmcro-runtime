// Controllers/HilController.cs
// ARCH-CTRL-001/002 (2026-08-09): resolves TYPE1 approval requests raised by
// DevUiHilChannel.RequestAsync (Loop/HilChannel.cs). That class blocks on a
// TaskCompletionSource keyed by requestId until IHilChannel.Resolve(id, bool)
// is called -- these two endpoints are the only thing that ever calls it.
// Development-only by design (matches the removed inline
// `if (app.Environment.IsDevelopment())` wrapper this replaces): production
// approval should go through a durable channel per HilChannel.cs's own
// class-header note, not an unauthenticated GET/POST-able HTTP route.
using Microsoft.AspNetCore.Mvc;
using ProjectName.OrchestratorService.Loop;

namespace ProjectName.OrchestratorService.Controllers;

[ApiController]
[Route("hil")]
public sealed class HilController(IHilChannel hilChannel, IHostEnvironment env) : ControllerBase
{
    [HttpPost("approve")]
    public IActionResult Approve([FromQuery] string id)
    {
        if (!env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(id)) return BadRequest("Missing id.");
        hilChannel.Resolve(id, approved: true);
        return Ok(new { id, resolved = "approved" });
    }

    [HttpPost("deny")]
    public IActionResult Deny([FromQuery] string id)
    {
        if (!env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(id)) return BadRequest("Missing id.");
        hilChannel.Resolve(id, approved: false);
        return Ok(new { id, resolved = "denied" });
    }
}
