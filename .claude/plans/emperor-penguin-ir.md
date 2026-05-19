# EmperorPenguin IR 设计方案

> **状态**: 设计完成，实现计划见 [glistening-stargazing-cosmos.md](glistening-stargazing-cosmos.md)
>
> 本文档是 IR 指令集和内存布局的**设计参考**。具体的 PenguinLang 实现步骤、数据结构定义、测试策略在实现计划文档中。

## Context

EmperorPenguin 已完成 BoundTree 全部 7 个 Pass。下一步需要实现一套完整的 IR (EmperorIR) 和内存布局系统，该系统需要同时支持：
1. **LLVM IR 代码生成** (AOT 编译生成可执行文件)
2. **LLVM JIT 执行** (用于编译时元编程 `#fun` 的动态执行)

设计参考了 BabyPenguin 的 BabyPenguinIR 指令集、RuntimeValue 体系、以及 PenguinLang 的语言特性。

---

## 1. IR 指令集 (EmperorIR)

采用 **三地址码 + SSA 兼容** 形式，使用类型化虚拟寄存器 (`%v0`, `%v1`, ...)，可直接映射到 LLVM IR。

### 1.1 常量与字面量
```
%dst = CONST <type> <value>
%dst = CONST_NULL <type>
```

### 1.2 算术与逻辑 (全部带类型，无隐式转换)
```
%dst = ADD|SUB|MUL|SDIV|UDIV|SREM|UREM <type> %lhs, %rhs
%dst = AND|OR|XOR|SHL|SHR <type> %lhs, %rhs
%dst = NEG|NOT <type> %operand
```

### 1.3 比较 (返回 i1)
```
%dst = EQ|NE|SLT|ULT|SLE|ULE|SGT|UGT|SGE|UGE <type> %lhs, %rhs
```

### 1.4 内存操作
```
%dst = ALLOCA <type>                    // 栈分配
%dst = LOAD <type> %ptr                 // 从指针加载
STORE %ptr, %value                       // 存储到指针
%dst = GEP <type> %ptr, <offset>        // 获取子元素指针 (struct field / array element)
%dst = INDEX <type> %arr, %idx          // 数组元素访问
```

### 1.5 类型转换
```
%dst = CAST <srctype> <dsttype> %val
%dst = BITCAST <srctype> <dsttype> %val
%dst = ISINSTANCE <classtype> %obj      // 运行时类型检查 (用于 is 操作符)
```

类与接口之间的转换是零开销的 (指针不变)，无需专门指令。

### 1.6 对象构造
```
%dst = NEW <classtype>                  // GC_ALLOC + 调用构造器
%dst = NEW_ENUM <enumtype> <variantidx> [ %payload ]  // 创建枚举实例 (值类型, 栈上)
```

`NEW <classtype>` 的 IR 展开为:
```
%obj = GC_ALLOC <classtype>
CALL void @ClassName_ctor(%obj, <ctor_args>)
```

### 1.7 函数调用
```
%dst = CALL <functype> %callee( %arg0, %arg1, ... )       // 直接调用 (PenguinLang 函数)
%dst = CALL_VIRT <functype> %obj, <ifaceidx>, <methodslot>( %arg0 )  // 通过 metadata 接口分派
%dst = CALL_EXTERN <name>( %arg0, %arg1, ... )             // C 风格外部函数调用 (extern "C")
```

**CALL_EXTERN** 用于调用标记为 `extern` 的 PenguinLang 函数，按照 C ABI 调用约定处理：
- 直接使用 C 函数名 (带命名空间前缀，通过 mangling 映射)
- 遵循目标平台 C 调用约定 (System V AMD64 / Windows x64)
- String 类型需要 marshal 为 `const char*` (后续处理)
- 不经过 PenguinLang 的引用计数 (GC_RETAIN/GC_RELEASE 不适用于 extern 调用)

### 1.8 控制流
```
BR <label>                                              // 无条件跳转
BR_COND %cond, <true_label>, <false_label>              // 条件跳转
BR_SWITCH <type> %val, <default_label>, [ <val0> <label0>, ... ]
RET [ %val ]                                            // 返回 (void 或 带值)
```

### 1.9 GC 指令

```
%dst = GC_ALLOC <classtype>                         // 分配对象 (refcount=1, 零初始化)
GC_RETAIN %obj                                      // 引用计数 +1
GC_RELEASE %obj                                     // 引用计数 -1，可能触发析构和释放
```

这些专用指令为后续 GC 策略升级保留灵活性——编译器无需关心底层实现是引用计数还是分代回收，只需在正确位置插入这些指令。

**GC_ALLOC** 的语义:
- 分配 `<classtype>` 大小的内存
- 零初始化所有字段
- 设置 header: `refcount = 1`, `metadata = ClassMetadata`
- 返回对象指针

**GC_RETAIN** 的语义:
- 对象引用计数 +1 (原子操作)
- null 安全 (传入 null 无操作)

**GC_RELEASE** 的语义:
- 对象引用计数 -1 (原子操作)
- 若降为 0: 调用析构器 (释放引用类型字段) → 释放内存
- null 安全

**赋值引用类型字段** (`obj.field = new_value`):
```
%field_ptr = GEP %obj, <field_offset>
%old       = LOAD ptr, %field_ptr
STORE %field_ptr, %new_obj
GC_RETAIN %new_obj
GC_RELEASE %old
```

**局部变量 (栈上) 引用类型**:
```
%obj = GC_ALLOC <MyClass>
// ... 使用 %obj ...
// 函数返回前:
GC_RELEASE %obj
```

### 1.10 杂项
```
NOP
SIGNAL <code>                                           // 调试信号/断点
PHI <type> [ %val0 <label0>, %val1 <label1>, ... ]      // SSA phi 节点
```

**设计原则**: IR 不包含协程/事件专用指令。初期仅支持 PenguinLang 同步子集。

### 与 BabyPenguinIR 的映射关系

| BabyPenguinIR                | EmperorIR                           |
|------------------------------|-------------------------------------|
| AssignLiteralToSymbol        | CONST                               |
| AssignmentInstruction        | COPY (LLVM 寄存器赋值)              |
| BinaryOperationInstruction   | ADD/SUB/MUL/EQ/NE/...              |
| UnaryOperationInstruction    | NEG/NOT                             |
| GotoInstruction              | BR / BR_COND                        |
| ReturnInstruction            | RET                                 |
| FunctionCallInstruction      | CALL / CALL_VIRT                    |
| NewInstanceInstruction       | NEW / NEW_ENUM                      |
| ReadMemberInstruction        | GEP + LOAD                          |
| WriteMemberInstruction       | GEP + STORE + GC_RETAIN + GC_RELEASE |
| CastInstruction              | CAST (类/接口转换零开销)            |
| SignalInstruction            | SIGNAL                              |

---

## 2. 内存布局

### 2.1 基本类型 (值类型)

| PenguinLang | LLVM Type | Size | Align |
|-------------|-----------|------|-------|
| void        | void      | 0    | -     |
| bool        | i8        | 1    | 1     |
| i8          | i8        | 1    | 1     |
| i16         | i16       | 2    | 2     |
| i32         | i32       | 4    | 4     |
| i64         | i64       | 8    | 8     |
| u8-u64      | 同 signed | 同   | 同    |
| f32         | float     | 4    | 4     |
| f64         | double    | 8    | 8     |
| char        | i32       | 4    | 4     |

有符号/无符号使用相同 LLVM 类型，符号性仅在比较、除法、移位时影响操作。

### 2.2 引用类型对象 (单指针设计)

**核心设计**: 引用类型变量持有的是**直接指向对象的指针**。对象不可被 GC 移动（非移动式 GC）。

```
// 变量持有的:
//   ptr → Object (直接指针，一次解引用访问字段)

// 对象布局:
struct Object {                  // 每个 class 类型不同
    i64 refcount;                // offset 0:  GC 引用计数
    ptr metadata;                // offset 8:  → ClassMetadata (全局常量)
    // fields...                 // offset 16: 字段数据
};
```

类 `MyClass` 字段 `x: i32, y: f64, z: MyOtherClass`:

```
struct MyClass {                 // 40 bytes
    i64 refcount;                // offset 0
    ptr metadata;                // offset 8: → @MyClass_metadata
    i32 x;                       // offset 16
    [4 padding]
    f64 y;                       // offset 24
    ptr z;                       // offset 32: → MyOtherClass (直接指针)
};
```

**访问路径**:
```
let obj: MyClass = ...;          // obj 是 ptr (直接指向对象)
obj.x                            // obj→x           (1 次解引用)
obj.z                            // obj→z           (1 次解引用，得到另一对象指针)
obj.z.x                          // obj→z→x         (2 次解引用)
```

**关键特性**:
- 字段访问只需一次解引用，性能最优
- 对象一旦分配就不再移动（非移动式 GC，简化实现）
- 类引用和接口引用是同一个指针
- `metadata` 始终可通过固定偏移访问，为反射打基础

### 2.3 String (引用类型)

```
struct PenguinString {           // 变长
    i64 refcount;                // offset 0
    ptr metadata;                // offset 8: → @String_metadata
    i64 length;                  // offset 16: 字符串长度
    i8  data[];                  // offset 24: UTF-8 数据
};
```
变量持有 `ptr` 直接指向 PenguinString。字符串字面量作为全局常量。

### 2.4 ClassMetadata (类元数据)

每个类生成一个全局唯一的 ClassMetadata 常量：

```
struct ClassMetadata {
    ptr name;                             // 类名 (用于反射/调试)
    i32 instance_size;                    // 实例总大小 (含 header)
    i32 field_count;                      // 字段数量
    ptr field_layout;                     // 字段偏移表: i32[] (实例中的偏移量)
    ptr field_types;                      // 字段类型元数据: ClassMetadata[]
    ptr field_flags;                      // 字段标志: i8[] (0=值类型, 1=引用类型)
    ptr virtual_method_table;             // 虚方法表: ptr[]
    i32 interface_count;                  // 实现的接口数量
    ptr interface_map;                    // 接口映射表: InterfaceMapEntry[]
    ptr destructor;                       // 析构函数指针
};

struct InterfaceMapEntry {
    ptr interface_id;                     // 接口标识 (唯一全局常量)
    ptr interface_method_table;           // 该接口的方法实现表: ptr[]
};
```

### 2.5 Enum (值类型)

采用 tagged union 布局：

```
struct EnumType {                 // 大小 = max(align) + max(payload) 向上对齐
    i32 _variant;                 // 变体判别符
    [padding to max align]
    <largest_payload> _payload;
};
```

无 payload 的枚举只有 `{ i32 _variant }`。实现接口的枚举被装箱为堆分配对象（同 class 布局）。

### 2.6 Interface Reference

接口引用就是**对象指针**，与类引用完全相同。

```
let obj: MyClass = new MyClass();     // obj 类型: ptr
let iface: IFoo = obj;                // iface 类型: ptr (同一个指针)
```

接口方法分派通过 `obj→metadata→interface_map` 查找。编译期已知类型时，metadata 是全局常量，可优化为固定偏移。

### 2.7 Function Value (闭包)

```
struct FunctionValue {           // 16 bytes
    ptr code;                    // 函数代码指针
    ptr env;                     // 捕获环境指针 (null = 纯函数指针)
};
```
LLVM: `{ ptr, ptr }`。静态函数 `env = null`，lambda `env` 指向堆分配的捕获结构。

---

## 3. 类型系统

### 3.1 EmperorIRType 层次

```
EmperorIRType (抽象基类)
├── PrimitiveType(name, llvm_type, size, alignment)
├── StringType                                         // → ptr
├── ClassType(name, fields, metadata_ref)              // → { i64, ptr, fields... }*
├── EnumType(name, variants, payload_type)             // → { i32, payload_union }
├── InterfaceType(name, method_sigs)                   // → ptr (同 class)
├── FunctionType(return_type, param_types)             // → { ptr, ptr }
├── ArrayType(element_type, count)                     // → [ N x T ]
├── PointerType(pointee_type)                          // → ptr
└── VoidType
```

### 3.2 泛型单态化

泛型通过单态化实现。每个具体实例化生成独立的 LLVM 结构体类型和函数。

---

## 4. 协程与高级功能 (后续扩展)

**初期不支持**。协程 (`async`/`wait`/`yield`)、事件系统 (`emit`/`on`) 等功能将在 IR 基础稳定后通过高层转换实现，使用基本 IR 指令组合（状态机、函数调用等）。

---

## 5. 函数调用约定

### 5.1 普通函数
直接映射为 LLVM 函数，遵循目标平台 C 调用约定。基本类型按值传递，引用类型传指针，枚举按值传。

### 5.2 实例方法
`this` 作为第一个参数（对象指针）。通过 `this→field` 直接访问字段。

### 5.3 虚分派 (接口方法调用)

CALL_VIRT 通过 `obj→metadata→interface_map` 查找方法:
```
%metadata   = load ptr, ptr %obj, i32 1           // obj→metadata (offset 8)
%imap       = load ptr, ptr %metadata, i32 <imap> // metadata→interface_map
%entry      = load ptr, ptr %imap, i32 <idx>      // InterfaceMapEntry
%method_tbl = load ptr, ptr %entry, i32 1          // method table
%func_ptr   = load ptr, ptr %method_tbl, i32 <slot>
%result     = call %func_ptr(ptr %obj, ...)
```

编译期已知类型时，metadata 是全局常量，可优化。

### 5.4 外部函数调用 (extern)

CALL_EXTERN 映射为 LLVM 中对 C 函数的直接调用:
```llvm
declare i32 @puts(ptr)

define void @NS_initial_routine_0() {
    %str = ... ; string → const char* marshal
    call i32 @puts(ptr %str)
    ret void
}
```

extern 函数在 LLVM Module 中用 `declare` 声明，不生成定义。PenguinLang 的 `extern` 函数名通过规则映射为 C 符号名 (如 `__builtin_print` → `__builtin_print`)。
```
%code = extractvalue { ptr, ptr } %closure, 0
%env  = extractvalue { ptr, ptr } %closure, 1
%result = call %code(ptr %env, ...)
```

---

## 6. 对象模型

### 6.1 ClassMetadata LLVM 生成

```llvm
@Foo_vtable = private constant [3 x ptr] [
    ptr @Foo_method1, ptr @Foo_method2, ptr @Foo_method3
]
@Foo_IBar_itable = private constant [2 x ptr] [
    ptr @Foo_IBar_methodA, ptr @Foo_IBar_methodB
]
@Foo_imap_entry_IBar = private constant { ptr, ptr } {
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

### 6.2 对象创建与引用

```llvm
; 分配对象 (GC_ALLOC 指令)
%obj = gc_alloc %Foo
; 语义: malloc(40) + memset(0) + refcount=1 + metadata=@Foo_metadata

; 类引用: %obj
; 接口引用: 也是 %obj (同一指针)
```

### 6.3 字段访问

值类型字段 (`obj.x: i32`):
```llvm
%x_ptr = getelementptr ptr %obj, i32 16         ; 直接偏移到字段
%x     = load i32, ptr %x_ptr
```

引用类型字段 (`obj.z: MyOtherClass`):
```llvm
%z_ptr = getelementptr ptr %obj, i32 32         ; 直接偏移到字段
%z     = load ptr, ptr %z_ptr                   ; 另一个对象指针
; 访问 z 的字段: getelementptr %z, <offset>
```

### 6.4 `is` 类型检查
```llvm
%metadata = load ptr, ptr %obj, i32 8            ; obj→metadata
%result   = call i1 @__check_interface(%metadata, ptr @IBar_interface_id)
```

### 6.5 反射基础

ClassMetadata 已包含完整类型信息，后续可支持：
- 运行时类型查询、字段遍历、动态接口检查、序列化

---

## 7. GC 策略

### 方案: 非移动式引用计数 (GC_ALLOC / GC_RETAIN / GC_RELEASE)

编译器在 IR 中插入专用的 GC 指令，底层实现暂时使用引用计数 + malloc/free。
对象一旦分配就不移动。

### 7.1 GC_ALLOC 语义

```
%dst = GC_ALLOC <classtype>
```

初期实现:
```c
ptr gc_alloc_impl(i32 size, ptr metadata) {
    ptr obj = malloc(size);
    memset(obj, 0, size);
    *(i64*)obj = 1;              // refcount = 1
    *(ptr*)(obj + 8) = metadata; // metadata
    return obj;
}
```

后续可替换为: 分代堆分配、bump allocator 等。

### 7.2 GC_RETAIN / GC_RELEASE 语义

初期实现:
```c
void gc_retain_impl(ptr obj) {
    if (!obj) return;
    atomic_fetch_add((i64*)obj, 1);
}

void gc_release_impl(ptr obj) {
    if (!obj) return;
    i64 old = atomic_fetch_sub((i64*)obj, 1);
    if (old == 1) {
        ptr meta = *(ptr*)(obj + 8);
        if (((ClassMetadata*)meta)->destructor)
            ((ClassMetadata*)meta)->destructor(obj);
        free(obj);
    }
}
```

后续可替换为: 写屏障、mark bit 操作等，IR 指令不变。

### 7.3 编译器自动插入规则

| 场景 | 操作 |
|------|------|
| 引用类型赋值给局部变量 | GC_RETAIN |
| 局部变量离开作用域 | GC_RELEASE |
| 引用类型赋值给对象字段 | GC_RELEASE 旧值 + GC_RETAIN 新值 |
| 引用类型作为函数参数 | GC_RETAIN |
| 函数返回时 | GC_RELEASE 所有局部引用 |
| 引用类型作为返回值 | GC_RETAIN (所有权转移) |
| 值类型 | 不做任何操作 |
| String | 作为引用类型同样处理 |

### 7.4 赋值引用字段的 IR 序列

`obj.field = new_value`:
```
%field_ptr = GEP %obj, <field_offset>
%old       = LOAD ptr, %field_ptr
STORE %field_ptr, %new_obj
GC_RETAIN %new_obj
GC_RELEASE %old
```

### 7.5 析构器生成

```llvm
define void @Foo_dtor(ptr %obj) {
    %z_ptr = getelementptr ptr %obj, i32 32
    %z     = load ptr, ptr %z_ptr
    gc_release %z
    ret void
}
```

### 7.6 后续升级灵活性

GC 专用指令的优势: IR 层面与具体 GC 实现解耦。

| 初期实现 | 后续可替换为 |
|----------|-------------|
| GC_ALLOC → malloc + refcount=1 | 分代堆 bump allocation |
| GC_RETAIN → refcount++ | 写屏障 / mark bit set |
| GC_RELEASE → refcount-- + 可能 free | mark-sweep / 分代回收 |

更换 GC 策略只需修改 IR → LLVM IR 翻译层和运行时，编译器前端和 IR 生成 pass 不需要改动。

---

## 8. 模块结构

### 8.1 每个编译单元生成一个 LLVM Module

```llvm
; 对象类型 (每个类不同)
%Foo = type { i64, ptr, i32, f64, ptr }           ; { refcount, metadata, x, y, z }
%ClassMetadata = type { ptr, i32, i32, ptr, ptr, ptr, ptr, i32, ptr, ptr }

; 全局常量
@string_lit_0 = private constant { i64, ptr, i64, [5 x i8] } { ... }
@Foo_metadata = private constant %ClassMetadata { ... }

; GC 运行时 (由 gc 指令调用)
declare ptr  @__gc_alloc_impl(i32, ptr)
declare void @__gc_retain_impl(ptr)
declare void @__gc_release_impl(ptr)

; 函数定义
define i32 @NS_ClassName_method(ptr %this, i32 %p) { ... }

; 主入口
define i32 @__penguin_main(i32 %argc, ptr %argv) {
    call void @__runtime_init()
    call void @NS_initial_routine_0()
    ret i32 0
}
```

### 8.2 IR 数据结构

```
EmperorIRModule:
  name: string
  types: List<EmperorIRType>
  globals: List<EmperorIRGlobal>
  functions: List<EmperorIRFunction>
  extern_decls: List<EmperorIRExternDecl>

EmperorIRFunction:
  name, linkage, return_type, parameters
  basic_blocks: List<EmperorIRBasicBlock>

EmperorIRBasicBlock:
  label: string
  instructions: List<EmperorIRInstruction>
  terminator: BR | BR_COND | RET | ...
```

---

## 9. 编译管线扩展

| Pass | 名称 | 职责 |
|------|------|------|
| 10 | Monomorphization | 展开所有泛型为具体实例化 |
| 11 | EmperorIRGeneration | 遍历语义模型生成 EmperorIR |
| 12 | EmperorIRLowering | IR 级别优化和验证 |
| 13 | LLVMIRGeneration | EmperorIR → LLVM IR |
| 14 | LLVMBackend | LLVM 优化和代码生成 (JIT/AOT) |

---

## 10. 实现顺序

| Phase | 内容 | 依赖 |
|-------|------|------|
| **Phase 1** | 类型系统 + 内存布局 | 无 |
| | - EmperorIRType 层次定义 | |
| | - 类型布局计算 (size, alignment, field offsets) | |
| | - 单态化 pass 框架 | |
| **Phase 2** | 基本 IR + 代码生成 | Phase 1 |
| | - CONST, 算术, 比较, BR, BR_COND, RET | |
| | - 函数定义, CALL, ALLOCA, LOAD, STORE | |
| **Phase 3** | 对象模型 | Phase 2 |
| | - NEW, GEP, 字段 LOAD/STORE | |
| | - ClassMetadata 生成 | |
| | - 构造器、接口分派、`is` 检查 | |
| | - NEW_ENUM 和变体访问 | |
| **Phase 4** | 控制流 + 闭包 | Phase 2 |
| | - BR_SWITCH, PHI | |
| | - FunctionValue, 闭包创建和调用 | |
| | - while, if, for | |
| **Phase 5** | GC + 运行时 | Phase 3 |
| | - GC_ALLOC, GC_RETAIN, GC_RELEASE 指令实现 | |
| | - 编译器自动 retain/release 插入 | |
| | - 析构器生成、基本运行时 | |
| **Phase 6** | LLVM 后端集成 | Phase 4+5 |
| | - EmperorIR → LLVM IR 翻译 | |
| | - LLVM JIT 集成、AOT 代码生成 | |

---

## 11. 关键文件参考

| 文件 | 用途 |
|------|------|
| `BabyPenguin/VirtualMachine/BabyPenguinIR.cs` | 现有 IR 指令，新 IR 需覆盖 |
| `BabyPenguin/VirtualMachine/RuntimeFrame.cs` | VM 执行引擎参考 |
| `BabyPenguin/VirtualMachine/RuntimeValue.cs` | 运行时值层次参考 |
| `BabyPenguin/SemanticInterface/ICodeContainer.cs` | 代码生成逻辑 |
| `BabyPenguin/SemanticInterface/IVTableContainer.cs` | VTable 结构 |
| `EmperorPenguin/src/ast/AST.penguin` | AST 节点定义 |

---

## 12. 验证方案

1. **Phase 1**: 对每种类型计算 size/alignment，验证正确性
2. **Phase 2**: 编译简单算术函数，JIT 执行验证返回值
3. **Phase 3**: 编译含类和接口的程序，验证对象创建、字段访问、接口分派
4. **Phase 4**: 编译含循环、分支、闭包的程序
5. **Phase 5**: 长时间运行验证无内存泄漏
6. **Phase 6**: 端到端编译示例程序，验证自举可行性
