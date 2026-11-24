using ScheduleSpark.Api.Models;
using ScheduleSpark.Api.Repositories;

namespace ScheduleSpark.Api.Services;

public interface IPlanningService { Task<object> GetWorkspaceAsync(string semester); Task<CourseOffering> CreateOfferingAsync(CreateOfferingRequest request); }
public interface ISchedulingService { Task<ScheduleDraft> GenerateDraftAsync(GenerateDraftRequest request); }
public interface IConflictService { Task<List<string>> ValidateAsync(ScheduleDraft draft); }
public interface IDraftService { Task<List<ScheduleDraft>> GetAllAsync(); Task<ScheduleDraft?> GetAsync(string id); Task<ScheduleDraft> SaveAsync(ScheduleDraft draft); }

public sealed class PlanningService(IOfferingRepository offerings, IInstructorRepository instructors, IRoomRepository rooms, IConstraintRepository constraints) : IPlanningService
{
    public async Task<object> GetWorkspaceAsync(string semester) => new
    {
        Semester = semester,
        Offerings = await offerings.GetBySemesterAsync(semester),
        Instructors = await instructors.GetAllAsync(),
        Rooms = await rooms.GetAllAsync(),
        Constraints = await constraints.GetEnabledAsync()
    };

    public async Task<CourseOffering> CreateOfferingAsync(CreateOfferingRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.CourseCode) || string.IsNullOrWhiteSpace(r.CourseTitle)) throw new ArgumentException("Course code and title are required.");
        var item = new CourseOffering { CourseCode=r.CourseCode, CourseTitle=r.CourseTitle, Semester=r.Semester, Credits=r.Credits, HistoricalEnrollment=r.HistoricalEnrollment, PredictedEnrollment=r.PredictedEnrollment, RequiresLab=r.RequiresLab, CrossListedWith=r.CrossListedWith, CoLocationGroup=r.CoLocationGroup };
        await offerings.AddAsync(item); return item;
    }
}

public sealed class SchedulingService(IOfferingRepository offerings, IInstructorRepository instructors, IRoomRepository rooms, IConflictService conflicts) : ISchedulingService
{
    public async Task<ScheduleDraft> GenerateDraftAsync(GenerateDraftRequest request)
    {
        var all = await offerings.GetBySemesterAsync(request.Semester);
        var selected = request.OfferingIds.Count == 0 ? all : all.Where(x => request.OfferingIds.Contains(x.Id)).ToList();
        var ins = await instructors.GetAllAsync(); var rms = await rooms.GetAllAsync();
        var draft = new ScheduleDraft { Name = $"{request.Semester} Draft {DateTime.UtcNow:yyyyMMddHHmm}", Semester=request.Semester };
        var blocks = new[] { ("Monday", "09:00-10:15"), ("Tuesday", "10:30-11:45"), ("Wednesday", "13:00-14:15"), ("Thursday", "15:00-16:15") };
        for (var i=0; i<selected.Count; i++)
        {
            var course=selected[i]; var instructor=ins.FirstOrDefault(x=>x.QualifiedCourses.Contains(course.CourseCode)); var room=rms.FirstOrDefault(x=>x.Capacity>=course.PredictedEnrollment && (!course.RequiresLab || x.RoomType.Equals("Lab",StringComparison.OrdinalIgnoreCase)));
            var b=blocks[i%blocks.Length]; draft.Sections.Add(new ScheduledSection { CourseOfferingId=course.Id, CourseCode=course.CourseCode, InstructorId=instructor?.Id ?? "", RoomId=room?.Id ?? "", Day=b.Item1, TimeBlock=b.Item2, IsLab=course.RequiresLab });
        }
        draft.RequiredViolationCount=(await conflicts.ValidateAsync(draft)).Count; draft.Status=draft.RequiredViolationCount==0?"Valid":"Needs Review"; return draft;
    }
}

public sealed class ConflictService(IOfferingRepository offerings, IInstructorRepository instructors, IRoomRepository rooms) : IConflictService
{
    public async Task<List<string>> ValidateAsync(ScheduleDraft draft)
    {
        var messages=new List<string>(); var courses=await Task.WhenAll(draft.Sections.Select(x=>offerings.GetAsync(x.CourseOfferingId))); var roomsList=await rooms.GetAllAsync();
        foreach(var section in draft.Sections)
        {
            var course=courses.FirstOrDefault(x=>x?.Id==section.CourseOfferingId); var room=roomsList.FirstOrDefault(x=>x.Id==section.RoomId);
            if(course is null) messages.Add($"{section.CourseCode}: offering not found.");
            if(room is null) messages.Add($"{section.CourseCode}: room is not assigned.");
            else if(course is not null && room.Capacity<course.PredictedEnrollment) messages.Add($"{section.CourseCode}: room capacity is below predicted enrollment.");
        }
        foreach(var group in draft.Sections.Where(x=>x.InstructorId!="").GroupBy(x=>new{x.InstructorId,x.Day,x.TimeBlock})) if(group.Count()>1) messages.Add($"Instructor overlap for {group.Key.InstructorId} on {group.Key.Day} at {group.Key.TimeBlock}.");
        foreach(var group in draft.Sections.Where(x=>x.RoomId!="").GroupBy(x=>new{x.RoomId,x.Day,x.TimeBlock})) if(group.Count()>1) messages.Add($"Room overlap for {group.Key.RoomId} on {group.Key.Day} at {group.Key.TimeBlock}.");
        return messages;
    }
}

public sealed class DraftService(IDraftRepository drafts) : IDraftService
{
    public Task<List<ScheduleDraft>> GetAllAsync()=>drafts.GetAllAsync(); public Task<ScheduleDraft?> GetAsync(string id)=>drafts.GetAsync(id);
    public async Task<ScheduleDraft> SaveAsync(ScheduleDraft draft){if(string.IsNullOrWhiteSpace(draft.Name))draft.Name=$"{draft.Semester} Draft";await drafts.AddAsync(draft);return draft;}
}
