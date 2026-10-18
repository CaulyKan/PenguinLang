# ArrayInGenericClass
## Description
Arrays inside generic classes: a `array<T>` field and array<T> parameter/return in generic-class methods specialize correctly (array<T> resolves through the specialized scope).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
class Stack {
    items: mut array<T>;
    count: mut u64 = 0;

    fun new(mut this, cap: u64) {
        this.items = create_array(cap, default_of());
        this.count = 0;
    }

    fun push(mut this, v: T) {
        this.items.set(this.count, v);
        this.count += 1;
    }

    fun top(this) -> T {
        return this.items.at(this.count - 1).some;
    }
}

fun default_of() -> i64 {
    return 0;
}

initial {
    let s : mut Stack<i64> = new Stack<i64>(8);
    s.push(10);
    s.push(20);
    s.push(30);
    println("top=" + cast<string>(s.top()));
    println("count=" + cast<string>(s.count));
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
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `top=30
count=3
`
ExpectedStderr: DISCARD
