using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Fusion.GKeys
{
    class VoltageMap
    {
        List<(uint, float)> _values = new List<(uint, float)>();

        public VoltageMap()
        {
        }

        public void Add(uint voltage, float percent)
        {
            _values.Add((voltage, percent));
        }
        public void Sort()
        {
            _values.Sort((x, y) => { return (int)(x.Item1 - y.Item1); });
        }

        int search(int left, int right, uint value)
        {
            while (left <= right)
            {
                int mid = left + (right - left) / 2;

                if (_values[mid].Item1 == value ||
                    (_values[mid].Item1 >= value && mid > 0 && _values[mid - 1].Item1 <= value) ||
                    (_values[mid].Item1 <= value && (mid + 1) != _values.Count && _values[mid + 1].Item1 >= value))
                {
                    return mid;
                }

                if (_values[mid].Item1 < value)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            return -1;
        }

        float ApproxPercent(uint voltage)
        {
            int count = _values.Count;

            if(count == 0)
            {
                return -1;
            }

            if (voltage <= _values[0].Item1)
            {
                return _values[0].Item2;
            }

            if (voltage >= _values[count - 1].Item1)
            {
                return _values[count - 1].Item2;
            }
            int index = search(0, count - 1, voltage);
            if (index >= 0)
            {
                var selectedVoltage = _values[index].Item1;
                var selectedPercent = _values[index].Item2;

                if (voltage == selectedVoltage)
                {
                    return selectedPercent;
                }

                if (voltage > selectedVoltage && index + 1 < count)
                {
                    var nextVoltage = _values[index + 1].Item1;
                    var nextPercent = _values[index + 1].Item2;

                    var voltageDiff = nextVoltage - selectedVoltage;
                    var percentDiff = nextPercent - selectedPercent;

                    return selectedPercent + percentDiff * ((voltage - selectedVoltage) / (float)voltageDiff);
                }
                if (voltage < selectedVoltage && index > 0)
                {
                    var nextVoltage = _values[index - 1].Item1;
                    var nextPercent = _values[index - 1].Item2;

                    var voltageDiff = selectedVoltage - nextVoltage;
                    var percentDiff = selectedPercent - nextPercent;

                    return selectedPercent - percentDiff * ((selectedVoltage - voltage) / (float)voltageDiff);
                }


                return _values[index].Item2;
            }
            return -1;
        }

        public float GetPercent(uint voltage)
        {
            return ApproxPercent(voltage);
        }

        public void Serialize(Stream stream)
        {
            StreamWriter writer = new StreamWriter(stream);

            foreach (var v in _values)
            {
                writer.WriteLine("{} {}", v.Item1, v.Item2);
            }
        }

        public void Deserialize(Stream stream)
        {
            StreamReader reader = new StreamReader(stream);

            List<ReadOnlyMemory<char>> tokens = new List<ReadOnlyMemory<char>>();
            while (!reader.EndOfStream)
            {
                tokens.Clear();
                var line = reader.ReadLine();
                Tokenizer.Tokenize(tokens, line);
                if (tokens.Count >= 2)
                {
                    if (uint.TryParse(tokens[0].Span, out var voltage) &&
                        float.TryParse(tokens[1].Span, NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, out var percent))
                    {
                        Add(voltage, percent);
                    }
                }
            }
            Sort();
        }
    }
}
