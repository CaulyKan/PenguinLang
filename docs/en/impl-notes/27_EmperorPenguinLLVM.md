# 27. EmperorPenguin LLVM Lowering

## Overview

This document defines the strategy for lowering EmperorPenguin IR to LLVM IR, including the memory layout of all types, how each data structure is represented, and the conversion rules from every IR instruction to LLVM IR.

**Implementation location**: `EmperorPenguin/src/llvm/LLVMEmitter.penguin` (driven by `LLVMCompiler.penguin`, which chains it after the IRGenerator)

**Output format**: LLVM IR text (`.ll` files), platform-independent; linking (clang + the C runtime archive) is handled by the `emperor`/`emperor_penguin.bat` driver scripts outside the compiler

**Core design decisions**:
- Value-type and reference-type classes are distinguished via the `ICopy`/`IRef` marker interfaces
- Reference-type class (IRef) variables hold **pointers directly to the heap object**
- Reference-type object headers contain a `metadata` pointer
- Value-type classes (ICopy) are allocated on the stack (entry-block alloca) and copied by value
- Class references and interface references are **the same pointer** (single-pointer design); value types go through BOX boxing when converted to interface references
- Enums (enum) are **value types**, represented as tagged unions
- Primitive types map directly to LLVM primitive types

---

## 1. Type Mapping

### 1.1 Primitive Types

| PenguinLang type | LLVM IR type | Size (bytes) | Alignment (bytes) | Notes |
|-----------------|-------------|-------------|-------------|------|
| `void` | `void` | 0 | — | No value |
| `bool` | `i8` | 1 | 1 | Boolean (0/1) |
| `i8` | `i8` | 1 | 1 | Signed 8-bit integer |
| `i16` | `i16` | 2 | 2 | Signed 16-bit integer |
| `i32` | `i32` | 4 | 4 | Signed 32-bit integer |
| `i64` | `i64` | 8 | 8 | Signed 64-bit integer |
| `u8` ~ `u64` | same as signed | same | same | Unsigned values use the same LLVM types; signedness is determined by the instructions |
| `f32` | `float` | 4 | 4 | IEEE 754 single precision |
| `f64` | `double` | 8 | 8 | IEEE 754 double precision |
| `char` | `i32` | 4 | 4 | Unicode code point |
| `string` | `ptr` | 8 | 8 | Pointer to a PenguinString (reference type) |

### 1.2 ICopy/IRef Value/Reference Type Classification

EmperorPenguin distinguishes value types from reference types via two marker interfaces, `ICopy` and `IRef`:

- **`ICopy`**: marked as a value type (stack allocation, by-value pass/copy semantics)
- **`IRef`**: marked as a reference type (heap allocation, by-pointer pass/shared semantics)
- **Automatic classification**: if a class does not explicitly implement `ICopy` or `IRef`:
  - all fields are value types → automatically a value type (equivalent to ICopy)
  - any field is a reference type, or a cyclic dependency is detected during classification → reference type (equivalent to IRef)
- **Conflict detection**: implementing both `ICopy` and `IRef` is a compile error
- **Default behavior**: primitive types (except string) and enums are always value types; string and interfaces are always reference types

### 1.3 Type Classification and LLVM Representation Strategy

| BoundType classification | is_value_type | LLVM representation | Passing style |
|---------------|--------------|----------|---------|
| Primitive types (except string) | Yes | Direct LLVM primitive types | Passed by value |
| enum | Yes | tagged union struct `{ ptr metadata, i64 tag, [N x i8] payload }` | Passed by value (the register holds a ptr to the storage) |
| class (value type, impl ICopy) | Yes | LLVM struct `{ ptr metadata, fields... }` (stack allocated; the register holds a ptr to the storage) | Passed by value (copied whole) |
| class (reference type, impl IRef) | No | `ptr` (points directly to the heap object) | Passed by pointer |
| interface | No (reference type) | `ptr` (the same pointer as a class reference; value types are BOX-boxed) | Passed by pointer |
| string | No (reference type) | `ptr` (points to a PenguinString) | Passed by pointer |
| function type | — | `{ ptr, ptr }` (code pointer + environment pointer) | Passed by value |

---

## 2. Data Structure Memory Layout

### 2.1 Reference-type class (impl IRef, heap allocated)

**Core design**: a reference-type variable holds a **pointer directly to the heap object**. Field access requires only one dereference.

```
// What a variable holds:
//   ptr → Object (direct pointer; one dereference to access fields)

// Object layout (differs per class type):
struct Object {
    ptr metadata;                // offset 0:  → ClassMetadata (global constant)
    // fields...                 // offset 8:  field data
};
```

**Access paths**:
```
let obj: MyClass = ...;          // obj is a ptr (pointing directly at the object)
obj.x                            // obj+8           (1 dereference)
obj.z                            // obj+24          (1 dereference, yielding another object pointer)
obj.z.x                          // obj+24→+8       (2 dereferences)
```

### 2.2 Value-type class (impl ICopy, stack allocated)

Value-type classes are allocated on the stack (entry-block `alloca`, zero-initialized). Field 0 of the LLVM struct type is likewise `ptr metadata` (layout unified with IRef so boxed instances can participate in virtual dispatch); fields start at offset 8. A value type in a register is represented as "a pointer to its storage"; by-value uses (argument passing / assignment / return) copy the entire struct via `coerce_operand`.

```
// Value-type class layout:
struct ValueClass {
    ptr metadata;                // offset 0:  → ClassMetadata (global constant)
    // fields...                 // offset 8:  field data
};

// LLVM struct type:
%class.Point = type { ptr, i32, i32 }  ; { metadata, x, y }
```

**Key differences** (relative to IRef):
- Allocated on the stack (entry-block alloca + zeroinitializer), never on the GC heap
- By-value copies duplicate the whole struct
- Registers hold pointers to the storage; fields are accessed with struct-indexed GEPs (from field 1 onward)

### 2.3 Detailed Class Memory Layout Rules

Depending on the ICopy/IRef classification, class memory layout takes two forms:

#### 2.3.1 Reference-type class (impl IRef)

Class instances are heap-allocated and represented in LLVM as `ptr`. The object header is `ptr metadata`, followed by the instance fields in declaration order.

```penguin
class MyClass {
    x: i32;
    y: mut i64 = 0;
    name: string;
    impl IRef {}        // explicitly marked as a reference type
}
```

```llvm
; LLVM struct type (without the header; only the field part is used for GEP computation)
; Actual object memory layout:
; offset 0:  ptr metadata → @MyClass_metadata
; offset 8:  i32 x
; offset 12: [4 padding]
; offset 16: i64 y
; offset 24: ptr name (points to a PenguinString)
; total: 32 bytes

%class.MyClass_fields = type {
    i32,                        ; [0] x
    i64,                        ; [1] y
    ptr                         ; [2] name
}
```

**Field offset computation**:
```
field_offset = 8 + field_position_offset   // 8 = sizeof(ptr metadata)
```

#### 2.3.2 Value-type class (impl ICopy)

Class instances are stack-allocated (entry-block alloca, zero-initialized); field 0 of the LLVM struct type is `ptr metadata` (layout unified with IRef), and fields start at offset 8.

```penguin
class Point {
    x: i32;
    y: i32;
    impl ICopy {}       // explicitly marked as a value type
}
```

```llvm
; Value-type class layout:
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

**Field offset computation**:
```
field_offset = 8 + field_position_offset   // 8 = sizeof(ptr metadata)
```

### 2.4 String Memory Layout (reference type)

A `string` value is a `ptr` pointing to a **header-prefixed block** (C-side definition in `EmperorPenguin/std/include/emperor_string.h`):

```llvm
; _emperor_string: variable-length structure (literals and heap strings share the same shape)
; offset 0:  ptr metadata   → @_emperor_string_metadata (EmperorClassMetadata,
;                                interface_count=0 — `x is IFace` cleanly returns false for string)
; offset 8:  i64 length     (length in bytes, excluding the trailing NUL; string_length reads it in O(1))
; offset 16: i8  data[]     (length bytes + data[length] = '\0', a C interop convenience)
```

- **Heap strings**: runtime strings are allocated by `_emperor_string_alloc(len)` via `_emperor_gc_alloc(16+len+1, is_string=1)` (the GC neither scans nor finalizes is_string blocks; the metaptr in the header is completely invisible to the collector).
- **Literals**: `@str_N = private constant { ptr, i64, [len+1 x i8] } { ptr @_emperor_string_metadata, i64 len, [len+1 x i8] c"...\00" }` — a literal's **value is the address of this very struct**, fully C-ABI-identical to heap strings (static storage, untracked by the GC). Modules containing literals carry one extra line `@_emperor_string_metadata = external global i8` (the address is taken only; resolved at link time from libcore_builtin.a / the exe's `-rdynamic`, and in JIT unit B via the process symbol table).
- **libc boundary**: `to_cstring = +16` (i.e. the data pointer); `from_cstring = -16` is legal only for values known to be data pointers. Foreign C strings (libc return values) must be copied via `_emperor_string_adopt_cstring`. Call-site marshaling for bare top-level externs is covered in §6 (string arguments `getelementptr +16`, string return values adopted).
- Immutable: strings have no member-write path; assignment / argument passing / by-value memcpy all share the pointer, and `==` is a content comparison (length first, then memcmp).

A variable holds a `ptr` pointing directly at this structure.

### 2.5 Enum Memory Layout (Tagged Union, value type + metadata ptr)

Enums are **value types** (stack allocated, passed by value); the first field of the struct is `ptr metadata` (pointing to a global constant EnumMetadata, shape-identical to ClassMetadata). A variable represents the enum in a register as a pointer to its storage.

```
struct EnumType {
    ptr metadata;                 // [0] enum metadata (global constant)
    i64 _variant;                 // [1] variant discriminator
    [N x i8] _payload;            // [2] payload byte array (only when there is a payload)
};
```

Enums without a payload have struct `{ ptr, i64 }`. The payload is represented as an **opaque byte array** (sized to the largest variant's payload) and accessed through typed loads/stores — the byte array forces every variant's copy to be byte-faithful, preventing the optimizer from truncating the copy at some variant's field boundaries.

```penguin
#template(T: type)
enum Option {
    some: T;      // payload: T
    none;         // no payload
}
```

```llvm
@Option__i32_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr } { ... }

%enum.Option__i32 = type {
    ptr,          ; [0] metadata → @Option__i32_metadata
    i64,          ; [1] _variant: 0=some, 1=none
    [4 x i8]      ; [2] _payload (unused for none)
}
```

**NEW_ENUM lowering** (stack allocation):
```llvm
%tmp = alloca %enum.Option__i32
%md_ptr = getelementptr %enum.Option__i32, ptr %tmp, i32 0, i32 0
store ptr @Option__i32_metadata, ptr %md_ptr
%var_ptr = getelementptr %enum.Option__i32, ptr %tmp, i32 0, i32 1
store i32 0, ptr %var_ptr                 ; some = 0 (the tag field is i64; low 4 bytes written)
%pay_ptr = getelementptr %enum.Option__i32, ptr %tmp, i32 0, i32 2
store i32 42, ptr %pay_ptr                ; typed store into the payload byte array
%result = ...                              ; the register holds %tmp as a ptr
```

**Enums with multi-typed payloads** (the payload is an opaque byte array sized to the largest variant):

```penguin
enum Shape {
    Circle(f64);           // payload: f64
    Rectangle(f64, f64);   // payload: two f64s (multi-field payloads are held in the byte array)
    Point;                 // no payload
}
```

```llvm
@Shape_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr } { ... }

%enum.Shape = type {
    ptr,              ; [0] metadata → @Shape_metadata
    i64,              ; [1] _variant: 0=Circle, 1=Rectangle, 2=Point
    [16 x i8]         ; [2] _payload (byte count of the largest variant's payload)
}
```

### 2.6 Interface Reference (single-pointer design)

An interface reference is just the **object pointer**, exactly identical to a class reference. Conversion from a reference-type class (impl IRef) is a zero-cost, same-pointer pass; a value-type class (impl ICopy) converted to an interface reference goes through **BOX** boxing — heap allocation (`_emperor_alloc_impl`) plus a copy of the entire struct (including metadata); virtual dispatch on the interface then targets the boxed object.

```penguin
let obj: MyClass = new MyClass();     // obj type: ptr (MyClass is IRef)
let iface: IFoo = obj;                // iface type: ptr (the same pointer)
```

Interface method dispatch is resolved through the `obj→metadata→interface_map` lookup. When the type is known at compile time, the metadata is a global constant and can be optimized into fixed offsets.

### 2.7 ClassMetadata (class metadata)

**All classes (both ICopy and IRef) and all enums** get a shape-identical 9-field metadata global constant `@<TypeName>_metadata` (an enum's size/field slots are zero; the interface_map covers interfaces implemented by variant payloads). GC traversal and runtime type checks depend on it.

```llvm
; @X_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr }
%ClassMetadata = type {
    ptr,    ; name → @.X_name (type-name string constant)
    i32,    ; instance_size (total instance size, including header)
    i32,    ; field_count
    ptr,    ; field_offsets → i32[] (field offset table, used by GC traversal)
    ptr,    ; field_is_ptr → i32[] (0=non-pointer field, 1=pointer field)
    ptr,    ; virtual_method_table (currently emitted as null; virtual dispatch always goes through the interface_map)
    i32,    ; interface_count
    ptr,    ; interface_map → InterfaceMapEntry[] (emitted as an inline { ptr, ptr } array)
    ptr     ; destructor (GC finalizer: reference types implementing IMemoryDispose point at their dispose_mem implementation, invoked before sweep reclamation; otherwise null)
}

%InterfaceMapEntry = type {
    ptr,    ; interface_id → @.<Iface>_interface_id (unique global string constant)
    ptr     ; interface_method_table → @<Type>_<Iface>_vtable (ptr[], the method implementation table for that interface)
}
```

**LLVM generation example**:

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

### 2.8 FunctionValue (closure)

```llvm
%FunctionValue = type {
    ptr,    ; code (function code pointer)
    ptr     ; env (captured environment pointer, null = pure function pointer)
}
```

Static functions have `env = null`; for lambdas, `env` points to a heap-allocated capture structure.

Calling a closure:
```llvm
%code = extractvalue { ptr, ptr } %closure, 0
%env  = extractvalue { ptr, ptr } %closure, 1
%result = call %code(ptr %env, ...)
```

### 2.9 Method Dispatch Model

#### 2.9.1 Deterministic Dispatch (Direct Dispatch)

When the receiver's concrete class type is known at compile time, **deterministic dispatch** (a direct call) is used:

```penguin
let p = new Point(1, 2);   // p: Point (concrete type)
p.show();                   // direct call to Point.show
```

```llvm
; deterministic dispatch — the target function is known at compile time
call void @"<global>.Point.show"(ptr %p)
```

**Decision rules** (SemanticModel layer):
- the member-access base expression's type is `ClassKind` → deterministic dispatch
- the target function is resolved in the class scope and a CALL instruction is generated
- no vtable or interface_map lookup is needed

#### 2.9.2 Dynamic Dispatch (via the Interface Map)

When the receiver's type is an **interface type**, dispatch must go through the runtime interface_map table lookup:

```penguin
let s: IShow = new Point(1, 2);   // s: IShow (interface type)
s.show();                          // runtime table-lookup dispatch
```

```llvm
; dynamic dispatch — runtime lookup through metadata→interface_map
%func_ptr = call ptr @_emperor_vtable_lookup(ptr %s, ptr @.IShow_interface_id, i32 0)
call void %func_ptr(ptr %s)
```

**Decision rules** (SemanticModel layer):
- the member-access base expression's type is `InterfaceKind` → dynamic dispatch
- the target function is resolved in the interface scope and a CALL_VIRT instruction is generated
- CALL_VIRT carries `interface_id` and `vtable_slot` for the runtime lookup

#### 2.9.3 Interface Default Methods

Interfaces can contain **default implementations** with method bodies. A class can choose in its `impl` block:
- **Empty impl** (`impl IFoo {}`): inherits all default implementations
- **Overriding impl** (`impl IFoo { fun method(...) { ... } }`): overrides the specified methods

Call paths for default methods:
- Concrete type calls a default method (empty impl) → deterministic dispatch to the interface's default function
- Concrete type calls an overriding method → deterministic dispatch to the class's override function
- Interface type calls a method → dynamic dispatch (the vtable points at the actual implementation)

vtable generation rules:
- Empty impl slot → points at the interface's default function
- Overriding impl slot → points at the class's override function

#### 2.9.4 `is` Type Checks

The `is` operator supports several type-check modes:

| Expression | IR instruction | Implementation | Applicable types |
|--------|---------|---------|---------|
| `x is EnumType.Variant` | ISENUM | compares the tagged union's variant tag | enum |
| `x is InterfaceType` | ISINSTANCE | runtime metadata interface_map lookup | IRef class (value types likewise, once BOX-boxed) |
| `x is ClassName` | ISINSTANCE | runtime metadata name/class_id comparison | IRef class (value types likewise, once BOX-boxed) |

**Compile-time optimization**: when the type information is statically determinable (e.g. a concrete type `is` a known interface), a constant `true`/`false` is emitted directly.

**Runtime checks require objects to carry metadata**: a value-type class (ICopy) can participate in runtime type checks once BOX-boxed; boxed objects take the same `_emperor_isinstance` / `_emperor_check_class` paths as reference-type objects.

### 2.10 C Interop Structures

The following C structs (`std/include/emperor_types.h`, `std/include/emperor_interop.h`) match the LLVM IR data layout exactly; C runtime code uses them to handle PenguinLang objects:

```c
// corresponds to the LLVM interface_map entry (inline { ptr, ptr })
typedef struct EmperorInterfaceMapEntry {
    const char* interface_id;    // unique interface identifier (string constant)
    void** method_table;         // array of method pointers ptr[]
} EmperorInterfaceMapEntry;

// corresponds to LLVM @X_metadata ({ ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr })
typedef struct EmperorClassMetadata {
    const char* name;            // type name
    int instance_size;           // total instance size (including header)
    int field_count;             // number of fields
    int* field_offsets;          // field offset table i32[]
    int* field_is_ptr;           // whether each field contains a pointer i32[] (for GC traversal)
    void** virtual_method_table; // virtual method table ptr[] (currently NULL; dispatch goes through interface_map)
    int interface_count;         // number of implemented interfaces
    EmperorInterfaceMapEntry* interface_map; // interface map table
    void (*destructor)(void*);   // GC finalizer: reference types implementing IMemoryDispose point at dispose_mem (the signatures happen to match), invoked before sweep reclamation
} EmperorClassMetadata;
```

**Memory layout guarantee**: the field order, sizes, and alignments of the C struct and the LLVM type must match exactly. All pointers use `ptr` (8 bytes on 64-bit); integers use `i32` (4 bytes).

---

## 3. EmperorPenguin IR → LLVM IR Instruction Conversion

### 3.1 CONST

**IR**: `%dst:ty = CONST value`

| IR type | LLVM instruction |
|---------|----------|
| Integers (`i8` ~ `i64`) | `%dst = add iXX 0, <value>`, or the constant used directly |
| Floats (`f32` / `f64`) | LLVM floating-point constant used directly |
| `bool` | `%dst = add i8 0, 1` (true) or `add i8 0, 0` (false) |
| `string` | `%dst = store ptr @str_N, ptr %dst_alloca` (address of the global constant `@str_N`) |
| Reference-type null | `%dst = inttoptr i64 0, ptr` |

### 3.2 ARG

**IR**: `%result:ty = ARG param_name index`

Function parameters are used directly as `%param_name` in LLVM; the ARG instruction disappears during lowering.

### 3.3 ASSIGN

**IR**: `%dest:ty = ASSIGN %src`

| Type classification | LLVM instruction |
|---------|----------|
| Mutable register (any type) | `store <ty> %src, ptr %dest_alloca` (entry-block alloca slot; for value-type class/enum the alloca holds the whole struct, and when the source is a pointer a coerce performs a ptr→struct load) |
| Immutable register | a register-value mapping (reg_map) is established; later uses resolve directly to the source value with no extra instructions |
| Reference-type class (IRef) | the pointer value is passed along directly (shallow copy) |

### 3.4 CAST

**IR**: `%result:to_ty = CAST %operand from_ty->to_ty`

| Conversion | LLVM instruction |
|---------|----------|
| Integer widening (i8→i32) | `%result = zext i8 %operand, i32` (unsigned) or `sext` (signed) |
| Integer truncation (i64→i32) | `%result = trunc i64 %operand, i32` |
| Integer→float | `%result = sitofp i32 %operand, float` (signed) or `uitofp` (unsigned) |
| Float→integer | `%result = fptosi float %operand, i32` or `fptoui` |
| Float precision (f32↔f64) | `%result = fpext float %operand, double` or `fptrunc` |
| class→interface | IRef: zero cost (same pointer); ICopy: BOX boxing |
| interface→class | ISINSTANCE check then zero cost (same pointer); ICopy: UNBOX alias view |

### 3.5 BINOP

**IR**: `%result:ty = BINOP op %left, %right`

| IR op | Integer LLVM instruction | Float LLVM instruction |
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

| IR op | LLVM instruction |
|-------|----------|
| `neg` | `%result = sub iXX 0, %operand` (integer) or `fneg float %operand` (float) |
| `not` | `%result = xor i8 %operand, 1` |
| `bitnot` | `%result = xor iXX %operand, -1` |

### 3.7 RDMBR (read member)

**IR**: `%result:ty = RDMBR %obj, .field_name`

**Reference-type class** — `obj` is a `ptr`; field offsets include the metadata header:

```llvm
; %result:i32 = RDMBR %obj, .x
%field_ptr = getelementptr i8, ptr %obj, i32 <field_offset>
%result = load i32, ptr %field_ptr
```

Field offset = `8 + field_position_offset`.

**Value-type class** — `obj` is a pointer to the struct storage; fields are accessed with struct-indexed GEPs (field 0 is the metadata; instance fields start at index 1 / offset 8):

```llvm
; %result:i32 = RDMBR %obj, .x
%field_ptr = getelementptr %class.Point, ptr %obj_ptr, i32 0, i32 <field_index>
%result = load i32, ptr %field_ptr
```

### 3.8 WRMBR (write member)

**IR**: `WRMBR %obj, .field_name, %value`

**Reference-type class**:
```llvm
%field_ptr = getelementptr i8, ptr %obj, i32 <field_offset>
store i32 %value, ptr %field_ptr
```

**Value-type class**:
```llvm
%field_ptr = getelementptr %class.Point, ptr %obj_ptr, i32 0, i32 <field_index>
store i32 %value, ptr %field_ptr
```

(Struct-indexed GEP; field 0 is the metadata, instance fields start at index 1.)

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
; value type
ret i32 %value
; reference type
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

**Void calls** (CALL_VOID):

**IR**: `CALL @func_name(%arg1, ...)`

```llvm
call void @func_name(i32 %arg1)
```

### 3.14 CALL_VIRT

**IR**: `%result:ty = CALL_VIRT %obj, interface="InterfaceName", slot=N(%arg1, ...)`

Dynamic virtual dispatch operates on objects carrying metadata: reference-type classes (impl IRef) are passed in directly; value-type classes (impl ICopy) are first BOX-boxed (copied to the heap, including metadata), and the virtual call is made on the box.

#### 3.14.1 Dispatch Path Selection

The compiler chooses the dispatch mode based on the receiver's type:

| Receiver type | Dispatch mode | IR instruction | LLVM generation |
|-----------|---------|---------|----------|
| Concrete class (e.g. `Point`) | Deterministic dispatch | CALL | `call ReturnType @func_name(args...)` |
| Interface type (e.g. `IShow`) | Dynamic dispatch | CALL_VIRT | runtime table lookup + indirect call |

**Note**: interface method calls on concrete types (e.g. a `Point` object calling a method from `impl IShow`) use deterministic dispatch and do not go through CALL_VIRT.

#### 3.14.2 Dynamic Dispatch Lowering (CALL_VIRT)

Dynamic virtual calls dispatch through the C runtime helper `_emperor_vtable_lookup`:

```llvm
; IR: %result:ty = CALL_VIRT %obj, interface="IShow", slot=0(%obj, %arg1, ...)

; 1. look up the function pointer through the C runtime
%func_ptr = call ptr @_emperor_vtable_lookup(
    ptr %obj,                       ; object pointer
    ptr @.IShow_interface_id,       ; interface ID string constant
    i32 0                           ; vtable slot index
)

; 2. indirect call through the function pointer
%result = call ReturnType %func_ptr(ptr %obj, ArgType %arg1)
```

The `_emperor_vtable_lookup` lookup flow (`std/c/penguinlang_interop.c`):
1. Read the metadata from the object: `metadata = *(void**)obj`
2. Walk `metadata->interface_map`, matching `interface_id`
3. Get `method_table` from the matching entry
4. Return `method_table[slot]`

Aggregate return values (enum / large struct) go through sret: first `alloca` an sret buffer in the entry block, make the indirect call with `ptr sret(<ty>)` as the first parameter, then map the result onto the sret buffer.

#### 3.14.3 Deterministic Dispatch Lowering (Direct CALL)

```llvm
; concrete type calling an interface method — direct dispatch
; p.show() where p: Point
call ReturnType @"<global>.Point.show"(ptr %p)
```

When the concrete type is known at compile time, the target function is resolved in the class scope and a plain CALL is generated with no runtime overhead. The CALL_VIRT path always goes through `_emperor_vtable_lookup`; the compile-time-known metadata global constant merely makes the runtime lookup inputs constants.

### 3.15 NEW

**IR**: `%result = NEW TypeName(%arg1, ...)`

All NEWs go through the unified `allocate_class` path (zero initialization + metadata stamping + registration as a `ptr`), followed by a call to the constructor `@"<global>.TypeName.new"`:

#### 3.15.1 Reference-type class (impl IRef)

GC heap allocation + memset zeroing + metadata stamping + constructor call:

```llvm
; %result:ptr = NEW TypeName(%arg1, ...)

; 1. allocate and zero the object
%result = call ptr @_emperor_alloc_impl(i32 <sizeof>)
call void @llvm.memset.p0.i64(ptr %result, i8 0, i64 <sizeof>, i1 false)

; 2. stamp the metadata (offset 0)
store ptr @TypeName_metadata, ptr %result

; 3. call the constructor
call void @"<global>.TypeName.new"(ptr %result, i32 %arg1)
```

#### 3.15.2 Value-type class (impl ICopy)

entry-block alloca + zeroinitializer + metadata stamping + constructor call:

```llvm
; %result = NEW TypeName(%arg1, ...)

; 1. allocate and zero stack space (entry block; zero-initializes the whole struct)
%result = alloca %class.TypeName, align 8
store %class.TypeName zeroinitializer, ptr %result

; 2. stamp the metadata (field 0)
store ptr @TypeName_metadata, ptr %result

; 3. call the constructor (this is a pointer to the storage)
call void @"<global>.TypeName.new"(ptr %result, i32 %arg1)
```

The register holds the result as a `ptr`; by-value uses (argument passing / assignment) copy the entire struct via `coerce_operand`. Aggregate arguments over 16 bytes are passed as `ptr byval(<ty>)`.

### 3.16 NEW_ENUM

**IR**: `%result = NEW_ENUM EnumType.variant_name(%payload)`

Constructs a metadata-carrying tagged union (a value type on the stack):

```llvm
; %result = NEW_ENUM Option__i32.some(42)

; 1. allocate stack space
%enum_ptr = alloca %enum.Option__i32

; 2. set the metadata (field 0)
%md_ptr = getelementptr %enum.Option__i32, ptr %enum_ptr, i32 0, i32 0
store ptr @Option__i32_metadata, ptr %md_ptr

; 3. set _variant (field 1; the tag field is i64)
%variant_ptr = getelementptr %enum.Option__i32, ptr %enum_ptr, i32 0, i32 1
store i32 0, ptr %variant_ptr              ; some = 0 (low 4 bytes written)

; 4. set _payload (field 2; typed store into the byte array)
%payload_ptr = getelementptr %enum.Option__i32, ptr %enum_ptr, i32 0, i32 2
store i32 42, ptr %payload_ptr

; 5. the result register holds %enum_ptr as a ptr
%result = ...
```

Payload-less variant:
```llvm
%enum_ptr = alloca %enum.Option__i32
%md_ptr = getelementptr %enum.Option__i32, ptr %enum_ptr, i32 0, i32 0
store ptr @Option__i32_metadata, ptr %md_ptr
%variant_ptr = getelementptr %enum.Option__i32, ptr %enum_ptr, i32 0, i32 1
store i32 1, ptr %variant_ptr              ; none = 1
%result = ...                              ; ptr holds %enum_ptr
```

### 3.17 ISENUM

**IR**: `%result:bool = ISENUM %enum_value, %variant_idx`

Checks the tagged union's _variant field (field 1, skipping the metadata ptr field 0):

```llvm
; enum_value materialized into a pointer to the struct storage
%tmp = materialize_ptr %enum_value

; 1. read _variant (field 1; the i32 load takes the tag's low 4 bytes)
%variant_ptr = getelementptr %enum.Option__i32, ptr %tmp, i32 0, i32 1
%tag = load i32, ptr %variant_ptr

; 2. compare against the target variant_idx (trunc first when idx is not i32)
%cmp = icmp eq i32 %tag, %variant_idx
%result = zext i1 %cmp, i8                 ; i8 bool
```

### 3.18 RDENUM

**IR**: `%result:ty = RDENUM %enum_value, .variant_name`

Reads the tagged union's payload (field 2, skipping metadata + variant) with a typed load of that variant's payload type:

```llvm
%tmp = materialize_ptr %enum_value
%payload_ptr = getelementptr %enum.Option__i32, ptr %tmp, i32 0, i32 2
%result = load i32, ptr %payload_ptr
```

When the extraction result is the receiver of a write chain (`e.a.x = 9`, `e.a.increment()`), the payload pointer targets the slot inside the enum's **own storage** (not a materialized copy) so the write-back takes effect.

### 3.19 LABEL

**IR**: `label_name:`

Maps directly to an LLVM basic block label:

```llvm
label_name:
```

### 3.20 GLOBAL_LOAD

**IR**: `%result:ty = GLOBAL_LOAD @global_name`

Reference-type / string / primitive-type global variables load directly:

```llvm
%result = load <ty>, ptr @global_name
```

Value-type class global variables store the struct **inline** in the global slot:
- Write-chain reads (`g.x = 9` lowers to GLOBAL_LOAD + WRMBR) → the result register maps to the `@global_name` address itself (lvalue addressing, no copy)
- Other reads → `load <struct>, ptr @global_name` (by-value copy semantics)

### 3.21 GLOBAL_STORE

**IR**: `GLOBAL_STORE @global_name, %value`

```llvm
store <ty> %value, ptr @global_name
```

Value-type class global variables store the entire struct: the value side first goes through `coerce_operand` to converge ptr→struct (loading the complete struct from the value's storage), then stores the whole thing.

### 3.22 ADDRESS_OF

**IR**: `%result:u64 = ADDRESS_OF %operand`

Takes the raw address of the operand's storage; the result is u64 (i64):

```llvm
; mutable register → reuse its alloca directly; otherwise materialize into a temporary alloca
%base = alloca <ty>, align 8
store <ty> %operand, ptr %base

%result = ptrtoint ptr %base to i64
```

### 3.23 LOAD_PTR

**IR**: `%result:ty = LOAD_PTR %addr`

Loads from a raw u64 address according to `load_type`:

```llvm
%ptr = inttoptr i64 %addr to ptr
%result = load <ty>, ptr %ptr
```

Value-type class slots (`ref<...>` with ICopy) store the struct inline, while the value model represents values as pointers — the load is a **copy**: fresh alloca + `llvm.memcpy`:

```llvm
%ptr = inttoptr i64 %addr to ptr
%result = alloca %class.X, align 8
call void @llvm.memcpy.p0.p0.i32(ptr %result, ptr %ptr, i32 <sizeof>, i1 false)
```

### 3.24 STORE_PTR

**IR**: `STORE_PTR %addr, %value:store_type`

Stores to a raw u64 address according to `store_type`:

```llvm
%ptr = inttoptr i64 %addr to ptr
store <ty> %value, ptr %ptr
```

A value-type class store must copy the **entire struct** into the slot (the value resolves to a pointer to its storage; storing the pointer directly would leak a stack address into the buffer): the type is replaced by the class struct type, coerced, and then stored.

### 3.25 CALL_INDIRECT

**IR**: `%result:ret_ty = CALL_INDIRECT %callee(%arg1, ...)`

Calls through a function-pointer value (fun-typed fields / local variables). The callee is a live `ptr` register (a fat function pointer); with no call-site signature metadata available to converge arguments, the actuals are emitted as-is with their own IR types:

```llvm
; non-void return
%result = call <ret_ty> %callee(<ty1> %arg1)

; void return
call void %callee(<ty1> %arg1)
```

Aggregate return values (enum / large struct) go through sret: `alloca` an sret buffer in the entry block, make the indirect call with `ptr sret(<ty>)` as the first parameter, then `load` the result:

```llvm
%sret_buf = alloca %enum.X
call void %callee(ptr sret(%enum.X) %sret_buf, <ty1> %arg1)
%result = load %enum.X, ptr %sret_buf
```

---

## 4. Function Calling Conventions

### 4.1 Ordinary Functions

Mapped directly to LLVM functions, following the target platform's C calling convention. Primitives are passed by value, reference-type classes pass pointers, and enums and value-type classes are represented as pointers but passed with by-value semantics (aggregate arguments over 16 bytes are declared `ptr byval(<ty>)`; aggregate return values use sret — the caller supplies a `ptr sret(<ty>)` buffer).

### 4.2 Instance Methods

How `this` is passed depends on the class's ICopy/IRef classification:

**Reference-type class (impl IRef)**: `this` is the object pointer.

```
EmperorPenguin IR:
function @MyClass.get_x(%this:ptr) -> i32 { ... }

LLVM IR:
define i32 @MyClass_get_x(ptr %this) {
    ...
}
```

**Value-type class (impl ICopy)**: `this` is passed as a pointer (to the caller's stack frame location), but the method reads the complete value via a `load` internally.

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

(Field 0 is the metadata; instance fields start at struct index 1.)

For `mut this` methods (which need to modify fields), a value-type class modifies the caller's stack data directly through the pointer.

### 4.3 Constructors

Constructors receive the `this` pointer and return void; metadata stamping is done uniformly by NEW's `allocate_class` (both paths write `@TypeName_metadata` at offset 0), and the constructor only initializes fields:

**Reference-type class (impl IRef)**:
```
LLVM IR:
define void @"<global>.MyClass.new"(ptr %this, i32 %x) {
    ; initialize fields (metadata already written by allocate_class)
    ...
    ret void
}
```

**Value-type class (impl ICopy)**:
```
LLVM IR:
define void @"<global>.Point.new"(ptr %this, i32 %x, i32 %y) {
    ; initialize fields directly (metadata already written by allocate_class)
    %x_ptr = getelementptr %class.Point, ptr %this, i32 0, i32 1
    store i32 %x, ptr %x_ptr
    %y_ptr = getelementptr %class.Point, ptr %this, i32 0, i32 2
    store i32 %y, ptr %y_ptr
    ret void
}
```

### 4.4 External Function Calls (extern)

Calls to extern functions go through ordinary CALL instructions (the IR has no dedicated extern-call instruction); they are `declare`d in the LLVM Module with no definition generated:

```llvm
declare i32 @puts(ptr)

define void @NS_initial_routine_0() {
    %data = getelementptr i8, ptr @str_N, i64 16   ; string → data pointer (+16 skips the header)
    call i32 @puts(ptr %data)
    ret void
}
```

**string marshaling** for bare libc externs (`string` is a header-prefixed block; libc only understands `char*`):
- string **arguments**: `getelementptr i8, ptr %s, i64 16` (the data pointer)
- string **return values**: `%adopted = call ptr @_emperor_string_adopt_cstring(ptr %raw)` (copies the foreign C string into a GC string)

Symbol name mapping rules (`llvm_func_name`):
- externs in the `__builtin` / `_utils` runtime namespaces → `@_emperor_<tail>` (e.g. `__builtin.println` → `@_emperor_println`)
- externs in any other namespace (std or user code) → `@<full dotted name joined with _>` (e.g. `std.io.file_open` → `@std_io_file_open`)
- bare top-level externs → the literal symbol name (e.g. `extern fun abs` → `@abs`, straight to libc)
- dyn-lib export declarations (`is_lib_export_decl`) keep ordinary mangled names, consistent with their definitions in the `.penguin-lib`

---

## 5. Control Flow Patterns

### 5.1 if Expressions

```
EmperorPenguin IR:
  %t0 = BINOP eq %x, 0
  BR_COND %t0, then_0, else_0
  then_0:
  %t1 = CONST 1
  %t3 = ASSIGN %t1          ; the result register is ASSIGNed once in each branch
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
  store i32 %t1, ptr %t3_alloca      ; multi-assigned registers are backed by an alloca
  br label %merge_0
else_0:
  %t2 = add i32 0, 2
  store i32 %t2, ptr %t3_alloca
  br label %merge_0
merge_0:
  %t3 = load i32, ptr %t3_alloca     ; read at the merge point
```

The IR has no phi instructions: an if expression allocates one result temp register and each branch ASSIGNs it; `scan_mutable_regs` detects registers with multiple definitions and backs them with an entry-block alloca — stores inside the branches, a load at the merge point.

### 5.2 while Loops

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

- `break` → `br label %loop_exit_N` (jump to the current loop's exit label)
- `continue` → `br label %loop_header_N` (jump to the current loop's header label)

---

## 6. `is` Type Checks

### 6.1 enum is Checks (ISENUM)

PenguinLang's `if (x is Option<i32>.some)` compiles to the ISENUM instruction:

```llvm
; ISENUM: the struct value is materialized to a pointer, then _variant is extracted (field 1)
%tmp = materialize_ptr %x
%variant_ptr = getelementptr %enum.Option__i32, ptr %tmp, i32 0, i32 1
%tag_val = load i32, ptr %variant_ptr
%t0 = icmp eq i32 %tag_val, 0           ; some = 0
%result = zext i1 %t0, i8
; then BR_COND %result, then_0, else_0
```

### 6.2 class/interface is Checks (ISINSTANCE)

Operates on objects carrying metadata: reference-type classes (impl IRef) are checked directly; value-type classes (ICopy) can be checked too once BOX-boxed (enum/value-class struct operands are materialized to pointers before being passed to the helpers).

#### 6.2.1 object is interface

Checks whether an object implements a given interface:

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

The `_emperor_isinstance` lookup flow:
1. Read the metadata from the object: `metadata = *(void**)obj`
2. Walk `metadata->interface_map`
3. Compare `entry->interface_id` with the target `interface_id` (strcmp)
4. Return 1 if found, otherwise 0

#### 6.2.2 interface is class (type narrowing)

Checks whether an interface reference points to an instance of a specific class:

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

`_emperor_check_class` takes the **object pointer** (internally fetching `metadata = *(void**)obj` itself) and works by comparing `metadata->name` with the target class name. If the operand is an enum/value-class struct held in a register, it is first materialized into a temporary alloca and the pointer is taken.

#### 6.2.3 interface is interface

Checks whether an interface reference also implements another interface:

```penguin
let obj: IBase = new Impl();   // Impl impl IBase, IDerived
println(cast<string>(obj is IDerived));   // true
```

```llvm
; ISINSTANCE: obj is IDerived (same lookup logic as 6.2.1)
%result = call i32 @_emperor_isinstance(ptr %obj, ptr @.IDerived_type_id)
```

#### 6.2.4 Compile-Time Optimization

When the type information is fully known at compile time, a constant result is emitted directly:
- concrete class type `is` an implemented interface → `CONST true`
- concrete class type `is` an unimplemented interface → `CONST false`

When folding is impossible, the runtime helpers are used uniformly (object pointer + `@.<Type>_type_id` string constant):

```llvm
; ISINSTANCE %obj, "IBar"
%result = call i32 @_emperor_isinstance(ptr %obj, ptr @.IBar_type_id)
%bool = icmp ne i32 %result, 0
%result_i8 = zext i1 %bool, i8
```

---

## 7. Runtime Functions

During lowering, the following runtime support functions are `declare`d on demand (`emit_extern_declarations_runtime_to`; C implementations in `std/c/core_builtin.c`, `std/c/penguinlang_interop.c`, `std/c/gc.c`, `std/c/scheduler.c`):

| Function | Signature | Purpose |
|------|------|------|
| `_emperor_alloc_impl` | `ptr (i32 size)` | GC heap memory allocation (IRef classes, boxing, strings) |
| `_emperor_vtable_lookup` | `ptr (ptr obj, ptr interface_id, i32 slot)` | dynamic dispatch: looks up a function pointer in the object metadata's interface_map |
| `_emperor_isinstance` | `i32 (ptr obj, ptr interface_id)` | runtime interface type check: whether an object implements the given interface |
| `_emperor_check_class` | `i32 (ptr obj, ptr class_id)` | runtime class type check: whether an object is an instance of the given class |
| `_emperor_ICopy_copy` | `ptr (ptr value)` | runtime copy helper for the ICopy interface |
| `_emperor_int_to_string` | `ptr (i32 value)` | i32 to string |
| `_emperor_i64_to_string` | `ptr (i64 value)` | i64 to string |
| `_emperor_double_to_string` | `ptr (double value)` | f64 to string |
| `_emperor_bool_to_string` | `ptr (i8 value)` | bool to string |
| `_emperor_string_concat` | `ptr (ptr a, ptr b)` | string concatenation |
| `_emperor_string_equal` | `i32 (ptr a, ptr b)` | string equality comparison |
| `_emperor_gc_init` | `void (ptr stack_top)` | GC initialization (uses the stack top as a scanning root) |
| `_emperor_gc_add_root` | `void (ptr global)` | registers a reference-type global variable as a GC root |
| `_emperor_gc_scan_add` | `void (i64 addr, i64 size)` | registers a value-type global's whole struct as a conservative scan region |
| `_emperor_args_init` | `void (i32 argc, ptr argv)` | command-line argument initialization |
| `_emperor_boost_stack` | `void ()` | raises the main thread's stack limit (deep-recursion protection) |
| `_emperor_co_spawn_fn0` | `void (ptr fn)` | coroutine scheduling: starts the initial routine as a coroutine |
| `_emperor_sched_run` | `i32 ()` | coroutine scheduler main loop |
| `_setjmp` | `i32 (ptr jmp_buf, ptr frame)` | try/catch landing pad (`returns_twice`; the two-parameter form is compatible with glibc/mingw) |
| `llvm.memcpy` / `llvm.memset` | LLVM intrinsic | struct copy / object zeroing |

**Note**: ordinary allocation of value-type classes (ICopy) does not go through `_emperor_alloc_impl` (entry-block alloca); they enter the GC heap only when boxed into interface references (BOX).

---

## 8. LLVM Module Structure

### 8.1 One LLVM Module per Compilation Unit

```llvm
; type declarations
; metadata is emitted as an inline literal struct type (9 fields):
;   { ptr name, i32 size, i32 field_count, ptr field_offsets, ptr field_is_ptr,
;     ptr vtable, i32 interface_count, ptr interface_map, ptr destructor }
%FunctionValue = type { ptr, ptr }

; reference-type class (IRef) — heap allocated, with a metadata header
%class.Foo = type { ptr, i32, f64, ptr }  ; { metadata, x, y, z }

; value-type class (ICopy) — stack allocated (same-shape layout; field 0 is likewise the metadata)
%class.Point = type { ptr, i32, i32 }     ; { metadata, x, y }

; global constants
@str_0 = private constant { ptr, i64, [5 x i8] } { ... }
@Foo_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr } { ... }

; runtime
declare ptr @_emperor_alloc_impl(i32)

; function definitions
define i32 @"<global>.ClassName.method"(ptr %this, i32 %p) { ... }

; main entry
define i32 @main(i32 %argc, ptr %argv) {
    call void @_emperor_boost_stack()
    call void @_emperor_args_init(i32 %argc, ptr %argv)
    %_gc_sp = call ptr @llvm.frameaddress(i32 0)
    call void @_emperor_gc_init(ptr %_gc_sp)
    ; register global variables as GC roots (string/ref → add_root; value-type structs → scan_add conservative scan region)
    call void @_emperor_gc_add_root(ptr @some_ref_global)
    ; non-trivial global variable initialization (init function)
    call void @"<global>.g_init_0"()
    ; entry function (initial block); run through the coroutine scheduler when it contains suspension points
    call void @"_ns_main.initial_0"()
    ret i32 0
}
```

---

## 9. Complete Lowering Example

### PenguinLang Source

```penguin
class Counter {
    count: mut i32 = 0;
    impl IRef {}              // reference type (implements an interface; needs virtual dispatch)
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

**ICopy/IRef classification**: Counter implements the IRef interface (because virtual calls through the ICounter interface are needed), making it a reference-type class.

### LLVM Type Definitions

```llvm
; metadata is emitted as an inline literal struct type (9 fields)

; Counter object layout:
; offset 0:  ptr metadata → @Counter_metadata
; offset 8:  i32 count
; total: 16 bytes (including 4 bytes of padding)

@.Counter_name = private unnamed_addr constant [8 x i8] c"Counter\00"
@.ICounter_interface_id = private unnamed_addr constant [9 x i8] c"ICounter\00"

@Counter_field_offsets = private constant [1 x i32] [i32 8]
@Counter_field_is_ptr = private constant [1 x i32] [i32 0]  ; count is not a pointer
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

### LLVM Function Definitions

```llvm
; emission of NEW Counter() (unified allocate_class path):
;   %obj = call ptr @_emperor_alloc_impl(i32 16)
;   call void @llvm.memset.p0.i64(ptr %obj, i8 0, i64 16, i1 false)
;   store ptr @Counter_metadata, ptr %obj
;   call void @"<global>.Counter.new"(ptr %obj)

define void @"<global>.Counter.new"(ptr %this) {
entry:
    ; initialize count = 0 (metadata already written by NEW's allocate_class)
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

## 10. Lowering Pipeline

```
EmperorPenguin Bound Tree + IRModule
       ↓
Pre-step: ICopy/IRef classification (semantic-layer ClassifyValueTypesPass)
  - checks each class's explicit ICopy/IRef implementation (via vtable lookup)
  - classes without an explicit designation are classified automatically from their field types
  - cyclic dependency detection (encountering an already-visited class during recursive classification → reference type)
  - implementing both ICopy and IRef → compile error
  - the result is written to BoundClassDefinition.is_value_class
       ↓
LLVMEmitter.lower(module, unit):
Pass 1: collect string literals
  - scans the CONST instructions of all functions and the global variable initializers
  - aggregates, deduplicates, and numbers them @str_N
       ↓
Pass 2: build layout tables (build_layout_tables)
  - walks all class/enum/interface definitions
  - layout unified as { ptr metadata, fields... } (enum: { ptr, i64 tag, [N x i8] payload }):
    - IRef class: heap allocated, byte-offset GEPs
    - ICopy class: stack allocated, struct-indexed GEPs (field 0 is the metadata)
  - aggregates each type's interface vtable slots
       ↓
Pass 3: emit functions (emit_functions + emit_main)
  - walks each IRFunction, converting IRInstructions to LLVM instructions one by one
  - picks the correct lowering path by ICopy/IRef classification:
    - NEW: heap allocation (alloc_impl + memset) vs entry-block alloca
    - RDMBR/WRMBR: byte-offset GEP vs struct-indexed GEP
    - ASSIGN: pointer copy vs struct copy
    - CALL_VIRT: interface_map lookup via _emperor_vtable_lookup
  - multi-definition registers are backed by entry-block allocas via scan_mutable_regs (no phi nodes)
  - function bodies are emitted first, to determine the required set of runtime declares
       ↓
Final assembly
  - type definitions (%class./%enum. structs, ordered by layout completion = type-nesting topological order)
  - metadata global constants for all classes/enums, interface ID constants, vtables/interface_maps
  - string literal global constants (@str_N)
  - global variables (@<name>; value-type structs stored inline)
  - extern declares (user externs + on-demand runtime declares)
  - function bodies + the @main entry
       ↓
LLVM Module text (.ll)
```

---

## 11. Implementation Status

### Implemented

| Feature | IR instruction | LLVM generation | Notes |
|------|---------|----------|------|
| Basic type operations | BINOP | icmp/add/sub/mul/div etc. | i32/i64/f32/f64/bool |
| Variable assignment | ASSIGN | alloca + store + load | mutable registers with multiple definitions go through an alloca |
| Control flow | BR/BR_COND | br/br i1 | if/else/while |
| Short-circuit evaluation | (BR_COND sequence) | br i1 + labels | `&&`/`\|\|` |
| Function calls | CALL/CALL_VOID | call | direct function calls (including externs) |
| Indirect calls | CALL_INDIRECT | call %callee | fun-typed function-pointer values; aggregate returns via sret |
| Dynamic dispatch | CALL_VIRT | `_emperor_vtable_lookup` + indirect call | interface-typed method calls |
| Interface type checks | ISINSTANCE | `_emperor_isinstance` | `obj is InterfaceType` |
| Class type checks | ISINSTANCE | `_emperor_check_class` | `obj is ClassName` |
| Boxing/unboxing | BOX/UNBOX | `_emperor_alloc_impl` + memcpy / alias view | value type ↔ interface reference |
| Class instantiation | NEW | alloc_impl + memset + metadata (IRef) / entry alloca (ICopy) | unified allocate_class path + constructor call |
| Field read/write | RDMBR/WRMBR | getelementptr + load/store | IRef byte offsets / ICopy struct indexes |
| Enum construction | NEW_ENUM | alloca + store variant + payload | tagged union |
| Enum matching | ISENUM | load variant + icmp eq | variant tag comparison |
| Enum field read | RDENUM | getelementptr + load payload | payload extraction (write chains target the enum's own storage) |
| Global variables | GLOBAL_LOAD/GLOBAL_STORE | load/store @global | value-type structs stored inline |
| Address intrinsics | ADDRESS_OF/LOAD_PTR/STORE_PTR | ptrtoint / inttoptr + load/store | `#__address_of`/`#__load`/`#__store` |
| Type conversions | CAST | zext/sext/trunc/sitofp etc. | numeric type conversions |
| try/catch | (setjmp sequence) | `_setjmp` landing pad + jmp_buf table | `try_site_counter` index |
| Full ClassMetadata | — | `@<Type>_metadata` (9 fields) | all classes and enums; includes interface_map + field_offsets + field_is_ptr + destructor |
| Interface default method dispatch | CALL | vtable slots point at the specialized default implementations | empty impl inherits the default implementations |
| Deterministic dispatch | CALL | call @func_name | concrete-type method calls |

All 29 `IRInstruction` variants have a corresponding `emit_*` lowering in the `emit_instruction` dispatcher.

### C Runtime Files

| File | Contents |
|------|------|
| `std/c/core_builtin.c` | built-in function implementations (print, string operations, GC allocation, type conversions, file I/O) |
| `std/c/gc.c` | conservative mark-sweep GC (stack scanning, root registration, finalizer invocation) |
| `std/c/penguinlang_interop.c` | interop helpers: `_emperor_vtable_lookup`, `_emperor_isinstance`, `_emperor_check_class` |
| `std/c/scheduler.c` | coroutine scheduler (`_emperor_sched_run`, `_emperor_co_spawn_fn0`) |
| `std/c/meta_stubs.c` / `std/c/penguin_jit.cpp` | metaprogramming JIT host |
| `std/include/*.h` | C-side type and interop declarations (`emperor_types.h`, `emperor_interop.h`, `emperor_gc.h`, `emperor_builtin.h`) |
