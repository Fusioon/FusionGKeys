using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Device.Net;
using Device.Net.Windows;
using Microsoft.Win32.SafeHandles;

namespace FusionGKeys
{
    public enum EKeyboardModel
    {
        Unknown,
        G910
    }

    enum NativeEffectGroup : byte
    {
        color = 0x01,
        breathing,
        cycle,
        waves
    };
    //  enum NativeEffect : ushort
    //  {
    //      color = (NativeEffectGroup::color) << 8,
    //breathing = static_cast<uint16_t>(NativeEffectGroup::breathing) << 8,
    //cycle = static_cast<uint16_t>(NativeEffectGroup::cycle) << 8,
    //waves = static_cast<uint16_t>(NativeEffectGroup::waves) << 8,
    //hwave,
    //vwave,
    //cwave
    //  };

    public enum NativeEffectPart : byte
    {
        All = 0xff,
        Keys = 0x00,
        Logo
    }

    enum NativeEffectStorage : byte
    {
        none = 0x00,
        // "user-stored lighting" can be recalled with backlight+7
        user,
    }

    public class Keyboard : IDisposable
    {
        // Logitech Vendor ID
        const int k_VendorId = 0x46d;

        const int k_G910_1 = 0xc335;
        const int k_G910_2 = 0x46d;


        SafeFileHandle _writeHandle;
        SafeFileHandle _readHandle;

        public SafeFileHandle WriteHandle => _writeHandle;
        public SafeFileHandle ReadHandle => _readHandle;

        public string Name { get; }
        public EKeyboardModel Model { get; }

        static Hid.Net.Windows.WindowsHidDeviceFactory s_deviceFactory = new Hid.Net.Windows.WindowsHidDeviceFactory(null, null);
        static Hid.Net.Windows.WindowsHidApiService s_hidApi = new Hid.Net.Windows.WindowsHidApiService(null);

        public bool SetGKeys(bool asFKeys)
        {
            byte value = (byte)(asFKeys ? 0 : 1);
            byte[] data = new byte[20];

            data[0] = 0x11;
            data[1] = 0xff;
            data[2] = 0x08;
            data[3] = 0x2e;
            data[4] = value;

            return SendData(data);
        }

        public bool SetMRKeys(byte value)
        {
            switch(Model)
            {
                case EKeyboardModel.G910:
                    switch(value)
                    {
                        case 0x00:
                        case 0x01:
                            byte[] data = new byte[20];
                            data[0] = 0x11;
                            data[1] = 0xff;
                            data[2] = 0x0a;
                            data[3] = 0x0e;
                            data[4] = value;
                            return SendData(data);
                    }
                    break;
            }
            return false;
        }

        public bool SetMNKeys(byte value)
        {
            switch (Model)
            {
                case EKeyboardModel.G910:
                    switch (value)
                    {
                        case 0x00:
                        case 0x01:
                        case 0x02:
                        case 0x03:
                        case 0x04:
                        case 0x05:
                        case 0x06:
                        case 0x07:
                            byte[] data = new byte[20];
                            data[0] = 0x11;
                            data[1] = 0xff;
                            data[2] = 0x09;
                            data[3] = 0x1e;
                            data[4] = value;
                            return SendData(data);
                    }
                    break;
            }
            return false;
        }

        private byte GetProtocolByte()
        {
            const byte K_G910_PROTOCOL_BYTE = 0x10;

            switch (Model)
            {

                case EKeyboardModel.G910:
                    return K_G910_PROTOCOL_BYTE;
            }
            return 0;
        }
        public bool SetColor(in Settings settings, NativeEffectPart part = NativeEffectPart.All)
        {
            if (part == NativeEffectPart.All)
            {
                return
                    SetColor(settings, NativeEffectPart.Keys) &&
                    SetColor(settings, NativeEffectPart.Logo);
            }

            byte effectGroup = (byte)NativeEffectGroup.cycle;
            byte storage = (byte)NativeEffectStorage.user;
            ushort effect = (ushort)(effectGroup << 8);

            switch (settings.effect)
            {
                case EEffect.Fixed:
                    effectGroup = (byte)NativeEffectGroup.color;
                    effect = (ushort)(effectGroup << 8);
                    break;
                case EEffect.Cycle:
                    effectGroup = (byte)NativeEffectGroup.cycle;
                    effect = (ushort)(effectGroup << 8);
                    break;
                case EEffect.Breathing:
                    effectGroup = (byte)NativeEffectGroup.breathing;
                    effect = (ushort)(effectGroup << 8);
                    break;
                case EEffect.ColorWave:
                    effectGroup = (byte)NativeEffectGroup.waves;
                    switch (settings.cycle)
                    {
                        case ECycle.Horizontal:
                            effect = (ushort)((effectGroup << 8) + 1);
                            break;
                        case ECycle.Vertical:
                            effect = (ushort)((effectGroup << 8) + 2);
                            break;
                        case ECycle.Center:
                            effect = (ushort)((effectGroup << 8) + 3);
                            break;
                    }
                    break;
            }

            if (!_writeHandle.IsInvalid)
            {
                byte protocolByte = GetProtocolByte();

                byte[] data = new byte[20]
                {
                    0x11, 0xff, protocolByte, 0x3c,
                    (byte)part, effectGroup,
                    // color of static-color and breathing effects
                    settings.color.red, settings.color.green, settings.color.blue,
                    // period of breathing effect (ms)
                    (byte)(settings.rate >> 8), (byte)(settings.rate & 0xFF),
                    // period of cycle effect (ms)
                    (byte)(settings.rate >> 8), (byte)(settings.rate & 0xFF),
                    // wave variation (e.g. horizontal)
                    (byte)(effect & 0xFF),
                    0x64, // unused?
                    // period of wave effect (ms)
                    (byte)(settings.rate >> 8),
                    storage,
                    0,0,0 // unused?
                };

                return SendData(data);
            }
            return false;
        }

        public bool SendData(byte[] data)
        {
            uint written;
            return APICalls.WriteFile(_writeHandle, data, (uint)data.Length, out written, 0);
        }
        public bool ReadData(byte[] data)
        {
            int readBytes;
            return APICalls.ReadFile(_readHandle, data, data.Length, out readBytes, 0);
        }

        private Keyboard(string name, EKeyboardModel model, SafeFileHandle write, SafeFileHandle read)
        {
            Name = name;
            Model = model;
            _writeHandle = write;
            _readHandle = read;
        }

        public static async Task<List<Keyboard>> GetConnected()
        {
            List<Keyboard> keyboards = new List<Keyboard>();
            var result = await s_deviceFactory.GetConnectedDeviceDefinitionsAsync(new FilterDeviceDefinition() { DeviceType = DeviceType.Hid, VendorId = k_VendorId });
            foreach (var device in result)
            {
                EKeyboardModel model = EKeyboardModel.Unknown;
                SafeFileHandle write;
                SafeFileHandle read;

                switch (device.ProductId)
                {
                    case k_G910_1:
                    case k_G910_2:
                        model = EKeyboardModel.G910;
                        break;
                }

                if (model != EKeyboardModel.Unknown)
                {
                    if (device.WriteBufferSize != 20) continue;
                    if (device.ReadBufferSize != 20) continue;

                    write = APICalls.CreateFile(
                        device.DeviceId,
                        FileAccessRights.GenericWrite,
                        APICalls.FileShareWrite | APICalls.FileShareRead,
                        IntPtr.Zero,
                        APICalls.OpenExisting,
                        0,
                        IntPtr.Zero);

                    read = APICalls.CreateFile(
                        device.DeviceId,
                        FileAccessRights.GenericRead,
                        APICalls.FileShareWrite | APICalls.FileShareRead,
                        IntPtr.Zero,
                        APICalls.OpenExisting,
                        0,
                        IntPtr.Zero);

                    if (!write.IsInvalid && !read.IsInvalid)
                    {
                        keyboards.Add(new Keyboard(device.ProductName, model, write, read));
                    }
                }
            }
            return keyboards;
        }

        public void Dispose()
        {
            _writeHandle.Dispose();
            _readHandle.Dispose();
        }
    }
}
