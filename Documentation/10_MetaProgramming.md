# Meta Programming

Penguin-lang provides powerful compile-time meta-programming capabilities through **Meta Functions** and **Compile-Time Evaluation**. This allows you to write code that executes during compilation, enabling zero-cost abstractions and type-level computations.

## Overview

| Feature                | Syntax         | Description                                   |
| ---------------------- | -------------- | --------------------------------------------- |
| Meta Function          | `#fun`         | Function executed at compile time             |
| Meta Function Call     | `#func_name()` | Invoke a meta function                        |
| Compile-Time Condition | `const if`     | Conditional code generation                   |
| Compile-Time Loop      | `const for`    | Loop unrolling at compile time                |
| Generic Declaration    | `#template`    | Syntactic sugar for type-level meta functions |
| Compile-Time Symbol    | `#define`      | Define compile-time symbols                   |
| AST Parameter          | `ast` type     | Pass arguments as AST nodes                   |
| Compile Check          | `#can_compile` | Test if code can compile                      |
| Code Generation        | `#fun -> ast`  | Generate code at compile time                 |

## Meta Functions

A meta function is a function that executes during compilation. It must have a return value and can return either a value or a type.

### Basic Meta Function

```penguin
#fun fib(n: u32) -> u32 {
    // Regular Penguin syntax, executed at compile time
    if (n <= 1) return n;
    return fib(n-1) + fib(n-2);  // Recursive calls don't need # prefix
}

initial {
    let x: u32 = #fib(10);  // x = 55, computed at compile time
    // The above is equivalent to: let x: u32 = 55;
}
```

### Type-Level Meta Function

Meta functions can operate on and return types:

```penguin
#fun signed_to_unsigned(t: type) -> type {
    if (t == typeof(i32)) return typeof(u32);
    if (t == typeof(i64)) return typeof(u64);
    if (t == typeof(i8)) return typeof(u8);
    if (t == typeof(i16)) return typeof(u16);
    compiler().error("Unsupported type");
}

initial {
    let t : #signed_to_unsigned(i32) = 0;  // t is u32
}
```

This approach provides a straightforward way to manipulate types, avoiding the complex template metaprogramming techniques required in C++.

### Using Meta Functions in Type Positions

```penguin
#template(T: type)
fun abs(v: T) -> #signed_to_unsigned(T) {
	if (v > 0) {
		return cast<#signed_to_unsigned(T)>(v);
	} 
	else {
		return cast<#signed_to_unsigned(T)>(-v);
	}
}
```

## Compile-Time Conditions: `const if`

`const if` enables conditional code generation at compile time. The condition must be evaluable at compile time.

```penguin
#template(T: type)
fun default_value() -> T {
    const if (T == i32) {
        return 0;
    } else if (T == f32) {
        return 0.0;
    } else if (T == string) {
        return "";
    } else {
        return T.default();
    }
}
```

**Important**: When using `const if`, all `else if` and `else` branches are automatically compile-time branches. You cannot mix runtime and compile-time branches.

```penguin
// CORRECT: All branches are compile-time
const if (condition) {
    // ...
} else {
    // This is also compile-time
}
```

## Compile-Time Loops: `const for`

`const for` enables loop unrolling at compile time:

```penguin
#template(N: u32)
fun sum() -> u32 {
    let result: u32 = 0;
    const for (i in range(0, N)) {
        result = result + i;
    }
    return result;
}

initial {
    let x = sum<10>();  // Compile-time equivalent of:
                        // let result = 0;
                        // result = result + 0;
                        // result = result + 1;
                        // ... (unrolled 10 times)
}
```

## Generic Declaration: `#template`

`#template` is syntactic sugar for a meta function that returns a type:

```penguin
// These two declarations are equivalent:

// Style 1: #template sugar
#template(T: type)
class Box<T> {
    value: T;
}

// Style 2: Full meta function form
#fun Box(T: type) -> type {
    return compiler().create_class(...);
}
```

### Template Parameters

Templates support both type and value parameters:

```penguin
#template(T: type, v: T)
class Container<T> {
    data: T = v;
}
```

## Compile-Time Symbols: `#define`, `#value`, and `#defined`

PenguinLang supports `#define`, `#value`, and `#defined` which is quite similar to C/C++ preprocessor macros. But they are basically a map hold by
compiler.

```penguin
initial {
    #define("PI", 3.14);
    println("PI = {}", #value("PI"));
    const if (#defined("PI")) {
        println("PI is defined");
    }
}
```

## Compile-Time Code Check: `#can_compile`

`#can_compile` allows the compiler to attempt compiling a piece of code and return whether it succeeds. This provides a cleaner alternative to C++'s SFINAE:

```penguin
#template(T: type)
fun try_call_foo(v: T) {
    const if (#can_compile("v.foo()")) {
        v.foo();  // Only generated if T has method foo()
    } else {
        print("Type does not have foo() method");
    }
}
```

### Building Custom Constraints

You can build custom constraint functions similar to C++ concepts:

```penguin
#fun Addable(t: type) -> bool {
    return #can_compile("let a: t; let b: t; a + b;");
}

#fun Comparable(t: type) -> bool {
    return #can_compile("let a: t; let b: t; a < b;");
}

#template(T: type)
fun max(a: T, b: T) -> T {
    const if (#Comparable(T)) {
        if (a < b) return b;
        return a;
    } else {
        compiler().error("Type T must be Comparable");
    }
}
```

## AST Parameters and Code Generation

### AST Type Parameter

When a meta function has a single parameter of type `ast`, the compiler passes arguments as AST nodes rather than evaluating them:

```penguin
#fun generate_getter(field_ast: ast) -> ast {
    // field_ast contains the raw AST of the argument
    let field_name = field_ast.as_identifier();
    let field_type = field_ast.infer_type();

    return compiler().create_function(
        "get_" + field_name,
        "return this." + field_name + ";"
    );
}

class Point {
    x: i32;
    y: i32;

    #generate_getter(x);  // Generates: fun get_x() -> i32 { return this.x; }
    #generate_getter(y);  // Generates: fun get_y() -> i32 { return this.y; }
}
```

### Variadic Support via AST

The `ast` parameter type naturally supports variadic arguments:

```penguin
#fun print_all(args: ast) -> ast {
    // args contains all arguments as a list of AST nodes
    let mut code = "";
    const for (i in 0..args.count()) {
        code = code + "print(" + args[i].to_string() + ");";
    }
    return compiler().create_ast(code);
}

initial {
    #print_all("hello", 42, 3.14);  // Generates: print("hello"); print(42); print(3.14);
}
```

### Meta Functions Returning AST

When a `#fun` returns `ast`, the result is inserted at the call site:

```penguin
#fun derive_clone(t: type) -> ast {
    let fields = t.get_fields();
    let mut clone_body = "return new " + t.to_string() + "(";

    const for (i in 0..fields.count()) {
        if (i > 0) clone_body = clone_body + ", ";
        clone_body = clone_body + "this." + fields[i].name;
    }
    clone_body = clone_body + ");";

    return compiler().create_function(
        "clone",
        "fun clone(this) -> " + t.to_string() + " { " + clone_body + " }"
    );
}

class Point {
    x: i32;
    y: i32;

    #derive_clone(Point);  // Inserts clone() method implementation
}
```
