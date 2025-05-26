using System;
using System.Text;
using System.Text.RegularExpressions;

namespace PenguinLangSyntax
{
    public class Tools
    {
        public static string FormatPenguinLangSource(string source)
        {
            if (string.IsNullOrEmpty(source))
                return source;

            var lines = source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var result = new StringBuilder();
            var indent = 0;
            var inQuotes = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmedLine = line.Trim();

                // 检查是否为空行
                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    continue;
                }

                // 如果这一行以}开头，先减少缩进
                if (trimmedLine.StartsWith("}"))
                {
                    indent = Math.Max(0, indent - 1);
                }

                // 应用当前行的缩进
                result.Append(new string(' ', indent * 4));
                result.AppendLine(trimmedLine);

                // 处理引号内的内容
                for (int j = 0; j < line.Length; j++)
                {
                    if (line[j] == '"')
                    {
                        inQuotes = !inQuotes;
                    }
                    else if (!inQuotes)
                    {
                        if (line[j] == '{')
                        {
                            indent++;
                            break;
                        }
                    }
                }
            }

            return result.ToString().TrimEnd();
        }
    }
}