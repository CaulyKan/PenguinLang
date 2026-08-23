## Timing Model
Penguin-lang has a very unique concept in general purpose programming languages, the timing model. This concept enables penguin-lang to process job with the control of how time elapses, which is very useful in gaming logic, HDL simulation and scientific calculation.

## Realtime Timing Model
The default timing model is the realtime timing model, which uses wall clock as timing source. In this model, when you call wait, current routine will be suspended for given wall time.
```
initial {
	print("wall_time: {}, time: {}, hello @0", time.wall_time(), time.now());
	wait(2s);
	print("wall_time: {}, time: {}, hello @2", time.wall_time(), time.now());
}

initial {
	while (true) {
		wait(1s);
		print("wall_time: {}, time: {}, hello @1", time.wall_time(), time.now());
	}
}
```
Above code prints:
```
wall_time: 10:05:01, time: 10:05:01, hello @0
wall_time: 10:05:02, time: 10:05:02, hello @1
wall_time: 10:05:03, time: 10:05:03, hello @2
```

## Custom Timing Model
In custom timing model, you can define own timing units, referred as simulation time. For example, in game logic, we offen use 1 tick as minimum time unit. Now, `wait` keyword can wait for given ticks, but no longer actual time units. You can still call `sleep` to suspend current routine  for given wall time, but this will not change simulation time.
```
initial {
	print("wall_time: {}, time: {}, hello @0", time.wall_time(), time.now());
	wait(2 tick);
	print("wall_time: {}, time: {}, hello @2", time.wall_time(), time.now());
	sleep(1s);
	print("wall_time: {}, time: {}, hello @2", time.wall_time(), time.now());
}
	
initial {
	while (true) {
		wait 1 tick;
		print("wall_time: {}, time: {}, hello @1", time.wall_time(), time.now());
	}
}

initial {
	wait 3 tick;
	print("wall_time: {}, time: {}, hello @3", time.wall_time(), time.now());
}
```
Above code prints:
```
wall_time: 10:05:01, time: 0, hello @0
wall_time: 10:05:01, time: 1, hello @1
wall_time: 10:05:01, time: 2, hello @2
wall_time: 10:05:02, time: 2, hello @2
wall_time: 10:05:02, time: 3, hello @3
```
Note that `sleep` only cause the change of wall time, but not simulation time.

------------
Value assignment in custom timing model is different from realtime model. Consider following code:
```
let a : mut i32 = 0;				// initial value, assigned before start of simulation
initial {
	a = 2;
	println("a={} @ {} tick", a, time.now());
	wait 1 tick;
	println("a={} @ {} tick", a, time.now());
}
```
Above code will print 
```
a=0 @ 0 tick
a=2 @ 1 tick
```
Maybe it doesn't match your expection, but first print is not the value you assigned. This is because it's not possible for you to modify the value at current time. Any value assignment will take 'simulation time' to take effect. This is actually same in realtime model, because still you can't modify a value at current time, CPU will have to spend about 1ns to do an assignment.
If you have to visit variable after assignment, you can use zero-time. 
```
let a : mut i32 = 0;				// initial value, assigned before start of simulation
initial {
	a = 2;
	println("a={} @ {} tick", a, time.now());
	wait 0 tick;
	println("a={} @ {} tick", a, time.now());
}
```
Above code will print 
```
a=0 @ 0 tick
a=2 @ 0 tick
```
Waiting for zero-time wont cause simulation time to proceed, it will notify the scheduler to finish all jobs at current simulation time (like `yield` in many other co-routine libraries), update all assignments, then re-schedule current routine. You MUST NOT rely on wait zero-time to wait for value assignments on other routines, and if you have to, use an event (`Event<T>` broadcast) or a port/channel connection (see `11_PortsChannelsEvents.md`).

## Waiting for conditions and edges

`wait <condition>` is level-sensitive: the routine parks and the condition is re-evaluated every scheduler round until it holds (`wait a == 5`, `wait port == false`).

`wait change(<expr>)` is edge-sensitive: it samples the watched expression's value at entry, parks until the value differs, and yields the NEW value — the classic edge-detection idiom (`let v = x; while (x == v) { wait x; }`) as sugar. Works on plain variables and port reads alike:

```penguin
initial {
    let v : i64 = wait change(level);   // wakes on the next value change
}
```

A condition (or watched value) that never changes contributes identical scheduler rounds and ends the program at quiescence, like every other parked waiter.

## Example
Following is an example of playing chess between two players, which make a good use of custom timing model.
```
fun move_black() {
	...
}
	
fun move_white() {
	...
}

enum victory_result {
	white;
	black;
	none;
}
	
fun	check_victory() -> victory_result {
	...
}

initial {
	while true {
		move_black();
		wait 2 tick;
	}
}
		
initial {
	wait 1 tick;
	while true {
		move_white();
		wait 2 tick;
	}
}

initial {
	while (true) {
		wait 1tick;
		match check_victory() {
			case none:
				break;
			case black:
				println("black wins!");
				exit(0);
			case white:
				println("white wins!");
				exit(0);
		}
	}
}
```