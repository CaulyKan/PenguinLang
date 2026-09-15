# TinyRiscvAsmGolden
## Description
Golden encoding test for the tinyriscv example's mini RV32I assembler (`Examples/tinyriscv/sw/Asm.penguin`): the hello-world firmware (lui/addi/sw/lbu/beq/lw/andi/bne/sw/j + li/j/beqz/bnez pseudo-ops + .string data, labels as load offsets and branch targets) must encode byte-exact. Expected words generated and hand-verified against the RISC-V unprivileged spec encodings by `Examples/tinyriscv/sw/reference_asm.py` (the independent Python reference encoder).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
using tinyriscv;
using esl;

initial {
    let src: string = "    lui  t0, 0x30000       # UART base 0x30000000\n" +
        "    addi t1, x0, 1\n" +
        "    sw   t1, 0(t0)         # CTRL = tx_en\n" +
        "    addi t1, x0, 0         # i = 0\n" +
        "loop:\n" +
        "    lbu  t2, str(t1)       # c = str[i]\n" +
        "    beqz t2, done\n" +
        "poll:\n" +
        "    lw   t3, 4(t0)         # STATUS\n" +
        "    andi t3, t3, 1\n" +
        "    bnez t3, poll\n" +
        "    sw   t2, 12(t0)        # TXDATA\n" +
        "    addi t1, t1, 1\n" +
        "    j    loop\n" +
        "done:\n" +
        "    li   x26, 1\n" +
        "end:\n" +
        "    j    end\n" +
        "str:\n" +
        "    .string \"hello world\\n\"\n";
    print(assemble(src));
}
```

## Compile
Args: `Examples/tinyriscv/sw/Asm.penguin EmperorPenguin/others/libpenguin-esl/Bit.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `0x300002B7
0x00100313
0x0062A023
0x00000313
0x03C34383
0x00038E63
0x0042AE03
0x001E7E13
0xFE0E1CE3
0x0072A623
0x00130313
0xFE5FF06F
0x00000D37
0x001D0D13
0x0000006F
0x6C6C6568
0x6F77206F
0x0A646C72
0x00000000
`
ExpectedStderr: DISCARD
