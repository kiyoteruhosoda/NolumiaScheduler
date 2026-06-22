using NolumiaScheduler.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace NolumiaScheduler.WinUI.Helpers;

internal static class EventLocation
{
    internal static void Open(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return;

        try
        {
            Process.Start(new ProcessStartInfo(location) { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", location));
            }
            catch
            {
                // Silently ignore if location cannot be opened
            }
        }
    }
}
