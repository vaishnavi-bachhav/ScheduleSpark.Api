using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ScheduleSpark.Api.Models;

public abstract class MongoEntity
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
}

public sealed class CourseOffering : MongoEntity
{
    public string CourseCode { get; set; } = "";
    public string CourseTitle { get; set; } = "";
    public string Semester { get; set; } = "Fall 2026";
    public string ProgramLevel { get; set; } = "Graduate";

    public int Credits { get; set; }
    public int HistoricalEnrollment { get; set; }
    public int PredictedEnrollment { get; set; }
    public bool RequiresLab { get; set; }
    public bool IsActive { get; set; } = true;

    public List<string> RequiredEquipment { get; set; } = [];
    public string? CrossListedWith { get; set; }
    public string? CoLocationGroup { get; set; }
}

public sealed class Instructor : MongoEntity
{
    public string Name { get; set; } = "";
    public List<string> QualifiedCourses { get; set; } = [];
    public List<string> PreferredCourses { get; set; } = [];
    public List<string> AvailableBlocks { get; set; } = [];
    public int MaximumCourseLoad { get; set; } = 2;
    public bool PrefersBackToBack { get; set; }
    [BsonIgnore]
    public List<string>? SemesterAvailableBlocks { get; set; }
}

public sealed class InstructorSemesterAvailability : MongoEntity
{
    public string InstructorId { get; set; } = "";
    public string Semester { get; set; } = "";
    public List<string> AvailableBlocks { get; set; } = [];
}

public sealed class Room : MongoEntity
{
    public string RoomNumber { get; set; } = "";
    public string Building { get; set; } = "";
    public int Capacity { get; set; }
    public string RoomType { get; set; } = "Lecture";
    public List<string> Equipment { get; set; } = [];
    public List<string> AvailableBlocks { get; set; } = [];
}

public sealed class SchedulingConstraint : MongoEntity
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "Required";
    public int Priority { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public string Source { get; set; } = "Department rule";
}

public sealed class ScheduleBlockConfiguration : MongoEntity
{
    public string Semester { get; set; } = "Fall 2026";
    public List<UniversityScheduleBlock> Blocks { get; set; } = [];
}

public sealed class UniversityScheduleBlock
{
    public string TimeBlock { get; set; } = "";
    public string Label { get; set; } = "";
    public string Category { get; set; } = "Normal";
    public List<string> CourseTypes { get; set; } = ["Lecture", "Laboratory"];
    public bool Enabled { get; set; } = true;
}

public static class UniversityScheduleBlocks
{
    public static List<UniversityScheduleBlock> Defaults() =>
    [
        new() { TimeBlock = "08:00-09:20", Label = "8:00–9:20 AM" },
        new() { TimeBlock = "09:20-10:10", Label = "9:20–10:10 AM" },
        new() { TimeBlock = "10:10-11:30", Label = "10:10–11:30 AM" },
        new() { TimeBlock = "11:40-13:00", Label = "11:40 AM–1:00 PM" },
        new() { TimeBlock = "13:30-14:50", Label = "1:30–2:50 PM" },
        new() { TimeBlock = "15:00-16:20", Label = "3:00–4:20 PM" },
        new() { TimeBlock = "16:30-17:50", Label = "4:30–5:50 PM" },
        new() { TimeBlock = "18:00-19:20", Label = "6:00–7:20 PM", Category = "Evening" },
        new() { TimeBlock = "19:30-20:50", Label = "7:30–8:50 PM", Category = "Evening" },
        new() { TimeBlock = "ASYNC", Label = "Asynchronous", Category = "Asynchronous" },
    ];
}

public sealed class ScheduledSection
{
    public string CourseOfferingId { get; set; } = "";
    public string CourseCode { get; set; } = "";
    public string InstructorId { get; set; } = "";
    public string InstructorName { get; set; } = "";
    public string RoomId { get; set; } = "";
    public string RoomNumber { get; set; } = "";
    public string Day { get; set; } = "";
    public string TimeBlock { get; set; } = "";
    public bool IsLab { get; set; }
}

public sealed class ScheduleDraft : MongoEntity
{
    public string Name { get; set; } = "";
    public string Semester { get; set; } = "Fall 2026";
    public List<ScheduledSection> Sections { get; set; } = [];
    public int RequiredViolationCount { get; set; }
    public double PreferenceScore { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedAtUtc { get; set; } = DateTime.UtcNow;
    public int VersionNumber { get; set; } = 1;
}

public sealed class ScheduleDraftVersion : MongoEntity
{
    public string DraftId { get; set; } = "";
    public int VersionNumber { get; set; }
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
    public ScheduleDraft Snapshot { get; set; } = new();
}

public sealed class ConflictDetail
{
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "Required";
    public string CourseCode { get; set; } = "";
    public List<string> AffectedCourseCodes { get; set; } = [];
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string SuggestedAction { get; set; } = "Review the assignment and regenerate the draft.";
}
