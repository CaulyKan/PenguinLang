namespace PenguinLangParser;

/// <summary>Category-level error codes shared by BabyPenguin and EmperorPenguin.</summary>
public enum ErrorCode
{
    // ── Resolution errors ──
    E_RESOLVE_SYMBOL,      // Undefined identifier, member, or 'this'
    E_RESOLVE_TYPE,        // Undefined type/interface name
    E_RESOLVE_NAMESPACE,   // Namespace not found

    // ── Duplicate declarations ──
    E_DUPLICATE_SYMBOL,    // Class/func/enum/iface/symbol already exists
    E_DUPLICATE_PARAM,     // Duplicate parameter name

    // ── Type errors ──
    E_TYPE_MISMATCH,       // Assignment/incompatible/branch-differ
    E_RETURN_TYPE_MISMATCH,// Return statement type mismatch
    E_CAST_INVALID,        // Cast not valid
    E_MUTABILITY,          // Mutability violation
    E_TYPE_INFERENCE,      // Type inference failure

    // ── Generics ──
    E_GENERIC_ARITY,       // Wrong generic arguments count
    E_GENERIC_SPECIALIZE,  // Cannot specialize

    // ── Interface / value-type ──
    E_UNSIZED_INTERFACE,   // Using interface as field without Box
    E_VALUE_TYPE_CONFLICT, // Class implements both ICopy and IRef
    E_INTERFACE_IMPL,      // Missing/mismatched interface method impl

    // ── Control flow ──
    E_COND_NOT_BOOL,       // if/while condition not bool
    E_LOOP_CONTEXT,        // break/continue outside loop
    E_RETURN_MISSING,      // Non-void function missing return path

    // ── Call errors ──
    E_CALL_NOT_FUNCTION,   // Called something not a function
    E_CALL_ARITY,          // Wrong number of arguments
    E_CALL_CONVENTION,     // Free function called via receiver or vice versa

    // ── Constructor / new ──
    E_NO_CONSTRUCTOR,      // Can't find constructor
    E_NEW_INTERFACE,       // Can't create instance of interface

    // ── Enum ──
    E_ENUM_VARIANT,        // Enum variant error (name, arity)

    // ── Iterator / yield ──
    E_ITERATOR_INVALID,    // For-loop iterator error
    E_YIELD_CONTEXT,       // Yield outside generator / return mismatch

    // ── Event ──
    E_EVENT_INVALID,       // Event/emit/signal error

    // ── Async / coroutine ──
    E_ASYNC_INVALID,       // Async/fork/wait error

    // ── Parse ──
    E_PARSE,               // Parser/lexer syntax error

    // ── Project / CLI ──
    E_PROJECT_FILE,        // Project file not found or parse error

    // ── Builtin / internal ──
    E_BUILTIN_MISSING,     // Builtin namespace or symbol missing
    E_UNSUPPORTED,         // Known-unsupported feature (for-loops, etc.)
    E_INTERNAL,            // Compiler invariant violation (shouldn't happen)

    // ── Runtime errors ──
    E_RUNTIME_TYPE,        // Runtime type mismatch (assign/cast)
    E_RUNTIME_LOOKUP,      // Runtime function/field lookup failed
    E_RUNTIME_INVALID_OP,  // Runtime invalid operation
}
