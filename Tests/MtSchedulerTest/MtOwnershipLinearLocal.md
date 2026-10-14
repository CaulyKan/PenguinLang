# MtOwnershipLinearLocal
## Description
Ownership freshness form 3 (design doc §9.2 rule 1, the escape analysis): a LINEARLY HELD local is accepted — `let l = new ...` whose initializer is fresh, whose only uses before the wrap are RECEIVER calls (l.push(..) mutates the object being transferred), wrapped at the end. List<i64> pushes then Ownership-move; the moved list is fully usable. Cf. MtOwnershipLinearLocalAliased (an aliasing use is still rejected) and MtOwnershipLinearLocalPassedOut (passing the local to a call is still rejected).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Builder {
    impl __builtin.IReferenceType;

    total: mut i64;

    fun new(mut this) {
        this.total = 0;
    }

    fun add(mut this, v: i64) {
        this.total = this.total + v;
    }
}

initial {
    let mut b = new Builder();
    b.add(1);
    b.add(2);
    b.add(3);
    let o : mut __builtin.Ownership<Builder> = new __builtin.Ownership<Builder>(b);
    let got : Builder = o.move();
    println("total " + cast<string>(got.total));
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `total 6
`
ExpectedStderr: DISCARD
