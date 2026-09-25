using System.Text.Json;
using PrintPlanner.Api.Planning;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/plan", (Func<HttpContext, Task<IResult>>)(http => HandlePlan(http, svg: false)));
app.MapPost("/api/plan/svg", (Func<HttpContext, Task<IResult>>)(http => HandlePlan(http, svg: true)));

static async Task<IResult> HandlePlan(HttpContext http, bool svg)
{
    PlanRequest? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<PlanRequest>(http.Request.Body, Program.JsonOptions);
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { errors = new[] { new FieldError { Field = "body", Message = $"Invalid JSON: {ex.Message}" } } });
    }

    var errors = PlanValidator.Validate(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new { errors });
    }

    var plan = Planner.Plan(request!);
    if (svg)
    {
        return Results.Content(SvgRenderer.Render(request!, plan), "image/svg+xml");
    }
    return Results.Ok(plan);
}

app.Run();

public partial class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
