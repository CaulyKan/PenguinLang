namespace PenguinLangSyntax
{
    public class Tools
    {
        public static string FormatPenguinLangSource(string source)
        {
            if (string.IsNullOrEmpty(source))
                return source;

            var result = new System.Text.StringBuilder();
            int indentLevel = 0;
            bool isNewLine = true;
            bool inString = false;
            bool escapeNext = false;
            bool lastLineWasEmpty = false;

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];

                if (escapeNext)
                {
                    result.Append(c);
                    escapeNext = false;
                    continue;
                }

                if (c == '\\')
                {
                    escapeNext = true;
                    result.Append(c);
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    result.Append(c);
                    continue;
                }

                if (inString)
                {
                    result.Append(c);
                    continue;
                }

                if (isNewLine)
                {
                    if (c == '}' && indentLevel > 0)
                        indentLevel--;

                    bool currentLineIsEmpty = true;
                    for (int j = i; j < source.Length; j++)
                    {
                        if (source[j] == '\n' || source[j] == '\r')
                            break;
                        if (!char.IsWhiteSpace(source[j]))
                        {
                            currentLineIsEmpty = false;
                            break;
                        }
                    }

                    if (lastLineWasEmpty && currentLineIsEmpty)
                    {
                        while (i < source.Length && source[i] != '\n' && source[i] != '\r')
                            i++;
                        if (i < source.Length && source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
                            i++;
                        i++;
                        continue;
                    }

                    result.Append(new string(' ', indentLevel * 4));
                    isNewLine = false;
                    lastLineWasEmpty = currentLineIsEmpty;
                }

                if (c == '{')
                {
                    result.Append(c);
                    result.AppendLine();
                    indentLevel++;
                    isNewLine = true;
                    lastLineWasEmpty = false;
                }
                else if (c == '}')
                {
                    result.AppendLine();
                    result.Append(new string(' ', indentLevel * 4));
                    result.Append(c);
                    result.AppendLine();
                    isNewLine = true;
                    lastLineWasEmpty = false;
                }
                else if (c == ';')
                {
                    result.Append(c);
                    result.AppendLine();
                    isNewLine = true;
                    lastLineWasEmpty = false;
                }
                else if (c == ' ' || c == '\t')
                {
                    if (!isNewLine)
                        result.Append(' ');
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
                        i++;
                    result.AppendLine();
                    isNewLine = true;
                }
                else
                {
                    result.Append(c);
                }
            }

            return result.ToString().TrimEnd();
        }
    }
}