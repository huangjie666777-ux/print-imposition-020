using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public sealed class PlanTests
{
    private static JsonSerializerOptions JsonOptions => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static object SampleRequest() => new
    {
        sheet = new
        {
            width = 100m,
            height = 70m,
            margins = new { top = 5m, right = 5m, bottom = 5m, left = 5m },
            spacing = 2m,
            bleed = 1m,
            maxSheets = 3,
        },
        labels = new object[]
        {
            new { id = "A", width = 30m, height = 20m, quantity = 3, allowRotate = true },
            new { id = "B", width = 18m, height = 12m, quantity = 4, allowRotate = false },
        },
    };

    [Fact]
    public async Task PlanProducesValidLayout()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/plan", SampleRequest(), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.GetProperty("sheetsUsed").GetInt32() >= 1);
        Assert.Equal(0, root.GetProperty("unplaced").GetArrayLength());

        var placed = 0;
        foreach (var sheet in root.GetProperty("sheets").EnumerateArray())
        {
            var boxes = new List<(decimal X1, decimal Y1, decimal X2, decimal Y2)>();
            foreach (var p in sheet.GetProperty("placements").EnumerateArray())
            {
                placed++;
                var x = p.GetProperty("x").GetDecimal();
                var y = p.GetProperty("y").GetDecimal();
                var w = p.GetProperty("width").GetDecimal();
                var h = p.GetProperty("height").GetDecimal();
                // Expanded (bleed) box must stay inside margins.
                Assert.True(x - 1m >= 5m && y - 1m >= 5m);
                Assert.True(x + w + 1m <= 95m && y + h + 1m <= 65m);
                boxes.Add((x - 1m, y - 1m, x + w + 1m, y + h + 1m));
            }
            // Expanded boxes must keep at least the spacing on one axis.
            for (var i = 0; i < boxes.Count; i++)
            {
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i];
                    var b = boxes[j];
                    var separated = a.X2 + 2m <= b.X1 || b.X2 + 2m <= a.X1
                        || a.Y2 + 2m <= b.Y1 || b.Y2 + 2m <= a.Y1;
                    Assert.True(separated, $"Boxes {i} and {j} violate spacing.");
                }
            }
        }
        Assert.Equal(7, placed);
    }

    [Fact]
    public async Task PlanIsDeterministic()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        string? first = null;
        for (var i = 0; i < 3; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/plan", SampleRequest(), JsonOptions);
            var body = await response.Content.ReadAsStringAsync();
            first ??= body;
            Assert.Equal(first, body);
        }
    }

    [Fact]
    public async Task RejectsInvalidRequestsWithFieldErrors()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var bad = new
        {
            sheet = new
            {
                width = -1m,
                height = 0m,
                margins = new { top = 0m, right = 0m, bottom = 0m, left = 0m },
                spacing = -2m,
                bleed = 0m,
                maxSheets = 0,
            },
            labels = new object[]
            {
                new { id = "A", width = 10m, height = 10m, quantity = 1, allowRotate = false },
                new { id = "A", width = 0m, height = 10m, quantity = 0, allowRotate = false },
            },
        };
        using var response = await client.PostAsJsonAsync("/api/plan", bad, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("sheet.width", body);
        Assert.Contains("sheet.height", body);
        Assert.Contains("sheet.spacing", body);
        Assert.Contains("sheet.maxSheets", body);
        Assert.Contains("labels[1].id", body);
        Assert.Contains("labels[1].width", body);
        Assert.Contains("labels[1].quantity", body);
    }

    [Fact]
    public async Task ReportsUnplacedInstancesWithReasons()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var request = new
        {
            sheet = new
            {
                width = 50m,
                height = 50m,
                margins = new { top = 5m, right = 5m, bottom = 5m, left = 5m },
                spacing = 0m,
                bleed = 0m,
                maxSheets = 1,
            },
            labels = new object[]
            {
                new { id = "TOO-BIG", width = 80m, height = 80m, quantity = 1, allowRotate = true },
                new { id = "FITS", width = 40m, height = 40m, quantity = 2, allowRotate = false },
            },
        };
        using var response = await client.PostAsJsonAsync("/api/plan", request, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var unplaced = json.RootElement.GetProperty("unplaced");
        Assert.Equal(2, unplaced.GetArrayLength());
        var reasons = unplaced.EnumerateArray().Select(u => u.GetProperty("reason").GetString()).ToList();
        Assert.Contains(reasons, r => r!.Contains("does not fit"));
        Assert.Contains(reasons, r => r!.Contains("Sheet limit reached"));
    }

    [Fact]
    public async Task SvgPreviewMatchesPlan()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/plan/svg", SampleRequest(), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        var svg = await response.Content.ReadAsStringAsync();
        Assert.Contains("<svg", svg);
        Assert.Contains("A #1", svg);
    }

    [Fact]
    public async Task SvgEscapesLabelNames()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var request = new
        {
            sheet = new
            {
                width = 100m,
                height = 100m,
                margins = new { top = 5m, right = 5m, bottom = 5m, left = 5m },
                spacing = 0m,
                bleed = 0m,
                maxSheets = 1,
            },
            labels = new object[]
            {
                new { id = "<script>&\"", width = 10m, height = 10m, quantity = 1, allowRotate = false },
            },
        };
        using var response = await client.PostAsJsonAsync("/api/plan/svg", request, JsonOptions);
        var svg = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>", svg);
        Assert.Contains("&lt;script&gt;", svg);
    }
}
