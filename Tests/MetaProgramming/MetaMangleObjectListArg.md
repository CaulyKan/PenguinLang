# MetaMangleObjectListArg
## Description
A std GENERIC container (`_utils.List<i64>`) as an object value-template argument. The IUniqueMangleName impl for List/Box/Option now lives in core_builtin as `#specializing` blocks with NATIVELY evaluated `umangleable(T)` gates and meta-free bodies (every primitive implements IUniqueMangleName, so element renders are uniform `.get_unique_name()` calls). Because core_builtin is the stdlib of every host compilation AND a base source of every unit-B (meta) compilation, the impl is injected symmetrically in all modules that specialize these types — the cross-module asymmetry that previously crashed the JIT'd unique-name trampoline is gone, and no JIT gate recursion is possible. `new Bag<#make_list()>()` specializes with the live list's canonical name (`Ls_3_1_2_3`) and `#list_sum(L)` folds 1+2+3 at compile time (output `sum=6`). Pass3 (object args are JIT-only); utils.penguin via Compile.Args (`_utils.List` is not auto-loaded).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
#fun make_list() -> _utils.List<i64> {
    let l: mut _utils.List<i64> = new _utils.List<i64>();
    l.push(1);
    l.push(2);
    l.push(3);
    return l;
}
#fun list_sum(l: _utils.List<i64>) -> i64 {
    let s: mut i64 = 0;
    let i: mut i64 = 0;
    while (i < cast<i64>(l.size())) {
        s = s + l.at(cast<u64>(i)).some;
        i = i + 1;
    }
    return s;
}
#template(L: _utils.List<i64>) class Bag {
    sum: i64 = #list_sum(L);
}
initial {
    let probe = new _utils.List<i64>();
    println("sum=" + cast<string>(new Bag<#make_list()>().sum));
}
```

## Compile
Args: `EmperorPenguin/src/utils.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `sum=6
`
ExpectedStderr: DISCARD
