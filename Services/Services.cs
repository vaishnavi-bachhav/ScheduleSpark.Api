using ScheduleSpark.Api.Models;
using ScheduleSpark.Api.Repositories;

namespace ScheduleSpark.Api.Services;

public interface IPlanningService { Task<object> GetWorkspaceAsync(string semester); Task<CourseOffering> CreateOfferingAsync(CreateOfferingRequest request); Task<ScheduleBlockConfiguration> SaveScheduleBlocksAsync(string semester, SaveScheduleBlocksRequest request); Task<InstructorSemesterAvailability> SaveInstructorAvailabilityAsync(string instructorId, string semester, SaveInstructorAvailabilityRequest request); }
public interface ISchedulingService { Task<ScheduleDraft> GenerateDraftAsync(GenerateDraftRequest request); }
public interface IConflictService { Task<List<string>> ValidateAsync(ScheduleDraft draft); Task<List<ConflictDetail>> ValidateDetailedAsync(ScheduleDraft draft); }
public interface IDraftService { Task<List<ScheduleDraft>> GetAllAsync(); Task<ScheduleDraft?> GetAsync(string id); Task<ScheduleDraft> SaveAsync(ScheduleDraft draft); Task<bool> DeleteAsync(string id); Task<ScheduleDraft?> DuplicateAsync(string id); Task<List<ScheduleDraftVersion>?> GetVersionsAsync(string id); Task<ScheduleDraft?> RestoreVersionAsync(string id, int versionNumber); }
public interface IScheduleEditService { Task<(ScheduleDraft? Draft, List<ConflictDetail> Conflicts)> EditSectionAsync(string draftId, string courseCode, EditScheduleSectionRequest request); }

public sealed class PlanningService(IOfferingRepository offerings, IInstructorRepository instructors, IRoomRepository rooms, IConstraintRepository constraints, IScheduleBlockConfigurationRepository blockConfigurations, IInstructorAvailabilityRepository instructorAvailability) : IPlanningService
{
    public async Task<object> GetWorkspaceAsync(string semester)
    {
        var instructorList = await instructors.GetAllAsync();
        var semesterAvailability = await instructorAvailability.GetBySemesterAsync(semester);
        var scheduleBlocks = (await blockConfigurations.GetOrCreateAsync(semester)).Blocks;
        foreach (var instructor in instructorList)
        {
            var saved = semesterAvailability.FirstOrDefault(x => x.InstructorId == instructor.Id);
            var source = saved?.AvailableBlocks ?? instructor.AvailableBlocks;
            instructor.SemesterAvailableBlocks = NormalizeAvailability(source, scheduleBlocks);
        }
        return new
        {
            Semester = semester,
            Offerings = await offerings.GetBySemesterAsync(semester),
            Instructors = instructorList,
            Rooms = await rooms.GetAllAsync(),
            Constraints = await constraints.GetEnabledAsync(),
            ScheduleBlocks = scheduleBlocks
        };
    }

    private static List<string> NormalizeAvailability(IEnumerable<string> blocks, List<UniversityScheduleBlock> approvedBlocks)
    {
        var normalized = new List<string>();
        foreach (var selection in blocks)
        {
            if (!SchedulingAvailability.TryParse(selection, out var day, out var timeBlock)) continue;
            var canonicalTime = SchedulingAvailability.CanonicalTimeBlock(timeBlock);
            var approved = approvedBlocks.FirstOrDefault(block => block.Enabled && block.TimeBlock.Equals(canonicalTime, StringComparison.OrdinalIgnoreCase) && block.CourseTypes.Count > 0);
            if (approved is null) continue;
            var asynchronous = approved.Category.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase);
            if (asynchronous != day.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase)) continue;
            normalized.Add($"{(asynchronous ? "Asynchronous" : CanonicalDay(day))} {approved.TimeBlock}");
        }
        return normalized.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string CanonicalDay(string day) => new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" }.FirstOrDefault(item => item.Equals(day, StringComparison.OrdinalIgnoreCase)) ?? day;

    public async Task<InstructorSemesterAvailability> SaveInstructorAvailabilityAsync(string instructorId, string semester, SaveInstructorAvailabilityRequest request)
    {
        if (await instructors.GetAsync(instructorId) is null) throw new KeyNotFoundException("Instructor not found.");
        var selected = request.AvailableBlocks ?? [];
        if (selected.Count == 0) throw new ArgumentException("Select at least one available block.");
        var configuration = await blockConfigurations.GetOrCreateAsync(semester);
        var validDays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Asynchronous" };
        var normalized = new List<string>();
        foreach (var selection in selected)
        {
            if (!SchedulingAvailability.TryParse(selection, out var day, out var timeBlock) || !validDays.Contains(day, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Choose an approved day and scheduling block.");
            var canonicalTime = SchedulingAvailability.CanonicalTimeBlock(timeBlock);
            var approved = configuration.Blocks.FirstOrDefault(block => block.Enabled && block.TimeBlock.Equals(canonicalTime, StringComparison.OrdinalIgnoreCase) && block.CourseTypes.Count > 0);
            if (approved is null) throw new ArgumentException($"The selected block '{selection}' is disabled or not approved for {semester}.");
            var isAsyncBlock = approved.Category.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase);
            if (isAsyncBlock != day.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Asynchronous blocks must use the Asynchronous day, and physical blocks must use a weekday.");
            var canonicalDay = isAsyncBlock ? "Asynchronous" : CanonicalDay(day);
            normalized.Add($"{canonicalDay} {approved.TimeBlock}");
        }
        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Count)
            throw new ArgumentException("Each day and block can only be selected once.");
        var availability = await instructorAvailability.GetAsync(instructorId, semester) ?? new InstructorSemesterAvailability { InstructorId = instructorId, Semester = semester };
        availability.AvailableBlocks = normalized;
        await instructorAvailability.SaveAsync(availability);
        return availability;
    }

    public async Task<ScheduleBlockConfiguration> SaveScheduleBlocksAsync(string semester, SaveScheduleBlocksRequest request)
    {
        var defaults = UniversityScheduleBlocks.Defaults();
        var submitted = request.Blocks ?? [];
        if (submitted.GroupBy(x => x.TimeBlock, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new ArgumentException("Each approved scheduling block can only be configured once.");
        var configured = new List<UniversityScheduleBlock>();
        foreach (var block in defaults)
        {
            var selected = submitted.FirstOrDefault(x => x.TimeBlock == block.TimeBlock);
            if (selected is null) { configured.Add(block); continue; }
            var courseTypes = (selected.CourseTypes ?? []).Where(type => type is "Lecture" or "Laboratory").Distinct().ToList();
            if (selected.Enabled && courseTypes.Count == 0)
                throw new ArgumentException($"Choose Lecture, Laboratory, or both for {block.Label}.");
            configured.Add(new UniversityScheduleBlock
            {
                TimeBlock = block.TimeBlock,
                Label = block.Label,
                Category = block.Category,
                CourseTypes = courseTypes,
                Enabled = selected.Enabled
            });
        }
        if (submitted.Any(x => !defaults.Any(block => block.TimeBlock == x.TimeBlock)))
            throw new ArgumentException("Only approved university scheduling blocks can be configured.");
        var configuration = await blockConfigurations.GetOrCreateAsync(semester);
        configuration.Blocks = configured;
        await blockConfigurations.UpdateAsync(configuration);
        return configuration;
    }

    public async Task<CourseOffering> CreateOfferingAsync(CreateOfferingRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.CourseCode) || string.IsNullOrWhiteSpace(r.CourseTitle)) throw new ArgumentException("Course code and title are required.");
        var item = new CourseOffering { CourseCode = r.CourseCode, CourseTitle = r.CourseTitle, Semester = r.Semester, Credits = r.Credits, HistoricalEnrollment = r.HistoricalEnrollment, PredictedEnrollment = r.PredictedEnrollment, RequiresLab = r.RequiresLab, RequiredEquipment = r.RequiredEquipment ?? [], CrossListedWith = r.CrossListedWith, CoLocationGroup = r.CoLocationGroup };
        await offerings.AddAsync(item); return item;
    }
}

public sealed class SchedulingService(IOfferingRepository offerings, IInstructorRepository instructors, IRoomRepository rooms, IConstraintRepository constraints, IConflictService conflicts, IDraftRepository drafts, IScheduleBlockConfigurationRepository blockConfigurations, IInstructorAvailabilityRepository instructorAvailability) : ISchedulingService
{
    public async Task<ScheduleDraft> GenerateDraftAsync(GenerateDraftRequest request)
    {
        var all = await offerings.GetBySemesterAsync(request.Semester);
        var selected = request.OfferingIds.Count == 0 ? all : all.Where(x => request.OfferingIds.Contains(x.Id)).ToList();
        var ins = await instructors.GetAllAsync(); var rms = await rooms.GetAllAsync();
        var enabledConstraints = await constraints.GetEnabledAsync();
        var approvedBlocks = (await blockConfigurations.GetOrCreateAsync(request.Semester)).Blocks;
        var semesterInstructorAvailability = await instructorAvailability.GetBySemesterAsync(request.Semester);
        var equipmentRuleEnabled = enabledConstraints.Any(x => NormalizeRuleName(x.Name) == NormalizeRuleName("Required room equipment"));
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
                    && x.RoomType.Equals(requiredRoomType, StringComparison.OrdinalIgnoreCase)
                    && (!equipmentRuleEnabled || HasRequiredEquipment(x, course)))
                .ToList();

            (Instructor Instructor, Room? Room, string Day, string TimeBlock)? assignment = null;
            foreach (var instructor in instructorCandidates)
            {
                var availableBlocks = semesterInstructorAvailability.FirstOrDefault(item => item.InstructorId == instructor.Id)?.AvailableBlocks ?? instructor.AvailableBlocks;
                foreach (var instructorBlock in availableBlocks)
                {
                    if (!SchedulingAvailability.TryParse(instructorBlock, out var day, out var timeBlock)) continue;
                    timeBlock = SchedulingAvailability.CanonicalTimeBlock(timeBlock);
                    var courseType = course.RequiresLab ? "Laboratory" : "Lecture";
                    if (!approvedBlocks.Any(block => block.Enabled && block.TimeBlock == timeBlock && block.CourseTypes.Contains(courseType))) continue;
                    var slot = SchedulingAvailability.SlotKey(day, timeBlock);
                    var isAsynchronous = timeBlock.Equals("ASYNC", StringComparison.OrdinalIgnoreCase);
                    if (!isAsynchronous && assignedInstructors.Contains((instructor.Id, slot))) continue;

                    if (isAsynchronous)
                    {
                        assignment = (instructor, null, "Asynchronous", timeBlock);
                        break;
                    }

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
                if (!chosen.Value.TimeBlock.Equals("ASYNC", StringComparison.OrdinalIgnoreCase))
                {
                    assignedInstructors.Add((chosen.Value.Instructor.Id, slot));
                    if (chosen.Value.Room is not null) occupiedRooms.Add((chosen.Value.Room.Id, slot));
                }
            }
            draft.Sections.Add(new ScheduledSection
            {
                CourseOfferingId = course.Id,
                CourseCode = course.CourseCode,
                InstructorId = chosen?.Instructor.Id ?? "",
                InstructorName = chosen?.Instructor.Name ?? "Instructor unassigned",
                RoomId = chosen?.Room?.Id ?? "",
                RoomNumber = chosen?.Room?.RoomNumber ?? (chosen?.TimeBlock == "ASYNC" ? "Online / asynchronous" : "Room unassigned"),
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

    private static string NormalizeRuleName(string name) => string.Concat(name.Where(char.IsLetterOrDigit));

    private static bool HasRequiredEquipment(Room room, CourseOffering course) =>
        (course.RequiredEquipment ?? []).All(required => (room.Equipment ?? []).Contains(required, StringComparer.OrdinalIgnoreCase));
}

public sealed class ConflictService(IOfferingRepository offerings, IInstructorRepository instructors, IRoomRepository rooms, IConstraintRepository constraints, IScheduleBlockConfigurationRepository blockConfigurations, IInstructorAvailabilityRepository instructorAvailability) : IConflictService
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
        var approvedBlocks = (await blockConfigurations.GetOrCreateAsync(draft.Semester)).Blocks;
        var semesterInstructorAvailability = await instructorAvailability.GetBySemesterAsync(draft.Semester);
        static string NormalizeRuleName(string name) => string.Concat(name.Where(char.IsLetterOrDigit));
        var enabledRuleNames = enabledRules.Select(x => NormalizeRuleName(x.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool RuleEnabled(params string[] names) => names.Any(name => enabledRuleNames.Contains(NormalizeRuleName(name)));
        static bool HasRequiredEquipment(Room room, CourseOffering course) =>
            (course.RequiredEquipment ?? []).All(required => (room.Equipment ?? []).Contains(required, StringComparer.OrdinalIgnoreCase));
        var courseById = courses.Where(x => x is not null).ToDictionary(x => x!.Id, x => x!);
        foreach (var section in draft.Sections)
        {
            courseById.TryGetValue(section.CourseOfferingId, out var course);
            var isAsynchronous = section.TimeBlock.Equals("ASYNC", StringComparison.OrdinalIgnoreCase);
            var courseType = section.IsLab ? "Laboratory" : "Lecture";
            var approvedBlock = approvedBlocks.FirstOrDefault(block => block.Enabled && block.TimeBlock == section.TimeBlock && block.CourseTypes.Contains(courseType));
            if (approvedBlock is null || (isAsynchronous != section.Day.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase)))
                conflicts.Add(new ConflictDetail { Code = "INVALID_TIME_BLOCK", CourseCode = section.CourseCode, Category = "Unapproved time block", Message = $"{section.CourseCode}: {section.TimeBlock} is not an enabled university block for {courseType.ToLowerInvariant()} courses in {draft.Semester}.", SuggestedAction = "Choose an enabled university scheduling block for this course type." });
            var instructor = instructorsList.FirstOrDefault(x => x.Id == section.InstructorId);
            List<string> availableBlocks = [];
            if (instructor is not null)
            {
                var instructorSemesterAvailability = semesterInstructorAvailability.FirstOrDefault(item => item.InstructorId == instructor.Id);
                availableBlocks = instructorSemesterAvailability?.AvailableBlocks ?? instructor.AvailableBlocks;
            }
            var room = roomsList.FirstOrDefault(x => x.Id == section.RoomId);
            if (course is null) conflicts.Add(new ConflictDetail { Code = "COURSE_NOT_FOUND", CourseCode = section.CourseCode, Category = "Course data", Message = $"{section.CourseCode}: offering not found.", SuggestedAction = "Return to the Planning Workspace and select a valid offering." });
            if (instructor is null) conflicts.Add(new ConflictDetail { Code = "INSTRUCTOR_UNASSIGNED", CourseCode = section.CourseCode, Category = "Instructor assignment", Message = $"{section.CourseCode}: instructor is not assigned.", SuggestedAction = "Assign a qualified instructor who is available for this time block." });
            else if (course is not null && RuleEnabled("Instructor qualification") && !instructor.QualifiedCourses.Contains(course.CourseCode, StringComparer.OrdinalIgnoreCase)) conflicts.Add(new ConflictDetail { Code = "INSTRUCTOR_UNQUALIFIED", CourseCode = section.CourseCode, Category = "Instructor qualification", Message = $"{section.CourseCode}: {instructor.Name} is not qualified to teach this course.", SuggestedAction = "Assign an instructor qualified for this course." });
            else if (RuleEnabled("Instructor availability") && !availableBlocks.Any(block => SchedulingAvailability.Matches(block, section.Day, section.TimeBlock))) conflicts.Add(new ConflictDetail { Code = "INSTRUCTOR_UNAVAILABLE", CourseCode = section.CourseCode, Category = "Instructor availability", Message = $"{section.CourseCode}: {instructor.Name} is not available on {section.Day} at {section.TimeBlock}.", SuggestedAction = "Choose one of the instructor's available blocks for this semester." });
            if (room is null && !isAsynchronous) conflicts.Add(new ConflictDetail { Code = "ROOM_UNASSIGNED", CourseCode = section.CourseCode, Category = "Room assignment", Message = $"{section.CourseCode}: room is not assigned.", SuggestedAction = "Assign an available room with sufficient capacity and the required room type." });
            else if (!isAsynchronous && room is not null && course is not null && RuleEnabled("Room capacity") && room.Capacity < course.PredictedEnrollment) conflicts.Add(new ConflictDetail { Code = "ROOM_CAPACITY", CourseCode = section.CourseCode, Category = "Room capacity", Message = $"{section.CourseCode}: predicted enrollment is {course.PredictedEnrollment}, but {room.RoomNumber} has capacity {room.Capacity}.", SuggestedAction = "Assign a larger room or revise the predicted enrollment." });
            if (!isAsynchronous && room is not null && course is not null)
            {
                var expectedRoomType = course.RequiresLab ? "Lab" : "Lecture";
                if (course.RequiresLab && RuleEnabled("Required laboratory room") && !room.RoomType.Equals("Lab", StringComparison.OrdinalIgnoreCase)) conflicts.Add(new ConflictDetail { Code = "ROOM_TYPE_MISMATCH", CourseCode = section.CourseCode, Category = "Room type", Message = $"{section.CourseCode}: requires a laboratory room, but {room.RoomNumber} is a {room.RoomType} room.", SuggestedAction = "Assign a laboratory room." });
                if (RuleEnabled("Room availability") && !room.AvailableBlocks.Any(block => SchedulingAvailability.Matches(block, section.Day, section.TimeBlock))) conflicts.Add(new ConflictDetail { Code = "ROOM_UNAVAILABLE", CourseCode = section.CourseCode, Category = "Room availability", Message = $"{section.CourseCode}: {room.RoomNumber} is not available on {section.Day} at {section.TimeBlock}.", SuggestedAction = "Choose a time block when the room is available." });
                if (RuleEnabled("Required room equipment"))
                {
                    var missingEquipment = (course.RequiredEquipment ?? [])
                        .Where(required => !(room.Equipment ?? []).Contains(required, StringComparer.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (missingEquipment.Count > 0) conflicts.Add(new ConflictDetail { Code = "ROOM_EQUIPMENT_MISSING", CourseCode = section.CourseCode, Category = "Room equipment", Message = $"{section.CourseCode} cannot use {room.RoomNumber} because it is missing required equipment: {string.Join(", ", missingEquipment)}.", SuggestedAction = "Assign a room with the required equipment or update the course requirements." });
                }
            }
            else if (!isAsynchronous && room is null && course is not null && RuleEnabled("Required room equipment"))
            {
                var expectedRoomType = course.RequiresLab ? "Lab" : "Lecture";
                var potentialRooms = roomsList.Where(candidate => candidate.Capacity >= course.PredictedEnrollment && candidate.RoomType.Equals(expectedRoomType, StringComparison.OrdinalIgnoreCase)).ToList();
                if (potentialRooms.Count > 0 && !potentialRooms.Any(candidate => HasRequiredEquipment(candidate, course)))
                {
                    var missingEquipment = (course.RequiredEquipment ?? []).Where(required => potentialRooms.All(candidate => !(candidate.Equipment ?? []).Contains(required, StringComparer.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (missingEquipment.Count > 0) conflicts.Add(new ConflictDetail { Code = "ROOM_EQUIPMENT_MISSING", CourseCode = section.CourseCode, Category = "Room equipment", Message = $"{section.CourseCode} has no suitable room with the required equipment: {string.Join(", ", missingEquipment)}.", SuggestedAction = "Add a room with the required equipment or update the course requirements." });
                }
            }
        }
        foreach (var group in draft.Sections.Where(x => x.InstructorId != "" && !x.TimeBlock.Equals("ASYNC", StringComparison.OrdinalIgnoreCase)).GroupBy(x => new { x.InstructorId, x.Day, x.TimeBlock }))
        {
            var courseCodes = group.Select(x => x.CourseCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (group.Count() > 1 && RuleEnabled("Avoid instructor time conflicts")) conflicts.Add(new ConflictDetail { Code = "INSTRUCTOR_OVERLAP", CourseCode = string.Join(", ", courseCodes), AffectedCourseCodes = courseCodes, Category = "Instructor overlap", Message = $"Instructor {group.Key.InstructorId} is assigned to multiple courses on {group.Key.Day} at {group.Key.TimeBlock}.", SuggestedAction = "Move one course to another available time block or instructor." });
        }
        foreach (var group in draft.Sections.Where(x => x.RoomId != "" && !x.TimeBlock.Equals("ASYNC", StringComparison.OrdinalIgnoreCase)).GroupBy(x => new { x.RoomId, x.Day, x.TimeBlock }))
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

    public static string CanonicalTimeBlock(string timeBlock) => timeBlock.Trim() switch
    {
        "09:00-10:15" => "09:20-10:10",
        "10:30-11:45" => "10:10-11:30",
        "13:00-14:15" => "13:30-14:50",
        "15:00-16:15" => "15:00-16:20",
        _ => timeBlock.Trim()
    };

    public static string SlotKey(string day, string timeBlock) => $"{day.Trim()}|{CanonicalTimeBlock(timeBlock)}".ToUpperInvariant();

    public static bool Matches(string block, string day, string timeBlock) =>
        TryParse(block, out var availableDay, out var availableTime)
        && SlotKey(availableDay, availableTime) == SlotKey(day, timeBlock);
}

public sealed class DraftService(IDraftRepository drafts, IDraftVersionRepository versions) : IDraftService
{
    public Task<List<ScheduleDraft>> GetAllAsync() => drafts.GetAllAsync();
    public Task<ScheduleDraft?> GetAsync(string id) => drafts.GetAsync(id);

    public async Task<bool> DeleteAsync(string id)
    {
        if (await drafts.GetAsync(id) is null) return false;
        await drafts.DeleteAsync(id);
        await versions.DeleteByDraftIdAsync(id);
        return true;
    }

    public async Task<ScheduleDraft?> DuplicateAsync(string id)
    {
        var source = await drafts.GetAsync(id);
        if (source is null) return null;

        var now = DateTime.UtcNow;
        var duplicate = CopyDraft(source);
        duplicate.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        duplicate.Name = $"{(string.IsNullOrWhiteSpace(source.Name) ? "Untitled schedule draft" : source.Name)} copy";
        duplicate.CreatedAtUtc = now;
        duplicate.LastModifiedAtUtc = now;
        duplicate.VersionNumber = 1;
        await drafts.AddAsync(duplicate);
        return duplicate;
    }

    public async Task<List<ScheduleDraftVersion>?> GetVersionsAsync(string id)
    {
        var current = await drafts.GetAsync(id);
        if (current is null) return null;
        var history = await versions.GetByDraftIdAsync(id);
        history.Add(new ScheduleDraftVersion
        {
            DraftId = id,
            VersionNumber = Math.Max(1, current.VersionNumber),
            ModifiedAtUtc = current.LastModifiedAtUtc,
            Snapshot = CopyDraft(current)
        });
        return history.OrderByDescending(item => item.VersionNumber).ToList();
    }

    public async Task<ScheduleDraft?> RestoreVersionAsync(string id, int versionNumber)
    {
        var snapshot = await versions.GetAsync(id, versionNumber);
        if (snapshot is null) return null;
        var restored = CopyDraft(snapshot.Snapshot);
        restored.Id = id;
        return await SaveAsync(restored);
    }

    public async Task<ScheduleDraft> SaveAsync(ScheduleDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name)) draft.Name = $"{draft.Semester} Draft";
        var now = DateTime.UtcNow;
        var existing = await drafts.GetAsync(draft.Id);
        if (existing is null)
        {
            draft.VersionNumber = Math.Max(1, draft.VersionNumber);
            draft.CreatedAtUtc = draft.CreatedAtUtc == default ? now : draft.CreatedAtUtc;
            draft.LastModifiedAtUtc = now;
            await drafts.AddAsync(draft);
            return draft;
        }

        if (!HasChanges(existing, draft)) return existing;

        var currentVersion = Math.Max(1, existing.VersionNumber);
        await versions.AddAsync(new ScheduleDraftVersion
        {
            DraftId = existing.Id,
            VersionNumber = currentVersion,
            ModifiedAtUtc = existing.LastModifiedAtUtc,
            Snapshot = CopyDraft(existing)
        });
        draft.VersionNumber = currentVersion + 1;
        draft.CreatedAtUtc = existing.CreatedAtUtc;
        draft.LastModifiedAtUtc = now;
        await drafts.UpdateAsync(draft);
        return draft;
    }

    private static bool HasChanges(ScheduleDraft before, ScheduleDraft after)
    {
        if (before.Name != after.Name || before.Semester != after.Semester || before.Status != after.Status
            || before.RequiredViolationCount != after.RequiredViolationCount || before.PreferenceScore != after.PreferenceScore
            || before.Sections.Count != after.Sections.Count) return true;

        for (var i = 0; i < before.Sections.Count; i++)
        {
            var left = before.Sections[i];
            var right = after.Sections[i];
            if (left.CourseOfferingId != right.CourseOfferingId || left.CourseCode != right.CourseCode
                || left.InstructorId != right.InstructorId || left.InstructorName != right.InstructorName
                || left.RoomId != right.RoomId || left.RoomNumber != right.RoomNumber || left.Day != right.Day
                || left.TimeBlock != right.TimeBlock || left.IsLab != right.IsLab) return true;
        }
        return false;
    }

    private static ScheduleDraft CopyDraft(ScheduleDraft source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Semester = source.Semester,
        Sections = source.Sections.Select(section => new ScheduledSection
        {
            CourseOfferingId = section.CourseOfferingId,
            CourseCode = section.CourseCode,
            InstructorId = section.InstructorId,
            InstructorName = section.InstructorName,
            RoomId = section.RoomId,
            RoomNumber = section.RoomNumber,
            Day = section.Day,
            TimeBlock = section.TimeBlock,
            IsLab = section.IsLab
        }).ToList(),
        RequiredViolationCount = source.RequiredViolationCount,
        PreferenceScore = source.PreferenceScore,
        Status = source.Status,
        CreatedAtUtc = source.CreatedAtUtc,
        LastModifiedAtUtc = source.LastModifiedAtUtc,
        VersionNumber = source.VersionNumber
    };
}

public sealed class ScheduleEditService(IDraftService drafts, IConflictService conflicts, IInstructorRepository instructors,
    IRoomRepository rooms, IScheduleBlockConfigurationRepository blockConfigurations) : IScheduleEditService
{
    public async Task<(ScheduleDraft? Draft, List<ConflictDetail> Conflicts)> EditSectionAsync(string draftId, string courseCode, EditScheduleSectionRequest request)
    {
        var draft = await drafts.GetAsync(draftId);
        if (draft is null) return (null, []);

        var section = draft.Sections.FirstOrDefault(x => x.CourseCode.Equals(courseCode, StringComparison.OrdinalIgnoreCase));
        if (section is null) return (draft, [new ConflictDetail { Code = "COURSE_NOT_IN_DRAFT", CourseCode = courseCode, Category = "Schedule edit", Message = $"{courseCode} is not present in this draft.", SuggestedAction = "Open a draft containing the selected course." }]);

        var courseType = section.IsLab ? "Laboratory" : "Lecture";
        var approvedBlocks = (await blockConfigurations.GetOrCreateAsync(draft.Semester)).Blocks;
        var approvedBlock = approvedBlocks.FirstOrDefault(block => block.Enabled && block.TimeBlock == request.TimeBlock && block.CourseTypes.Contains(courseType));
        var isAsynchronous = approvedBlock?.Category.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase) == true;
        if (approvedBlock is null || (isAsynchronous != request.Day.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase)))
            return (draft, [new ConflictDetail { Code = "INVALID_TIME_BLOCK", CourseCode = courseCode, Category = "Unapproved time block", Message = $"{request.TimeBlock} is not an enabled university block for {courseType.ToLowerInvariant()} courses in {draft.Semester}.", SuggestedAction = "Choose an enabled university scheduling block for this course type." }]);

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
        var savedDraft = await drafts.SaveAsync(draft);
        return (savedDraft, details);
    }
}
