using MongoDB.Driver;
using ScheduleSpark.Api.Infrastructure;
using ScheduleSpark.Api.Models;

namespace ScheduleSpark.Api.Repositories;

public interface IOfferingRepository { Task<List<CourseOffering>> GetBySemesterAsync(string semester); Task<CourseOffering?> GetAsync(string id); Task AddAsync(CourseOffering item); }
public interface IInstructorRepository { Task<List<Instructor>> GetAllAsync(); Task<Instructor?> GetAsync(string id); }
public interface IRoomRepository { Task<List<Room>> GetAllAsync(); Task<Room?> GetAsync(string id); }
public interface IConstraintRepository { Task<List<SchedulingConstraint>> GetEnabledAsync(); }
public interface IDraftRepository { Task<List<ScheduleDraft>> GetAllAsync(); Task<ScheduleDraft?> GetAsync(string id); Task AddAsync(ScheduleDraft draft); Task UpdateAsync(ScheduleDraft draft); }

public sealed class OfferingRepository(MongoDbContext db) : IOfferingRepository
{
    public Task<List<CourseOffering>> GetBySemesterAsync(string semester) => db.Offerings.Find(x => x.Semester == semester).ToListAsync();
    public Task<CourseOffering?> GetAsync(string id) => db.Offerings.Find(x => x.Id == id).FirstOrDefaultAsync();
    public Task AddAsync(CourseOffering item) => db.Offerings.InsertOneAsync(item);
}
public sealed class InstructorRepository(MongoDbContext db) : IInstructorRepository
{
    public Task<List<Instructor>> GetAllAsync() => db.Instructors.Find(_ => true).ToListAsync();
    public Task<Instructor?> GetAsync(string id) => db.Instructors.Find(x => x.Id == id).FirstOrDefaultAsync();
}
public sealed class RoomRepository(MongoDbContext db) : IRoomRepository
{
    public Task<List<Room>> GetAllAsync() => db.Rooms.Find(_ => true).ToListAsync();
    public Task<Room?> GetAsync(string id) => db.Rooms.Find(x => x.Id == id).FirstOrDefaultAsync();
}
public sealed class ConstraintRepository(MongoDbContext db) : IConstraintRepository
{
    public Task<List<SchedulingConstraint>> GetEnabledAsync() => db.Constraints.Find(x => x.Enabled).SortBy(x => x.Priority).ToListAsync();
}
public sealed class DraftRepository(MongoDbContext db) : IDraftRepository
{
    public Task<List<ScheduleDraft>> GetAllAsync() => db.Drafts.Find(_ => true).SortByDescending(x => x.CreatedAtUtc).ToListAsync();
    public Task<ScheduleDraft?> GetAsync(string id) => db.Drafts.Find(x => x.Id == id).FirstOrDefaultAsync();
    public Task AddAsync(ScheduleDraft draft) => db.Drafts.InsertOneAsync(draft);
    public Task UpdateAsync(ScheduleDraft draft) => db.Drafts.ReplaceOneAsync(x => x.Id == draft.Id, draft);
}
