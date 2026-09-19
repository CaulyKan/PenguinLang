# 枚举

Penguin-lang 支持 Rust 风格的枚举：一组命名变体，每个可选携带载荷。最常用的枚举类型之一是 `Option<T>`，可为 `some` 或 `none`。

带泛型参数的枚举用 `#template` 声明（见 [Class](./05_Class.md)）：
```
#template(T: type)
enum Option {
	some: T;
	none;
}

let a : mut Option<i32> = new Option<i32>.none();
initial {
	a = new Option<i32>.some(1);
}
```

## 构造枚举值
枚举值总是用 `new` 构造，无载荷变体也带括号：
```
let a = new Option<i32>.some(1);
let b = new Option<i32>.none();
```

## 检查枚举
Penguin-lang 支持用 'is' 关键字检查枚举是否匹配某变体。用变体名读取载荷，例如 `a.some`。
```
initial {
	let a = new Option<i32>.some(1);
	if (a is Option<i32>.some) {
		println("a is some(" + cast<string>(a.some) + ")");
	} else if (a is Option<i32>.none) {
		println("a is none");
	}
}
```
比较两个枚举值是否相等时，把两者都转换为 `string`：
```
if (cast<string>(a) == cast<string>(b)) { ... }
```

## 枚举成员
与类一样，枚举可以有自己的成员：
```
#template(T: type)
enum Option {
	some: T;
	none;

	// 方法
	fun value_or(this, default_val: T) -> T {
		if (this.is_none()) {
			return default_val;
		} else {
			return this.some;
		}
	}

	// 但枚举不能有构造器
}
```

## 分类
枚举是**值类型**：赋值复制整个带标签联合（标签 + 载荷）。载荷字段为引用类型时仍共享被引用对象。枚举可实现接口（枚举同样支持虚表分派，见[接口](./07_Interface.md)）。
