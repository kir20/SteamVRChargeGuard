using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ChargeGuard
{
    /// <summary>Just enough of the Windows HID API to find dongles and read their input reports.</summary>
    static class Hid
    {
        public sealed class Device
        {
            public string Path;
            public ushort VendorId, ProductId;
            public string Serial;
        }

        const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_READ = 0x80000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID, ProductID, VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                          NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                          NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);
        [DllImport("hid.dll")] static extern bool HidD_GetSerialNumberString(SafeFileHandle device, byte[] buffer, int length);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, int index,
                                                       ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail,
                                                           int detailSize, out int requiredSize, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
                                                uint disposition, uint flags, IntPtr template);

        public static List<Device> Enumerate(ushort vendorId)
        {
            var devices = new List<Device>();
            HidD_GetHidGuid(out Guid guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) return devices;
            try
            {
                for (int i = 0; ; i++)
                {
                    var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                    if (size <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(size);
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA_W: DWORD cbSize; WCHAR DevicePath[1]
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero)) continue;
                        Device device = Describe(Marshal.PtrToStringUni(detail + 4));
                        if (device != null && device.VendorId == vendorId) devices.Add(device);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return devices;
        }

        static Device Describe(string path)
        {
            // No access rights needed for attributes and strings, so this works even while SteamVR has it open.
            using (SafeFileHandle handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) return null;
                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                if (!HidD_GetAttributes(handle, ref attributes)) return null;
                var buffer = new byte[512];
                string serial = HidD_GetSerialNumberString(handle, buffer, buffer.Length)
                    ? Encoding.Unicode.GetString(buffer).Split('\0')[0]
                    : "";
                return new Device { Path = path, VendorId = attributes.VendorID, ProductId = attributes.ProductID, Serial = serial };
            }
        }

        /// <summary>Opens a device for reading input reports. Each Read returns one report, report ID first.</summary>
        public static FileStream OpenForReading(string path, out int reportLength)
        {
            SafeFileHandle handle = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) throw new IOException("CreateFile failed, error " + Marshal.GetLastWin32Error());
            reportLength = 65;
            if (HidD_GetPreparsedData(handle, out IntPtr preparsed))
            {
                try
                {
                    if (HidP_GetCaps(preparsed, out HIDP_CAPS caps) == HIDP_STATUS_SUCCESS && caps.InputReportByteLength > 0)
                        reportLength = caps.InputReportByteLength;
                }
                finally
                {
                    HidD_FreePreparsedData(preparsed);
                }
            }
            return new FileStream(handle, FileAccess.Read, 1, false);
        }
    }
}
