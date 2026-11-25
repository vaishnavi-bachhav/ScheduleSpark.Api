namespace ScheduleSpark.Api.Models;

public sealed record CreateOfferingRequest(string CourseCode, string CourseTitle, string Semester, int Credits, int HistoricalEnrollment, int PredictedEnrollment, bool RequiresLab, string? CrossListedWith, string? CoLocationGroup, List<string>? RequiredEquipment = null);
public sealed record GenerateDraftRequest(string Semester, List<string> OfferingIds);
public sealed record ValidateDraftRequest(ScheduleDraft Draft);
public sealed record EditScheduleSectionRequest(string InstructorId, string RoomId, string Day, string TimeBlock);
public sealed record SaveScheduleBlocksRequest(List<UniversityScheduleBlock> Blocks);
public sealed record SaveInstructorAvailabilityRequest(List<string> AvailableBlocks);

public sealed class ScheduleImportBatch
{
    public string Semester { get; set; } = "";
    public List<CourseOffering> Courses { get; set; } = [];
    public List<Instructor> Instructors { get; set; } = [];
    public List<Room> Rooms { get; set; } = [];
    public List<InstructorSemesterAvailability> InstructorAvailability { get; set; } = [];
}

public sealed record ScheduleImportIssue(string Severity, string Source, int Row, string Message);
public sealed record ScheduleImportPreview(ScheduleImportBatch Batch, List<ScheduleImportIssue> Warnings, List<ScheduleImportIssue> Errors, bool CanConfirm);
public sealed record ScheduleImportSummary(int CoursesImported, int InstructorsImported, int RoomsImported, int AvailabilityRecordsSaved, List<ScheduleImportIssue> Warnings, List<ScheduleImportIssue> Errors);
