# IStringOpsCompareSplit
## Description
End-to-end test of `__builtin.IStringOps` comparison and split methods for the primitive `string`: equals_ignore_case (ASCII fold, length-sensitive), compare (lexicographic unit-wise, negative/zero/positive, prefix ordering), count (non-overlapping occurrences; empty sub → 0), and split — a lazy `StringSplitIterator` (IIterator<string>) that is directly for-in-able. Split semantics: pieces between non-overlapping separators, a trailing separator yields one final empty piece (Python-like), a missing separator yields the whole string, an empty source with a non-empty separator yields exactly one empty piece, and manual `.next()` calls exhaust to none. Verified on all five compilers (BabyPenguin VM + CS, EmperorPenguin Pass1/Pass2/Pass3).

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    println("eqic_t=" + cast<string>("AbC".equals_ignore_case("aBc")));
    println("eqic_f=" + cast<string>("AbC".equals_ignore_case("abD")));
    println("eqic_len=" + cast<string>("abc".equals_ignore_case("abcd")));
    println("eqic_empty=" + cast<string>("".equals_ignore_case("")));
    println("cmp_lt=" + cast<string>("abc".compare("abd")));
    println("cmp_eq=" + cast<string>("abc".compare("abc")));
    println("cmp_gt=" + cast<string>("b".compare("a")));
    println("cmp_prefix=" + cast<string>("ab".compare("abc")));
    println("cmp_empty_l=" + cast<string>("".compare("a")));
    println("cmp_empty_r=" + cast<string>("a".compare("")));
    println("count=" + cast<string>("a-b-a-b-a".count("a")));
    println("count_sub=" + cast<string>("aaaa".count("aa")));
    println("count_miss=" + cast<string>("abc".count("z")));
    println("count_empty=" + cast<string>("abc".count("")));
    for (let part : string in "one,two,,three".split(",")) {
        println("part=[" + part + "]");
    }
    for (let part : string in "a;b;c".split(";")) {
        println("semi=[" + part + "]");
    }
    let trailing: string = "x,y,";
    for (let part : string in trailing.split(",")) {
        println("trail=[" + part + "]");
    }
    for (let part : string in "solo".split(",")) {
        println("solo=[" + part + "]");
    }
    for (let part : string in "".split(",")) {
        println("never=[" + part + "]");
    }
    let it: mut IIterator<string> = "p-q".split("-");
    println("manual=" + it.next().some);
    println("manual=" + it.next().some);
    println("manual_done=" + cast<string>(it.next().is_none()));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `eqic_t=true
eqic_f=false
eqic_len=false
eqic_empty=true
cmp_lt=-1
cmp_eq=0
cmp_gt=1
cmp_prefix=-1
cmp_empty_l=-1
cmp_empty_r=1
count=3
count_sub=2
count_miss=0
count_empty=0
part=[one]
part=[two]
part=[]
part=[three]
semi=[a]
semi=[b]
semi=[c]
trail=[x]
trail=[y]
trail=[]
solo=[solo]
never=[]
manual=p
manual=q
manual_done=true
`
ExpectedStderr: DISCARD
