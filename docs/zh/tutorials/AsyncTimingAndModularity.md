# 异步、时序与模块

PenguinLang 的并发模型有三层：**协程**（`async`/`wait`）、**离散时序模型**（以 tick 计的仿真时钟）与**模块**（带端口、可连线的类）。三者合起来支撑普通并发程序、仿真和 RTL 风格的硬件描述。本页做巡览；精确规则见[异步与时序模型](../specifications/09_AsyncAndTimingModel.md)与[模块化编程](../specifications/10_ModularProgramming.md)规范。

EmperorPenguin 上协程特性需要 `--enable-coroutine`（BabyPenguin 无条件启用）。不含 `wait`/`async` 的程序无论开关与否行为一致。

## 启动作业：async 与 wait

`async f(args)` 把 `f` 作为并发作业启动并返回 `IFuture<T>`。`wait task` 挂起直到 future 完成并取得结果：

```penguin
fun work() -> i32 {
	wait 2 tick;
	return 42;
}

initial {
	let task: mut IFuture<i32> = async work();
	println("before wait");
	let a: i32 = wait task;
	println("wait done " + cast<string>(a));
}

initial {
	wait 1 tick;
	println("tick 1");
}
```

它总是按 `before wait`、`tick 1`、`wait done 42` 的顺序输出：`work` 挂起在时钟上，第二个例程推进到 tick 1，然后时钟推进到 tick 2、`work` 返回。

直接调用一个可挂起函数是隐式 wait——`bar()` 等价于 `wait async bar();`。裸 `wait;` 让出一个调度回合。调度器是**协作式单线程**的：作业运行到挂起点（`wait`、通道停靠）后切换到下一个作业。没有抢占，调度器之内没有数据竞争；值类型跨挂起点复制，引用类型由垃圾回收保持存活。

## 事件

事件是一等值（`Event<T>`）：可存储、可传递、可从任何地方发射。消费者是 wait 循环：

```penguin
let done : mut Event<void> = new Event<void>();

initial {
	println("working");
	done.emit(void);          // 广播给所有停靠在它上面的例程
}

initial {
	wait done;
	println("finished");
}
```

发射是**广播**：停靠在事件上的每个例程都收到值。无人停靠时发射的值会丢失（广播不是排队——每个值都必须保留时用 `Fifo` 通道）。`emit` 广播后让出一个调度回合，重新停靠的循环能跟上连续发射。

## 时序模型

所有例程共享一个以 **tick** 计的离散仿真时钟。调度器只当前一时刻无作业可推进时才推进时钟。`_sim_now()` 读取它：

```penguin
initial {
	wait 3 tick;
	println("c:" + cast<string>(_sim_now()));    // c:3
}
initial {
	wait 1 tick;
	println("a:" + cast<string>(_sim_now()));    // a:1
}
```

`wait n tick;`（或短形式 `wait n;`）挂起 `n` 个 tick。时长更短的先触发；时长相等按调度顺序触发。裸 `wait;` 停靠一个调度回合（一个 **delta**），不推进时钟——这是*零时间*的单位：`wait 0 tick;` 让当前时刻所有可运行作业结束后再恢复本例程。当所有例程停靠且无任何东西能唤醒任何作业时，程序到达**静默（quiescence）**并正常终止（退出码 0）。

`wait` 接受的不只是时长：

| 形式 | 唤醒条件 | 值 |
|---|---|---|
| `wait;` | 过一个调度回合 | — |
| `wait 5 tick;` | 时钟推进 5 tick | — |
| `wait a == 5;` | 条件成立（每回合重查） | — |
| `wait change(x);` | 被观察值与进入采样不同 | 新值 |
| `wait ev;` | 事件发射 | 送达的载荷 |
| `wait task;` | IFuture 完成 | 结果 |
| `wait this.port;` | 端口送达一个事务 | 送达值 |

## 模块：端口与 connect

带 `input`/`output` 端口声明的类是**模块**——跨例程的数据通道。模块在 `construct` 块中用 `connect` 连线，连线拓扑接受静态检查：

```penguin
class Incrementer {
	input x : i64;
	output y : i64;

	initial {
		while (true) {
			let v : i64 = wait this.x;   // 消费一个输入事务
			this.y.write(v + 1);         // 驱动输出
		}
	}
}

construct {
	let x : mut i64 = 1;                 // mut 变量成为连线网络
	let f1 : mut Incrementer = new Incrementer();
	let f2 : mut Incrementer = new Incrementer();
	connect(x, f1.x);                    // 变量 -> 输入
	connect(f1.y, f2.x);                 // 输出 -> 输入
}

initial {
	x = 2;                               // 每次赋值都驱动网络
	let out : i64 = wait f2.y;           // 4——2 被加了两次
	println(cast<string>(out));
}
```

规则是 RTL 严格风格，编译期检查：

* 模块内部：`input` 只读/只可 wait；`output` 用 `write`（或赋值糖 `this.y = v`）驱动。
* 模块外部：他人模块的 input 不可见；output 可读不可驱动。
* 每个 output 恰有一个驱动例程；每个 input 恰由一条 `connect` 馈送（一个源扇出到多个输入合法——每个消费者有自己的线）。
* `construct` 块在 elaboration 阶段、任何 `initial` 之前运行。`connect` 的源可以是输出端口、通道、事件、`mut` 变量（隐式网络，如上），或模块自身的 input（透传）；汇是 input 端口（或做扇入的 `MultiInput`）。

## 通道

通道是同时为事务源与汇的一等对象；缓冲**策略**由你选择的具体通道决定：

```penguin
let q : mut Fifo<i64> = new Fifo<i64>(2, new FifoPolicy.backpressure());

initial {                       // 生产者
	let i : mut i64 = 1;
	while (i <= 5) {
		q.write(i);
		println("w " + cast<string>(i));
		i += 1;
	}
}

initial {                       // 消费者
	let n : mut i64 = 0;
	while (n < 5) {
		let v : i64 = wait q;
		println("r " + cast<string>(v));
		n += 1;
	}
}
```

容量 2 的 `Fifo` 配 backpressure 策略：写满时挂起生产者（`w 1`、`w 2` 之后生产者等消费者读走才继续）——零流控代码的端到端流控。`LatestChannel<T>` 是线策略：每个调度回合一个沉淀值（用于电平/标志，不用于消息）。`close(ch)` 以运行时错误唤醒所有等待者——监督式停机的惯用法。

## MultiInput

`MultiInput<T>` 接受 N 条 connect——归并策略放在模块内部的扇入。`wait this.inputs` 轮转（round-robin）地从任意源取下一个事务：

```penguin
class Sink {
	inputs : mut MultiInput<i64> = new MultiInput<i64>();
	initial {
		while (true) {
			let v : i64 = wait this.inputs;
		}
	}
}
```

## 组织更大的程序

程序结构在软件层面遵循同样的思想：符号组织用**命名空间**与 `using`，多文件构建用 `.penguins` 工程文件，代码分发用（带 `export` 标记定义的）`.penguin-lib` 共享库——见[命名空间与工程](../specifications/08_NamespaceAndProject.md)规范。在端口/通道层之上还有 `libpenguin-esl`（`EmperorPenguin/others/libpenguin-esl/`），一个带两相位 `Clock`、`Reg`、`Pipe`、`Bus`、`Mem` 的 RTL 建模库——tinyriscv 测试台就是用它搭的。

## 接下来去哪

* [异步与时序模型规范](../specifications/09_AsyncAndTimingModel.md)
* [模块化编程规范](../specifications/10_ModularProgramming.md)
* 语言巡览见[基础入门](./BasicIntroduction.md)。
