# NamespacedRecursiveLinkedList
## Description
A generic linked list inside a namespace whose node class is auto-classified
(the node's only fields are a value and an enum link whose payload is the node
itself — a value-layout cycle, so the classifier must settle the node as a
REFERENCE type). Guards the specialized field/payload substitution in
Monomorphize/ResolveTypes: a specialized class field or enum payload typed
`Sibling<T>` used to stay in template-with-args form, which made classification
skip the variant-payload cycle check (false E_SIZE_CYCLE), made byte_size()
size the template graph while the emitter laid out the specialized one, and
diverged List slot strides from emitted struct layouts.

Known red on EmperorPenguin (all passes, verified 2026-08-18): the compile now
passes semantics (node = ref) but LLVMEmitter.emit_new finds no layout for the
specialized node under its namespaced name and emits only a `; NEW ... (no
layout)` comment — the constructor writes through an undefined register and
clang rejects the module (`use of undefined value '%t11'`). BabyPenguin (the
reference) compiles and runs it correctly; this sentinel should turn green
once the emitter's NEW path resolves namespaced specialized layouts.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace ll {
    #template(T: type)
    class LinkedList {
        head: LinkedListNode<T>;
        tail: LinkedListNode<T>;
        size: i32 = 0;

        fun add(this: mut LinkedList<T>, v: T) {
            if (this.size == 0) {
               this.head = new LinkedListNode<T>();
               this.head.value = v;
               this.head.link = new LinkedListLink<T>.end();
               this.tail = this.head;
            } else {
               let newNode : mut LinkedListNode<T> = new LinkedListNode<T>();
               newNode.value = v;
               newNode.link = new LinkedListLink<T>.end();
               this.tail.link = new LinkedListLink<T>.next(newNode);
               this.tail = newNode;
            }
            this.size += 1;
        }

        fun print_all(this: LinkedList<T>) {
            if (this.size > 0) {
                let current : mut LinkedListNode<T> = this.head;
                while (true) {
                    print(current.value);
                    if (current.link is LinkedListLink<T>.end) {
                        break;
                    } else {
                        current = current.link.next;
                        print(",");
                    }
                }
            }
        }
    }

    #template(T: type)
    enum LinkedListLink {
        end;
        next: auto LinkedListNode<T>;
    }

    #template(T: type)
    class LinkedListNode {
        value: auto T;
        link: mut LinkedListLink<T> = new LinkedListLink<T>.end();
    }

    initial {
        let ll : mut LinkedList<i32> = new LinkedList<i32>();
        ll.add(1);
        ll.add(2);
        ll.add(3);
        ll.print_all();
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
ExpectedStdout: EQUALS `1,2,3`
ExpectedStderr: DISCARD
