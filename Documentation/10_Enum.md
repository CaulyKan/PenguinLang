
## Enum Types
Penguin-lang supports rust-like enums, which can contain values. One of the most commonly used enum type is `option<T>`, which can be `some` or `none`.
Note that
```
enum option<T> {
	some: T;
	none;
}
	
var a : option<int> = new option<int>.none();
inital {
	a = new option<int>.some(1);
}
```

## Checking a enum
Penguin-lang supports using 'is' keyword to check if enum matchs a value. '==' works when comparing enum instances, to check if they have some enum and some value.
```
enum option<T> {
	some: T;
	none;
}

var a = new option<int>.some(1);
initial {
	if (a is option<int>.some) {
		println("a is some({})", a.some);
	} else if (a is option<int>.none) {
		println("a is none");
	}
	
	a == new option<int>.some(1); // true
	a == new option<int>.none(); // false
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
	
	// initial/on is supported 
	initial {
		println("hello enum");
	}
	on (this is option<T>.none) {
		println("hello none");
	}
}

var a = new option<int>.some(1)
initial {	
	if (a is option<int>.some) {
		println("a is some({})", a.some);
	} else if (a is option<int>.none) {
		println("a is none");
	}
}
```