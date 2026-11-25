using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using ScheduleSpark.Api.Models;
using ScheduleSpark.Api.Repositories;

namespace ScheduleSpark.Api.Services;

public interface IScheduleImportService
{
    Task<ScheduleImportPreview> PreviewAsync(string semester, IReadOnlyList<IFormFile> files);
    Task<ScheduleImportSummary> ConfirmAsync(ScheduleImportBatch batch);
}

public sealed class ScheduleImportService(
    IOfferingRepository offerings,
    IInstructorRepository instructors,
    IRoomRepository rooms,
    IScheduleBlockConfigurationRepository blockConfigurations,
    IInstructorAvailabilityRepository instructorAvailability) : IScheduleImportService
{
    private sealed record ImportTable(string Name, List<string> Headers, List<Dictionary<string, string>> Rows);
    private sealed record CourseReference(int Row, string InstructorName, string RoomNumber);
    private sealed record AvailabilityRow(int Row, string Name, string Day, string TimeBlock);

    public async Task<ScheduleImportPreview> PreviewAsync(string semester, IReadOnlyList<IFormFile> files)
    {
        var warnings = new List<ScheduleImportIssue>();
        var errors = new List<ScheduleImportIssue>();
        var batch = new ScheduleImportBatch { Semester = semester };
        if (files.Count == 0)
        {
            errors.Add(new("Error", "File", 0, "Choose at least one CSV or Excel workbook."));
            return new(batch, warnings, errors, false);
        }

        var tables = new List<ImportTable>();
        foreach (var file in files)
        {
            if (file.Length > 20 * 1024 * 1024)
            {
                errors.Add(new("Error", file.FileName, 0, "Files must be 20 MB or smaller."));
                continue;
            }
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension == ".csv")
            {
                using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, true);
                tables.Add(ClassifyCsv(Path.GetFileNameWithoutExtension(file.FileName), await reader.ReadToEndAsync()));
            }
            else if (extension == ".xlsx") tables.AddRange(await ReadWorkbookAsync(file));
            else errors.Add(new("Error", file.FileName, 0, "Only .csv and .xlsx files are supported. Save older .xls workbooks as .xlsx first."));
        }

        if (tables.Count == 0)
        {
            if (errors.Count == 0) errors.Add(new("Error", "File", 0, "No supported data sheets were found."));
            return new(batch, warnings, errors, false);
        }

        var existingCourses = await offerings.GetBySemesterAsync(semester);
        var existingInstructors = await instructors.GetAllAsync();
        var existingRooms = await rooms.GetAllAsync();
        var blockConfiguration = await blockConfigurations.GetOrCreateAsync(semester);
        var existingCourseCodes = existingCourses.Select(x => Normalize(x.CourseCode)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var instructorByName = existingInstructors.GroupBy(x => Normalize(x.Name)).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var roomByNumber = existingRooms.GroupBy(x => Normalize(x.RoomNumber)).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var uploadedInstructorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uploadedRoomNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var schemaValid = true;
        var courseReferences = new List<CourseReference>();

        foreach (var table in tables.Where(x => x.Name == "Courses"))
        {
            if (!HasColumns(table, errors, "CourseCode", "CourseTitle", "Credits", "PredictedEnrollment")) schemaValid = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rowNumber = 1;
            foreach (var row in table.Rows)
            {
                rowNumber++;
                var code = Get(row, "CourseCode", "Code");
                var title = Get(row, "CourseTitle", "Title", "CourseName");
                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(title)) { errors.Add(new("Error", table.Name, rowNumber, "Course code and course title are required.")); continue; }
                if (!seen.Add(Normalize(code)) || existingCourseCodes.Contains(Normalize(code))) { errors.Add(new("Error", table.Name, rowNumber, $"Course code {code} is duplicated in this upload or already exists for {semester}.")); continue; }
                if (!TryInt(Get(row, "Credits"), out var credits) || credits <= 0 || !TryInt(Get(row, "PredictedEnrollment", "Enrollment"), out var predicted) || predicted < 0)
                { errors.Add(new("Error", table.Name, rowNumber, "Credits must be a positive whole number and predicted enrollment must be zero or greater.")); continue; }
                var historicalText = Get(row, "HistoricalEnrollment");
                if (!string.IsNullOrWhiteSpace(historicalText) && (!TryInt(historicalText, out var historical) || historical < 0)) { errors.Add(new("Error", table.Name, rowNumber, "Historical enrollment must be a whole number greater than or equal to zero.")); continue; }
                if (!TryBool(Get(row, "RequiresLab", "Laboratory"), out var requiresLab)) { errors.Add(new("Error", table.Name, rowNumber, "RequiresLab must be true/false, yes/no, or 1/0.")); continue; }
                batch.Courses.Add(new CourseOffering
                {
                CourseCode = code.Trim(), CourseTitle = title.Trim(), Semester = semester, Credits = credits,
                    HistoricalEnrollment = int.TryParse(historicalText, out var parsedHistorical) ? parsedHistorical : 0,
                    PredictedEnrollment = predicted, RequiresLab = requiresLab,
                    RequiredEquipment = SplitList(Get(row, "RequiredEquipment", "Equipment")),
                    CrossListedWith = NullIfEmpty(Get(row, "CrossListedWith")), CoLocationGroup = NullIfEmpty(Get(row, "CoLocationGroup"))
                });
                existingCourseCodes.Add(Normalize(code));
                courseReferences.Add(new(rowNumber, Get(row, "InstructorName", "Instructor"), Get(row, "RoomNumber", "Room")));
            }
        }

        foreach (var table in tables.Where(x => x.Name == "Instructors"))
        {
            if (!HasColumns(table, errors, "Name")) schemaValid = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rowNumber = 1;
            foreach (var row in table.Rows)
            {
                rowNumber++;
                var name = Get(row, "Name", "InstructorName");
                if (string.IsNullOrWhiteSpace(name)) { errors.Add(new("Error", table.Name, rowNumber, "Instructor name is required.")); continue; }
                if (!seen.Add(Normalize(name)) || !uploadedInstructorNames.Add(Normalize(name))) { errors.Add(new("Error", table.Name, rowNumber, $"Instructor {name} appears more than once in this upload.")); continue; }
                var maxText = Get(row, "MaximumCourseLoad", "MaxLoad");
                if (!string.IsNullOrWhiteSpace(maxText) && (!TryInt(maxText, out var maxLoad) || maxLoad < 0)) { errors.Add(new("Error", table.Name, rowNumber, "Maximum course load must be a whole number greater than or equal to zero.")); continue; }
                if (!TryBool(Get(row, "PrefersBackToBack"), out var backToBack)) { errors.Add(new("Error", table.Name, rowNumber, "PrefersBackToBack must be true/false, yes/no, or 1/0.")); continue; }
                var instructor = new Instructor
                {
                    Name = name.Trim(), QualifiedCourses = SplitList(Get(row, "QualifiedCourses")),
                    PreferredCourses = SplitList(Get(row, "PreferredCourses")),
                    MaximumCourseLoad = string.IsNullOrWhiteSpace(maxText) ? 2 : int.Parse(maxText, CultureInfo.InvariantCulture),
                    PrefersBackToBack = backToBack
                };
                if (instructorByName.TryGetValue(Normalize(name), out var existing)) instructor.Id = existing.Id;
                batch.Instructors.Add(instructor);
                instructorByName[Normalize(name)] = instructor;
            }
        }

        foreach (var table in tables.Where(x => x.Name == "Rooms"))
        {
            if (!HasColumns(table, errors, "RoomNumber", "Capacity", "RoomType")) schemaValid = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rowNumber = 1;
            foreach (var row in table.Rows)
            {
                rowNumber++;
                var number = Get(row, "RoomNumber", "Room");
                var type = Get(row, "RoomType", "Type");
                if (string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(type)) { errors.Add(new("Error", table.Name, rowNumber, "Room number and room type are required.")); continue; }
                if (!seen.Add(Normalize(number)) || !uploadedRoomNumbers.Add(Normalize(number))) { errors.Add(new("Error", table.Name, rowNumber, $"Room {number} appears more than once in this upload.")); continue; }
                if (!TryInt(Get(row, "Capacity"), out var capacity) || capacity <= 0) { errors.Add(new("Error", table.Name, rowNumber, "Room capacity must be a positive whole number.")); continue; }
                var room = new Room { RoomNumber = number.Trim(), Building = Get(row, "Building").Trim(), Capacity = capacity, RoomType = type.Trim(), Equipment = SplitList(Get(row, "Equipment")) };
                if (roomByNumber.TryGetValue(Normalize(number), out var existing)) { room.Id = existing.Id; room.AvailableBlocks = existing.AvailableBlocks; }
                batch.Rooms.Add(room);
                roomByNumber[Normalize(number)] = room;
            }
        }

        foreach (var reference in courseReferences)
        {
            if (!string.IsNullOrWhiteSpace(reference.InstructorName) && !instructorByName.ContainsKey(Normalize(reference.InstructorName)))
                warnings.Add(new("Warning", "Courses", reference.Row, $"Instructor '{reference.InstructorName}' is not present in the workspace or this upload."));
            if (!string.IsNullOrWhiteSpace(reference.RoomNumber) && !roomByNumber.ContainsKey(Normalize(reference.RoomNumber)))
                warnings.Add(new("Warning", "Courses", reference.Row, $"Room '{reference.RoomNumber}' is not present in the workspace or this upload."));
        }

        var instructorAvailabilityRows = new List<AvailabilityRow>();
        foreach (var table in tables.Where(x => x.Name == "InstructorAvailability"))
        {
            if (!HasColumns(table, errors, "InstructorName", "Day", "TimeBlock")) schemaValid = false;
            var rowNumber = 1;
            foreach (var row in table.Rows) { rowNumber++; instructorAvailabilityRows.Add(new(rowNumber, Get(row, "InstructorName", "Instructor", "Name"), Get(row, "Day"), Get(row, "TimeBlock", "Block", "Time"))); }
        }
        var availabilityByInstructor = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in instructorAvailabilityRows)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || !instructorByName.TryGetValue(Normalize(item.Name), out var instructor))
            { warnings.Add(new("Warning", "InstructorAvailability", item.Row, $"Unknown instructor '{item.Name}'. This availability row was skipped.")); continue; }
            if (!TryNormalizeBlock(item.Day, item.TimeBlock, blockConfiguration.Blocks, out var normalized, out var error)) { errors.Add(new("Error", "InstructorAvailability", item.Row, error)); continue; }
            if (!availabilityByInstructor.TryGetValue(instructor.Id, out var selected)) availabilityByInstructor[instructor.Id] = selected = new(StringComparer.OrdinalIgnoreCase);
            selected.Add(normalized);
        }
        foreach (var (instructorId, selected) in availabilityByInstructor)
            batch.InstructorAvailability.Add(new InstructorSemesterAvailability { InstructorId = instructorId, Semester = semester, AvailableBlocks = selected.ToList() });

        foreach (var table in tables.Where(x => x.Name == "RoomAvailability"))
        {
            if (!HasColumns(table, errors, "RoomNumber", "Day", "TimeBlock")) schemaValid = false;
            var rowNumber = 1;
            foreach (var row in table.Rows)
            {
                rowNumber++;
                var number = Get(row, "RoomNumber", "Room");
                if (string.IsNullOrWhiteSpace(number) || !roomByNumber.TryGetValue(Normalize(number), out var room))
                { warnings.Add(new("Warning", "RoomAvailability", rowNumber, $"Unknown room '{number}'. This availability row was skipped.")); continue; }
                if (!TryNormalizeBlock(Get(row, "Day"), Get(row, "TimeBlock", "Block", "Time"), blockConfiguration.Blocks, out var normalized, out var error) || normalized.StartsWith("Asynchronous ", StringComparison.OrdinalIgnoreCase))
                { errors.Add(new("Error", "RoomAvailability", rowNumber, error.Length > 0 ? error : "Rooms cannot have asynchronous availability.")); continue; }
                if (!batch.Rooms.Contains(room))
                {
                    if (existingRooms.Any(x => x.Id == room.Id)) batch.Rooms.Add(room);
                }
                if (!room.AvailableBlocks.Contains(normalized, StringComparer.OrdinalIgnoreCase)) room.AvailableBlocks.Add(normalized);
            }
        }

        if (!tables.Any(x => x.Name is "Courses" or "Instructors" or "Rooms" or "InstructorAvailability" or "RoomAvailability"))
            errors.Add(new("Error", "File", 0, "No recognized sheets were found. Use Courses, Instructors, Rooms, InstructorAvailability, and RoomAvailability."));
        var hasValidRecords = batch.Courses.Count + batch.Instructors.Count + batch.Rooms.Count + batch.InstructorAvailability.Count > 0;
        return new(batch, warnings, errors, schemaValid && hasValidRecords && !errors.Any(x => x.Row == 0 || x.Row == 1));
    }

    public async Task<ScheduleImportSummary> ConfirmAsync(ScheduleImportBatch batch)
    {
        var warnings = new List<ScheduleImportIssue>();
        var errors = new List<ScheduleImportIssue>();
        if (string.IsNullOrWhiteSpace(batch.Semester))
            return new(0, 0, 0, 0, warnings, [new("Error", "Import", 0, "Semester is required.")]);

        var existingCourses = await offerings.GetBySemesterAsync(batch.Semester);
        var knownCodes = existingCourses.Select(x => Normalize(x.CourseCode)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var coursesImported = 0;
        foreach (var course in batch.Courses)
        {
            if (string.IsNullOrWhiteSpace(course.CourseCode) || string.IsNullOrWhiteSpace(course.CourseTitle) || !knownCodes.Add(Normalize(course.CourseCode)))
            { errors.Add(new("Error", "Courses", 0, $"Course {course.CourseCode} is incomplete or already exists for {batch.Semester}.")); continue; }
            course.Semester = batch.Semester;
            await offerings.AddAsync(course);
            coursesImported++;
        }

        var existingInstructors = await instructors.GetAllAsync();
        var knownInstructors = existingInstructors.GroupBy(x => Normalize(x.Name)).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var instructorsImported = 0;
        foreach (var instructor in batch.Instructors)
        {
            if (string.IsNullOrWhiteSpace(instructor.Name)) { errors.Add(new("Error", "Instructors", 0, "An instructor without a name was not imported.")); continue; }
            if (knownInstructors.TryGetValue(Normalize(instructor.Name), out var current)) { instructor.Id = current.Id; await instructors.UpdateAsync(instructor); }
            else { await instructors.AddAsync(instructor); knownInstructors[Normalize(instructor.Name)] = instructor; }
            instructorsImported++;
        }

        var existingRooms = await rooms.GetAllAsync();
        var knownRooms = existingRooms.GroupBy(x => Normalize(x.RoomNumber)).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var roomsImported = 0;
        foreach (var room in batch.Rooms)
        {
            if (string.IsNullOrWhiteSpace(room.RoomNumber) || room.Capacity <= 0 || string.IsNullOrWhiteSpace(room.RoomType)) { errors.Add(new("Error", "Rooms", 0, $"Room {room.RoomNumber} is incomplete and was not imported.")); continue; }
            if (knownRooms.TryGetValue(Normalize(room.RoomNumber), out var current)) { room.Id = current.Id; await rooms.UpdateAsync(room); }
            else { await rooms.AddAsync(room); knownRooms[Normalize(room.RoomNumber)] = room; }
            roomsImported++;
        }

        var availableBlocks = (await blockConfigurations.GetOrCreateAsync(batch.Semester)).Blocks;
        var availabilitySaved = 0;
        foreach (var availability in batch.InstructorAvailability)
        {
            if (!knownInstructors.Values.Any(x => x.Id == availability.InstructorId))
            { errors.Add(new("Error", "InstructorAvailability", 0, $"Unknown instructor ID {availability.InstructorId}.")); continue; }
            if (availability.AvailableBlocks.Count == 0 || availability.AvailableBlocks.Any(selection => !IsApprovedAvailability(selection, availableBlocks)))
            { errors.Add(new("Error", "InstructorAvailability", 0, $"Availability for {availability.InstructorId} contains an empty or disabled block.")); continue; }
            availability.Semester = batch.Semester;
            await instructorAvailability.SaveAsync(availability);
            availabilitySaved++;
        }
        return new(coursesImported, instructorsImported, roomsImported, availabilitySaved, warnings, errors);
    }

    private static bool IsApprovedAvailability(string value, List<UniversityScheduleBlock> blocks)
    {
        if (!SchedulingAvailability.TryParse(value, out var day, out var time)) return false;
        var block = blocks.FirstOrDefault(x => x.Enabled && x.TimeBlock.Equals(time, StringComparison.OrdinalIgnoreCase));
        if (block is null || block.CourseTypes.Count == 0) return false;
        var async = block.Category.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase);
        return async == day.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeBlock(string day, string timeBlock, List<UniversityScheduleBlock> blocks, out string normalized, out string error)
    {
        normalized = "";
        error = "";
        var approved = blocks.FirstOrDefault(block => block.Enabled &&
            (block.TimeBlock.Equals(timeBlock.Trim(), StringComparison.OrdinalIgnoreCase) || block.Label.Equals(timeBlock.Trim(), StringComparison.OrdinalIgnoreCase)) && block.CourseTypes.Count > 0);
        if (approved is null) { error = $"'{timeBlock}' is not an enabled approved block for this semester."; return false; }
        var isAsync = approved.Category.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase);
        if (isAsync != day.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase)) { error = "Use the Asynchronous day only with the asynchronous block."; return false; }
        if (!isAsync && !new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" }.Contains(day, StringComparer.OrdinalIgnoreCase))
        { error = $"'{day}' is not an approved scheduling day."; return false; }
        var canonicalDay = isAsync ? "Asynchronous" : new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" }.First(x => x.Equals(day, StringComparison.OrdinalIgnoreCase));
        normalized = $"{canonicalDay} {approved.TimeBlock}";
        return true;
    }

    private static bool HasColumns(ImportTable table, List<ScheduleImportIssue> errors, params string[] required)
    {
        var headers = table.Headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = required.Where(column => !HeaderAliases(column).Any(headers.Contains)).ToList();
        if (missing.Count == 0) return true;
        errors.Add(new("Error", table.Name, 1, $"Missing required column(s): {string.Join(", ", missing)}."));
        return false;
    }

    private static IEnumerable<string> HeaderAliases(string header) => header switch
    {
        "CourseCode" => ["coursecode", "code"],
        "CourseTitle" => ["coursetitle", "title", "coursename"],
        "PredictedEnrollment" => ["predictedenrollment", "enrollment", "predictedstudents"],
        "InstructorName" => ["instructorname", "instructor", "name"],
        "RoomNumber" => ["roomnumber", "room"],
        "TimeBlock" => ["timeblock", "block", "time"],
        "RoomType" => ["roomtype", "type"],
        "MaximumCourseLoad" => ["maximumcourseload", "maxload"],
        _ => [Normalize(header)]
    };

    private static string Get(Dictionary<string, string> row, params string[] headers)
    {
        foreach (var header in headers)
            if (row.TryGetValue(Normalize(header), out var value)) return value.Trim();
        return "";
    }

    private static string Normalize(string value) => Regex.Replace(value ?? "", "[^A-Za-z0-9]", "").ToLowerInvariant();
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static List<string> SplitList(string value) => value.Split([';', '|', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static bool TryInt(string value, out int number) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    private static bool TryBool(string value, out bool result)
    {
        result = false;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "true" or "yes" or "1" or "y") { result = true; return true; }
        if (normalized is "false" or "no" or "0" or "n") return true;
        return false;
    }

    private static ImportTable ClassifyCsv(string fileName, string content)
    {
        var matrix = ParseCsv(content);
        if (matrix.Count == 0) return new(ClassifyName(fileName), [], []);
        var tableName = ClassifyName(fileName);
        if (tableName == "Unknown") tableName = ClassifyHeaders(matrix[0]);
        return MakeTable(tableName, matrix);
    }

    private static string ClassifyName(string name)
    {
        var normalized = Normalize(name);
        if (normalized.Contains("instructoravailability")) return "InstructorAvailability";
        if (normalized.Contains("roomavailability")) return "RoomAvailability";
        if (normalized.Contains("instructor")) return "Instructors";
        if (normalized.Contains("room")) return "Rooms";
        if (normalized.Contains("course")) return "Courses";
        return "Unknown";
    }

    private static string ClassifyHeaders(List<string> headers)
    {
        var normalized = headers.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasTime = normalized.Contains("timeblock") || normalized.Contains("time") || normalized.Contains("block");
        if ((normalized.Contains("instructorname") || normalized.Contains("instructor")) && normalized.Contains("day") && hasTime) return "InstructorAvailability";
        if ((normalized.Contains("roomnumber") || normalized.Contains("room")) && normalized.Contains("day") && hasTime) return "RoomAvailability";
        if (normalized.Contains("coursecode")) return "Courses";
        if (normalized.Contains("roomnumber") && normalized.Contains("capacity")) return "Rooms";
        if (normalized.Contains("name") && (normalized.Contains("qualifiedcourses") || normalized.Contains("maximumcourseload"))) return "Instructors";
        return "Unknown";
    }

    private static ImportTable MakeTable(string name, List<List<string>> matrix)
    {
        if (matrix.Count == 0) return new(name, [], []);
        var headers = matrix[0].Select(Normalize).ToList();
        var rows = new List<Dictionary<string, string>>();
        foreach (var cells in matrix.Skip(1))
        {
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++) row[headers[i]] = i < cells.Count ? cells[i].Trim() : "";
            rows.Add(row);
        }
        return new(name, headers, rows);
    }

    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var character = csv[i];
            if (character == '"')
            {
                if (quoted && i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted) { row.Add(cell.ToString()); cell.Clear(); }
            else if ((character == '\n' || character == '\r') && !quoted)
            {
                if (character == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                if (row.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row);
                row = [];
            }
            else cell.Append(character);
        }
        row.Add(cell.ToString());
        if (row.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row);
        return rows;
    }

    private static async Task<List<ImportTable>> ReadWorkbookAsync(IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        var workbookEntry = archive.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("The Excel file is missing its workbook definition.");
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("The Excel file is missing its sheet relationships.");
        var workbook = XDocument.Load(workbookEntry.Open());
        var workbookRelationships = XDocument.Load(relationshipsEntry.Open());
        var relationTargets = workbookRelationships.Root!.Elements(packageRelationships + "Relationship")
            .ToDictionary(element => (string)element.Attribute("Id")!, element => (string)element.Attribute("Target")!, StringComparer.OrdinalIgnoreCase);
        var sharedStrings = new List<string>();
        if (archive.GetEntry("xl/sharedStrings.xml") is { } sharedEntry)
        {
            var sharedDoc = XDocument.Load(sharedEntry.Open());
            sharedStrings = sharedDoc.Root!.Elements(spreadsheet + "si").Select(item => string.Concat(item.Descendants(spreadsheet + "t").Select(text => text.Value))).ToList();
        }
        var tables = new List<ImportTable>();
        foreach (var sheet in workbook.Root!.Element(spreadsheet + "sheets")!.Elements(spreadsheet + "sheet"))
        {
            var name = (string)sheet.Attribute("name")!;
            var relationId = (string)sheet.Attribute(relationships + "id")!;
            if (!relationTargets.TryGetValue(relationId, out var target)) continue;
            var path = target.StartsWith('/') ? target.TrimStart('/') : $"xl/{target}";
            path = path.Replace("xl/../", "", StringComparison.Ordinal);
            var entry = archive.GetEntry(path);
            if (entry is null) continue;
            var document = XDocument.Load(entry.Open());
            var matrix = new List<List<string>>();
            foreach (var row in document.Descendants(spreadsheet + "row"))
            {
                var cells = new SortedDictionary<int, string>();
                foreach (var cell in row.Elements(spreadsheet + "c"))
                {
                    var reference = (string?)cell.Attribute("r") ?? "A1";
                    var column = ColumnIndex(reference);
                    var type = (string?)cell.Attribute("t");
                    var value = type == "inlineStr"
                        ? string.Concat(cell.Descendants(spreadsheet + "t").Select(text => text.Value))
                        : cell.Element(spreadsheet + "v")?.Value ?? "";
                    if (type == "s" && int.TryParse(value, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count) value = sharedStrings[sharedIndex];
                    cells[column] = value;
                }
                if (cells.Count == 0) continue;
                var values = Enumerable.Repeat("", cells.Keys.Max() + 1).ToList();
                foreach (var (column, value) in cells) values[column] = value;
                matrix.Add(values);
            }
            var tableName = ClassifyName(name);
            if (tableName == "Unknown" && matrix.Count > 0) tableName = ClassifyHeaders(matrix[0]);
            if (tableName != "Unknown") tables.Add(MakeTable(tableName, matrix));
        }
        return tables;
    }

    private static int ColumnIndex(string reference)
    {
        var value = 0;
        foreach (var character in reference.TakeWhile(char.IsLetter)) value = value * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        return Math.Max(0, value - 1);
    }
}
