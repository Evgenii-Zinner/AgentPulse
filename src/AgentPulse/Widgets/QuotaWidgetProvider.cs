using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using AgentPulse.Models;
using AgentPulse.Providers;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace AgentPulse.Widgets;

[ComVisible(true)]
[Guid("8A1C5F22-5B20-4F71-B88E-3298246AE690")]
public class QuotaWidgetProvider : IWidgetProvider
{
    private static readonly ConcurrentDictionary<string, WidgetContext> ActiveWidgets = new();
    private readonly AntigravityQuotaProvider _quotaProvider = new();

    public void CreateWidget(WidgetContext widgetContext)
    {
        ActiveWidgets[widgetContext.Id] = widgetContext;
        _ = UpdateWidgetAsync(widgetContext.Id, widgetContext.Size);
    }

    public void Activate(WidgetContext widgetContext)
    {
        ActiveWidgets[widgetContext.Id] = widgetContext;
        _ = UpdateWidgetAsync(widgetContext.Id, widgetContext.Size);
    }

    public void Deactivate(string widgetId)
    {
        ActiveWidgets.TryRemove(widgetId, out _);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        ActiveWidgets.TryRemove(widgetId, out _);
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        var widgetId = actionInvokedArgs.WidgetContext.Id;
        var size = actionInvokedArgs.WidgetContext.Size;
        _ = UpdateWidgetAsync(widgetId, size);
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var widgetId = contextChangedArgs.WidgetContext.Id;
        var size = contextChangedArgs.WidgetContext.Size;
        if (ActiveWidgets.TryGetValue(widgetId, out var context))
        {
            ActiveWidgets[widgetId] = contextChangedArgs.WidgetContext;
        }
        _ = UpdateWidgetAsync(widgetId, size);
    }

    private async Task UpdateWidgetAsync(string widgetId, WidgetSize size)
    {
        try
        {
            var status = await _quotaProvider.GetStatusAsync();

            int geminiPercent = 100;
            int geminiSecondaryPercent = 100;
            bool geminiHasSecondary = false;
            string geminiReset = "Ready";
            string geminiSecondaryReset = "Ready";

            int claudePercent = 100;
            int claudeSecondaryPercent = 100;
            bool claudeHasSecondary = false;
            string claudeReset = "Ready";

            int argonPercent = 100;
            int argonSecondaryPercent = 100;
            bool argonHasSecondary = false;
            string argonReset = "Ready";

            foreach (var m in status.Models)
            {
                if (m.Name.Contains("gemini", StringComparison.OrdinalIgnoreCase) &&
                    !m.Name.Contains("argon", StringComparison.OrdinalIgnoreCase))
                {
                    geminiPercent = m.PercentRemaining;
                    geminiSecondaryPercent = m.SecondaryPercentRemaining;
                    geminiHasSecondary = m.HasSecondaryLimit;
                    geminiReset = m.FormattedReset;
                    geminiSecondaryReset = m.FormattedSecondaryReset;
                }
                else if (m.Name.Contains("claude", StringComparison.OrdinalIgnoreCase) ||
                         m.Name.Contains("gpt", StringComparison.OrdinalIgnoreCase))
                {
                    claudePercent = m.PercentRemaining;
                    claudeSecondaryPercent = m.SecondaryPercentRemaining;
                    claudeHasSecondary = m.HasSecondaryLimit;
                    claudeReset = m.FormattedReset;
                }
                else if (m.Name.Contains("argon", StringComparison.OrdinalIgnoreCase))
                {
                    argonPercent = m.PercentRemaining;
                    argonSecondaryPercent = m.SecondaryPercentRemaining;
                    argonHasSecondary = m.HasSecondaryLimit;
                    argonReset = m.FormattedReset;
                }
            }

            int imgSize = 96;

            var geminiCircleImg = CreateConcentricRingImageBase64(geminiPercent, geminiSecondaryPercent, geminiHasSecondary, imgSize);
            var claudeCircleImg = CreateConcentricRingImageBase64(claudePercent, claudeSecondaryPercent, claudeHasSecondary, imgSize);
            var argonCircleImg = CreateConcentricRingImageBase64(argonPercent, argonSecondaryPercent, argonHasSecondary, imgSize);

            var dataObj = new
            {
                gemini_percent = geminiPercent,
                gemini_secondary_percent = geminiSecondaryPercent,
                gemini_reset = geminiReset,
                gemini_secondary_reset = geminiSecondaryReset,
                gemini_circle_image = geminiCircleImg,
                claude_percent = claudePercent,
                claude_reset = claudeReset,
                claude_circle_image = claudeCircleImg,
                argon_percent = argonPercent,
                argon_reset = argonReset,
                argon_circle_image = argonCircleImg
            };

            var dataJson = JsonSerializer.Serialize(dataObj);
            var templatePath = GetTemplatePathForSize(size);
            var templateJson = await File.ReadAllTextAsync(templatePath);

            var updateOptions = new WidgetUpdateRequestOptions(widgetId)
            {
                Template = templateJson,
                Data = dataJson
            };

            WidgetManager.GetDefault().UpdateWidget(updateOptions);
        }
        catch { }
    }

    private static string CreateConcentricRingImageBase64(int percent, int secondaryPercent, bool hasSecondary, int size)
    {
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        int padding = 4;
        int outerDiameter = size - (padding * 2);

        // 1. Outer Ring Background
        using var outerBgPen = new Pen(Color.FromArgb(255, 30, 36, 48), 5.5f);
        g.DrawEllipse(outerBgPen, padding, padding, outerDiameter, outerDiameter);

        // 2. Outer Ring Progress Arc (White)
        if (percent > 0)
        {
            float outerSweep = Math.Clamp(percent * 3.6f, 0, 360f);
            using var outerFgPen = new Pen(Color.White, 5.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(outerFgPen, padding, padding, outerDiameter, outerDiameter, -90f, outerSweep);
        }

        if (hasSecondary)
        {
            // 3. Inner Ring Background
            int innerPadding = padding + 11;
            int innerDiameter = size - (innerPadding * 2);
            using var innerBgPen = new Pen(Color.FromArgb(255, 20, 24, 34), 4f);
            g.DrawEllipse(innerBgPen, innerPadding, innerPadding, innerDiameter, innerDiameter);

            // 4. Inner Ring Progress Arc (Cyan #4CC2FF)
            if (secondaryPercent > 0)
            {
                float innerSweep = Math.Clamp(secondaryPercent * 3.6f, 0, 360f);
                using var innerFgPen = new Pen(Color.FromArgb(255, 76, 194, 255), 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(innerFgPen, innerPadding, innerPadding, innerDiameter, innerDiameter, -90f, innerSweep);
            }
        }

        // 5. Center Numbers
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        if (hasSecondary)
        {
            using var numFont = new Font("Segoe UI", size * 0.16f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush1 = new SolidBrush(Color.White);
            using var brush2 = new SolidBrush(Color.FromArgb(255, 76, 194, 255));

            g.DrawString($"{percent}%", numFont, brush1, new RectangleF(0, size * 0.25f, size, size * 0.24f), sf);
            g.DrawString($"{secondaryPercent}%", numFont, brush2, new RectangleF(0, size * 0.51f, size, size * 0.24f), sf);
        }
        else
        {
            using var font = new Font("Segoe UI", size * 0.22f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.White);
            g.DrawString($"{percent}%", font, brush, new RectangleF(0, 0, size, size), sf);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    private static string GetTemplatePathForSize(WidgetSize size)
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Widgets", "WidgetSmall.json");
    }
}
