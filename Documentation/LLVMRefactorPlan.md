# Refactoring Plan: LLVM Emit + Type System Overhaul

Status: **Accepted design (post-grilling)**. Implementation in progress.
Scope: Both BabyPenguin (C#) and EmperorPenguin (PenguinLang).
Motivation: EmperorPenguin's native value-type codegen is broadly buggy (4 distinct crashes diagnosed during bootstrap: enum-param ABI, valgrind strlen, i32-tag padding residual, zeroed-BoundSymbol). Each piecemeal fix costs a ~4h bootstrap and exposes the next. This refactor addresses the shared root cause.

---

## 1. Design Principles (confirmed in grilling)

1. **Unified layout.** Every non-primitive type (class, enum) has LLVM layout
   `{ ptr metaptr @0, [i32 tag @8,] fields @8|16+ }`.
   **Only primitives** (bool, char, i8–i64, u8–u64, f32, f64) lack a metaptr.
   The existing comments claiming "value types have no metaptr" are WRONG and will be removed.

2. **enum = special class.** An enum is a class plus an `i32 tag` at offset 8.
   Variant payloads share offset 16+ (union layout, sized to the largest variant).
   PenguinLang enum variants carry **at most one** payload field. Wrong-variant
   field access is a **runtime error** (no compile-time exhaustiveness check).

3. **No implicit boxing.**
   - A non-`IRef` interface has **unknown size at the language level** → it cannot
     be a class/enum field, and cannot be a function return type.
   - An interface **variable** (local/parameter) is, in EmperorPenguin's
     implementation, just a `data_ptr` (not a fat pointer). This is an
     *implementation detail* and does **not** relax the language rule.
   - Explicit indirection is provided by the compiler-intrinsic `Box<T>`
     (layout `{ metaptr, T }`, always GC-managed). The compiler special-cases
     `Box<NonIRefInterface>` to allow storing a non-IRef interface behind the Box.
   - `ICopy` ≠ value type. The mutually-exclusive classifiers are
     `IValueType` vs `IReferenceType` (= "IRef").

4. **Centralized memory operations.** Allocation, member read/write, value copy,
   and parameter passing are each handled by a single function. The ~40 scattered
   `coerce_operand` call sites in `LLVMEmitter.penguin` are eliminated.

5. **Function-signature pre-pass.** LLVM lowering collects ALL function signatures
   (return type → sret decision, parameter LLVM types) in a first pass, before
   emitting any function body or call site. Eliminates forward-reference mismatches
   (crash mechanism D).

---

## 2. Implementation Order

Per the grilling (A14+A15), ordered so each step is independently testable:

```
Phase 1  BabyPenguin C#   — Requirement 2 (no implicit boxing)     [semantic checks]
   ↓ test: all BabyPenguin tests pass
Phase 2  EmperorPenguin    — Requirement 2 mirrored in .penguin     [bound + IR]
   ↓ test: all EmperorPenguin tests pass (VM path)
Phase 3  LLVM refactor     — Requirements 1+3 + supplements         [LLVMEmitter + IR]
   3.0 signature pre-pass
   3.1 unified layout (enum = class)
   3.2 centralized memory ops (remove coerce_operand)
   3.3 IR-layer unification (IRGenerator + IRBuilder)
   3.4 debug assertions
   ↓ test: all EmperorPenguin E2E tests pass (incl. native execution)
   ↓ final: bootstrap (`emperor_penguin -b`)
```

---

## 3. Phase 1 — BabyPenguin C#: no implicit boxing (Requirement 2)

### 3.1 Reality check (from code survey)

- `EmitBox`/`EmitUnbox` are **defined** in `IRBuilder.cs:83-87` but **never called**
  from any semantic pass. The VM (`RuntimeFrame.cs:644-648`) throws
  `NotImplementedException` for `IRBoxInst`/`IRUnboxInst`/`IRIsInstanceInst`/`IRCallVirtInst`.
- Interface conversions are generated as plain `AddCastExpression` → `CAST`
  instruction (`05_InterfaceImplementation.cs:458`, `ICodeContainer.cs` multiple).
  The VM treats an interface value as a view of the underlying reference object.
- **Conclusion:** there is no auto-boxing to remove on the C# side. Phase 1 on C#
  is purely **additive semantic validation**.

### 3.2 Changes

| File | Change |
|---|---|
| `BabyPenguin/SemanticPass/05_InterfaceImplementation.cs` | Add `ValidateInterfaceFields()`: for every class & enum, for every field, if the field type is an interface that does NOT implement `__builtin.IReferenceType`, report error `"Field '{name}' of interface type '{iface}' requires IRef. Use Box<{iface}> for explicit indirection."` |
| `BabyPenguin/SemanticPass/09_CheckReturnValue.cs` | Add interface-return check: if a function's return type is a non-IRef interface, report error `"Cannot return non-IRef interface '{iface}'. Use Box<{iface}> or implement IRef."` |
| `BabyPenguin/Type/InterfaceType.cs` (or helper) | Add `IsIRefInterface(IType)`: true if the interface implements (or is) `__builtin.IReferenceType`. |
| `EmperorPenguin/std/penguin/core_builtin.penguin` | Confirm `Box<T>` implements `IReferenceType`. |

### 3.3 Field-vs-parameter distinction

The language rule treats interface occurrences differently:

| Occurrence | Non-IRef interface allowed? |
|---|---|
| local variable | **yes** (it's a `data_ptr`) |
| function parameter | **yes** (copying a pointer) |
| class/enum field | **NO** (unknown size) |
| function return type | **NO** (return = copy/escape) |

### 3.4 Tests

- Existing BabyPenguin tests must pass (a pre-survey confirms whether any existing
  test/example/stdlib uses non-IRef interface fields — if so, fix by wrapping in `Box<T>`).
- New negative tests: non-IRef interface field → error; non-IRef interface return → error.
- New positive test: `Box<IHashable>` (or similar) as a field compiles.

---

## 4. Phase 2 — EmperorPenguin bound+IR: no implicit boxing (Requirement 2)

Mirror Phase 1's language rule into the .penguin semantic model.

| File | Change |
|---|---|
| `EmperorPenguin/src/bound/SemanticModel.penguin` | New `pass_validate_interface_usage(result)` + `validate_interface_usage_def` recursive walk + `is_unsized_interface(bt)` helper, called after pass 7. Rejects non-IRef interface class fields, enum payloads, and function returns. |
| `EmperorPenguin/std/penguin/core_builtin.penguin` | Deleted dead `IIterator`/`IIterable` (no references anywhere; their `iter() -> IIterator<T>` return would have violated the new rule). |

**Scope refinement (vs original plan):** the `emit_box`/`emit_unbox` removal in
`IRGenerator.penguin` (~lines 825, 832) is **deferred to Phase 3.2**. Those IR emits
are driven by `needs_boxing`/`needs_unboxing` flags decided during expression binding,
and removing them is coupled with the LLVM-layer `BOX`/`UNBOX` emission that Phase 3.2
deletes. The field/return checks above are the substantive enforcement of Requirement 2
(they prevent the situations that would require boxing); the codegen cleanup happens with
the LLVM refactor.

**IRef-detection note:** EmperorPenguin currently has no interface that implements
`IReferenceType` (`IValueType`/`IReferenceType` are injected via `register_builtin_interface`,
and no user interface declares `impl IReferenceType`). So `is_unsized_interface` returns
true for every interface — which is correct for the current codebase (verified: EmperorPenguin
source has zero interface-typed fields, returns, enum payloads, or container instantiations).
When an EmperorPenguin interface later opts into being a sized reference interface, extend
`is_unsized_interface` to walk the interface's implemented-interface closure (via the unit's
`interface_impls` records).

Tests: all EmperorPenguin tests pass on the VM path.

---

## 5. Phase 3 — LLVM refactor (Requirements 1+3 + supplements)

Largest phase. Committed in 5 sub-steps, each tested before the next.

### 5.0 Signature pre-pass (supplement 4)

- In `LLVMEmitter.lower()`, add a first pass over `module.functions`:
  compute `needs_sret` + per-parameter LLVM type list, populate `func_param_types`.
- **Delete** the inline `func_param_types` population inside `emit_function`
  (~`LLVMEmitter.penguin:712`).
- Effect: `find_func_param_types` always hits → eliminates forward-reference
  crashes (mechanism D). Allows deleting the `emit_args_coerced` enum-arg fallback
  band-aid.

### 5.1 Unified layout (Requirement 3)

- Merge `build_class_layout` + `build_enum_layout` → `build_type_layout`,
  returning a single `TypeLayout`:
  - `metaptr` at offset 0 (all non-primitives)
  - enum: `i32 tag` at offset 8
  - fields from offset 8 (class) or 16 (enum); enum payload = union (max variant size)
- Unify LLVM type emission: `%class.X = { ptr, fields... }`,
  `%enum.X = { ptr, i32, [pad], payload... }`.
- Unify `emit_new` / `emit_new_enum`: keep both instructions (A9) but route through
  a shared `allocate(layout)` for the alloca + zero + metaptr.

### 5.2 Centralized memory ops (Requirement 1 + IR-layer supplement)

New single-responsibility helpers in `LLVMEmitter`:

```text
allocate(layout, result_name)
    value type → entry alloca + zeroinitializer
    ref type   → gc_alloc + memset 0
    always     → store metaptr; register result as "ptr"

read_member(obj, field_layout, result_name, target_type)
    GEP → field offset; load target_type; register result type

write_member(obj, field_layout, value, value_type)
    GEP → field offset; if value is alloca'd struct, load first; store

copy_value(src, dst, llvm_type)
    full-struct load + store (no partial copy, no padding leak)

pass_param(func_name, arg_index, arg) -> coerced string
    look up pre-computed signature; materialize sret/struct correctly
```

Call-site rewrites:

| Existing | Replacement |
|---|---|
| `emit_new` / `emit_new_enum` inline alloca | `allocate()` |
| `emit_wrmbr` (GEP + coerce_operand + store) | `write_member()` |
| `emit_rdmbr` (GEP + load + enum fallback) | `read_member()` |
| `emit_args_coerced` (coerce_arg + materialize_enum_ptr) | `pass_param()` per arg |
| `emit_call` sret buffer | `allocate()` |
| `coerce_operand` (40 sites) | **deleted** |
| `materialize_enum_ptr`, `resolve_enum_struct_type` | **deleted** |

### 5.3 IR-layer unification (supplement 1)

- In `IRGenerator.penguin` / `IRBuilder.penguin`: ensure `lower_expression`
  treats NEW / WRMBR / RDMBR consistently; NEW_ENUM payload handling shares
  logic with NEW constructor-arg handling.
- Remove `emit_box` / `emit_unbox` IR generation (done in Phase 2).
- Mirror the same unification in BabyPenguin C# `IRGenerator.cs`.

### 5.4 Debug assertions (supplement 5)

- `read_member`: in debug builds, assert `obj` metaptr != NULL.
- `rdenum`: in debug builds, assert tag matches the variant being read.
- C runtime (`core_builtin.c`): NULL-pointer asserts in `_emperor_string_concat` etc.

---

## 6. Crash mechanism → fix mapping

| Mechanism | Symptom | Fixed by |
|---|---|---|
| A. value/ptr representation inconsistency | enum param by-ref vs by-val | 5.2 `pass_param` + 5.0 pre-pass |
| B. field defaults not emitted | uninitialized fields | 5.1 unified layout + ctor default emission |
| C. i32-tag padding retains stack residual | `0x555500000000` return addr | 5.2 `allocate()` zero-init |
| D. forward-ref signature mismatch | wrong param coercion | 5.0 pre-pass |
| E. value-type escape → dangling | stack alloca stored into heap | 5.2 `copy_value` on field write |

Remaining (out of refactor scope, handled separately): F. GC, G. recursive types,
H. C runtime, I. runtime errors.

---

## 7. Disposition of current band-aids

| Band-aid | Disposition | Reason |
|---|---|---|
| `emit_args_coerced` enum-arg fallback | **delete** | superseded by 5.0 + 5.2 `pass_param` |
| `debug_loc` / `debug_begin_function` drop `to_string()` | **keep** | defensive; trace must not format risky values |
| `emit_entry_alloca` zero-init | **delete** | superseded by 5.2 `allocate()` zero-init |
| `emit_unaryop` "plus" case (IRGenerator `UnaryPlus`) | **keep** | correct fix, not a band-aid |
| `find_function_return_type` extern-void fix | **keep** | correct fix |

---

## 8. Risks & mitigation

| Risk | Mitigation |
|---|---|
| Phase 3 touches ~2500 lines | Commit per sub-step (5.0 → 5.1 → 5.2 → 5.3 → 5.4), test each |
| Unified layout changes sizeof/offsets | After 5.1, validate layout via VM tests before native |
| `Box<T>` compiler-intrinsic mechanism undefined | Designed in Phase 1: treat `Box<NonIRefInterface>` as a recognized special case in the field check |
| Bootstrap surfaces new native bugs | Keep `GC_DISABLED`; valgrind + gdb-hw-watchpoint recipes in `emperor-bootstrap-enum-abi-and-segfaut.md` |
| Existing code uses non-IRef interface fields | Pre-survey BabyPenguin tests/examples/stdlib before Phase 1 commit |

---

## 9. Verification

1. After Phase 1: `dotnet test BabyPenguin.Tests` (full log tee'd).
2. After Phase 2: `dotnet test EmperorPenguin.Tests` (VM path).
3. After each Phase 3 sub-step: EmperorPenguin E2E tests (which DO execute native code).
4. Final: `./emperor_penguin -b` (≈4h on this machine) → produces a clean pass2.

Always tee full logs to a file; never re-run expensive tests redundantly.
