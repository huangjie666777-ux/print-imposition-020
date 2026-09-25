using System.Text.Json;
using PrintPlanner.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/plans", (PlanningRequest? request) => Handle(request, svg: false));
app.MapPost("/api/plans/preview", (PlanningRequest? request) => Handle(request, svg: true));

app.Run();

static IResult Handle(PlanningRequest? request, bool svg)
{
    if (request is null)
        return Results.BadRequest(new ValidationErrorResponse(new()
        {
            new ValidationError(string.Empty, "request body must be a valid planning JSON object"),
        }));

    var (plan, errors) = PlannerEngine.Run(request);
    if (plan is null)
        return Results.BadRequest(new ValidationErrorResponse(errors));

    return svg
        ? Results.Text(SvgGenerator.Generate(request, plan), "image/svg+xml", System.Text.Encoding.UTF8)
        : Results.Ok(plan);
}

public partial class Program { }
