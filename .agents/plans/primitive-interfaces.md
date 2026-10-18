# 基本类型接口化改造（primitive interfaces）

日期：2026-10-16 ｜ 分支：`feature/primitive-interfaces`（每阶段一个里程碑提交）
状态：已确认方向，待开工

## 目标

让基本类型（int 系、float、bool、char）与 `string` 获得编译期可见的接口能力，
完善 `Box<T>` 使其成为原始类型的真多态载体。三个需求：

1. **类型接口注册表（meta 表）**：编译期 + 运行时两侧建立基本类型的接口
   注册；静态已知类型的 `is` 检查编译期判定。
   例：`let a : i32 = 1; true == a is IHash;`
2. **接口 cast 的立即调用例外**：`cast<IHash>(a).get_hash()` 与隐式
   `a.get_hash()` 允许（编译为直连调用）；`let b : IHash = a;` 仍不允许。
   例外规则写入规范文档。
3. **Box 自动透传**：`Box<T>` 用 `#specializing` 遍历 T 的接口并自动生成
   转发 impl（排除运行时标记接口）。
   例：`let a : Box<i32> = new Box<i32>(1); a.get_hash();` 成功；
   `let b : IHash = new Box<i32>(1);` 作为装箱后的真多态同样成立。

## 已确认的决策（2026-10-16 与用户对齐）

| 决策点 | 结论 |
| --- | --- |
| BabyPenguin 同步 | **两侧同步实现**（EP penguin 语义层 + BP C# 语义层），测试 Apply To 全后端 |
| is 静态化覆盖 | **所有静态具体类型**（basic + string + 值类 + 枚举）；接口变量的 is 维持运行时 |
| meta 表位置 | **编译期 + 运行时**：BasicType 接口注册表（编译期）；core_builtin.c 为每个 primitive 定义静态 `EmperorClassMetadata`（运行时，铺路，interface_map 暂留空） |
| Box 透传排除的"特殊接口" | **运行时标记类**：ICopy、IReferenceType、IMemoryDispose、IUniqueMangleName、ISync |
| class 的 is 静态化 | 只折叠可判定为 **true** 的情形；false 不静态化（子类可能实现更多接口），维持运行时 |

## 现状与缺口（2026-10-16 调研）

- `impl IHash for <primitive>` / `impl IStringOps for string` 走特设直连通道
  （方法修饰为 `<name>$$<type>`，`k.method()` 编译期解析为直接调用），**不进
  vtable / interface_map 机器**（core_builtin.penguin:301-309, 624-632）。
- `is` 一律发射运行时 ISINSTANCE。**string 分歧实证**：`"abc" is IStringOps`
  在 BabyPenguin VM 为 `true`（impl 把 vtable 挂到 BasicTypeNode），原生为
  `false`（`_emperor_string_metadata` 的 interface_count 恒 0）。规范
  03_DataTypes.md:54 的 "do not use `is IStringOps`" 实际在掩盖该分歧。
- `cast<I>(primitive)` 语义层直接 `E_TYPE_MISMATCH`
  （`let x : IStringOps = s;` 实证，BP 报错于赋值检查）。
- `BoundType` 反射面有 `fields()/methods()/variants()`
  （EmperorPenguin/src/bound/BoundType.penguin:717-755），**缺 `interfaces()`**
  —— 需求 3 的前置缺口。
- `#specializing __builtin.Box<T>` 机制已存在并已有先例
  （core_builtin.penguin:461-469，`if (umangleable(T))` 门控生成
  IUniqueMangleName impl；`umangleable` 由编译器原生求值，
  SemanticBindMetaCalls.penguin:876-899）。
- Box 现状（core_builtin.penguin:103-109）：`class Box { value: T; impl
  IReferenceType; fun new(...) }` —— 无任何转发接口。
- BP 侧 BasicTypeNode 已是 IVTableContainer（string impl 挂 vtable 的先例）。

## 阶段划分

### 阶段 1：类型接口注册表（编译期 + 运行时 meta 表）

编译期（两侧）：
- EP：basic 类型的 `BoundType` 挂"已实现接口集合"，由 `impl I for <basic>`
  声明注册填充（BuildScopes/InterfacesPass 挂接点实现时定）。
- BP：`BasicTypeNode` 同样挂接口集（vtable 容器已在，补注册路径）。

运行时（原生 C）：
- core_builtin.c 为 i8/i16/i32/i64/u8/u16/u32/u64/f32/f64/bool/char 各定义
  静态 `EmperorClassMetadata`（`_emperor_i32_metadata` 式，命名与
  `_emperor_string_metadata` 一致），`-rdynamic` 导出。interface_map 留空
  （静态 is 化后暂无运行时消费者；为运行时元数据统一铺路）。

产出：双侧注册表 + C 元数据；`dotnet test` 全绿。

### 阶段 2：静态已知类型的 is 编译期判定

`expr is I`（I 为接口）且 expr 静态类型已知时：
- basic / string / 值类 / 枚举（无子类）：查注册表/impl 集，折叠为
  `true`/`false` 常量。顺带消除 string is 的 VM/原生分歧。
- class：仅 true 折叠（静态类型实现集含 I）；false 维持运行时 ISINSTANCE
  （子类可能实现更多接口）。
- 接口类型变量的 is：维持现状（运行时）。

产出：两侧语义层改造；`Tests/InterfaceTest/` 新用例：
`i32 is IHash`、`f64 is IHash`、`string is IStringOps`、值类双向、
class true-折叠与 false-运行时两分支、接口变量 is 不变。
规范 03_DataTypes.md:54 的禁令改写为"is 合法且编译期判定"。

### 阶段 3：接口 cast 的立即调用例外

- 语义层（两侧）：`cast<I>(e)`（I 接口、e 为 basic/string）仅当作为
  **成员调用的接收者**出现时，重写为直连调用（`$$type` impl 函数）；
  其余位置维持 `E_TYPE_MISMATCH`，报错信息明确
  "interface cast of a primitive is only allowed as the receiver of an
  immediate method call"。
- 隐式 `a.get_hash()` 已工作，补测试锁定。
- 文档：`docs/{en,zh}/specifications/03_DataTypes.md`（§IStringOps 附近）
  + 接口章节，双语写入例外规则：*原始类型与 string 的接口方法只支持直连
  调用与立即 cast 调用；接口类型的存储/传参不允许，需要多态时使用 Box*。

产出：两侧改造 + `cast<IHash>(a).get_hash()` 正确、
`cast<IHash>(a)` 赋值仍报错的正反用例 + 双语规范更新。

### 阶段 4：Box 自动透传接口

前置：`BoundType.interfaces()` 反射（EP penguin，镜像 fields()/methods()/
variants() 模式；BP 侧 C# MetaEngine 对应 API）。

核心：扩展 `#specializing __builtin.Box<T>`：
- 遍历 `T.interfaces()`，排除运行时标记接口
  （ICopy/IReferenceType/IMemoryDispose/IUniqueMangleName/ISync），
  对每个其余接口用 meta 生成转发 impl：
  `fun m(this, args...) -> R { return this.value.m(args...); }`
- T 无可透传接口时生成空集（门控仿照 `umangleable(T)`）。

已知风险点（实现中处理）：
1. `mut this` 接口方法转发要求 `Box.value` 可写（现 `value: T` 字段定义
   可能需调整为 `mut`，注意 Box 自身 IReferenceType 语义不受影响）。
2. BP 侧 `#specializing` 由 C# MetaEngine 执行，动态生成 def 的能力边界
   需实测；不通则 BP 用等价 C# 钩子实现并在 Builtin.penguin 镜像声明。
3. `interfaces()` 对递归泛型（`Box<Box<i32>>`）必须终止。
4. 转发方法的泛型参数/返回值签名拼接要与 InterfacesPass 的 vtable 构建兼容。

产出：`Box<i32>` 的 `a.get_hash()` 直连；`let b : IHash = new Box<i32>(1)`
接口变量真多态；`Box<string>` 的 IStringOps；嵌套 Box；排除接口不透传
（`Box<X>` 不因 X 实现 ICopy 而变值语义）等用例。

## 验证

每阶段：`dotnet test`（BP+EP 单测）→ 相关 Tests/ 类别 → 全量套件。
收尾：`make bootstrap` 重收敛（EP 语义层源变）+ `make docs-site` 双语构建
+ C 运行时改动验 `release_win`（若 core_builtin.c 变更）。
每个 bug 的最小复现按仓库规矩落成 `Tests/*.md`。

## 工作量预估

阶段 4（反射 + meta 生成 + 双侧）> 阶段 2（双侧语义 + class 不对称）>
阶段 3 > 阶段 1。
