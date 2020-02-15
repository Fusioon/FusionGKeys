using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Device.Net;
using Microsoft.Win32.SafeHandles;
using Device.Net.Windows;
using System.Threading;
using System.IO;
using System.Collections.Generic;

namespace FusionGKeys
{
    //enum NativeEffectGroup : byte
    //{
    //    color = 0x01,
    //    breathing,
    //    cycle,
    //    waves
    //};
    ////  enum NativeEffect : ushort
    ////  {
    ////      color = (NativeEffectGroup::color) << 8,
    ////breathing = static_cast<uint16_t>(NativeEffectGroup::breathing) << 8,
    ////cycle = static_cast<uint16_t>(NativeEffectGroup::cycle) << 8,
    ////waves = static_cast<uint16_t>(NativeEffectGroup::waves) << 8,
    ////hwave,
    ////vwave,
    ////cwave
    ////  };

    //enum NativeEffectPart : byte
    //{
    //    all = 0xff,
    //    keys = 0x00,
    //    logo
    //}

    //enum NativeEffectStorage : byte
    //{
    //    none = 0x00,
    //    // "user-stored lighting" can be recalled with backlight+7
    //    user,
    //}

    class Program
    {
        static async Task<SafeFileHandle> SetGKeysFunc(bool asFKeys)
        {
            const int k_g910_1 = 0xc32b;
            const int k_g910_2 = 0xc335;
            const int k_logitech = 0x46d;

            var factory = new Hid.Net.Windows.WindowsHidDeviceFactory(null, null);
            var result = await factory.GetConnectedDeviceDefinitionsAsync(new FilterDeviceDefinition() { DeviceType = DeviceType.Hid, VendorId = k_logitech });

            var hidapi = new Hid.Net.Windows.WindowsHidApiService(null);
            foreach (var q in result)
            {
                if (q.ProductId == k_g910_1 || q.ProductId == k_g910_2)
                {
                    if (q.WriteBufferSize != 20) continue;

                    using var file_handle = APICalls.CreateFile(
                        q.DeviceId,
                        FileAccessRights.GenericWrite,
                        APICalls.FileShareWrite | APICalls.FileShareRead,
                        IntPtr.Zero,
                        APICalls.OpenExisting,
                        0,
                        IntPtr.Zero);
                    {
                        if (!file_handle.IsInvalid)
                        {
                            byte value = (byte)(asFKeys ? 0 : 1);
                            byte[] data = new byte[20];

                            data[0] = 0x11;
                            data[1] = 0xff;
                            data[2] = 0x08;
                            data[3] = 0x2e;
                            data[4] = value;

                            if (hidapi.AWriteFile(file_handle, data, 20, out var written, 0))
                            {
                                SetColor(file_handle, NativeEffectPart.All);

                                var read_handle = APICalls.CreateFile(
                                    q.DeviceId,
                                    FileAccessRights.GenericRead,
                                    APICalls.FileShareWrite | APICalls.FileShareRead,
                                    IntPtr.Zero,
                                    APICalls.OpenExisting,
                                    0,
                                    IntPtr.Zero);
                                if (!read_handle.IsInvalid)
                                {
                                    return read_handle;
                                }
                                break;
                            }
                        }
                    }

                }
            }
            return null;
        }

        static void Emit(KeyCode keyCode, bool check)
        {
            if (!check) return;
            Input.SendKeyPress(keyCode);
        }

        static Settings settings = new Settings()
        {
            effect = EEffect.Fixed,
            cycle = ECycle.Horizontal,
            color = new Color() { red = 127, green = 0, blue = 0 },
            echo_color = new Color() { red = 127, green = 0, blue = 0 },
            rate = 4000
        };

        static bool SetColor(SafeFileHandle writeHandle, NativeEffectPart part)
        {
            const byte K_G910_PROTOCOL_BYTE = 0x10;

            if (part == NativeEffectPart.All)
            {
                return
                    SetColor(writeHandle, NativeEffectPart.Keys) &&
                    SetColor(writeHandle, NativeEffectPart.Logo);
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

            if (!writeHandle.IsInvalid)
            {
                byte protocolByte = K_G910_PROTOCOL_BYTE;

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

                return APICalls.WriteFile(writeHandle, data, 20, out var written, 0);
            }
            return false;
        }

        static async Task Run()
        {
            Settings.load("settings.cfg", ref settings);
            var kbs = await Keyboard.GetConnected();
            if (kbs.Count == 0)
            {
                return;
            }
            Keyboard keyboard = kbs[0];
            keyboard.SetGKeys(false);
            keyboard.SetColor(settings, NativeEffectPart.All);

            byte[] buffer = new byte[20];
            while (!keyboard.ReadHandle.IsInvalid)
            {
                Array.Clear(buffer, 0, buffer.Length);
                if (keyboard.ReadData(buffer))
                {
                    if (buffer[0] == 0x11 && buffer[1] == 0xff)
                    {
                        // MKeys
                        if (buffer[2] == 0x08)
                        {
                            Emit(KeyCode.F13, ((buffer[4] & 1) != 0));      // G1
                            Emit(KeyCode.F14, ((buffer[4] & 2) != 0));      // G2
                            Emit(KeyCode.F15, ((buffer[4] & 4) != 0));      // G3
                            Emit(KeyCode.F16, ((buffer[4] & 8) != 0));      // G4
                            Emit(KeyCode.F17, ((buffer[4] & 16) != 0));     // G5
                            Emit(KeyCode.F18, ((buffer[4] & 32) != 0));     // G6
                            Emit(KeyCode.F19, ((buffer[4] & 64) != 0));     // G7
                            Emit(KeyCode.F20, ((buffer[4] & 128) != 0));    // G8
                            Emit(KeyCode.F21, ((buffer[5] & 1) != 0));      // G9
                        }
                        // MKeys
                        if (buffer[2] == 0x09 && buffer[3] == 0)
                        {
                            Emit(KeyCode.F22, (buffer[4] & 1) != 0);        // M1
                            Emit(KeyCode.F23, (buffer[4] & 2) != 0);        // M2
                            Emit(KeyCode.F24, (buffer[4] & 4) != 0);        // M3
                        }
                        // MRKey
                        if (buffer[2] == 0x0A && buffer[3] == 0)
                        {
                            Emit(KeyCode.F24, (buffer[4] & 1) != 0);        // MR
                        }
                    }
                }
            }
        }

        static async Task MouseTest()
        {
            const int k_DongleId = 0xC539;

            var factory = new Hid.Net.Windows.WindowsHidDeviceFactory(null, null);
            var result = await factory.GetConnectedDeviceDefinitionsAsync(new FilterDeviceDefinition() { DeviceType = DeviceType.Hid, VendorId = 0x46d });
            foreach (var device in result)
            {
                if(device.ReadBufferSize != 20 && device.WriteBufferSize != 20)
                {
                    continue;
                }
                if(device.ProductId != k_DongleId)
                {
                    continue;
                }
                using var write = APICalls.CreateFile(
                        device.DeviceId,
                        FileAccessRights.GenericWrite,
                        APICalls.FileShareWrite | APICalls.FileShareRead,
                        IntPtr.Zero,
                        APICalls.OpenExisting,
                        0,
                        IntPtr.Zero);

                if(write.IsInvalid)
                {
                    Console.WriteLine("handle invalid!");
                    continue;
                }



            }
        }

        static async Task Main(string[] args)
        {
            //await MouseTest();
           
            bool createdNew;
            Mutex mutex = new Mutex(true, "FusionGKeys", out createdNew);
            if (!createdNew)
            {
                return;
            }
            while (true)
            {
                await Run();
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }
    }
}
