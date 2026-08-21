// src/ProjectName.OrchestratorApi/Controllers/SkillsController.cs
// Exposes the real on-disk skill catalog (C-Suite domains, orchestrator-agent,
// tool-agent skills) making up the PMCR-O Colony over HTTP.
// GET /api/skills             -> list every skill's summary metadata
// GET /api/skills/{name}      -> one skill's full manifest + resource file lists
// This is a read-only facade over ProjectName.OrchestratorApi.Services.SkillCatalogService
// and never fabricates content -- 404 means the skill genuinely isn't on disk.

using Microsoft.AspNetCore.Mvc;
using ProjectName.OrchestratorApi.Services;

namespace ProjectName.OrchestratorApi.Controllers;

[ApiController]
[Route("api/skills")]
[Produces("application/json")]
public class SkillsController(SkillCatalogService catalog, ILogger<SkillsController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult ListSkills()
    {
        var skills = catalog.ListSkills();
        return Ok(new { count = skills.Count, skills });
    }

    [HttpGet("{name}")]
    public IActionResult GetSkill(string name)
    {
        var detail = catalog.GetSkill(name);
        if (detail is null)
        {
            logger.LogDebug("[SkillsController] No skill on disk named {Name}", name);
            return NotFound(new { name, error = "skill not found on disk" });
        }
        return Ok(detail);
    }
}
