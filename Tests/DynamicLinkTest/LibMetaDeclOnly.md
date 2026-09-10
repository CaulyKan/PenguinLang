# LibMetaDeclOnly
## Description
libmeta v1 declaration materialization: a lib whose sources contain NO template/meta constructs ships as a STRUCTURED SYMBOL TABLE only (no embedded source). The consumer materializes bodyless declarations (`class ... { fields; fun ...; }`, `enum`, `interface`, `impl ... for ...`, globals with initializer text) from the table — every def marked lib-export, bodies linked from the .so at runtime. Covers: exported value class with an explicit ICopy impl-for (layout must classify identically on both sides), an exported enum with payload + method, an exported interface + virtual dispatch across the boundary, a lib global re-initialized in the consumer (GOT interposition), and a NON-exported helper that must not leak into the metadata. Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace shop {
    let tax_rate: i64 = 7;

    export class Item {
        name: string;
        price_cents: mut i64;

        fun new(mut this, name: string, price_cents: i64) {
            this.name = name;
            this.price_cents = price_cents;
        }

        fun price_with_tax(this) -> i64 {
            return this.price_cents + tax_rate;
        }

        fun copy(this) -> shop.Item {
            return new shop.Item(this.name, this.price_cents);
        }
    }

    impl __builtin.ICopy<shop.Item> for shop.Item;

    export enum Tag {
        none;
        promo: string;
    }

    export interface Priced {
        fun total(this) -> i64;
    }

    export class Basket {
        a: Item;
        b: Item;
        has_b: bool;

        fun new(mut this, a: Item) {
            this.a = a;
            this.has_b = false;
        }

        fun add(mut this, item: Item) {
            this.b = item;
            this.has_b = true;
        }

        fun total(this) -> i64 {
            let sum: mut i64 = this.a.price_with_tax();
            if (this.has_b) { sum = sum + this.b.price_with_tax(); }
            return sum;
        }

        impl shop.Priced {
            fun total(this) -> i64 {
                let sum: mut i64 = this.a.price_with_tax();
                if (this.has_b) { sum = sum + this.b.price_with_tax(); }
                return sum;
            }
        }
    }

    // NOT exported: private helper — must not appear in the metadata at all.
    fun secret_helper() -> i64 { return 999; }

    export fun make_tag(code: string) -> Tag {
        return new Tag.promo(code);
    }
}
```
## Build 1
Kind: lib
Name: shop.penguin-lib
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    let b: mut shop.Basket = new shop.Basket(new shop.Item("apple", 100));
    b.add(new shop.Item("pear", 200));
    // direct method calls (declare-not-define -> linked into the .so)
    println("total=" + cast<string>(b.total()));
    // value-class copy over the boundary (ICopy impl-for materialized as `impl ... for ...;`)
    let copy: shop.Item = new shop.Item("plum", 10).copy();
    println("copy=" + copy.name + ":" + cast<string>(copy.price_with_tax()));
    // enum with payload built by a lib function, matched in the consumer
    let t: shop.Tag = shop.make_tag("SAVE");
    if (let p := t.promo) {
        println("tag=" + p);
    }
    // interface dispatch across the boundary (vtable rebuilt from decls)
    let priced: shop.Priced = b;
    println("priced=" + cast<string>(priced.total()));
}
```
## Build 2
Args: `--lib ${WORKDIR}/shop.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `total=314
copy=plum:17
tag=SAVE
priced=314
`
ExpectedStderr: DISCARD
