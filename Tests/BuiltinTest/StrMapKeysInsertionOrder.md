# StrMapKeysInsertionOrder
## Description
`_utils.StrMap<V>` generic value-type map with dynamic buckets (added in the
batch-0.4 refactor of `EmperorPenguin/src/utils.penguin`): the table starts at
8 heads and doubles at load factor 0.75. This locks in the two behavioral
guarantees of that design:

1. **keys() is insertion-ordered and rehash-independent** — the same insertion
   sequence yields the same keys() output no matter how many bucket doublings
   happened along the way (LinkedHashMap-style determinism, without an explicit
   entry chain: the key_list parallel array IS the order, buckets only
   accelerate lookup). 27 keys here cross the 8->16->32->64 growth path.
2. **Re-inserting an existing key updates its value in place and does NOT move
   it** ("alpha" stays first after being updated, "one" stays first in the
   string-valued map).

Also locks the `get(key, miss_value)` explicit-miss convention (-1 = absent)
for StrMap<i64> and exercises a non-i64 specialization (StrMap<string>).

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let m: mut _utils.StrMap<i64> = new _utils.StrMap<i64>();
        m.set("alpha", 1);
        m.set("beta", 2);
        m.set("gamma", 3);
        m.set("alpha", 10);
        let n: mut i64 = 0;
        while (n < 24) {
            m.set("key" + cast<string>(n), n * 2);
            n = n + 1;
        }
        let ks: _utils.List<string> = m.keys();
        let i: mut i64 = 0;
        while (i < cast<i64>(ks.size())) {
            println(ks.at_unchecked(cast<u64>(i)));
            i = i + 1;
        }
        println(cast<string>(m.get("alpha", -1)));
        println(cast<string>(m.get("key23", -1)));
        println(cast<string>(m.get("nope", -1)));
        println("size " + cast<string>(m.size()));
        let sm: mut _utils.StrMap<string> = new _utils.StrMap<string>();
        sm.set("one", "uno");
        sm.set("two", "dos");
        sm.set("one", "UNO");
        println(sm.get("one", "?"));
        println(sm.get("two", "?"));
        println(sm.get("three", "?"));
        println(sm.keys().at_unchecked(cast<u64>(0)));
        println(sm.keys().at_unchecked(cast<u64>(1)));
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
ExpectedStdout: EQUALS `alpha
beta
gamma
key0
key1
key2
key3
key4
key5
key6
key7
key8
key9
key10
key11
key12
key13
key14
key15
key16
key17
key18
key19
key20
key21
key22
key23
10
46
-1
size 27
UNO
dos
?
one
two
`
ExpectedStderr: DISCARD
