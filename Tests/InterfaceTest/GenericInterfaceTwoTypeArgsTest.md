# GenericInterfaceTwoTypeArgsTest
## Description
A generic interface with two type parameters (K, V), specialized to
IAssoc<u8, u8> and called. The implementing class provides key() -> K and
value() -> V, downcasting `this` to access concrete fields.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        #template(K: type, V: type)
        interface IAssoc {
            fun key(this: IAssoc<K, V>) -> K;
            fun value(this: IAssoc<K, V>) -> V;
        }

        class Pair {
            k: u8 = 5;
            v: u8 = 7;
            impl IAssoc<u8, u8> {
                fun key(this: IAssoc<u8, u8>) -> u8 {
                    let s : Pair = cast<Pair>(this);
                    return s.k;
                }
                fun value(this: IAssoc<u8, u8>) -> u8 {
                    let s : Pair = cast<Pair>(this);
                    return s.v;
                }
            }
        }

        initial {
            let p : Pair = new Pair();
            let a : IAssoc<u8, u8> = cast<IAssoc<u8, u8>>(p);
            print(cast<string>(a.key()));
            print(cast<string>(a.value()));
        }
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `57`
ExpectedStderr: DISCARD
