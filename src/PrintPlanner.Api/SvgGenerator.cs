using System.Globalization;
using System.Text;

namespace PrintPlanner.Api;

public static class SvgGenerator
{
    public static string Generate(PlanningRequest req, PlanningResponse plan)
    {
        var sb = new StringBuilder();
        const decimal sep = 8m;
        var totalH = plan.Sheets.Count * req.PaperHeight + (plan.Sheets.Count - 1) * sep;
        var pad = 4m;
        var w = req.PaperWidth + pad * 2;
        var h = totalH + pad * 2;

        sb.Append(CultureInfo.InvariantCulture, $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {F(w)} {F(h)}" width="{F(w)}mm" height="{F(h)}mm" font-family="sans-serif">""");
        sb.Append("<rect width='100%' height='100%' fill='#fafafa'/>");

        for (var i = 0; i < plan.Sheets.Count; i++)
        {
            var sheet = plan.Sheets[i];
            var top = pad + i * (req.PaperHeight + sep);
            sb.Append(CultureInfo.InvariantCulture, $"""<g><text x="{F(pad)}" y="{F(top - 1m)}" font-size="3" fill="#333">Sheet {sheet.Index}</text>""");
            sb.Append(CultureInfo.InvariantCulture,
                $"""<rect x="{F(pad)}" y="{F(top)}" width="{F(req.PaperWidth)}" height="{F(req.PaperHeight)}" fill="white" stroke="#999"/>""");

            // 留白内的可印区（蓝灰色），SVG y 自顶部：做 y 翻转后与 JSON 的左下原点坐标一致。
            var areaYTop = req.MarginTop;
            sb.Append(CultureInfo.InvariantCulture, $"""<rect x="{F(pad + req.MarginLeft)}" y="{F(top + areaYTop)}" width="{F(req.PaperWidth - req.MarginLeft - req.MarginRight)}" height="{F(req.PaperHeight - req.MarginTop - req.MarginBottom)}" fill="none" stroke="#3b6ea5" stroke-width="0.25" stroke-dasharray='1.2 0.8'/>""");

            foreach (var inst in sheet.Instances)
            {
                var bx = pad + inst.BleedBox.X;
                var by = top + req.PaperHeight - inst.BleedBox.Y - inst.BleedBox.Height;
                var cx = pad + inst.CutBox.X;
                var cy = top + req.PaperHeight - inst.CutBox.Y - inst.CutBox.Height;

                sb.Append(CultureInfo.InvariantCulture,
                    $"""<rect x="{F(bx)}" y="{F(by)}" width="{F(inst.BleedBox.Width)}" height="{F(inst.BleedBox.Height)}" fill="#fde7e7" stroke="#c0392b" stroke-width="0.2" stroke-dasharray='0.8 0.5'/>""");
                sb.Append(CultureInfo.InvariantCulture,
                    $"""<rect x="{F(cx)}" y="{F(cy)}" width="{F(inst.CutBox.Width)}" height="{F(inst.CutBox.Height)}" fill="#e8f0fb" stroke="#111" stroke-width="0.25"/>""");

                var label = $"{inst.SourceId}#{inst.Sequence}" + (inst.Rotated ? " R" : string.Empty);
                var tx = pad + inst.CutBox.X + inst.CutBox.Width / 2m;
                var ty = top + req.PaperHeight - inst.CutBox.Y - inst.CutBox.Height / 2m;
                var fs = Math.Min(2.6m, Math.Min(inst.CutBox.Width, inst.CutBox.Height) * 0.28m);
                if (fs > 0.8m)
                {
                    sb.Append(CultureInfo.InvariantCulture,
                        $"""<text x="{F(tx)}" y="{F(ty + fs * 0.35m)}" font-size="{F(fs)}" text-anchor="middle" fill="#111">{Escape(label)}</text>""");
                }
            }
            sb.Append("</g>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string F(decimal v) => v.ToString("0.########", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
