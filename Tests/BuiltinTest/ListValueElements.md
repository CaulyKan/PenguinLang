# ListValueElements
## Description
List<T> with a VALUE-type element class: elements live in the list's slot
storage and at()/iteration hand out VALUE copies (ICopy semantics) — mutating
the source after push does not alias the stored element, and set() replaces
the stored element wholesale. On EmperorPenguin the contiguous-buffer
_utils.List stores bare T slots (the std.Vector design: #sizeof(T) stride +
#__load/#__store), which for value-type T means the whole struct is
copied in/out of the buffer — no per-element boxing. BabyPenguin's own
stdlib List must show the same observable value semantics.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Item {
        v: i64;
    }
    initial {
        let mut l = new _utils.List<Item>();
        let a : mut Item = new Item();
        a.v = 1;
        l.push(a);
        a.v = 100;
        let b : mut Item = new Item();
        b.v = 2;
        l.push(b);
        println("n=" + cast<string>(l.size()));
        println("e0=" + cast<string>(l.at(0).some.v));
        println("e1=" + cast<string>(l.at(1).some.v));
        let c : mut Item = new Item();
        c.v = 9;
        l.set(1, c);
        c.v = 77;
        println("s1=" + cast<string>(l.at(1).some.v));
        let p : Option<Item> = l.pop();
        println("pop=" + cast<string>(p.some.v));
        println("n2=" + cast<string>(l.size()));
        let sum: mut i64 = 0;
        for (let it in l) { sum = sum + it.v; }
        println("sum=" + cast<string>(sum));
    }
```

## Compile
Args: `${PENGUIN_ROOT}/EmperorPenguin/src/utils.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `n=2
e0=1
e1=2
s1=9
pop=9
n2=1
sum=1
`
ExpectedStderr: DISCARD
