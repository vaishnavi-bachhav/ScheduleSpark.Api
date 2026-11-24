using Microsoft.AspNetCore.Mvc;
using ScheduleSpark.Api.Models;
using ScheduleSpark.Api.Services;

namespace ScheduleSpark.Api.Controllers;

[ApiController, Route("api/planning")]
public sealed class PlanningController(IPlanningService service) : ControllerBase
{
    [HttpGet("workspace/{semester}")] public Task<object> Workspace(string semester)=>service.GetWorkspaceAsync(semester);
    [HttpPost("offerings")] public async Task<ActionResult<CourseOffering>> CreateOffering(CreateOfferingRequest request){try{return Ok(await service.CreateOfferingAsync(request));}catch(ArgumentException e){return BadRequest(new{message=e.Message});}}
}
