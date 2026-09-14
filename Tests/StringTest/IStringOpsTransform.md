# IStringOpsTransform
## Description
End-to-end test of `__builtin.IStringOps` transform methods for the primitive `string`: to_upper/to_lower (ASCII alphabet-table fold), trim/trim_start/trim_end (whitespace set " \t\n\r"), replace (empty `from` returns the string unchanged; adjacent matches; removal; growth), reverse, repeat (n <= 0 → ""), pad_left/pad_right (first unit of `ch`, falling back to a single space for an empty `ch`; width <= length returns the string unchanged). Verified on all five compilers (BabyPenguin VM + CS, EmperorPenguin Pass1/Pass2/Pass3).

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    println("upper=" + "MiXeD 123!".to_upper());
    println("lower=" + "MiXeD 123!".to_lower());
    println("upper_empty=" + "[" + "".to_upper() + "]");
    println("trim=[" + "  hi \t\n".trim() + "]");
    println("trim_all_ws=[" + " \t\r\n".trim() + "]");
    println("trim_none=[" + "hi".trim() + "]");
    println("trim_start=[" + "  hi  ".trim_start() + "]");
    println("trim_end=[" + "  hi  ".trim_end() + "]");
    println("replace=" + "a-b-c".replace("-", "+"));
    println("replace_multi=" + "one fish two fish".replace("fish", "cat"));
    println("replace_adjacent=" + "a--b".replace("-", "x"));
    println("replace_to_empty=" + "a-b-c".replace("-", ""));
    println("replace_grow=" + "a.b".replace(".", ".."));
    println("replace_miss=" + "abc".replace("z", "y"));
    println("replace_empty_from=" + "abc".replace("", "x"));
    println("reverse=" + "stressed".reverse());
    println("reverse_empty=" + "[" + "".reverse() + "]");
    println("repeat=" + "ab".repeat(3));
    println("repeat_zero=" + "[" + "ab".repeat(0) + "]");
    println("repeat_neg=" + "[" + "ab".repeat(-2) + "]");
    println("pad_left=" + "[" + "7".pad_left(3, "0") + "]");
    println("pad_left_multi=" + "[" + "7".pad_left(5, "xy") + "]");
    println("pad_left_default=" + "[" + "7".pad_left(3, "") + "]");
    println("pad_left_exact=" + "[" + "abc".pad_left(3, "0") + "]");
    println("pad_left_short=" + "[" + "abcd".pad_left(3, "0") + "]");
    println("pad_right=" + "[" + "7".pad_right(3, ".") + "]");
    println("pad_right_default=" + "[" + "7".pad_right(3, "") + "]");
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
ExpectedStdout: EQUALS `upper=MIXED 123!
lower=mixed 123!
upper_empty=[]
trim=[hi]
trim_all_ws=[]
trim_none=[hi]
trim_start=[hi  ]
trim_end=[  hi]
replace=a+b+c
replace_multi=one cat two cat
replace_adjacent=axxb
replace_to_empty=abc
replace_grow=a..b
replace_miss=abc
replace_empty_from=abc
reverse=desserts
reverse_empty=[]
repeat=ababab
repeat_zero=[]
repeat_neg=[]
pad_left=[007]
pad_left_multi=[xxxx7]
pad_left_default=[  7]
pad_left_exact=[abc]
pad_left_short=[abcd]
pad_right=[7..]
pad_right_default=[7  ]
`
ExpectedStderr: DISCARD
