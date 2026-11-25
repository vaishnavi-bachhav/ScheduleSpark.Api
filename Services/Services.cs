using ScheduleSpark.Api.Models;
using ScheduleSpark.Api.Repositories;

namespace ScheduleSpark.Api.Services;

public interface IPlanningService { Task<object> GetWorkspaceAsync(string semester); Task<CourseOffering> CreateOfferingAsync(CreateOfferingRequest request); }
public interface ISchedulingService { Task<ScheduleDraft> GenerateDraftAsync(GenerateDraftRequest request); }
public interface IConflictService { Task<List<string>> ValidateAsync(ScheduleDraft draft); Task<List<ConflictDetail>> ValidateDetailedAsync(ScheduleDraft draft); }
public interface IDraftService { Task<List<ScheduleDraft>> GetAllAsync(); Task<ScheduleDraft?> GetAsync(string id); Task<ScheduleDraft> SaveAsync(ScheduleDraft draft); }
public interface IScheduleEditService { Task<(ScheduleDraft? Draft, List<ConflictDetail> Conflicts)> EditSectionAsync(string draftId, string courseCode, EditScheduleSectionRequest request); }

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
        var item = new CourseOffering { CourseCode = r.CourseCode, CourseTitle = r.CourseTitle, Semester = r.Semester, Credits = r.Credits, HistoricalEnrollment = r.HistoricalEnrollment, PredictedEnrollment = r.PredictedEnrollment, RequiresLab = r.RequiresLab, CrossListedWith = r.CrossListedWith, CoLocationGroup = r.CoLocationGroup };
        await offerings.AddAsync(item); return item;
    }
}

public sealed class SchedulingService(IOfferingRepository offerings, IInstructorRepository instructors, IRoomRepository rooms, IConflictService conflicts, IDraftRepository drafts) : ISchedulingService
{
    public async Task<ScheduleDraft> GenerateDraftAsync(GenerateDraftRequest request)
    {
        var all = await offerings.GetBySemesterAsync(request.Semester);
        var selected = request.OfferingIds.Count == 0 ? all : all.Where(x => request.OfferingIds.Contains(x.Id)).ToList();
        var ins = await instructors.GetAllAsync(); var rms = await rooms.GetAllAsync();
        var draft = new ScheduleDraft { Name = $"{request.Semester} Draft {DateTime.UtcNow:yyyyMMddHHmm}", Semester = request.Semester };
        var blocks = new[] { ("Monday", "09:00-10:15"), ("Tuesday", "10:30-11:45"), ("Wednesday", "13:00-14:15"), ("Thursday", "15:00-16:15") };
        for (var i = 0; i < selected.Count; i++)
        {
            var course = selected[i]; var instructor = ins.FirstOrDefault(x => x.QualifiedCourses.Contains(course.CourseCode)); var room = rms.FirstOrDefault(x => x.Capacity >= course.PredictedEnrollment && (!course.RequiresLab || x.RoomType.Equals("Lab", StringComparison.OrdinalIgnoreCase)));
            var b = blocks[i % blocks.Length]; draft.Sections.Add(new ScheduledSection { CourseOfferingId = course.Id, CourseCode = course.CourseCode, InstructorId = instructor?.Id ?? "", InstructorName = instructor?.Name ?? "Instructor unassigned", RoomId = room?.Id ?? "", RoomNumber = room?.RoomNumber ?? "Room unassigned", Day = b.Item1, TimeBlock = b.Item2, IsLab = course.RequiresLab });
        }
        var validationErrors = await conflicts.ValidateDetailedAsync(draft);

        draft.RequiredViolationCount = validationErrors.Count;
        draft.Status = validationErrors.Count == 0
            ? "Valid"
            : "Needs Review";

        await drafts.AddAsync(draft);

        return draft;
    }
}

public sealed class ConflictService(IOfferingRepository offerings, IInstructorRepository instructors, IRoomRepository rooms) : IConflictService
{
    public async Task<List<string>> ValidateAsync(ScheduleDraft draft)
    {
        var details = await ValidateDetailedAsync(draft);
        return details.Select(x => x.Message).ToList();
    }

    public async Task<List<ConflictDetail>> ValidateDetailedAsync(ScheduleDraft draft)
    {
        var conflicts = new List<ConflictDetail>(); var courses = await Task.WhenAll(draft.Sections.Select(x => offerings.GetAsync(x.CourseOfferingId))); var roomsList = await rooms.GetAllAsync();
        foreach (var section in draft.Sections)
        {
            var course = courses.FirstOrDefault(x => x?.Id == section.CourseOfferingId); var room = roomsList.FirstOrDefault(x => x.Id == section.RoomId);
            if (course is null) conflicts.Add(new ConflictDetail { Code = "COURSE_NOT_FOUND", CourseCode = section.CourseCode, Category = "Course data", Message = $"{section.CourseCode}: offering not found.", SuggestedAction = "Return to the Planning Workspace and select a valid offering." });
            if (room is null) conflicts.Add(new ConflictDetail { Code = "ROOM_UNASSIGNED", CourseCode = section.CourseCode, Category = "Room assignment", Message = $"{section.CourseCode}: room is not assigned.", SuggestedAction = "Assign an available room with sufficient capacity." });
            else if (course is not null && room.Capacity < course.PredictedEnrollment) conflicts.Add(new ConflictDetail { Code = "ROOM_CAPACITY", CourseCode = section.CourseCode, Category = "Room capacity", Message = $"{section.CourseCode}: predicted enrollment is {course.PredictedEnrollment}, but {room.RoomNumber} has capacity {room.Capacity}.", SuggestedAction = "Assign a larger room or revise the predicted enrollment." });
        }
        foreach (var group in draft.Sections.Where(x => x.InstructorId != "").GroupBy(x => new { x.InstructorId, x.Day, x.TimeBlock })) if (group.Count() > 1) conflicts.Add(new ConflictDetail { Code = "INSTRUCTOR_OVERLAP", CourseCode = string.Join(", ", group.Select(x => x.CourseCode)), Category = "Instructor overlap", Message = $"Instructor {group.Key.InstructorId} is assigned to multiple courses on {group.Key.Day} at {group.Key.TimeBlock}.", SuggestedAction = "Move one course to another available time block or instructor." });
        foreach (var group in draft.Sections.Where(x => x.RoomId != "").GroupBy(x => new { x.RoomId, x.Day, x.TimeBlock })) if (group.Count() > 1) conflicts.Add(new ConflictDetail { Code = "ROOM_OVERLAP", CourseCode = string.Join(", ", group.Select(x => x.CourseCode)), Category = "Room overlap", Message = $"Room {group.Key.RoomId} is assigned to multiple courses on {group.Key.Day} at {group.Key.TimeBlock}.", SuggestedAction = "Assign a different room or move one course to another time block." });
        return conflicts;
    }
}

public sealed class DraftService(IDraftRepository drafts) : IDraftService
{
    public Task<List<ScheduleDraft>> GetAllAsync() => drafts.GetAllAsync(); public Task<ScheduleDraft?> GetAsync(string id) => drafts.GetAsync(id);
    public async Task<ScheduleDraft> SaveAsync(ScheduleDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
            draft.Name = $"{draft.Semester} Draft";

        draft.CreatedAtUtc = draft.CreatedAtUtc == default
            ? DateTime.UtcNow
            : draft.CreatedAtUtc;

        var existing = await drafts.GetAsync(draft.Id);
        if (existing is null)
            await drafts.AddAsync(draft);
        else
            await drafts.UpdateAsync(draft);

        return draft;
    }
}

public sealed class ScheduleEditService(IDraftRepository drafts, IConflictService conflicts, IInstructorRepository instructors,
    IRoomRepository rooms) : IScheduleEditService
{
    public async Task<(ScheduleDraft? Draft, List<ConflictDetail> Conflicts)> EditSectionAsync(string draftId, string courseCode, EditScheduleSectionRequest request)
    {
        var draft = await drafts.GetAsync(draftId);
        if (draft is null) return (null, []);

        var section = draft.Sections.FirstOrDefault(x => x.CourseCode.Equals(courseCode, StringComparison.OrdinalIgnoreCase));
        if (section is null) return (draft, [new ConflictDetail { Code = "COURSE_NOT_IN_DRAFT", CourseCode = courseCode, Category = "Schedule edit", Message = $"{courseCode} is not present in this draft.", SuggestedAction = "Open a draft containing the selected course." }]);

        var instructor = await instructors.GetAsync(request.InstructorId);
        var room = await rooms.GetAsync(request.RoomId);

        section.InstructorId = request.InstructorId;
        section.InstructorName = instructor?.Name ?? "Instructor unassigned";

        section.RoomId = request.RoomId;
        section.RoomNumber = room?.RoomNumber ?? "Room unassigned";

        section.Day = request.Day;
        section.TimeBlock = request.TimeBlock;

        var details = await conflicts.ValidateDetailedAsync(draft);
        draft.RequiredViolationCount = details.Count;
        draft.Status = details.Count == 0 ? "Valid" : "Needs Review";
        await drafts.UpdateAsync(draft);
        return (draft, details);
    }
}
