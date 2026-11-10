using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SpectreEX.Setup
{
    // The Windows Explorer "Select Folder" dialog (IFileOpenDialog with FOS_PICKFOLDERS), called through
    // its COM vtable by hand so it works under any scripting backend. It runs on its own STA thread, so
    // the game keeps drawing while it's open; Pick hands the chosen folder (or null) to a callback on
    // that thread.
    public static class FolderPicker
    {
        public static bool Supported => Environment.OSVersion.Platform == PlatformID.Win32NT;

        public static void Pick(string title, string startFolder, Action<string> done)
        {
            IntPtr owner = Supported ? GetActiveWindow() : IntPtr.Zero;
            var t = new Thread(() =>
            {
                string result = null;
                try { result = Show(owner, title, startFolder); }
                catch (Exception e) { UnityEngine.Debug.LogWarning("Spectre VR-EX: folder picker failed: " + e.Message); }
                done(result);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        static readonly Guid ClsidFileOpenDialog = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
        static readonly Guid IidFileOpenDialog = new Guid("D57C7288-D4AD-4768-BE02-9D969532D960");
        static readonly Guid IidShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        const uint FosPickFolders = 0x20, FosForceFileSystem = 0x40, FosPathMustExist = 0x800;
        const uint SigdnFileSysPath = 0x80058000;
        const int ErrorCancelled = unchecked((int)0x800704C7);

        // vtable slots: IUnknown 0-2, IModalWindow.Show 3, IFileDialog from 4
        const int SlotRelease = 2, SlotShow = 3, SlotSetOptions = 9, SlotGetOptions = 10, SlotSetFolder = 12,
                  SlotSetTitle = 17, SlotGetResult = 20, SlotGetDisplayName = 5;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ShowFn(IntPtr self, IntPtr owner);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetOptionsFn(IntPtr self, uint options);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetOptionsFn(IntPtr self, out uint options);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetFolderFn(IntPtr self, IntPtr item);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] delegate int SetTitleFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string title);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetResultFn(IntPtr self, out IntPtr item);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDisplayNameFn(IntPtr self, uint kind, out IntPtr name);

        static T Slot<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size), typeof(T)) as T;
        }

        static void Release(IntPtr obj)
        {
            if (obj != IntPtr.Zero) Slot<ReleaseFn>(obj, SlotRelease)(obj);
        }

        static string Show(IntPtr owner, string title, string startFolder)
        {
            CoInitializeEx(IntPtr.Zero, 2);   // COINIT_APARTMENTTHREADED
            IntPtr dialog = IntPtr.Zero, start = IntPtr.Zero, item = IntPtr.Zero;
            try
            {
                Guid clsid = ClsidFileOpenDialog, iid = IidFileOpenDialog;
                Check(CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out dialog));
                Check(Slot<GetOptionsFn>(dialog, SlotGetOptions)(dialog, out uint options));
                Check(Slot<SetOptionsFn>(dialog, SlotSetOptions)(dialog, options | FosPickFolders | FosForceFileSystem | FosPathMustExist));
                if (!string.IsNullOrEmpty(title)) Slot<SetTitleFn>(dialog, SlotSetTitle)(dialog, title);
                if (!string.IsNullOrEmpty(startFolder) && System.IO.Directory.Exists(startFolder))
                {
                    Guid shellItem = IidShellItem;
                    if (SHCreateItemFromParsingName(startFolder, IntPtr.Zero, ref shellItem, out start) == 0)
                        Slot<SetFolderFn>(dialog, SlotSetFolder)(dialog, start);
                }
                int hr = Slot<ShowFn>(dialog, SlotShow)(dialog, owner);
                if (hr == ErrorCancelled) return null;
                Check(hr);
                Check(Slot<GetResultFn>(dialog, SlotGetResult)(dialog, out item));
                Check(Slot<GetDisplayNameFn>(item, SlotGetDisplayName)(item, SigdnFileSysPath, out IntPtr name));
                try { return Marshal.PtrToStringUni(name); }
                finally { CoTaskMemFree(name); }
            }
            finally
            {
                Release(item);
                Release(start);
                Release(dialog);
                CoUninitialize();
            }
        }

        static void Check(int hr)
        {
            if (hr < 0) throw new COMException("Folder dialog error", hr);
        }

        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint init);
        [DllImport("ole32.dll")] static extern void CoUninitialize();
        [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr obj);
        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr bindContext, ref Guid iid, out IntPtr item);
    }
}
