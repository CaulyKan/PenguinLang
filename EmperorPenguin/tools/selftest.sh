#!/usr/bin/env bash
# penguin-tools golden selftest — run via `make tools-test` (builds
# build/linux/penguin-tools first). Exercises all four subcommands with
# byte-exact goldens; the demangle/mangle round-trip is locked against the
# REAL shipped instances of the compiler's own libemperorpenguin.libmeta
# (mangle(demangle(x)) == x, c++filt-style passthrough for non-mangled
# input).
set -u
TOOLS="${TOOLS:-build/linux/penguin-tools}"
LIB="${LIB:-build/linux/libemperorpenguin.penguin-lib}"
if [ ! -x "$TOOLS" ]; then
    echo "selftest: $TOOLS missing (run: make tools)" >&2
    exit 1
fi
cd "$(dirname "$0")/../.." || exit 1

FAIL=0
check() {
    local name="$1" expected="$2" actual="$3"
    if [ "$expected" = "$actual" ]; then
        echo "  [ok] $name"
    else
        echo "  [FAIL] $name"
        echo "    expected: $(printf '%q' "$expected")"
        echo "    actual:   $(printf '%q' "$actual")"
        FAIL=1
    fi
}

echo "== demangle (fixed goldens) =="
GOT=$("$TOOLS" demangle '<global>.std.Vector$2FibnjNpwi1n6HAsmj6s2OYZSMUo7x00JTzCcYY0' 2>&1)
check "libmeta instance #2 (std.Vector<string>)" "std.Vector<string>" "$GOT"

GOT=$("$TOOLS" demangle 'not_a_mangled_name' 2>&1)
check "non-mangled passthrough" "not_a_mangled_name" "$GOT"

echo "== demangle stdin mode (c++filt behavior) =="
GOT=$(printf '%s\n%s\n' 'not_a_mangled_name' '<global>.std.Vector$2FibnjNpwi1n6HAsmj6s2OYZSMUo7x00JTzCcYY0' | "$TOOLS" demangle 2>&1)
WANT=$'not_a_mangled_name\nstd.Vector<string>'
check "reads names from stdin" "$WANT" "$GOT"

echo "== mangle (fixed golden) =="
GOT=$("$TOOLS" mangle 'std.Vector<string>' 2>&1)
check "mangle(demangle form)" '<global>.std.Vector$2FibnjNpwi1n6HAsmj6s2OYZSMUo7x00JTzCcYY0' "$GOT"

echo "== mangle/demangle round-trip over real libmeta instances =="
if [ -f "$LIB" ]; then
    RT_FAIL=0
    RT_N=0
    while IFS= read -r inst; do
        [ -n "$inst" ] || continue
        RT_N=$((RT_N + 1))
        DEM=$("$TOOLS" demangle "$inst" 2>&1)
        RE=$("$TOOLS" mangle "$DEM" 2>&1)
        if [ "$RE" != "$inst" ]; then
            echo "  [FAIL] round-trip: $inst"
            echo "    demangled: $DEM"
            echo "    re-mangled: $RE"
            RT_FAIL=1
        fi
    done < <("$TOOLS" meta --json "$LIB" | python3 -c "
import json,sys
meta = json.load(sys.stdin)
for s in meta.get('instances', []):
    print(s)
")
    if [ "$RT_FAIL" = 0 ]; then
        echo "  [ok] all $RT_N libmeta instances round-trip (mangle(demangle(x)) == x)"
    else
        FAIL=1
    fi
else
    echo "  (skip: $LIBMETA missing — run: make release)"
fi

echo "== meta (summary/symbols) =="
if [ -f "$LIB" ]; then
    GOT=$("$TOOLS" meta --json "$LIB" 2>&1 | python3 -c "
import json,sys
d = json.load(sys.stdin)
print(d.get('format'), d.get('name'), d.get('version'))
")
    check "meta --json is the raw document" "emperor-libmeta libemperorpenguin 1" "$GOT"

    GOT=$("$TOOLS" meta "$LIB" 2>&1 | head -1)
    check "meta default summary header" "lib:      libemperorpenguin (emperor-libmeta v1)" "$GOT"

    GOT=$("$TOOLS" meta -s "$LIB" 2>&1 | grep -c "class\|fun\|enum\|source\|global\|iface")
    if [ "$GOT" -gt 100 ]; then
        echo "  [ok] meta -s lists symbols ($GOT entries)"
    else
        echo "  [FAIL] meta -s expected >100 entries, got $GOT"
        FAIL=1
    fi
else
    echo "  (skip: $LIBMETA missing — run: make release)"
fi

echo "== format (golden + -o + -i + mutual exclusion) =="
SMOKE=$(mktemp -d)
trap 'rm -rf "$SMOKE"' EXIT
cat > "$SMOKE/messy.penguin" <<'SRC'
class   Point{ x:i32;   y : i32;
    // keep me
}
initial{
    let p:mut Point=new Point();println(cast<string>(p.x));}
SRC
"$TOOLS" format "$SMOKE/messy.penguin" > "$SMOKE/out.txt" 2>"$SMOKE/err.txt" || echo "format exited nonzero" >&2
cat > "$SMOKE/want.txt" <<'WANT'
class Point {
    x : i32;
    y : i32;
    // keep me
}
initial {
    let p : mut Point = new Point();
    println(cast<string>(p.x));
}
WANT
check "format golden" "$(cat "$SMOKE/want.txt")" "$(cat "$SMOKE/out.txt")"

"$TOOLS" format -o "$SMOKE/o.txt" "$SMOKE/messy.penguin" || echo "-o exited nonzero" >&2
check "format -o writes the file" "$(cat "$SMOKE/want.txt")" "$(cat "$SMOKE/o.txt")"

"$TOOLS" format -i "$SMOKE/messy.penguin" || echo "-i exited nonzero" >&2
check "format -i rewrites in place" "$(cat "$SMOKE/want.txt")" "$(cat "$SMOKE/messy.penguin")"

"$TOOLS" format -o "$SMOKE/x.txt" -i "$SMOKE/messy.penguin" > "$SMOKE/o2.txt" 2>&1
RC=$?
check "-o and -i are mutually exclusive (exit 2)" "2" "$RC"

echo "== format: generics glue, #meta preserved verbatim =="
cat > "$SMOKE/meta.penguin" <<'SRC'
class   Wrap<T>{   value : mut T;
}
#fun   mkbox(v:i64)->Wrap<i64>{
    let   w:mut Wrap<i64>=new Wrap<i64>();    w.value=v;   return w; }
initial{
    let w=#mkbox(7);
    println(cast<string>(w.value));}
SRC
"$TOOLS" format "$SMOKE/meta.penguin" > "$SMOKE/mout.txt" 2>&1 || echo "format(meta) exited nonzero" >&2
cat > "$SMOKE/mwant.txt" <<'WANT'
class Wrap<T> {
    value : mut T;
}
#fun   mkbox(v:i64)->Wrap<i64>{
    let   w:mut Wrap<i64>=new Wrap<i64>();    w.value=v;   return w; }
initial {
    let w = #mkbox(7);
    println(cast<string>(w.value));
}
WANT
check "format golden (generics + #meta)" "$(cat "$SMOKE/mwant.txt")" "$(cat "$SMOKE/mout.txt")"

cat > "$SMOKE/anno.penguin" <<'SRC'
class Options {
    #arg(  "help" ,  "-v","--verbose",  false ,"0" )
    verbose:   i64=0;
    #if  (X) {
    let  y:i64=1;
}
}
SRC
"$TOOLS" format "$SMOKE/anno.penguin" > "$SMOKE/aout.txt" 2>&1 || echo "format(anno) exited nonzero" >&2
cat > "$SMOKE/awant.txt" <<'WANT'
class Options {
    #arg(  "help" ,  "-v","--verbose",  false ,"0" )
    verbose : i64 = 0;
    #if (X) {
        let y : i64 = 1;
    }
}
WANT
check "format golden (#arg preserved, #if reformatted)" "$(cat "$SMOKE/awant.txt")" "$(cat "$SMOKE/aout.txt")"

echo "== help / unknown command =="
"$TOOLS" help > "$SMOKE/h.txt" 2>&1
check "help lists commands" "0" "$?"
grep -q "^Commands:" "$SMOKE/h.txt"
check "help mentions format" "0" "$?"

"$TOOLS" bogus > "$SMOKE/u.txt" 2>&1
RC=$?
check "unknown command exits 2" "2" "$RC"

if [ "$FAIL" = 0 ]; then
    echo "selftest: ALL PASSED"
    exit 0
else
    echo "selftest: FAILURES" >&2
    exit 1
fi
