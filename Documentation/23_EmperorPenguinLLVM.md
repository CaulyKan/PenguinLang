# 23. EmperorPenguin LLVM Lowering

## Overview

本文档定义 EmperorPenguin IR 到 LLVM IR 的 lowering（降低）策略，包括所有类型的内存布局、数据结构表示方式，以及每条 IR 指令到 LLVM IR 的转换规则。

**实现位置**: `EmperorPenguin/src/llvm/LLVMEmitter.penguin`（由 `LLVMCompiler.penguin` 串联 IRGenerator 调用）

**输出格式**: LLVM IR 文本（`.ll` 文件），平台无关；链接（clang + C 运行时归档）由编译器外部的 `emperor`/`emperor.bat` 驱动脚本完成

**核心设计决策**:
- 通过 `ICopy`/`IRef` 标记接口区分值类型和引用类型 class
- 引用类型 class（IRef）变量持有**直接指向堆对象的指针**
- 引用类型对象头部包含 `metadata` 指针
- 值类型 class（ICopy）在栈上分配（entry-block alloca），按值复制
- 类引用和接口引用是**同一个指针**（单指针设计）；值类型转接口引用时经 BOX 装箱
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
| enum | 是 | tagged union struct `{ ptr metadata, i64 tag, [N x i8] payload }` | 按值传递（寄存器以 ptr 持有存储） |
| class（值类型，impl ICopy） | 是 | LLVM struct `{ ptr metadata, fields... }`（栈分配，寄存器以 ptr 持有存储） | 按值传递（整体复制） |
| class（引用类型，impl IRef） | 否 | `ptr`（直接指向堆对象） | 按指针传递 |
| interface | 否（引用类型） | `ptr`（与类引用相同指针；值类型经 BOX 装箱） | 按指针传递 |
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

值类型 class 在栈上分配（entry-block `alloca`，零初始化）。LLVM struct 类型的 field 0 同样是 `ptr metadata`（布局与 IRef 统一，支持装箱后参与虚分派），字段从 offset 8 开始。寄存器中的值类型以"指向其存储的指针"表示，按值使用处（传参/赋值/返回）通过 `coerce_operand` 复制整个 struct。

```
// 值类型 class 布局:
struct ValueClass {
    ptr metadata;                // offset 0:  → ClassMetadata (全局常量)
    // fields...                 // offset 8:  字段数据
};

// LLVM struct 类型:
%class.Point = type { ptr, i32, i32 }  ; { metadata, x, y }
```

**关键差异**（相对 IRef）:
- 分配在栈上（entry-block alloca + zeroinitializer），不走 GC 堆
- 按值复制时整体复制 struct
- 寄存器持有指向存储的指针；struct 索引 GEP（field 1 起）访问字段

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

类实例在栈上分配（entry-block alloca，零初始化），LLVM struct 类型的 field 0 为 `ptr metadata`（与 IRef 布局统一），字段从 offset 8 开始。

```penguin
class Point {
    x: i32;
    y: i32;
    impl ICopy {}       // 显式标记为值类型
}
```

```llvm
; 值类型 class 布局:
; offset 0:  ptr metadata → @Point_metadata
; offset 8:  i32 x
; offset 12: i32 y
; total: 16 bytes

%class.Point = type {
    ptr,                        ; [0] metadata → @Point_metadata
    i32,                        ; [1] x
    i32                         ; [2] y
}
```

**字段偏移计算**:
```
field_offset = 8 + field_position_offset   // 8 = sizeof(ptr metadata)
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

Enum 为**值类型**（栈分配，按值传递），结构体第一个字段为 `ptr metadata`（指向全局常量 EnumMetadata，布局与 ClassMetadata 同形）。变量在寄存器中以指向其存储的指针表示。

```
struct EnumType {
    ptr metadata;                 // [0] 枚举元数据（全局常量）
    i64 _variant;                 // [1] 变体判别符
    [N x i8] _payload;            // [2] payload 字节数组（仅当有 payload 时）
};
```

无 payload 的枚举结构为 `{ ptr, i64 }`。payload 以**不透明字节数组**表示（大小取最大变体的 payload），访问通过类型化 load/store 进行——字节数组强制所有变体的复制按字节保真，避免优化器按某一变体的字段边界截断拷贝。

```penguin
#template(T: type)
enum Option {
    some: T;      // payload: T
    none;         // 无 payload
}
```

```llvm
@Option__i32_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr } { ... }

%enum.Option__i32 = type {
    ptr,          ; [0] metadata → @Option__i32_metadata
    i64,          ; [1] _variant: 0=some, 1=none
    [4 x i8]      ; [2] _payload（对于 none 未使用）
}
```

**NEW_ENUM lowering**（栈分配）:
```llvm
%tmp = alloca %enum.Option__i32
%md_ptr = getelementptr %enum.Option__i32, ptr %tmp, i32 0, i32 0
store ptr @Option__i32_metadata, ptr %md_ptr
%var_ptr = getelementptr %enum.Option__i32, ptr %tmp, i32 0, i32 1
store i32 0, ptr %var_ptr                 ; some = 0（tag 字段为 i64，低 4 字节写入）
%pay_ptr = getelementptr %enum.Option__i32, ptr %tmp, i32 0, i32 2
store i32 42, ptr %pay_ptr                ; 类型化 store 进 payload 字节数组
%result = ...                              ; 寄存器以 ptr 形式持有 %tmp
```

**多类型 payload 的枚举**（payload 为不透明字节数组，大小取最大变体）:

```penguin
enum Shape {
    Circle(f64);           // payload: f64
    Rectangle(f64, f64);   // payload: 两个 f64（多字段 payload 以字节数组容纳）
    Point;                 // 无 payload
}
```

```llvm
@Shape_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr } { ... }

%enum.Shape = type {
    ptr,              ; [0] metadata → @Shape_metadata
    i64,              ; [1] _variant: 0=Circle, 1=Rectangle, 2=Point
    [16 x i8]         ; [2] _payload（按最大变体 payload 的字节数）
}
```

### 2.6 Interface Reference（单指针设计）

接口引用就是**对象指针**，与类引用完全相同。引用类型 class（impl IRef）的转换是零开销的同指针传递；值类型 class（impl ICopy）转换为接口引用时通过 **BOX** 装箱——堆分配（`_emperor_alloc_impl`）并复制整个 struct（含 metadata），此后接口上的虚分派针对装箱对象进行。

```penguin
let obj: MyClass = new MyClass();     // obj 类型: ptr (MyClass 是 IRef)
let iface: IFoo = obj;                // iface 类型: ptr (同一个指针)
```

接口方法分派通过 `obj→metadata→interface_map` 查找。编译期已知类型时，metadata 是全局常量，可优化为固定偏移。

### 2.7 ClassMetadata（类元数据）

**所有 class（ICopy 与 IRef）与所有 enum** 都生成同形的 9 字段 metadata 全局常量 `@<TypeName>_metadata`（enum 的 size/field 槽位为零，interface_map 覆盖变体 payload 实现的接口）。GC 遍历与运行时类型检查依赖它。

```llvm
; @X_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr }
%ClassMetadata = type {
    ptr,    ; name → @.X_name (类型名字符串常量)
    i32,    ; instance_size (实例总大小，含 header)
    i32,    ; field_count
    ptr,    ; field_offsets → i32[] (字段偏移表，GC 遍历用)
    ptr,    ; field_is_ptr → i32[] (0=非指针字段, 1=指针字段)
    ptr,    ; virtual_method_table（当前发射为 null；虚分派一律走 interface_map）
    i32,    ; interface_count
    ptr,    ; interface_map → InterfaceMapEntry[]（发射为内联 { ptr, ptr } 数组）
    ptr     ; destructor (GC finalizer：实现 IMemoryDispose 的引用类型指向其 dispose_mem 实现，sweep 回收前调用；其余为 null)
}

%InterfaceMapEntry = type {
    ptr,    ; interface_id → @.<Iface>_interface_id (唯一全局字符串常量)
    ptr     ; interface_method_table → @<Type>_<Iface>_vtable (ptr[]，该接口的方法实现表)
}
```

**LLVM 生成示例**:

```llvm
@.IBar_interface_id = private unnamed_addr constant [5 x i8] c"IBar\00"

@Foo_IBar_vtable = private constant [2 x ptr] [
    ptr @Foo_IBar_methodA, ptr @Foo_IBar_methodB
]

@Foo_interface_map = private constant [1 x { ptr, ptr }] [
    { ptr, ptr } { ptr @.IBar_interface_id, ptr @Foo_IBar_vtable }
]

@Foo_field_offsets = private constant [3 x i32] [i32 8, i32 12, i32 16]
@Foo_field_is_ptr = private constant [3 x i32] [i32 0, i32 0, i32 1]

@Foo_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr } {
    ptr @.Foo_name, i32 40, i32 3,
    ptr @Foo_field_offsets, ptr @Foo_field_is_ptr,
    ptr null,
    i32 1, ptr @Foo_interface_map,
    ptr @Foo_dtor
}

@.Foo_name = private unnamed_addr constant [4 x i8] c"Foo\00"
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
%func_ptr = call ptr @_emperor_vtable_lookup(ptr %s, ptr @.IShow_interface_id, i32 0)
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
| `x is InterfaceType` | ISINSTANCE | 运行时 metadata interface_map 查找 | IRef class（值类型经 BOX 装箱后同样适用） |
| `x is ClassName` | ISINSTANCE | 运行时 metadata name/class_id 比较 | IRef class（值类型经 BOX 装箱后同样适用） |

**编译期优化**：当类型信息在编译期可确定时（如具体类型 `is` 已知接口），直接生成常量 `true`/`false`。

**运行时检查要求对象携带 metadata**：值类型 class（ICopy）经 BOX 装箱后即可参与运行时类型检查；装箱对象与引用类型对象走同一 `_emperor_isinstance` / `_emperor_check_class` 路径。

### 2.10 C Interop Structures

以下 C 结构体（`std/include/emperor_types.h`、`std/include/emperor_interop.h`）与 LLVM IR 数据布局完全对应，用于 C 运行时代码处理 PenguinLang 对象：

```c
// 对应 LLVM interface_map 条目（内联 { ptr, ptr }）
typedef struct EmperorInterfaceMapEntry {
    const char* interface_id;    // 接口唯一标识符（字符串常量）
    void** method_table;         // 方法指针数组 ptr[]
} EmperorInterfaceMapEntry;

// 对应 LLVM @X_metadata（{ ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr }）
typedef struct EmperorClassMetadata {
    const char* name;            // 类型名
    int instance_size;           // 实例总大小（含 header）
    int field_count;             // 字段数量
    int* field_offsets;          // 字段偏移表 i32[]
    int* field_is_ptr;           // 字段是否含指针 i32[]（GC 遍历用）
    void** virtual_method_table; // 虚方法表 ptr[]（当前为 NULL，分派走 interface_map）
    int interface_count;         // 实现的接口数量
    EmperorInterfaceMapEntry* interface_map; // 接口映射表
    void (*destructor)(void*);   // GC finalizer：实现 IMemoryDispose 的引用类型指向 dispose_mem（签名恰好一致），sweep 回收前调用
} EmperorClassMetadata;
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
| `string` | `%dst = store ptr @str_N, ptr %dst_alloca`（全局常量 `@str_N` 的地址） |
| 引用类型 null | `%dst = inttoptr i64 0, ptr` |

### 3.2 ARG

**IR**: `%result:ty = ARG param_name index`

函数参数在 LLVM 中直接使用 `%param_name`，ARG 指令在 lowering 时消失。

### 3.3 ASSIGN

**IR**: `%dest:ty = ASSIGN %src`

| 类型分类 | LLVM 指令 |
|---------|----------|
| 可变寄存器（任意类型） | `store <ty> %src, ptr %dest_alloca`（entry-block alloca 槽位；值类型 class/enum 的 alloca 持有整个 struct，源为指针时经 coerce 做 ptr→struct load） |
| 不可变寄存器 | 建立寄存器值映射（reg_map），后续使用直接解析到源值，无额外指令 |
| 引用类型 class (IRef) | 指针值直接传递（浅拷贝） |

### 3.4 CAST

**IR**: `%result:to_ty = CAST %operand from_ty->to_ty`

| 转换类型 | LLVM 指令 |
|---------|----------|
| 整数拓宽 (i8→i32) | `%result = zext i8 %operand, i32` (无符号) 或 `sext` (有符号) |
| 整数截断 (i64→i32) | `%result = trunc i64 %operand, i32` |
| 整数→浮点 | `%result = sitofp i32 %operand, float` (有符号) 或 `uitofp` (无符号) |
| 浮点→整数 | `%result = fptosi float %operand, i32` 或 `fptoui` |
| 浮点精度 (f32↔f64) | `%result = fpext float %operand, double` 或 `fptrunc` |
| 类→接口 | IRef：零开销（同一个指针）；ICopy：BOX 装箱 |
| 接口→类 | ISINSTANCE 检查后零开销（同一个指针）；ICopy：UNBOX 别名视图 |

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

**值类型 class** — `obj` 为指向 struct 存储的指针，字段经 struct 索引 GEP 访问（field 0 是 metadata，实例字段从索引 1 / 偏移 8 开始）:

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

（struct 索引 GEP，field 0 为 metadata，实例字段从索引 1 开始。）

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

动态虚分派作用于携带 metadata 的对象：引用类型 class（impl IRef）直接传入；值类型 class（impl ICopy）先经 BOX 装箱（复制到堆，含 metadata），再对其虚调用。

#### 3.14.1 分派路径选择

编译器根据调用者类型选择分派方式：

| 调用者类型 | 分派方式 | IR 指令 | LLVM 生成 |
|-----------|---------|---------|----------|
| 具体 class（如 `Point`） | 确定性分派 | CALL | `call ReturnType @func_name(args...)` |
| 接口类型（如 `IShow`） | 动态分派 | CALL_VIRT | 运行时查表 + 间接调用 |

**注意**: 具体类型的接口方法调用（如 `Point` 对象调用 `impl IShow` 中的方法）使用确定性分派，不经过 CALL_VIRT。

#### 3.14.2 动态分派 lowering（CALL_VIRT）

动态虚调用通过 C 运行时辅助函数 `_emperor_vtable_lookup` 实现分派：

```llvm
; IR: %result:ty = CALL_VIRT %obj, interface="IShow", slot=0(%obj, %arg1, ...)

; 1. 通过 C 运行时查找函数指针
%func_ptr = call ptr @_emperor_vtable_lookup(
    ptr %obj,                       ; 对象指针
    ptr @.IShow_interface_id,       ; 接口 ID 字符串常量
    i32 0                           ; vtable slot 索引
)

; 2. 通过函数指针间接调用
%result = call ReturnType %func_ptr(ptr %obj, ArgType %arg1)
```

`_emperor_vtable_lookup` 的查找流程（`std/c/penguinlang_interop.c`）：
1. 从对象读取 metadata：`metadata = *(void**)obj`
2. 遍历 `metadata->interface_map`，匹配 `interface_id`
3. 从匹配的 entry 获取 `method_table`
4. 返回 `method_table[slot]`

聚合返回值（enum/大 struct）走 sret：先在 entry block `alloca` 一个 sret 缓冲区，以 `ptr sret(<ty>)` 首参间接调用，再把结果映射到 sret 缓冲区。

#### 3.14.3 确定性分派 lowering（直接 CALL）

```llvm
; 具体类型调用接口方法 — 直接分派
; p.show() where p: Point
call ReturnType @"<global>.Point.show"(ptr %p)
```

编译期已知具体类型时，目标函数在 class scope 中解析，生成普通 CALL，无运行时开销。CALL_VIRT 路径总是经 `_emperor_vtable_lookup` 查表；编译期已知的 metadata 全局常量只是让运行时的查表输入成为常量。

### 3.15 NEW

**IR**: `%result = NEW TypeName(%arg1, ...)`

所有 NEW 都走统一的 `allocate_class` 路径（零初始化 + metadata 盖章 + 注册为 `ptr`），随后调用构造函数 `@"<global>.TypeName.new"`：

#### 3.15.1 引用类型 class（impl IRef）

GC 堆分配 + memset 清零 + metadata 盖章 + 调用构造器:

```llvm
; %result:ptr = NEW TypeName(%arg1, ...)

; 1. 分配并清零对象
%result = call ptr @_emperor_alloc_impl(i32 <sizeof>)
call void @llvm.memset.p0.i64(ptr %result, i8 0, i64 <sizeof>, i1 false)

; 2. 盖章 metadata（offset 0）
store ptr @TypeName_metadata, ptr %result

; 3. 调用构造函数
call void @"<global>.TypeName.new"(ptr %result, i32 %arg1)
```

#### 3.15.2 值类型 class（impl ICopy）

entry-block alloca + zeroinitializer + metadata 盖章 + 调用构造器:

```llvm
; %result = NEW TypeName(%arg1, ...)

; 1. 分配并清零栈空间（entry block，零初始化整个 struct）
%result = alloca %class.TypeName, align 8
store %class.TypeName zeroinitializer, ptr %result

; 2. 盖章 metadata（field 0）
store ptr @TypeName_metadata, ptr %result

; 3. 调用构造函数（this 为指向存储的指针）
call void @"<global>.TypeName.new"(ptr %result, i32 %arg1)
```

寄存器以 `ptr` 持有结果；按值使用处（传参/赋值）经 `coerce_operand` 复制整个 struct。超过 16 字节的聚合参数以 `ptr byval(<ty>)` 传递。

### 3.16 NEW_ENUM

**IR**: `%result = NEW_ENUM EnumType.variant_name(%payload)`

构造带 metadata 的 tagged union（栈上值类型）:

```llvm
; %result = NEW_ENUM Option__i32.some(42)

; 1. 分配栈空间
%enum_ptr = alloca %enum.Option__i32

; 2. 设置 metadata（field 0）
%md_ptr = getelementptr %enum.Option__i32, ptr %enum_ptr, i32 0, i32 0
store ptr @Option__i32_metadata, ptr %md_ptr

; 3. 设置 _variant（field 1，tag 字段为 i64）
%variant_ptr = getelementptr %enum.Option__i32, ptr %enum_ptr, i32 0, i32 1
store i32 0, ptr %variant_ptr              ; some = 0（低 4 字节写入）

; 4. 设置 _payload（field 2，类型化 store 进字节数组）
%payload_ptr = getelementptr %enum.Option__i32, ptr %enum_ptr, i32 0, i32 2
store i32 42, ptr %payload_ptr

; 5. 结果寄存器以 ptr 持有 %enum_ptr
%result = ...
```

无 payload 变体:
```llvm
%enum_ptr = alloca %enum.Option__i32
%md_ptr = getelementptr %enum.Option__i32, ptr %enum_ptr, i32 0, i32 0
store ptr @Option__i32_metadata, ptr %md_ptr
%variant_ptr = getelementptr %enum.Option__i32, ptr %enum_ptr, i32 0, i32 1
store i32 1, ptr %variant_ptr              ; none = 1
%result = ...                              ; ptr 持有 %enum_ptr
```

### 3.17 ISENUM

**IR**: `%result:bool = ISENUM %enum_value, %variant_idx`

检查 tagged union 的 _variant 字段（field 1，跳过 metadata ptr field 0）:

```llvm
; enum_value 物化为指向 struct 存储的指针
%tmp = materialize_ptr %enum_value

; 1. 读取 _variant（field 1，i32 load 取 tag 低 4 字节）
%variant_ptr = getelementptr %enum.Option__i32, ptr %tmp, i32 0, i32 1
%tag = load i32, ptr %variant_ptr

; 2. 与目标 variant_idx 比较（idx 非 i32 时先 trunc）
%cmp = icmp eq i32 %tag, %variant_idx
%result = zext i1 %cmp, i8                 ; i8 bool
```

### 3.18 RDENUM

**IR**: `%result:ty = RDENUM %enum_value, .variant_name`

读取 tagged union 的 payload（field 2，跳过 metadata + variant），按该变体的 payload 类型做类型化 load:

```llvm
%tmp = materialize_ptr %enum_value
%payload_ptr = getelementptr %enum.Option__i32, ptr %tmp, i32 0, i32 2
%result = load i32, ptr %payload_ptr
```

当提取结果是写链（`e.a.x = 9`、`e.a.increment()`）的接收者时，payload 指针指向枚举**自身存储**内的槽位（而非物化副本），使写回生效。

### 3.19 LABEL

**IR**: `label_name:`

直接映射为 LLVM 基本块标签:

```llvm
label_name:
```

### 3.20 GLOBAL_LOAD

**IR**: `%result:ty = GLOBAL_LOAD @global_name`

引用类型 / string / 原始类型全局变量直接 load:

```llvm
%result = load <ty>, ptr @global_name
```

值类型 class 全局变量把 struct **内联**存储在全局槽位：
- 写链读取（`g.x = 9` lowers 为 GLOBAL_LOAD + WRMBR）→ 结果寄存器映射到 `@global_name` 地址本身（lvalue 寻址，不复制）
- 其它读取 → `load <struct>, ptr @global_name`（值复制语义）

### 3.21 GLOBAL_STORE

**IR**: `GLOBAL_STORE @global_name, %value`

```llvm
store <ty> %value, ptr @global_name
```

值类型 class 全局变量存储整个 struct：value 侧先经 `coerce_operand` 做 ptr→struct 收敛（从值的存储 load 出完整 struct），再整体 store。

### 3.22 ADDRESS_OF

**IR**: `%result:u64 = ADDRESS_OF %operand`

取操作数存储的原始地址，结果为 u64（i64）：

```llvm
; 可变寄存器 → 直接复用其 alloca；否则物化到临时 alloca
%base = alloca <ty>, align 8
store <ty> %operand, ptr %base

%result = ptrtoint ptr %base to i64
```

### 3.23 LOAD_PTR

**IR**: `%result:ty = LOAD_PTR %addr`

从 u64 原始地址按 `load_type` 加载：

```llvm
%ptr = inttoptr i64 %addr to ptr
%result = load <ty>, ptr %ptr
```

值类型 class 槽位（`ref<...>` 且 ICopy）内联存储 struct，而值模型以指针表示值——load 是**复制**：fresh alloca + `llvm.memcpy`：

```llvm
%ptr = inttoptr i64 %addr to ptr
%result = alloca %class.X, align 8
call void @llvm.memcpy.p0.p0.i32(ptr %result, ptr %ptr, i32 <sizeof>, i1 false)
```

### 3.24 STORE_PTR

**IR**: `STORE_PTR %addr, %value:store_type`

向 u64 原始地址按 `store_type` 存储：

```llvm
%ptr = inttoptr i64 %addr to ptr
store <ty> %value, ptr %ptr
```

值类型 class store 必须把**整个 struct** 复制进槽位（value 解析为指向其存储的指针，直接存指针会把栈地址漏进缓冲区）：类型替换为 class struct 类型后经 coerce 再 store。

### 3.25 CALL_INDIRECT

**IR**: `%result:ret_ty = CALL_INDIRECT %callee(%arg1, ...)`

通过函数指针值调用（fun 类型字段 / 局部变量）。callee 是一个活的 `ptr` 寄存器（fat function pointer）；无调用点签名元数据可做参数收敛，实参按各自 IR 类型原样发射：

```llvm
; 非 void 返回
%result = call <ret_ty> %callee(<ty1> %arg1)

; void 返回
call void %callee(<ty1> %arg1)
```

聚合返回值（enum / 大 struct）走 sret：entry block `alloca` 一个 sret 缓冲区，以 `ptr sret(<ty>)` 首参间接调用，随后 `load` 结果：

```llvm
%sret_buf = alloca %enum.X
call void %callee(ptr sret(%enum.X) %sret_buf, <ty1> %arg1)
%result = load %enum.X, ptr %sret_buf
```

---

## 4. 函数调用约定

### 4.1 普通函数

直接映射为 LLVM 函数，遵循目标平台 C 调用约定。基本类型按值传递，引用类型 class 传指针，枚举与值类型 class 以指针表示、按值语义传递（超过 16 字节的聚合参数声明为 `ptr byval(<ty>)`；聚合返回值走 sret——调用方传入 `ptr sret(<ty>)` 缓冲区）。

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
    %x_ptr = getelementptr %class.Point, ptr %this_ptr, i32 0, i32 1
    %x = load i32, ptr %x_ptr
    ret i32 %x
}
```

（field 0 是 metadata，实例字段从 struct 索引 1 开始。）

对于 `mut this` 方法（需要修改字段），值类型 class 通过指针直接修改调用者栈上的数据。

### 4.3 构造函数

构造函数接收 `this` 指针并返回 void；metadata 盖章由 NEW 的 `allocate_class` 统一完成（两条路径都在 offset 0 写入 `@TypeName_metadata`），构造函数只初始化字段:

**引用类型 class（impl IRef）**:
```
LLVM IR:
define void @"<global>.MyClass.new"(ptr %this, i32 %x) {
    ; 初始化字段（metadata 已由 allocate_class 写入）
    ...
    ret void
}
```

**值类型 class（impl ICopy）**:
```
LLVM IR:
define void @"<global>.Point.new"(ptr %this, i32 %x, i32 %y) {
    ; 直接初始化字段（metadata 已由 allocate_class 写入）
    %x_ptr = getelementptr %class.Point, ptr %this, i32 0, i32 1
    store i32 %x, ptr %x_ptr
    %y_ptr = getelementptr %class.Point, ptr %this, i32 0, i32 2
    store i32 %y, ptr %y_ptr
    ret void
}
```

### 4.4 外部函数调用 (extern)

extern 函数的调用走普通 CALL 指令（IR 中没有专门的 extern 调用指令），在 LLVM Module 中用 `declare` 声明、不生成定义:

```llvm
declare i32 @puts(ptr)

define void @NS_initial_routine_0() {
    %str = ... ; string → const char* marshal
    call i32 @puts(ptr %str)
    ret void
}
```

符号名映射规则（`llvm_func_name`）：
- `__builtin` / `_utils` 运行时命名空间的 extern → `@_emperor_<tail>`（如 `__builtin.println` → `@_emperor_println`）
- 其它任何命名空间（std 或用户代码）的 extern → `@<以 _ 连接的完整点分名>`（如 `std.io.file_open` → `@std_io_file_open`）
- 顶层裸 extern → 字面符号名（如 `extern fun abs` → `@abs`，直连 libc）
- dyn-lib 导出声明（`is_lib_export_decl`）保持普通 mangled 名，与其在 `.penguin-lib` 中的定义一致

---

## 5. 控制流模式

### 5.1 if 表达式

```
EmperorPenguin IR:
  %t0 = BINOP eq %x, 0
  BR_COND %t0, then_0, else_0
  then_0:
  %t1 = CONST 1
  %t3 = ASSIGN %t1          ; 结果寄存器在两个分支内各 ASSIGN 一次
  BR merge_0
  else_0:
  %t2 = CONST 2
  %t3 = ASSIGN %t2
  BR merge_0
  merge_0:

LLVM IR:
  %t0 = icmp eq i32 %x, 0
  br i1 %t0, label %then_0, label %else_0
then_0:
  %t1 = add i32 0, 1
  store i32 %t1, ptr %t3_alloca      ; 多赋值寄存器由 alloca 支撑
  br label %merge_0
else_0:
  %t2 = add i32 0, 2
  store i32 %t2, ptr %t3_alloca
  br label %merge_0
merge_0:
  %t3 = load i32, ptr %t3_alloca     ; 合并点读取
```

IR 没有 phi 指令：if 表达式分配一个结果临时寄存器，两个分支各自 ASSIGN；`scan_mutable_regs` 检测到多处定义的寄存器后为其建立 entry-block alloca，分支内 store、合并点 load。

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

PenguinLang 的 `if (x is Option<i32>.some)` 编译为 ISENUM 指令:

```llvm
; ISENUM: struct 值物化为指针后提取 _variant（field 1）
%tmp = materialize_ptr %x
%variant_ptr = getelementptr %enum.Option__i32, ptr %tmp, i32 0, i32 1
%tag_val = load i32, ptr %variant_ptr
%t0 = icmp eq i32 %tag_val, 0           ; some = 0
%result = zext i1 %t0, i8
; 然后 BR_COND %result, then_0, else_0
```

### 6.2 class/interface is 检查（ISINSTANCE）

作用于携带 metadata 的对象：引用类型 class（impl IRef）直接检查；值类型 class（ICopy）经 BOX 装箱后同样可检查（enum/value-class struct 操作数会先物化为指针再传入辅助函数）。

#### 6.2.1 对象 is 接口

检查对象是否实现了某个接口：

```penguin
let p = new Point(1, 2);   // Point impl IShow
println(cast<string>(p is IShow));   // true
```

```llvm
; ISINSTANCE: p is IShow
%result = call i32 @_emperor_isinstance(ptr %p, ptr @.IShow_type_id)
%bool = icmp ne i32 %result, 0
%result_i8 = zext i1 %bool, i8
```

`_emperor_isinstance` 查找流程：
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
%result = call i32 @_emperor_check_class(ptr %a, ptr @.Dog_type_id)
%bool = icmp ne i32 %result, 0
```

`_emperor_check_class` 接收**对象指针**（内部自取 `metadata = *(void**)obj`），通过比较 `metadata->name` 与目标类名实现。若操作数是寄存器中的 enum/value-class struct，先物化为临时 alloca 再取指针。

#### 6.2.3 接口 is 接口

检查接口引用是否也实现了另一个接口：

```penguin
let obj: IBase = new Impl();   // Impl impl IBase, IDerived
println(cast<string>(obj is IDerived));   // true
```

```llvm
; ISINSTANCE: obj is IDerived（与 6.2.1 相同的查找逻辑）
%result = call i32 @_emperor_isinstance(ptr %obj, ptr @.IDerived_type_id)
```

#### 6.2.4 编译期优化

当类型信息在编译期完全可知时，直接生成常量结果：
- 具体 class 类型 `is` 已实现接口 → `CONST true`
- 具体 class 类型 `is` 未实现接口 → `CONST false`

无法折叠时统一走运行时辅助函数（对象指针 + `@.<Type>_type_id` 字符串常量）：

```llvm
; ISINSTANCE %obj, "IBar"
%result = call i32 @_emperor_isinstance(ptr %obj, ptr @.IBar_type_id)
%bool = icmp ne i32 %result, 0
%result_i8 = zext i1 %bool, i8
```

---

## 7. 运行时函数

Lowering 过程中按需 `declare` 以下运行时支持函数（`emit_extern_declarations_runtime_to`，C 实现 `std/c/core_builtin.c`、`std/c/penguinlang_interop.c`、`std/c/gc.c`、`std/c/scheduler.c`）:

| 函数 | 签名 | 用途 |
|------|------|------|
| `_emperor_alloc_impl` | `ptr (i32 size)` | GC 堆内存分配（IRef class、装箱、string） |
| `_emperor_vtable_lookup` | `ptr (ptr obj, ptr interface_id, i32 slot)` | 动态分派：在对象 metadata 的 interface_map 中查找函数指针 |
| `_emperor_isinstance` | `i32 (ptr obj, ptr interface_id)` | 运行时接口类型检查：对象是否实现了指定接口 |
| `_emperor_check_class` | `i32 (ptr obj, ptr class_id)` | 运行时类类型检查：对象是否为指定类的实例 |
| `_emperor_ICopy_copy` | `ptr (ptr value)` | ICopy 接口的运行时拷贝辅助 |
| `_emperor_int_to_string` | `ptr (i32 value)` | i32 转字符串 |
| `_emperor_i64_to_string` | `ptr (i64 value)` | i64 转字符串 |
| `_emperor_double_to_string` | `ptr (double value)` | f64 转字符串 |
| `_emperor_bool_to_string` | `ptr (i8 value)` | bool 转字符串 |
| `_emperor_string_concat` | `ptr (ptr a, ptr b)` | 字符串拼接 |
| `_emperor_string_equal` | `i32 (ptr a, ptr b)` | 字符串相等比较 |
| `_emperor_gc_init` | `void (ptr stack_top)` | GC 初始化（以栈顶为扫描根） |
| `_emperor_gc_add_root` | `void (ptr global)` | 把引用类型的全局变量注册为 GC 根 |
| `_emperor_gc_scan_add` | `void (i64 addr, i64 size)` | 把值类型全局整个 struct 注册为保守扫描区 |
| `_emperor_args_init` | `void (i32 argc, ptr argv)` | 命令行参数初始化 |
| `_emperor_boost_stack` | `void ()` | 提升主线程栈限额（深递归保护） |
| `_emperor_co_spawn_fn0` | `void (ptr fn)` | 协程调度：以协程方式启动 initial 例程 |
| `_emperor_sched_run` | `i32 ()` | 协程调度器主循环 |
| `_setjmp` | `i32 (ptr jmp_buf, ptr frame)` | try/catch 落点（`returns_twice`，双参数形式兼容 glibc/mingw） |
| `llvm.memcpy` / `llvm.memset` | LLVM intrinsic | struct 复制 / 对象清零 |

**注意**: 值类型 class（ICopy）常规分配不走 `_emperor_alloc_impl`（entry-block alloca）；仅当装箱为接口引用（BOX）时进入 GC 堆。

---

## 8. LLVM Module 结构

### 8.1 每个编译单元生成一个 LLVM Module

```llvm
; 类型声明
; metadata 以内联字面 struct 类型发射（9 字段）:
;   { ptr name, i32 size, i32 field_count, ptr field_offsets, ptr field_is_ptr,
;     ptr vtable, i32 interface_count, ptr interface_map, ptr destructor }
%FunctionValue = type { ptr, ptr }

; 引用类型 class (IRef) — 堆分配，含 metadata header
%class.Foo = type { ptr, i32, f64, ptr }  ; { metadata, x, y, z }

; 值类型 class (ICopy) — 栈分配（布局同形，field 0 同样是 metadata）
%class.Point = type { ptr, i32, i32 }     ; { metadata, x, y }

; 全局常量
@str_0 = private constant { ptr, i64, [5 x i8] } { ... }
@Foo_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr } { ... }

; 运行时
declare ptr @_emperor_alloc_impl(i32)

; 函数定义
define i32 @"<global>.ClassName.method"(ptr %this, i32 %p) { ... }

; 主入口
define i32 @main(i32 %argc, ptr %argv) {
    call void @_emperor_boost_stack()
    call void @_emperor_args_init(i32 %argc, ptr %argv)
    %_gc_sp = call ptr @llvm.frameaddress(i32 0)
    call void @_emperor_gc_init(ptr %_gc_sp)
    ; 注册全局变量为 GC 根（string/ref → add_root；值类型 struct → scan_add 保守扫描区）
    call void @_emperor_gc_add_root(ptr @some_ref_global)
    ; 全局变量的非平凡初始化（init 函数）
    call void @"<global>.g_init_0"()
    ; 入口函数（initial 块）；含挂起点时经协程调度器运行
    call void @"_ns_main.initial_0"()
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

### LLVM 类型定义

```llvm
; metadata 以内联字面 struct 类型发射（9 字段）

; Counter 对象布局:
; offset 0:  ptr metadata → @Counter_metadata
; offset 8:  i32 count
; total: 16 bytes (含 4 bytes padding)

@.Counter_name = private unnamed_addr constant [8 x i8] c"Counter\00"
@.ICounter_interface_id = private unnamed_addr constant [9 x i8] c"ICounter\00"

@Counter_field_offsets = private constant [1 x i32] [i32 8]
@Counter_field_is_ptr = private constant [1 x i32] [i32 0]  ; count 非指针
@Counter_ICounter_vtable = private constant [2 x ptr] [
    ptr @"<global>.Counter.increment",
    ptr @"<global>.Counter.get"
]
@Counter_interface_map = private constant [1 x { ptr, ptr }] [
    { ptr, ptr } { ptr @.ICounter_interface_id, ptr @Counter_ICounter_vtable }
]
@Counter_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr } {
    ptr @.Counter_name, i32 16, i32 1,
    ptr @Counter_field_offsets, ptr @Counter_field_is_ptr,
    ptr null,
    i32 1, ptr @Counter_interface_map,
    ptr null  ; no destructor (Counter does not impl IMemoryDispose; a class that does would point at its dispose_mem, e.g. ptr @"<global>.Vector__i64.dispose_mem")
}
```

### LLVM 函数定义

```llvm
; NEW Counter() 的发射（allocate_class 统一路径）:
;   %obj = call ptr @_emperor_alloc_impl(i32 16)
;   call void @llvm.memset.p0.i64(ptr %obj, i8 0, i64 16, i1 false)
;   store ptr @Counter_metadata, ptr %obj
;   call void @"<global>.Counter.new"(ptr %obj)

define void @"<global>.Counter.new"(ptr %this) {
entry:
    ; 初始化 count = 0（metadata 已由 NEW 的 allocate_class 写入）
    %count_ptr = getelementptr i8, ptr %this, i32 8
    store i32 0, ptr %count_ptr
    ret void
}

define void @"<global>.Counter.increment"(ptr %this) {
entry:
    %count_ptr = getelementptr i8, ptr %this, i32 8
    %old_count = load i32, ptr %count_ptr
    %new_count = add i32 %old_count, 1
    store i32 %new_count, ptr %count_ptr
    ret void
}

define i32 @"<global>.Counter.get"(ptr %this) {
entry:
    %count_ptr = getelementptr i8, ptr %this, i32 8
    %count = load i32, ptr %count_ptr
    ret i32 %count
}
```

---

## 10. Lowering 管线

```
EmperorPenguin Bound Tree + IRModule
       ↓
前置: ICopy/IRef 分类（语义层 ClassifyValueTypesPass）
  - 检查每个 class 的显式 ICopy/IRef 实现（通过 vtable 查找）
  - 对未显式指定的 class，根据字段类型自动分类
  - 循环依赖检测（递归分类过程中遇到已访问的 class → 引用类型）
  - 同时实现 ICopy 和 IRef → 编译错误
  - 结果写入 BoundClassDefinition.is_value_class
       ↓
LLVMEmitter.lower(module, unit):
Pass 1: 收集字符串字面量
  - 扫描所有函数的 CONST 指令与全局变量初始化器
  - 汇总去重，编号为 @str_N
       ↓
Pass 2: 构建布局表（build_layout_tables）
  - 遍历所有 class/enum/interface 定义
  - 布局统一为 { ptr metadata, fields... }（enum: { ptr, i64 tag, [N x i8] payload }）:
    - IRef class: 堆分配，按字节偏移 GEP
    - ICopy class: 栈分配，按 struct 索引 GEP（field 0 为 metadata）
  - 汇总每个类型的接口 vtable 槽位
       ↓
Pass 3: 发射函数（emit_functions + emit_main）
  - 遍历每个 IRFunction，逐条转换 IRInstruction → LLVM instruction
  - 根据 ICopy/IRef 分类选择正确的 lowering 路径:
    - NEW: 堆分配（alloc_impl + memset）vs entry-block alloca
    - RDMBR/WRMBR: 字节偏移 GEP vs struct 索引 GEP
    - ASSIGN: 指针复制 vs struct 复制
    - CALL_VIRT: 经 _emperor_vtable_lookup 查 interface_map
  - 多处定义的寄存器由 scan_mutable_regs 以 entry-block alloca 支撑（无 phi 节点）
  - 函数体先发射，以确定所需的运行时 declare 集合
       ↓
最终组合
  - 类型定义（%class./%enum. struct，按布局完成顺序 = 类型嵌套拓扑序）
  - 所有 class/enum 的 metadata 全局常量、接口 ID 常量、vtable/interface_map
  - 字符串字面量全局常量（@str_N）
  - 全局变量（@<name>，值类型 struct 内联存储）
  - extern declare（用户 extern + 按需运行时 declare）
  - 函数体 + @main 入口
       ↓
LLVM Module 文本（.ll）
```

---

## 11. 实现状态

### 已实现

| 功能 | IR 指令 | LLVM 生成 | 说明 |
|------|---------|----------|------|
| 基础类型运算 | BINOP | icmp/add/sub/mul/div 等 | i32/i64/f32/f64/bool |
| 变量赋值 | ASSIGN | alloca + store + load | 多处定义的可变寄存器经 alloca 间接访问 |
| 控制流 | BR/BR_COND | br/br i1 | if/else/while |
| 短路求值 | （BR_COND 序列） | br i1 + 标签 | `&&`/`\|\|` |
| 函数调用 | CALL/CALL_VOID | call | 直接函数调用（含 extern） |
| 间接调用 | CALL_INDIRECT | call %callee | fun 类型函数指针值；聚合返回走 sret |
| 动态分派 | CALL_VIRT | `_emperor_vtable_lookup` + 间接 call | 接口类型方法调用 |
| 接口类型检查 | ISINSTANCE | `_emperor_isinstance` | `obj is InterfaceType` |
| 类类型检查 | ISINSTANCE | `_emperor_check_class` | `obj is ClassName` |
| 装箱/拆箱 | BOX/UNBOX | `_emperor_alloc_impl` + memcpy / 别名视图 | 值类型 ↔ 接口引用 |
| 类实例化 | NEW | alloc_impl + memset + metadata (IRef) / entry alloca (ICopy) | allocate_class 统一路径 + 构造函数调用 |
| 字段读写 | RDMBR/WRMBR | getelementptr + load/store | IRef 字节偏移 / ICopy struct 索引 |
| 枚举创建 | NEW_ENUM | alloca + store variant + payload | tagged union |
| 枚举匹配 | ISENUM | load variant + icmp eq | variant tag 比较 |
| 枚举字段读取 | RDENUM | getelementptr + load payload | payload 提取（写链指向枚举自身存储） |
| 全局变量 | GLOBAL_LOAD/GLOBAL_STORE | load/store @global | 值类型 struct 内联存储 |
| 地址元内建 | ADDRESS_OF/LOAD_PTR/STORE_PTR | ptrtoint / inttoptr + load/store | `#__address_of`/`#__load`/`#__store` |
| 类型转换 | CAST | zext/sext/trunc/sitofp 等 | 数值类型转换 |
| try/catch | （setjmp 序列） | `_setjmp` 落点 + jmp_buf 表 | `try_site_counter` 索引 |
| 完整 ClassMetadata | — | `@<Type>_metadata`（9 字段） | 所有 class 与 enum；含 interface_map + field_offsets + field_is_ptr + destructor |
| 接口默认方法分派 | CALL | vtable slot 指向特化后的默认实现 | 空 impl 继承默认实现 |
| 确定性分派 | CALL | call @func_name | 具体类型方法调用 |

`IRInstruction` 的全部 29 种变体在 `emit_instruction` 分发器中均有对应的 `emit_*` lowering。

### C 运行时文件

| 文件 | 内容 |
|------|------|
| `std/c/core_builtin.c` | 内置函数实现（print、字符串操作、GC 分配、类型转换、文件 I/O） |
| `std/c/gc.c` | 保守标记-清除 GC（栈扫描、根注册、finalizer 调用） |
| `std/c/penguinlang_interop.c` | 互操作辅助：`_emperor_vtable_lookup`、`_emperor_isinstance`、`_emperor_check_class` |
| `std/c/scheduler.c` | 协程调度器（`_emperor_sched_run`、`_emperor_co_spawn_fn0`） |
| `std/c/meta_stubs.c` / `std/c/penguin_jit.cpp` | 元编程 JIT 宿主 |
| `std/include/*.h` | C 侧类型与互操作声明（`emperor_types.h`、`emperor_interop.h`、`emperor_gc.h`、`emperor_builtin.h`） |
