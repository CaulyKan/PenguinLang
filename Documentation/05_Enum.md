
## Enum Types
Penguin-lang supports rust-like enums, which can contain values. One of the most commonly used enum type is `option<T>`, which can be `some` or `none`.
Note that
```
enum option<T> {
	some: T;
	none;
}
	
var a : option<i32> = new option<i32>.none();
inital {
	a = new option<i32>.some(1);
}
```

## Checking a enum
Penguin-lang supports using 'is' keyword to check if enum matchs a value.
Use the enum name to get data from enum, e.g. `a.some`
```
enum option<T> {
	some: T;
	none;
}

var a = new option<i32>.some(1);
initial {
	if (a is option<i32>.some) {
		println("a is some({})", a.some);
	} else if (a is option<i32>.none) {
		println("a is none");
	}
}
```

## Enum members
Like classes, enum can have its members
```
enum option<T> {
	some: T;
	none;
	
	// method
	fun value_or(val this: option<T>, val default: T) -> T {
		if (this is option<T>.none) {
			return default;
		} else {
			return this.some;
		}
	}
	
	// however enum can't have a constructor
	
	on (this is option<T>.none) {
		println("hello none");
	}
}
```