using MongoDB.Driver;
using ScheduleSpark.Api.Models;

namespace ScheduleSpark.Api.Infrastructure;

public sealed class DatabaseSeeder(MongoDbContext db)
{
    public async Task SeedAsync()
    {
        await SeedCourseOfferingsAsync();

        await SeedInstructorsAsync();

        await SeedRoomsAsync();
        var existingConstraints = await db.Constraints.Find(FilterDefinition<SchedulingConstraint>.Empty).ToListAsync();
        var legacyConstraintNames = new[]
        {
            (Legacy: "No room overlap", Current: "Avoid room conflicts"),
            (Legacy: "Instructor preference", Current: "Preferred time blocks")
        };
        foreach (var (legacyName, currentName) in legacyConstraintNames)
        {
            var legacyConstraint = existingConstraints.FirstOrDefault(x => x.Name.Equals(legacyName, StringComparison.OrdinalIgnoreCase));
            var hasCurrentName = existingConstraints.Any(x => x.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));
            if (legacyConstraint is null || hasCurrentName) continue;
            legacyConstraint.Name = currentName;
            await db.Constraints.ReplaceOneAsync(x => x.Id == legacyConstraint.Id, legacyConstraint);
        }

        var defaultConstraints = new[]
            {
                new SchedulingConstraint { Name="Instructor qualification", Description="Assign instructors only to courses they are qualified to teach.", Type="Required", Priority=1, Source="Instructor profile" },
                new SchedulingConstraint { Name="Instructor availability", Description="Schedule instructors only during their available teaching blocks.", Type="Required", Priority=1, Source="Instructor profile" },
                new SchedulingConstraint { Name="Room capacity", Description="Room capacity must meet predicted enrollment.", Type="Required", Priority=1, Source="Department rule" },
                new SchedulingConstraint { Name="Required laboratory room", Description="Courses requiring a lab must be assigned to a laboratory room.", Type="Required", Priority=1, Source="Course requirement" },
                new SchedulingConstraint { Name="Required room equipment", Description="Assigned rooms must provide all equipment required by the course.", Type="Required", Priority=1, Source="Course requirement" },
                new SchedulingConstraint { Name="Instructor maximum load", Description="Do not assign an instructor more courses than their maximum course load.", Type="Required", Priority=1, Source="Instructor profile" },
                new SchedulingConstraint { Name="Avoid instructor time conflicts", Description="An instructor cannot teach more than one course in the same time block.", Type="Required", Priority=1, Source="Department rule" },
                new SchedulingConstraint { Name="Avoid room conflicts", Description="A room cannot be assigned to two courses at the same time.", Type="Required", Priority=1, Source="Department rule" },
                new SchedulingConstraint { Name="Room availability", Description="Schedule courses only during a room's available blocks.", Type="Required", Priority=1, Source="Room resource" },
                new SchedulingConstraint { Name="Preferred time blocks", Description="Prefer time blocks selected by instructors when possible.", Type="Preference", Priority=2, Source="Instructor preference" }
            };
        var existingConstraintNames = await db.Constraints.Find(FilterDefinition<SchedulingConstraint>.Empty).Project(x => x.Name).ToListAsync();
        var missingConstraints = defaultConstraints
            .Where(item => !existingConstraintNames.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (missingConstraints.Count > 0)
            await db.Constraints.InsertManyAsync(missingConstraints);
    }

    private async Task SeedCourseOfferingsAsync()
    {
        var courses = new List<CourseOffering>
    {
        // Undergraduate courses
        new()
        {
            CourseCode = "ICSI 201",
            CourseTitle = "Introduction to Programming",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 4,
            PredictedEnrollment = 80
        },
        new()
        {
            CourseCode = "ICSI 207",
            CourseTitle = "Object-Oriented Programming",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 4,
            PredictedEnrollment = 70
        },
        new()
        {
            CourseCode = "ICSI 210",
            CourseTitle = "Discrete Structures",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 4,
            PredictedEnrollment = 80
        },
        new()
        {
            CourseCode = "ICSI 213",
            CourseTitle = "Data Structures",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 4,
            PredictedEnrollment = 75
        },
        new()
        {
            CourseCode = "ICSI 235",
            CourseTitle = "AI for All",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 50
        },
        new()
        {
            CourseCode = "ICSI 300Z",
            CourseTitle = "Societal and Ethical Implications of Computing",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 60
        },
        new()
        {
            CourseCode = "ICSI 302",
            CourseTitle = "Systems I: C and Computer Organization",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 4,
            PredictedEnrollment = 50
        },
        new()
        {
            CourseCode = "ICSI 311",
            CourseTitle = "Principles of Programming Languages",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 4,
            PredictedEnrollment = 40
        },
        new()
        {
            CourseCode = "ICSI 333",
            CourseTitle = "System Fundamentals",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 4,
            PredictedEnrollment = 50
        },
        new()
        {
            CourseCode = "ICSI 401",
            CourseTitle = "Numerical Methods",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 30
        },
        new()
        {
            CourseCode = "ICSI 402",
            CourseTitle = "Systems II: Operating Systems and Networking",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 35
        },
        new()
        {
            CourseCode = "ICSI 403",
            CourseTitle = "Design and Analysis of Algorithms",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 45
        },
        new()
        {
            CourseCode = "ICSI 404",
            CourseTitle = "Computer Architecture and Organization",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 35
        },
        new()
        {
            CourseCode = "ICSI 407",
            CourseTitle = "Human Computer Interaction",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 30
        },
        new()
        {
            CourseCode = "ICSI 409",
            CourseTitle = "Automata and Formal Languages",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 35
        },
        new()
        {
            CourseCode = "ICSI 410",
            CourseTitle = "Database Systems",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 40
        },
        new()
        {
            CourseCode = "ICSI 412",
            CourseTitle = "Operating Systems",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 35
        },
        new()
        {
            CourseCode = "ICSI 416",
            CourseTitle = "Computer Communication Networks",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 35
        },
        new()
        {
            CourseCode = "ICSI 417",
            CourseTitle = "Compiler Construction",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 25
        },
        new()
        {
            CourseCode = "ICSI 418Y",
            CourseTitle = "Software Engineering",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 40
        },
        new()
        {
            CourseCode = "ICSI 424",
            CourseTitle = "Computer Security",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 35
        },
        new()
        {
            CourseCode = "ICSI 426",
            CourseTitle = "Cryptography",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 25
        },
        new()
        {
            CourseCode = "ICSI 431",
            CourseTitle = "Data Mining",
            ProgramLevel = "Undergraduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 30
        },

        // Graduate courses
        new()
        {
            CourseCode = "CSI 500",
            CourseTitle = "Operating Systems",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 30
        },
        new()
        {
            CourseCode = "CSI 501",
            CourseTitle = "Computational Linear Algebra, Nonlinear Algebra, and Optimization",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 25
        },
        new()
        {
            CourseCode = "CSI 502",
            CourseTitle = "Computer Graphics",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 25
        },
        new()
        {
            CourseCode = "CSI 503",
            CourseTitle = "Algorithms and Data Structures",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 35
        },
        new()
        {
            CourseCode = "CSI 508",
            CourseTitle = "Database Systems I",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 30
        },
        new()
        {
            CourseCode = "CSI 509",
            CourseTitle = "Automata and Theory of Computation",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 25
        },
        new()
        {
            CourseCode = "CSI 516",
            CourseTitle = "Computer Communications Networks",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 30,
            CrossListedWith = "ICSI 416"
        },
        new()
        {
            CourseCode = "CSI 518",
            CourseTitle = "Software Engineering",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 4,
            PredictedEnrollment = 30
        },
        new()
        {
            CourseCode = "CSI 521",
            CourseTitle = "Discrete Mathematics with Applications",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 25
        },
        new()
        {
            CourseCode = "CSI 524",
            CourseTitle = "Computer Security",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 30
        },
        new()
        {
            CourseCode = "CSI 525",
            CourseTitle = "Mobile Wireless Networks",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 25
        },
        new()
        {
            CourseCode = "CSI 526",
            CourseTitle = "Cryptography",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 25
        },
        new()
        {
            CourseCode = "CSI 529",
            CourseTitle = "Probability and Computing",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 25
        },
        new()
        {
            CourseCode = "CSI 531",
            CourseTitle = "Data Mining",
            ProgramLevel = "Graduate",
            Semester = "Fall 2026",
            Credits = 3,
            PredictedEnrollment = 25
        }
    };

        foreach (var course in courses)
        {
            var existing = await db.Offerings
                .Find(x =>
                    x.CourseCode == course.CourseCode &&
                    x.Semester == course.Semester)
                .FirstOrDefaultAsync();

            if (existing is null)
            {
                await db.Offerings.InsertOneAsync(course);
            }
        }
    }

    private async Task SeedInstructorsAsync()
    {
        var instructors = new List<Instructor>
    {
        new()
        {
            Name = "Jeff Offutt",
            QualifiedCourses =
            [
                "ICSI 418Y",
                "CSI 518",
                "ICSI 407"
            ],
            PreferredCourses =
            [
                "ICSI 418Y",
                "CSI 518"
            ],
            AvailableBlocks =
            [
                "Monday 09:20-10:10",
                "Wednesday 09:20-10:10",
                "Thursday 15:00-16:20"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = false
        },

        new()
        {
            Name = "Maryam Arab",
            QualifiedCourses =
            [
                "CSI 503",
                "CSI 521",
                "CSI 529"
            ],
            PreferredCourses =
            [
                "CSI 503",
                "CSI 529"
            ],
            AvailableBlocks =
            [
                "Monday 10:10-11:30",
                "Tuesday 13:30-14:50",
                "Thursday 10:10-11:30"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = false
        },

        new()
        {
            Name = "Shashank Arora",
            QualifiedCourses =
            [
                "ICSI 424",
                "ICSI 426",
                "CSI 524",
                "CSI 526"
            ],
            PreferredCourses =
            [
                "ICSI 424",
                "CSI 524"
            ],
            AvailableBlocks =
            [
                "Tuesday 09:20-10:10",
                "Wednesday 13:30-14:50",
                "Thursday 15:00-16:20"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = true
        },

        new()
        {
            Name = "Pradeep K. Atrey",
            QualifiedCourses =
            [
                "CSI 531",
                "CSI 532",
                "ICSI 431"
            ],
            PreferredCourses =
            [
                "CSI 531",
                "ICSI 431"
            ],
            AvailableBlocks =
            [
                "Monday 13:30-14:50",
                "Wednesday 15:00-16:20",
                "Friday 09:20-10:10"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = false
        },

        new()
        {
            Name = "Petko Bogdanov",
            QualifiedCourses =
            [
                "CSI 531",
                "CSI 529",
                "ICSI 431",
                "ICSI 403"
            ],
            PreferredCourses =
            [
                "CSI 531",
                "ICSI 403"
            ],
            AvailableBlocks =
            [
                "Monday 10:10-11:30",
                "Tuesday 15:00-16:20",
                "Thursday 13:30-14:50"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = false
        },

        new()
        {
            Name = "Ming-Ching Chang",
            QualifiedCourses =
            [
                "CSI 502",
                "CSI 531",
                "ICSI 422",
                "ICSI 431"
            ],
            PreferredCourses =
            [
                "CSI 502",
                "ICSI 422"
            ],
            AvailableBlocks =
            [
                "Tuesday 10:10-11:30",
                "Wednesday 15:00-16:20",
                "Thursday 09:20-10:10"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = true
        },

        new()
        {
            Name = "Jeong-Hyon Hwang",
            QualifiedCourses =
            [
                "CSI 508",
                "CSI 516",
                "CSI 520",
                "ICSI 410",
                "ICSI 416"
            ],
            PreferredCourses =
            [
                "CSI 508",
                "CSI 516",
                "ICSI 410"
            ],
            AvailableBlocks =
            [
                "Monday 15:00-16:20",
                "Wednesday 10:10-11:30",
                "Thursday 13:30-14:50"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = false
        },

        new()
        {
            Name = "Xin Li",
            QualifiedCourses =
            [
                "CSI 502",
                "CSI 531",
                "CSI 532",
                "ICSI 422",
                "ICSI 431"
            ],
            PreferredCourses =
            [
                "CSI 531",
                "ICSI 431"
            ],
            AvailableBlocks =
            [
                "Monday 13:30-14:50",
                "Tuesday 09:20-10:10",
                "Friday 10:10-11:30"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = false
        },

        new()
        {
            Name = "Sherry Sahebi",
            QualifiedCourses =
            [
                "CSI 531",
                "CSI 532",
                "ICSI 431",
                "ICSI 407"
            ],
            PreferredCourses =
            [
                "CSI 531",
                "ICSI 407"
            ],
            AvailableBlocks =
            [
                "Tuesday 13:30-14:50",
                "Wednesday 09:20-10:10",
                "Thursday 15:00-16:20"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = true
        },

        new()
        {
            Name = "Vladimir Kuperman",
            QualifiedCourses =
            [
                "ICSI 402",
                "ICSI 416",
                "CSI 516",
                "CSI 525"
            ],
            PreferredCourses =
            [
                "ICSI 402",
                "ICSI 416"
            ],
            AvailableBlocks =
            [
                "Monday 10:10-11:30",
                "Wednesday 13:30-14:50",
                "Thursday 09:20-10:10"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = true
        },

        new()
        {
            Name = "Michael Phipps",
            QualifiedCourses =
            [
                "ICSI 311",
                "ICSI 417",
                "ICSI 404",
                "ICSI 412"
            ],
            PreferredCourses =
            [
                "ICSI 311",
                "ICSI 417"
            ],
            AvailableBlocks =
            [
                "Tuesday 10:10-11:30",
                "Wednesday 15:00-16:20",
                "Friday 09:20-10:10"
            ],
            MaximumCourseLoad = 3,
            PrefersBackToBack = true
        },

        new()
        {
            Name = "Abram Magner",
            QualifiedCourses =
            [
                "CSI 503",
                "CSI 521",
                "CSI 529",
                "CSI 531"
            ],
            PreferredCourses =
            [
                "CSI 503",
                "CSI 521"
            ],
            AvailableBlocks =
            [
                "Monday 09:20-10:10",
                "Tuesday 13:30-14:50",
                "Thursday 10:10-11:30"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = false
        },

        new()
        {
            Name = "Paliath Narendran",
            QualifiedCourses =
            [
                "CSI 509",
                "CSI 517",
                "ICSI 409",
                "ICSI 417"
            ],
            PreferredCourses =
            [
                "CSI 509",
                "CSI 517"
            ],
            AvailableBlocks =
            [
                "Monday 15:00-16:20",
                "Wednesday 10:10-11:30",
                "Friday 13:30-14:50"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = false
        },

        new()
        {
            Name = "Mariya Zheleva",
            QualifiedCourses =
            [
                "CSI 516",
                "CSI 525",
                "ICSI 416"
            ],
            PreferredCourses =
            [
                "CSI 516",
                "CSI 525"
            ],
            AvailableBlocks =
            [
                "Tuesday 09:20-10:10",
                "Thursday 13:30-14:50",
                "Friday 15:00-16:20"
            ],
            MaximumCourseLoad = 2,
            PrefersBackToBack = false
        }
    };

        foreach (var instructor in instructors)
        {
            var existing = await db.Instructors
                .Find(x => x.Name == instructor.Name)
                .FirstOrDefaultAsync();

            if (existing is null)
            {
                await db.Instructors.InsertOneAsync(instructor);
            }
            else
            {
                existing.QualifiedCourses = instructor.QualifiedCourses;
                existing.PreferredCourses = instructor.PreferredCourses;
                existing.AvailableBlocks = instructor.AvailableBlocks;
                existing.MaximumCourseLoad = instructor.MaximumCourseLoad;
                existing.PrefersBackToBack = instructor.PrefersBackToBack;

                await db.Instructors.ReplaceOneAsync(
                    x => x.Id == existing.Id,
                    existing);
            }
        }
    }

    private async Task SeedRoomsAsync()
    {
        var rooms = new List<Room>
    {
        new()
        {
            RoomNumber = "BI0248",
            Building = "Biology Building",
            Capacity = 100,
            RoomType = "Lecture",
            Equipment =
            [
                "Instructor PC",
                "Projector",
                "Internet",
                "Whiteboard"
            ],
            AvailableBlocks = DefaultRoomBlocks(),
        },

        new()
        {
            RoomNumber = "TA0122",
            Building = "Taconic Building",
            Capacity = 80,
            RoomType = "Lecture",
            Equipment =
            [
                "Instructor PC",
                "Projector",
                "Internet",
                "Whiteboard"
            ],
            AvailableBlocks = DefaultRoomBlocks(),
        },

        new()
        {
            RoomNumber = "HD0110",
            Building = "Hudson Building",
            Capacity = 30,
            RoomType = "Lab",
            Equipment =
            [
                "Computers",
                "Projector",
                "Internet",
                "Whiteboard"
            ],
            AvailableBlocks = LabRoomBlocks(),
        },

        new()
        {
            RoomNumber = "CKB059",
            Building = "Campus Classroom Building",
            Capacity = 30,
            RoomType = "Lab",
            Equipment =
            [
                "Computers",
                "Projector",
                "Internet"
            ],
            AvailableBlocks = LabRoomBlocks(),
        },

        new()
        {
            RoomNumber = "HU0111",
            Building = "Husted Hall",
            Capacity = 30,
            RoomType = "Lab",
            Equipment =
            [
                "Computers",
                "Projector",
                "Internet",
                "Whiteboard"
            ],
            AvailableBlocks = LabRoomBlocks(),
        },

        new()
        {
            RoomNumber = "HU0019",
            Building = "Husted Hall",
            Capacity = 30,
            RoomType = "Lab",
            Equipment =
            [
                "Computers",
                "Projector",
                "Internet"
            ],
            AvailableBlocks = LabRoomBlocks(),
        },

        new()
        {
            RoomNumber = "UAB400",
            Building = "University Art Building",
            Capacity = 40,
            RoomType = "Lecture",
            Equipment =
            [
                "Instructor PC",
                "Projector",
                "Internet",
                "Whiteboard"
            ],
            AvailableBlocks = DefaultRoomBlocks(),
        },

        new()
        {
            RoomNumber = "UAB420",
            Building = "University Art Building",
            Capacity = 35,
            RoomType = "Lecture",
            Equipment =
            [
                "Instructor PC",
                "Projector",
                "Internet"
            ],
            AvailableBlocks = DefaultRoomBlocks(),
        },

        new()
        {
            RoomNumber = "UAB424A",
            Building = "University Art Building",
            Capacity = 25,
            RoomType = "Lab",
            Equipment =
            [
                "Computers",
                "Projector",
                "Internet"
            ],
            AvailableBlocks = LabRoomBlocks(),
        },

        new()
        {
            RoomNumber = "ETEC170C",
            Building = "ETEC",
            Capacity = 40,
            RoomType = "Lab",
            Equipment =
            [
                "Computers",
                "Projector",
                "Internet",
                "Network equipment"
            ],
            AvailableBlocks = LabRoomBlocks(),
        }
    };

        foreach (var room in rooms)
        {
            var existing = await db.Rooms
                .Find(x => x.RoomNumber == room.RoomNumber)
                .FirstOrDefaultAsync();

            if (existing is null)
            {
                await db.Rooms.InsertOneAsync(room);
            }
            else
            {
                existing.Building = room.Building;
                existing.Capacity = room.Capacity;
                existing.RoomType = room.RoomType;
                existing.Equipment = room.Equipment;
                existing.AvailableBlocks = room.AvailableBlocks;

                await db.Rooms.ReplaceOneAsync(
                    x => x.Id == existing.Id,
                    existing);
            }
        }
    }
    private static List<string> DefaultRoomBlocks()
    {
        return
        [
            "Monday 08:00-09:20",
        "Monday 09:20-10:10",
        "Monday 10:10-11:30",
        "Monday 13:30-14:50",
        "Tuesday 08:00-09:20",
        "Tuesday 09:20-10:10",
        "Tuesday 10:10-11:30",
        "Tuesday 13:30-14:50",
        "Wednesday 08:00-09:20",
        "Wednesday 09:20-10:10",
        "Wednesday 10:10-11:30",
        "Wednesday 13:30-14:50",
        "Thursday 08:00-09:20",
        "Thursday 09:20-10:10",
        "Thursday 10:10-11:30",
        "Thursday 13:30-14:50",
        "Friday 08:00-09:20",
        "Friday 09:20-10:10",
        "Friday 10:10-11:30"
        ];
    }

    private static List<string> LabRoomBlocks()
    {
        return
        [
            "Monday 09:20-10:10",
        "Monday 10:10-11:30",
        "Tuesday 09:20-10:10",
        "Tuesday 10:10-11:30",
        "Wednesday 09:20-10:10",
        "Wednesday 10:10-11:30",
        "Thursday 09:20-10:10",
        "Thursday 10:10-11:30",
        "Friday 09:20-10:10",
        "Friday 10:10-11:30"
        ];
    }
}
