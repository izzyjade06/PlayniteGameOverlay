using System;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;

namespace PlayniteGameOverlay.Access
{
    /// <summary>
    /// Speaks short messages through whatever screen reader is running, using a UI Automation
    /// notification event. The API exists from .NET Framework 4.8; it is called by reflection so the
    /// plugin still loads on older runtimes, where this simply does nothing.
    /// </summary>
    public static class Speech
    {
        private static readonly MethodInfo Raise = typeof(AutomationPeer).GetMethod("RaiseNotificationEvent");

        public static void Say(UIElement source, string text)
        {
            if (source == null || string.IsNullOrWhiteSpace(text) || Raise == null) return;
            try
            {
                AutomationPeer peer = UIElementAutomationPeer.FromElement(source) ?? UIElementAutomationPeer.CreatePeerForElement(source);
                if (peer == null) return;
                ParameterInfo[] p = Raise.GetParameters();
                // kind 4 = Other, processing 1 = ImportantMostRecent
                Raise.Invoke(peer, new object[] { Enum.ToObject(p[0].ParameterType, 4), Enum.ToObject(p[1].ParameterType, 1), text, "PlayniteOverlay" });
            }
            catch
            {
                // speech is a convenience; never let it break the overlay
            }
        }
    }
}
