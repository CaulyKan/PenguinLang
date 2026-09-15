# tinyriscv × PenguinLang — a cycle-accurate ESL model

A cycle-accurate model of [tinyriscv](https://github.com/liangkangnan/tinyriscv)
(a minimal 3-stage RV32I SoC) written in PenguinLang on top of
`libpenguin-esl` (see `EmperorPenguin/others/libpenguin-esl`). The firmware
is a real RV32I program (assembled from source), the CPU executes it through
a faithful IF/ID/EX pipeline, and the UART prints `hello world` — at exactly
the cycle count the RTL would take.

## Run (native pass3, after `make bootstrap`)

```bash
build/bootstrap/pass3 Examples/tinyriscv/tinyriscv.penguins \
    --enable-coroutine -o /tmp/tinyriscv
EmperorPenguin/emperor link /tmp/tinyriscv.ll -o /tmp/tinyriscv
/tmp/tinyriscv
```

Output:

```
hello world
[tinyriscv] simulation end: 48614 cycles (972280 ns @ 50 MHz), 12 chars sent
```

48614 cycles × 20 ns = 972.28 µs at the testbench's 50 MHz — 12 characters
through a busy-polled 115200-baud UART, with every poll iteration costing
real pipeline cycles.

## Timing model

**1 tick = 1 clock cycle** (the tinyriscv testbench runs 50 MHz, so
1 tick ≙ 20 ns; `esl.Clock` advances one tick per cycle). The clock drives a
two-phase evaluate/commit protocol (see `libpenguin-esl/Clock.penguin`):
posedge wakes every module process, evaluate reads only COMMITTED register
state and writes in-sides, then all registers commit atomically — module
wakeup order is irrelevant, the non-blocking-assignment determinism of an
HDL simulator. Cycle 1 is the (lost) reset edge — the clock's first loop
iteration emits nothing, so models see one full cycle of reset values before
the first posedge work (matching the RTL testbench's reset hold).

## Wiring (ports & channels)

All cross-module communication goes through the port/connect surface, wired
in `main.penguin`'s top-level `construct {}` (elaboration time, statically
audited — `error[E_WIRING]` on an unconnected input):

- **posedge fan-out**: `connect(clk.evt, m.clk)` wires the clock's
  `Event<i64>` (payload = cycle number) into each module's `input clk : i64`
  — `wait this.clk` ≡ `@(posedge clk)`.
- **pipeline registers**: `if_id` / `id_ex` are `esl.Pipe<FetchPacket>` /
  `esl.Pipe<DecPacket>` two-phase channels — write during evaluate,
  publish at the clock's commit (exactly one cycle of delay); a cycle with
  no write publishes a BUBBLE, which IS the RTL's NOP flush (`Hold_If` /
  `Hold_Id` need no extra flush call). `Ctrl` and the RIB arbitration probe
  read the pipe's `current()` — the registered (committed) view, stable
  through the whole cycle.
- **writeback**: Execute's `output wb : Writeback` port connects into
  RegFile's `input wb_in` — a wire with same-tick delta delivery, and the
  RegFile commits it at the clock edge (the RTL's posedge regfile write).
- **combinational paths** (regfile reads, the RIB bus) stay plain function
  calls during evaluate — the SystemC convention; every arbitration input
  derives from committed state, so wakeup order stays irrelevant.

## Module map (rtl ↔ model)

| tinyriscv rtl                     | model                                        |
| --------------------------------- | -------------------------------------------- |
| `rtl/core/pc_reg.v`               | `esl.Reg<u32>` pc owned by `core/Fetch.penguin` |
| `rtl/core/if_id.v`                | `Pipe<FetchPacket>` if_id channel (`Fetch` → `Decode`, value packets in `core/Pipeline.penguin`) |
| `rtl/core/id.v`                   | `core/Decoder.penguin` + `core/Decode.penguin` (meta-generated match: `core/InsnTable.penguin`) |
| `rtl/core/id_ex.v`                | `Pipe<DecPacket>` id_ex channel (`Decode` → `Execute`) |
| `rtl/core/ex.v`                   | `core/Execute.penguin` (+ `output wb : Writeback`) |
| `rtl/core/ctrl.v`                 | `Ctrl` in `core/Pipeline.penguin` (pure functions of the committed id_ex slot) |
| `rtl/core/regs.v`                 | `core/RegFile.penguin` (`input wb_in` port; single write port, committed at the edge) |
| `rtl/core/rib.v`                  | `libpenguin-esl/Bus.penguin` (addr[31:28] decode, m0 > m1 fixed priority; generated decode: `perips/MemMap.penguin`; the m0 arbitration probe is `Execute`'s `IBusMaster`) |
| `rtl/perips/rom/ram`              | `libpenguin-esl/Mem.penguin` (ROM@0x0, RAM@0x10000000) |
| `rtl/perips/tinyriscv_uart.v`     | `perips/Uart.penguin` (CTRL/STATUS/BAUD/TXDATA; 10 bits × (BAUD+1) cycles busy window) |
| `rtl/soc/tinyriscv_soc_top.v`     | `main.penguin` (construct wiring + ROM load + end report) |
| testbench (x26=1 convention)      | `SimEnd` hook in `main.penguin`              |
| firmware (C + toolchain)          | `sw/Firmware.penguin` (assembly source) + `sw/Asm.penguin` (mini assembler) |

## Behavioral fidelity (verified against the RTL sources)

- **Taken branch/jal**: flushes if_id AND id_ex to NOP (`0x00000001`), pc ←
  target — 2-cycle penalty (ctrl.v's Hold_Id path).
- **Load/store**: the EX data access (RIB m0, fixed priority over fetch m1)
  freezes ONLY the pc; the fetch is answered with a NOP bubble — 1-cycle
  bubble (ctrl.v's Hold_Pc path + rib.v's `m1_data_o = INST_NOP`).
- **Register read**: the RTL reads in ID with a write-bypass in regs.v
  (raddr == waddr → forward wdata); the model carries INDICES in id_ex and
  reads the (committed) regfile in EX — value- and cycle-equivalent for this
  instruction set, and wakeup-order independent under the commit protocol.
- **UART**: TXDATA writes are dropped unless CTRL[0]=1 and STATUS[0]=0;
  BAUD reset value 440 (441 cycles/bit ≈ 115200 @ 50 MHz); the character is
  printed to the host console at the exact simulation time the stop bit
  completes, so firmware STATUS polling consumes real cycles.

## Simplifications (documented deltas)

- RV32I subset only: LUI/ADDI/ANDI/LBU/LW/SW/BEQ/BNE/JAL (+ pseudo-ops
  `li/j/jal/beqz/bnez/nop`, directives `.string/.word`).
- No CSR/interrupt/CLINT/JTAG/div (ctrl.v's other hold sources).
- `id_ex` carries decoded fields (not the raw instruction word); `if_pc` is
  an explicit register (the RTL derives branch targets from pc_reg).
- Timer/GPIO regions exist in the generated memory map but have no slave.

## Deep meta (compile-time)

Three meta insertion points, all compiled natively (pass3):

1. **Decoder table** — `core/InsnTable.penguin`: `#class InsnEntry` table →
   `#fun gen_decode()` generates the opcode/funct3 match from the table.
2. **Memory map** — `perips/MemMap.penguin`: `#class MemMapEntry` table →
   generated `mmio_region_index` (installed on `esl.Bus` via `IBusDecoder`).
3. **Compile-time assembly** — `sw/Firmware.penguin`: `#fun
   firmware_image()` runs the full two-pass assembler inside the meta JIT;
   the image words are spliced into the program as a string literal.

These use the compile-time (unit B) file list — `meta-sources=[...]` in
`tinyriscv.penguins` (see `docs/specifications/10_MetaProgramming.md`): the
assembler + firmware + `esl.Bit` helpers are compiled into the meta engine
so the `#fun`s can call them.

## Test coverage

- `Tests/ExampleTest/TinyRiscvAsmGolden.md` — assembler encodings, verified
  word-for-word against `sw/reference_asm.py` (independent Python encoder).
- `Tests/ExampleTest/TinyRiscvHelloWorld.md` — this model, end to end.
- `EmperorPenguin/others/libpenguin-esl/smoke/smoke.penguin` — ESL library
  self-test (commit order independence, freeze/flush, arbitration, cycle
  counting; expected output in the file header).
