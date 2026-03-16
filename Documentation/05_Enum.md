## Enum Types
Penguin-lang supports Rust-style enums, which can contain values. One of the most commonly used enum types is `Option<T>`, which can be `some` or `none`.

```
enum Option<T> {
	some: T,
	none,
}

let a : mut Option<i32> = new Option<i32>.none();
initial {
	a = new Option<i32>.some(1);
}
```

## Checking an enum
Penguin-lang supports using the 'is' keyword to check if an enum matches a value. Use the enum name to get data from the enum, e.g. `a.some`.
```
let a = new Option<i32>.some(1);
initial {
	if (a is Option<i32>.some) {
		println("a is some({})", a.some);
	} else if (a is Option<i32>.none) {
		println("a is none");
	}
}
```

## Enum members
Like classes, enums can have their own members:
```
enum Option<T> {
	some: T,
	none,

	// method
	fun value_or(this, default: T) -> T {
		if (this.is_none()) {
			return default;
		} else {
			return this.some;
		}
	}

	// however, enums can't have a constructor

	on (this.is_none()) {
		println("hello none");
	}
}
```
