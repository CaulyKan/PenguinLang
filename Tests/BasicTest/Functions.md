# Functions
## Description
Function calls with parameters, return values, void functions, recursion, and nested calls.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    fun add(a: i64, b: i64) -> i64 {
        return a + b;
    }
    initial {
        println(cast<string>(add(3, 4)));
    }
}
namespace __c2 {
    fun mul3(a: i64, b: i64, c: i64) -> i64 {
        return a * b * c;
    }
    initial {
        println(cast<string>(mul3(2, 3, 4)));
    }
}
namespace __c3 {
    fun greet() {
        println("hi");
    }
    initial {
        greet();
    }
}
namespace __c4 {
    fun fact(n: i64) -> i64 {
        if (n <= 1) { return 1; }
        return n * fact(n - 1);
    }
    initial {
        println(cast<string>(fact(10)));
    }
}
namespace __c5 {
    fun fib(n: i64) -> i64 {
        if (n <= 1) { return n; }
        return fib(n - 1) + fib(n - 2);
    }
    initial {
        println(cast<string>(fib(10)));
    }
}
namespace __c6 {
    fun dbl(x: i64) -> i64 {
        return x * 2;
    }
    fun add_dbl(a: i64, b: i64) -> i64 {
        return dbl(a) + dbl(b);
    }
    initial {
        println(cast<string>(add_dbl(3, 4)));
    }
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
ExpectedStdout: EQUALS `7
24
hi
3628800
55
14
`
ExpectedStderr: DISCARD
