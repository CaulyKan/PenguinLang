# EnumVariantWithNestedEnum
## Description
Enum variant containing a class which contains another enum field.

## Apply To
* BabyPenguin

## Test Code
```
enum InnerEnum {
    option_a;
    option_b;
}

class OuterClass {
    inner: !mut InnerEnum = new InnerEnum.option_a();
    data: !mut string = "";
    fun new(mut this, e: InnerEnum, d: string) {
        this.inner = e;
        this.data = d;
    }
}

enum OuterEnum {
    wrapper: OuterClass;
}

initial {
    let inner = new InnerEnum.option_b();
    let obj = new OuterClass(inner, "nested");
    let wrapped = new OuterEnum.wrapper(obj);

    if (wrapped is OuterEnum.wrapper) {
        if (wrapped.wrapper.inner is InnerEnum.option_b) {
            println("data=" + wrapped.wrapper.data);
        }
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
ExpectedStdout: EQUALS `data=nested
`
ExpectedStderr: DISCARD
