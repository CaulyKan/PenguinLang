# MetaCreateDefinitionMemberShape
## Description
`compiler().create_definition` accepts CLASS-MEMBER-shaped definition text: the bare field `width : i32 = 7 ;` parses (via Parser.parse_definitions_unit's Identifier branch — top-level defs never start with a plain identifier, so this is meta-only surface) and splices as a normal class member. Guards the member-shape half of create_definition's definition-list parsing. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun mk_field() -> ast {
    return compiler().create_definition("width : i32 = 7 ;");
}
class Box {
    #mk_field();
    fun area(this) -> i32 { return this.width * 2; }
}
initial {
    let b: mut Box = new Box();
    println(cast<string>(b.area()));
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
ExpectedStdout: EQUALS `14
`
ExpectedStderr: DISCARD
