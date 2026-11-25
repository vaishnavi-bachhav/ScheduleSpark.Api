using MongoDB.Driver;
using ScheduleSpark.Api.Models;

namespace ScheduleSpark.Api.Infrastructure;

public sealed class DatabaseSeeder(MongoDbContext db)
{
    public async Task SeedAsync()
    {
        if (await db.Offerings.CountDocumentsAsync(FilterDefinition<CourseOffering>.Empty) == 0)
            await db.Offerings.InsertManyAsync([
                new CourseOffering { CourseCode="CSI 402", CourseTitle="Operating Systems", Semester="Fall 2026", Credits=3, HistoricalEnrollment=28, PredictedEnrollment=30 },
                new CourseOffering { CourseCode="CSI 410", CourseTitle="Database Systems", Semester="Fall 2026", Credits=3, HistoricalEnrollment=32, PredictedEnrollment=35 },
                new CourseOffering { CourseCode="CSI 435", CourseTitle="Computer Networks", Semester="Fall 2026", Credits=3, HistoricalEnrollment=24, PredictedEnrollment=25 },
                new CourseOffering { CourseCode="CSI 450", CourseTitle="Software Engineering", Semester="Fall 2026", Credits=3, HistoricalEnrollment=38, PredictedEnrollment=40 },
                new CourseOffering { CourseCode="CSI 472", CourseTitle="Artificial Intelligence Lab", Semester="Fall 2026", Credits=3, HistoricalEnrollment=18, PredictedEnrollment=20, RequiresLab=true }
            ]);
        if (await db.Instructors.CountDocumentsAsync(FilterDefinition<Instructor>.Empty) == 0)
            await db.Instructors.InsertManyAsync([
                new Instructor { Name="Dr. Priya Shah", QualifiedCourses=["CSI 402", "CSI 410"], PreferredCourses=["CSI 410"], AvailableBlocks=["Monday 09:00-10:15", "Wednesday 09:00-10:15"], MaximumCourseLoad=2, PrefersBackToBack=true },
                new Instructor { Name="Dr. Michael Chen", QualifiedCourses=["CSI 435", "CSI 472"], PreferredCourses=["CSI 435"], AvailableBlocks=["Tuesday 10:30-11:45", "Thursday 10:30-11:45"], MaximumCourseLoad=2 },
                new Instructor { Name="Dr. Elena Garcia", QualifiedCourses=["CSI 450", "CSI 472"], PreferredCourses=["CSI 450"], AvailableBlocks=["Wednesday 13:00-14:15", "Thursday 15:00-16:15"], MaximumCourseLoad=2 }
            ]);
        if (await db.Rooms.CountDocumentsAsync(FilterDefinition<Room>.Empty) == 0)
            await db.Rooms.InsertManyAsync([
                new Room { RoomNumber="D-101", Building="Draper", Capacity=40, RoomType="Lecture", Equipment=["Projector", "Whiteboard"], AvailableBlocks=["Monday 09:00-10:15", "Tuesday 10:30-11:45", "Wednesday 13:00-14:15"] },
                new Room { RoomNumber="D-202", Building="Draper", Capacity=50, RoomType="Lecture", Equipment=["Projector", "Computer"], AvailableBlocks=["Monday 09:00-10:15", "Thursday 15:00-16:15"] },
                new Room { RoomNumber="LAB-1", Building="Draper", Capacity=25, RoomType="Lab", Equipment=["Computers", "Network equipment"], AvailableBlocks=["Tuesday 10:30-11:45", "Thursday 10:30-11:45"] }
            ]);
        var defaultConstraints = new[]
            {
                new SchedulingConstraint { Name="Instructor availability", Description="Schedule instructors only during their available teaching blocks.", Type="Required", Priority=1, Source="Instructor profile" },
                new SchedulingConstraint { Name="Room capacity", Description="Room capacity must meet predicted enrollment.", Type="Required", Priority=1, Source="Department rule" },
                new SchedulingConstraint { Name="Required laboratory room", Description="Courses requiring a lab must be assigned to a laboratory room.", Type="Required", Priority=1, Source="Course requirement" },
                new SchedulingConstraint { Name="Instructor maximum load", Description="Do not assign an instructor more courses than their maximum course load.", Type="Required", Priority=1, Source="Instructor profile" },
                new SchedulingConstraint { Name="Avoid instructor time conflicts", Description="An instructor cannot teach more than one course in the same time block.", Type="Required", Priority=1, Source="Department rule" },
                new SchedulingConstraint { Name="Avoid room conflicts", Description="A room cannot be assigned to two courses at the same time.", Type="Required", Priority=1, Source="Department rule" },
                new SchedulingConstraint { Name="Preferred time blocks", Description="Prefer time blocks selected by instructors when possible.", Type="Preference", Priority=2, Source="Instructor preference" }
            };
        var existingConstraintNames = await db.Constraints.Find(FilterDefinition<SchedulingConstraint>.Empty)
            .Project(x => x.Name)
            .ToListAsync();
        var missingConstraints = defaultConstraints
            .Where(item => !existingConstraintNames.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (missingConstraints.Count > 0)
            await db.Constraints.InsertManyAsync(missingConstraints);
    }
}
