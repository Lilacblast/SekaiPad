// SPDX-License-Identifier: MIT
// Portions adapted from emoacht/RawInput.Touchpad (Copyright (c) 2021 emoacht).
// https://github.com/emoacht/RawInput.Touchpad
// Original source revision: 38a9e68c1c42cb19bae02fe84ee88b2a4b2a8639
// Upstream license: licenses/RawInput.Touchpad.MIT.
// SekaiPad modifications are not attributed to the upstream author.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RawInput.Touchpad
{
	internal static class TouchpadHelper
	{
		#region Win32

		[DllImport("User32", SetLastError = true)]
		private static extern uint GetRawInputDeviceList(
			[Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList,
			ref uint puiNumDevices,
			uint cbSize);

		[StructLayout(LayoutKind.Sequential)]
		private struct RAWINPUTDEVICELIST
		{
			public IntPtr hDevice;
			public uint dwType; // RIM_TYPEMOUSE or RIM_TYPEKEYBOARD or RIM_TYPEHID
		}

		private const uint RIM_TYPEMOUSE = 0;
		private const uint RIM_TYPEKEYBOARD = 1;
		private const uint RIM_TYPEHID = 2;

		[DllImport("User32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool RegisterRawInputDevices(
			RAWINPUTDEVICE[] pRawInputDevices,
			uint uiNumDevices,
			uint cbSize);

		[StructLayout(LayoutKind.Sequential)]
		private struct RAWINPUTDEVICE
		{
			public ushort usUsagePage;
			public ushort usUsage;
			public uint dwFlags; // RIDEV_INPUTSINK
			public IntPtr hwndTarget;
		}

		private const uint RIDEV_INPUTSINK = 0x00000100;

		[DllImport("User32.dll", SetLastError = true)]
		private static extern uint GetRawInputData(
			IntPtr hRawInput, // lParam in WM_INPUT
			uint uiCommand, // RID_HEADER
			IntPtr pData,
			ref uint pcbSize,
			uint cbSizeHeader);

		private const uint RID_INPUT = 0x10000003;

		[StructLayout(LayoutKind.Sequential)]
		private struct RAWINPUT
		{
			public RAWINPUTHEADER Header;
			public RAWHID Hid;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RAWINPUTHEADER
		{
			public uint dwType; // RIM_TYPEMOUSE or RIM_TYPEKEYBOARD or RIM_TYPEHID
			public uint dwSize;
			public IntPtr hDevice;
			public IntPtr wParam; // wParam in WM_INPUT
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RAWHID
		{
			public uint dwSizeHid;
			public uint dwCount;
			public IntPtr bRawData; // This is not for use.
		}

		[DllImport("User32.dll", SetLastError = true)]
		private static extern uint GetRawInputDeviceInfo(
			IntPtr hDevice, // hDevice by RAWINPUTHEADER
			uint uiCommand, // RIDI_PREPARSEDDATA
			IntPtr pData,
			ref uint pcbSize);

		[DllImport("User32.dll", SetLastError = true)]
		private static extern uint GetRawInputDeviceInfo(
			IntPtr hDevice, // hDevice by RAWINPUTDEVICELIST
			uint uiCommand, // RIDI_DEVICEINFO
			ref RID_DEVICE_INFO pData,
			ref uint pcbSize);

		private const uint RIDI_PREPARSEDDATA = 0x20000005;
		private const uint RIDI_DEVICEINFO = 0x2000000b;

		[StructLayout(LayoutKind.Sequential)]
		private struct RID_DEVICE_INFO
		{
			public uint cbSize; // This is determined to accommodate RID_DEVICE_INFO_KEYBOARD.
			public uint dwType;
			public RID_DEVICE_INFO_HID hid;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RID_DEVICE_INFO_HID
		{
			public uint dwVendorId;
			public uint dwProductId;
			public uint dwVersionNumber;
			public ushort usUsagePage;
			public ushort usUsage;
		}

		[DllImport("Hid.dll", SetLastError = true)]
		private static extern uint HidP_GetCaps(
			IntPtr PreparsedData,
			out HIDP_CAPS Capabilities);

		private const uint HIDP_STATUS_SUCCESS = 0x00110000;

		[StructLayout(LayoutKind.Sequential)]
		private struct HIDP_CAPS
		{
			public ushort Usage;
			public ushort UsagePage;
			public ushort InputReportByteLength;
			public ushort OutputReportByteLength;
			public ushort FeatureReportByteLength;

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
			public ushort[] Reserved;

			public ushort NumberLinkCollectionNodes;
			public ushort NumberInputButtonCaps;
			public ushort NumberInputValueCaps;
			public ushort NumberInputDataIndices;
			public ushort NumberOutputButtonCaps;
			public ushort NumberOutputValueCaps;
			public ushort NumberOutputDataIndices;
			public ushort NumberFeatureButtonCaps;
			public ushort NumberFeatureValueCaps;
			public ushort NumberFeatureDataIndices;
		}

		[DllImport("Hid.dll", CharSet = CharSet.Auto)]
		private static extern uint HidP_GetValueCaps(
			HIDP_REPORT_TYPE ReportType,
			[Out] HIDP_VALUE_CAPS[] ValueCaps,
			ref ushort ValueCapsLength,
			IntPtr PreparsedData);

		private enum HIDP_REPORT_TYPE
		{
			HidP_Input,
			HidP_Output,
			HidP_Feature
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct HIDP_VALUE_CAPS
		{
			public ushort UsagePage;
			public byte ReportID;

			[MarshalAs(UnmanagedType.U1)]
			public bool IsAlias;

			public ushort BitField;
			public ushort LinkCollection;
			public ushort LinkUsage;
			public ushort LinkUsagePage;

			[MarshalAs(UnmanagedType.U1)]
			public bool IsRange;
			[MarshalAs(UnmanagedType.U1)]
			public bool IsStringRange;
			[MarshalAs(UnmanagedType.U1)]
			public bool IsDesignatorRange;
			[MarshalAs(UnmanagedType.U1)]
			public bool IsAbsolute;
			[MarshalAs(UnmanagedType.U1)]
			public bool HasNull;

			public byte Reserved;
			public ushort BitSize;
			public ushort ReportCount;

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
			public ushort[] Reserved2;

			public uint UnitsExp;
			public uint Units;
			public int LogicalMin;
			public int LogicalMax;
			public int PhysicalMin;
			public int PhysicalMax;

			// Range
			public ushort UsageMin;
			public ushort UsageMax;
			public ushort StringMin;
			public ushort StringMax;
			public ushort DesignatorMin;
			public ushort DesignatorMax;
			public ushort DataIndexMin;
			public ushort DataIndexMax;

			// NotRange
			public ushort Usage => UsageMin;
			// ushort Reserved1;
			public ushort StringIndex => StringMin;
			// ushort Reserved2;
			public ushort DesignatorIndex => DesignatorMin;
			// ushort Reserved3;
			public ushort DataIndex => DataIndexMin;
			// ushort Reserved4;
		}

		[DllImport("Hid.dll", CharSet = CharSet.Auto)]
		private static extern uint HidP_GetUsageValue(
			HIDP_REPORT_TYPE ReportType,
			ushort UsagePage,
			ushort LinkCollection,
			ushort Usage,
			out uint UsageValue,
			IntPtr PreparsedData,
			IntPtr Report,
			uint ReportLength);

		[DllImport("Hid.dll")]
		private static extern uint HidP_GetUsages(
			HIDP_REPORT_TYPE ReportType,
			ushort UsagePage,
			ushort LinkCollection,
			[Out] ushort[] UsageList,
			ref uint UsageLength,
			IntPtr PreparsedData,
			IntPtr Report,
			uint ReportLength);

		#endregion

		public static bool Exists()
		{
			uint deviceListCount = 0;
			uint rawInputDeviceListSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();

			if (GetRawInputDeviceList(
				null,
				ref deviceListCount,
				rawInputDeviceListSize) != 0)
			{
				return false;
			}

			var devices = new RAWINPUTDEVICELIST[deviceListCount];

			if (GetRawInputDeviceList(
				devices,
				ref deviceListCount,
				rawInputDeviceListSize) != deviceListCount)
			{
				return false;
			}

			foreach (var device in devices.Where(x => x.dwType == RIM_TYPEHID))
			{
				uint deviceInfoSize = 0;

				if (GetRawInputDeviceInfo(
					device.hDevice,
					RIDI_DEVICEINFO,
					IntPtr.Zero,
					ref deviceInfoSize) != 0)
				{
					continue;
				}

				var deviceInfo = new RID_DEVICE_INFO { cbSize = deviceInfoSize };

				if (GetRawInputDeviceInfo(
					device.hDevice,
					RIDI_DEVICEINFO,
					ref deviceInfo,
					ref deviceInfoSize) == unchecked((uint)-1))
				{
					continue;
				}

				if ((deviceInfo.hid.usUsagePage == 0x000D) &&
					(deviceInfo.hid.usUsage == 0x0005))
				{
					return true;
				}
			}
			return false;
		}

		public static bool RegisterInput(IntPtr windowHandle)
		{
			// Precision Touchpad (PTP) in HID Clients Supported in Windows
			// https://docs.microsoft.com/en-us/windows-hardware/drivers/hid/hid-architecture#hid-clients-supported-in-windows
			var device = new RAWINPUTDEVICE
			{
				usUsagePage = 0x000D,
				usUsage = 0x0005,
				dwFlags = RIDEV_INPUTSINK, // receive while emulator is foreground
				hwndTarget = windowHandle
			};

			return RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
		}

		public const int WM_INPUT = 0x00FF;
		public const int RIM_INPUT = 0;
		public const int RIM_INPUTSINK = 1;

		public static int MinX { get; private set; } = 0;
		public static int MaxX { get; private set; } = 4095;
		public static int MinY { get; private set; } = 0;
		public static int MaxY { get; private set; } = 4095;

		private static readonly TouchpadFrameAssembler FrameAssembler = new();
		public static long ValidHidReportCount { get; private set; }
		public static long CompleteFrameCount { get; private set; }
		public static bool AwaitingContinuation => FrameAssembler.AwaitingContinuation;
		public static void ResetPendingReports() => FrameAssembler.Reset();

		/// <summary>
		/// Parse each native HID report separately. A single WM_INPUT can contain
		/// multiple 12-byte reports, and this device emits one contact per report.
		/// The assembler outputs only complete parallel/hybrid frames; no partial
		/// frame may be interpreted as an implicit finger release.
		/// </summary>
		public static IReadOnlyList<TouchpadContact[]> ParseFrames(IntPtr lParam)
		{
			var completed = new List<TouchpadContact[]>();
			uint rawSize = 0;
			uint headerBytes = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
			if (GetRawInputData(lParam, RID_INPUT, IntPtr.Zero,
				ref rawSize, headerBytes) != 0 ||
				rawSize < headerBytes + 8 || rawSize > 1024 * 1024)
				return completed;

			IntPtr rawPtr = IntPtr.Zero;
			IntPtr capsPtr = IntPtr.Zero;
			try
			{
				rawPtr = Marshal.AllocHGlobal((int)rawSize);
				if (GetRawInputData(lParam, RID_INPUT, rawPtr,
					ref rawSize, headerBytes) != rawSize)
					return completed;
				var header = Marshal.PtrToStructure<RAWINPUTHEADER>(rawPtr);
				if (header.dwType != RIM_TYPEHID) return completed;
				int offset = (int)headerBytes + 8; // RAWHID: dwSizeHid, dwCount, bRawData[]
				uint reportSize = (uint)Marshal.ReadInt32(rawPtr, (int)headerBytes);
				uint reportCount = (uint)Marshal.ReadInt32(rawPtr, (int)headerBytes + 4);
				if (reportSize == 0 || reportSize > 4096 ||
					reportCount == 0 || reportCount > 128 ||
					(long)offset + (long)reportSize * reportCount > rawSize)
					return completed;

				uint preparsedBytes = 0;
				if (GetRawInputDeviceInfo(header.hDevice, RIDI_PREPARSEDDATA,
					IntPtr.Zero, ref preparsedBytes) != 0 ||
					preparsedBytes == 0 || preparsedBytes > 1024 * 1024)
					return completed;
				capsPtr = Marshal.AllocHGlobal((int)preparsedBytes);
				if (GetRawInputDeviceInfo(header.hDevice, RIDI_PREPARSEDDATA,
					capsPtr, ref preparsedBytes) != preparsedBytes ||
					HidP_GetCaps(capsPtr, out HIDP_CAPS caps) != HIDP_STATUS_SUCCESS ||
					caps.UsagePage != 0x0D || caps.Usage != 0x05 ||
					caps.InputReportByteLength != reportSize)
					return completed;

				ushort valuesLength = caps.NumberInputValueCaps;
				if (valuesLength == 0 || valuesLength > 1024) return completed;
				var values = new HIDP_VALUE_CAPS[valuesLength];
				if (HidP_GetValueCaps(HIDP_REPORT_TYPE.HidP_Input,
					values, ref valuesLength, capsPtr) != HIDP_STATUS_SUCCESS)
					return completed;

				var contactCollections = values.Take(valuesLength)
					.Where(c => c.LinkCollection != 0)
					.GroupBy(c => c.LinkCollection)
					.ToArray();

				for (int index = 0; index < (int)reportCount; ++index)
				{
					var reportPtr = IntPtr.Add(rawPtr, offset + index * (int)reportSize);
					byte reportId = Marshal.ReadByte(reportPtr);
					bool ReadValue(ushort page, ushort link, ushort usage, out uint result)
					{
						result = 0;
						return HidP_GetUsageValue(HIDP_REPORT_TYPE.HidP_Input,
							page, link, usage, out result,
							capsPtr, reportPtr, reportSize) == HIDP_STATUS_SUCCESS;
					}

					if (!ReadValue(0x0D, 0, 0x54, out uint count))
						continue; // Not a PTP contact frame
					ReadValue(0x0D, 0, 0x56, out uint scanTime);
					var sampleList = new List<TouchpadHidSample>();
					foreach (var coll in contactCollections)
					{
						// Do not read an unrelated HID report ID's field definitions.
						bool Compatible(ushort page, ushort usage) =>
							coll.Any(v => v.UsagePage == page && v.Usage == usage &&
								(v.ReportID == 0 || v.ReportID == reportId));
						ushort link = coll.Key;
						if (!Compatible(0x0D, 0x51) ||
							!Compatible(0x01, 0x30) || !Compatible(0x01, 0x31) ||
							!ReadValue(0x0D, link, 0x51, out uint id) ||
							!ReadValue(0x01, link, 0x30, out uint x) ||
							!ReadValue(0x01, link, 0x31, out uint y))
							continue;

						var xCap = coll.First(c => c.UsagePage == 0x01 && c.Usage == 0x30);
						var yCap = coll.First(c => c.UsagePage == 0x01 && c.Usage == 0x31);
						if (xCap.LogicalMax > xCap.LogicalMin)
						{ MinX = xCap.LogicalMin; MaxX = xCap.LogicalMax; }
						if (yCap.LogicalMax > yCap.LogicalMin)
						{ MinY = yCap.LogicalMin; MaxY = yCap.LogicalMax; }

						// Tip Switch (0x0D/0x42) is a BUTTON usage and cannot
						// be decoded via HidP_GetUsageValue (value caps).
						ushort[] pressedUsages = new ushort[16];
						uint pressedCount = (uint)pressedUsages.Length;
						if (HidP_GetUsages(HIDP_REPORT_TYPE.HidP_Input,
							0x0D, link, pressedUsages, ref pressedCount,
							capsPtr, reportPtr, reportSize) != HIDP_STATUS_SUCCESS ||
							pressedCount > pressedUsages.Length)
							continue; // Fail closed when TIP is unparseable.
						bool down = pressedUsages.Take((int)pressedCount).Contains((ushort)0x42);
						sampleList.Add(new TouchpadHidSample((int)id, (int)x, (int)y, down));
					}
					ValidHidReportCount++;
					var frame = FrameAssembler.Accept(count, scanTime, sampleList);
					if (frame != null)
					{
						CompleteFrameCount++;
						completed.Add(frame);
					}
				}
				return completed;
			}
			finally
			{
				if (capsPtr != IntPtr.Zero) Marshal.FreeHGlobal(capsPtr);
				if (rawPtr != IntPtr.Zero) Marshal.FreeHGlobal(rawPtr);
			}
		}

	}
}