namespace PrintPlanner.Api;

public sealed record PlanningRequest
{
    public decimal PaperWidth { get; init; }
    public decimal PaperHeight { get; init; }
    public decimal MarginTop { get; init; }
    public decimal MarginRight { get; init; }
    public decimal MarginBottom { get; init; }
    public decimal MarginLeft { get; init; }
    public decimal Gap { get; init; }
    public decimal Bleed { get; init; }
    public int MaxSheets { get; init; }
    public List<LabelSpec> Labels { get; init; } = new();
}

public sealed record LabelSpec
{
    public string Id { get; init; } = string.Empty;
    public decimal Width { get; init; }
    public decimal Height { get; init; }
    public int Quantity { get; init; }
    public bool AllowRotation { get; init; }
}

public sealed record RectDto(decimal X, decimal Y, decimal Width, decimal Height);

public sealed record PlacedInstanceDto(
    string SourceId,
    int Sequence,
    RectDto CutBox,
    RectDto BleedBox,
    bool Rotated);

public sealed record SheetDto(int Index, List<PlacedInstanceDto> Instances);

public sealed record UnplacedInstanceDto(string SourceId, int Sequence, string Reason);

public sealed record PlanningResponse(
    List<SheetDto> Sheets,
    List<UnplacedInstanceDto> Unplaced,
    int SheetsUsed);

public sealed record ValidationError(string Field, string Message);

public sealed record ValidationErrorResponse(List<ValidationError> Errors);
