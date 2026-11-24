using Microsoft.Extensions.Options;
using MongoDB.Driver;
using ScheduleSpark.Api.Models;

namespace ScheduleSpark.Api.Infrastructure;

public sealed class MongoDbContext
{
    private readonly IMongoDatabase _database;
    public MongoDbContext(IMongoClient client, IOptions<MongoDbSettings> options)
        => _database = client.GetDatabase(options.Value.DatabaseName);

    public IMongoCollection<CourseOffering> Offerings => _database.GetCollection<CourseOffering>("offerings");
    public IMongoCollection<Instructor> Instructors => _database.GetCollection<Instructor>("instructors");
    public IMongoCollection<Room> Rooms => _database.GetCollection<Room>("rooms");
    public IMongoCollection<SchedulingConstraint> Constraints => _database.GetCollection<SchedulingConstraint>("constraints");
    public IMongoCollection<ScheduleDraft> Drafts => _database.GetCollection<ScheduleDraft>("drafts");
}
