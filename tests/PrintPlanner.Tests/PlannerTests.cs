using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PrintPlanner.Api;
using Xunit;

public sealed class PlannerTests
{
    private static PlanningRequest Sample() => new()
    {
        PaperWidth = 100,
        PaperHeight = 80,
        MarginTop = 5,
        MarginRight = 5,
        MarginBottom = 5,
        MarginLeft = 5,
        Gap = 2,
        Bleed = 1,
        MaxSheets = 2,
        Labels = new()
        {
            new LabelSpec { Id = "a", Width = 40, Height = 30, Quantity = 4, AllowRotation = false },
            new LabelSpec { Id = "big", Width = 200, Height = 10, Quantity = 1, AllowRotation = true },
        },
    };

    [Fact]
    public async Task PlansLabelsAndReportsUnplacedReasons()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/plans", Sample());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = await response.Content.ReadFromJsonAsync<PlanningResponse>();
        Assert.NotNull(plan);
        Assert.Equal(1, plan!.SheetsUsed);
        Assert.Equal(4, plan.Sheets.Sum(s => s.Instances.Count));
        var big = Assert.Single(plan.Unplaced);
        Assert.Equal("big", big.SourceId);
        Assert.StartsWith("too_large", big.Reason);
    }

    [Fact]
    public void LayoutRespectsBleedMarginsAndGapsExactly()
    {
        var req = Sample();
        var (plan, errors) = PlannerEngine.Run(req);
        Assert.Empty(errors);
        Assert.NotNull(plan);

        foreach (var sheet in plan!.Sheets)
        {
            foreach (var p in sheet.Instances)
            {
                Assert.True(p.BleedBox.X >= 5m);
                Assert.True(p.BleedBox.Y >= 5m);
                Assert.True(p.BleedBox.X + p.BleedBox.Width <= 95m);
                Assert.True(p.BleedBox.Y + p.BleedBox.Height <= 75m);
                Assert.Equal(p.BleedBox.X + 1m, p.CutBox.X);
                Assert.Equal(p.BleedBox.Y + 1m, p.CutBox.Y);
            }

            var boxes = sheet.Instances.Select(i => i.BleedBox).ToList();
            for (var i = 0; i < boxes.Count; i++)
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i];
                var b = boxes[j];
                var overlapX = a.X < b.X + b.Width && b.X < a.X + a.Width;
                var overlapY = a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
                if (overlapX && overlapY)
                    Assert.Fail("bleed boxes overlap");
                if (overlapX)
                {
                    var verticalGap = Math.Max(a.Y - (b.Y + b.Height), b.Y - (a.Y + a.Height));
                    Assert.True(verticalGap >= 2m, $"vertical gap {verticalGap} < 2");
                }
                if (overlapY)
                {
                    var horizontalGap = Math.Max(a.X - (b.X + b.Width), b.X - (a.X + a.Width));
                    Assert.True(horizontalGap >= 2m, $"horizontal gap {horizontalGap} < 2");
                }
            }
        }
    }

    [Fact]
    public void RotationIsUsedAndDeterministic()
    {
        var req = new PlanningRequest
        {
            PaperWidth = 50, PaperHeight = 50,
            MarginTop = 0, MarginRight = 0, MarginBottom = 0, MarginLeft = 0,
            Gap = 0, Bleed = 0, MaxSheets = 1,
            Labels = new()
            {
                new LabelSpec { Id = "tall", Width = 10, Height = 40, Quantity = 2, AllowRotation = true },
            },
        };
        var first = PlannerEngine.Run(req).Plan!;
        var second = PlannerEngine.Run(req).Plan!;
        Assert.Equal(first.SheetsUsed, second.SheetsUsed);
        var placed = first.Sheets[0].Instances;
        Assert.Contains(placed, p => p.Rotated && p.CutBox.Width == 40m && p.CutBox.Height == 10m);
        Assert.Equal(2, placed.Count);
    }

    [Fact]
    public async Task RejectsInvalidDimensionsDuplicateIdsAndZeroPrintableArea()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var req = new PlanningRequest
        {
            PaperWidth = 10, PaperHeight = 10,
            MarginTop = 6, MarginBottom = 6, MarginLeft = 0, MarginRight = 0,
            Gap = 1, Bleed = 1, MaxSheets = 1,
            Labels = new()
            {
                new LabelSpec { Id = "x", Width = 0, Height = -1, Quantity = 0, AllowRotation = false },
                new LabelSpec { Id = "x", Width = 5, Height = 5, Quantity = 1, AllowRotation = false },
            },
        };
        using var response = await client.PostAsJsonAsync("/api/plans", req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidationErrorResponse>();
        Assert.NotNull(body);
        Assert.Contains(body!.Errors, e => e.Field.Contains("width"));
        Assert.Contains(body.Errors, e => e.Field.Contains("height"));
        Assert.Contains(body.Errors, e => e.Field.Contains("quantity"));
        Assert.Contains(body.Errors, e => e.Field.Contains("duplicate", StringComparison.OrdinalIgnoreCase) || e.Field.Contains("id"));
        Assert.Contains(body.Errors, e => e.Field == "margins");
    }

    [Fact]
    public async Task SheetLimitReportsUnplacedWithoutExtraSheets()
    {
        var req = new PlanningRequest
        {
            PaperWidth = 30, PaperHeight = 30,
            MarginTop = 0, MarginRight = 0, MarginBottom = 0, MarginLeft = 0,
            Gap = 0, Bleed = 0, MaxSheets = 1,
            Labels = new()
            {
                new LabelSpec { Id = "a", Width = 20, Height = 20, Quantity = 3, AllowRotation = false },
            },
        };
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/plans", req);
        var plan = await response.Content.ReadFromJsonAsync<PlanningResponse>();
        Assert.Equal(1, plan!.SheetsUsed);
        Assert.Equal(2, plan.Unplaced.Count);
        Assert.All(plan.Unplaced, u => Assert.StartsWith("sheet_limit", u.Reason));
    }
}
