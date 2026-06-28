using System.Runtime.InteropServices;

[ComImport, Guid("1F02B6C5-7842-4EE6-8A0B-9A24183A95CA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfInputProcessorProfiles
{
    void Register(ref Guid rclsid);
    void Unregister(ref Guid rclsid);
    void AddLanguageProfile(ref Guid rclsid, ushort langid, ref Guid profile, string desc, uint cchDesc, string icon, uint cchFile, uint iconIndex);
    void RemoveLanguageProfile(ref Guid rclsid, ushort langid, ref Guid profile);
    void EnumInputProcessorInfo(out IntPtr enumGuid);
    void GetDefaultLanguageProfile(ushort langid, ref Guid catid, out Guid clsid, out Guid profile);
    void SetDefaultLanguageProfile(ushort langid, ref Guid clsid, ref Guid profile);
    void ActivateLanguageProfile(ref Guid clsid, ushort langid, ref Guid profile);
    void GetActiveLanguageProfile(ref Guid clsid, out ushort langid, out Guid profile);
}

[ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfInputProcessorProfileMgr
{
    void ActivateProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, uint dwFlags);
}

internal static class TsfNative
{
    [DllImport("msctf.dll")]
    internal static extern int TF_CreateInputProcessorProfiles(out IntPtr profiles);

    [DllImport("msctf.dll")]
    internal static extern int TF_CreateThreadMgr(out IntPtr ppTim);

    [DllImport("imm32.dll")]
    internal static extern bool ImmSimulateHotKey(IntPtr hWnd, int dwHotKeyID);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();
}

[ComImport, Guid("AA80E80D-2021-11D2-93E0-0060B067B86E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfThreadMgr
{
    [PreserveSig] int Activate(out int ptid);
    [PreserveSig] int Deactivate();
    [PreserveSig] int CreateDocumentMgr(out IntPtr ppDim);
    [PreserveSig] int EnumDocumentMgrs(out IntPtr ppEnum);
    [PreserveSig] int GetFocus(out IntPtr ppDim);
    [PreserveSig] int SetFocus(IntPtr pdim);
    [PreserveSig] int AssociateFocus(IntPtr hwnd, IntPtr pdimPrev, out IntPtr ppdimNew);
    [PreserveSig] int IsThreadFocus([MarshalAs(UnmanagedType.Bool)] out bool pfThreadFocus);
    [PreserveSig] int GetFunctionProvider(ref Guid clsid, out IntPtr ppFuncProv);
    [PreserveSig] int EnumFunctionProviders(out IntPtr ppEnum);
    [PreserveSig] int GetGlobalCompartment(out IntPtr ppCompMgr);
}

[ComImport, Guid("7DCF57AC-18AD-438B-824D-979BFFB74B7C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfCompartmentMgr
{
    [PreserveSig] int GetCompartment(ref Guid rguidComp, out IntPtr ppComp);
    [PreserveSig] int ClearCompartment(int tid, ref Guid rguidComp);
    [PreserveSig] int EnumCompartments(out IntPtr ppEnum);
}

[ComImport, Guid("BB08F7A9-607A-4384-8623-056892B64371"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfCompartment
{
    [PreserveSig] int GetValue(out int pvarValue);
    [PreserveSig] int SetValue(int tid, ref int pvarValue);
}

internal static class Program
{
    private static readonly Guid GuidCompartmentKeyboardOpenclose = new("D441A8BC-3D1C-4280-86FB-0EF243A5EBB8");

    private static int Main(string[] args)
    {
        ActivateWeaselProfile();
        return 0;
    }

    private static void ActivateWeaselProfile()
    {
        const ushort ChineseLang = 0x0804;
        Guid managerClsid = new("33C53A50-F456-4884-B049-85FD643ECFED");
        Guid clsid = new("A3F4CDED-B1E9-41EE-9CA6-7B4D0DE6CB0A");
        Guid profile = new("3D02CAB6-2B8E-4781-BA20-1C9267529467");

        try
        {
            object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(managerClsid)!)!;
            ITfInputProcessorProfileMgr manager = (ITfInputProcessorProfileMgr)instance;
            try
            {
                manager.ActivateProfile(
                    0x0001,
                    ChineseLang,
                    ref clsid,
                    ref profile,
                    IntPtr.Zero,
                    0x20000000 | 0x00000001 | 0x00000004);
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException or InvalidCastException)
            {
                System.Diagnostics.Debug.WriteLine($"[Activator] ActivateProfile failed: {ex.Message}");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or InvalidCastException)
        {
            System.Diagnostics.Debug.WriteLine($"[Activator] TSF ThreadMgr activate failed: {ex.Message}");
        }

        IntPtr profilesPointer = IntPtr.Zero;
        int hr = TsfNative.TF_CreateInputProcessorProfiles(out profilesPointer);
        if (hr == 0 && profilesPointer != IntPtr.Zero)
        {
            try
            {
                ITfInputProcessorProfiles profiles =
                    (ITfInputProcessorProfiles)Marshal.GetTypedObjectForIUnknown(profilesPointer, typeof(ITfInputProcessorProfiles));
                try
                {
                    profiles.ActivateLanguageProfile(ref clsid, ChineseLang, ref profile);
                }
                catch (Exception ex) when (ex is InvalidOperationException or COMException or InvalidCastException)
                {
                    System.Diagnostics.Debug.WriteLine($"[Activator] ActivateLanguageProfile failed: {ex.Message}");
                }
            }
            finally
            {
                Marshal.Release(profilesPointer);
            }
        }

        bool imeOpen = false;
        bool imeForcedOpen = false;
        try
        {
            IntPtr ptm = IntPtr.Zero;
            hr = TsfNative.TF_CreateThreadMgr(out ptm);
            if (hr == 0 && ptm != IntPtr.Zero)
            {
                ITfThreadMgr threadMgr = (ITfThreadMgr)Marshal.GetTypedObjectForIUnknown(ptm, typeof(ITfThreadMgr));
                threadMgr.Activate(out _);

                IntPtr pCompMgr = IntPtr.Zero;
                hr = threadMgr.GetGlobalCompartment(out pCompMgr);
                if (hr == 0 && pCompMgr != IntPtr.Zero)
                {
                    ITfCompartmentMgr compMgr = (ITfCompartmentMgr)Marshal.GetTypedObjectForIUnknown(pCompMgr, typeof(ITfCompartmentMgr));
                    IntPtr pComp = IntPtr.Zero;
                    Guid openGuid = GuidCompartmentKeyboardOpenclose;
                    hr = compMgr.GetCompartment(ref openGuid, out pComp);
                    if (hr == 0 && pComp != IntPtr.Zero)
                    {
                        ITfCompartment comp = (ITfCompartment)Marshal.GetTypedObjectForIUnknown(pComp, typeof(ITfCompartment));
                        int val = 0;
                        hr = comp.GetValue(out val);
                        if (hr == 0)
                        {
                            imeOpen = val != 0;
                        }
                        Marshal.Release(pComp);
                    }
                    Marshal.Release(pCompMgr);
                }
                threadMgr.Deactivate();
                Marshal.Release(ptm);
            }

            if (!imeOpen)
            {
                IntPtr fg = TsfNative.GetForegroundWindow();
                imeForcedOpen = TsfNative.ImmSimulateHotKey(fg, 0x10);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or InvalidCastException)
        {
            System.Diagnostics.Debug.WriteLine($"[Activator] IME state detection failed: {ex.Message}");
        }

        Console.WriteLine($"{{");
        Console.WriteLine($"  \"activated\": true,");
        Console.WriteLine($"  \"ime_open\": {imeOpen.ToString().ToLowerInvariant()},");
        Console.WriteLine($"  \"ime_forced_open\": {imeForcedOpen.ToString().ToLowerInvariant()}");
        Console.WriteLine($"}}");
    }
}
