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
                    else if (v is ReferenceRuntimeValue)
                        result!.AssignFrom(v.Clone());
                    else if (v is EnumRuntimeValue)
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
                var org = Interlocked.Exchange(ref atomic.I64ValueRef, other_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = org;
            });

            vm.Global.RegisterExternFunction("__builtin.AtomicI64.compare_exchange", (result, args) =>
            {
                var atomic = args[0].As<ReferenceRuntimeValue>().Fields["value"].As<BasicRuntimeValue>();
                var current_value = args[1].As<BasicRuntimeValue>().I64Value;
                var new_value = args[2].As<BasicRuntimeValue>().I64Value;
                var org = Interlocked.CompareExchange(ref atomic.I64ValueRef, new_value, current_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = org;
            });

            vm.Global.RegisterExternFunction("__builtin.AtomicI64.fetch_add", (result, args) =>
            {
                var atomic = args[0].As<ReferenceRuntimeValue>().Fields["value"].As<BasicRuntimeValue>();
                var add_value = (Int64)args[1].As<BasicRuntimeValue>().I64Value;
                var res = Interlocked.Add(ref atomic.I64ValueRef, add_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = res;
            });
        }

        public static void AddList(BabyPenguinVM vm)
        {
            foreach (var listType in vm.Model.ResolveTypeNode("_utils.List<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(listType.FullName() + ".new", (result, args) =>
                {
                    var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    self.ExternImplenmentationValue = new List<IRuntimeValue>();
                });

                vm.Global.RegisterExternFunction(listType.FullName() + ".at", (result, args) =>
                {
                    var list = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var items = list.ExternImplenmentationValue as List<IRuntimeValue>;
                    var index = (int)args[1].As<BasicRuntimeValue>().U64Value;
                    var enumResult = result!.As<EnumRuntimeSymbol>().EnumValue;
                    if (items != null && index >= 0 && index < items.Count)
                    {
                        var val = items[index];
                        enumResult.ContainingValue = val is BasicRuntimeValue || val is EnumRuntimeValue ? val.Clone() : val;
                        enumResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                    else
                    {
                        enumResult.ContainingValue = null;
                        enumResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                });

                vm.Global.RegisterExternFunction(listType.FullName() + ".push", (result, args) =>
                {
                    var list = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var items = (list.ExternImplenmentationValue as List<IRuntimeValue>)!;
                    var val = args[1];
                    items.Add(val is BasicRuntimeValue || val is EnumRuntimeValue ? val.Clone() : val);
                });

                vm.Global.RegisterExternFunction(listType.FullName() + ".pop", (result, args) =>
                {
                    var list = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var items = (list.ExternImplenmentationValue as List<IRuntimeValue>)!;
                    var enumResult = result!.As<EnumRuntimeSymbol>().EnumValue;
                    if (items.Count > 0)
                    {
                        var last = items[^1];
                        items.RemoveAt(items.Count - 1);
                        enumResult.ContainingValue = last;
                        enumResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                    else
                    {
                        enumResult.ContainingValue = null;
                        enumResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                });

                vm.Global.RegisterExternFunction(listType.FullName() + ".remove", (result, args) =>
                {
                    var list = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var items = (list.ExternImplenmentationValue as List<IRuntimeValue>)!;
                    var index = (int)args[1].As<BasicRuntimeValue>().U64Value;
                    if (index >= 0 && index < items.Count)
                    {
                        items.RemoveAt(index);
                    }
                });

                vm.Global.RegisterExternFunction(listType.FullName() + ".size", (result, args) =>
                {
                    var list = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var items = list.ExternImplenmentationValue as List<IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)(items?.Count ?? 0);
                });

                vm.Global.RegisterExternFunction(listType.FullName() + ".set", (result, args) =>
                {
                    var list = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var items = (list.ExternImplenmentationValue as List<IRuntimeValue>)!;
                    var index = (int)args[1].As<BasicRuntimeValue>().U64Value;
                    var val = args[2];
                    if (index >= 0 && index < items.Count)
                    {
                        items[index] = val is BasicRuntimeValue || val is EnumRuntimeValue ? val.Clone() : val;
                    }
                });
            }

            foreach (var queueType in vm.Model.ResolveTypeNode("_utils.Queue<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(queueType.FullName() + ".new", (result, args) =>
                {
                    var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    self.ExternImplenmentationValue = new List<IRuntimeValue>();
                });

                vm.Global.RegisterExternFunction(queueType.FullName() + ".enqueue", (result, args) =>
                {
                    var queue = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var items = (queue.ExternImplenmentationValue as List<IRuntimeValue>)!;
                    var val = args[1];
                    items.Add(val is BasicRuntimeValue || val is EnumRuntimeValue ? val.Clone() : val);
                });

                vm.Global.RegisterExternFunction(queueType.FullName() + ".dequeue", (result, args) =>
                {
                    var queue = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var items = (queue.ExternImplenmentationValue as List<IRuntimeValue>)!;
                    var enumResult = result!.As<EnumRuntimeSymbol>().EnumValue;
                    if (items.Count > 0)
                    {
                        var first = items[0];
                        items.RemoveAt(0);
                        enumResult.ContainingValue = first;
                        enumResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                    else
                    {
                        enumResult.ContainingValue = null;
                        enumResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                });

                vm.Global.RegisterExternFunction(queueType.FullName() + ".peek", (result, args) =>
                {
                    var queue = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var items = queue.ExternImplenmentationValue as List<IRuntimeValue>;
                    var enumResult = result!.As<EnumRuntimeSymbol>().EnumValue;
                    if (items != null && items.Count > 0)
                    {
                        enumResult.ContainingValue = items[0] is BasicRuntimeValue || items[0] is EnumRuntimeValue ? items[0].Clone() : items[0];
                        enumResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                    else
                    {
                        enumResult.ContainingValue = null;
                        enumResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                });

                vm.Global.RegisterExternFunction(queueType.FullName() + ".size", (result, args) =>
                {
                    var queue = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var items = queue.ExternImplenmentationValue as List<IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)(items?.Count ?? 0);
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
                    {
                        frameResult = res.Right!;
                        if (frameResult.ReturnStatus == ReturnStatus.Blocked)
                            break;
                    }
                }
            }
            else
            {
                var funcSymbol = target?.FunctionSymbol as FunctionSymbol ?? throw new BabyPenguinRuntimeException("cant find function symbol on " + target!.ToString(), code: ErrorCode.E_RUNTIME_LOOKUP);
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
                    {
                        frameResult = res.Right!;
                        if (frameResult.ReturnStatus == ReturnStatus.Blocked)
                            break;
                    }
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
            vm.Global.RegisterExternFunction("_utils.file_read_text", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                var content = System.IO.File.ReadAllText(path);
                result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = content;
            });

            vm.Global.RegisterExternFunction("_utils.file_write_text", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                var content = args[1].As<BasicRuntimeValue>().StringValue;
                System.IO.File.WriteAllText(path, content);
            });

            // Binary-mode file I/O for .penguin-lib metadata (read paths MUST be
            // binary so a .so/.dll tail + appended JSON survive; metadata is ASCII).
            vm.Global.RegisterExternFunction("_utils.file_size", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                long size = -1;
                try { size = new System.IO.FileInfo(path).Length; } catch { }
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = size;
            });

            vm.Global.RegisterExternFunction("_utils.file_read_range", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                var offset = args[1].As<BasicRuntimeValue>().I64Value;
                var size = args[2].As<BasicRuntimeValue>().I64Value;
                var text = "";
                if (offset >= 0 && size >= 0)
                {
                    try
                    {
                        using var fs = System.IO.File.OpenRead(path);
                        fs.Seek(offset, System.IO.SeekOrigin.Begin);
                        var buf = new byte[size];
                        int n = fs.Read(buf, 0, (int)size);
                        text = System.Text.Encoding.UTF8.GetString(buf, 0, n);
                    }
                    catch { }
                }
                result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = text;
            });

            vm.Global.RegisterExternFunction("_utils.file_append", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                var text = args[1].As<BasicRuntimeValue>().StringValue;
                try { System.IO.File.AppendAllText(path, text); } catch { }
            });

            vm.Global.RegisterExternFunction("_utils.exe_path", (result, args) =>
            {
                result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = System.Environment.ProcessPath ?? "";
            });

            vm.Global.RegisterExternFunction("_utils.mkdir", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                try
                {
                    System.IO.Directory.CreateDirectory(path);
                    result!.As<BasicRuntimeSymbol>().BasicValue.BoolValue = true;
                }
                catch
                {
                    result!.As<BasicRuntimeSymbol>().BasicValue.BoolValue = false;
                }
            });

            vm.Global.RegisterExternFunction("_utils.file_exists", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                result!.As<BasicRuntimeSymbol>().BasicValue.BoolValue = System.IO.File.Exists(path);
            });

            vm.Global.RegisterExternFunction("_utils.dir_exists", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                result!.As<BasicRuntimeSymbol>().BasicValue.BoolValue = System.IO.Directory.Exists(path);
            });

            vm.Global.RegisterExternFunction("_utils.dir_get_entries", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                string entries = "";
                try
                {
                    entries = string.Join("\n", System.IO.Directory.GetFileSystemEntries(path)
                        .Select(e => System.IO.Path.GetFileName(e)));
                }
                catch { }
                result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = entries;
            });

            vm.Global.RegisterExternFunction("_utils.create_temp_dir", (result, args) =>
            {
                var prefix = args[0].As<BasicRuntimeValue>().StringValue;
                if (string.IsNullOrEmpty(prefix)) prefix = "penguin";
                var baseDir = System.IO.Path.GetTempPath();
                // Each attempt derives a new GUID, so uniqueness is guaranteed
                // even across parallel processes; CreateDirectory is atomic and
                // throws if a (vanishingly unlikely) collision wins the race.
                string dir = "";
                for (int attempt = 0; ; attempt++)
                {
                    var candidate = System.IO.Path.Combine(baseDir, prefix + "_" + System.Guid.NewGuid().ToString("N"));
                    try
                    {
                        System.IO.Directory.CreateDirectory(candidate);
                        dir = candidate;
                        break;
                    }
                    catch
                    {
                        if (attempt >= 16) throw;
                    }
                }
                result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = dir;
            });

            vm.Global.RegisterExternFunction("__builtin._exec_cmd", (result, args) =>
            {
                var cmd = args[0].As<BasicRuntimeValue>().StringValue;
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/bin/sh",
                        Arguments = "-c \"" + cmd.Replace("\"", "\\\"") + "\"",
                        RedirectStandardOutput = false,
                        RedirectStandardError = false,
                        UseShellExecute = false
                    };
                    var process = System.Diagnostics.Process.Start(psi);
                    process!.WaitForExit();
                    result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = process.ExitCode;
                }
                catch
                {
                    result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = -1;
                }
            });
        }
        private static void AddArgs(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.__args_count", (result, args) =>
            {
                var commandLineArgs = vm.Global.CommandLineArgs;
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = commandLineArgs.Length;
            });

            vm.Global.RegisterExternFunction("__builtin.__args_get", (result, args) =>
            {
                var commandLineArgs = vm.Global.CommandLineArgs;
                var index = (int)args[0].As<BasicRuntimeValue>().I64Value;
                if (index >= 0 && index < commandLineArgs.Length)
                {
                    result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = commandLineArgs[index];
                }
                else
                {
                    result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = "";
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
