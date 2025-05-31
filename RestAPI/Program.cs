using Gremlin.Net.Driver.Remote;
using Gremlin.Net.Driver;
using Gremlin.Net.Process.Traversal;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddLogging();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSingleton<GremlinClient>(
        (serviceProvider) =>
        {
            
            var gremlinServer = new GremlinServer(
                hostname: $"{accountName}.gremlin.cosmos.azure.com",
                port: 443,
                username: "/dbs/DCRgraph/colls/events",
                password: $"{accountKey}",
                enableSsl: true
            );
            return new GremlinClient(
                gremlinServer: gremlinServer,
                messageSerializer: new Gremlin.Net.Structure.IO.GraphSON.GraphSON2MessageSerializer()
            );
        }
    );

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
