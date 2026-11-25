using MongoDB.Driver;
using ScheduleSpark.Api.Infrastructure;
using ScheduleSpark.Api.Models;

namespace ScheduleSpark.Api.Repositories;

public interface IOfferingRepository { Task<List<CourseOffering>> GetBySemesterAsync(string semester); Task<CourseOffering?> GetAsync(string id); Task AddAsync(CourseOffering item); }
public interface IInstructorRepository { Task<List<Instructor>> GetAllAsync(); Task<Instructor?> GetAsync(string id); Task AddAsync(Instructor item); Task UpdateAsync(Instructor item); Task DeleteAsync(string id); }
public interface IRoomRepository { Task<List<Room>> GetAllAsync(); Task<Room?> GetAsync(string id); Task AddAsync(Room item); Task UpdateAsync(Room item); Task DeleteAsync(string id); }
public interface IConstraintRepository { Task<List<SchedulingConstraint>> GetAllAsync(); Task<List<SchedulingConstraint>> GetEnabledAsync(); Task AddAsync(SchedulingConstraint item); Task UpdateAsync(SchedulingConstraint item); Task DeleteAsync(string id); }
public interface IDraftRepository { Task<List<ScheduleDraft>> GetAllAsync(); Task<ScheduleDraft?> GetAsync(string id); Task AddAsync(ScheduleDraft draft); Task UpdateAsync(ScheduleDraft draft); Task DeleteAsync(string id); }
public interface IDraftVersionRepository { Task<List<ScheduleDraftVersion>> GetByDraftIdAsync(string draftId); Task<ScheduleDraftVersion?> GetAsync(string draftId, int versionNumber); Task AddAsync(ScheduleDraftVersion version); Task DeleteByDraftIdAsync(string draftId); }

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
    public Task AddAsync(Instructor item) => db.Instructors.InsertOneAsync(item);
    public Task UpdateAsync(Instructor item) => db.Instructors.ReplaceOneAsync(x => x.Id == item.Id, item);
    public Task DeleteAsync(string id) => db.Instructors.DeleteOneAsync(x => x.Id == id);
}
public sealed class RoomRepository(MongoDbContext db) : IRoomRepository
{
    public Task<List<Room>> GetAllAsync() => db.Rooms.Find(_ => true).ToListAsync();
    public Task<Room?> GetAsync(string id) => db.Rooms.Find(x => x.Id == id).FirstOrDefaultAsync();
    public Task AddAsync(Room item) => db.Rooms.InsertOneAsync(item);
    public Task UpdateAsync(Room item) => db.Rooms.ReplaceOneAsync(x => x.Id == item.Id, item);
    public Task DeleteAsync(string id) => db.Rooms.DeleteOneAsync(x => x.Id == id);
}
public sealed class ConstraintRepository(MongoDbContext db) : IConstraintRepository
{
    public Task<List<SchedulingConstraint>> GetAllAsync() => db.Constraints.Find(_ => true).SortBy(x => x.Priority).ToListAsync();
    public Task<List<SchedulingConstraint>> GetEnabledAsync() => db.Constraints.Find(x => x.Enabled).SortBy(x => x.Priority).ToListAsync();
    public Task AddAsync(SchedulingConstraint item) => db.Constraints.InsertOneAsync(item);
    public Task UpdateAsync(SchedulingConstraint item) => db.Constraints.ReplaceOneAsync(x => x.Id == item.Id, item);
    public Task DeleteAsync(string id) => db.Constraints.DeleteOneAsync(x => x.Id == id);
}
public sealed class DraftRepository(MongoDbContext db) : IDraftRepository
{
    public Task<List<ScheduleDraft>> GetAllAsync() => db.Drafts.Find(_ => true).SortByDescending(x => x.CreatedAtUtc).ToListAsync();
    public Task<ScheduleDraft?> GetAsync(string id) => db.Drafts.Find(x => x.Id == id).FirstOrDefaultAsync();
    public Task AddAsync(ScheduleDraft draft) => db.Drafts.InsertOneAsync(draft);
    public Task UpdateAsync(ScheduleDraft draft) => db.Drafts.ReplaceOneAsync(x => x.Id == draft.Id, draft);
    public Task DeleteAsync(string id) => db.Drafts.DeleteOneAsync(x => x.Id == id);
}
public sealed class DraftVersionRepository(MongoDbContext db) : IDraftVersionRepository
{
    public Task<List<ScheduleDraftVersion>> GetByDraftIdAsync(string draftId) => db.DraftVersions.Find(x => x.DraftId == draftId).SortByDescending(x => x.VersionNumber).ToListAsync();
    public Task<ScheduleDraftVersion?> GetAsync(string draftId, int versionNumber) => db.DraftVersions.Find(x => x.DraftId == draftId && x.VersionNumber == versionNumber).FirstOrDefaultAsync();
    public Task AddAsync(ScheduleDraftVersion version) => db.DraftVersions.InsertOneAsync(version);
    public Task DeleteByDraftIdAsync(string draftId) => db.DraftVersions.DeleteManyAsync(x => x.DraftId == draftId);
}
