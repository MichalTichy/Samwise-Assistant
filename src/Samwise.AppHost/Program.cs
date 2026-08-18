var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("samwise-postgres-data");
var database = postgres.AddDatabase("samwise");

builder.AddProject<Projects.Samwise_Web>("samwise-web")
    .WithReference(database)
    .WaitFor(database)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
