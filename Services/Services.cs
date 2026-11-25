using ScheduleSpark.Api.Models;
using ScheduleSpark.Api.Repositories;

namespace ScheduleSpark.Api.Services;

public interface IPlanningService { Task<object> GetWorkspaceAsync(string semester); Task<CourseOffering> CreateOfferingAsync(CreateOfferingRequest request); }
public interface ISchedulingService { Task<ScheduleDraft> GenerateDraftAsync(GenerateDraftRequest request); }
public interface IConflictService { Task<List<string>> ValidateAsync(ScheduleDraft draft); Task<List<ConflictDetail>> ValidateDetailedAsync(ScheduleDraft draft); }
public interface IDraftService { Task<List<ScheduleDraft>> GetAllAsync(); Task<ScheduleDraft?> GetAsync(string id); Task<ScheduleDraft> SaveAsync(ScheduleDraft draft); Task<bool> DeleteAsync(string id); }
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
        var instructorLoads = ins.ToDictionary(x => x.Id, _ => 0);
        var assignedInstructors = new HashSet<(string InstructorId, string Slot)>();
        var occupiedRooms = new HashSet<(string RoomId, string Slot)>();
        foreach (var course in selected)
        {
            var instructorCandidates = ins
                .Where(x => x.QualifiedCourses.Contains(course.CourseCode, StringComparer.OrdinalIgnoreCase)
                    && instructorLoads[x.Id] < x.MaximumCourseLoad)
                .OrderByDescending(x => x.PreferredCourses.Contains(course.CourseCode, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var requiredRoomType = course.RequiresLab ? "Lab" : "Lecture";
            var roomCandidates = rms
                .Where(x => x.Capacity >= course.PredictedEnrollment
                    && x.RoomType.Equals(requiredRoomType, StringComparison.OrdinalIgnoreCase))
                .ToList();

            (Instructor Instructor, Room Room, string Day, string TimeBlock)? assignment = null;
            foreach (var instructor in instructorCandidates)
            {
                foreach (var instructorBlock in instructor.AvailableBlocks)
                {
                    if (!SchedulingAvailability.TryParse(instructorBlock, out var day, out var timeBlock)) continue;
                    var slot = SchedulingAvailability.SlotKey(day, timeBlock);
                    if (assignedInstructors.Contains((instructor.Id, slot))) continue;

                    foreach (var room in roomCandidates)
                    {
                        if (!room.AvailableBlocks.Any(block => SchedulingAvailability.Matches(block, day, timeBlock))) continue;
                        if (occupiedRooms.Contains((room.Id, slot))) continue;
                        assignment = (instructor, room, day, timeBlock);
                        break;
                    }
                    if (assignment is not null) break;
                }
                if (assignment is not null) break;
            }

            var chosen = assignment;
            if (chosen is not null)
            {
                instructorLoads[chosen.Value.Instructor.Id]++;
                var slot = SchedulingAvailability.SlotKey(chosen.Value.Day, chosen.Value.TimeBlock);
                assignedInstructors.Add((chosen.Value.Instructor.Id, slot));
                occupiedRooms.Add((chosen.Value.Room.Id, slot));
            }
            draft.Sections.Add(new ScheduledSection
            {
                CourseOfferingId = course.Id,
                CourseCode = course.CourseCode,
                InstructorId = chosen?.Instructor.Id ?? "",
                InstructorName = chosen?.Instructor.Name ?? "Instructor unassigned",
                RoomId = chosen?.Room.Id ?? "",
                RoomNumber = chosen?.Room.RoomNumber ?? "Room unassigned",
                Day = chosen?.Day ?? "",
                TimeBlock = chosen?.TimeBlock ?? "",
                IsLab = course.RequiresLab
            });
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

public sealed class ConflictService(IOfferingRepository offerings, IInstructorRepository instructors, IRoomRepository rooms, IConstraintRepository constraints) : IConflictService
{
    public async Task<List<string>> ValidateAsync(ScheduleDraft draft)
    {
        var details = await ValidateDetailedAsync(draft);
        return details.Select(x => x.Message).ToList();
    }

    public async Task<List<ConflictDetail>> ValidateDetailedAsync(ScheduleDraft draft)
    {
        var conflicts = new List<ConflictDetail>();
        var courses = await Task.WhenAll(draft.Sections.Select(x => offerings.GetAsync(x.CourseOfferingId)));
        var instructorsList = await instructors.GetAllAsync();
        var roomsList = await rooms.GetAllAsync();
        var enabledRules = await constraints.GetEnabledAsync();
        static string NormalizeRuleName(string name) => string.Concat(name.Where(char.IsLetterOrDigit));
        var enabledRuleNames = enabledRules.Select(x => NormalizeRuleName(x.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool RuleEnabled(params string[] names) => names.Any(name => enabledRuleNames.Contains(NormalizeRuleName(name)));
        var courseById = courses.Where(x => x is not null).ToDictionary(x => x!.Id, x => x!);
        foreach (var section in draft.Sections)
        {
            courseById.TryGetValue(section.CourseOfferingId, out var course);
            var instructor = instructorsList.FirstOrDefault(x => x.Id == section.InstructorId);
            var room = roomsList.FirstOrDefault(x => x.Id == section.RoomId);
            if (course is null) conflicts.Add(new ConflictDetail { Code = "COURSE_NOT_FOUND", CourseCode = section.CourseCode, Category = "Course data", Message = $"{section.CourseCode}: offering not found.", SuggestedAction = "Return to the Planning Workspace and select a valid offering." });
            if (instructor is null) conflicts.Add(new ConflictDetail { Code = "INSTRUCTOR_UNASSIGNED", CourseCode = section.CourseCode, Category = "Instructor assignment", Message = $"{section.CourseCode}: instructor is not assigned.", SuggestedAction = "Assign a qualified instructor who is available for this time block." });
            else if (course is not null && RuleEnabled("Instructor qualification") && !instructor.QualifiedCourses.Contains(course.CourseCode, StringComparer.OrdinalIgnoreCase)) conflicts.Add(new ConflictDetail { Code = "INSTRUCTOR_UNQUALIFIED", CourseCode = section.CourseCode, Category = "Instructor qualification", Message = $"{section.CourseCode}: {instructor.Name} is not qualified to teach this course.", SuggestedAction = "Assign an instructor qualified for this course." });
            else if (RuleEnabled("Instructor availability") && !instructor.AvailableBlocks.Any(block => SchedulingAvailability.Matches(block, section.Day, section.TimeBlock))) conflicts.Add(new ConflictDetail { Code = "INSTRUCTOR_UNAVAILABLE", CourseCode = section.CourseCode, Category = "Instructor availability", Message = $"{section.CourseCode}: {instructor.Name} is not available on {section.Day} at {section.TimeBlock}.", SuggestedAction = "Choose one of the instructor's available blocks." });
            if (room is null) conflicts.Add(new ConflictDetail { Code = "ROOM_UNASSIGNED", CourseCode = section.CourseCode, Category = "Room assignment", Message = $"{section.CourseCode}: room is not assigned.", SuggestedAction = "Assign an available room with sufficient capacity and the required room type." });
            else if (course is not null && RuleEnabled("Room capacity") && room.Capacity < course.PredictedEnrollment) conflicts.Add(new ConflictDetail { Code = "ROOM_CAPACITY", CourseCode = section.CourseCode, Category = "Room capacity", Message = $"{section.CourseCode}: predicted enrollment is {course.PredictedEnrollment}, but {room.RoomNumber} has capacity {room.Capacity}.", SuggestedAction = "Assign a larger room or revise the predicted enrollment." });
            if (room is not null && course is not null)
            {
                var expectedRoomType = course.RequiresLab ? "Lab" : "Lecture";
                if (course.RequiresLab && RuleEnabled("Required laboratory room") && !room.RoomType.Equals("Lab", StringComparison.OrdinalIgnoreCase)) conflicts.Add(new ConflictDetail { Code = "ROOM_TYPE_MISMATCH", CourseCode = section.CourseCode, Category = "Room type", Message = $"{section.CourseCode}: requires a laboratory room, but {room.RoomNumber} is a {room.RoomType} room.", SuggestedAction = "Assign a laboratory room." });
                if (RuleEnabled("Room availability") && !room.AvailableBlocks.Any(block => SchedulingAvailability.Matches(block, section.Day, section.TimeBlock))) conflicts.Add(new ConflictDetail { Code = "ROOM_UNAVAILABLE", CourseCode = section.CourseCode, Category = "Room availability", Message = $"{section.CourseCode}: {room.RoomNumber} is not available on {section.Day} at {section.TimeBlock}.", SuggestedAction = "Choose a time block when the room is available." });
            }
        }
        foreach (var group in draft.Sections.Where(x => x.InstructorId != "").GroupBy(x => new { x.InstructorId, x.Day, x.TimeBlock }))
        {
            var courseCodes = group.Select(x => x.CourseCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (group.Count() > 1 && RuleEnabled("Avoid instructor time conflicts")) conflicts.Add(new ConflictDetail { Code = "INSTRUCTOR_OVERLAP", CourseCode = string.Join(", ", courseCodes), AffectedCourseCodes = courseCodes, Category = "Instructor overlap", Message = $"Instructor {group.Key.InstructorId} is assigned to multiple courses on {group.Key.Day} at {group.Key.TimeBlock}.", SuggestedAction = "Move one course to another available time block or instructor." });
        }
        foreach (var group in draft.Sections.Where(x => x.RoomId != "").GroupBy(x => new { x.RoomId, x.Day, x.TimeBlock }))
        {
            var courseCodes = group.Select(x => x.CourseCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (group.Count() > 1 && RuleEnabled("Avoid room conflicts")) conflicts.Add(new ConflictDetail { Code = "ROOM_OVERLAP", CourseCode = string.Join(", ", courseCodes), AffectedCourseCodes = courseCodes, Category = "Room overlap", Message = $"Room {group.Key.RoomId} is assigned to multiple courses on {group.Key.Day} at {group.Key.TimeBlock}.", SuggestedAction = "Assign a different room or move one course to another time block." });
        }
        foreach (var group in draft.Sections.Where(x => x.InstructorId != "").GroupBy(x => x.InstructorId))
        {
            var instructor = instructorsList.FirstOrDefault(x => x.Id == group.Key);
            if (instructor is not null && RuleEnabled("Instructor maximum load") && group.Count() > instructor.MaximumCourseLoad) conflicts.Add(new ConflictDetail { Code = "INSTRUCTOR_MAX_LOAD", CourseCode = string.Join(", ", group.Select(x => x.CourseCode)), Category = "Instructor workload", Message = $"{instructor.Name} is assigned {group.Count()} courses, exceeding the maximum load of {instructor.MaximumCourseLoad}.", SuggestedAction = "Reassign courses to instructors with available load." });
        }
        return conflicts;
    }
}

internal static class SchedulingAvailability
{
    public static bool TryParse(string block, out string day, out string timeBlock)
    {
        day = "";
        timeBlock = "";
        if (string.IsNullOrWhiteSpace(block)) return false;
        var splitAt = block.IndexOf(' ');
        if (splitAt <= 0 || splitAt == block.Length - 1) return false;
        day = block[..splitAt].Trim();
        timeBlock = block[(splitAt + 1)..].Trim();
        return day.Length > 0 && timeBlock.Length > 0;
    }

    public static string SlotKey(string day, string timeBlock) => $"{day.Trim()}|{timeBlock.Trim()}".ToUpperInvariant();

    public static bool Matches(string block, string day, string timeBlock) =>
        TryParse(block, out var availableDay, out var availableTime)
        && SlotKey(availableDay, availableTime) == SlotKey(day, timeBlock);
}

public sealed class DraftService(IDraftRepository drafts) : IDraftService
{
    public Task<List<ScheduleDraft>> GetAllAsync() => drafts.GetAllAsync(); public Task<ScheduleDraft?> GetAsync(string id) => drafts.GetAsync(id);
    public async Task<bool> DeleteAsync(string id)
    {
        if (await drafts.GetAsync(id) is null) return false;
        await drafts.DeleteAsync(id);
        return true;
    }
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
