# EmperorIR: Penguin-lang Compilation IR 设计方案

## 1. 设计目标

EmperorIR 是位于 Bound Tree 和 LLVM IR 之间的中间表示，需要在以下两者之间取得平衡：

- **足够低层**：可以直接一对一映射到 LLVM IR（三地址码、类型化虚拟寄存器、基本块+终结指令）
- **足够高层**：保留语言级语义（虚分派、接口映射、GC语义、协程状态），方便后续分析和优化
- **支持两种执行模式**：AOT 编译（→ LLVM IR → native）和 JIT 执行（用于 `#fun` 等编译时求值）

## 2. IR 核心理念

| 特性 | BabyPenguinIR | EmperorIR（新设计） |
|------|--------------|-------------------|
| 形式 | 符号导向树形IR | **SSA兼容三地址码** |
| 操作数 | 命名符号（ISymbol） | **类型化虚拟寄存器** `%v0`, `%v1` |
| 内存模型 | 隐式（Dictionary<string,IRuntimeValue>） | **显式**（ALLOCA/LOAD/STORE/GEP） |
| GC | 无（C# GC 托管） | **显式GC指令**（GC_ALLOC/RETAIN/RELEASE） |
| 控制流 | GOTO + 标签 | **BR/BR_COND + 基本块** |
| 类型 | IType 引用 | **具体 LLVM 类型映射** |
| 对象布局 | 字符串字段名 | **固定偏移量**（via GEP） |
| 编译目标 | 解释执行 | **LLVM IR → Native / JIT** |

## 3. 完整指令集

### 3.1 寄存器与常量

```
%r = CONST <type> <value>         // 整型、浮点、bool、char字面量
%r = CONST_STRING <value>         // 字符串字面量（引用类型，全局常量）
%r = CONST_NULL <type>            // 空引用（用于 Option.none 等）
%r = CONST_ZERO <type>            // 零初始化（所有字段为0）
%r = CONST_ENUM <enumtype> <variant_idx> [%payload]  // 编译时常量枚举
```

### 3.2 算术运算（全部显式类型，无隐式转换）

```
%r = ADD|SUB|MUL <type> %a, %b               // 整数/浮点算术
%r = SDIV|UDIV|SREM|UREM <type> %a, %b       // 有/无符号除法和取模
%r = FADD|FSUB|FMUL|FDIV|FREM <type> %a, %b   // 浮点专用
```

### 3.3 位运算和逻辑

```
%r = AND|OR|XOR <type> %a, %b
%r = SHL|ASHR|LSHR <type> %a, %b             // 左移、算术右移、逻辑右移
%r = NEG <type> %a                            // 取负
%r = NOT <type> %a                            // 位取反（~）
%r = LOGICAL_NOT <type> %a                    // 逻辑非（!），返回 bool
```

### 3.4 比较（返回 `bool`，即 `i1`）

```
%r = EQ|NE <type> %a, %b                     // 等式比较
%r = SLT|ULT|SLE|ULE|SGT|UGT|SGE|UGE <type> %a, %b  // 有/无符号序比较
%r = FLT|FLE|FGT|FGE <type> %a, %b            // 浮点序比较
```

### 3.5 类型转换

```
%r = SEXT|ZEXT <srctype> <dsttype> %v         // 有/无符号扩展（i32→i64）
%r = TRUNC <srctype> <dsttype> %v             // 截断（i64→i32）
%r = SITOFP|UITOFP <srctype> <dsttype> %v     // 整型→浮点
%r = FPTOSI|FPTOUI <srctype> <dsttype> %v     // 浮点→整型
%r = FPEXT <srctype> <dsttype> %v             // f32→f64
%r = FPTRUNC <srctype> <dsttype> %v           // f64→f32
%r = BITCAST <srctype> <dsttype> %v           // 位模式重解释
%r = POINTER_CAST <srctype> <dsttype> %v       // 指针类型转换（类→接口，零开销）
```

### 3.6 内存操作

```
%r = ALLOCA <type>                  // 栈上分配（值类型、基本类型局部变量）
%r = ALLOCA_ARRAY <type> <count>    // 栈上分配数组
%r = LOAD <type> %ptr               // 从指针加载
STORE %ptr, %value                   // 存储到指针
%r = GEP <type> %ptr, <index>       // 结构体字段/数组元素指针计算
%r = EXTRACT_VALUE <aggr_type> <index> %aggr  // 从聚合值中提取（enum payload等）
%r = INSERT_VALUE <aggr_type> <index> %aggr, %val  // 插入到聚合值
```

### 3.7 对象操作

```
%r = NEW <classtype>                // GC堆分配 + 零初始化 + refcount=1
%r = NEW_ENUM <enumtype> <variant_idx> [%payload]  // 创建枚举实例（值类型）
%r = ENUM_VARIANT %enum             // 获取枚举变体判别符（i32）
%r = ENUM_PAYLOAD <enumtype> <variant_idx> %enum   // 获取枚举有效载荷
%r = IS_ENUM_VARIANT %enum <variant_idx>            // 判断枚举是否为某变体，返回bool
```

### 3.8 函数调用

```
%r = CALL <functype> @func(%a, %b, ...)              // 直接调用
%r = CALL_VIRT <functype> %obj, <vtable_idx>, <slot> (%args...)  // 虚分派（接口方法）
%r = CALL_EXTERN <functype> @c_func(%a, ...)          // C ABI 外部调用（extern函数）
%r = CALL_CLOSURE <functype> %closure(%a, ...)        // 闭包调用
```

### 3.9 控制流（基本块终结指令）

```
BR <label>                                              // 无条件跳转
BR_COND %cond, <true_label>, <false_label>              // 条件跳转
BR_SWITCH <type> %val, <default_label> [%v1-><L1>, ...] // 多路跳转（枚举匹配）
RET [%val]                                               // 返回（void 或 带值）
```

### 3.10 GC 指令

```
%r = GC_ALLOC <classtype>              // 分配对象（refcount=1, 零初始化）
GC_RETAIN %obj                          // 引用计数+1（原子操作，null安全）
GC_RELEASE %obj                         // 引用计数-1，必要时释放（null安全）
```

### 3.11 运行时支持

```
%r = IS_INSTANCE <type> %obj                           // 运行时类型检查（is 操作符）
%r = STRING_LENGTH %str                                // 字符串长度
%r = STRING_CONCAT %a, %b                              // 字符串拼接
SIGNAL <code>                                           // 调试信号（断点）
NOP                                                     // 空操作（标签锚点）
```

### 3.12 Phi 节点（SSA 构造）

```
%r = PHI <type> [%v0, <L0>], [%v1, <L1>], ...
```

## 4. 内存布局（完全指定）

### 4.1 基本类型

| PenguinLang | LLVM Type | Size | Align | 说明 |
|-------------|-----------|------|-------|------|
| `void` | `void` | 0 | — | |
| `bool` | `i1`（栈）/ `i8`（内存） | 1 | 1 | |
| `i8`, `u8` | `i8` | 1 | 1 | 符号性在指令中区分 |
| `i16`, `u16` | `i16` | 2 | 2 | |
| `i32`, `u32` | `i32` | 4 | 4 | |
| `i64`, `u64` | `i64` | 8 | 8 | |
| `f32` | `float` | 4 | 4 | |
| `f64` | `double` | 8 | 8 | |
| `char` | `i32` | 4 | 4 | Unicode码点 |
| `string` | `ptr` | 8 | 8 | 引用类型（见4.4） |

有符号/无符号使用**相同LLVM类型**，符号性在 `SDIV/UDIV`、`SLT/ULT` 等指令操作码中区分。

### 4.2 值类型 vs 引用类型

- **值类型**：按值传递、复制。包括所有基本类型（string除外）、enum、实现 `IValueType` 的 class
- **引用类型**：按引用传递。包括 string、class（未实现 IValueType）、interface

### 4.3 类布局（引用类型）

```
struct <ClassName> {               // 变长，每个类不同
    i64  _refcount;                // offset 0: GC引用计数
    ptr  _metadata;                // offset 8: 指向 ClassMetadata
    // fields...                   // offset 16: 按声明顺序排列，遵守对齐
};
```

字段偏移在编译时由 `ClassMetadata` 给出，通过 `GEP ptr %obj, <field_index>` 访问。

**字符串字段**（如 `name: string`）在对象中存的是 `ptr`（指向 PenguinString），赋值时需要 `GC_RETAIN/GC_RELEASE`。

### 4.4 字符串布局（引用类型，不可变）

```
struct PenguinString {
    i64  _refcount;                // offset 0
    ptr  _metadata;                // offset 8
    i64  _length;                  // offset 16: 字节长度
    i8   _data[];                  // offset 24: UTF-8字节数据（变长）
};
```
字符串不可变，因此不需要对字符串内容写 GC（GC_RETAIN/GC_RELEASE 仅针对变量到变量的赋值）。

### 4.5 枚举布局（值类型）

采用 tagged union 布局：

```
struct <EnumType> {
    i32  _variant;                 // 变体判别符
    // padding 到最大payload对齐
    <union of all payloads> _data; // 最大大小的payload
};
```

无payload的枚举仅为 `{i32}`。`Option<T>` 为 `{i32, T}`。枚举作为值类型在栈上分配、复制。

### 4.6 接口布局（指针 = 对象指针）

接口引用就是对象指针。接口分派通过 `_metadata->interface_map` 查找。

### 4.7 闭包 / 函数指针

```
struct FunctionValue {             // 16 bytes
    ptr _code;                     // 函数代码指针
    ptr _env;                      // 捕获环境指针（null = 纯函数指针）
};
```

lambda 表达式创建闭包：在堆上分配捕获变量结构体，`_code` 指向生成的匿名函数，`_env` 指向捕获结构。

### 4.8 数组布局

```
// 固定大小数组: [N x T]（栈上）
// 动态数组: 作为内置类型 List<T> 实现，其本身是引用类型
struct PenguinArray<T> {           // List<T> 运行时
    i64  _refcount;
    ptr  _metadata;
    i64  _length;
    i64  _capacity;
    T    _data[];                  // 内联存储
};
```

### 4.9 虚分派元数据（ClassMetadata）

```
struct ClassMetadata {
    ptr  _name;                    // 类名字符串
    i32  _instance_size;          // 实例总大小
    i32  _field_count;            // 字段数量
    ptr  _field_offsets;          // 字段偏移表: i32[]
    ptr  _field_types;            // 字段类型: ClassMetadata*[]
    i8   _field_flags[];          // 字段标志: 0=值, 1=引用 (需要GC)
    ptr  _vtable;                 // 虚方法表: ptr[]
    i32  _iface_count;            // 实现的接口数
    ptr  _iface_map;              // 接口映射: {ptr iface_id, ptr itable}[]
    ptr  _dtor;                   // 析构函数指针
};
```

## 5. 语言特性的 IR 映射

### 5.1 变量声明与赋值

```penguin
let x: i32 = 42;
let y: mut i32 = 0;
y = x + 1;
```
→
```
%x = ALLOCA i32                ; 栈上分配不可变变量
STORE %x, CONST i32 42
%y = ALLOCA i32                ; 栈上分配可变变量
STORE %y, CONST i32 0
%t = LOAD i32 %x
%t2 = ADD i32 %t, CONST i32 1
STORE %y, %t2
```

### 5.2 对象创建与字段访问

```penguin
class Point { x: i32 = 0; y: i32 = 0; name: string = ""; }
let p = new Point();
p.x = 5;
```
→
```
%p = GC_ALLOC @Point            ; 分配 + refcount=1
CALL void @Point_ctor(%p)       ; 构造器初始化
%x_ptr = GEP @Point %p, 2       ; field index 2 (refcount=0, metadata=1)
STORE %x_ptr, CONST i32 5
```

### 5.3 枚举创建与模式匹配

```penguin
let a = new Option<i32>.some(42);
if (a is Option<i32>.some) {
    let v = a.some;
}
```
→
```
%a = ALLOCA Option_i32
%t = NEW_ENUM @Option_i32 0, CONST i32 42   ; variant 0 = some
STORE %a, %t
%v = ALLOCA i32
%cond = IS_ENUM_VARIANT %t 0
BR_COND %cond, <then>, <else>
then:
%val = ENUM_PAYLOAD @Option_i32 0 %t
STORE %v, %val
```

### 5.4 虚分派（接口方法调用）

```penguin
interface IFoo { fun bar(this) -> i32; }
class MyClass { impl IFoo { fun bar(this) -> i32 { return 1; } } }

fun test(obj: IFoo) -> i32 {
    return obj.bar();
}
```
→
```
define i32 @test(ptr %obj) {
    ; 通过 metadata->iface_map[IFoo_idx]->itable[bar_slot] 查找函数指针
    %meta = LOAD ptr, GEP @Object %obj, 1        ; offset 8 → _metadata
    %ifmap = LOAD ptr, GEP @ClassMetadata %meta, 6  ; iface_map
    %entry = GEP @InterfaceMapEntry %ifmap, <IFoo_idx>
    %itable = LOAD ptr, GEP @InterfaceMapEntry %entry, 1
    %func = LOAD ptr, GEP ptr %itable, <bar_slot>
    %r = CALL i32 %func(%obj)
    RET %r
}
```

### 5.5 字符串操作

字符串不可变。拼接、截取等操作通过运行时库函数（`STRING_CONCAT`、`STRING_SUBSTRING` 等），返回新字符串。

```penguin
let s = "hello" + " world";
```
→
```
%t0 = CONST_STRING "hello"
%t1 = CONST_STRING " world"
%s = STRING_CONCAT %t0, %t1
GC_RETAIN %s               ; 字符串拼接返回新对象，refcount=1
; ... in cleanup:
GC_RELEASE %s
```

### 5.6 控制流：if/while/for

```penguin
let mut x = 0;
while (x < 10) {
    x = x + 1;
}
```
→
```
%x = ALLOCA i32
STORE %x, CONST i32 0
BR <loop_head>
loop_head:
%v = LOAD i32 %x
%cond = SLT i32 %v, CONST i32 10
BR_COND %cond, <loop_body>, <loop_exit>
loop_body:
%v2 = LOAD i32 %x
%v3 = ADD i32 %v2, CONST i32 1
STORE %x, %v3
BR <loop_head>
loop_exit:
```

### 5.7 for-each 循环（迭代器协议）

```penguin
for (let v in range(0, 10)) { ... }
```
→ 翻译为对 `IIterator<T>` 接口的 while 循环：
```
%iter = CALL @range(%0, %10)
BR <loop_head>
loop_head:
%opt = CALL_VIRT %iter, <IIterator_idx>, <next_slot>(%iter)
%is_some = IS_ENUM_VARIANT %opt 0              ; Option.some
BR_COND %is_some, <loop_body>, <loop_exit>
loop_body:
%v = ENUM_PAYLOAD @Option_i32 0 %opt
...
BR <loop_head>
loop_exit:
GC_RELEASE %iter
```

### 5.8 协程 / async / await（栈无状态机变换）

这是对 IR 设计最重要的补充。协程通过**编译时变换**实现，IR 层面只需要支持**状态机结构体**。

异步函数 `async fun foo() -> i32` 被变换为：

```
// 状态机结构体
struct foo_SM {
    i32 _state;                  // 状态：0=入口, 1=第一个await后, ...
    i32 _result;                 // 最终返回值
    // 跨suspend点的局部变量...
};

// 生成的恢复函数
define i32 @foo_resume(ptr %sm) {
    %state = LOAD i32, GEP @foo_SM %sm, 0
    BR_SWITCH i32 %state, <invalid>, [
        CONST i32 0 -> <s0>,
        CONST i32 1 -> <s1>,
        ...
    ]
s0:
    ; 原始函数开头...
    STORE GEP @foo_SM %sm, 0, CONST i32 1     ; 更新状态
    RET <suspended_flag>                       ; 暂停，等待恢复
s1:
    ; 从第一个await后继续...
    STORE GEP @foo_SM %sm, 0, CONST i32 -1    ; 终态
    STORE GEP @foo_SM %sm, 1, %result
    RET <done_flag>
}
```

**IR 层面支持**：`RET` 需要新增一个返回状态标志位（`SUSPENDED` / `DONE`），或使用独立的 `YIELD` 指令。暂时可在 EmperorIR 中引入 `YIELD  %next_state`（将状态机推进到下一状态并暂停）。

实际上，协程变换在 EmperorIR 生成之前完成（作为 Bound Tree → EmperorIR 之间的一个 pass），EmperorIR 本身**不包含协程专用指令**。`RET` 支持可选状态即可：

```
RET <status> [%value]            ; status: DONE(0), SUSPENDED(1)
```

### 5.9 事件系统（emit / on / wait event）

事件系统本质上是**发布-订阅**模式 + **等待队列**。在 IR 层面：

- `emit Event(value)` → 调用运行时函数 `__emit_event(event_id, value)`，将事件数据推入所有已注册的 `on` 处理队列和所有 `wait` 阻塞者
- `on Event(x) { body }` → 在程序启动时注册 `body` 为一个独立的任务，每次事件发射时调度执行
- `wait Event` → 在协程状态机中插入一个 suspend 点，阻塞直到事件发射

这不需要特殊的 IR 指令，只需运行时库支持和协程 suspend/resume 机制：
```
; emit A(42):
CALL_EXTERN void @__emit_event(@A_event_id, CONST i32 42)

; wait A:
%v = CALL_EXTERN i32 @__wait_event_result(%sm, @A_event_id)
; 如果事件尚未就绪，__wait_event_result 内部设置 sm->state 并返回 SUSPENDED
```

### 5.10 元编程（#fun, const if, const for）

元编程在**编译时**完成，不产生运行时 IR：

- `#fun` → 编译时执行（通过 JIT 或 BabyPenguinVM 解释执行），结果替换调用点
- `const if` / `const for` → 编译时求值条件，**仅生成满足条件的分支**（死代码消除在 IR 生成前完成）
- `#template` → 单态化后展开为具体类型的具体 IR
- `#can_compile` → 编译时尝试编译代码片段，返回 bool

这些特性**不影响 IR 设计**，在 EmperorIR 生成阶段之前已被处理。

### 5.11 lambda / 闭包

```penguin
let f: fun<i32, i32> = fun(x: i32) -> i32 { return x + this.offset; };
```

→ 生成一个匿名函数 + 捕获结构体：

```
struct lambda_0_env {
    ptr _this;                   ; 捕获的 this 引用
};

define i32 @lambda_0(ptr %env, i32 %x) {
    %this = LOAD ptr, GEP @lambda_0_env %env, 0
    %off = LOAD i32, GEP @MyClass %this, <offset_idx>
    %r = ADD i32 %x, %off
    RET %r
}

; 闭包创建:
%env = GC_ALLOC @lambda_0_env
STORE GEP @lambda_0_env %env, 0, %this
%closure = INSERT_VALUE @FunctionValue {ptr, ptr} 0, @lambda_0
%closure = INSERT_VALUE @FunctionValue {ptr, ptr} 1, %env
```

## 6. GC 策略

### 6.1 基于引用计数的非移动式 GC

- `GC_ALLOC` → `malloc` + 零初始化 + `refcount=1`
- `GC_RETAIN` → 原子 `refcount++`（null安全）
- `GC_RELEASE` → 原子 `refcount--`，归零时调用析构器 + `free`

编译器在 IR 生成时**自动插入** RETAIN/RELEASE：

| 场景 | 操作 |
|------|------|
| 引用类型赋值给局部变量 | GC_RETAIN |
| 局部变量离开作用域 | GC_RELEASE |
| 引用类型赋值给对象字段 | GC_RELEASE旧值 + GC_RETAIN新值 |
| 引用类型作为函数参数（传引用） | 不操作（调用者持有引用） |
| 函数返回引用类型 | GC_RETAIN（所有权转移） |
| 值类型（基本类型、enum、IValueType类） | 不操作 |

### 6.2 IR 中的赋值序列

```
; obj.field = new_value（field 是引用类型）:
%fptr = GEP @MyClass %obj, <field_index>
%old = LOAD ptr %fptr
STORE %fptr, %new_value
GC_RETAIN %new_value
GC_RELEASE %old
```

### 6.3 析构器生成

对于包含引用类型字段的类，生成析构器：
```
define void @MyClass_dtor(ptr %obj) {
    %fptr = GEP @MyClass %obj, <ref_field_idx>
    %fval = LOAD ptr %fptr
    GC_RELEASE %fval
    RET
}
```

### 6.4 未来升级路径

IR 中 GC 专用指令（`GC_ALLOC`/`GC_RETAIN`/`GC_RELEASE`）与具体 GC 策略解耦。后续可替换为：
- 分代 GC（写屏障代替 GC_RETAIN）
- 标记-清除（bump allocator + 扫描）
- 追踪 GC（替换 GC_ALLOC 为 bump pointer allocation）

## 7. IR 数据结构（PenguinLang 实现）

```penguin
namespace ir {

// --- 类型系统 ---
enum IRTypeKind { VoidKind; BoolKind; IntKind; FloatKind; PtrKind; StructKind; ArrayKind; FuncKind; }

class IRType {
    kind: IRTypeKind;
    name: string;                    // e.g. "i32", "%MyClass", "Option_i32"
    size_bytes: i64;
    alignment_bytes: i64;
    // 对于 IntKind: bit_width, is_signed
    // 对于 StructKind: fields (name + type + offset)
    // 对于 FuncKind: return_type, param_types
    // 对于 PtrKind: pointee_type
}

// --- 虚拟寄存器 ---
class VirtualRegister {
    id: i64;
    ir_type: mut IRType;
}

// --- 指令 ---
enum IROpcode {
    // 常量
    CONST; CONST_STRING; CONST_NULL; CONST_ZERO; CONST_ENUM;
    // 算术
    ADD; SUB; MUL; SDIV; UDIV; SREM; UREM;
    FADD; FSUB; FMUL; FDIV; FREM;
    // 位/逻辑
    AND; OR; XOR; SHL; ASHR; LSHR; NEG; NOT; LOGICAL_NOT;
    // 比较
    EQ; NE; SLT; ULT; SLE; ULE; SGT; UGT; SGE; UGE; FLT; FLE; FGT; FGE;
    // 类型转换
    SEXT; ZEXT; TRUNC; SITOFP; UITOFP; FPTOSI; FPTOUI; FPEXT; FPTRUNC; BITCAST; POINTER_CAST;
    // 内存
    ALLOCA; ALLOCA_ARRAY; LOAD; STORE; GEP; EXTRACT_VALUE; INSERT_VALUE;
    // 对象
    NEW; NEW_ENUM; ENUM_VARIANT; ENUM_PAYLOAD; IS_ENUM_VARIANT;
    // 调用
    CALL; CALL_VIRT; CALL_EXTERN; CALL_CLOSURE;
    // 控制流
    BR; BR_COND; BR_SWITCH; RET; YIELD;
    // GC
    GC_ALLOC; GC_RETAIN; GC_RELEASE;
    // 运行时
    IS_INSTANCE; STRING_LENGTH; STRING_CONCAT;
    // 其他
    PHI; SIGNAL; NOP;
}

class IRInstruction {
    opcode: IROpcode;
    result: Option<VirtualRegister>;
    operands: List<VirtualRegister>;
    type_params: List<IRType>;       // 类型参数（如 GEP 的类型）
    int_params: List<i64>;           // 整数参数（如 variant_idx, field_index）
    string_params: List<string>;     // 字符串参数（如字面量值、标签名）
}

// --- 基本块 ---
class BasicBlock {
    label: string;
    instructions: List<IRInstruction>;
    terminator: IRInstruction;       // 必须是 BR/BR_COND/BR_SWITCH/RET/YIELD 之一
    predecessors: List<string>;      // 前驱块标签（用于PHI放置和优化）
}

// --- 函数 ---
class IRFunction {
    name: string;
    linkage: string;                 // "internal" / "external" / "export"
    return_type: IRType;
    parameters: List<{name: string, ir_type: IRType}>;
    basic_blocks: List<BasicBlock>;
    local_allocas: List<{reg: VirtualRegister, ir_type: IRType}>;
}

// --- 全局 ---
class IRGlobal {
    name: string;
    ir_type: IRType;
    is_constant: bool;
    initializer: Option<IRConstant>;
}

// --- 模块 ---
class IRModule {
    name: string;
    struct_types: List<IRType>;      // 所有结构体类型定义
    functions: List<IRFunction>;
    globals: List<IRGlobal>;
    extern_decls: List<{name: string, ir_type: IRType}>;
    metadata: List<ClassMetadata>;
}
```

## 8. 与 BabyPenguinIR 的映射

| BabyPenguinIR | EmperorIR | 说明 |
|---|---|---|
| LITERAL | CONST / CONST_STRING | 字面量加载 |
| ASSIGN | STORE / LOAD | 变量赋值 |
| ADD..XOR (17个) | ADD, SUB, MUL... (对应操作码) | 算术/比较 |
| unary_op (6个) | NEG, NOT, LOGICAL_NOT | 单目运算 |
| NEW | GC_ALLOC + CALL(ctor) / NEW_ENUM | 对象创建 |
| RDMBR | GEP + LOAD | 字段读取 |
| WRMBR | GEP + STORE + GC_RETAIN/RELEASE | 字段写入 |
| CALL | CALL / CALL_VIRT / CALL_EXTERN | 函数调用 |
| CAST | SEXT/ZEXT/TRUNC/SITOFP/... | 类型转换（精确） |
| WRENUM | INSERT_VALUE | 写枚举载荷 |
| RDENUM | EXTRACT_VALUE | 读枚举载荷 |
| GOTO | BR / BR_COND | 控制流 |
| RETN | RET / YIELD | 函数返回 |
| SIGNAL | SIGNAL | 调试信号 |
| NOP | NOP | 空操作 |

## 9. 编译管线扩展

在现有 EmperorPenguin 的 4 个 pass 之后新增：

| Pass | 名称 | 输入 | 输出 | 职责 |
|------|------|------|------|------|
| 5 | CoroutineTransform | Bound Tree | Bound Tree（状态机化） | 将 async/stateful 函数拆分为状态机 |
| 6 | Monomorphization | Bound Tree | Bound Tree（展开泛型） | 展开所有 `#template` 和泛型为具体实例 |
| 7 | EmperorIRGeneration | Bound Tree | IRModule | 遍历语义模型生成 EmperorIR |
| 8 | EmperorIROptimize | IRModule | IRModule | IR级优化（死代码消除、常量折叠、内联） |
| 9 | GCInsertionPass | IRModule | IRModule | 自动插入 GC_RETAIN/GC_RELEASE |
| 10 | LLVMIRGeneration | IRModule | LLVM Module | EmperorIR → LLVM IR |
| 11 | LLVMBackend | LLVM Module | Native / JIT | LLVM 优化 + 目标代码生成 |

## 10. 实现顺序

| Phase | 内容 | 关键产出 |
|-------|------|---------|
| **Phase 1: 基础** | IR 数据结构定义（IRType, IRInstruction, BasicBlock, IRFunction, IRModule） | `EmperorPenguin/src/ir/` 下的 `.penguin` 文件 |
| **Phase 2: 简单程序** | CONST, 算术, 比较, LOAD/STORE, ALLOCA, BR/BR_COND, RET, CALL | 支持编译 `initial { let x = 1+2; }` |
| **Phase 3: 类型系统** | 类布局/字段访问/GEP, NEW, GC_ALLOC/RETAIN/RELEASE, ClassMetadata | 支持类创建和字段操作 |
| **Phase 4: 控制流** | BR_SWITCH, PHI, while/for/if 完整支持 | 支持循环和分支 |
| **Phase 5: 枚举/接口** | NEW_ENUM, ENUM_PAYLOAD, IS_ENUM_VARIANT, CAST, CALL_VIRT, IS_INSTANCE | Option, Result, 接口分派 |
| **Phase 6: 协程** | CoroutineTransform pass, YIELD, 状态机框架 | async/await/yield |
| **Phase 7: 高级特性** | 字符串操作, 闭包, 事件系统, lambda | 完整语言支持 |
| **Phase 8: LLVM 后端** | EmperorIR → LLVM IR 翻译, JIT 执行 | AOT编译 + JIT |

## 11. 与现有计划的区别

相比原有的 `.claude/plans/emperor-penguin-ir.md`，本设计有以下增强：

1. **协程支持策略明确化**：通过编译时 CoroutineTransform pass 将 async 函数变为状态机，IR 层面仅需 `YIELD` 指令
2. **事件系统映射**：事件通过运行时库函数 + 协程 suspend 机制实现，不需 IR 特殊指令
3. **完整字符串模型**：PenguinString 引用类型布局 + 运行时函数 + GC 语义
4. **枚举操作细化**：`IS_ENUM_VARIANT`、`ENUM_VARIANT`、`ENUM_PAYLOAD`、`NEW_ENUM` 专用指令
5. **闭包/函数指针**：FunctionValue 类型 + `CALL_CLOSURE` 指令
6. **类型转换精确化**：区分 SEXT/ZEXT/TRUNC/SITOFP/FPTOSI 等不同转换语义
7. **元编程无 IR 开销**：`#fun`/`const if`/`const for` 在 IR 生成前处理完毕
8. **IR 数据结构以 PenguinLang 实现**：与 EmperorPenguin 自举目标一致
9. **浮点专用指令**：区分整数和浮点算术

## 12. 验证方案

1. **Phase 1**: 对每种类型计算 size/alignment，验证正确性
2. **Phase 2**: 编译简单算术函数，JIT 执行验证返回值
3. **Phase 3**: 编译含类和接口的程序，验证对象创建、字段访问、接口分派
4. **Phase 4**: 编译含循环、分支、闭包的程序
5. **Phase 5**: 长时间运行验证无内存泄漏
6. **Phase 6**: 端到端编译示例程序，验证自举可行性
