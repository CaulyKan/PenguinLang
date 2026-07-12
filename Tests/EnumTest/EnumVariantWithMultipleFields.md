# EnumVariantWithMultipleFields
## Description
Enum variant wrapping a class with multiple fields of different types.

## Apply To
* BabyPenguin

## Test Code
```
class MultiField {
    name: !mut string = "";
    count: !mut i64 = 0;
    flag: !mut bool = false;
    fun new(mut this, n: string, c: i64, f: bool) {
        this.name = n;
        this.count = c;
        this.flag = f;
    }
}

enum MultiEnum {
    first: MultiField;
}

initial {
    let obj = new MultiField("test", 42, true);
    let wrapped = new MultiEnum.first(obj);

    if (wrapped is MultiEnum.first) {
        println("name=" + wrapped.first.name);
        println("count=" + cast<string>(wrapped.first.count));
        println("flag=" + (if (wrapped.first.flag) { "true" } else { "false" }));
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
ExpectedStdout: EQUALS `name=test
count=42
flag=true
`
ExpectedStderr: DISCARD
