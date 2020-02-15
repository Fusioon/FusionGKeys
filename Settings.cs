using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FusionGKeys
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

    public struct Settings
    {
        public EEffect effect;
        public ECycle cycle;
        public Color color;
        public Color echo_color;
        public ushort rate;

        static List<string> Tokenize(string input)
        {
            void MoveToNewLine(ref int i, string input)
            {
                for (; i < input.Length; i++)
                {
                    if (input[i] == '\n')
                    {
                        i++;
                        return;
                    }
                }
            }

            void SkipWhitespace(ref int i, string input)
            {
                for (; i < input.Length; i++)
                {
                    if (!char.IsWhiteSpace(input[i]))
                    {
                        return;
                    }
                }
            }

            void SkipComment(ref int i, string input)
            {
                for (; i < input.Length;)
                {
                    if (input[i] != ';')
                    {
                        break;
                    }
                    MoveToNewLine(ref i, input);
                }

            }

            string ParseTokenNormal(ref int i, string input)
            {
                int start = i;
                for (; i < input.Length; i++)
                {
                    if (char.IsWhiteSpace(input[i]))
                    {
                        break;
                    }
                }

                if (start < i)
                {
                    return input.Substring(start, i - start);
                }
                return null;
            }


            List<string> tokens = new List<string>();

            for (int i = 0; i < input.Length;)
            {
                SkipWhitespace(ref i, input);
                SkipComment(ref i, input);

                if (i == input.Length)
                {
                    break;
                }

                var token = ParseTokenNormal(ref i, input);
                if (!string.IsNullOrEmpty(token))
                {
                    tokens.Add(token);
                }
                else
                {
                    i++;
                }

            }
            return tokens;
        }



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
                $";echocolor <red green blue> (0-255)",
                $";rate <int> (ms)",
                $";cycle <{GetEnumValues<ECycle>()}>",
            };

            File.WriteAllText(path, string.Join("\n", text));
        }

        public static void load(string filePath, ref Settings settings)
        {
            T ParseEnum<T>(ref int i, List<string> tokens, T defultVal) where T : unmanaged, Enum
            {
                int start = i;
                i++;
                if (i < tokens.Count)
                {
                    var tok = tokens[i];
                    if (Enum.TryParse(tok, out T effect))
                    {
                        return effect;
                    }
                    if (int.TryParse(tok, out int result) && Enum.IsDefined(typeof(T), result))
                    {
                        unsafe
                        {
                            return *(T*)&result;
                        }
                    }
                }
                return defultVal;
            }

            Color ParseColor(ref int i, List<string> tokens)
            {
                int start = i;
                i++;
                Color color = new Color() { red = 127, green = 0, blue = 0 };
                if (i < tokens.Count)
                {
                    if (i + 2 < tokens.Count)
                    {
                        byte red, green, blue;
                        if (byte.TryParse(tokens[i], out red) &&
                            byte.TryParse(tokens[i + 1], out green) &&
                            byte.TryParse(tokens[i + 2], out blue))
                        {
                            color = new Color() { red = red, green = green, blue = blue };
                        }
                    }
                }

                return color;
            }

            if (File.Exists(filePath))
            {
                var content = File.ReadAllText(filePath);
                if (!string.IsNullOrEmpty(content))
                {
                    var tokens = Tokenize(content);
                    for (int i = 0; i < tokens.Count; i++)
                    {
                        var t = tokens[i];
                        if (string.Compare(t, "effect", true) == 0)
                        {
                            settings.effect = ParseEnum<EEffect>(ref i, tokens, EEffect.Fixed);
                        }
                        else if (string.Compare(t, "color", true) == 0)
                        {
                            settings.color = ParseColor(ref i, tokens);
                        }
                        else if (string.Compare(t, "echocolor", true) == 0)
                        {
                            settings.echo_color = ParseColor(ref i, tokens);
                        }
                        else if (string.Compare(t, "rate", true) == 0)
                        {
                            i++;
                            if (i < tokens.Count)
                            {
                                if (ushort.TryParse(tokens[i], out var rate))
                                {
                                    settings.rate = rate;
                                }
                            }
                        }
                        else if (string.Compare(t, "cycle", true) == 0)
                        {
                            settings.cycle = ParseEnum<ECycle>(ref i, tokens, ECycle.Horizontal);
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
