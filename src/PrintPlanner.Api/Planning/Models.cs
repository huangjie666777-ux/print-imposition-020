namespace PrintPlanner.Api.Planning;

public sealed class Margins
{
    public decimal Top { get; set; }
    public decimal Right { get; set; }
    public decimal Bottom { get; set; }
    public decimal Left { get; set; }
}

public sealed class SheetSpec
{
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public Margins? Margins { get; set; }
    public decimal Spacing { get; set; }
    public decimal Bleed { get; set; }
    public int MaxSheets { get; set; }
}

public sealed class LabelSpec
{
    public string? Id { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public int Quantity { get; set; }
    public bool AllowRotate { get; set; }
}

public sealed class PlanRequest
{
    public SheetSpec? Sheet { get; set; }
    public List<LabelSpec>? Labels { get; set; }
}

public sealed class PlacementDto
{
    public required string LabelId { get; init; }
    public required int Sequence { get; init; }
    public required decimal X { get; init; }
    public required decimal Y { get; init; }
    public required decimal Width { get; init; }
    public required decimal Height { get; init; }
    public required bool Rotated { get; init; }
}

public sealed class SheetDto
{
    public required int Index { get; init; }
    public required List<PlacementDto> Placements { get; init; }
}

public sealed class UnplacedDto
{
    public required string LabelId { get; init; }
    public required int Sequence { get; init; }
    public required string Reason { get; init; }
}

public sealed class PlanResponse
{
    public required List<SheetDto> Sheets { get; init; }
    public required List<UnplacedDto> Unplaced { get; init; }
    public required int SheetsUsed { get; init; }
}

public sealed class FieldError
{
    public required string Field { get; init; }
    public required string Message { get; init; }
}
