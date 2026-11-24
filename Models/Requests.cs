namespace ScheduleSpark.Api.Models;

public sealed record CreateOfferingRequest(string CourseCode, string CourseTitle, string Semester, int Credits, int HistoricalEnrollment, int PredictedEnrollment, bool RequiresLab, string? CrossListedWith, string? CoLocationGroup);
public sealed record GenerateDraftRequest(string Semester, List<string> OfferingIds);
public sealed record ValidateDraftRequest(ScheduleDraft Draft);
