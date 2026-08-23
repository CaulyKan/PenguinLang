# WireCollapseSameValue
## Description
Wire (LatestChannel) collapse semantics per design Q4: two same-value writes while the consumer has not yet woken collapse to one delivery — the parked waiter wakes once with the final value and a follow-up try_poll finds nothing more. Every write is still a transaction, but the slot is latest-wins.

## Apply To
* BabyPenguin

## Test Code
```
let q : mut __builtin.LatestChannel<i64> = new __builtin.LatestChannel<i64>();
initial {
    let a : i64 = wait q;
    let more : __builtin.Option<i64> = q.try_poll();
    let suffix : mut string = "none";
    if (more.is_some()) { suffix = "some"; }
    println(cast<string>(a) + "," + suffix);
    exit(0);
}
initial { wait 1 tick; q.write(7); q.write(7); }
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `7,none
`
ExpectedStderr: DISCARD
