# EnumVariantWithStringField
## Description
Enum variant wrapping a class with string field.

## Apply To
* BabyPenguin

## Test Code
```
class TestClass {
    value: !mut string = "";
    fun new(mut this, v: string) {
        this.value = v;
    }
}

enum TestEnum {
    variant_a: TestClass;
}

initial {
    let obj = new TestClass("hello");
    let wrapped = new TestEnum.variant_a(obj);

    if (wrapped is TestEnum.variant_a) {
        let result = wrapped.variant_a.value;
        println("result=" + result);
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
ExpectedStdout: EQUALS `result=hello
`
ExpectedStderr: DISCARD
