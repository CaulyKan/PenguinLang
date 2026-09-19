# 异步与时序模型

Penguin-lang 为异步与并发而生：`async` 把函数作为并发作业启动并返回 `IFuture`，`wait` 是唯一的挂起/唤醒原语，离散仿真时钟让程序控制时间流逝——这对游戏逻辑、HDL 仿真与科学计算很有用。

EmperorPenguin 上并发特性经编译期 `--enable-coroutine` 启用（BabyPenguin 无条件启用）。不含 `wait`/`async` 的程序无论开关与否输出一致。

## async 与 IFuture

```penguin
fun test() -> i32 {
    wait; // 模拟异步工作
    return 1;
}

initial {
    let task : mut IFuture<i32> = async test();
    println("before wait");
    let a : i32 = wait task;
    println("wait done");
    print(cast<string>(a));
}
```
输出：
```
before wait
wait done
1
```

`async expr` 要求是函数调用；被调函数自身可 async 可不 async。结果是 `IFuture<T>`（立即创建、作业返回时完成）。静态成员调用上的 `async` 被拒绝。

## 有状态函数与隐式 wait

PenguinLang 自动识别函数是否有状态：使用 `wait` 或 `yield`、或调用其他有状态函数的函数是有状态（可挂起）函数；`async`/`!async` 说明符可强制或抑制。

直接调用有状态函数是 `wait async f();` 的简写：
```
initial {
	bar();				// 隐式 wait
	wait (async bar());	// 与上一行等价
}
```

不带表达式的 `wait` 暂停当前作业，调度到下一个调度回合。

## wait 的形式

| 形式 | 唤醒条件 | 值 |
|---|---|---|
| `wait;` | 过一个调度回合（一个 delta） | — |
| `wait n;` / `wait n tick;` | 仿真时钟推进 `n` tick | — |
| `wait <condition>;` | 条件成立（每回合重查） | — |
| `wait change(<expr>);` | 被观察值与进入采样不同 | 新值 |
| `wait <event>;` | 事件发射 | 送达的载荷 |
| `wait <IFuture>;` | future 完成 | 结果 |
| `wait <port>;` | 端口送达事务 | 送达值（见[模块化编程](./10_ModularProgramming.md)） |

`wait change(x)` 在进入时采样被观察表达式的值，停靠到值变化，并产出**新**值——经典边沿检测惯用法的语法糖。对普通变量与端口读取都有效。

## 调度模型

运行时在单线程上协作调度作业：每个挂起点（`wait`、事件停靠、通道轮询）把控制权交还调度器，调度器恢复下一个可运行作业。没有抢占，程序之内没有数据竞争。

* BabyPenguin 在其 VM 上执行有状态函数，在挂起点交错作业。
* EmperorPenguin 原生运行时用纤程（POSIX/Windows）在主线程上运行调度器，在调度器与每个挂起作业之间切换。

值类型在每个值模型边界复制，因此跨挂起点总是安全的；引用类型值由垃圾回收保持存活。

## 事件

事件是一等值（`Event<T>`，载荷必须是值类型），由 wait 循环消费：
```
let A : mut Event<i32> = new Event<i32>();

initial {
	for (let mut i : i32 in range(0, 10)) {
		A.emit(i);
	}
}

initial {
	while (true) {
		let x : i32 = wait A;
		print(cast<string>(x));
	}
}
```
订阅循环按发射顺序接收事件。`emit` 广播后让出一个 delta，重新停靠的循环能跟上连续发射；无人停靠时发射的值丢失（广播不是排队——无论消费者节奏、每个值都必须保留时用 `Fifo` 通道，见[模块化编程](./10_ModularProgramming.md)）。

多个停靠的 wait 循环收到每次发射（广播）；一个 delta 内按协作调度器的启动顺序唤醒。void 载荷事件（`Event<void>`）用 void 字面量发射（`ev.emit(void);`），唤醒不返回值。

## 时序模型

Penguin-lang 使用以 **tick** 计的离散仿真时钟。所有例程共享同一仿真时间；调度器只当前一时刻无作业可推进时才推进它。

当前仿真时间可经 `_sim_now()` 内建读取：
```
initial {
	println(cast<string>(_sim_now()));   // 0
	wait 3 tick;
	println(cast<string>(_sim_now()));   // 3
}
```

### 等待时长
`wait <n>;` 挂起例程直到仿真时钟推进 `n` tick，`<n>` 是任意整数表达式——字面量、变量（任意整数宽度；较窄整数转换到 i64 截止单位）或计算。长形式 `wait <n> tick;` 含义完全相同。时长更短的先触发；时长相等按调度顺序触发：
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
输出 `A` 然后 `B`。

### 零时间与沉淀
`wait 0 tick;` 不推进仿真时间。它让调度器完成当前时刻所有可运行作业——更新赋值与传播——然后重新调度当前例程：
```
let a : mut i32 = 0;

fun set_a() {
	a = 2;
}

initial {
	let f = async set_a();
	wait 0 tick;
	println(cast<string>(a));   // 2
}
```
裸 `wait;`（无表达式）停靠一个调度回合——一个 **delta**——不触碰 tick 计数。**不应**依赖零时间 wait 观察其他例程的值赋值；请用事件（`Event<T>` 广播）或端口/通道连接（见[模块化编程](./10_ModularProgramming.md)）。

### 变量赋值与读取
普通变量是普通存储：读取观察最新赋值——本例程内后续读取立即可见，其他例程在赋值例程执行后可见。
```
let a : mut i32 = 0;				// 初始值，仿真开始前赋值
initial {
	a = 2;
	println(cast<string>(a));		// 输出 2
}
```
端口读取遵循不同规则：裸端口读是**沉淀点**——先让当前时间的传播沉淀，再采样通道当前槽（见[模块化编程](./10_ModularProgramming.md)）。

### 静默
当所有例程停靠、没有定时器、事件或通道能唤醒任何东西时，程序到达静默并正常终止——退出码 0。永不为真的条件是合法的终态：
```
let a : mut i32 = 0;

initial {
	println("start");
	wait a == 99;			// 永久停靠；程序在静默处结束
	println("never");
}
```
输出 `start` 并以退出码 0 退出。程序也可以停靠在文件描述符就绪上（`__builtin._fd_wait_read/_fd_wait_write`，stdio 语言服务器所用）——只要有 fd 等待者停靠，调度器在 `poll()` 中阻塞而不是退出。

## 示例
下面是两位棋手下棋的示例，很好地利用了时序模型。
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
