using System;
using System.Runtime.InteropServices;
using WinRT;

namespace AgentPulse.Widgets;

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("00000001-0000-0000-C000-000000000046")]
public interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, in Guid riid, out IntPtr ppvObject);

    [PreserveSig]
    int LockServer(bool fLock);
}

public class WidgetProviderFactory<TInterface, TImplementation> : IClassFactory
    where TImplementation : TInterface, new()
{
    private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
    private const int E_NOINTERFACE = unchecked((int)0x80004002);
    private const int S_OK = 0;

    public int CreateInstance(IntPtr pUnkOuter, in Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;

        if (pUnkOuter != IntPtr.Zero)
        {
            return CLASS_E_NOAGGREGATION;
        }

        try
        {
            var impl = new TImplementation();
            ppvObject = MarshalInspectable<TInterface>.FromManaged(impl);
            return S_OK;
        }
        catch
        {
            return E_NOINTERFACE;
        }
    }

    public int LockServer(bool fLock) => S_OK;
}
