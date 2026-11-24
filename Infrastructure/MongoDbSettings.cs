namespace ScheduleSpark.Api.Infrastructure;

public sealed class MongoDbSettings
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "schedulespark";
}
