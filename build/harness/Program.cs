using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using PlayniteGameOverlay;

static class Program
{
    [STAThread]
    static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        OverlayWindow w = null;
        app.Startup += (s, e) =>
        {
            w = new OverlayWindow(new OverlaySettings());
            w.ShowOverlay();
            var t = new Thread(() =>
            {
                Thread.Sleep(2500);
                try
                {
                    IntPtr hwnd = IntPtr.Zero;
                    w.Dispatcher.Invoke(() => hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle);
                    AutomationElement root = AutomationElement.FromHandle(hwnd);
                    Console.WriteLine("WINDOW: " + root.Current.Name);
                    var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                    Console.WriteLine("BUTTONS: " + buttons.Count);
                    foreach (AutomationElement b in buttons) Console.WriteLine("  [" + b.Current.Name + "] focusable=" + b.Current.IsKeyboardFocusable);
                    Console.WriteLine("FOCUS WALK (down):");
                    for (int i = 0; i < 16; i++)
                    {
                        AutomationElement f = AutomationElement.FocusedElement;
                        Console.WriteLine("  " + i + ": " + f.Current.ControlType.ProgrammaticName.Replace("ControlType.", "") + " [" + f.Current.Name + "]");
                        w.FocusDown();
                        Thread.Sleep(150);
                    }
                }
                catch (Exception ex) { Console.WriteLine("ERR " + ex); }
                w.Dispatcher.Invoke(() => app.Shutdown());
            });
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
        };
        app.Run();
    }
}
