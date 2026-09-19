# 22. PenguinLang IR

## 概述

寄存器型 IR 是 PenguinLang 各编译器共享的中间层。两个实现——BabyPenguin（C#）与 EmperorPenguin（PenguinLang 自举）——都把各自的语义树下降到**同一 IR 设计**：相同的指令集、相同的值模型（命名寄存器 `%x`、临时 `%tN`、标签 `name:`、全局 `@name`）、逐字节一致的 `display()` 文本。正是这个 IR 让两个编译器可以在跨编译器测试矩阵中互相替换。

共享的是**格式**而非代码：每个编译器用自己的语言重新实现 IR，二者之间没有 IR 文件交换（打印机只用于调试输出）。

| | BabyPenguin | EmperorPenguin |
|---|---|---|
| IR 定义 | `BabyPenguin/VirtualMachine/IRInstruction.cs`（597 行） | `EmperorPenguin/src/ir/IRInstruction.penguin`（786 行） |
| 生产者 | `BabyPenguin/VirtualMachine/IRGenerator.cs`——下降每个 `ICodeContainer` 的符号级 BabyPenguinIR（其文档字符串："Translates BabyPenguin's existing compiled IR into the new EmperorPenguin-compatible IR format"） | `EmperorPenguin/src/ir/IRGenerator.penguin`——下降 Bound 树 |
| 消费者 | VM 解释器（`RuntimeFrame`）与 C# 后端 | `LLVMEmitter.lower()` → LLVM IR 文本 |
| 调试转储 | `IRModule.Display()`、`BP_DUMP_IR=/tmp/bp_ir.txt` | `IRPrinter.print_module()` |

两个实现之间的已知差异：

* 间接调用：BabyPenguin 拆为 `IRCallFuncPtrInst` / `IRCallFuncPtrVoidInst`；EmperorPenguin 只有一个 `IRCallIndirect`。
* 裸指针：EmperorPenguin 增加了 `ADDRESS_OF` / `LOAD_PTR` / `STORE_PTR`（元内建 `#__address_of`/`#__load`/`#__store`），BabyPenguin 没有对应物。
* 协程状态：BabyPenguin 的 `IRRetInst` / `IRRetVoidInst` 携带 `ReturnStatus` 整数（Blocked/YieldNotFinished/Finished/YieldFinished）用于 VM 协程挂起；EmperorPenguin 的 `RET` 无状态字段。
* `CALL_VIRT` / `BOX` / `UNBOX` 两侧都有，但 BabyPenguin VM 解释器未实现（接口分派改走函数指针 `RDMBR` + `CALL_FUNC_PTR`）。

本页余下部分以 EmperorPenguin 实现为参照记录 IR 本身。

**EmperorPenguin 源码位置**：`EmperorPenguin/src/ir/`

**核心文件**：
- `IRSourceLocation.penguin` — source location tracking (alias shell for `ast.SourceLocation`)
- `IRValue.penguin` — IR value types (registers, constants, labels)
- `IRInstruction.penguin` — the 29 core instructions
- `IRFunction.penguin` — IR functions and parameters
- `IRBuilder.penguin` — instruction emitter (Builder pattern)
- `IRGenerator.penguin` — Bound Tree → IR converter
- `IRPrinter.penguin` — IR text output
- `IRModule.penguin` — IR module (collection of functions)

---

## 设计原则

1. **高抽象层级**：隐藏内存布局细节——没有 alloca/load/store/GEP
2. **可读性优先**：每条指令都有生成人类可读文本的 `display()` 方法
3. **符号化变量**：使用符号名（`%x`、`%this`）而非编号寄存器
4. **源位置跟踪**：每条指令携带 `SourceLocation`，支持错误定位
5. **隐式内存**：字段访问经 RDMBR/WRMBR；分配策略由编译器自动处理

---

## IRSourceLocation

`IRSourceLocation.penguin` 只保留空命名空间壳；源位置统一使用 `ast.SourceLocation`（IR 层直接以 `SourceLocation` 引用）。

```
SourceLocation (ast)
├── filename: string    # source file path
├── line: i64           # line number (1-based)
└── col: i64            # column number (1-based)
```

| 方法 | 描述 |
|------|------|
| `new(filename, line, col)` | Constructor |
| `copy() -> SourceLocation` | Copy |
| `to_string() -> string` | Formats as `file:line:col` |

---

## The IRValue Enum

IR 指令的操作数与结果统一表示为 `IRValue` 枚举：

| 变体 | 类名 | 字段 | 显示格式 |
|------|------|------|-------------|
| `named_reg` | IRNamedRegister | `name`, `ir_type`, `source_line`, `source_col` | `%name` |
| `temp_reg` | IRTempRegister | `index`, `ir_type` | `%tN` |
| `constant` | IRConstant | `value`, `ir_type` | `value` |
| `label` | IRLabel | `name` | `name:` |
| `global_ref` | IRGlobalRef | `name`, `ir_type` | `@name` |

**通用方法**：

| 方法 | 描述 |
|------|------|
| `get_ir_type() -> string` | Gets the IR type string |
| `display() -> string` | Human-readable text representation |

---

## 指令集（29 条核心指令）

`IRInstruction` 枚举含 29 个变体，分 6 个功能类别：

### 1. 常量与赋值（4）

#### CONST — Constant Assignment

```
%result:ty = CONST value
```

| 字段 | 描述 |
|------|------|
| `result: IRValue` | Destination register |
| `value: string` | Text representation of the constant value |
| `location: SourceLocation` | Source location |

#### ARG — Parameter Access

```
%result:ty = ARG param_name index
```

| 字段 | 描述 |
|------|------|
| `result: IRValue` | Destination register |
| `param_name: string` | Parameter name |
| `param_index: i64` | Parameter index |
| `ir_type: string` | Parameter type |
| `location: SourceLocation` | Source location |

#### ASSIGN — Variable Assignment

```
%dest:ty = ASSIGN %src
```

| 字段 | 描述 |
|------|------|
| `dest: IRValue` | Destination register |
| `src: IRValue` | Source register |
| `location: SourceLocation` | Source location |

#### CAST — Type Conversion

```
%result:to_ty = CAST %operand from_ty->to_ty
```

| 字段 | 描述 |
|------|------|
| `result: IRValue` | Destination register |
| `operand: IRValue` | Operand |
| `from_type: string` | Source type |
| `to_type: string` | Target type |
| `location: SourceLocation` | Source location |

### 2. 算术与逻辑（2）

#### BINOP — Binary Operation

```
%result:ty = BINOP op %left, %right
```

| 字段 | 描述 |
|------|------|
| `op: string` | Operator (ADD/SUB/MUL/DIV/MOD/AND/OR/XOR/EQ/NE/LT/GT/LE/GE) |
| `left: IRValue` | Left operand |
| `right: IRValue` | Right operand |
| `result: IRValue` | Result register |
| `ir_type: string` | Result type |
| `location: SourceLocation` | Source location |

#### UNARYOP — Unary Operation

```
%result:ty = UNARYOP op %operand
```

| 字段 | 描述 |
|------|------|
| `op: string` | Operator (NEG/NOT/BITNOT) |
| `operand: IRValue` | Operand |
| `result: IRValue` | Result register |
| `ir_type: string` | Result type |
| `location: SourceLocation` | Source location |

### 3. 内存访问（7）

#### RDMBR — Read Member (replaces GEP + LOAD)

```
%result:ty = RDMBR %obj, .field_name
```

| 字段 | 描述 |
|------|------|
| `result: IRValue` | Result register |
| `obj: IRValue` | Object reference |
| `field_name: string` | Field name |
| `ir_type: string` | Field type |
| `location: SourceLocation` | Source location |

#### WRMBR — Write Member (replaces GEP + STORE)

```
WRMBR %obj, .field_name, %value
```

| 字段 | 描述 |
|------|------|
| `obj: IRValue` | Object reference |
| `field_name: string` | Field name |
| `value: IRValue` | Value to write |
| `location: SourceLocation` | Source location |

#### GLOBAL_LOAD — Read a Global Variable

```
%result:ty = GLOBAL_LOAD @global_name
```

| 字段 | 描述 |
|------|------|
| `result: IRValue` | Result register |
| `global_name: string` | Global variable name |
| `ir_type: string` | Global variable type |
| `location: SourceLocation` | Source location |

#### GLOBAL_STORE — Write a Global Variable

```
GLOBAL_STORE @global_name, %value
```

| 字段 | 描述 |
|------|------|
| `global_name: string` | Global variable name |
| `value: IRValue` | Value to write |
| `location: SourceLocation` | Source location |

#### ADDRESS_OF — Address Of

```
%result:u64 = ADDRESS_OF %operand
```

| 字段 | 描述 |
|------|------|
| `result: IRValue` | Result register (u64 raw address) |
| `operand: IRValue` | Operand |
| `location: SourceLocation` | Source location |

#### LOAD_PTR — Load from a Raw Address

```
%result:ty = LOAD_PTR %addr
```

| 字段 | 描述 |
|------|------|
| `result: IRValue` | Result register |
| `addr: IRValue` | u64 raw address |
| `load_type: string` | Load type |
| `location: SourceLocation` | Source location |

#### STORE_PTR — Store to a Raw Address

```
STORE_PTR %addr, %value:store_type
```

| 字段 | 描述 |
|------|------|
| `addr: IRValue` | u64 raw address |
| `value: IRValue` | Value to write |
| `store_type: string` | Store type |
| `location: SourceLocation` | Source location |

### 4. 控制流（4）

#### BR — Unconditional Branch

```
BR target_label
```

#### BR_COND — Conditional Branch

```
BR_COND %cond, true_label, false_label
```

#### RET — Return with a Value

```
RET %value
```

#### RET_VOID — Return without a Value

```
RET_VOID
```

**IRInstruction 通用方法**：

| 方法 | 描述 |
|------|------|
| `display() -> string` | Text representation of the instruction |
| `is_terminator() -> bool` | Whether it is a terminator instruction (RET/RET_VOID) |
| `is_control_flow() -> bool` | Whether it is a control-flow instruction (BR/BR_COND/RET/RET_VOID) |

### 5. 函数调用（4）

#### CALL — Synchronous Function Call (with a return value)

```
%result:ret_ty = CALL @func_name(%arg1:ty1, %arg2:ty2, ...)
```

| 字段 | 描述 |
|------|------|
| `func_name: string` | Function name |
| `args: List<IRValue>` | Argument list |
| `result_value: IRValue` | Result register |
| `ret_type: string` | Return type |
| `location: SourceLocation` | Source location |

#### CALL_VOID — Function Call without a Return Value

```
CALL @func_name(%arg1:ty1, ...)
```

#### CALL_INDIRECT — Indirect Function Call

```
%result:ret_ty = CALL_INDIRECT %callee(%arg1:ty1, ...)
```

经函数指针值（`fun` 类型的字段/局部变量）调用；`callee` 是持有胖函数指针的 IRValue。

| 字段 | 描述 |
|------|------|
| `callee: IRValue` | Callee function pointer |
| `args: List<IRValue>` | Argument list |
| `result_value: IRValue` | Result register |
| `ret_type: string` | Return type |
| `location: SourceLocation` | Source location |

#### CALL_VIRT — Virtual Function Call

```
%result:ret_ty = CALL_VIRT %obj, interface=InterfaceName, slot=N(%arg1:ty1, ...)
```

| 字段 | 描述 |
|------|------|
| `obj: IRValue` | Object reference |
| `interface_id: string` | Interface identifier |
| `vtable_slot: i64` | vtable slot index |
| `args: List<IRValue>` | Argument list |
| `result_value: IRValue` | Result register |
| `ret_type: string` | Return type |
| `location: SourceLocation` | Source location |

### 6. 对象创建与类型操作（8）

#### NEW — Object Creation

```
%result:ptr = NEW TypeName(%arg1:ty1, ...)
```

#### NEW_ENUM — Enum Variant Creation

```
%result = NEW_ENUM EnumType.variant_name(%payload)
```

| 字段 | 描述 |
|------|------|
| `type_name: string` | Enum type name |
| `variant_idx: i64` | Variant index |
| `variant_name: string` | Variant name |
| `payload: Option<IRValue>` | Payload value (optional) |
| `result: IRValue` | Result register |

#### ISENUM — Enum Variant Check

```
%result:bool = ISENUM %enum_value, %variant_idx
```

用于模式匹配：检查枚举值是否为指定变体。

#### RDENUM — Read the Enum Payload

```
%result:ty = RDENUM %enum_value, .variant_name
```

| 字段 | 描述 |
|------|------|
| `result: IRValue` | Result register |
| `enum_value: IRValue` | Enum value |
| `variant_name: string` | Variant name |
| `payload_type: string` | Payload type |

#### ISINSTANCE — Runtime Type Check

```
%result:bool = ISINSTANCE %obj, type_id
```

经对象元数据做运行时类型检查：`type_id` 为接口时查 interface_map；为类名时比较 metadata->name。

| 字段 | 描述 |
|------|------|
| `result: IRValue` | Result register |
| `obj: IRValue` | Object reference |
| `type_id: string` | Interface name or class name |
| `location: SourceLocation` | Source location |

#### BOX — Boxing

```
%result:ptr = BOX %operand, source_type_name
```

堆分配并复制值类型（类或枚举）以用作接口引用（值类型 → 接口装箱方向是复制）。

| 字段 | 描述 |
|------|------|
| `result: IRValue` | Result register (ptr) |
| `operand: IRValue` | Operand |
| `source_type_name: string` | Source type name |
| `location: SourceLocation` | Source location |

#### UNBOX — Unboxing

```
%result:target_ir_type = UNBOX %operand, target_type_name
```

从堆指针取回值类型。UNBOX 是别名视图（复用 BOX 自身的存储），允许 `mut this` 接口方法写入对调用者可见的对象。

| 字段 | 描述 |
|------|------|
| `result: IRValue` | Result register |
| `operand: IRValue` | Operand (boxed pointer) |
| `target_type_name: string` | Target type name |
| `target_ir_type: string` | Target IR type |
| `location: SourceLocation` | Source location |

#### LABEL — Basic Block Label

```
label_name:
```

---

## IRFunction（IR 函数）

对应 Bound 树中一个函数或 initial 块的 IR 函数。

```
IRFunction
├── name: string                 # function name (internal identifier)
├── display_name: string         # display name
├── return_type: string          # return type
├── parameters: List<IRParameter>  # parameter list
├── instructions: List<IRInstruction>  # instruction sequence
├── next_temp: i64               # temporary register counter
├── next_label: i64              # label counter
├── is_extern: bool              # whether it is an extern function
├── is_lib_export_decl: bool     # dyn-lib export declaration (function body lives in the .penguin-lib, not a C-runtime extern)
├── location: SourceLocation     # source location
└── used_reg_names: List<string> # allocated register names (deduplication of same-named lets)
```

### IRParameter（IR 参数）

```
IRParameter
├── name: string        # parameter name
├── ir_type: string     # parameter type
├── index: i64          # parameter index
├── source_line: i64    # source line number
└── source_col: i64     # source column number
```

### IRFunction（IR 函数） Methods

| 方法 | 描述 |
|------|------|
| `new(name, return_type, loc)` | Constructor |
| `alloc_named_reg(name, ir_type, line, col) -> IRValue` | Allocates a named register (a `_N` suffix is appended automatically on name clashes) |
| `make_unique_reg_name(name) -> string` | Generates an unoccupied register name and registers it |
| `reg_name_taken(name) -> bool` | Whether the register name is already taken |
| `alloc_temp(ir_type) -> IRValue` | Allocates a temporary register |
| `alloc_label(prefix) -> IRLabel` | Allocates a label (auto-numbered) |
| `add_inst(inst)` | Appends an instruction to the end |
| `alloc_param(name, ir_type, line, col) -> IRValue` | Adds a parameter and allocates its register |
| `has_terminator() -> bool` | Whether the last instruction is a terminator instruction |
| `ends_with_control_flow() -> bool` | Whether the last or second-to-last instruction is a control-flow instruction |
| `to_string() -> string` | Function summary information |

---

## IRBuilder（IR 构建器）

指令发射器，封装 IRFunction 的指令构建操作。每个 emit 方法自动分配临时寄存器并追加指令。

### emit 方法

| 方法 | 签名 | 生成的指令 |
|------|------|-----------|
| `emit_const` | `(value, ir_type, loc) -> IRValue` | CONST |
| `emit_const_direct` | `(dest, value, loc)` | CONST (explicit destination) |
| `emit_const_i64` | `(value, loc) -> IRValue` | CONST (i64 type) |
| `emit_const_bool` | `(value, loc) -> IRValue` | CONST (bool type) |
| `emit_const_string` | `(value, loc) -> IRValue` | CONST (string type) |
| `emit_arg` | `(param_name, param_index, ir_type, loc) -> IRValue` | ARG |
| `emit_assign` | `(dest, src, loc)` | ASSIGN |
| `emit_cast` | `(operand, from_type, to_type, loc) -> IRValue` | CAST |
| `emit_binop` | `(op, left, right, ir_type, loc) -> IRValue` | BINOP |
| `emit_unaryop` | `(op, operand, ir_type, loc) -> IRValue` | UNARYOP |
| `emit_rdmbr` | `(obj, field_name, ir_type, loc) -> IRValue` | RDMBR |
| `emit_wrmbr` | `(obj, field_name, value, loc)` | WRMBR |
| `emit_br` | `(target, loc)` | BR |
| `emit_br_cond` | `(cond, true_lbl, false_lbl, loc)` | BR_COND |
| `emit_ret` | `(value, loc)` | RET |
| `emit_ret_void` | `(loc)` | RET_VOID |
| `emit_call` | `(func_name, args, ret_type, loc) -> Option<IRValue>` | CALL or CALL_VOID |
| `emit_call_indirect` | `(callee, args, ret_type, loc) -> Option<IRValue>` | CALL_INDIRECT |
| `emit_call_virt` | `(obj, interface_id, slot, args, ret_type, loc) -> Option<IRValue>` | CALL_VIRT |
| `emit_new` | `(type_name, args, loc) -> IRValue` | NEW |
| `emit_new_enum` | `(type_name, variant_idx, variant_name, payload, loc) -> IRValue` | NEW_ENUM |
| `emit_isenum` | `(enum_value, variant_idx, loc) -> IRValue` | ISENUM |
| `emit_rdenum` | `(enum_value, variant_name, payload_type, loc) -> IRValue` | RDENUM |
| `emit_isinstance` | `(obj, type_id, loc) -> IRValue` | ISINSTANCE |
| `emit_box` | `(operand, source_type_name, loc) -> IRValue` | BOX |
| `emit_unbox` | `(operand, target_type_name, target_ir_type, loc) -> IRValue` | UNBOX |
| `emit_label` | `(label)` | LABEL |
| `emit_global_load` | `(global_name, ir_type, loc) -> IRValue` | GLOBAL_LOAD |
| `emit_global_store` | `(global_name, value, loc)` | GLOBAL_STORE |
| `emit_address_of` | `(operand, loc) -> IRValue` | ADDRESS_OF |
| `emit_load_ptr` | `(addr, load_type, loc) -> IRValue` | LOAD_PTR |
| `emit_store_ptr` | `(addr, value, store_type, loc)` | STORE_PTR |

### 辅助方法

| 方法 | 描述 |
|------|------|
| `alloc_temp(ir_type) -> IRValue` | Allocates a temporary register |
| `make_loc(file_path, line, col) -> SourceLocation` | Constructs a source location |
| `void_value() -> IRValue` | Returns the void constant value |

---

## IRGenerator（IR 生成器）

Bound 树 → IR 转换器。遍历 Bound 树的定义、表达式与语句，生成对应的 IR 指令序列。

```
IRGenerator
├── builder: mut IRBuilder         # current instruction builder
├── symbol_regs: List<SymbolRegEntry>  # symbol → register mapping
├── try_site_counter: i64          # try/catch setjmp site counter (indexes the C runtime's jmp_buf table)
├── loop_stack: List<LoopLabels>   # loop label stack (break/continue)
├── current_module: mut IRModule   # current IR module
├── source_file: string            # current source file
└── verbose: i64                   # log level
```

### 辅助类型

**SymbolRegEntry**——符号到 IR 寄存器的映射：
```
SymbolRegEntry
├── symbol_full_name: string
└── value: IRValue
```

**LoopLabels**——循环的头标签与出口标签：
```
LoopLabels
├── header: IRLabel   # loop header (continue jump target)
└── exit: IRLabel     # loop exit (break jump target)
```

### 入口方法

| 方法 | 描述 |
|------|------|
| `generate(unit: BoundCompilationUnit) -> IRModule` | Main entry: converts the bound compilation unit into an IR module |

### 定义级方法

| 方法 | 描述 |
|------|------|
| `lower_definition(def, module)` | Dispatches definitions (function/namespace/class/interface/enum/initial_routine/global_var/impl_for — 8 kinds) |
| `lower_namespace(def, module)` | Handles a namespace (recursively processes sub-definitions) |
| `lower_function_def(def, module)` | Handles a function definition (creates the IRFunction, emits parameters, processes the function body) |
| `lower_class_def(def, module)` | Handles a class definition (walks methods, constructors, impl blocks) |
| `lower_enum_def(def, module)` | Handles an enum definition (walks methods, impl blocks) |
| `lower_interface_def(def, module)` | Handles an interface definition (walks default methods, impl blocks) |
| `lower_impl_for_def(def, module)` | Handles an `impl ... for ...` block (lowers each method inside it one by one) |
| `lower_initial_routine(def, module)` | Handles the initial block |
| `lower_global_var_def(def, module)` | Handles a global variable definition (literal initializers are written directly into the IRGlobalVariable; otherwise an init function is generated) |

### 表达式级方法

| 方法 | BoundExpression 变体 | 描述 |
|------|---------------------|------|
| `lower_expression(expr) -> IRValue` | — | Dispatches expressions (BoundExpression has 15 variants in total, 13 of which are dispatched here; `lambda_expr` and `meta_call` never appear in a bound tree that reaches the IR layer — meta calls are rewritten/spliced away at the semantic layer) |
| `lower_literal(expr) -> IRValue` | `literal` | Constant → CONST |
| `lower_identifier(expr) -> IRValue` | `identifier` | Identifier → looks up the symbol register (global variables → GLOBAL_LOAD) |
| `lower_binary(expr) -> IRValue` | `binary` | Binary operation → BINOP (supports chained operations; `is` → ISENUM/ISINSTANCE) |
| `lower_binary_chain(expr, ir_type, idx, accum) -> IRValue` | — | Recursive handling of chained binary operations |
| `lower_short_circuit(expr, is_and) -> IRValue` | — | `&&`/`\|\|` short-circuit evaluation (BR_COND + labels) |
| `lower_unary(expr) -> IRValue` | `unary` | Unary operation → UNARYOP (`#__address_of`/`#__load`/`#__store` meta builtins → ADDRESS_OF/LOAD_PTR/STORE_PTR) |
| `lower_function_call(expr) -> IRValue` | `function_call` | Function call → CALL/CALL_VOID/CALL_VIRT/CALL_INDIRECT |
| `lower_code_block(expr) -> IRValue` | `code_block` | Code block → emits statements and the trailing expression in order |
| `lower_cast(expr) -> IRValue` | `cast_expr` | Type conversion → CAST (value type ↔ interface → BOX/UNBOX) |
| `lower_if_expr(expr) -> IRValue` | `if_expr` | if expression → BR_COND + labels |
| `lower_while_expr(expr) -> IRValue` | `while_expr` | while loop → BR/BR_COND + labels |
| `lower_member_access(expr) -> IRValue` | `member_access` | Member access → RDMBR |
| `lower_new(expr) -> IRValue` | `new_expr` | Object creation → NEW |
| `lower_enum_variant(expr) -> IRValue` | `enum_variant` | Enum variant → NEW_ENUM |
| `lower_try_bind(expr) -> IRValue` | `try_bind` | try-bind pattern → BR_COND + payload extraction on the matching path |

### 语句级方法

| 方法 | BoundStatement 变体 | 描述 |
|------|---------------------|------|
| `lower_statement(stmt)` | — | Dispatches statements (expression/return/let_decl/assignment/if_stmt/try_catch/while_stmt/for_stmt/block/break/continue) |
| `lower_let_decl(stmt)` | `let_decl` | Variable declaration → symbol registration |
| `lower_assignment(stmt)` | `assignment` | Assignment → ASSIGN/WRMBR (global variables → GLOBAL_STORE) |
| `lower_if_stmt(stmt)` | `if_stmt` | if statement → delegates to `lower_if_expr` |
| `lower_try_catch(stmt)` | `try_catch` | try/catch → setjmp landing site (`try_site_counter` indexes the C runtime's jmp_buf table) |
| `lower_while_stmt(stmt)` | `while_stmt` | while statement → delegates to `lower_while_expr` |
| `lower_block(stmt)` | `block` | Block statement → processes sub-statements in order |
| `lower_break(stmt)` | `break_stmt` | break → BR (jumps to the loop exit) |
| `lower_continue(stmt)` | `continue_stmt` | continue → BR (jumps to the loop header) |

The return statement is handled inline in `lower_statement`: an integer-literal return value is converged (CAST) to the function's return type before emitting RET; otherwise RET_VOID is emitted. for_stmt has already been desugared into while at the semantic layer and is unreachable at the IR layer (reaching it triggers E_INTERNAL).

### 辅助方法

| 方法 | 描述 |
|------|------|
| `make_loc(line, col) -> SourceLocation` | Constructs a source location from the current source file and line/column numbers |
| `sym_loc(sym: Option<BoundSymbol>) -> SourceLocation` | Extracts a source location from a bound symbol |
| `sym_loc_func(sym: Option<BoundFunctionSymbol>) -> SourceLocation` | Extracts a source location from a function symbol |
| `resolve_vtable_slot(expr) -> i64` | Resolves the vtable slot of a virtual call |
| `resolve_interface_id(expr) -> string` | Resolves the interface identifier of a virtual call (uses the template's simple name) |
| `lower_call_receiver(expr) -> IRValue` | Resolves and lowers the receiver (`this`) of a method call |
| `ensure_extern_decl_from_def(def, module)` | Adds an extern declaration for a dyn-lib exported function (`is_lib_export_decl`) |
| `find_class_def_from_scope(scope) -> Option<BoundClassDefinition>` | Finds a class definition along the scope chain |
| `wrap_stmt_as_expr(stmt) -> BoundExpression` | Wraps a statement as a void code-block expression |
| `wrap_stmt_option(stmt_opt) -> Option<BoundExpression>` | Wraps an optional statement |
| `bound_type_to_ir_type(bt) -> string` | BoundType → IR type string |
| `binary_op_to_ir(op) -> string` | AST BinaryOperator → IR operator string |
| `unary_op_to_ir(op) -> string` | AST UnaryOperator → IR operator string |
| `get_symbol_reg(symbol) -> Option<IRValue>` | Looks up the IR register corresponding to a symbol |
| `set_symbol_reg(symbol, value)` | Registers the IR register for a symbol |
| `set_symbol_reg_by_name(name, value)` | Registers a register by name |
| `clean_function_name(full_name) -> string` | Cleans a function name (strips prefixes) |
| `reset_locals()` | Resets the local symbol mapping |

### 统一的 if/while 策略

`lower_if_stmt` and `lower_while_stmt` use `wrap_stmt_as_expr` to wrap the statement as a `BoundCodeBlockExpression`, then delegate to the corresponding `lower_if_expr` / `lower_while_expr`. This avoids duplicating the if/while implementation at both the statement and expression levels.

---

## IRPrinter（IR 打印器）

把 IR 模块输出为人类可读文本。

| 方法 | 描述 |
|------|------|
| `print_module(module) -> string` | Outputs the entire module (all functions) |
| `print_function(func) -> string` | Outputs a single function |

### 输出格式示例

```
function @main(%x:i32, %y:i32) -> i32 {  ; line 5
  %t0:i32 = CONST 0
  %t1:i32 = BINOP ADD %x, %y
  RET %t1
}
```

---

## IRModule（IR 模块）

IR 模块，包含单次编译产出的所有函数与全局变量。

```
IRModule
├── location: SourceLocation                  # source location
├── functions: List<IRFunction>               # function list
├── entry_functions: List<IRFunction>         # entry functions (initial blocks)
├── init_functions: List<IRFunction>          # global variable initializer functions
└── global_variables: List<IRGlobalVariable>  # global variable list
```

### IRGlobalVariable（IR 全局变量）

```
IRGlobalVariable
├── name: string             # global variable name
├── ir_type: string          # IR type
└── initializer_value: string # initializer value (literal text)
```

| 方法 | 描述 |
|------|------|
| `new(location)` | Constructor |
| `add_function(func)` | Adds a function |
| `add_global_variable(gv)` | Adds a global variable |
| `find_function(name) -> Option<IRFunction>` | Finds a function by name |
| `find_global_variable(name) -> Option<IRGlobalVariable>` | Finds a global variable by name |
| `to_string() -> string` | Module summary information |

---

## 流水线位置

```
Source Code → Lexer → Parser → AST → SemanticModel → Bound Tree → IRGenerator → IRModule
                                                                                  ├→ IRPrinter → IR text output (debugging)
                                                                                  └→ LLVMEmitter.lower() → LLVM IR text (.ll)
```

IR 是编译器后端的起点。它接收 Bound 树（携带完整语义信息的中间表示），生成抽象、平台无关的指令序列。`src/llvm/LLVMCompiler.penguin` 串联后端两个阶段：`build_ll()` 先用 `IRGenerator.generate()` 生成 IRModule，再由 `LLVMEmitter.lower()` 下降为 LLVM IR 文本并写出 `.ll` 文件。`.ll` 与平台无关；链接（clang + C 运行时库）由编译器之外的 `emperor`/`emperor.bat` 驱动脚本完成。
