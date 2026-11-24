using Microsoft.AspNetCore.Mvc;
using ScheduleSpark.Api.Models;
using ScheduleSpark.Api.Services;

namespace ScheduleSpark.Api.Controllers;

[ApiController, Route("api/schedules")]
public sealed class SchedulesController(ISchedulingService scheduling, IConflictService conflicts, IDraftService drafts) : ControllerBase
{
    [HttpPost("generate")] public async Task<ActionResult<ScheduleDraft>> Generate(GenerateDraftRequest request)=>Ok(await scheduling.GenerateDraftAsync(request));
    [HttpPost("validate")] public async Task<IActionResult> Validate(ValidateDraftRequest request){var errors=await conflicts.ValidateAsync(request.Draft);return Ok(new{isValid=errors.Count==0,errors});}
    [HttpGet("drafts")] public Task<List<ScheduleDraft>> GetDrafts()=>drafts.GetAllAsync();
    [HttpGet("drafts/{id}")] public async Task<ActionResult<ScheduleDraft>> GetDraft(string id){var d=await drafts.GetAsync(id);return d is null?NotFound():Ok(d);}
    [HttpPost("drafts")] public async Task<ActionResult<ScheduleDraft>> SaveDraft(ScheduleDraft draft)=>Ok(await drafts.SaveAsync(draft));
}
