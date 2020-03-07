using System;
using System.Collections.Generic;

namespace Fusion.GKeys
{
    public static class Tokenizer
    {
        static void MoveToNewLine(ref int i, string input)
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
        static void SkipWhitespace(ref int i, string input)
        {
            for (; i < input.Length; i++)
            {
                if (!char.IsWhiteSpace(input[i]))
                {
                    return;
                }
            }
        }
        static void SkipComment(ref int i, string input)
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

        static ReadOnlyMemory<char> DefaultTokenParser(ref int i, string input)
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
                return input.AsMemory().Slice(start, i - start);
            }
            return null;
        }

        public delegate ReadOnlyMemory<char> TokenParsingFunc(ref int i, string input);
        public static List<ReadOnlyMemory<char>> Tokenize(string input, TokenParsingFunc tokenParser = null, bool supportComments = true)
        {
            List<ReadOnlyMemory<char>> tokens = new List<ReadOnlyMemory<char>>();
            Tokenize(tokens, input, tokenParser, supportComments);
            return tokens;
        }

        public static void Tokenize(List<ReadOnlyMemory<char>> r_Tokens, string input, TokenParsingFunc tokenParser = null, bool supportComments = true)
        {
            if (tokenParser == null)
            {
                tokenParser = DefaultTokenParser;
            }

            for (int i = 0; i < input.Length;)
            {
                SkipWhitespace(ref i, input);
                if (supportComments) SkipComment(ref i, input);

                if (i == input.Length)
                {
                    break;
                }

                var token = tokenParser(ref i, input);
                if (!token.IsEmpty)
                {
                    r_Tokens.Add(token);
                }
                else
                {
                    i++;
                }

            }
        }

    }
}
