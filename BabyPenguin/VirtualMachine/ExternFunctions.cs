using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using Mono.Cecil;
using BabyPenguin.Type;
using BabyPenguin.SemanticInterface;
using System.Collections;
using PenguinLangParser;
using PenguinLangParser.SyntaxNodes;

namespace BabyPenguin.VirtualMachine
{
    class ExternFunctions
    {
        public static void Build(BabyPenguinVM vm)
        {
            AddPrint(vm);
            AddCopy(vm);
            AddAtmoic(vm);
            AddList(vm);
            AddRoutineContext(vm);

            AddFile(vm);
            AddArgs(vm);
            AddStringBuilder(vm);
            AddStringHelpers(vm);
            AddSexpParser(vm);
            AddBitShift(vm);
            // AddMap(vm);
        }

        public static void AddPrint(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.print", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                vm.Global.Print(s);
            });

            vm.Global.RegisterExternFunction("__builtin.println", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                vm.Global.Print(s, true);
            });

            vm.Global.RegisterExternFunction("__builtin.eprint", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                Console.Error.Write(s);
            });

            vm.Global.RegisterExternFunction("__builtin.eprintln", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                Console.Error.WriteLine(s);
            });

            vm.Global.RegisterExternFunction("__builtin.exit", Exit);
        }

        private static IEnumerable<RuntimeBreak> Exit(RuntimeFrame frame, IRuntimeSymbol? resultVar, List<IRuntimeValue> args)
        {
            frame.Global.ExitCode = args[0].As<BasicRuntimeValue>().I32Value;
            yield return new RuntimeBreak(RuntimeBreakReason.Exited, frame);
        }

        public static void AddCopy(BabyPenguinVM vm)
        {
            foreach (var ICopy in vm.Model.ResolveTypeNode("__builtin.ICopy<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(ICopy.FullName() + ".copy", (result, args) =>
                {
                    var v = args[0];
                    if (v is BasicRuntimeValue)
                        result!.AssignFrom(v.Clone());
                });
            }
        }

        public static void AddAtmoic(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.AtomicI64.swap", (result, args) =>
            {
                var atomic = args[0].As<ReferenceRuntimeValue>().Fields["value"].As<BasicRuntimeValue>();
                var other_value = args[1].As<BasicRuntimeValue>().I64Value;
                var org = Interlocked.Exchange(ref atomic.I64Value, other_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = org;
            });

            vm.Global.RegisterExternFunction("__builtin.AtomicI64.compare_exchange", (result, args) =>
            {
                var atomic = args[0].As<ReferenceRuntimeValue>().Fields["value"].As<BasicRuntimeValue>();
                var current_value = args[1].As<BasicRuntimeValue>().I64Value;
                var new_value = args[2].As<BasicRuntimeValue>().I64Value;
                var org = Interlocked.CompareExchange(ref atomic.I64Value, new_value, current_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = org;
            });

            vm.Global.RegisterExternFunction("__builtin.AtomicI64.fetch_add", (result, args) =>
            {
                var atomic = args[0].As<ReferenceRuntimeValue>().Fields["value"].As<BasicRuntimeValue>();
                var add_value = (Int64)args[1].As<BasicRuntimeValue>().I64Value;
                var res = Interlocked.Add(ref atomic.I64Value, add_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = res;
            });
        }

        public static void AddList(BabyPenguinVM vm)
        {
            foreach (var queue in vm.Model.ResolveTypeNode("__builtin.Queue<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(queue.FullName() + ".new", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    impl.ExternImplenmentationValue = new Queue<IRuntimeValue>();
                });

                vm.Global.RegisterExternFunction(queue.FullName() + ".enqueue", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    q!.Enqueue(args[1]);
                });

                vm.Global.RegisterExternFunction(queue.FullName() + ".dequeue", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    if (q!.Count == 0)
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = null;
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = q.Dequeue();
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                });

                vm.Global.RegisterExternFunction(queue.FullName() + ".peek", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    if (q!.Count == 0)
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = null;
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = q.Peek();
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                });

                vm.Global.RegisterExternFunction(queue.FullName() + ".size", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)q!.Count;
                });
            }

            foreach (var list in vm.Model.ResolveTypeNode("__builtin.List<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(list.FullName() + ".new", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    impl.ExternImplenmentationValue = new List<IRuntimeValue>();
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".push", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    var val = args[1];
                    l!.Add(val is BasicRuntimeValue || val is EnumRuntimeValue ? val.Clone() : val);
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".pop", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    if (l!.Count == 0)
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = null;
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = l.Last();
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                        l.RemoveAt(l.Count - 1);
                    }
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".at", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var idx = args[1].As<BasicRuntimeValue>().U64Value;
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    if ((ulong)l!.Count <= idx)
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = null;
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = l.ElementAt((int)idx);
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".remove", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var idx = args[1].As<BasicRuntimeValue>().U64Value;
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    if ((ulong)l!.Count <= idx)
                    {
                        // remove out of range
                    }
                    else
                    {
                        l.RemoveAt((int)idx);
                    }
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".size", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)l!.Count;
                });
            }
        }

        private static IEnumerable<RuntimeBreak> RoutineContextCall(RuntimeFrame frame, IRuntimeSymbol? resultVar, List<IRuntimeValue> args)
        {
            var target = args[0].As<ReferenceRuntimeValue>().Fields["target"].As<FunctionRuntimeValue>() ?? throw new NotImplementedException();
            var frameRuntimeVar = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
            RuntimeFrameResult? frameResult = null;
            if (frameRuntimeVar.ExternImplenmentationValue is RuntimeFrame f)
            {
                foreach (var res in f.Run())
                {
                    if (res.IsLeft)
                        yield return res.Left!;
                    else
                        frameResult = res.Right!;
                }
            }
            else
            {
                // target is a async_fun<T>
                var funcSymbol = target?.FunctionSymbol as FunctionSymbol ?? throw new BabyPenguinRuntimeException("cant find function symbol on " + target!.ToString());
                List<IRuntimeValue> funcArguments = [];
                if (target!.TypeInfo.GenericArguments.Count > 1 && target.Owner is not NotInitializedRuntimeValue)
                {
                    funcArguments.Add(target.Owner);
                }
                else if (target.TypeInfo.GenericArguments.Count == 1 && target.Owner is NotInitializedRuntimeValue)
                {
                    // no arguments
                }

                var newFrame = new RuntimeFrame(funcSymbol.CodeContainer, frame.Global, funcArguments, frame);
                frameRuntimeVar.ExternImplenmentationValue = newFrame;
                foreach (var res in newFrame.Run())
                {
                    if (res.IsLeft)
                        yield return res.Left!;
                    else
                        frameResult = res.Right!;
                }
            }

            if (frameResult!.ReturnStatus == ReturnStatus.YieldFinished || frameResult!.ReturnStatus == ReturnStatus.Finished)
            {
                frame.ChildFrame = null;
                frameRuntimeVar.ExternImplenmentationValue = null;
            }

            var routineContextResult = args[0].As<ReferenceRuntimeValue>().Fields["result"].As<EnumRuntimeValue>();
            if (frameResult.ReturnValue != null)
            {
                routineContextResult.ContainingValue = frameResult.ReturnValue.Value;
                routineContextResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
            }
            else
            {
                routineContextResult.ContainingValue = null;
                routineContextResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
            }

            resultVar!.As<BasicRuntimeSymbol>().BasicValue.I64Value = (int)frameResult!.ReturnStatus;
        }

        public static void AddRoutineContext(BabyPenguinVM vm)
        {

            foreach (var routineContext in vm.Model.ResolveTypeNode("__builtin.RoutineContext<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(routineContext.FullName() + ".call", RoutineContextCall);
            }
        }

        private static void AddFile(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.file_read_text", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                var content = System.IO.File.ReadAllText(path);
                result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = content;
            });

            vm.Global.RegisterExternFunction("__builtin.file_write_text", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                var content = args[1].As<BasicRuntimeValue>().StringValue;
                System.IO.File.WriteAllText(path, content);
            });
        }
        private static void AddArgs(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.__args_init", (result, args) =>
            {
                var commandLineArgs = vm.Global.CommandLineArgs;
                var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>(); ;
                var list = self.ExternImplenmentationValue as List<IRuntimeValue>;
                var stringType = vm.Model.BasicTypeNodes.String.ToType(Mutability.Immutable);

                foreach (var arg in commandLineArgs)
                {
                    var basicStringValue = new BasicRuntimeValue(stringType);
                    basicStringValue.StringValue = arg;
                    list!.Add(basicStringValue);
                }
            });
        }

        private static void AddStringBuilder(BabyPenguinVM vm)
        {
            var stringBuilderType = vm.Model.ResolveTypeNode("__builtin.StringBuilder");
            if (stringBuilderType == null) return; // 如果代码中没用到，就跳过

            // 注册 StringBuilder.new()
            vm.Global.RegisterExternFunction(stringBuilderType.FullName() + ".new", (result, args) =>
            {
                // 1. 获取 'this' 实例
                var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>(); ;
                // 2. 创建一个 C# StringBuilder 并附加
                self.ExternImplenmentationValue = new System.Text.StringBuilder();
            });

            // 注册 StringBuilder.append(text: string)
            vm.Global.RegisterExternFunction(stringBuilderType.FullName() + ".append", (result, args) =>
            {
                // 1. 获取 'this' 实例和底层的 StringBuilder
                var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>(); ;
                var sb = self.ExternImplenmentationValue as System.Text.StringBuilder;

                // 2. 获取要追加的文本
                var textToAppend = args[1].As<BasicRuntimeValue>().StringValue;

                // 3. 执行 append 操作
                sb!.Append(textToAppend);
            });

            // 注册 StringBuilder.to_string() -> string
            vm.Global.RegisterExternFunction(stringBuilderType.FullName() + ".to_string", (result, args) =>
            {
                // 1. 获取 'this' 实例和底层的 StringBuilder
                var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>(); ;
                var sb = self.ExternImplenmentationValue as System.Text.StringBuilder;

                // 2. 将结果赋给返回值
                result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = sb!.ToString();
            });
        }

        private static void AddStringHelpers(BabyPenguinVM vm)
        {
            // Register string helper functions
            vm.Global.RegisterExternFunction("__builtin.string_length", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = s.Length;
            });

            vm.Global.RegisterExternFunction("__builtin.string_find", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                var substring = args[1].As<BasicRuntimeValue>().StringValue;
                var index = s.IndexOf(substring);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = index;
            });

            vm.Global.RegisterExternFunction("__builtin.string_find_from", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                var substring = args[1].As<BasicRuntimeValue>().StringValue;
                var startIndex = (int)args[2].As<BasicRuntimeValue>().I64Value;
                var index = s.IndexOf(substring, startIndex);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = index;
            });

            vm.Global.RegisterExternFunction("__builtin.string_substring", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                var start = (int)args[1].As<BasicRuntimeValue>().I64Value;
                var length = args.Count > 2 ? (int)args[2].As<BasicRuntimeValue>().I64Value : s.Length - start;
                if (start < 0) start = 0;
                if (start + length > s.Length) length = s.Length - start;
                if (start >= s.Length || length < 0)
                {
                    result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = "";
                }
                else
                {
                    result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = s.Substring(start, length);
                }
            });

            vm.Global.RegisterExternFunction("__builtin.string_char_at", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                var index = (int)args[1].As<BasicRuntimeValue>().I64Value;
                if (index >= 0 && index < s.Length)
                {
                    result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = s[index].ToString();
                }
                else
                {
                    result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = "";
                }
            });

            vm.Global.RegisterExternFunction("__builtin.string_char_code", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                if (s.Length > 0)
                {
                    result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = (long)s[0];
                }
                else
                {
                    result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = -1;
                }
            });

            vm.Global.RegisterExternFunction("__builtin.string_to_int", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                if (long.TryParse(s.Trim(), out var value))
                {
                    result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = value;
                }
                else
                {
                    result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = 0;
                }
            });
        }

        private static void AddBitShift(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.lshift", (result, args) =>
            {
                var value = args[0].As<BasicRuntimeValue>().I64Value;
                var shift = args[1].As<BasicRuntimeValue>().I64Value;
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = value << (int)shift;
            });

            vm.Global.RegisterExternFunction("__builtin.rshift", (result, args) =>
            {
                var value = args[0].As<BasicRuntimeValue>().I64Value;
                var shift = args[1].As<BasicRuntimeValue>().I64Value;
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = value >> (int)shift;
            });
        }

        private static void AddSexpParser(BabyPenguinVM vm)
        {
            // Register sexp.SexpParser.roundtrip(input: string) -> string
            var sexpSexpParserType = vm.Model.ResolveTypeNode("sexp.SexpParser");
            if (sexpSexpParserType != null)
            {
                vm.Global.RegisterExternFunction(sexpSexpParserType.FullName() + ".roundtrip", (result, args) =>
                {
                    var input = args[1].As<BasicRuntimeValue>().StringValue;
                    var output = SexpParserUtils.Roundtrip(input);
                    result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = output;
                });
            }
        }
    }

    // S-expression parser utilities - uses JSON as intermediate format
    internal static class SexpParserUtils
    {
        // Tokenize S-exp to JSON string
        public static string TokenizeToJson(string input)
        {
            var tokens = SexpLexer.Tokenize(input);
            return System.Text.Json.JsonSerializer.Serialize(tokens);
        }

        // Parse tokens JSON to node JSON
        public static string ParseJson(string tokenJson)
        {
            var tokens = System.Text.Json.JsonSerializer.Deserialize<List<SexpToken>>(tokenJson);
            var node = SexpParser.Parse(tokens);
            return System.Text.Json.JsonSerializer.Serialize(node);
        }

        // Format node JSON to S-exp string
        public static string FormatJson(string nodeJson)
        {
            var node = System.Text.Json.JsonSerializer.Deserialize<SexpNode>(nodeJson);
            return FormatNode(node);
        }

        // Roundtrip: parse and format
        public static string Roundtrip(string input)
        {
            var tokens = SexpLexer.Tokenize(input);
            var node = SexpParser.Parse(tokens);
            return FormatNode(node);
        }

        private static string FormatNode(SexpNode node, int indent = 0)
        {
            if (node == null) return "nil";

            var sb = new System.Text.StringBuilder();
            if (node.Type == "list")
            {
                sb.Append('(');

                // Check if any child is a keyword attribute (multi-line format)
                bool hasKeywords = node.Children.Any(c => c.Type == "keyword" || (c.Type == "list" && c.Children.Count > 0 && c.Children[0].Type == "keyword"));

                if (hasKeywords)
                {
                    sb.Append('\n');
                    for (int i = 0; i < node.Children.Count; i++)
                    {
                        for (int s = 0; s < indent + 2; s++) sb.Append(' ');
                        sb.Append(FormatNode(node.Children[i], indent + 2));
                        if (i < node.Children.Count - 1)
                            sb.Append('\n');
                    }
                }
                else
                {
                    for (int i = 0; i < node.Children.Count; i++)
                    {
                        if (i > 0) sb.Append(' ');
                        sb.Append(FormatNode(node.Children[i], indent + 1));
                    }
                }
                sb.Append(')');
            }
            else if (node.Type == "string")
            {
                sb.Append('"').Append(EscapeString(node.Value)).Append('"');
            }
            else
            {
                sb.Append(node.Value);
            }
            return sb.ToString();
        }

        private static string EscapeString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }

    // S-expression token
    internal class SexpToken
    {
        public string Type { get; set; } = "symbol";
        public string Value { get; set; } = "";
    }

    // S-expression node
    internal class SexpNode
    {
        public string Type { get; set; } = "nil";
        public string Value { get; set; } = "";
        public List<SexpNode> Children { get; set; } = new();
    }

    // S-expression lexer
    internal class SexpLexer
    {
        public static List<SexpToken> Tokenize(string input)
        {
            var tokens = new List<SexpToken>();
            int pos = 0;

            while (pos < input.Length)
            {
                while (pos < input.Length && char.IsWhiteSpace(input[pos])) pos++;
                if (pos >= input.Length) break;

                char c = input[pos];

                if (c == '(')
                {
                    tokens.Add(new SexpToken { Type = "lparen" });
                    pos++;
                }
                else if (c == ')')
                {
                    tokens.Add(new SexpToken { Type = "rparen" });
                    pos++;
                }
                else if (c == '"')
                {
                    tokens.Add(new SexpToken { Type = "string", Value = ReadString(input, ref pos) });
                }
                else
                {
                    var value = ReadSymbol(input, ref pos);
                    if (value == "true")
                        tokens.Add(new SexpToken { Type = "true", Value = "true" });
                    else if (value == "false")
                        tokens.Add(new SexpToken { Type = "false", Value = "false" });
                    else if (value == "nil")
                        tokens.Add(new SexpToken { Type = "nil", Value = "nil" });
                    else if (value.StartsWith(":"))
                        tokens.Add(new SexpToken { Type = "keyword", Value = value });
                    else if (IsNumber(value))
                        tokens.Add(new SexpToken { Type = "number", Value = value });
                    else
                        tokens.Add(new SexpToken { Type = "symbol", Value = value });
                }
            }

            return tokens;
        }

        private static string ReadString(string input, ref int pos)
        {
            pos++;
            var sb = new System.Text.StringBuilder();
            while (pos < input.Length)
            {
                char c = input[pos++];
                if (c == '"') break;
                if (c == '\\' && pos < input.Length)
                {
                    char escaped = input[pos++];
                    sb.Append(escaped switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '"' => '"',
                        '\\' => '\\',
                        _ => escaped
                    });
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string ReadSymbol(string input, ref int pos)
        {
            int start = pos;
            while (pos < input.Length && !char.IsWhiteSpace(input[pos]) && input[pos] != '(' && input[pos] != ')')
            {
                pos++;
            }
            return input.Substring(start, pos - start);
        }

        private static bool IsNumber(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                if (!char.IsDigit(c) && c != '-' && c != '+') return false;
            }
            return true;
        }
    }

    // S-expression parser
    internal class SexpParser
    {
        private List<SexpToken> tokens = new();
        private int pos;

        public static SexpNode Parse(List<SexpToken> tokenList)
        {
            var parser = new SexpParser { tokens = tokenList, pos = 0 };
            return parser.ParseNode();
        }

        private SexpNode ParseNode()
        {
            if (pos >= tokens.Count) return new SexpNode { Type = "nil" };

            var token = tokens[pos];
            if (token.Type == "lparen")
            {
                pos++;
                var node = new SexpNode { Type = "list" };
                while (pos < tokens.Count && tokens[pos].Type != "rparen")
                {
                    node.Children.Add(ParseNode());
                }
                if (pos < tokens.Count) pos++;
                return node;
            }

            pos++;
            return token.Type switch
            {
                "string" => new SexpNode { Type = "string", Value = token.Value },
                "number" => new SexpNode { Type = "number", Value = token.Value },
                "keyword" => new SexpNode { Type = "keyword", Value = token.Value },
                "true" => new SexpNode { Type = "true", Value = "true" },
                "false" => new SexpNode { Type = "false", Value = "false" },
                "nil" => new SexpNode { Type = "nil", Value = "nil" },
                _ => new SexpNode { Type = "symbol", Value = token.Value }
            };
        }
    }

    // 需要一个辅助类来比较 IRuntimeValue
    public class RuntimeValueComparer : IEqualityComparer<IRuntimeValue>
    {
        public bool Equals(IRuntimeValue? x, IRuntimeValue? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            // 对于 BasicRuntimeValue，比较其内部值
            if (x is BasicRuntimeValue bx && y is BasicRuntimeValue by)
            {
                if (bx.TypeInfo.Type != by.TypeInfo.Type) return false;
                return bx.TypeInfo.Type switch
                {
                    TypeEnum.Bool => bx.BoolValue == by.BoolValue,
                    TypeEnum.U8 => bx.U8Value == by.U8Value,
                    TypeEnum.U16 => bx.U16Value == by.U16Value,
                    TypeEnum.U32 => bx.U32Value == by.U32Value,
                    TypeEnum.U64 => bx.U64Value == by.U64Value,
                    TypeEnum.I8 => bx.I8Value == by.I8Value,
                    TypeEnum.I16 => bx.I16Value == by.I16Value,
                    TypeEnum.I32 => bx.I32Value == by.I32Value,
                    TypeEnum.I64 => bx.I64Value == by.I64Value,
                    TypeEnum.Float => bx.FloatValue == by.FloatValue,
                    TypeEnum.Double => bx.DoubleValue == by.DoubleValue,
                    TypeEnum.String => bx.StringValue == by.StringValue,
                    TypeEnum.Char => bx.CharValue == by.CharValue,
                    _ => false // 其他类型不应作为基本值进行比较
                };
            }
            // 对于 ReferenceRuntimeValue，比较其引用 ID
            else if (x is ReferenceRuntimeValue rx && y is ReferenceRuntimeValue ry)
            {
                return rx.RefId == ry.RefId;
            }
            // 对于 FunctionRuntimeValue，比较其函数符号
            else if (x is FunctionRuntimeValue fx && y is FunctionRuntimeValue fy)
            {
                return fx.FunctionSymbol.FullName() == fy.FunctionSymbol.FullName();
            }
            // 对于 EnumRuntimeValue，比较其值和类型
            else if (x is EnumRuntimeValue ex && y is EnumRuntimeValue ey)
            {
                return ex.TypeInfo.FullName() == ey.TypeInfo.FullName() &&
                       ex.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value == ey.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value &&
                       Equals(ex.ContainingValue, ey.ContainingValue);
            }

            return false;
        }

        public int GetHashCode(IRuntimeValue obj)
        {
            if (obj is null) return 0;

            if (obj is BasicRuntimeValue bx)
            {
                return bx.TypeInfo.Type switch
                {
                    TypeEnum.Bool => bx.BoolValue.GetHashCode(),
                    TypeEnum.U8 => bx.U8Value.GetHashCode(),
                    TypeEnum.U16 => bx.U16Value.GetHashCode(),
                    TypeEnum.U32 => bx.U32Value.GetHashCode(),
                    TypeEnum.U64 => bx.U64Value.GetHashCode(),
                    TypeEnum.I8 => bx.I8Value.GetHashCode(),
                    TypeEnum.I16 => bx.I16Value.GetHashCode(),
                    TypeEnum.I32 => bx.I32Value.GetHashCode(),
                    TypeEnum.I64 => bx.I64Value.GetHashCode(),
                    TypeEnum.Float => bx.FloatValue.GetHashCode(),
                    TypeEnum.Double => bx.DoubleValue.GetHashCode(),
                    TypeEnum.String => bx.StringValue.GetHashCode(),
                    TypeEnum.Char => bx.CharValue.GetHashCode(),
                    _ => obj.GetHashCode() // Fallback
                };
            }
            else if (obj is ReferenceRuntimeValue rx)
            {
                return rx.RefId.GetHashCode();
            }
            else if (obj is FunctionRuntimeValue fx)
            {
                return fx.FunctionSymbol.FullName().GetHashCode();
            }
            else if (obj is EnumRuntimeValue ex)
            {
                return HashCode.Combine(ex.TypeInfo.FullName().GetHashCode(),
                                        ex.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value.GetHashCode(),
                                        ex.ContainingValue?.GetHashCode() ?? 0);
            }

            return obj.GetHashCode();
        }
    }
}
