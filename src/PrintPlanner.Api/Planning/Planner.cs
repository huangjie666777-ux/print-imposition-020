namespace PrintPlanner.Api.Planning;

public static class PlanValidator
{
    public static List<FieldError> Validate(PlanRequest? request)
    {
        var errors = new List<FieldError>();
        if (request is null)
        {
            errors.Add(new FieldError { Field = "body", Message = "Request body is required." });
            return errors;
        }

        var sheet = request.Sheet;
        if (sheet is null)
        {
            errors.Add(new FieldError { Field = "sheet", Message = "Sheet specification is required." });
        }
        else
        {
            if (sheet.Width <= 0) errors.Add(new FieldError { Field = "sheet.width", Message = "Sheet width must be positive." });
            if (sheet.Height <= 0) errors.Add(new FieldError { Field = "sheet.height", Message = "Sheet height must be positive." });
            if (sheet.Spacing < 0) errors.Add(new FieldError { Field = "sheet.spacing", Message = "Spacing must be zero or positive." });
            if (sheet.Bleed < 0) errors.Add(new FieldError { Field = "sheet.bleed", Message = "Bleed must be zero or positive." });
            if (sheet.MaxSheets < 1) errors.Add(new FieldError { Field = "sheet.maxSheets", Message = "MaxSheets must be at least 1." });
            if (sheet.Margins is null)
            {
                errors.Add(new FieldError { Field = "sheet.margins", Message = "Margins are required." });
            }
            else
            {
                if (sheet.Margins.Top < 0) errors.Add(new FieldError { Field = "sheet.margins.top", Message = "Margin must be zero or positive." });
                if (sheet.Margins.Right < 0) errors.Add(new FieldError { Field = "sheet.margins.right", Message = "Margin must be zero or positive." });
                if (sheet.Margins.Bottom < 0) errors.Add(new FieldError { Field = "sheet.margins.bottom", Message = "Margin must be zero or positive." });
                if (sheet.Margins.Left < 0) errors.Add(new FieldError { Field = "sheet.margins.left", Message = "Margin must be zero or positive." });
                if (sheet.Width > 0 && sheet.Margins.Left + sheet.Margins.Right >= sheet.Width)
                    errors.Add(new FieldError { Field = "sheet.margins", Message = "Left and right margins leave no usable width." });
                if (sheet.Height > 0 && sheet.Margins.Top + sheet.Margins.Bottom >= sheet.Height)
                    errors.Add(new FieldError { Field = "sheet.margins", Message = "Top and bottom margins leave no usable height." });
            }
        }

        if (request.Labels is null || request.Labels.Count == 0)
        {
            errors.Add(new FieldError { Field = "labels", Message = "At least one label is required." });
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < request.Labels.Count; i++)
            {
                var label = request.Labels[i];
                var prefix = $"labels[{i}]";
                if (string.IsNullOrWhiteSpace(label.Id))
                {
                    errors.Add(new FieldError { Field = $"{prefix}.id", Message = "Label id is required." });
                }
                else if (!seen.Add(label.Id))
                {
                    errors.Add(new FieldError { Field = $"{prefix}.id", Message = $"Duplicate label id '{label.Id}'." });
                }
                if (label.Width <= 0) errors.Add(new FieldError { Field = $"{prefix}.width", Message = "Label width must be positive." });
                if (label.Height <= 0) errors.Add(new FieldError { Field = $"{prefix}.height", Message = "Label height must be positive." });
                if (label.Quantity < 1) errors.Add(new FieldError { Field = $"{prefix}.quantity", Message = "Quantity must be a positive integer." });
            }
        }

        return errors;
    }
}

internal sealed record Instance(string LabelId, int Sequence, decimal CutWidth, decimal CutHeight, bool AllowRotate);

internal sealed record PlacedInstance(Instance Instance, decimal X, decimal Y, decimal ExpandedWidth, decimal ExpandedHeight, bool Rotated);

internal sealed class Shelf
{
    public decimal Y { get; init; }
    public decimal Height { get; init; }
    public decimal NextX { get; set; }
}

internal sealed class SheetLayout
{
    public List<Shelf> Shelves { get; } = new();
    public List<PlacedInstance> Placements { get; } = new();
}

public static class Planner
{
    public static PlanResponse Plan(PlanRequest request)
    {
        var sheet = request.Sheet!;
        var margins = sheet.Margins!;
        var usableLeft = margins.Left;
        var usableTop = margins.Top;
        var usableRight = sheet.Width - margins.Right;
        var usableBottom = sheet.Height - margins.Bottom;
        var usableWidth = usableRight - usableLeft;
        var usableHeight = usableBottom - usableTop;

        var instances = new List<Instance>();
        foreach (var label in request.Labels!)
        {
            for (var seq = 1; seq <= label.Quantity; seq++)
            {
                instances.Add(new Instance(label.Id!, seq, label.Width, label.Height, label.AllowRotate));
            }
        }

        // Deterministic order: largest expanded dimension first, then stable request order.
        var ordered = instances
            .Select((inst, index) => (inst, index))
            .OrderByDescending(t => Math.Max(
                t.inst.CutWidth + 2 * sheet.Bleed, t.inst.CutHeight + 2 * sheet.Bleed))
            .ThenBy(t => t.index)
            .Select(t => t.inst)
            .ToList();

        var sheets = new List<SheetLayout>();
        var unplaced = new List<UnplacedDto>();

        foreach (var instance in ordered)
        {
            var orientations = GetOrientations(instance, sheet.Bleed);
            var fitsAnyOrientation = orientations.Any(o => o.Ew <= usableWidth && o.Eh <= usableHeight);
            if (!fitsAnyOrientation)
            {
                unplaced.Add(new UnplacedDto
                {
                    LabelId = instance.LabelId,
                    Sequence = instance.Sequence,
                    Reason = "Instance (including bleed) does not fit within the usable sheet area.",
                });
                continue;
            }

            var placed = false;
            foreach (var layout in sheets)
            {
                if (TryPlace(layout, instance, orientations, sheet, usableLeft, usableTop, usableRight, usableBottom))
                {
                    placed = true;
                    break;
                }
            }

            if (!placed && sheets.Count < sheet.MaxSheets)
            {
                var layout = new SheetLayout();
                sheets.Add(layout);
                placed = TryPlace(layout, instance, orientations, sheet, usableLeft, usableTop, usableRight, usableBottom);
            }

            if (!placed)
            {
                unplaced.Add(new UnplacedDto
                {
                    LabelId = instance.LabelId,
                    Sequence = instance.Sequence,
                    Reason = sheets.Count >= sheet.MaxSheets
                        ? "Sheet limit reached; no remaining space on opened sheets."
                        : "No remaining space on opened sheets.",
                });
            }
        }

        return new PlanResponse
        {
            Sheets = sheets.Select((layout, i) => new SheetDto
            {
                Index = i + 1,
                Placements = layout.Placements.Select(p => new PlacementDto
                {
                    LabelId = p.Instance.LabelId,
                    Sequence = p.Instance.Sequence,
                    X = p.X + sheet.Bleed,
                    Y = p.Y + sheet.Bleed,
                    Width = p.Rotated ? p.Instance.CutHeight : p.Instance.CutWidth,
                    Height = p.Rotated ? p.Instance.CutWidth : p.Instance.CutHeight,
                    Rotated = p.Rotated,
                }).ToList(),
            }).ToList(),
            Unplaced = unplaced,
            SheetsUsed = sheets.Count,
        };
    }

    private static List<(decimal Ew, decimal Eh, bool Rotated)> GetOrientations(Instance instance, decimal bleed)
    {
        var list = new List<(decimal, decimal, bool)>
        {
            (instance.CutWidth + 2 * bleed, instance.CutHeight + 2 * bleed, false),
        };
        if (instance.AllowRotate && instance.CutWidth != instance.CutHeight)
        {
            list.Add((instance.CutHeight + 2 * bleed, instance.CutWidth + 2 * bleed, true));
        }
        return list;
    }

    private static bool TryPlace(
        SheetLayout layout,
        Instance instance,
        List<(decimal Ew, decimal Eh, bool Rotated)> orientations,
        SheetSpec sheet,
        decimal usableLeft,
        decimal usableTop,
        decimal usableRight,
        decimal usableBottom)
    {
        foreach (var shelf in layout.Shelves)
        {
            foreach (var (ew, eh, rotated) in orientations)
            {
                if (eh <= shelf.Height && shelf.NextX + ew <= usableRight)
                {
                    layout.Placements.Add(new PlacedInstance(instance, shelf.NextX, shelf.Y, ew, eh, rotated));
                    shelf.NextX += ew + sheet.Spacing;
                    return true;
                }
            }
        }

        var shelfY = layout.Shelves.Count == 0
            ? usableTop
            : layout.Shelves[^1].Y + layout.Shelves[^1].Height + sheet.Spacing;
        foreach (var (ew, eh, rotated) in orientations)
        {
            if (shelfY + eh <= usableBottom && usableLeft + ew <= usableRight)
            {
                var shelf = new Shelf { Y = shelfY, Height = eh, NextX = usableLeft + ew + sheet.Spacing };
                layout.Shelves.Add(shelf);
                layout.Placements.Add(new PlacedInstance(instance, usableLeft, shelfY, ew, eh, rotated));
                return true;
            }
        }

        return false;
    }
}
