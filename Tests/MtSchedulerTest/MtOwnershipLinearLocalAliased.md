# MtOwnershipLinearLocalAliased
## Description
The escape analysis's negative twin: a local whose value is ALIASED before the wrap (`let m = l;`) is NOT linearly held — the second binding is a non-receiver use of l. Construction must fail to compile with the freshness diagnostic.

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
    let mut l = new Builder();
    l.add(1);
    let m = l;
    let o : mut __builtin.Ownership<Builder> = new __builtin.Ownership<Builder>(l);
    let got : Builder = o.move();
    println("size " + cast<string>(got.size()));
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
