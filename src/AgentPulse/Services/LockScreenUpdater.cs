using System;
using System.Linq;
using AgentPulse.Models;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace AgentPulse.Services;

/// <summary>
/// Smart, event-driven Windows Lock Screen updater.
/// Answers the critical question: "Am I refreshed yet?"
/// </summary>
public class LockScreenUpdater
{
    public bool IsSupported { get; private set; }

    public LockScreenUpdater()
    {
        try
        {
            var package = Windows.ApplicationModel.Package.Current;
            IsSupported = package != null;
        }
        catch
        {
            IsSupported = false;
        }
    }

    /// <summary>
    /// Updates the lock screen numeric or glyph badge.
    /// </summary>
    public void UpdateBadge(AgentStatus status, bool isRefreshedNotification = false)
    {
        if (!IsSupported) return;

        try
        {
            var badgeUpdater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();

            if (isRefreshedNotification)
            {
                // Show 100 badge when refreshed
                var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);
                var element = badgeXml.SelectSingleNode("/badge") as XmlElement;
                element?.SetAttribute("value", "100");
                badgeUpdater.Update(new BadgeNotification(badgeXml));
                return;
            }

            if (status.StatusLevel == StatusLevel.Throttled)
            {
                var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeGlyph);
                var element = badgeXml.SelectSingleNode("/badge") as XmlElement;
                element?.SetAttribute("value", "alert");
                badgeUpdater.Update(new BadgeNotification(badgeXml));
            }
            else if (status.StatusLevel == StatusLevel.Warning)
            {
                var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeGlyph);
                var element = badgeXml.SelectSingleNode("/badge") as XmlElement;
                element?.SetAttribute("value", "attention");
                badgeUpdater.Update(new BadgeNotification(badgeXml));
            }
            else
            {
                var lowest = status.Models.OrderBy(m => m.RemainingFraction).FirstOrDefault();
                if (lowest != null && lowest.PercentRemaining < 100)
                {
                    var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);
                    var element = badgeXml.SelectSingleNode("/badge") as XmlElement;
                    element?.SetAttribute("value", lowest.PercentRemaining.ToString());
                    badgeUpdater.Update(new BadgeNotification(badgeXml));
                }
                else
                {
                    badgeUpdater.Clear();
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Pushes precise lock screen detailed text based on whether limits are ready or waiting for reset.
    /// </summary>
    public void UpdateTileText(AgentStatus status, bool isRefreshedNotification = false)
    {
        if (!IsSupported) return;

        try
        {
            var tileXml = TileUpdateManager.GetTemplateContent(TileTemplateType.TileWide310x150Text01);
            var textElements = tileXml.GetElementsByTagName("text");

            if (textElements.Count >= 2)
            {
                string line1;
                string line2;

                if (isRefreshedNotification)
                {
                    line1 = "REFRESHED • READY TO WORK";
                    line2 = "5-Hour Limit Reset • All Quotas Full";
                }
                else
                {
                    // Find the model with an active future reset
                    var activeResetModel = status.Models
                        .Where(m => m.ResetAt.HasValue && m.ResetAt.Value > DateTime.UtcNow && m.RemainingFraction < 0.98)
                        .OrderBy(m => m.ResetAt)
                        .FirstOrDefault();

                    var gemini = status.Models.FirstOrDefault(m => m.Name.Contains("Gemini", StringComparison.OrdinalIgnoreCase) && !m.Name.Contains("Argon"));

                    if (activeResetModel != null && activeResetModel.ResetAt.HasValue)
                    {
                        var localReset = activeResetModel.ResetAt.Value.ToLocalTime();
                        var diff = activeResetModel.ResetAt.Value - DateTime.UtcNow;
                        var remainingStr = diff.TotalHours >= 1
                            ? $"in {(int)diff.TotalHours}h {diff.Minutes:D2}m"
                            : $"in {diff.Minutes}m";

                        line1 = $"REFRESHES AT {localReset:HH:mm} ({remainingStr})";
                        line2 = gemini != null
                            ? $"Gemini: {gemini.PercentRemaining}% (5h) | {gemini.SecondaryPercentRemaining}% (Wk)"
                            : $"{activeResetModel.Name}: {activeResetModel.PercentRemaining}%";
                    }
                    else
                    {
                        line1 = "REFRESHED • ALL LIMITS READY";
                        line2 = gemini != null
                            ? $"Gemini: {gemini.PercentRemaining}% | Claude: 100%"
                            : "All Antigravity Quotas Ready";
                    }
                }

                textElements[0].AppendChild(tileXml.CreateTextNode(line1));
                textElements[1].AppendChild(tileXml.CreateTextNode(line2));
            }

            var notification = new TileNotification(tileXml);
            TileUpdateManager.CreateTileUpdaterForApplication().Update(notification);
        }
        catch { }
    }
}
