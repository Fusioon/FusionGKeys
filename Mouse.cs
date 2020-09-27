using Device.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Fusion.GKeys
{
    public enum EMouseModel
    {
        Unknown,
        G903
    }

    public enum EPowerState
    {
        Unknown,
        OnBattery,  // not plugged in - discharging
        Charging,   // plugged in - charging
        Charged     // plugged in - fully charged
    }

    enum EReportId : byte
    {
        Short = 0x10,
        Long = 0x11
    }

    [StructLayout(LayoutKind.Explicit)]
    unsafe struct hidpp20_message
    {
        [FieldOffset(0)] public fixed byte data[20];
        [FieldOffset(0)] public byte report_id;
        [FieldOffset(1)] public byte device_idx;
        [FieldOffset(2)] public byte sub_id;
        [FieldOffset(3)] public byte address;
        [FieldOffset(4)] public fixed byte parameters[16];
        [FieldOffset(4)] public byte param0;
        [FieldOffset(5)] public byte param1;
        [FieldOffset(6)] public byte param2;
    }

    struct hidpp20_feature
    {
        public ushort feature;
        public byte type;
    };

    public enum EHidppPage : ushort
    {
        Root = 0x0000,
        FeatureSet = 0x0001,
        DeviceInfo = 0x0003,
        DeviceName = 0x0005,
        Reset = 0x0020,
        BatteryLevelStatus = 0x1000,
        BatteryVoltage = 0x1001,
        LedSWControl = 0x1300,
        WirelessDeviceStatus = 0x1D4B,
        AdjustableDPI = 0x2201,
        AdjustableReportRate = 0x8060,
        ColorLedEffects = 0x8070,
        RGBEffects = 0x8071,
        OnboardProfiles = 0x8100,
        MouseButtonSpy = 0x8110

    }

    class Mouse : IDisposable
    {
        const byte k_ShortMessage = 0x10;
        const int k_ShortMessageLength = 7;

        const byte k_LongMessage = 0x11;
        const int k_LongMessageLength = 20;

        // Logitech Vendor ID
        const int k_VendorId = 0x046d;

        const byte k_DeviceReceiver = 0xFF;
        const uint k_DevicesMax = 6;

        static readonly Dictionary<uint, EMouseModel> s_SupportedModels = new Dictionary<uint, EMouseModel>()
        {
            { 0xC539, EMouseModel.G903 },   // Wireless dongle
            { 0xC086, EMouseModel.G903 },   // Mouse when connected with wire
        };

        static readonly HashSet<uint> s_PriorityDevice = new HashSet<uint>()
        {
            0xC086,                         // G903 Wired connection
        };

        static readonly HashSet<uint> s_WirelessConnection = new HashSet<uint>()
        {
            0xC539                          // G903 Wireless dongle
        };

        static readonly HashSet<EMouseModel> s_WirelessModels = new HashSet<EMouseModel>
        {
            EMouseModel.G903
        };

        static unsafe string ParamsToString(hidpp20_message* msg)
        {
            unsafe
            {
                int length = msg->report_id == k_LongMessage ? k_LongMessageLength : k_ShortMessageLength;
                length -= 4;
                Span<char> buffer = new char[length * 3];
                for (int i = 0; i < length; i++)
                {
                    var str_idx = i * 3;
                    var hex = msg->parameters[i].ToString("X2");
                    buffer[str_idx] = hex[0];
                    buffer[str_idx + 1] = hex[1];
                    buffer[str_idx + 2] = ' ';
                }
                return new string(buffer);
            }
        }

        static string ParamsToString(in hidpp20_message msg)
        {
            unsafe
            {
                fixed (hidpp20_message* pMsg = &msg)
                {
                    return ParamsToString(pMsg);
                }
            }
        }

        static unsafe void PrintMessage(hidpp20_message* msg, [CallerMemberName] string CallerName = "")
        {
            Debug.Info(@$"{CallerName}
                        report_id: {msg->report_id:X2}
                        device_id: {msg->device_idx:X2}
                        sub_id:    {msg->sub_id:X2}
                        address:   {msg->address:X2}
                        params:    {ParamsToString(msg)}

                    ");
        }

        static void PrintMessage(in hidpp20_message msg, [CallerMemberName] string CallerName = "")
        {
            unsafe
            {
                fixed (hidpp20_message* pMsg = &msg)
                    PrintMessage(pMsg, CallerName);
            }
        }

        static void PrintMessage(byte[] bytes, [CallerMemberName] string CallerName = "")
        {
            if (bytes.Length == 7 || bytes.Length == 20)
            {
                unsafe
                {
                    hidpp20_message msg;
                    Marshal.Copy(bytes, 0, new IntPtr(&msg), bytes.Length);
                    PrintMessage(msg, CallerName);
                }
            }
        }

        static string ByteArrayToString(byte[] bytes)
        {
            return string.Concat(Array.ConvertAll(bytes, x => ($"{x:X2}")));
        }

        public string Name { get; private set; }
        public EMouseModel Model { get; }
        public bool IsWireless { get; }

        IDevice device7;
        IDevice device20;

        byte deviceId;
        byte versionMajor;
        byte versionMinor;
        Dictionary<EHidppPage, (int, hidpp20_feature)> features;

        Dictionary<EReportId, Task<ReadResult>> devicesReadTask = new Dictionary<EReportId, Task<ReadResult>>();

        public Action<Mouse, uint, float, EPowerState> OnBatteryChanged;
        public Action<Mouse, ushort> OnButtonChanged;

        VoltageMap voltagePercentMap = new VoltageMap();
        Task<ReadResult> CreateReadTask(EReportId expReportId)
        {
            IDevice device;
            switch (expReportId)
            {
                case EReportId.Short:
                    device = device7;
                    break;
                case EReportId.Long:
                    device = device20;
                    break;
                default:
                    return null;
            }

            if (devicesReadTask.TryGetValue(expReportId, out Task<ReadResult> task))
            {
                if (!task.IsCompleted) return task;
            }

            return devicesReadTask[expReportId] = device.ReadAsync();
        }


        async Task<ReadResult?> Read(EReportId expReportId)
        {
            try
            {
                if (expReportId == 0x00) // read any message
                {
                    return await await Task.WhenAny(CreateReadTask(EReportId.Short), CreateReadTask(EReportId.Long));
                }

                return await CreateReadTask(expReportId);
            }
            catch (IOException)
            {
                device20 = null;
            }
            return null;
        }

        async Task<(uint, hidpp20_message msg)> ReadMessageTimeout(EReportId expReportId, TimeSpan timeoutTime)
        {
            var readTask = Read(expReportId);
            
            if (readTask.Wait(timeoutTime) && await readTask is ReadResult result)
            {
                hidpp20_message msg;
                unsafe
                {
                    Marshal.Copy(result.Data, 0, new IntPtr(&msg), (int)result.BytesRead);
                }

                if (expReportId == 0x00 || msg.report_id == (byte)expReportId)
                {
                    return (result.BytesRead, msg);
                }
                else
                {
                    Debug.Error("Unexpected report id.");
                }
            }

            return (0, default);
        }

        async Task<(uint, hidpp20_message msg)> ReadMessage(EReportId expReportId, TimeSpan? timeoutTime)
        {
            var readTask = Read(expReportId);

            if ((!timeoutTime.HasValue || readTask.Wait(timeoutTime.Value)) && await readTask is ReadResult result)
            {
                hidpp20_message msg;
                unsafe
                {
                    Marshal.Copy(result.Data, 0, new IntPtr(&msg), (int)result.BytesRead);
                }

                if (msg.report_id == (byte)expReportId)
                {
                    return (result.BytesRead, msg);
                }
                else if (expReportId == 0 &&
                        (msg.report_id == k_ShortMessage || msg.report_id == k_LongMessage))
                {
                    /* HACK: ping response for HID++ 2.0 is a LONG
                        * message, but for HID++ 1.0 it is a SHORT one. */
                    return (result.BytesRead, msg);
                }
                else
                {
                    Debug.Error("Unexpected report id.");
                }
            }
            else
            {
                Debug.Error("Read request timed out!");
            }
            return (0, default);
        }

        async Task<(uint, hidpp20_message msg)> ReadMessage(EReportId expReportId)
        {
            TimeSpan timeoutTime = TimeSpan.FromMilliseconds(2000);
            return await ReadMessage(expReportId, timeoutTime);
        }


        async Task<bool> SendMessage(hidpp20_message msg)
        {
            int length = msg.report_id == k_LongMessage ? k_LongMessageLength : k_ShortMessageLength;
            byte[] buffer = new byte[length];

            unsafe
            {
                Marshal.Copy(new IntPtr(&msg), buffer, 0, length);
            }
            try
            {
                await (length == k_LongMessageLength ? device20.WriteAsync(buffer) : device7.WriteAsync(buffer));
            }
            catch (IOException)
            {
                return false;
            }
            return true;
        }

        async Task<hidpp20_message?> SendReadMessage(hidpp20_message msg, EReportId expReportId, [CallerMemberName] string CallerName = "")
        {
            if (await SendMessage(msg))
            {
                while (true)
                {
                    if (await ReadMessage(expReportId) is (var length, var res) && length != 0)
                    {
                        if (res.device_idx == msg.device_idx &&
                            res.sub_id == msg.sub_id)
                        {
                            return res;
                        }
                    }
                    else
                    {
                        Debug.Error($"Failed to read message! '{CallerName}' sub_id: 0x{msg.sub_id:X2}");
                        break;
                    }
                }
            }
            else
            {
                Debug.Error($"Failed to send message! '{CallerName}'");
            }
            return null;
        }

        public async Task<bool> SetOnboardProfiles(bool enable)
        {
            hidpp20_message msg = new hidpp20_message()
            {
                report_id = k_ShortMessage,
                device_idx = deviceId,
                address = (byte)(enable ? 0x1F : 0x2D)

            };
            if (features.TryGetValue(EHidppPage.OnboardProfiles, out var feature))
            {
                msg.sub_id = (byte)feature.Item1;
                msg.param0 = (byte)(enable ? 1 : 0);

                if (await SendReadMessage(msg, 0x00) is hidpp20_message res)
                {
                    PrintMessage(res);
                    return true;
                }
            }
            return false;
        }

        public async Task<bool> SetMouseSpy(bool enable)
        {
            // 10 01 0f 2d 00 00 00

            hidpp20_message msg = new hidpp20_message()
            {
                report_id = k_ShortMessage,
                device_idx = deviceId,
                address = (byte)(enable ? 0x1F : 0x2D)
            };
            if (features.TryGetValue(EHidppPage.MouseButtonSpy, out var feature))
            {
                msg.sub_id = (byte)feature.Item1;
                if (await SendReadMessage(msg, 0x00) is hidpp20_message res)
                {
                    return res.param0 == 0;
                }
            }

            return false;
        }

        async Task GetInfo()
        {
            hidpp20_message msg = new hidpp20_message()
            {

                report_id = k_ShortMessage,
                device_idx = deviceId,
                address = 0

            };

            if (features.TryGetValue(EHidppPage.DeviceInfo, out var feature))
            {
                msg.sub_id = (byte)feature.Item1;
                msg.address = 0x1e;
                {
                    if (await SendReadMessage(msg, EReportId.Long) is hidpp20_message res)
                    {
                        PrintMessage(res);
                    }
                }
                {
                    msg.address = 0x0e;
                    if (await SendReadMessage(msg, EReportId.Long) is hidpp20_message res)
                    {
                        PrintMessage(res);
                    }
                }
            }
        }

        public async Task<(ushort, EPowerState)?> GetBatteryVoltage()
        {
            const byte CMD_BATTERY_VOLTAGE_GET_BATTERY_VOLTAGE = 0x00;
            hidpp20_message msg = new hidpp20_message()
            {

                report_id = k_ShortMessage,
                device_idx = deviceId,
                address = CMD_BATTERY_VOLTAGE_GET_BATTERY_VOLTAGE
            };

            if (features.TryGetValue(EHidppPage.BatteryVoltage, out var feature))
            {
                msg.sub_id = (byte)feature.Item1;

                if (await SendReadMessage(msg, EReportId.Long) is hidpp20_message res)
                {
                    EPowerState state;
                    switch (res.param2)
                    {
                        case 0x00: /* discharging */
                            state = EPowerState.OnBattery;
                            break;
                        case 0x10: /* wireless charging */
                        case 0x80: /* charging */
                            state = EPowerState.Charging;
                            break;
                        case 0x81: /* fully charged */
                            state = EPowerState.Charged;
                            break;
                        default:
                            state = EPowerState.Unknown;
                            break;
                    }

                    ushort voltage = (ushort)((res.param0 << 8) | res.param1);
                    return (voltage, state);
                }
            }

            return null;
        }

        protected virtual float ConvertBatteryVoltageToPercent(ushort voltage)
        {
            return voltage;
        }

        async Task<bool> WaitForDeviceConnection(TimeSpan? maxWaitTime = null)
        {
            try
            {
                DateTime startTime = DateTime.Now;
                while (true)
                {
                    var elapsedTime = DateTime.Now - startTime;
                    if (maxWaitTime.HasValue && elapsedTime >= maxWaitTime.Value)
                    {
                        break;
                    }
                    var readTask = ReadMessage(EReportId.Short, null);
                    if ((!maxWaitTime.HasValue || readTask.Wait(maxWaitTime.Value - elapsedTime)) &&
                        await readTask is (var length, var msg) && length != 0)
                    {
                        if (msg.sub_id == 0x41 && (msg.param0 & 0x40) == 0) // Device connect
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Exception(e);
            }
            return false;
        }

        bool OnMessageRead(hidpp20_message msg)
        {
            if (msg.sub_id == 0x0F)
            {
                ushort buttons = (ushort)((msg.param0 << 8) | msg.param1);
                OnButtonChanged?.Invoke(this, buttons);
                //Debug.Info($"Mouse button pressed! {buttons:X4}");
            }
            if (msg.report_id == k_ShortMessage && msg.sub_id == 0x41)
            {
                //PrintMessage(msg);
                if ((msg.param0 & 0x40) != 0)   // Device disconnect
                {
                    return false;
                }

            }
            return true;
        }

        public async Task<bool> ListenForEvents()
        {
            TimeSpan maxSleepTime = TimeSpan.FromSeconds(30);
            ushort lastVoltage = 0;
            EPowerState lastState = EPowerState.Unknown;
            DateTime lastBatteryUpdate = DateTime.Now.Subtract(maxSleepTime);


            while (device20 != null)
            {
                var diff = DateTime.Now - lastBatteryUpdate;
                if (diff >= maxSleepTime && await GetBatteryVoltage() is (ushort voltage, EPowerState state))
                {
                    lastBatteryUpdate = DateTime.Now;
                    if (voltage != lastVoltage || state != lastState)
                    {
                        lastVoltage = voltage;
                        lastState = state;
                        try
                        {
                            var percent = voltagePercentMap.GetPercent(voltage);
                            OnBatteryChanged?.Invoke(this, voltage, percent, state);
                        }
                        catch (Exception e)
                        {
                            Debug.Exception(e, true);
                        }
                    }
                }

                var waitTime = maxSleepTime - diff;
                if(waitTime <= TimeSpan.Zero)
                {
                    continue;
                }
                if (await ReadMessageTimeout(0x00, waitTime) is (var length, var msg) && length != 0)
                {
                    if (OnMessageRead(msg) == false)
                    {
                        return true;
                    }
                }
            }

            return !IsWireless && s_WirelessModels.Contains(Model);
        }

        async Task<bool> EnableConnectionNotifications()
        {
            hidpp20_message msg;
            // Use to check if notification are enabled
            //{
            //    msg = new hidpp20_message()
            //    {
            //        report_id = k_ShortMessage,
            //        device_idx = 0xFF,
            //        sub_id = 0x81,
            //        address = 0x00
            //    };
            //    if (await SendReadMessage(msg, EReportId.Short) is hidpp20_message result)
            //    {
            //        PrintMessage(result);
            //    }
            //}
            //{
            //    msg = new hidpp20_message()
            //    {
            //        report_id = k_ShortMessage,
            //        device_idx = 0xFF,
            //        sub_id = 0x81,
            //        address = 0x02
            //    };
            //    if (await SendReadMessage(msg, EReportId.Short) is hidpp20_message result)
            //    {
            //        PrintMessage(result);
            //    }
            //}
            msg = new hidpp20_message()
            {
                report_id = k_ShortMessage,
                device_idx = 0xFF,
                sub_id = 0x80,
                param1 = 0x01,
            };

            if (!(await SendReadMessage(msg, EReportId.Short) is hidpp20_message _))
            {
                return false;
            }

            msg = new hidpp20_message()
            {
                report_id = k_ShortMessage,
                device_idx = 0xFF,
                sub_id = 0x80,
                address = 0x02,
                param0 = 0x02,
            };

            if (await SendReadMessage(msg, EReportId.Short) is hidpp20_message res)
            {
                return true;
                // return (res.param0 & 0x40) == 0; should be value indicating if any device is connected to the receiver -- seems to not work
            }
            return false;
        }

        async Task<bool> IsDeviceConnected()
        {
            // @TODO - find a way to detect if there is any device connected to the receiver

            //hidpp20_message msg = new hidpp20_message()
            //{
            //    report_id = k_ShortMessage,
            //    device_idx = 0xFF,
            //    sub_id = 0x81,
            //};

            // If we dont timeout we should be connected
            hidpp20_message msg = new hidpp20_message()
            {
                report_id = k_LongMessage,
                device_idx = deviceId,
                sub_id = 0x00,
                address = 0x00
            };
            if (await SendMessage(msg))
            {
                if (await ReadMessageTimeout(EReportId.Long, TimeSpan.FromMilliseconds(1000)) is (var length, var result) && length != 0)
                {
                    return true;
                }
            }


            return false;
        }

        /// <returns>(major, minor) version or null</returns>
        public async Task<(byte, byte)?> GetProtocolVersion()
        {
            const byte PAGE_ROOT_IDX = 0x00;
            const byte CMD_ROOT_GET_PROTOCOL_VERSION = 0x10;
            hidpp20_message msg = new hidpp20_message()
            {
                report_id = k_LongMessage,
                device_idx = deviceId,
                sub_id = PAGE_ROOT_IDX,
                address = CMD_ROOT_GET_PROTOCOL_VERSION
            };

            if (await SendReadMessage(msg, EReportId.Long) is hidpp20_message res)
            {
                return (res.param0, res.param1);
            }

            return null;
        }

        public async Task<bool> Initialize()
        {
            if (device7 == null || device20 == null)
                return false;

            await device7.InitializeAsync();
            await device20.InitializeAsync();

            deviceId = 0x01;

            if (IsWireless &&
                await EnableConnectionNotifications() &&
                await IsDeviceConnected() == false)
            {
                return false;
            }

            if (await RootGetFeature(EHidppPage.FeatureSet) is (byte featureIndex, byte featureType, byte featureVersion))
            {
                if (await GetProtocolVersion() is (byte major, byte minor))
                {
                    versionMajor = major;
                    versionMinor = minor;
                }

                var featureCount = await GetFeatureCount(featureIndex);
                if (featureCount < 0) return false;
                featureCount += 1;
                features = new Dictionary<EHidppPage, (int, hidpp20_feature)>(featureCount);
                for (int i = 0; i < featureCount; i++)
                {
                    if (await GetFeature(featureIndex, (byte)i) is hidpp20_feature feature)
                    {
                        features.Add((EHidppPage)feature.feature, (i, feature));
                    }
                    else
                    {
                        Debug.Error($"Couldn't get device feature: 0x{i.ToString("X")}");
                        return false;
                    }
                }

                string voltageMapFilePath = $"./data/{Model}.vm";
                if (File.Exists(voltageMapFilePath))
                {
                    using FileStream fileStream = File.OpenRead(voltageMapFilePath);
                    voltagePercentMap.Deserialize(fileStream);
                }

                return true;
            }

            return false;
        }

        private Mouse(string name, EMouseModel model, bool isWireless, Dictionary<int, IDevice> _devices)
        {
            Name = name;
            Model = model;
            IsWireless = isWireless;
            _devices.TryGetValue(7, out device7);
            _devices.TryGetValue(20, out device20);
        }

        public static async Task<Mouse> GetConnected()
        {

            var devices = await DeviceManager.Current.GetConnectedDeviceDefinitionsAsync(
               new FilterDeviceDefinition() { DeviceType = DeviceType.Hid, VendorId = k_VendorId, UsagePage = 0xFF00 }
               );

            EMouseModel selectedModel;
            HashSet<uint> added_devices = new HashSet<uint>();

            selectedModel = EMouseModel.Unknown;
            Dictionary<int, IDevice> device_map = new Dictionary<int, IDevice>();
            uint selectedProductId = 0;
            string productName = "";
            foreach (var definition in devices)
            {
                if (!definition.ProductId.HasValue) continue;
                if (definition.ReadBufferSize != definition.WriteBufferSize) continue;

                uint productId = definition.ProductId.Value;

                if (added_devices.Contains(productId)) continue;

                if (s_SupportedModels.TryGetValue(productId, out var model))
                {
                    if (selectedModel != EMouseModel.Unknown && selectedModel != model)
                    {
                        continue;
                    }
                    selectedModel = model;

                    if (s_PriorityDevice.Contains(productId))
                    {
                        if (selectedProductId != productId)
                        {
                            device_map.Clear();
                            added_devices.Add(selectedProductId);
                            selectedProductId = productId;
                        }
                    }
                    
                    selectedProductId = productId;

                    productName = definition.ProductName;
                    IDevice device = DeviceManager.Current.GetDevice(definition);
                    await device.InitializeAsync();
                    device_map.Add(definition.ReadBufferSize.Value, device);
                }
            }
            added_devices.Add(selectedProductId);
            if (device_map.Count > 0)
            {
                bool isWireless = s_WirelessConnection.Contains(selectedProductId);
                return new Mouse(productName, selectedModel, isWireless, device_map);
            }
            return null;
        }

        /// <returns>featureIndex, featureType, featureVersion</returns>
        public async Task<(byte, byte, byte)?> RootGetFeature(EHidppPage feature)
        {
            const byte PAGE_ROOT_IDX = 0x00;
            const byte CMD_ROOT_GET_FEATURE = 0x00;
            //const byte CMD_ROOT_GET_PROTOCOL_VERSION = 0x10;

            hidpp20_message msg = new hidpp20_message()
            {
                report_id = k_LongMessage,
                device_idx = deviceId,
                sub_id = PAGE_ROOT_IDX,
                address = CMD_ROOT_GET_FEATURE,
                param0 = (byte)((byte)feature >> 8),
                param1 = (byte)((byte)feature & 0xFF)

            };

            if (await SendReadMessage(msg, EReportId.Long) is hidpp20_message res)
            {
                return (res.param0, res.param1, res.param2);
            }

            return null;
        }

        public async Task<byte> GetFeatureCount(byte reg)
        {
            hidpp20_message msg = new hidpp20_message()
            {
                report_id = k_LongMessage,
                device_idx = deviceId,
                sub_id = reg,
                address = 0x00
            };

            if (await SendReadMessage(msg, EReportId.Long) is hidpp20_message res)
            {
                return res.param0;
            }

            return 0;
        }

        public async Task<hidpp20_feature?> GetFeature(byte reg, byte featureId)
        {
            const byte CMD_FEATURE_SET_GET_FEATURE_ID = 0x10;
            hidpp20_message msg = new hidpp20_message()
            {

                report_id = k_LongMessage,
                device_idx = deviceId,
                address = CMD_FEATURE_SET_GET_FEATURE_ID,
                sub_id = reg,
                param0 = featureId
            };

            if (await SendReadMessage(msg, EReportId.Long) is hidpp20_message res)
            {
                return new hidpp20_feature()
                {
                    feature = (ushort)((res.param0 << 8) | (res.param1)),
                    type = res.param2
                };
            }

            return null;
        }

        public void Dispose()
        {
            if (device20 != null)
            {
                device20.Dispose();
                device20 = null;
                device7.Dispose();
                device7 = null;
            }
        }

    }
}
