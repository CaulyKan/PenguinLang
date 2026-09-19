# Enum

Penguin-lang supports Rust-style enums: a set of named variants, each optionally carrying a payload. One of the most commonly used enum types is `Option<T>`, which can be `some` or `none`.

Enums with generic parameters are declared with `#template` (see [Class](./05_Class.md)):
```
#template(T: type)
enum Option {
	some: T;
	none;
}

let a : mut Option<i32> = new Option<i32>.none();
initial {
	a = new Option<i32>.some(1);
}
```

## Constructing an enum value
Enum values are always constructed with `new`, with parentheses even for variants without a payload:
```
let a = new Option<i32>.some(1);
let b = new Option<i32>.none();
```

## Checking an enum
Penguin-lang supports using the 'is' keyword to check if an enum matches a variant. Use the variant name to read the payload, e.g. `a.some`.
```
initial {
    let a = new Option<i32>.some(1);
	if (a is Option<i32>.some) {
		println("a is some(" + cast<string>(a.some) + ")");
	} else if (a is Option<i32>.none) {
		println("a is none");
	}
}
```
To compare two enum values for equality, cast both to `string`:
```
if (cast<string>(a) == cast<string>(b)) { ... }
```

## Enum members
Like classes, enums can have their own members:
```
#template(T: type)
enum Option {
	some: T;
	none;

	// method
	fun value_or(this, default_val: T) -> T {
		if (this.is_none()) {
			return default_val;
		} else {
			return this.some;
		}
	}

	// however, enums can't have a constructor
}
```

## Classification
Enums are **value types**: assignment copies the whole tagged union (tag + payload). A payload field of a reference type still shares the referenced object. Enums may implement interfaces (vtable dispatch works for enums as well, see [Interface](./07_Interface.md)).
