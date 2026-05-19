using BabyPenguin;

namespace EmperorPenguin.Tests;

public class BoundTreeInterfaceImplTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitBoundBatch<BoundTreeInterfaceImplTest>());

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""interface IGreeter { fun greet(); } class Bar { impl IGreeter { fun greet() {} } }"");
    let def1 = result.definitions.at(cast<u64>(1)).some;
    if (def1 is bound.BoundDefinition.class_def) {
        let cls = def1.class_def;
        if (cast<i64>(cls.vtables.size()) >= 1) {
            let vt = cls.vtables.at(cast<u64>(0)).some;
            println(""vtable_count="" + cast<string>(cast<i64>(cls.vtables.size())));
            println(""slot_count="" + cast<string>(cast<i64>(vt.slots.size())));
            if (cast<i64>(vt.slots.size()) >= 1) {
                let slot = vt.slots.at(cast<u64>(0)).some;
                if (slot.interface_method.is_some()) {
                    println(""iface_method_name="" + slot.interface_method.some.name);
                    println(""iface_method_full="" + slot.interface_method.some.full_name);
                }
                if (slot.implementation_method.is_some()) {
                    println(""impl_method_name="" + slot.implementation_method.some.name);
                    println(""impl_method_full="" + slot.implementation_method.some.full_name);
                }
            }
        }
    }
}
", "vtable_count=1\nslot_count=1\niface_method_name=greet\niface_method_full=<global>.IGreeter.greet\nimpl_method_name=greet\nimpl_method_full=<global>.Bar.greet")]
    public void ClassImplInterfaceVTableSlotTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""interface ISerializable { fun serialize(); } class Data {} impl ISerializable for Data { fun serialize() {} }"");
    let def_impl_for = result.definitions.at(cast<u64>(2)).some;
    if (def_impl_for is bound.BoundDefinition.impl_for_def) {
        let impl_for = def_impl_for.impl_for_def;
        if (impl_for.vtable.is_some()) {
            let vt = impl_for.vtable.some;
            println(""vtable_slot_count="" + cast<string>(cast<i64>(vt.slots.size())));
            if (cast<i64>(vt.slots.size()) >= 1) {
                let slot = vt.slots.at(cast<u64>(0)).some;
                if (slot.interface_method.is_some()) {
                    println(""iface_method_name="" + slot.interface_method.some.name);
                    println(""iface_method_full="" + slot.interface_method.some.full_name);
                }
                if (slot.implementation_method.is_some()) {
                    println(""impl_method_name="" + slot.implementation_method.some.name);
                    println(""impl_method_full="" + slot.implementation_method.some.full_name);
                }
            }
        }
    }
}
", "vtable_slot_count=1\niface_method_name=serialize\niface_method_full=<global>.ISerializable.serialize\nimpl_method_name=serialize\nimpl_method_full=<global>.serialize")]
    public void ImplForVTableSlotSourceTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""interface IRunnable { fun run(); } class Worker {} impl IRunnable for Worker { fun run() {} }"");
    let def_impl_for = result.definitions.at(cast<u64>(2)).some;
    if (def_impl_for is bound.BoundDefinition.impl_for_def) {
        let impl_for = def_impl_for.impl_for_def;
        println(""iface_type_kind="" + cast<string>(impl_for.interface_type.kind));
        println(""for_type_kind="" + cast<string>(impl_for.for_type.kind));
    }
}
", "iface_type_kind=InterfaceKind\nfor_type_kind=ClassKind")]
    public void ImplForTypesResolvedTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Baz {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        println(""vtable_count="" + cast<string>(cast<i64>(cls.vtables.size())));
        println(""impl_count="" + cast<string>(cast<i64>(cls.interface_impls.size())));
    }
}
", "vtable_count=0\nimpl_count=0")]
    public void ClassImplInterfaceCountTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""interface ICombo { fun method_a(); fun method_b(); } class Cls { impl ICombo { fun method_a() {} fun method_b() {} } }"");
    let def1 = result.definitions.at(cast<u64>(1)).some;
    if (def1 is bound.BoundDefinition.class_def) {
        let cls = def1.class_def;
        if (cast<i64>(cls.vtables.size()) >= 1) {
            let vt = cls.vtables.at(cast<u64>(0)).some;
            println(""slot_count="" + cast<string>(cast<i64>(vt.slots.size())));
            if (cast<i64>(vt.slots.size()) >= 2) {
                let slot0 = vt.slots.at(cast<u64>(0)).some;
                let slot1 = vt.slots.at(cast<u64>(1)).some;
                if (slot0.interface_method.is_some()) {
                    println(""s0_iface="" + slot0.interface_method.some.full_name);
                }
                if (slot0.implementation_method.is_some()) {
                    println(""s0_impl="" + slot0.implementation_method.some.full_name);
                }
                if (slot1.interface_method.is_some()) {
                    println(""s1_iface="" + slot1.interface_method.some.full_name);
                }
                if (slot1.implementation_method.is_some()) {
                    println(""s1_impl="" + slot1.implementation_method.some.full_name);
                }
            }
        }
    }
}
", "slot_count=2\ns0_iface=<global>.ICombo.method_a\ns0_impl=<global>.Cls.method_a\ns1_iface=<global>.ICombo.method_b\ns1_impl=<global>.Cls.method_b")]
    public void ClassImplInterfaceWithMultipleMethodsTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""namespace App { interface IFoo { fun do_it(); } class FooImpl {} impl IFoo for FooImpl { fun do_it() {} } }"");
    let ns = result.definitions.at(cast<u64>(0)).some;
    if (ns is bound.BoundDefinition.namespace_def) {
        let impl_for_def = ns.namespace_def.children.at(cast<u64>(2)).some;
        if (impl_for_def is bound.BoundDefinition.impl_for_def) {
            let impl_for = impl_for_def.impl_for_def;
            if (impl_for.vtable.is_some()) {
                let vt = impl_for.vtable.some;
                if (cast<i64>(vt.slots.size()) >= 1) {
                    let slot = vt.slots.at(cast<u64>(0)).some;
                    if (slot.interface_method.is_some()) {
                        println(""iface_method_full="" + slot.interface_method.some.full_name);
                    }
                    if (slot.implementation_method.is_some()) {
                        println(""impl_method_full="" + slot.implementation_method.some.full_name);
                    }
                }
            }
        }
    }
}
", "iface_method_full=<global>.App.IFoo.do_it\nimpl_method_full=<global>.App.do_it")]
    public void ImplForInNamespaceSlotSourceTest() => _batch.Value.Assert();
}
