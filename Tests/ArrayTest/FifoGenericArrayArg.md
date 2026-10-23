# FifoGenericArrayArg
## Description
A native array as a generic argument of `Fifo<T>` (`Fifo<array<u8>>` — the
consumer reads it immutably). RED SENTINEL (EmperorPenguin): EP fails type
resolution with `Can't resolve type specifier 'mut <mut u8>'` — the Fifo
generic body's `mut T` slot expansion mis-composes with the array type
(the element-mutability rule does not apply to array REFERENCES).
BabyPenguin compiles and runs the same code (known-good). Should turn
green on EP once the generic expansion handles array<T> arguments.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let ch: mut __builtin.Fifo<array<u8>> = new __builtin.Fifo<array<u8>>(8, new __builtin.FifoPolicy.backpressure());
    let b: mut array<u8> = create_array(4, 1);
    ch.write(b);
    let v: array<u8> = wait ch;
    println("len=" + cast<string>(v.size()));
}
```

## Run
ExitCode: 0
ExpectedStdout: EQUALS `len=4
`
