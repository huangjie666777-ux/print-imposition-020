namespace PrintPlanner.Api;

public readonly record struct Inst(string Id, int Sequence, decimal W, decimal H, bool AllowRotation);

public readonly record struct FRect(decimal X, decimal Y, decimal W, decimal H)
{
    public decimal Right => X + W;
    public decimal Top => Y + H;
}

public sealed class SheetLayout
{
    public required FRect Area { get; init; }
    public List<FRect> Free { get; set; } = new();
    public List<PlacedInstanceDto> Instances { get; } = new();
}

internal readonly record struct Candidate(FRect BleedRect, bool Rotated, decimal Short, decimal Long);

public static class PlannerEngine
{
    public static (PlanningResponse? Plan, List<ValidationError> Errors) Run(PlanningRequest req)
    {
        var errors = Validate(req);
        if (errors.Count > 0) return (null, errors);

        var area = new FRect(
            req.MarginLeft, req.MarginBottom,
            req.PaperWidth - req.MarginLeft - req.MarginRight,
            req.PaperHeight - req.MarginBottom - req.MarginTop);

        var instances = new List<Inst>();
        foreach (var label in req.Labels) // 请求顺序确定，序号从 1 开始
            for (var n = 1; n <= label.Quantity; n++)
                instances.Add(new Inst(label.Id, n, label.Width, label.Height, label.AllowRotation));

        var sheets = new List<SheetLayout>();
        var unplaced = new List<UnplacedInstanceDto>();

        foreach (var inst in instances)
        {
            var best = BestOnSheets(sheets, inst, req.Bleed);

            if (best is null)
            {
                var flatW = inst.W + 2m * req.Bleed;
                var flatH = inst.H + 2m * req.Bleed;
                var fitsFlat = flatW <= area.W && flatH <= area.H;
                var fitsRotated = inst.AllowRotation && flatH <= area.W && flatW <= area.H;

                if (!fitsFlat && !fitsRotated)
                {
                    unplaced.Add(new UnplacedInstanceDto(inst.Id, inst.Sequence,
                        "too_large: bleed box does not fit in the printable area of a single sheet"));
                    continue;
                }

                if (sheets.Count >= req.MaxSheets)
                {
                    unplaced.Add(new UnplacedInstanceDto(inst.Id, inst.Sequence,
                        "sheet_limit: no fit on open sheets and maxSheets reached"));
                    continue;
                }

                var sheet = new SheetLayout { Area = area };
                sheet.Free.Add(area);
                sheets.Add(sheet);
                best = BestOnSheets(sheets, inst, req.Bleed);
                if (best is null)
                {
                    sheets.Remove(sheet);
                    unplaced.Add(new UnplacedInstanceDto(inst.Id, inst.Sequence,
                        "too_large: bleed box does not fit in the printable area of a single sheet"));
                    continue;
                }
            }

            ApplyPlacement(best.Value.Sheet, best.Value.BleedRect, best.Value.Rotated, inst, area, req.Gap, req.Bleed);
        }

        var sheetDtos = sheets.Select((s, i) => new SheetDto(i + 1, s.Instances)).ToList();
        return (new PlanningResponse(sheetDtos, unplaced, sheets.Count), errors);
    }

    private static List<ValidationError> Validate(PlanningRequest r)
    {
        var errors = new List<ValidationError>();
        void CheckPositive(string field, decimal value)
        {
            if (value <= 0m) errors.Add(new ValidationError(field, "must be a positive number of millimetres"));
        }
        void CheckNonNegative(string field, decimal value)
        {
            if (value < 0m) errors.Add(new ValidationError(field, "must be zero or a positive number of millimetres"));
        }

        CheckPositive("paperWidth", r.PaperWidth);
        CheckPositive("paperHeight", r.PaperHeight);
        foreach (var (field, value) in new[]
        {
            ("marginTop", r.MarginTop),
            ("marginRight", r.MarginRight),
            ("marginBottom", r.MarginBottom),
            ("marginLeft", r.MarginLeft),
            ("gap", r.Gap),
            ("bleed", r.Bleed),
        })
            CheckNonNegative(field, value);
        if (r.MaxSheets < 1)
            errors.Add(new ValidationError("maxSheets", "must be a positive integer"));

        if (r.PaperWidth > 0 && r.MarginLeft >= 0 && r.MarginRight >= 0 &&
            r.MarginLeft + r.MarginRight >= r.PaperWidth)
            errors.Add(new ValidationError("margins", "left + right margins must leave positive printable width"));
        if (r.PaperHeight > 0 && r.MarginTop >= 0 && r.MarginBottom >= 0 &&
            r.MarginTop + r.MarginBottom >= r.PaperHeight)
            errors.Add(new ValidationError("margins", "top + bottom margins must leave positive printable height"));

        if (r.Labels is null || r.Labels.Count == 0)
        {
            errors.Add(new ValidationError("labels", "at least one label is required"));
            return errors;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < r.Labels.Count; i++)
        {
            var label = r.Labels[i];
            var prefix = $"labels[{i}]";
            if (string.IsNullOrWhiteSpace(label.Id))
                errors.Add(new ValidationError($"{prefix}.id", "id is required"));
            else if (!ids.Add(label.Id))
                errors.Add(new ValidationError($"{prefix}.id", $"duplicate label id '{label.Id}'"));
            CheckPositive($"{prefix}.width", label.Width);
            CheckPositive($"{prefix}.height", label.Height);
            if (label.Quantity < 1)
                errors.Add(new ValidationError($"{prefix}.quantity", "must be a positive integer"));
        }

        return errors;
    }

    // MaxRects/BSSF：按纸张打开顺序复用；同张内选剩余短边最小的位置，
    // 再以剩余长边、不旋转、靠下、靠左作为确定性同分规则。
    private static (SheetLayout Sheet, FRect BleedRect, bool Rotated)? BestOnSheets(
        List<SheetLayout> sheets, Inst inst, decimal bleed)
    {
        Candidate? globalBest = null;
        SheetLayout? globalSheet = null;

        foreach (var sheet in sheets)
        {
            Candidate? sheetBest = null;
            foreach (var free in sheet.Free)
            {
                var orientations = inst.AllowRotation ? new[] { false, true } : new[] { false };
                foreach (var rotated in orientations)
                {
                    var cutW = rotated ? inst.H : inst.W;
                    var cutH = rotated ? inst.W : inst.H;
                    var bleedW = cutW + 2m * bleed;
                    var bleedH = cutH + 2m * bleed;
                    if (bleedW > free.W || bleedH > free.H) continue;

                    var dx = free.W - bleedW;
                    var dy = free.H - bleedH;
                    var candidate = new Candidate(
                        new FRect(free.X, free.Y, bleedW, bleedH), rotated,
                        Math.Min(dx, dy), Math.Max(dx, dy));
                    if (sheetBest is null || CompareRank(Rank(candidate), Rank(sheetBest.Value)) < 0)
                        sheetBest = candidate;
                }
            }

            if (sheetBest is not null && (globalBest is null || CompareRank(Rank(sheetBest.Value), Rank(globalBest.Value)) < 0))
            {
                globalBest = sheetBest;
                globalSheet = sheet;
            }
        }

        return globalBest is null ? null : (globalSheet!, globalBest.Value.BleedRect, globalBest.Value.Rotated);
    }

    private static (decimal, decimal, int, decimal, decimal) Rank(in Candidate c) =>
        (c.Short, c.Long, c.Rotated ? 1 : 0, c.BleedRect.Y, c.BleedRect.X);

    private static int CompareRank(
        (decimal A, decimal B, int R, decimal Y, decimal X) a,
        (decimal A, decimal B, int R, decimal Y, decimal X) b)
    {
        var c = a.A.CompareTo(b.A);
        if (c != 0) return c;
        c = a.B.CompareTo(b.B);
        if (c != 0) return c;
        c = a.R.CompareTo(b.R);
        if (c != 0) return c;
        c = a.Y.CompareTo(b.Y);
        return c != 0 ? c : a.X.CompareTo(b.X);
    }

    private static void ApplyPlacement(SheetLayout sheet, FRect bleedRect, bool rotated, Inst inst,
        FRect area, decimal gap, decimal bleed)
    {
        var cut = new RectDto(
            bleedRect.X + bleed, bleedRect.Y + bleed,
            rotated ? inst.H : inst.W, rotated ? inst.W : inst.H);
        var bleedDto = new RectDto(bleedRect.X, bleedRect.Y, bleedRect.W, bleedRect.H);
        sheet.Instances.Add(new PlacedInstanceDto(inst.Id, inst.Sequence, cut, bleedDto, rotated));

        // 占用区 = 出血框四周外扩 gap，再裁剪回可印区：贴纸边不额外加间距。
        // 自由矩形已扣除历史占用区，因此新出血框与历史出血框在至少一个轴向上留足 gap。
        var gx = Math.Max(area.X, bleedRect.X - gap);
        var gy = Math.Max(area.Y, bleedRect.Y - gap);
        var grown = new FRect(
            gx, gy,
            Math.Min(area.Right, bleedRect.Right + gap) - gx,
            Math.Min(area.Top, bleedRect.Top + gap) - gy);

        var next = new List<FRect>();
        foreach (var free in sheet.Free)
            foreach (var split in SplitFree(free, grown))
                AddFree(next, split);
        sheet.Free = next;
    }

    private static IEnumerable<FRect> SplitFree(FRect free, FRect occ)
    {
        if (occ.X >= free.Right || occ.Right <= free.X || occ.Y >= free.Top || occ.Top <= free.Y)
        {
            yield return free;
            yield break;
        }

        if (occ.X > free.X) yield return free with { W = occ.X - free.X };
        if (occ.Right < free.Right) yield return new FRect(occ.Right, free.Y, free.Right - occ.Right, free.H);
        if (occ.Y > free.Y) yield return free with { H = occ.Y - free.Y };
        if (occ.Top < free.Top) yield return new FRect(free.X, occ.Top, free.W, free.Top - occ.Top);
    }

    private static void AddFree(List<FRect> list, FRect rect)
    {
        if (rect.W <= 0m || rect.H <= 0m) return;
        foreach (var existing in list)
            if (rect.X >= existing.X && rect.Y >= existing.Y &&
                rect.Right <= existing.Right && rect.Top <= existing.Top)
                return;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var e = list[i];
            if (e.X >= rect.X && e.Y >= rect.Y && e.Right <= rect.Right && e.Top <= rect.Top)
                list.RemoveAt(i);
        }
        list.Add(rect);
    }
}
