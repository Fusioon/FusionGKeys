using Hid.Net.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Notifications;

namespace Fusion.GKeys
{
    class Program
    {
        const string k_AppName = "FusionGKeys";
        static Settings settings = new Settings()
        {
            effect = EEffect.Fixed,
            cycle = ECycle.Horizontal,
            color = new Color() { red = 127, green = 0, blue = 0 },
            rate = 4000,
            low_battery_voltage = 3500,
            keymap = new Dictionary<EMacroKey, KeyCode>()
            {
                { EMacroKey.G1, KeyCode.F13 },
                { EMacroKey.G2, KeyCode.F14 },
                { EMacroKey.G3, KeyCode.F15 },
                { EMacroKey.G4, KeyCode.F16 },
                { EMacroKey.G5, KeyCode.F17 },
                { EMacroKey.G6, KeyCode.F18 },
                { EMacroKey.G7, KeyCode.F19 },
                { EMacroKey.G8, KeyCode.F20 },
                { EMacroKey.G9, KeyCode.F21 },
                { EMacroKey.M1, KeyCode.F22 },
                { EMacroKey.M2, KeyCode.F23 },
                { EMacroKey.M3, KeyCode.F24 },
            }
        };

        static DateTime lastVoltageNotification;

        static void OnMacroKeyPressed(Keyboard kb, EMacroKey key)
        {
            if (settings.keymap.TryGetValue(key, out var kc))
            {
                Input.SendKeyPress(kc);
            }
            if (key == EMacroKey.MR)
            {
                TimeSpan voltageNotificatinCooldown = TimeSpan.FromSeconds(5);

                if (DateTime.Now - lastVoltageNotification < voltageNotificatinCooldown)
                {
                    return;
                }

                if (lastPowerState != 0)
                {
                    var template = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText01);
                    var textNodes = template.GetElementsByTagName("text");
                    textNodes.Item(0).InnerText = $"Mouse battery voltage: {lastVoltage}mV ~{lastPercent:0.#}% ({lastPowerState})";
                    var notification = new ToastNotification(template);
                    toastNotifier.Show(notification);
                    lastVoltageNotification = DateTime.Now;
                }
            }
        }

        static DateTime lastNotificationTime;
        static EPowerState lastPowerState = EPowerState.Unknown;
        static uint lastVoltage = 0;
        static float lastPercent = 0;
        static ToastNotifier toastNotifier = ToastNotificationManager.CreateToastNotifier(k_AppName);

        static void SendBatteryLowNotification()
        {
            if ((DateTime.Now - lastNotificationTime) < TimeSpan.FromMinutes(30))
            {
                return;
            }

            lastNotificationTime = DateTime.Now;

            var template = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText01);
            var textNodes = template.GetElementsByTagName("text");
            textNodes.Item(0).InnerText = $"Battery level low!";
            var notification = new ToastNotification(template);
            toastNotifier.Show(notification);
        }

        static void SendBatteryFullNotification()
        {
            var template = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText01);
            var textNodes = template.GetElementsByTagName("text");
            textNodes.Item(0).InnerText = $"Battery is fully charged!";
            var notification = new ToastNotification(template);
            toastNotifier.Show(notification);
        }

        static void OnBatteryChanged(Mouse mouse, uint voltage, float percent, EPowerState state)
        {

            if (state == EPowerState.OnBattery && voltage < settings.low_battery_voltage)
            {
                SendBatteryLowNotification();
            }
            if (lastPowerState != state && state == EPowerState.Charged)
            {
                SendBatteryFullNotification();
            }

            lastPowerState = state;
            lastVoltage = voltage;
            lastPercent = percent;
        }

        static void OnMouseButtonsChanged(Mouse mouse, ushort buttons)
        {

        }

        static async Task<bool> RunMouse()
        {
            lastVoltage = 0;
            lastPowerState = 0;

            using Mouse mouse = await Mouse.GetConnected();

            if (mouse != null && await mouse.Initialize())
            {
                mouse.OnBatteryChanged = OnBatteryChanged;
                mouse.OnButtonChanged = OnMouseButtonsChanged;
                return await mouse.ListenForEvents();
            }

            return false;
        }

        static async Task<bool> RunKeyboard()
        {
            using Keyboard keyboard = await Keyboard.GetConnected();

            if (keyboard != null)
            {
                await keyboard.SetOnBoardMode(OnBoardMode.Software);
                await keyboard.SetGKeysMode(false);
                await keyboard.SetColor(settings, NativeEffectPart.All);
                await keyboard.Commit();

                keyboard.OnMacroKeyPressed = OnMacroKeyPressed;
                await keyboard.ListenForMacroKeys();
                return true;
            }

            return false;
        }

        static async Task Run()
        {
            WindowsHidDeviceFactory.Register(null, null);
            Settings.load("settings.cfg", ref settings);

            TimeSpan sleepTime = TimeSpan.FromMilliseconds(3000);

            Task<bool> keyboardTask = null, mouseTask = null;

            int maxRetryCount = 20;
            while (maxRetryCount >= 0)
            {
                if (keyboardTask == null || keyboardTask.IsCompleted)
                {
                    keyboardTask = RunKeyboard();
                }
                if (mouseTask == null || mouseTask.IsCompleted)
                {
                    mouseTask = RunMouse();
                }

                await Task.WhenAny(keyboardTask, mouseTask);

                if ((keyboardTask.IsCompleted && !keyboardTask.Result) && (mouseTask.IsCompleted && !mouseTask.Result))
                {
                    --maxRetryCount;
                }

                if (!mouseTask.IsCompleted || !mouseTask.Result)
                {
                    await Task.Delay(sleepTime);
                }
            }
        }

        static async Task Main(string[] args)
        {
            try
            {
                var executablePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                Directory.SetCurrentDirectory(Path.GetDirectoryName(executablePath));

                using EventWaitHandle handle = new EventWaitHandle(
                    false,
                    EventResetMode.ManualReset,
                    k_AppName, out var createdNew
                );

                if (!createdNew)
                {
                    handle.Set();
                    handle.Reset();
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }

                Task exitTask = Task.Run(() =>
                {
                    handle.WaitOne();
                });

                TaskScheduler.UnobservedTaskException += OnUnobserverdTaskException;

                await Task.WhenAny(exitTask, Run());
            }
            catch(Exception e)
            {
                Debug.Exception(e, true);
            }
        }

        private static void OnUnobserverdTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Debug.Exception(e.Exception, true);
        }
    }
}
