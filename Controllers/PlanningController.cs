using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ScheduleSpark.Api.Models;
using ScheduleSpark.Api.Services;

namespace ScheduleSpark.Api.Controllers;

[ApiController, Route("api/planning")]
public sealed class PlanningController(IPlanningService service, IScheduleImportService importService) : ControllerBase
{
    [HttpGet("workspace/{semester}")] public Task<object> Workspace(string semester)=>service.GetWorkspaceAsync(semester);
    [HttpPut("blocks/{semester}")] public async Task<IActionResult> SaveBlocks(string semester, SaveScheduleBlocksRequest request)
    {
        try { return Ok(await service.SaveScheduleBlocksAsync(semester, request)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
    [HttpPut("instructors/{instructorId}/availability/{semester}")] public async Task<IActionResult> SaveInstructorAvailability(string instructorId, string semester, SaveInstructorAvailabilityRequest request)
    {
        try { return Ok(await service.SaveInstructorAvailabilityAsync(instructorId, semester, request)); }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
    [HttpPost("offerings")] public async Task<ActionResult<CourseOffering>> CreateOffering(CreateOfferingRequest request){try{return Ok(await service.CreateOfferingAsync(request));}catch(ArgumentException e){return BadRequest(new{message=e.Message});}}
    [HttpPost("import/preview"), Consumes("multipart/form-data")] public async Task<IActionResult> PreviewImport([FromForm] string semester, [FromForm] List<IFormFile> files)
    {
        try { return Ok(await importService.PreviewAsync(semester, files)); }
        catch (InvalidDataException e) { return BadRequest(new { message = e.Message }); }
        catch (System.Xml.XmlException) { return BadRequest(new { message = "The Excel workbook could not be read. Check that it is a valid .xlsx file." }); }
    }
    [HttpPost("import/confirm")] public async Task<IActionResult> ConfirmImport(ScheduleImportBatch batch) => Ok(await importService.ConfirmAsync(batch));
}
