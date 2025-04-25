Basic Syntax
-------------
The penguin-lang uses a braces-based block syntax similar to rust.

```
var a: int = 0;

fun add(val a: int, val b: int) -> int {
	return a + b;
}

initial {
	if (a == 0) {
		print("a == 0");
	} else {
		break;
	}
		
	while a < 10 {
		a++;
	}
		
	for (var i in 0..a) {
		val b = add(i, 1);
		print("b == {}", b);
	}
}
```
