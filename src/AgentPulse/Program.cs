using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using AgentPulse.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace AgentPulse;

public static class Program
{
    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoRegisterClassObject(
        in Guid rclsid,
        [MarshalAs(UnmanagedType.Interface)] IClassFactory pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoRevokeClassObject(uint dwRegister);

    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 1;
    private static readonly Guid WidgetProviderClsid = new("8A1C5F22-5B20-4F71-B88E-3298246AE690");

    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        bool isComServer = args.Any(a => a.Contains("RegisterProcessAsComServer", StringComparison.OrdinalIgnoreCase));

        // Register Widget Provider COM Class Factory
        uint cookie = 0;
        try
        {
            var factory = new WidgetProviderFactory<IWidgetProvider, QuotaWidgetProvider>();
            CoRegisterClassObject(in WidgetProviderClsid, factory, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, out cookie);
        }
        catch { }

        if (isComServer)
        {
            // The process was started by the Windows Widget Board
            var exitEvent = new ManualResetEvent(false);
            exitEvent.WaitOne();

            if (cookie != 0)
            {
                CoRevokeClassObject(cookie);
            }
        }
        else
        {
            // Standard GUI launch via WinUI 3 XAML runtime
            global::Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });

            if (cookie != 0)
            {
                CoRevokeClassObject(cookie);
            }
        }
    }
}
