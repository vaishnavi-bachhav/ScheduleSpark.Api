using Microsoft.AspNetCore.Mvc;
using ScheduleSpark.Api.Models;
using ScheduleSpark.Api.Services;

namespace ScheduleSpark.Api.Controllers;

[ApiController, Route("api/schedules")]
public sealed class SchedulesController(ISchedulingService scheduling, IConflictService conflicts, IDraftService drafts, IScheduleEditService edits) : ControllerBase
{
    [HttpPost("generate")] public async Task<ActionResult<ScheduleDraft>> Generate(GenerateDraftRequest request) => Ok(await scheduling.GenerateDraftAsync(request));
    [HttpPost("validate")] public async Task<IActionResult> Validate(ValidateDraftRequest request) { var details = await conflicts.ValidateDetailedAsync(request.Draft); return Ok(new { isValid = details.Count == 0, errors = details }); }
    [HttpGet("drafts")] public Task<List<ScheduleDraft>> GetDrafts() => drafts.GetAllAsync();
    [HttpGet("drafts/{id}")] public async Task<ActionResult<ScheduleDraft>> GetDraft(string id) { var d = await drafts.GetAsync(id); return d is null ? NotFound() : Ok(d); }
    [HttpPost("drafts")] public async Task<ActionResult<ScheduleDraft>> SaveDraft(ScheduleDraft draft) => Ok(await drafts.SaveAsync(draft));
    [HttpDelete("drafts/{id}")] public async Task<IActionResult> DeleteDraft(string id) => await drafts.DeleteAsync(id) ? NoContent() : NotFound(new { message = "Draft not found." });
    [HttpPut("drafts/{draftId}/sections/{courseCode}")]
    public async Task<IActionResult> EditSection(string draftId, string courseCode, EditScheduleSectionRequest request)
    {
        var result = await edits.EditSectionAsync(draftId, courseCode, request);
        if (result.Draft is null) return NotFound(new { message = "Draft not found." });
        return Ok(new { draft = result.Draft, isValid = result.Conflicts.Count == 0, errors = result.Conflicts });
    }
}
