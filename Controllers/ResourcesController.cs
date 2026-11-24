using Microsoft.AspNetCore.Mvc;
using ScheduleSpark.Api.Models;
using ScheduleSpark.Api.Repositories;

namespace ScheduleSpark.Api.Controllers;

[ApiController, Route("api")]
public sealed class ResourcesController(IInstructorRepository instructors, IRoomRepository rooms, IConstraintRepository constraints) : ControllerBase
{
    [HttpGet("instructors")] public Task<List<Instructor>> GetInstructors() => instructors.GetAllAsync();
    [HttpPost("instructors")] public async Task<ActionResult<Instructor>> AddInstructor(Instructor item) { await instructors.AddAsync(item); return CreatedAtAction(nameof(GetInstructor), new { id = item.Id }, item); }
    [HttpGet("instructors/{id}")] public async Task<ActionResult<Instructor>> GetInstructor(string id) { var item = await instructors.GetAsync(id); return item is null ? NotFound() : Ok(item); }
    [HttpPut("instructors/{id}")] public async Task<IActionResult> UpdateInstructor(string id, Instructor item) { item.Id = id; if (await instructors.GetAsync(id) is null) return NotFound(); await instructors.UpdateAsync(item); return NoContent(); }
    [HttpDelete("instructors/{id}")] public async Task<IActionResult> DeleteInstructor(string id) { await instructors.DeleteAsync(id); return NoContent(); }
    [HttpGet("rooms")] public Task<List<Room>> GetRooms() => rooms.GetAllAsync();
    [HttpPost("rooms")] public async Task<ActionResult<Room>> AddRoom(Room item) { await rooms.AddAsync(item); return CreatedAtAction(nameof(GetRoom), new { id = item.Id }, item); }
    [HttpGet("rooms/{id}")] public async Task<ActionResult<Room>> GetRoom(string id) { var item = await rooms.GetAsync(id); return item is null ? NotFound() : Ok(item); }
    [HttpPut("rooms/{id}")] public async Task<IActionResult> UpdateRoom(string id, Room item) { item.Id = id; if (await rooms.GetAsync(id) is null) return NotFound(); await rooms.UpdateAsync(item); return NoContent(); }
    [HttpDelete("rooms/{id}")] public async Task<IActionResult> DeleteRoom(string id) { await rooms.DeleteAsync(id); return NoContent(); }
    [HttpGet("constraints")] public Task<List<SchedulingConstraint>> GetConstraints() => constraints.GetAllAsync();
    [HttpPost("constraints")] public async Task<ActionResult<SchedulingConstraint>> AddConstraint(SchedulingConstraint item) { await constraints.AddAsync(item); return Created($"/api/constraints/{item.Id}", item); }
    [HttpPut("constraints/{id}")] public async Task<IActionResult> UpdateConstraint(string id, SchedulingConstraint item) { item.Id = id; await constraints.UpdateAsync(item); return NoContent(); }
    [HttpDelete("constraints/{id}")] public async Task<IActionResult> DeleteConstraint(string id) { await constraints.DeleteAsync(id); return NoContent(); }
}
