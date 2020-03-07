using System;
using System.Collections.Generic;
using System.IO;

namespace Fusion.GKeys
{
    public enum EEffect
    {
        Off,
        Fixed,
        Cycle,
        ColorWave,
        Breathing,
        //EchoPress
    }
    public enum ECycle
    {
        Horizontal,
        Vertical,
        Center
    }
    public struct Color
    {
        public byte red;
        public byte green;
        public byte blue;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class SerializeAs : Attribute
    {
        public string Name { get; set; }
        public string Info { get; set; }

        public SerializeAs(string name, string info)
        {
            Name = name;
            Info = info;
        }
    }

    public struct Settings
    {
        public EEffect effect;
        public ECycle cycle;
        public Color color;
        //public Color echo_color;
        public ushort rate;
        public Dictionary<EMacroKey, KeyCode> keymap;
        public uint low_battery_voltage;

        static void CreateSettingsFile(string path)
        {
            string GetEnumValues<T>() where T : Enum
            {
                var names = Enum.GetNames(typeof(T));
                var values = Enum.GetValues(typeof(T));
                List<string> result = new List<string>(names.Length);
                for (int i = 0; i < names.Length; i++)
                {
                    result.Add($"{names[i]}({(int)values.GetValue(i)})");
                }
                return string.Join(", ", result);
            }

            string[] text =
             {
                $";effect <{GetEnumValues<EEffect>()}>",
                $";color <red green blue> (0-255)",
                //$";echocolor <red green blue> (0-255)",
                $";rate <int> (ms)",
                $";cycle <{GetEnumValues<ECycle>()}>",
                $";warn_voltage <uint> (mV)"
            };

            File.WriteAllText(path, string.Join("\n", text));
        }

        delegate void OnMatch(ref int i, List<ReadOnlyMemory<char>> tokens, ref Settings settings1);
        struct settings_entry
        {
            public string id;
            public OnMatch match;
            public settings_entry(string id, OnMatch match)
            {
                this.id = id;
                this.match = match;
            }
        }
        public static void load(string filePath, ref Settings settings)
        {
            bool TryParseEnum<T>(ref int i, List<ReadOnlyMemory<char>> tokens, out T val) where T : unmanaged, Enum
            {
                int start = i;
                i++;
                if (i < tokens.Count)
                {
                    var tok = tokens[i];
                    var names = Enum.GetNames(typeof(T));
                    var values = (T[])Enum.GetValues(typeof(T));
                    Debug.Assert(names.Length == values.Length);
                    for (int j = 0; j < names.Length; j++)
                    {
                        if (MemoryExtensions.CompareTo(names[j].AsSpan(), tok.Span, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            val = values[j];
                            return true;
                        }
                    }

                    if (int.TryParse(tok.Span, out int result) && Enum.IsDefined(typeof(T), result))
                    {
                        unsafe
                        {
                            val = *(T*)&result;
                            return true;
                        }
                    }
                }
                val = default(T);
                return false;
            }

            bool TryParseColor(ref int i, List<ReadOnlyMemory<char>> tokens, out Color color)
            {
                int start = i;

                if (i + 3 < tokens.Count)
                {
                    byte red, green, blue;
                    if (byte.TryParse(tokens[++i].Span, out red) &&
                        byte.TryParse(tokens[++i].Span, out green) &&
                        byte.TryParse(tokens[++i].Span, out blue))
                    {
                        color = new Color() { red = red, green = green, blue = blue };
                        return true;
                    }
                }

                color = default;
                return false;
            }

            List<settings_entry> settings_list = new List<settings_entry>()
            {
                new settings_entry("effect", (ref int i, List<ReadOnlyMemory<char>> tokens, ref Settings sett) => {
                    if(TryParseEnum<EEffect>(ref i, tokens, out var effect))
                        sett.effect = effect;
                }),
                new settings_entry("cycle", (ref int i,  List<ReadOnlyMemory<char>> tokens, ref Settings sett) => {
                    if(TryParseEnum<ECycle>(ref i, tokens, out var cycle))
                        sett.cycle = cycle;
                }),
                new settings_entry("color", (ref int i,  List<ReadOnlyMemory<char>> tokens, ref Settings sett) => {
                    if(TryParseColor(ref i, tokens, out var color))
                        sett.color = color;
                }),
                //new settings_entry("echocolor", (ref int i,  List<ReadOnlyMemory<char>> tokens, ref Settings sett) => {
                //    if(TryParseColor(ref i, tokens, out var color))
                //        sett.echo_color = color;
                //}),
                new settings_entry("rate", (ref int i,  List<ReadOnlyMemory<char>> tokens, ref Settings sett) => {
                    if (++i < tokens.Count && ushort.TryParse(tokens[i].Span, out var rate))
                        sett.rate = rate;
                }),
                new settings_entry("warn_voltage", (ref int i,  List<ReadOnlyMemory<char>> tokens, ref Settings sett) => {
                    if (++i < tokens.Count && uint.TryParse(tokens[i].Span, out var voltage))
                        sett.low_battery_voltage = voltage;
                }),
            };

            if (File.Exists(filePath))
            {
                var content = File.ReadAllText(filePath);
                if (!string.IsNullOrEmpty(content))
                {
                    var tokens = Tokenizer.Tokenize(content);
                    for (int i = 0; i < tokens.Count; i++)
                    {
                        var t = tokens[i];
                        foreach (var s in settings_list)
                        {
                            if (MemoryExtensions.CompareTo(t.Span, s.id.AsSpan(), StringComparison.OrdinalIgnoreCase) == 0 &&
                                s.match != null)
                            {
                                s.match(ref i, tokens, ref settings);
                            }


                        }
                    }
                }
            }
            else
            {
                CreateSettingsFile(filePath);
            }
        }
    }
}
