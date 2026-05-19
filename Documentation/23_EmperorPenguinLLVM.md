# 23. EmperorPenguin LLVM Lowering

## Overview

本文档定义 EmperorPenguin IR 到 LLVM IR 的 lowering（降低）策略，包括所有类型的内存布局、数据结构表示方式，以及每条 IR 指令到 LLVM IR 的转换规则。

**设计参考**: `.claude/plans/emperor-penguin-ir.md`

**目标平台**: LLVM IR（通过 LLVM C API 或 llvmlite 生成）

**核心设计决策**:
- 通过 `ICopy`/`IRef` 标记接口区分值类型和引用类型 class
- 引用类型 class（IRef）变量持有**直接指向堆对象的指针**
- 引用类型对象头部包含 `metadata` 指针
- 值类型 class（ICopy）在栈上分配，无 metadata header，按值复制
- 类引用和接口引用是**同一个指针**（单指针设计，仅 IRef class）
- 枚举（enum）为**值类型**，使用 tagged union 表示
- 原始类型直接映射为 LLVM 原始类型

---

## 1. 类型映射

### 1.1 原始类型

| PenguinLang 类型 | LLVM IR 类型 | 大小 (bytes) | 对齐 (bytes) | 说明 |
|-----------------|-------------|-------------|-------------|------|
| `void` | `void` | 0 | — | 无值 |
| `bool` | `i8` | 1 | 1 | 布尔值（0/1） |
| `i8` | `i8` | 1 | 1 | 有符号 8 位整数 |
| `i16` | `i16` | 2 | 2 | 有符号 16 位整数 |
| `i32` | `i32` | 4 | 4 | 有符号 32 位整数 |
| `i64` | `i64` | 8 | 8 | 有符号 64 位整数 |
| `u8` ~ `u64` | 同 signed | 同 | 同 | 无符号使用相同 LLVM 类型，符号性由指令决定 |
| `f32` | `float` | 4 | 4 | IEEE 754 单精度 |
| `f64` | `double` | 8 | 8 | IEEE 754 双精度 |
| `char` | `i32` | 4 | 4 | Unicode 码点 |
| `string` | `ptr` | 8 | 8 | 指向 PenguinString 的指针（引用类型） |

### 1.2 ICopy/IRef 值/引用类型分类

EmperorPenguin 通过 `ICopy` 和 `IRef` 两个标记接口区分值类型和引用类型：

- **`ICopy`**: 标记为值类型（栈分配，按值传递/复制语义）
- **`IRef`**: 标记为引用类型（堆分配，按指针传递/共享语义）
- **自动分类**: 如果 class 未显式实现 `ICopy` 或 `IRef`：
  - 所有字段均为值类型 → 自动为值类型（等效 ICopy）
  - 任一字段为引用类型，或分类过程中检测到循环依赖 → 引用类型（等效 IRef）
- **冲突检测**: 同时实现 `ICopy` 和 `IRef` 为编译错误
- **默认行为**: 原始类型（除 string）、enum 始终为值类型；string、interface 始终为引用类型

### 1.3 类型分类与 LLVM 表示策略

| BoundType 分类 | is_value_type | LLVM 表示 | 传递方式 |
|---------------|--------------|----------|---------|
| 原始类型（除 string） | 是 | 直接 LLVM 原始类型 | 按值传递 |
| enum | 是 | tagged union struct `{ ptr metadata, i32, payload }` | 按值传递 |
| class（值类型，impl ICopy） | 是 | LLVM struct（内联，无 metadata header） | 按值传递（整体复制） |
| class（引用类型，impl IRef） | 否 | `ptr`（直接指向堆对象） | 按指针传递 |
| interface | 否（引用类型） | `ptr`（与类引用相同指针） | 按指针传递 |
| string | 否（引用类型） | `ptr`（指向 PenguinString） | 按指针传递 |
| function type | — | `{ ptr, ptr }`（代码指针 + 环境指针） | 按值传递 |

---

## 2. 数据结构内存布局

### 2.1 引用类型 class（impl IRef，堆分配）

**核心设计**: 引用类型变量持有的是**直接指向堆对象的指针**。字段访问只需一次解引用。

```
// 变量持有的:
//   ptr → Object (直接指针，一次解引用访问字段)

// 对象布局 (每个 class 类型不同):
struct Object {
    ptr metadata;                // offset 0:  → ClassMetadata (全局常量)
    // fields...                 // offset 8:  字段数据
};
```

**访问路径**:
```
let obj: MyClass = ...;          // obj 是 ptr (直接指向对象)
obj.x                            // obj+8           (1 次解引用)
obj.z                            // obj+24          (1 次解引用，得到另一对象指针)
obj.z.x                          // obj+24→+8       (2 次解引用)
```

### 2.2 值类型 class（impl ICopy，栈分配）

值类型 class 无 metadata header，直接内联存储字段。变量持有值本身（非指针），在 LLVM 中表示为 struct 类型。

```
// 值类型 class 布局 (无 header):
struct ValueClass {
    // fields...                 // offset 0:  直接从字段开始
};

// LLVM struct 类型:
%class.Point = type { i32, i32 }  ; { x, y }
```

**关键差异**:
- 无 `ptr metadata` 头部
- 字段从 offset 0 开始（而非 offset 8）
- 按值复制时整体 memcpy
- 不能作为接口引用（无 metadata 用于虚分派）

### 2.3 Class 内存布局详细规则

根据 ICopy/IRef 分类，class 的内存布局有两种形式：

#### 2.3.1 引用类型 class（impl IRef）

类实例在堆上分配，LLVM 中表示为 `ptr`。对象头部为 `ptr metadata`，之后按声明顺序排列实例字段。

```penguin
class MyClass {
    x: i32;
    y: mut i64 = 0;
    name: string;
    impl IRef {}        // 显式标记为引用类型
}
```

```llvm
; LLVM struct 类型 (不含 header，仅字段部分用于 GEP 计算)
; 实际对象内存布局:
; offset 0:  ptr metadata → @MyClass_metadata
; offset 8:  i32 x
; offset 12: [4 padding]
; offset 16: i64 y
; offset 24: ptr name (指向 PenguinString)
; total: 32 bytes

%class.MyClass_fields = type {
    i32,                        ; [0] x
    i64,                        ; [1] y
    ptr                         ; [2] name
}
```

**字段偏移计算**:
```
field_offset = 8 + field_position_offset   // 8 = sizeof(ptr metadata)
```

#### 2.3.2 值类型 class（impl ICopy）

类实例可直接在栈上分配，LLVM 中表示为 struct 值类型。无 metadata header。

```penguin
class Point {
    x: i32;
    y: i32;
    impl ICopy {}       // 显式标记为值类型
}
```

```llvm
; 值类型 class 布局 (无 header):
; offset 0: i32 x
; offset 4: i32 y
; total: 8 bytes

%class.Point = type {
    i32,                        ; [0] x
    i32                         ; [1] y
}
```

**字段偏移计算**:
```
field_offset = field_position_offset   // 无 header，从 0 开始
```

### 2.4 String 内存布局（引用类型）

```llvm
; PenguinString: 变长结构
; offset 0:  ptr metadata → @String_metadata
; offset 8:  i64 length (字符串长度)
; offset 16: i8  data[] (UTF-8 数据，内联存储)
```

字符串字面量作为全局常量。变量持有 `ptr` 直接指向 PenguinString。

### 2.5 Enum 内存布局（Tagged Union，值类型 + metadata ptr）

Enum 为**值类型**（栈分配，按值传递），但结构体第一个字段为 `ptr metadata`（指向全局常量 EnumMetadata）。变量持有 struct 值本身。

```
struct EnumType {
    ptr metadata;                 // [0] 枚举元数据（全局常量）
    i32 _variant;                 // [1] 变体判别符
    <largest_payload> _payload;   // [2] payload（仅当有 payload 时）
};
```

无 payload 的枚举结构为 `{ ptr, i32 }`。

```penguin
enum Option<i32> {
    None;           // 无 payload
    Some(i32);      // payload: i32
}
```

```llvm
@Option_metadata = private constant { ptr, i32 } { ptr null, i32 2 }

%enum.Option_i32 = type {
    ptr,      ; [0] metadata → @Option_metadata
    i32,      ; [1] _variant: 0=None, 1=Some
    i32       ; [2] _payload (对于 None 未使用)
}
```

**NEW_ENUM lowering**（栈分配）:
```llvm
%tmp = alloca %enum.Option_i32
%md_ptr = getelementptr %enum.Option_i32, ptr %tmp, i32 0, i32 0
store ptr @Option_metadata, ptr %md_ptr
%var_ptr = getelementptr %enum.Option_i32, ptr %tmp, i32 0, i32 1
store i32 1, ptr %var_ptr
%pay_ptr = getelementptr %enum.Option_i32, ptr %tmp, i32 0, i32 2
store i32 42, ptr %pay_ptr
%result = load %enum.Option_i32, ptr %tmp
```

**多类型 payload 的枚举**（当前简化为最大 payload 类型）:

```penguin
enum Shape {
    Circle(f64);           // payload: f64
    Rectangle(f64, f64);   // payload: 两个 f64（当前简化为单一最大类型）
    Point;                 // 无 payload
}
```

```llvm
@Shape_metadata = private constant { ptr, i32 } { ptr null, i32 3 }

%enum.Shape = type {
    ptr,              ; [0] metadata → @Shape_metadata
    i32,              ; [1] _variant: 0=Circle, 1=Rectangle, 2=Point
    f64               ; [2] _payload (按最大单字段类型)
}
```

### 2.6 Interface Reference（单指针设计）

接口引用就是**对象指针**，与类引用完全相同。**仅引用类型 class（impl IRef）** 可以转换为接口引用，值类型 class 不支持（无 metadata 用于虚分派）。

```penguin
let obj: MyClass = new MyClass();     // obj 类型: ptr (MyClass 是 IRef)
let iface: IFoo = obj;                // iface 类型: ptr (同一个指针)
```

接口方法分派通过 `obj→metadata→interface_map` 查找。编译期已知类型时，metadata 是全局常量，可优化为固定偏移。

### 2.7 ClassMetadata（类元数据）

**仅引用类型 class（impl IRef）** 生成 ClassMetadata。值类型 class（impl ICopy）不需要元数据，因为它们没有虚分派需求。

```llvm
%ClassMetadata = type {
    ptr,    ; name (类名，用于反射/调试)
    i32,    ; instance_size (实例总大小，含 header)
    i32,    ; field_count
    ptr,    ; field_layout → i32[] (字段偏移表)
    ptr,    ; field_types → ClassMetadata[]
    ptr,    ; field_flags → i8[] (0=值类型, 1=引用类型)
    ptr,    ; virtual_method_table → ptr[]
    i32,    ; interface_count
    ptr,    ; interface_map → InterfaceMapEntry[]
    ptr     ; destructor (析构函数指针)
}

%InterfaceMapEntry = type {
    ptr,    ; interface_id (唯一全局常量)
    ptr     ; interface_method_table → ptr[] (该接口的方法实现表)
}
```

**LLVM 生成示例**:

```llvm
@Foo_vtable = private constant [3 x ptr] [
    ptr @Foo_method1, ptr @Foo_method2, ptr @Foo_method3
]

@Foo_IBar_itable = private constant [2 x ptr] [
    ptr @Foo_IBar_methodA, ptr @Foo_IBar_methodB
]

@Foo_imap_entry_IBar = private constant %InterfaceMapEntry {
    ptr @IBar_interface_id, ptr @Foo_IBar_itable
}

@Foo_metadata = private constant %ClassMetadata {
    ptr @"Foo", i32 40, i32 3,
    ptr @Foo_field_offsets, ptr @Foo_field_types, ptr @Foo_field_flags,
    ptr @Foo_vtable,
    i32 1, ptr @Foo_interface_map,
    ptr @Foo_dtor
}
```

### 2.8 FunctionValue（闭包）

```llvm
%FunctionValue = type {
    ptr,    ; code (函数代码指针)
    ptr     ; env (捕获环境指针, null = 纯函数指针)
}
```

静态函数 `env = null`，lambda `env` 指向堆分配的捕获结构。

调用闭包:
```llvm
%code = extractvalue { ptr, ptr } %closure, 0
%env  = extractvalue { ptr, ptr } %closure, 1
%result = call %code(ptr %env, ...)
```

### 2.9 方法分派模型

#### 2.9.1 确定性分派（Direct Dispatch）

当编译期已知调用者的具体 class 类型时，使用**确定性分派**（直接调用）：

```penguin
let p = new Point(1, 2);   // p: Point（具体类型）
p.show();                   // 直接调用 Point.show
```

```llvm
; 确定性分派 — 编译期已知目标函数
call void @"<global>.Point.show"(ptr %p)
```

**决策规则**（SemanticModel 层）：
- 成员访问的 base 表达式类型为 `ClassKind` → 确定性分派
- 目标函数在 class scope 中解析，生成 CALL 指令
- 无需 vtable 或 interface_map 查找

#### 2.9.2 动态分派（Dynamic Dispatch via Interface Map）

当调用者类型为**接口类型**时，必须通过运行时 interface_map 查表：

```penguin
let s: IShow = new Point(1, 2);   // s: IShow（接口类型）
s.show();                          // 运行时查表分派
```

```llvm
; 动态分派 — 运行时通过 metadata→interface_map 查找
%func_ptr = call ptr @__penguin_vtable_lookup(ptr %s, ptr @.IShow_interface_id, i32 0)
call void %func_ptr(ptr %s)
```

**决策规则**（SemanticModel 层）：
- 成员访问的 base 表达式类型为 `InterfaceKind` → 动态分派
- 目标函数在 interface scope 中解析，生成 CALL_VIRT 指令
- CALL_VIRT 携带 `interface_id` 和 `vtable_slot` 用于运行时查找

#### 2.9.3 接口默认方法

接口可以包含带方法体的**默认实现**。class 在 `impl` 块中可以选择：
- **空 impl**（`impl IFoo {}`）：继承所有默认实现
- **覆盖 impl**（`impl IFoo { fun method(...) { ... } }`）：覆盖指定方法

默认方法的调用路径：
- 具体类型调用默认方法（空 impl）→ 确定性分派到接口默认函数
- 具体类型调用覆盖方法 → 确定性分派到 class 覆盖函数
- 接口类型调用方法 → 动态分派（vtable 指向实际实现）

vtable 生成规则：
- 空 impl 的 slot → 指向接口默认函数
- 覆盖 impl 的 slot → 指向 class 覆盖函数

#### 2.9.4 `is` 类型检查

`is` 运算符支持多种类型检查模式：

| 表达式 | IR 指令 | 实现方式 | 适用类型 |
|--------|---------|---------|---------|
| `x is EnumType.Variant` | ISENUM | 比较 tagged union 的 variant tag | enum |
| `x is InterfaceType` | ISINSTANCE | 运行时 metadata interface_map 查找 | IRef class |
| `x is ClassName` | ISINSTANCE | 运行时 metadata name/class_id 比较 | IRef class |

**编译期优化**：当类型信息在编译期可确定时（如具体类型 `is` 已知接口），直接生成常量 `true`/`false`。

**仅适用于 IRef class**：值类型 class（ICopy）无 metadata，不支持运行时类型检查。若 class 实现了接口，自动提升为 IRef。

### 2.10 C Interop Structures

以下 C 结构体与 LLVM IR 数据布局完全对应，用于 C 运行时代码处理 PenguinLang 对象：

```c
// 对应 LLVM %InterfaceMapEntry
typedef struct PenguinInterfaceMapEntry {
    const char* interface_id;    // 接口唯一标识符（字符串常量）
    void** method_table;         // 方法指针数组 ptr[]
} PenguinInterfaceMapEntry;

// 对应 LLVM %ClassMetadata
typedef struct PenguinClassMetadata {
    const char* name;            // 类名
    int instance_size;           // 实例总大小（含 header）
    int field_count;             // 字段数量
    int* field_offsets;          // 字段偏移表 i32[]
    void** virtual_method_table; // 虚方法表 ptr[]
    int interface_count;         // 实现的接口数量
    PenguinInterfaceMapEntry* interface_map; // 接口映射表
    void (*destructor)(void*);   // 析构函数指针
} PenguinClassMetadata;
```

**内存布局保证**：C struct 和 LLVM type 的字段顺序、大小、对齐必须完全一致。所有指针使用 `ptr`（8 bytes on 64-bit），整数使用 `i32`（4 bytes）。

---

## 3. EmperorPenguin IR → LLVM IR 指令转换

### 3.1 CONST

**IR**: `%dst:ty = CONST value`

| IR 类型 | LLVM 指令 |
|---------|----------|
| 整数 (`i8` ~ `i64`) | `%dst = add iXX 0, <value>` 或直接使用常量 |
| 浮点 (`f32` / `f64`) | 直接使用 LLVM 浮点常量 |
| `bool` | `%dst = add i8 0, 1` (true) 或 `add i8 0, 0` (false) |
| `string` | `%dst = ptrtoint ptr @string_lit_N, ptr` (全局常量地址) |
| 引用类型 null | `%dst = inttoptr i64 0, ptr` |

### 3.2 ARG

**IR**: `%result:ty = ARG param_name index`

函数参数在 LLVM 中直接使用 `%param_name`，ARG 指令在 lowering 时消失。

### 3.3 ASSIGN

**IR**: `%dest:ty = ASSIGN %src`

| 类型分类 | LLVM 指令 |
|---------|----------|
| 原始值类型 | `%dest = add iXX 0, %src` (复制值) |
| enum 值类型 | `%dest = insertvalue %enum_type undef, ...` (逐字段复制) |
| 值类型 class (ICopy) | 整体 struct 复制（LLVM 自动按值语义） |
| 引用类型 class (IRef) | 指针值直接使用（浅拷贝），无需额外指令 |

### 3.4 CAST

**IR**: `%result:to_ty = CAST %operand from_ty->to_ty`

| 转换类型 | LLVM 指令 |
|---------|----------|
| 整数拓宽 (i8→i32) | `%result = zext i8 %operand, i32` (无符号) 或 `sext` (有符号) |
| 整数截断 (i64→i32) | `%result = trunc i64 %operand, i32` |
| 整数→浮点 | `%result = sitofp i32 %operand, float` (有符号) 或 `uitofp` (无符号) |
| 浮点→整数 | `%result = fptosi float %operand, i32` 或 `fptoui` |
| 浮点精度 (f32↔f64) | `%result = fpext float %operand, double` 或 `fptrunc` |
| 类→接口 | 零开销 (同一个指针，仅 IRef class) |
| 接口→类 | ISINSTANCE 检查后零开销 (同一个指针，仅 IRef class) |

### 3.5 BINOP

**IR**: `%result:ty = BINOP op %left, %right`

| IR op | 整数 LLVM 指令 | 浮点 LLVM 指令 |
|-------|---------------|---------------|
| `add` | `%r = add iXX %l, %rr` | `%r = fadd float %l, %rr` |
| `sub` | `%r = sub iXX %l, %rr` | `%r = fsub float %l, %rr` |
| `mul` | `%r = mul iXX %l, %rr` | `%r = fmul float %l, %rr` |
| `div` | `%r = sdiv iXX %l, %rr` / `udiv` | `%r = fdiv float %l, %rr` |
| `mod` | `%r = srem iXX %l, %rr` / `urem` | `%r = frem float %l, %rr` |
| `eq` | `%r = icmp eq iXX %l, %rr` | `%r = fcmp oeq float %l, %rr` |
| `ne` | `%r = icmp ne iXX %l, %rr` | `%r = fcmp une float %l, %rr` |
| `slt` / `ult` | `%r = icmp slt` / `icmp ult` | `%r = fcmp olt` |
| `sgt` / `ugt` | `%r = icmp sgt` / `icmp ugt` | `%r = fcmp ogt` |
| `sle` / `ule` | `%r = icmp sle` / `icmp ule` | `%r = fcmp ole` |
| `sge` / `uge` | `%r = icmp sge` / `icmp uge` | `%r = fcmp oge` |
| `and` | `%r = and iXX %l, %rr` | — |
| `or` | `%r = or iXX %l, %rr` | — |
| `xor` | `%r = xor iXX %l, %rr` | — |

### 3.6 UNARYOP

**IR**: `%result:ty = UNARYOP op %operand`

| IR op | LLVM 指令 |
|-------|----------|
| `neg` | `%result = sub iXX 0, %operand` (整数) 或 `fneg float %operand` (浮点) |
| `not` | `%result = xor i8 %operand, 1` |
| `bitnot` | `%result = xor iXX %operand, -1` |

### 3.7 RDMBR（读取成员）

**IR**: `%result:ty = RDMBR %obj, .field_name`

**引用类型 class** — `obj` 为 `ptr`，字段偏移包含 metadata header:

```llvm
; %result:i32 = RDMBR %obj, .x
%field_ptr = getelementptr i8, ptr %obj, i32 <field_offset>
%result = load i32, ptr %field_ptr
```

字段偏移 = `8 + field_position_offset`。

**值类型 class** — `obj` 为 struct 指针，字段偏移从 0 开始（无 header）:

```llvm
; %result:i32 = RDMBR %obj, .x
%field_ptr = getelementptr %class.Point, ptr %obj_ptr, i32 0, i32 <field_index>
%result = load i32, ptr %field_ptr
```

### 3.8 WRMBR（写入成员）

**IR**: `WRMBR %obj, .field_name, %value`

**引用类型 class**:
```llvm
%field_ptr = getelementptr i8, ptr %obj, i32 <field_offset>
store i32 %value, ptr %field_ptr
```

**值类型 class**:
```llvm
%field_ptr = getelementptr %class.Point, ptr %obj_ptr, i32 0, i32 <field_index>
store i32 %value, ptr %field_ptr
```

### 3.9 BR

**IR**: `BR target_label`

```llvm
br label %target_label
```

### 3.10 BR_COND

**IR**: `BR_COND %cond, true_label, false_label`

```llvm
%cond_i1 = trunc i8 %cond, i1        ; bool i8 → i1 for branch
br i1 %cond_i1, label %true_label, label %false_label
```

### 3.11 RET

**IR**: `RET %value`

```llvm
; 值类型
ret i32 %value
; 引用类型
ret ptr %value
```

### 3.12 RET_VOID

**IR**: `RET_VOID`

```llvm
ret void
```

### 3.13 CALL

**IR**: `%result:ty = CALL @func_name(%arg1, %arg2, ...)`

```llvm
%result = call i32 @func_name(i32 %arg1, ptr %arg2)
```

**Void 调用** (CALL_VOID):

**IR**: `CALL @func_name(%arg1, ...)`

```llvm
call void @func_name(i32 %arg1)
```

### 3.14 CALL_VIRT

**IR**: `%result:ty = CALL_VIRT %obj, interface="InterfaceName", slot=N(%arg1, ...)`

**仅适用于引用类型 class（impl IRef）**。值类型 class（impl ICopy）不支持虚分派（无 metadata，不能实现接口动态分派）。

#### 3.14.1 分派路径选择

编译器根据调用者类型选择分派方式：

| 调用者类型 | 分派方式 | IR 指令 | LLVM 生成 |
|-----------|---------|---------|----------|
| 具体 class（如 `Point`） | 确定性分派 | CALL | `call ReturnType @func_name(args...)` |
| 接口类型（如 `IShow`） | 动态分派 | CALL_VIRT | 运行时查表 + 间接调用 |

**注意**: 具体类型的接口方法调用（如 `Point` 对象调用 `impl IShow` 中的方法）使用确定性分派，不经过 CALL_VIRT。

#### 3.14.2 动态分派 lowering（CALL_VIRT）

动态虚调用通过 C 运行时辅助函数 `__penguin_vtable_lookup` 实现分派：

```llvm
; IR: %result:ty = CALL_VIRT %obj, interface="IShow", slot=0(%obj, %arg1, ...)

; 1. 通过 C 运行时查找函数指针
%func_ptr = call ptr @__penguin_vtable_lookup(
    ptr %obj,                       ; 对象指针
    ptr @.IShow_interface_id,       ; 接口 ID 字符串常量
    i32 0                           ; vtable slot 索引
)

; 2. 通过函数指针间接调用
%result = call ReturnType %func_ptr(ptr %obj, ArgType %arg1)
```

`__penguin_vtable_lookup` 的查找流程：
1. 从对象读取 metadata：`metadata = *(void**)obj`
2. 遍历 `metadata->interface_map`，匹配 `interface_id`
3. 从匹配的 entry 获取 `method_table`
4. 返回 `method_table[slot]`

#### 3.14.3 确定性分派 lowering（直接 CALL）

```llvm
; 具体类型调用接口方法 — 直接分派
; p.show() where p: Point
call ReturnType @"<global>.Point.show"(ptr %p)
```

编译期已知具体类型时，目标函数在 class scope 中解析。无运行时开销。

```llvm
; 1. 从对象获取 metadata
%metadata_ptr = getelementptr i8, ptr %obj, i32 0
%metadata = load ptr, ptr %metadata_ptr

; 2. 从 metadata 获取 interface_map
%imap_ptr = getelementptr i8, ptr %metadata, i32 <imap_offset>
%imap = load ptr, ptr %imap_ptr

; 3. 从 interface_map 获取接口方法表
%entry_ptr = getelementptr i8, ptr %imap, i32 <entry_offset>
%entry = load ptr, ptr %entry_ptr
%method_tbl_ptr = getelementptr i8, ptr %entry, i32 8  ; InterfaceMapEntry.method_table
%method_tbl = load ptr, ptr %method_tbl_ptr

; 4. 从方法表获取第 N 个函数指针
%func_ptr_ptr = getelementptr i8, ptr %method_tbl, i32 <slot_N_offset>
%func_ptr = load ptr, ptr %func_ptr_ptr

; 5. 通过函数指针调用
%result = call i32 %func_ptr(ptr %obj, i32 %arg1)
```

编译期已知类型时，metadata 是全局常量，可优化为直接偏移访问。

### 3.15 NEW

**IR**: `%result = NEW TypeName(%arg1, ...)`

根据 ICopy/IRef 分类，有两种 lowering 路径：

#### 3.15.1 引用类型 class（impl IRef）

分配堆对象 + 初始化 metadata + 调用构造器:

```llvm
; %result:ptr = NEW TypeName(%arg1, ...)

; 1. 分配对象
%obj = call ptr @__alloc_impl(i32 <sizeof>)

; 2. 初始化 metadata
%metadata_ptr = getelementptr i8, ptr %obj, i32 0
store ptr @TypeName_metadata, ptr %metadata_ptr

; 3. 调用构造函数
call void @TypeName_ctor(ptr %obj, i32 %arg1)

; 4. 返回对象指针
%result = %obj
```

#### 3.15.2 值类型 class（impl ICopy）

栈上分配 + 直接初始化字段（无 metadata）:

```llvm
; %result:%class.TypeName = NEW TypeName(%arg1, ...)

; 1. 分配栈空间
%tmp = alloca %class.TypeName

; 2. 初始化字段（无 metadata header）
%field0_ptr = getelementptr %class.TypeName, ptr %tmp, i32 0, i32 0
store i32 %arg1, ptr %field0_ptr
; ... 初始化其余字段 ...

; 3. 加载完整值
%result = load %class.TypeName, ptr %tmp
```

值类型 class 也可以通过 `alloca` + 字段初始化实现构造。如果 class 有构造函数:

```llvm
; 1. 分配栈空间
%tmp = alloca %class.TypeName

; 2. 调用构造函数（构造函数参数为指向栈空间的指针）
call void @TypeName_ctor(ptr %tmp, i32 %arg1)

; 3. 加载完整值
%result = load %class.TypeName, ptr %tmp
```

### 3.16 NEW_ENUM

**IR**: `%result = NEW_ENUM EnumType.variant_name(%payload)`

构造带 metadata 的 tagged union（栈上值类型）:

```llvm
; %result = NEW_ENUM Option_i32.Some(42)

; 1. 分配栈空间
%enum_ptr = alloca %enum.Option_i32

; 2. 设置 metadata（field 0）
%md_ptr = getelementptr %enum.Option_i32, ptr %enum_ptr, i32 0, i32 0
store ptr @Option_metadata, ptr %md_ptr

; 3. 设置 _variant（field 1）
%variant_ptr = getelementptr %enum.Option_i32, ptr %enum_ptr, i32 0, i32 1
store i32 1, ptr %variant_ptr              ; Some = 1

; 4. 设置 _payload（field 2）
%payload_ptr = getelementptr %enum.Option_i32, ptr %enum_ptr, i32 0, i32 2
store i32 42, ptr %payload_ptr

; 5. 加载完整值
%result = load %enum.Option_i32, ptr %enum_ptr
```

无 payload 变体:
```llvm
%enum_ptr = alloca %enum.Option_i32
%md_ptr = getelementptr %enum.Option_i32, ptr %enum_ptr, i32 0, i32 0
store ptr @Option_metadata, ptr %md_ptr
%variant_ptr = getelementptr %enum.Option_i32, ptr %enum_ptr, i32 0, i32 1
store i32 0, ptr %variant_ptr              ; None = 0
%result = load %enum.Option_i32, ptr %enum_ptr
```

### 3.17 ISENUM

**IR**: `%result:bool = ISENUM %enum_value, %variant_idx`

检查 tagged union 的 _variant 字段（field 1，跳过 metadata ptr field 0）:

```llvm
; enum_value 为 struct，需 alloca 后提取字段
%tmp = alloca %enum.Option_i32
store %enum.Option_i32 %enum_value, ptr %tmp

; 1. 读取 _variant（field 1）
%variant_ptr = getelementptr %enum.Option_i32, ptr %tmp, i32 0, i32 1
%tag = load i32, ptr %variant_ptr

; 2. 与目标 variant_idx 比较
%cmp = icmp eq i32 %tag, %variant_idx
%result = zext i1 %cmp, i8                 ; i8 bool
```

### 3.18 RDENUM

**IR**: `%result:ty = RDENUM %enum_value, .variant_name`

读取 tagged union 的 payload（field 2，跳过 metadata + variant）:

```llvm
%tmp = alloca %enum.Option_i32
store %enum.Option_i32 %enum_value, ptr %tmp
%payload_ptr = getelementptr %enum.Option_i32, ptr %tmp, i32 0, i32 2
%result = load i32, ptr %payload_ptr
```

### 3.19 LABEL

**IR**: `label_name:`

直接映射为 LLVM 基本块标签:

```llvm
label_name:
```

---

## 4. 函数调用约定

### 4.1 普通函数

直接映射为 LLVM 函数，遵循目标平台 C 调用约定。基本类型按值传递，引用类型 class 传指针，枚举按值传，值类型 class 按 LLVM struct 值传递。

### 4.2 实例方法

`this` 的传递方式取决于 class 的 ICopy/IRef 分类：

**引用类型 class（impl IRef）**: `this` 为对象指针。

```
EmperorPenguin IR:
function @MyClass.get_x(%this:ptr) -> i32 { ... }

LLVM IR:
define i32 @MyClass_get_x(ptr %this) {
    ...
}
```

**值类型 class（impl ICopy）**: `this` 通过指针传递（指向调用者的栈帧位置），但方法内部通过 `load` 读取完整值。

```
EmperorPenguin IR:
function @Point.get_x(%this:ptr) -> i32 { ... }

LLVM IR:
define i32 @Point_get_x(ptr %this_ptr) {
    %this = load %class.Point, ptr %this_ptr
    %x_ptr = getelementptr %class.Point, ptr %this_ptr, i32 0, i32 0
    %x = load i32, ptr %x_ptr
    ret i32 %x
}
```

对于 `mut this` 方法（需要修改字段），值类型 class 通过指针直接修改调用者栈上的数据。

### 4.3 构造函数

构造函数接收 `this` 指针并返回 void:

**引用类型 class（impl IRef）**:
```
LLVM IR:
define void @MyClass_ctor(ptr %this, i32 %x) {
    ; 初始化 metadata
    %metadata_ptr = getelementptr i8, ptr %this, i32 0
    store ptr @MyClass_metadata, ptr %metadata_ptr
    ; 初始化字段
    ...
    ret void
}
```

**值类型 class（impl ICopy）**: 无需初始化 metadata:
```
LLVM IR:
define void @Point_ctor(ptr %this, i32 %x, i32 %y) {
    ; 直接初始化字段（无 metadata header）
    %x_ptr = getelementptr %class.Point, ptr %this, i32 0, i32 0
    store i32 %x, ptr %x_ptr
    %y_ptr = getelementptr %class.Point, ptr %this, i32 0, i32 1
    store i32 %y, ptr %y_ptr
    ret void
}
```

### 4.4 外部函数调用 (extern)

CALL_EXTERN 映射为 LLVM 中对 C 函数的直接调用:

```llvm
declare i32 @puts(ptr)

define void @NS_initial_routine_0() {
    %str = ... ; string → const char* marshal
    call i32 @puts(ptr %str)
    ret void
}
```

extern 函数在 LLVM Module 中用 `declare` 声明，不生成定义。

---

## 5. 控制流模式

### 5.1 if 表达式

```
EmperorPenguin IR:
  %t0 = BINOP eq %x, 0
  BR_COND %t0, then_0, else_0
  then_0:
  %t1 = CONST 1
  BR merge_0
  else_0:
  %t2 = CONST 2
  BR merge_0
  merge_0:
  %t3 = phi [i32 %t1, %then_0], [i32 %t2, %else_0]

LLVM IR:
  %t0 = icmp eq i32 %x, 0
  br i1 %t0, label %then_0, label %else_0
then_0:
  %t1 = add i32 0, 1
  br label %merge_0
else_0:
  %t2 = add i32 0, 2
  br label %merge_0
merge_0:
  %t3 = phi i32 [%t1, %then_0], [%t2, %else_0]
```

### 5.2 while 循环

```
EmperorPenguin IR:
  BR loop_header_0
  loop_header_0:
  %t0 = BINOP slt %i, 10
  BR_COND %t0, loop_body_0, loop_exit_0
  loop_body_0:
  ...loop body...
  BR loop_header_0
  loop_exit_0:

LLVM IR:
  br label %loop_header_0
loop_header_0:
  %t0 = icmp slt i32 %i, 10
  br i1 %t0, label %loop_body_0, label %loop_exit_0
loop_body_0:
  ...loop body...
  br label %loop_header_0
loop_exit_0:
```

### 5.3 break / continue

- `break` → `br label %loop_exit_N` (跳转到当前循环的出口标签)
- `continue` → `br label %loop_header_N` (跳转到当前循环的头标签)

---

## 6. `is` 类型检查

### 6.1 enum is 检查（ISENUM）

PenguinLang 的 `if (x is Option<i32>.Some)` 编译为 ISENUM 指令:

```llvm
; ISENUM: struct 值需 alloca 后提取 _variant（field 1）
%tmp = alloca %enum.Option_i32
store %enum.Option_i32 %x, ptr %tmp
%variant_ptr = getelementptr %enum.Option_i32, ptr %tmp, i32 0, i32 1
%tag_val = load i32, ptr %variant_ptr
%t0 = icmp eq i32 %tag_val, 1
%result = zext i1 %t0, i8
; 然后 BR_COND %result, then_0, else_0
```

### 6.2 class/interface is 检查（ISINSTANCE）

**仅适用于引用类型 class（impl IRef）**。值类型 class（ICopy）无 metadata，不支持运行时类型检查。

#### 6.2.1 对象 is 接口

检查对象是否实现了某个接口：

```penguin
let p = new Point(1, 2);   // Point impl IShow
println(cast<string>(p is IShow));   // true
```

```llvm
; ISINSTANCE: p is IShow
%result = call i32 @__penguin_isinstance(ptr %p, ptr @.IShow_interface_id)
%bool = icmp ne i32 %result, 0
%result_i8 = zext i1 %bool, i8
```

`__penguin_isinstance` 查找流程：
1. 从对象读取 metadata：`metadata = *(void**)obj`
2. 遍历 `metadata->interface_map`
3. 比较 `entry->interface_id` 与目标 `interface_id`（strcmp）
4. 找到返回 1，否则返回 0

#### 6.2.2 接口 is 类（类型窄化）

检查接口引用是否指向特定类的实例：

```penguin
let a: IAnimal = new Dog();
if (a is Dog) {
    println("is dog");
}
```

```llvm
; ISINSTANCE: a is Dog
%metadata_ptr = load ptr, ptr %a
%name_ptr = load ptr, ptr %metadata_ptr           ; ClassMetadata.name
%result = call i32 @__penguin_check_class(ptr %metadata_ptr, ptr @.Dog_class_id)
%bool = icmp ne i32 %result, 0
```

`__penguin_check_class` 通过比较 `metadata->name` 与目标类名实现。

#### 6.2.3 接口 is 接口

检查接口引用是否也实现了另一个接口：

```penguin
let obj: IBase = new Impl();   // Impl impl IBase, IDerived
println(cast<string>(obj is IDerived));   // true
```

```llvm
; ISINSTANCE: obj is IDerived（与 6.2.1 相同的查找逻辑）
%result = call i32 @__penguin_isinstance(ptr %obj, ptr @.IDerived_interface_id)
```

#### 6.2.4 编译期优化

当类型信息在编译期完全可知时，直接生成常量结果：
- 具体 class 类型 `is` 已实现接口 → `CONST true`
- 具体 class 类型 `is` 未实现接口 → `CONST false`

```llvm
; ISINSTANCE MyClass %obj
%metadata_ptr = getelementptr i8, ptr %obj, i32 0
%metadata = load ptr, ptr %metadata_ptr
%result = call i1 @__check_interface(ptr %metadata, ptr @IBar_interface_id)
```

---

## 7. 运行时函数

Lowering 过程中需要以下运行时支持函数:

| 函数 | 签名 | 用途 |
|------|------|------|
| `__alloc_impl` | `ptr (i32 size)` | 堆内存分配（仅 IRef class 和 string） |
| `__penguin_vtable_lookup` | `ptr (ptr obj, ptr interface_id, i32 slot)` | 动态分派：在对象 metadata 的 interface_map 中查找函数指针 |
| `__penguin_isinstance` | `i32 (ptr obj, ptr interface_id)` | 运行时接口类型检查：对象是否实现了指定接口 |
| `__penguin_check_class` | `i32 (ptr obj, ptr class_id)` | 运行时类类型检查：对象是否为指定类的实例 |
| `__int_to_string` | `ptr (i32 value)` | i32 转字符串 |
| `__i64_to_string` | `ptr (i64 value)` | i64 转字符串 |
| `__string_concat` | `ptr (ptr a, ptr b)` | 字符串拼接 |
| `__bool_to_string` | `ptr (i8 value)` | bool 转字符串 |

**注意**: 值类型 class（ICopy）不需要 `__alloc_impl`（栈分配）或 `__penguin_isinstance`（不支持接口动态分派）。

---

## 8. LLVM Module 结构

### 8.1 每个编译单元生成一个 LLVM Module

```llvm
; 类型声明
%ClassMetadata = type { ptr, i32, i32, ptr, ptr, ptr, ptr, i32, ptr, ptr }
%InterfaceMapEntry = type { ptr, ptr }
%FunctionValue = type { ptr, ptr }

; 引用类型 class (IRef) — 堆分配，含 metadata header
%class.Foo = type { ptr, i32, f64, ptr }  ; { metadata, x, y, z }

; 值类型 class (ICopy) — 栈分配，无 header
%class.Point = type { i32, i32 }           ; { x, y }

; 全局常量
@string_lit_0 = private constant { ptr, i64, [5 x i8] } { ... }
@Foo_metadata = private constant %ClassMetadata { ... }
; Point 无需 metadata（值类型）

; 运行时
declare ptr  @__alloc_impl(i32)

; 函数定义
define i32 @NS_ClassName_method(ptr %this, i32 %p) { ... }

; 主入口
define i32 @__penguin_main(i32 %argc, ptr %argv) {
    call void @__runtime_init()
    call void @NS_initial_routine_0()
    ret i32 0
}
```

---

## 9. 完整 Lowering 示例

### PenguinLang 源码

```penguin
class Counter {
    count: mut i32 = 0;
    impl IRef {}              // 引用类型（实现了接口，需要虚分派）
    impl ICounter {
        fun increment(mut this) {
            this.count = this.count + 1;
        }
        fun get(this) -> i32 {
            return this.count;
        }
    }
}
```

**ICopy/IRef 分类**: Counter 实现了 IRef 接口（因为需要通过 ICounter 接口进行虚调用），为引用类型 class。
```

### LLVM 类型定义

```llvm
%ClassMetadata = type { ptr, i32, i32, ptr, ptr, ptr, ptr, i32, ptr, ptr }
%InterfaceMapEntry = type { ptr, ptr }

; Counter 对象布局:
; offset 0:  ptr metadata → @Counter_metadata
; offset 8:  i32 count
; total: 16 bytes (含 4 bytes padding)

@Counter_field_offsets = private constant [1 x i32] [i32 8]
@Counter_field_types = private constant [1 x ptr] [ptr @i32_metadata]
@Counter_field_flags = private constant [1 x i8] [i8 0]  ; 值类型
@Counter_vtable = private constant [2 x ptr] [
    ptr @Counter_increment,
    ptr @Counter_get
]
@Counter_imap_entry_ICounter = private constant %InterfaceMapEntry {
    ptr @ICounter_interface_id,
    ptr @Counter_vtable
}
@Counter_interface_map = private constant [1 x ptr] [
    ptr @Counter_imap_entry_ICounter
]
@Counter_metadata = private constant %ClassMetadata {
    ptr @"Counter", i32 16, i32 1,
    ptr @Counter_field_offsets, ptr @Counter_field_types, ptr @Counter_field_flags,
    ptr @Counter_vtable,
    i32 1, ptr @Counter_interface_map,
    ptr null  ; no destructor (only i32 field)
}
```

### LLVM 函数定义

```llvm
define void @Counter_ctor(ptr %this) {
entry:
    ; 初始化 metadata
    %metadata_ptr = getelementptr i8, ptr %this, i32 0
    store ptr @Counter_metadata, ptr %metadata_ptr
    ; 初始化 count = 0
    %count_ptr = getelementptr i8, ptr %this, i32 8
    store i32 0, ptr %count_ptr
    ret void
}

define void @Counter_increment(ptr %this) {
entry:
    %count_ptr = getelementptr i8, ptr %this, i32 8
    %old_count = load i32, ptr %count_ptr
    %new_count = add i32 %old_count, 1
    store i32 %new_count, ptr %count_ptr
    ret void
}

define i32 @Counter_get(ptr %this) {
entry:
    %count_ptr = getelementptr i8, ptr %this, i32 8
    %count = load i32, ptr %count_ptr
    ret i32 %count
}
```

---

## 10. Lowering 管线

```
EmperorPenguin Bound Tree
       ↓
Phase 0: ICopy/IRef 分类
  - 检查每个 class 的显式 ICopy/IRef 实现（通过 vtable 查找）
  - 对未显式指定的 class，根据字段类型自动分类
  - 循环依赖检测（递归分类过程中遇到已访问的 class → 引用类型）
  - 同时实现 ICopy 和 IRef → 编译错误
  - 结果写入 BoundClassDefinition.is_value_class
       ↓
Phase 1: 类型注册
  - 遍历所有 class/enum/interface 定义
  - 根据 ICopy/IRef 分类计算不同的内存布局:
    - IRef class: 含 metadata header，堆分配，ptr 表示
    - ICopy class: 无 header，栈分配，struct 表示
  - 仅 IRef class 生成 ClassMetadata 全局常量
  - 仅 IRef class 生成 InterfaceMapEntry 全局常量
       ↓
Phase 2: 函数代码生成
  - 遍历每个 IRFunction
  - 逐条转换 IRInstruction → LLVM instruction
  - 根据 ICopy/IRef 分类选择正确的 lowering 路径:
    - NEW: 堆分配 vs 栈分配
    - RDMBR/WRMBR: 含/不含 header 偏移
    - ASSIGN: 指针复制 vs struct 复制
    - CALL_VIRT: 仅 IRef class 支持
  - 插入 phi 节点 (if/while 的值合并)
  - 处理基本块边界
       ↓
Phase 3: 全局变量生成
  - 生成字符串字面量全局常量
  - 生成 vtable 全局常量（仅 IRef class）
  - 生成静态字段全局变量
       ↓
LLVM Module
```

---

## 11. 实现状态

### 已实现

| 功能 | IR 指令 | LLVM 生成 | 说明 |
|------|---------|----------|------|
| 基础类型运算 | BINOP | icmp/add/sub/mul/div 等 | i32/i64/f32/f64/bool |
| 变量赋值 | ASSIGN | alloca + store + load | 可变变量通过指针间接访问 |
| 控制流 | BR/BR_COND | br/br i1 | if/else/while |
| 函数调用 | CALL/CALL_VOID | call | 直接函数调用 |
| 类实例化 | NEW | alloc_impl + ctor (IRef) / alloca + ctor (ICopy) | 含构造函数调用 |
| 字段读写 | RDMBR/WRMBR | getelementptr + load/store | 含 IRef header 偏移计算 |
| 枚举创建 | NEW_ENUM | alloca + store variant + payload | tagged union |
| 枚举匹配 | ISENUM | load variant + icmp eq | variant tag 比较 |
| 枚举字段读取 | RDENUM | getelementptr + load payload | payload 提取 |
| 类型转换 | CAST | zext/sext/trunc/sitofp 等 | 数值类型转换 |
| 确定性分派 | CALL | call @func_name | 具体类型方法调用 |

### 计划实现

| 功能 | IR 指令 | 依赖 | 说明 |
|------|---------|------|------|
| 动态分派 | CALL_VIRT | ClassMetadata vtable/interface_map | 接口类型方法调用 |
| 接口类型检查 | ISINSTANCE | ClassMetadata interface_map | `obj is InterfaceType` |
| 类类型检查 | ISINSTANCE | ClassMetadata name | `obj is ClassName` |
| 完整 ClassMetadata | — | — | 含 vtable + interface_map + field_offsets |
| 接口默认方法分派 | CALL | bind_member_access 回退 | 空 impl 继承默认实现 |

### C 运行时文件

| 文件 | 内容 |
|------|------|
| `std/core_builtin_extern.c` | 基础运行时（println, print, alloc, 类型转换） |
| `std/penguinlang_interop.c` | PenguinLang 对象互操作结构体和运行时辅助函数（计划） |
