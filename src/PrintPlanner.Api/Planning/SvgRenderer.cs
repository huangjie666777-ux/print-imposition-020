using System.Globalization;
using System.Security;
using System.Text;

namespace PrintPlanner.Api.Planning;

public static class SvgRenderer
{
    private static string F(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Render(PlanRequest request, PlanResponse plan)
    {
        var sheet = request.Sheet!;
        var margins = sheet.Margins!;
        const decimal gap = 10m;
        var sb = new StringBuilder();
        var totalHeight = sheet.Height * plan.Sheets.Count + gap * Math.Max(0, plan.Sheets.Count - 1);
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(sheet.Width)}\" height=\"{F(totalHeight)}\" viewBox=\"0 0 {F(sheet.Width)} {F(totalHeight)}\" font-family=\"sans-serif\">");
        sb.AppendLine("<style>text{font-size:4px}</style>");

        foreach (var sheetDto in plan.Sheets)
        {
            var offsetY = (sheetDto.Index - 1) * (sheet.Height + gap);
            sb.AppendLine($"<g transform=\"translate(0,{F(offsetY)})\">");
            sb.AppendLine($"<rect x=\"0\" y=\"0\" width=\"{F(sheet.Width)}\" height=\"{F(sheet.Height)}\" fill=\"#ffffff\" stroke=\"#333333\" stroke-width=\"0.5\"/>");
            sb.AppendLine($"<rect x=\"{F(margins.Left)}\" y=\"{F(margins.Top)}\" width=\"{F(sheet.Width - margins.Left - margins.Right)}\" height=\"{F(sheet.Height - margins.Top - margins.Bottom)}\" fill=\"none\" stroke=\"#999999\" stroke-width=\"0.3\" stroke-dasharray=\"2,1\"/>");
            sb.AppendLine($"<text x=\"1\" y=\"4\" fill=\"#333333\">Sheet {sheetDto.Index}</text>");

            foreach (var p in sheetDto.Placements)
            {
                var bleedX = p.X - sheet.Bleed;
                var bleedY = p.Y - sheet.Bleed;
                var bleedW = p.Width + 2 * sheet.Bleed;
                var bleedH = p.Height + 2 * sheet.Bleed;
                sb.AppendLine($"<rect x=\"{F(bleedX)}\" y=\"{F(bleedY)}\" width=\"{F(bleedW)}\" height=\"{F(bleedH)}\" fill=\"#fde68a\" stroke=\"#f59e0b\" stroke-width=\"0.2\"/>");
                sb.AppendLine($"<rect x=\"{F(p.X)}\" y=\"{F(p.Y)}\" width=\"{F(p.Width)}\" height=\"{F(p.Height)}\" fill=\"#dbeafe\" stroke=\"#1d4ed8\" stroke-width=\"0.3\"/>");
                var name = SecurityElement.Escape($"{p.LabelId} #{p.Sequence}") ?? string.Empty;
                var suffix = p.Rotated ? " (R)" : "";
                sb.AppendLine($"<text x=\"{F(p.X + 0.5m)}\" y=\"{F(p.Y + 4)}\" fill=\"#1e3a8a\">{name}{suffix}</text>");
            }

            sb.AppendLine("</g>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }
}
