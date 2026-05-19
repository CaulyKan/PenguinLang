# 22. EmperorPenguin IR

## Overview

IR（中间表示）是 EmperorPenguin 编译器后端的核心数据结构。它将 Bound Tree 的高级语义转换为一种抽象的、人类可读的指令序列，作为后续 LLVM IR 生成的前端。

**源文件位置**: `EmperorPenguin/src/ir/`

**核心文件**:
- `IRSourceLocation.penguin` — 源位置追踪
- `IRValue.penguin` — IR 值类型（寄存器、常量、标签）
- `IRInstruction.penguin` — 20 种核心指令
- `IRFunction.penguin` — IR 函数与参数
- `IRBuilder.penguin` — 指令发射器（Builder 模式）
- `IRGenerator.penguin` — Bound Tree → IR 转换器
- `IRPrinter.penguin` — IR 文本输出
- `IRModule.penguin` — IR 模块（函数集合）

---

## 设计原则

1. **高抽象层次**：屏蔽内存布局细节，无 alloca/load/store/GEP
2. **可读性优先**：每条指令有 `display()` 方法，生成人类友好的文本
3. **符号变量**：使用符号名（`%x`、`%this`）而非编号寄存器
4. **源位置追踪**：每条指令携带 `IRSourceLocation`，支持错误定位
5. **隐式内存**：字段访问通过 RDMBR/WRMBR，编译器自动处理分配策略

---

## IRSourceLocation

源位置信息，附加到每条指令上用于调试和错误报告。

```
IRSourceLocation
├── file_path: string    # 源文件路径
├── line: i64            # 行号（1-based）
└── column: i64          # 列号（1-based）
```

| 方法 | 说明 |
|------|------|
| `new(file_path, line, column)` | 构造函数 |
| `to_string() -> string` | 格式化为 `file:line:column` |

---

## IRValue 枚举

IR 指令的操作数和结果统一表示为 `IRValue` 枚举：

| 变体 | 类名 | 字段 | display 格式 |
|------|------|------|-------------|
| `named_reg` | IRNamedRegister | `name`, `ir_type`, `source_line`, `source_col` | `%name` |
| `temp_reg` | IRTempRegister | `index`, `ir_type` | `%tN` |
| `constant` | IRConstant | `value`, `ir_type` | `value` |
| `label` | IRLabel | `name` | `name:` |

**公共方法**：

| 方法 | 说明 |
|------|------|
| `get_ir_type() -> string` | 获取 IR 类型字符串 |
| `display() -> string` | 人类可读的文本表示 |

---

## 指令集（20 种核心指令）

`IRInstruction` 枚举包含 20 种变体，分为 6 个功能类别：

### 1. 常量与赋值（4 种）

#### CONST — 常量赋值

```
%result:ty = CONST value
```

| 字段 | 说明 |
|------|------|
| `result: IRValue` | 目标寄存器 |
| `value: string` | 常量值的文本表示 |
| `location: IRSourceLocation` | 源位置 |

#### ARG — 参数访问

```
%result:ty = ARG param_name index
```

| 字段 | 说明 |
|------|------|
| `result: IRValue` | 目标寄存器 |
| `param_name: string` | 参数名称 |
| `param_index: i64` | 参数索引 |
| `ir_type: string` | 参数类型 |
| `location: IRSourceLocation` | 源位置 |

#### ASSIGN — 变量赋值

```
%dest:ty = ASSIGN %src
```

| 字段 | 说明 |
|------|------|
| `dest: IRValue` | 目标寄存器 |
| `src: IRValue` | 源寄存器 |
| `location: IRSourceLocation` | 源位置 |

#### CAST — 类型转换

```
%result:to_ty = CAST %operand from_ty->to_ty
```

| 字段 | 说明 |
|------|------|
| `result: IRValue` | 目标寄存器 |
| `operand: IRValue` | 操作数 |
| `from_type: string` | 源类型 |
| `to_type: string` | 目标类型 |
| `location: IRSourceLocation` | 源位置 |

### 2. 算术与逻辑（2 种）

#### BINOP — 二元运算

```
%result:ty = BINOP op %left, %right
```

| 字段 | 说明 |
|------|------|
| `op: string` | 运算符（ADD/SUB/MUL/DIV/MOD/AND/OR/XOR/EQ/NE/LT/GT/LE/GE） |
| `left: IRValue` | 左操作数 |
| `right: IRValue` | 右操作数 |
| `result: IRValue` | 结果寄存器 |
| `ir_type: string` | 结果类型 |
| `location: IRSourceLocation` | 源位置 |

#### UNARYOP — 一元运算

```
%result:ty = UNARYOP op %operand
```

| 字段 | 说明 |
|------|------|
| `op: string` | 运算符（NEG/NOT/BITNOT） |
| `operand: IRValue` | 操作数 |
| `result: IRValue` | 结果寄存器 |
| `ir_type: string` | 结果类型 |
| `location: IRSourceLocation` | 源位置 |

### 3. 内存访问（2 种）

#### RDMBR — 读取成员（替代 GEP + LOAD）

```
%result:ty = RDMBR %obj, .field_name
```

| 字段 | 说明 |
|------|------|
| `result: IRValue` | 结果寄存器 |
| `obj: IRValue` | 对象引用 |
| `field_name: string` | 字段名 |
| `ir_type: string` | 字段类型 |
| `location: IRSourceLocation` | 源位置 |

#### WRMBR — 写入成员（替代 GEP + STORE）

```
WRMBR %obj, .field_name, %value
```

| 字段 | 说明 |
|------|------|
| `obj: IRValue` | 对象引用 |
| `field_name: string` | 字段名 |
| `value: IRValue` | 写入值 |
| `location: IRSourceLocation` | 源位置 |

### 4. 控制流（4 种）

#### BR — 无条件分支

```
BR target_label
```

#### BR_COND — 条件分支

```
BR_COND %cond, true_label, false_label
```

#### RET — 带值返回

```
RET %value
```

#### RET_VOID — 无值返回

```
RET_VOID
```

**IRInstruction 公共方法**：

| 方法 | 说明 |
|------|------|
| `display() -> string` | 指令的文本表示 |
| `is_terminator() -> bool` | 是否终止指令（RET/RET_VOID） |
| `is_control_flow() -> bool` | 是否控制流指令（BR/BR_COND/RET/RET_VOID） |

### 5. 函数调用（3 种）

#### CALL — 同步函数调用（有返回值）

```
%result:ret_ty = CALL @func_name(%arg1:ty1, %arg2:ty2, ...)
```

| 字段 | 说明 |
|------|------|
| `func_name: string` | 函数名 |
| `args: List<IRValue>` | 实参列表 |
| `result_value: IRValue` | 结果寄存器 |
| `ret_type: string` | 返回类型 |
| `location: IRSourceLocation` | 源位置 |

#### CALL_VOID — 无返回值函数调用

```
CALL @func_name(%arg1:ty1, ...)
```

#### CALL_VIRT — 虚函数调用

```
%result:ret_ty = CALL_VIRT %obj, slot=N(%arg1:ty1, ...)
```

| 字段 | 说明 |
|------|------|
| `obj: IRValue` | 对象引用 |
| `vtable_slot: i64` | vtable 槽位索引 |
| `args: List<IRValue>` | 实参列表 |
| `result_value: IRValue` | 结果寄存器 |
| `ret_type: string` | 返回类型 |
| `location: IRSourceLocation` | 源位置 |

### 6. 对象创建与枚举操作（5 种）

#### NEW — 对象创建

```
%result:ptr = NEW TypeName(%arg1:ty1, ...)
```

#### NEW_ENUM — 枚举变体创建

```
%result = NEW_ENUM EnumType.variant_name(%payload)
```

| 字段 | 说明 |
|------|------|
| `type_name: string` | 枚举类型名 |
| `variant_idx: i64` | 变体索引 |
| `variant_name: string` | 变体名称 |
| `payload: Option<IRValue>` | 载荷值（可选） |
| `result: IRValue` | 结果寄存器 |

#### ISENUM — 枚举变体检查

```
%result:bool = ISENUM %enum_value, %variant_idx
```

用于模式匹配：检查枚举值是否为指定变体。

#### RDENUM — 读取枚举载荷

```
%result:ty = RDENUM %enum_value, .variant_name
```

| 字段 | 说明 |
|------|------|
| `result: IRValue` | 结果寄存器 |
| `enum_value: IRValue` | 枚举值 |
| `variant_name: string` | 变体名称 |
| `payload_type: string` | 载荷类型 |

#### LABEL — 基本块标签

```
label_name:
```

---

## IRFunction

IR 函数，对应 Bound Tree 中的一个函数或 initial 块。

```
IRFunction
├── name: string                 # 函数名（内部标识符）
├── display_name: string         # 显示名称
├── return_type: string          # 返回类型
├── parameters: List<IRParameter>  # 参数列表
├── instructions: List<IRInstruction>  # 指令序列
├── next_temp: i64               # 临时寄存器计数器
├── next_label: i64              # 标签计数器
├── is_extern: bool              # 是否外部函数
├── source_file: string          # 源文件
├── source_line: i64             # 源行号
└── source_col: i64              # 源列号
```

### IRParameter

```
IRParameter
├── name: string        # 参数名
├── ir_type: string     # 参数类型
├── index: i64          # 参数索引
├── source_line: i64    # 源行号
└── source_col: i64     # 源列号
```

### IRFunction 方法

| 方法 | 说明 |
|------|------|
| `new(name, return_type)` | 构造函数 |
| `alloc_named_reg(name, ir_type, line, col) -> IRValue` | 分配命名寄存器 |
| `alloc_temp(ir_type) -> IRValue` | 分配临时寄存器 |
| `alloc_label(prefix) -> IRLabel` | 分配标签（自动编号） |
| `add_inst(inst)` | 添加指令到末尾 |
| `alloc_param(name, ir_type, line, col) -> IRValue` | 添加参数并分配寄存器 |
| `has_terminator() -> bool` | 最后一条指令是否终止指令 |
| `ends_with_control_flow() -> bool` | 最后一条或倒数第二条是否控制流指令 |

---

## IRBuilder

指令发射器，封装 IRFunction 的指令构建操作。每个 emit 方法自动分配临时寄存器并添加指令。

### emit 方法

| 方法 | 签名 | 生成的指令 |
|------|------|-----------|
| `emit_const` | `(value, ir_type, loc) -> IRValue` | CONST |
| `emit_const_direct` | `(dest, value, loc)` | CONST（指定目标） |
| `emit_const_i64` | `(value, loc) -> IRValue` | CONST（i64 类型） |
| `emit_const_bool` | `(value, loc) -> IRValue` | CONST（bool 类型） |
| `emit_const_string` | `(value, loc) -> IRValue` | CONST（string 类型） |
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
| `emit_call` | `(func_name, args, ret_type, loc) -> Option<IRValue>` | CALL 或 CALL_VOID |
| `emit_call_virt` | `(obj, slot, args, ret_type, loc) -> Option<IRValue>` | CALL_VIRT |
| `emit_new` | `(type_name, args, loc) -> IRValue` | NEW |
| `emit_new_enum` | `(type_name, variant_idx, variant_name, payload, loc) -> IRValue` | NEW_ENUM |
| `emit_isenum` | `(enum_value, variant_idx, loc) -> IRValue` | ISENUM |
| `emit_rdenum` | `(enum_value, variant_name, payload_type, loc) -> IRValue` | RDENUM |
| `emit_label` | `(label)` | LABEL |

### 辅助方法

| 方法 | 说明 |
|------|------|
| `alloc_temp(ir_type) -> IRValue` | 分配临时寄存器 |
| `make_loc(file_path, line, col) -> IRSourceLocation` | 构造源位置 |
| `void_value() -> IRValue` | 返回 void 常量值 |

---

## IRGenerator

Bound Tree → IR 转换器。遍历 Bound Tree 的定义、表达式和语句，生成对应的 IR 指令序列。

```
IRGenerator
├── builder: mut IRBuilder         # 当前指令构建器
├── symbol_regs: List<SymbolRegEntry>  # 符号 → 寄存器映射
├── loop_stack: List<LoopLabels>   # 循环标签栈（break/continue）
└── source_file: string            # 当前源文件
```

### 辅助类型

**SymbolRegEntry** — 符号到 IR 寄存器的映射：
```
SymbolRegEntry
├── symbol_full_name: string
└── value: IRValue
```

**LoopLabels** — 循环的入口和出口标签：
```
LoopLabels
├── header: IRLabel   # 循环头（continue 跳转目标）
└── exit: IRLabel     # 循环出口（break 跳转目标）
```

### 入口方法

| 方法 | 说明 |
|------|------|
| `generate(unit: BoundCompilationUnit) -> IRModule` | 主入口：将绑定编译单元转换为 IR 模块 |

### 定义级别方法

| 方法 | 说明 |
|------|------|
| `lower_definition(def, module)` | 分发定义（namespace/function/class/initial_routine） |
| `lower_namespace(def, module)` | 处理命名空间（递归处理子定义） |
| `lower_function_def(def, module)` | 处理函数定义（创建 IRFunction、发射参数、处理函数体） |
| `lower_class_def(def, module)` | 处理类定义（遍历方法和构造函数） |
| `lower_initial_routine(def, module)` | 处理 initial 块 |

### 表达式级别方法

| 方法 | BoundExpression 变体 | 说明 |
|------|---------------------|------|
| `lower_expression(expr) -> IRValue` | — | 分发表达式（13 种） |
| `lower_literal(expr) -> IRValue` | `literal` | 常量 → CONST |
| `lower_identifier(expr) -> IRValue` | `identifier` | 标识符 → 查找符号寄存器 |
| `lower_binary(expr) -> IRValue` | `binary` | 二元运算 → BINOP（支持链式运算） |
| `lower_binary_chain(expr, ir_type, idx, accum) -> IRValue` | — | 链式二元运算的递归处理 |
| `lower_unary(expr) -> IRValue` | `unary` | 一元运算 → UNARYOP |
| `lower_function_call(expr) -> IRValue` | `function_call` | 函数调用 → CALL/CALL_VOID/CALL_VIRT |
| `lower_code_block(expr) -> IRValue` | `code_block` | 代码块 → 顺序发射语句和尾表达式 |
| `lower_cast(expr) -> IRValue` | `cast_expr` | 类型转换 → CAST |
| `lower_if_expr(expr) -> IRValue` | `if_expr` | if 表达式 → BR_COND + 标签 |
| `lower_while_expr(expr) -> IRValue` | `while_expr` | while 循环 → BR/BR_COND + 标签 |
| `lower_member_access(expr) -> IRValue` | `member_access` | 成员访问 → RDMBR |
| `lower_new(expr) -> IRValue` | `new_expr` | 对象创建 → NEW |
| `lower_enum_variant(expr) -> IRValue` | `enum_variant` | 枚举变体 → NEW_ENUM |

### 语句级别方法

| 方法 | BoundStatement 变体 | 说明 |
|------|---------------------|------|
| `lower_statement(stmt)` | — | 分发语句 |
| `lower_let_decl(stmt)` | `let_decl` | 变量声明 → 符号注册 |
| `lower_assignment(stmt)` | `assignment` | 赋值 → ASSIGN/WRMBR |
| `lower_if_stmt(stmt)` | `if_stmt` | if 语句 → 委托给 `lower_if_expr` |
| `lower_while_stmt(stmt)` | `while_stmt` | while 语句 → 委托给 `lower_while_expr` |
| `lower_block(stmt)` | `block` | 块语句 → 顺序处理子语句 |
| `lower_break(stmt)` | `break_stmt` | break → BR（跳转到循环出口） |
| `lower_continue(stmt)` | `continue_stmt` | continue → BR（跳转到循环头） |

### 辅助方法

| 方法 | 说明 |
|------|------|
| `make_loc(line, col) -> IRSourceLocation` | 从当前源文件和行列号构造源位置 |
| `sym_loc(sym: Option<BoundSymbol>) -> IRSourceLocation` | 从绑定符号提取源位置 |
| `sym_loc_func(sym: Option<BoundFunctionSymbol>) -> IRSourceLocation` | 从函数符号提取源位置 |
| `resolve_vtable_slot(expr) -> i64` | 解析虚调用的 vtable 槽位 |
| `find_class_def_from_scope(scope) -> Option<BoundClassDefinition>` | 从作用域链查找类定义 |
| `wrap_stmt_as_expr(stmt) -> BoundExpression` | 将语句包装为 void 代码块表达式 |
| `wrap_stmt_option(stmt_opt) -> Option<BoundExpression>` | 包装可选语句 |
| `bound_type_to_ir_type(bt) -> string` | BoundType → IR 类型字符串 |
| `binary_op_to_ir(op) -> string` | AST BinaryOperator → IR 运算符字符串 |
| `unary_op_to_ir(op) -> string` | AST UnaryOperator → IR 运算符字符串 |
| `get_symbol_reg(symbol) -> Option<IRValue>` | 查找符号对应的 IR 寄存器 |
| `set_symbol_reg(symbol, value)` | 注册符号的 IR 寄存器 |
| `set_symbol_reg_by_name(name, value)` | 按名称注册寄存器 |
| `clean_function_name(full_name) -> string` | 清理函数名（去除前缀） |
| `reset_locals()` | 重置局部符号映射 |

### if/while 统一策略

`lower_if_stmt` 和 `lower_while_stmt` 通过 `wrap_stmt_as_expr` 将语句包装为 `BoundCodeBlockExpression`，然后委托给对应的 `lower_if_expr` / `lower_while_expr`。这避免了 if/while 在语句和表达式两个层面的重复实现。

---

## IRPrinter

将 IR 模块输出为人类可读的文本格式。

| 方法 | 说明 |
|------|------|
| `print_module(module) -> string` | 输出整个模块（所有函数） |
| `print_function(func) -> string` | 输出单个函数 |

### 输出格式示例

```
function @main(%x:i32, %y:i32) -> i32 {  ; line 5
  %t0:i32 = CONST 0
  %t1:i32 = BINOP ADD %x, %y
  RET %t1
}
```

---

## IRModule

IR 模块，包含一个源文件编译产生的所有函数。

```
IRModule
├── source_file: string              # 源文件路径
├── functions: List<IRFunction>      # 函数列表
└── entry_function: Option<IRFunction>  # 入口函数
```

| 方法 | 说明 |
|------|------|
| `add_function(func)` | 添加函数 |
| `find_function(name) -> Option<IRFunction>` | 按名查找函数 |

---

## Pipeline 位置

```
Source Code → Lexer → Parser → AST → SemanticModel → Bound Tree → IRGenerator → IR → IRPrinter → 文本输出
                                                                          ↓
                                                                    LLVM IR（未来）
```

IR 是编译器后端的起始点。它接收 Bound Tree（携带完整语义信息的中间表示），生成抽象的、平台无关的指令序列。未来将通过 LLVM lowering 将 IR 转换为 LLVM IR 进行机器码生成。
