# EnumVariantWithListField
## Description
Enum variant with List<T> field.

## Apply To
* BabyPenguin

## Test Code
```
class WithList {
    items: mut _utils.List<i64> = new _utils.List<i64>();
    name: !mut string = "";
    fun new(mut this, name: string) {
        this.name = name;
    }
}

enum ListEnum {
    with_list: WithList;
}

initial {
    let obj = new WithList("test");
    obj.items.push(1);
    obj.items.push(2);
    let wrapped = new ListEnum.with_list(obj);

    if (wrapped is ListEnum.with_list) {
        println("name=" + wrapped.with_list.name);
        println("count=" + cast<string>(cast<i64>(wrapped.with_list.items.size())));
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
count=2
`
ExpectedStderr: DISCARD
