#!/usr/bin/env python3
"""Reference encoder for the tinyriscv mini-assembler golden test.

Mirrors Examples/tinyriscv/sw/Asm.penguin exactly (two-pass, same pseudo-op
expansion, same .string packing) so the penguin assembler's output can be
diffed word-for-word.
"""
import sys

REGS = {}
for i in range(32):
    REGS[f"x{i}"] = i
abi = ["zero","ra","sp","gp","tp","t0","t1","t2","s0","s1","a0","a1","a2","a3",
       "a4","a5","a6","a7","s2","s3","s4","s5","s6","s7","s8","s9","s10","s11",
       "t3","t4","t5","t6"]
for i, n in enumerate(abi):
    REGS[n] = i
REGS["fp"] = 8

def parse_int(s):
    s = s.strip()
    neg = s.startswith("-")
    if neg: s = s[1:]
    if s[:2] in ("0x", "0X"): v = int(s[2:], 16)
    elif s[:2] in ("0b", "0B"): v = int(s[2:], 2)
    else: v = int(s, 10)
    return -v if neg else v

def floor_shift(v, n):
    return v >> n  # python >> is arithmetic floor

def enc_i(op, rd, f3, rs1, imm):
    return ((imm & 0xFFF) << 20) | (rs1 << 15) | (f3 << 12) | (rd << 7) | op
def enc_s(op, f3, rs1, rs2, imm):
    return (((imm >> 5) & 0x7F) << 25) | (rs2 << 20) | (rs1 << 15) | (f3 << 12) | ((imm & 0x1F) << 7) | op
def enc_b(op, f3, rs1, rs2, off):
    return (((off >> 12) & 1) << 31) | (((off >> 5) & 0x3F) << 25) | (rs2 << 20) | (rs1 << 15) | (f3 << 12) | (((off >> 1) & 0xF) << 8) | (((off >> 11) & 1) << 7) | op
def enc_u(op, rd, imm20):
    return ((imm20 & 0xFFFFF) << 12) | (rd << 7) | op
def enc_j(op, rd, off):
    return (((off >> 20) & 1) << 31) | (((off >> 1) & 0x3FF) << 21) | (((off >> 11) & 1) << 20) | (((off >> 12) & 0xFF) << 12) | (rd << 7) | op

def split_memop(s):
    s = s.strip()
    p = s.find("(")
    if p < 0: return s.strip(), ""
    return s[:p].strip(), s[p+1:s.rfind(")")].strip()

def unescape(s):
    out = []
    i = 0
    while i < len(s):
        c = s[i]
        if c == "\\" and i + 1 < len(s):
            e = s[i+1]
            out.append({"n":"\n","t":"\t","r":"\r","0":"\0","\\":"\\","\"":"\"","'":"''"}.get(e, e))
            i += 2
        else:
            out.append(c)
            i += 1
    return "".join(out)

def assemble(src):
    items = []   # (kind, ...) kind 0 instr / 1 data / 2 word
    labels = {}
    off = 0
    for raw in src.split("\n"):
        line = raw.split("#")[0].strip()
        if not line: continue
        if ":" in line and line[0].isalpha() or (line[0] == "_" if line else False):
            colon = line.find(":")
            name = line[:colon]
            if all(ch.isalnum() or ch == "_" for ch in name):
                labels[name] = off
                line = line[colon+1:].strip()
                if not line: continue
        mnem, _, rest = line.partition(" ") if " " in line else (line, "", "")
        rest = rest.strip()
        if mnem == ".string":
            payload = rest.strip()
            text = payload[1:-1]
            data = unescape(text) + "\0"
            items.append((1, data))
            off += ((len(data) + 3) // 4) * 4
            continue
        if mnem == ".word":
            items.append((2, rest))
            off += 4
            continue
        ops = [o.strip() for o in rest.split(",")] if rest else []
        while len(ops) < 3: ops.append("")
        oa, ob, oc = ops
        if mnem == "li":
            imm = parse_int(ob)
            hi = floor_shift(imm + 2048, 12)
            lo = imm - (hi << 12)
            items.append((0, "lui", oa, str(hi), "", off)); off += 4
            items.append((0, "addi", oa, oa, str(lo), off)); off += 4
            continue
        if mnem == "j":
            items.append((0, "jal", "x0", oa, "", off)); off += 4
            continue
        if mnem == "jal" and oc == "" and ob != "":
            items.append((0, "jal", "ra", ob, "", off)); off += 4
            continue
        if mnem == "beqz":
            items.append((0, "beq", oa, "x0", ob, off)); off += 4
            continue
        if mnem == "bnez":
            items.append((0, "bne", oa, "x0", ob, off)); off += 4
            continue
        if mnem == "nop":
            items.append((0, "addi", "x0", "x0", "0", off)); off += 4
            continue
        items.append((0, mnem, oa, ob, oc, off))
        off += 4

    def target(op, frm):
        if op in labels: return labels[op] - frm
        return parse_int(op)

    def resolve_memoff(op):
        if op in labels: return labels[op]
        return parse_int(op)

    out = []
    for it in items:
        if it[0] == 0:
            _, m, a, b, c, addr = it
            if m == "lui":   w = enc_u(0x37, REGS[a], parse_int(b))
            elif m == "addi": w = enc_i(0x13, REGS[a], 0, REGS[b], parse_int(c))
            elif m == "andi": w = enc_i(0x13, REGS[a], 7, REGS[b], parse_int(c))
            elif m == "lbu":
                memoff, reg = split_memop(b)
                w = enc_i(0x03, REGS[a], 4, REGS[reg], resolve_memoff(memoff))
            elif m == "lw":
                memoff, reg = split_memop(b)
                w = enc_i(0x03, REGS[a], 2, REGS[reg], resolve_memoff(memoff))
            elif m == "sw":
                memoff, reg = split_memop(b)
                w = enc_s(0x23, 2, REGS[reg], REGS[a], resolve_memoff(memoff))
            elif m == "beq": w = enc_b(0x63, 0, REGS[a], REGS[b], target(c, addr))
            elif m == "bne": w = enc_b(0x63, 1, REGS[a], REGS[b], target(c, addr))
            elif m == "jal": w = enc_j(0x6F, REGS[a], target(b, addr))
            else: raise SystemExit(f"unknown mnemonic {m}")
            out.append(w)
        elif it[0] == 1:
            data = it[1]
            nb = len(data)
            for wi in range((nb + 3) // 4):
                w = 0
                for b in range(3, -1, -1):
                    idx = wi * 4 + b
                    if idx < nb:
                        w |= (ord(data[idx]) & 255) << (b * 8)
                out.append(w)
        else:
            out.append(parse_int(it[1]) & 0xFFFFFFFF)
    return "".join(f"0x{w:08X}\n" for w in out)

if __name__ == "__main__":
    src = sys.stdin.read()
    sys.stdout.write(assemble(src))
