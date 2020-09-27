using Device.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Fusion.GKeys
{
    public enum EKeyboardModel
    {
        Unknown,
        G910
    }

    public enum EStartupEffect : byte
    {
        wave = 0x01,
        color
    }

    enum NativeEffectGroup : byte
    {
        color = 0x01,
        breathing,
        cycle,
        waves
    };

    public enum NativeEffectPart : byte
    {
        All = 0xff,
        Keys = 0x00,
        Logo
    }

    public enum EMacroKey
    {
        Unknown = 0,

        G1,
        G2,
        G3,
        G4,
        G5,
        G6,
        G7,
        G8,
        G9,

        M1,
        M2,
        M3,
        MR,


        MAX
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
        const int k_VendorId = 0x046d;

        static readonly Dictionary<uint, EKeyboardModel> s_SupportedModels = new Dictionary<uint, EKeyboardModel>()
        {
            { 0xC335, EKeyboardModel.G910 },
        };

        public delegate void OnMacroKey(Keyboard kb, EMacroKey key);

        public string Name { get; }
        public EKeyboardModel Model { get; }

        public OnMacroKey OnMacroKeyPressed;
        public OnMacroKey OnMacroKeyReleased;

        bool[] macroKeysDown = new bool[(int)EMacroKey.MAX];

        IDevice _device;
        public async Task SetGKeys(bool asFKeys)
        {
            byte value = (byte)(asFKeys ? 0 : 1);
            byte[] data = new byte[20];

            data[0] = 0x11;
            data[1] = 0xff;
            data[2] = 0x08;
            data[3] = 0x2e;
            data[4] = value;

            await _device.WriteAsync(data);
        }

        public async Task SetMRKeys(byte value)
        {
            switch (Model)
            {
                case EKeyboardModel.G910:
                    switch (value)
                    {
                        case 0x00:
                        case 0x01:
                            byte[] data = new byte[20];
                            data[0] = 0x11;
                            data[1] = 0xff;
                            data[2] = 0x0a;
                            data[3] = 0x0e;
                            data[4] = value;
                            await _device.WriteAsync(data);
                            break;
                    }
                    break;
            }
        }

        public async Task SetMNKeys(byte value)
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
                            await _device.WriteAsync(data);
                            break;
                    }
                    break;
            }
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

        public async Task SetStartupEffect(EStartupEffect effect)
        {
            byte[] data = new byte[20];
            data[0] = 0x11;
            data[1] = 0xff;
            data[2] = GetProtocolByte();
            switch (Model)
            {
                case EKeyboardModel.G910:
                    data[3] = 0x5e;
                    break;

                default:
                    return;
            }
            data[4] = 0x00;
            data[5] = 0x01;
            data[6] = (byte)effect;
            await _device.WriteAsync(data);
        }

        public async Task SetColor(Settings settings, NativeEffectPart part = NativeEffectPart.All)
        {
            if (part == NativeEffectPart.All)
            {
                await SetColor(settings, NativeEffectPart.Keys);
                await SetColor(settings, NativeEffectPart.Logo);
            }

            byte effectGroup = (byte)NativeEffectGroup.cycle;
            byte storage = (byte)NativeEffectStorage.user;
            ushort effect = (ushort)(effectGroup << 8);

            switch (settings.effect)
            {
                case EEffect.Off:
                    effectGroup = 0x00;
                    effect = (ushort)(effectGroup << 8);
                    break;
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

            await _device.WriteAsync(data);
        }

        protected void MacroKeyUpdate(bool isDown, EMacroKey key)
        {
            int index = (int)key;
            if (index < 0 || index >= macroKeysDown.Length)
            {
                return;
            }

            if (isDown && !macroKeysDown[index])
            {
                OnMacroKeyPressed?.Invoke(this, key);
            }

            if(!isDown && macroKeysDown[index])
            {
                OnMacroKeyReleased?.Invoke(this, key);
            }

            macroKeysDown[index] = isDown;
        }

        protected void MacroKeyEvent(byte sub_id, byte param0, byte param1)
        {
            // MKeys
            if (sub_id == 0x08)
            {
                MacroKeyUpdate((param0 & 0x01) != 0, EMacroKey.G1);
                MacroKeyUpdate((param0 & 0x02) != 0, EMacroKey.G2);
                MacroKeyUpdate((param0 & 0x04) != 0, EMacroKey.G3);
                MacroKeyUpdate((param0 & 0x08) != 0, EMacroKey.G4);
                MacroKeyUpdate((param0 & 0x10) != 0, EMacroKey.G5);
                MacroKeyUpdate((param0 & 0x20) != 0, EMacroKey.G6);
                MacroKeyUpdate((param0 & 0x40) != 0, EMacroKey.G7);
                MacroKeyUpdate((param0 & 0x80) != 0, EMacroKey.G8);
                MacroKeyUpdate((param1 & 0x01) != 0, EMacroKey.G9);
            }

            // MKeys
            if (sub_id == 0x09)
            {
                MacroKeyUpdate((param0 & 0x01) != 0, EMacroKey.M1);
                MacroKeyUpdate((param0 & 0x02) != 0, EMacroKey.M2);
                MacroKeyUpdate((param0 & 0x04) != 0, EMacroKey.M3);
            }

            // MRKey
            if (sub_id == 0x0A)
            {
                MacroKeyUpdate((param0 & 0x01) != 0, EMacroKey.MR);
            }
        }

        public async Task ListenForMacroKeys()
        {
            while (_device != null)
            {
                ReadResult result;
                try
                {
                    result = await _device.ReadAsync();
                }
                catch (IOException)
                {
                    return;
                }

                if (result.BytesRead == 20 && result.Data[0] == 0x11 && result.Data[1] == 0xff)
                {
                    switch (result.Data[2])
                    {
                        case 0x08:
                        case 0x09:
                        case 0x0A:
                            MacroKeyEvent(result.Data[2], result.Data[4], result.Data[5]);
                            break;
                    }
                }

            }
        }

        private Keyboard(string name, EKeyboardModel model, IDevice device)
        {
            Name = name;
            Model = model;
            _device = device;
        }
        public void Dispose()
        {
            if (_device != null)
            {
                _device.Dispose();
                _device = null;
            }
        }

        public static async Task<Keyboard> GetConnected()
        {
            
            var devices = await DeviceManager.Current.GetConnectedDeviceDefinitionsAsync(
                new FilterDeviceDefinition() { DeviceType = DeviceType.Hid, VendorId = k_VendorId, UsagePage = 0xFF43 }
                );

            foreach (var definition in devices)
            {
                if (definition.ReadBufferSize != 20 ||
                    definition.WriteBufferSize != 20)
                {
                    continue;
                }

                if (definition.ProductId.HasValue && s_SupportedModels.TryGetValue(definition.ProductId.Value, out var model))
                {
                    var device = DeviceManager.Current.GetDevice(definition);
                    await device.InitializeAsync();
                    return new Keyboard(definition.ProductName, model, device);
                }
            }
            return null;
        }

    }
}
