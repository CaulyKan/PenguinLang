# MetaIfBareNameValue
## Description
File-scoped conditional compilation (batch 2.1/2.2, both implementations): a BARE identifier in a #if condition reads its define VALUE directly (`TIER == "v"`), the redesign §3 replacement for the retired `#option("K") == "v"` form. Combined with `#defined` guarding and elif value dispatch.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#define("TIER","gold");
#if (TIER == "gold") {
    fun level() -> string { return "gold"; }
} #elif (TIER == "silver") {
    fun level() -> string { return "silver"; }
} #else {
    fun level() -> string { return "bronze"; }
}
#if (#defined("TIER") && TIER == "gold") {
    fun guarded() -> string { return "guarded-gold"; }
}
initial {
    println(level());
    println(guarded());
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
ExpectedStdout: EQUALS `gold
guarded-gold
`
ExpectedStderr: DISCARD
