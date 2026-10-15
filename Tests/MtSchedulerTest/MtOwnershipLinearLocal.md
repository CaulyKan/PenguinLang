# MtOwnershipLinearLocal
## Description
Ownership freshness form 3 (design doc §9.2 rule 1, the escape analysis): a LINEARLY HELD local is accepted — `let l = new ...` whose initializer is fresh, whose only uses before the wrap are RECEIVER calls (l.push(..) mutates the object being transferred), wrapped at the end. List<i64> pushes then Ownership-move; the moved list is fully usable. Cf. MtOwnershipLinearLocalAliased (an aliasing use is still rejected) and MtOwnershipLinearLocalPassedOut (passing the local to a call is still rejected).


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
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

## Run 1
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `total 6
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `total 6
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `total 6
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `total 6
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `total 6
`
ExpectedStderr: DISCARD
