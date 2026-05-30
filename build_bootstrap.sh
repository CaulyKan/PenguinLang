#!/bin/bash
# build_bootstrap.sh — Attempt to compile EmperorPenguin with itself via BabyPenguin VM
# This script runs the EmperorPenguin compiler (on BabyPenguin VM) on all its own source
# files, producing LLVM IR, then links with the C runtime to produce a native executable.
#
# Usage: ./build_bootstrap.sh
# The script will stop at the first error, allowing iterative fixing.

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

EP_DIR="EmperorPenguin"
TMP_DIR="tmp_bootstrap"

echo "=== EmperorPenguin Self-Bootstrapping Build ==="
echo ""

# Step 1: Clean up previous attempt
echo "[1/4] Cleaning previous build..."
rm -rf "$TMP_DIR"
mkdir -p "$TMP_DIR"

# Step 2: Build C runtime
echo "[2/4] Building C runtime..."
make -C "$EP_DIR/std/c" clean > /dev/null 2>&1 || true
make -C "$EP_DIR/std/c" OUTPUT_DIR="$SCRIPT_DIR/$TMP_DIR"
echo "      -> $TMP_DIR/libcore_builtin.a"
echo ""

# Step 3: Run EmperorPenguin compiler on all its own source files
echo "[3/4] Compiling EmperorPenguin sources with BabyPenguin VM..."

# Collect all source files in dependency order
SOURCES=(
    "$EP_DIR/src/ast/Token.penguin"
    "$EP_DIR/src/ir/IRSourceLocation.penguin"
    "$EP_DIR/src/ir/IRValue.penguin"
    "$EP_DIR/src/ir/IRFunction.penguin"
    "$EP_DIR/src/ir/IRModule.penguin"
    "$EP_DIR/src/ir/IRInstruction.penguin"
    "$EP_DIR/src/ir/IRBuilder.penguin"
    "$EP_DIR/src/bound/BoundType.penguin"
    "$EP_DIR/src/bound/BoundSymbol.penguin"
    "$EP_DIR/src/bound/BoundScope.penguin"
    "$EP_DIR/src/bound/BoundCompilationUnit.penguin"
    "$EP_DIR/src/bound/BoundDefinition.penguin"
    "$EP_DIR/src/bound/BoundExpression.penguin"
    "$EP_DIR/src/bound/BoundStatement.penguin"
    "$EP_DIR/src/bound/BoundTypeRegistry.penguin"
    "$EP_DIR/src/bound/BoundTreePrinter.penguin"
    "$EP_DIR/src/ast/AST.penguin"
    "$EP_DIR/src/ast/Lexer.penguin"
    "$EP_DIR/src/ast/Parser.penguin"
    "$EP_DIR/src/ir/IRPrinter.penguin"
    "$EP_DIR/src/ir/IRGenerator.penguin"
    "$EP_DIR/src/bound/EmperorPenguinCompiler.penguin"
    "$EP_DIR/src/bound/SemanticModel.penguin"
    "$EP_DIR/src/llvm/LLVMEmitter.penguin"
    "$EP_DIR/main.penguin"
)

# Run the compiler with all source files
# The EmperorPenguin main.penguin already handles multi-file compilation + linking
dotnet run --project BabyPenguin -- "${SOURCES[@]}" 2>&1 | tee "$TMP_DIR/bootstrap.log" || {
    echo ""
    echo "=== COMPILATION FAILED ==="
    echo "Check $TMP_DIR/bootstrap.log for details."
    echo ""
    # Try to show the most relevant error
    grep -i "error\|failed\|exception" "$TMP_DIR/bootstrap.log" | tail -20 || true
    exit 1
}

echo ""

# Step 4: Check if we got an executable
echo "[4/4] Verifying output..."
if [ -f "$TMP_DIR/out.exe" ]; then
    echo "SUCCESS: Native executable at $TMP_DIR/out.exe"
    ls -la "$TMP_DIR/out.exe"
elif [ -f "tmp/out.exe" ]; then
    echo "SUCCESS: Native executable at tmp/out.exe"
    ls -la "tmp/out.exe"
else
    echo "WARNING: No executable found. Check $TMP_DIR/bootstrap.log for errors."
    echo ""
    echo "Generated .ll files:"
    find "$TMP_DIR" "tmp" -name "*.ll" 2>/dev/null | sort || true
fi

echo ""
echo "=== Build Complete ==="
