## Timing Model
Penguin-lang has a very unique concept in general purpose programming languages, the timing model. This concept enables penguin-lang to process jobs with the control of how time elapses, which is very useful in gaming logic, HDL simulation and scientific calculation.

Penguin-lang uses a discrete simulation clock measured in **ticks**. All routines share the same simulation time; the scheduler advances it only when no job can make progress at the current time. Timing features require the `--enable-coroutine` compile flag.

The current simulation time is available through the `_sim_now()` builtin:
```
initial {
	println(cast<string>(_sim_now()));   // 0
	wait 3 tick;
	println(cast<string>(_sim_now()));   // 3
}
```

## Waiting for Duration
`wait <n>;` suspends the routine until the simulation clock has advanced by `n` ticks, where `<n>` is any integer expression — a literal, a variable (any integer width; narrower ints are cast to the i64 deadline unit) or a computation. The long form `wait <n> tick;` means exactly the same thing; the `tick` keyword is optional. Timers with shorter durations fire first; equal durations fire in scheduling order:
```
initial {
	wait 1;
	println("A");
}
initial {
	wait 2 tick;
	println("B");
}
```
This prints `A` then `B`.

## Zero-Time and Settling
`wait 0 tick;` does not advance simulation time. It lets the scheduler finish every runnable job at the current time — updating assignments and propagation — and then reschedules the current routine:
```
let a : mut i32 = 0;

async fun set_a() {
	a = 2;
}

initial {
	let f = async set_a();
	wait 0 tick;
	println(cast<string>(a));   // 2
}
```
A bare `wait;` (no expression) parks the routine for one scheduler round — one delta — without touching the tick counter. You MUST NOT rely on zero-time waits to observe value assignments on other routines; use an event (`Event<T>` broadcast) or a port/channel connection (see `11_PortsChannelsEvents.md`).

## Variable Assignment and Reads
A plain variable is ordinary storage: a read observes the latest assignment — immediately for subsequent reads in the same routine, and for other routines once the assigning routine has executed it.
```
let a : mut i32 = 0;				// initial value, assigned before start of simulation
initial {
	a = 2;
	println(cast<string>(a));		// prints 2
}
```
Port reads follow different rules: a bare port read is a **settle point** — it first lets the current time's propagation settle, then samples the channel's current slot (see `11_PortsChannelsEvents.md`).

## Waiting for Conditions and Edges

`wait <condition>` is level-sensitive: the routine parks and the condition is re-evaluated every scheduler round until it holds (`wait a == 5`, `wait port == false`).

`wait change(<expr>)` is edge-sensitive: it samples the watched expression's value at entry, parks until the value differs, and yields the NEW value — the classic edge-detection idiom (`let v = x; while (x == v) { wait x; }`) as sugar. Works on plain variables and port reads alike:

```penguin
initial {
    let v : i64 = wait change(level);   // wakes on the next value change
}
```

## Quiescence
When every routine is parked and no timer, event, or channel can wake anything, the program has reached quiescence and terminates normally — with exit code 0. A condition that never becomes true is a legal final state:
```
let a : mut i32 = 0;

initial {
	println("start");
	wait a == 99;			// parks forever; program ends at quiescence
	println("never");
}
```
This prints `start` and exits with code 0.

## Example
Following is an example of playing chess between two players, which makes good use of the timing model.
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
		wait 1 tick;
		let r : victory_result = check_victory();
		if (r is victory_result.none) {
			continue;
		} else if (r is victory_result.black) {
			println("black wins!");
			exit(0);
		} else {
			println("white wins!");
			exit(0);
		}
	}
}
```
