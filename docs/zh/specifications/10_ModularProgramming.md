# 模块化编程

本章记录 RTL 风格的通信层——模块端口、`construct` 连线块、一等通道、广播事件与其下的调度语义（delta 回合、沉淀点、静默）——以及软件模块化层：`.penguin-lib` 共享库。词汇表刻意精简：

- **`wait` 是唯一的唤醒原语。** 它接受时长（`wait 5 tick`）、条件（`wait a == 5`）、边沿（`wait change(x)`——停靠到被观察值与进入采样不同，产出新值）、事件（`let v : T = wait ev`）、future（`let v : T = wait task`）或端口（`let v : T = wait this.x`）。每个带载荷的形式都返回送达值。
- **没有 `event`/`emit`/`on` 关键字。** 广播是一等值 `Event<T>`；订阅就是 wait 循环。没有 `poll`。
- **编译器拥有三样东西**：端口声明、`connect` 拓扑检查、调度/送达原语。每个缓冲、路由或仲裁*策略*都是库代码（实现 `ISource`/`ISink` 的通道）。

## 事件（匿名广播）

事件是普通值——存进全局、类字段或局部，传给自由函数，从任何地方发射：

```penguin
let clk : mut Event<i32> = new Event<i32>();

fun deep(c : mut Event<i32>) { c.emit(1); }    // 自由函数可以发射

initial {
    while (true) {
        let v : i32 = wait clk;
        println(cast<string>(v));
    }
}
```

void 载荷事件（`Event<void>`）不带值；用 void 字面量发射：`ev.emit(void);`，`wait ev;` 唤醒不返回任何东西。

语义——三条规则：

1. **广播**：停靠在事件上的每个例程都收到值。每个 `wait ev` 停靠在自己的单槽订阅上，N 个等待者看到 N 次送达。
2. **无人监听即丢失**：无人停靠时发射的值不复存在——广播不是排队。需要存储转发用 `Fifo` 通道。
3. **每次等待一个槽**：等待者重新停靠前的两次发射塌缩为最后一个值（连线语义）。`emit` 广播后让出一个 delta，等待者循环能跟上连续发射。

事件也可以**连进模块端口**——`connect(ev, f.x)` 为每个连接的输入授予一条由每次发射馈送的永久线，与输出端口扇出完全一致；停靠的 `wait ev` 订阅者与连接的线收到相同发射。每条连接的线有自己的送达游标：不同调度回合的发射各自（按序）送达每个消费者，同回合的发射塌缩为最终值。

## 端口

带端口声明的类是模块。端口是正式的跨例程数据通道：

```penguin
class Foo {
    input x : i64;
    output y : i64;

    initial {
        while (true) {
            let v : i64 = wait this.x;   // 消费一个输入事务
            this.y = v;                  // y.write(v) 的赋值糖
        }
    }
}
```

**权限矩阵（RTL 严格）：**

|              | 模块内       | 模块外 |
|--------------|-------------------------|--------------------|
| `input x`    | 只读 / 只可 `wait x`    | 不可读、不可写  |
| `output y`   | `y.write(v)` / `y = v`  | 只读          |

- 写输入、写他人输出、或从两处驱动输出都是编译错误——**每种语法形式**都拒绝：普通赋值（`this.x = v`）、方法调用（`this.x.write(v)`、`m.y.write(v)`）与复合赋值（`this.x += 1`）。驱动输入的唯一方式是 `connect`。两个*例程*写同一输出同样是编译错误（每个输出一个驱动例程；合流请用通道）。
- 读其他模块的 input——裸读（`m.x`）或 wait（`wait m.x`）——是编译错误；输入只在自己模块内可见。
- 端口不带 `mut` 修饰——矩阵已固定所有读写规则。
- 默认值与初值：端口的初值是其显式默认**或载荷类型的零值**。
  - `input x : i64 = 0;` 绑定常量源（默认是*电平*，裸读可读；从不送达事务）。
  - `output line : bool = true;`（UART 空闲电平情形）是弱可送达种子：首个 `wait line` 立即以它唤醒，真实写入取而代之。
  - *不带*默认的输出以类型零值作为仅当前种子——裸读确定性地返回它，但 `wait port` 停靠到模块真正写入。
- **拓扑静态检查**（`error[E_WIRING]`）：两个源连到同一输入、construct 实例化模块的无默认输入未连接、输出同时被体内代码*和* construct 线驱动，都是编译错误。*动态*实例化的模块（construct 之外的 `new`）跳过静态审计——未绑定输入在运行时优雅停靠（阻塞而非崩溃）。
- **裸端口读**（`let v : i64 = s.out;`）是**沉淀点**：先每次停靠一个 delta 直到当前仿真时间的传播沉淀（上一调度回合无事务活动且当前回合尚安静），再采样通道*当前槽*。经典例子——`x = 2; println(f2.y)`——输出传播后的 `2` 而不是过期槽。`construct` 块内读端口是编译错误（沉淀点是挂起，construct 不得等待）。`wait port` 消费下一个事务；`wait <condition>` 每个调度回合经沉淀读重求值（`wait s.line == false` 是 Verilog `wait()` 惯用法）。`wait change(port)` 是唯一例外：它每回合采样**原始电平**、不沉淀——边沿检测必须看到单回合脉冲。*普通变量*读取是对最新赋值的普通读取（见[异步与时序模型](./09_AsyncAndTimingModel.md)）。

## connect 与 construct

`connect(source, sink)` 连接拓扑；只在 `construct` 块内合法。顶层 `construct` 块在 elaboration 阶段、任何 `initial` 之前运行；类级 `construct` 在 `new` 时（构造器之后、实例 initial 启动之前）运行：

```penguin
construct {
    let x : mut i64 = 1;
    let f1 : mut Foo = new Foo();
    let f2 : mut Foo = new Foo();
    connect(x, f1.x);
    connect(f1.y, f2.x);
}

initial {
    x = 2;                             // 每次赋值同时驱动网络
    let out : i64 = wait f2.y;         // 2
}
```

- construct 块的 `let` 成为外围作用域的普通绑定（顶层）或留在连线块局部（类级）。
- **源**：输出端口、任何通道表达式、`Event`（每次发射经自己的永久线馈送输入——广播与连线共存）、`mut` 变量（隐式网络，见下）、或本模块自身的 input（透传，见下）。**汇**：输入端口（成员访问 `f.x` / `this.x`）或 `MultiInput`（端口字段或裸 `let mi` 绑定）。
- 一个输出连到 N 个输入即扇出：每个连接的输入得到*自己的*线与独立送达游标——每个消费者**按序收到每个事务、何时轮询何时收**；迟到的消费者不丢事务。同一调度回合内的多次写入塌缩为最终值（线每 delta 沉淀为一个值）。
- 输出**透传**组合层次：`connect(inner.y, this.y)` 使组合者的输出就是内部枢纽（一个驱动者、透明转发）。
- 输入**透传**（`connect(this.x, inner.x)`）配合较晚的外部连线：construct 把输入字段重绑为中继，*外部* connect（必然较晚）把真实源绑进中继——无需手写转发进程。v1 限制：每个输入一条透连线（透传输入的扇出不支持），并且同时*消费*该输入的模块体会与中继竞争事务。

### 隐式网络（`mut` 变量源）

`connect(x, f.x)` 中 `x` 是 `mut` 变量时，该变量变成连线网络：

- 顶层 construct `let`（要求显式 `mut T` 类型）与类字段（`connect(this.baud, inner.clk)`——共享配置寄存器形态）都支持；网络枢纽是按实例的隐藏字段。
- connect 时变量的值是网络的可送达种子（连接输入的首次 `wait` 以它唤醒；首次真实赋值取代之）；此后对该变量的每次赋值——普通、复合（`x += 1`）、来自任何例程——也写隐藏枢纽，连接的输入观察到新值。
- 网络读取保持普通变量读取（变量可见处任意读取当前值）。扇出与输出端口相同：每条 connect 订阅自己的线。

## 通道

通道是同时为事务源与汇的一等对象（`ISource<T>` + `ISink<T>`）。模块只看到接口；*策略*在连线所选的具体通道里：

| 通道          | 策略                                                     |
|------------------|------------------------------------------------------------|
| `Fifo<T>(cap, policy)` | 每个值都被送达、按序；`Backpressure` 满时挂起写者（零流控代码的端到端流控），`Drop` 满时丢弃 |
| `LatestChannel<T>` | 线：每个调度回合一个沉淀值——同回合写入塌缩为最终值，不同回合写入各自按序送达每个消费者游标 |
| `MultiInput<T>`  | 动态扇入——见下                                 |

通道可直接消费（`q.write(v)`、`let v : T = wait q;`、`q.try_poll() -> Option<T>`），也可作为 connect 的**源**馈送输入端口（`connect(q, f.x)`）。通道不是合法的 connect *汇*——汇端永远是输入端口或 `MultiInput`。

两个反转界定线与 FIFO 的领地：

- **线（`LatestChannel`）用于电平**——“最新值即真相”：状态标志、配置寄存器、握手线。它*不是*消息工具：同一调度回合写入的两帧塌缩成协议损坏（不同回合写入各自到达，但依赖这种时序是脆弱的——线契约是每回合一个沉淀值）。
- **FIFO 用于消息**——每条必须到达、恰好一次、按序的行、帧或命令。

每次写入都是一个事务——同一值写两次送达两次（没有 Verilog 式去重）。`write(v)` 可能挂起（背压）；`try_write(v) -> bool` 是非阻塞出口。输出上的 `y = v` 脱糖为 `y.write(v)`，因此输出赋值是潜在挂起点——推理调度时应与 `wait` 并列记录。

**close 是监督式停机**：`close(ch)` 以运行时错误（“channel closed”）唤醒所有等待中与未来的等待者；写已关闭的通道同样抛错。关闭一条顶层通道可拆掉仍等待它的整棵模块树——每个模块的 `try/catch` 决定那是否为有序停机（见 `ExceptionTest`）。

## MultiInput

`MultiInput<T>` 是接受 N 条 connect 的类端口对象——归并策略放在模块*内部*的扇入：

```penguin
class Sink {
    inputs : mut MultiInput<i64> = new MultiInput<i64>();
    initial {
        while (true) {
            let v : i64 = wait this.inputs;   // 任意源的下一个事务
        }
    }
}

construct {
    let s : mut Sink = new Sink();
    connect(producer_a.out, s.inputs);
    connect(producer_b.out, s.inputs);
}
```

`wait mi` 轮转扫描注册的源（忙源不会饿死其他源）。自定义策略——优先级、按源处理——迭代源视图并无阻塞探测：

```penguin
for (let src : mut ISource<i64> in this.inputs.iter()) {
    let v : Option<i64> = src.try_poll();
    ...
}
```

替代模式是*连线者持有的共享 Fifo*（多个生产者写入），策略留在连线端；`MultiInput` 把扇入契约自述在模块上。两者都合法。

## 库（.penguin-lib）

除单进程模块外，程序还可拆成共享库。`.penguin-lib` 是一个原生共享对象（`.so`/`.dll`），二进制后追加 JSON 符号表，以 `.penguin-lib` 输出名构建产出：

```bash
# 构建库（export 标记的定义成为其公共面）
emperor libsrc.penguin -o libfoo.penguin-lib

# 以它为编译目标编译消费者，然后把 exe+lib 链到一起
emperor app.penguin --lib libfoo.penguin-lib -o app
```

规则要点：

* **导出**：只有 `export` 标记的定义（见[命名空间与工程](./08_NamespaceAndProject.md)）加上其引用类型闭包、每个全局变量（由消费者重新初始化）、每个顶层 `impl X for Y` 边，以及含模板/元构造文件的**逐字源码**（让消费者能本地单态化新的泛型实例）进入库元数据。其余对 `.so` 私有。
* **声明不定义**：消费者针对库的声明编译，运行时调用 `.so`。库已搭载的特化被复用；新特化在消费者中实例化。
* **可重定位对**：可执行文件携带 C 运行时（与可选 JIT），经 `-rdynamic` 从自身绑定库的运行时符号，经 `$ORIGIN` rpath 找到旁边的 `.penguin-lib`——exe + `.penguin-lib` 对可整体移动。
* 库需要支持动态库的编译器（EmperorPenguin Pass3 及之后的构建）。完整元数据格式与消费管线见实现笔记（[EmperorPenguin Dynlib](../impl-notes/28_EmperorPenguinDynlib.md)）。

## 终止与病态拓扑

- **终止 = 所有 initial 例程结束 + 调度器静默。** 静默本身不是退出——停靠等待外部输入的服务器是设计中的合法终态——且自 LSP 运行时落地后该状态是真实的：`__builtin._fd_wait_read(fd)` / `_fd_wait_write(fd)` 把当前协程停靠在文件描述符就绪上（stdio 语言服务器的 stdin/stdout，任何非阻塞 fd）。只要有 fd 等待者停靠，调度器绝不在静默处退出——它在注册描述符上 `poll()` 阻塞，任一就绪注入下一个 delta 回合（EOF 唤醒读等待者；随后的 `_read_fd` 返回 `""`）。从不停靠在 fd 上的程序保持 v1 行为：死寂的程序就此结束。显式 `$finish` 用 `exit()`。
- 永不为真的条件（`wait a == 99`）贡献相同的调度器回合，与停靠的通道等待者一样在静默处结束。
- 零延迟振荡循环在一个仿真时间内消耗 delta 回合；调度器在回合预算后以活锁错误中止。收敛的组合环（无新事务）合法。
