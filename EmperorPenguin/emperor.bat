@echo off
rem ============================================================================
rem emperor.bat — external driver for the EmperorPenguin compiler (Windows).
rem
rem Mirror of the `emperor` bash driver. The compiler binary
rem (emperor_penguin_llvmir_emitter.exe) only emits platform-independent LLVM
rem IR (.ll files); this script owns the LLVM environment checks, the C
rem runtime build (make -C EmperorPenguin/std/c) and the final clang link.
rem
rem Usage:
rem   emperor.bat [--emitter <path>] <src...> [flags] -o <out>
rem   emperor.bat link <file.ll> -o <out> [-enable-meta] [-llvm-win <dir>]
rem                   [--consumer-lib <so>]...
rem   emperor.bat link-lib <file.ll> <file.libmeta> -o <out.penguin-lib>
rem
rem Requires make + clang + llvm-ar on PATH (MSYS2-style environment).
rem ============================================================================
setlocal enabledelayedexpansion
set "SCRIPT_DIR=%~dp0"
set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
set "CALLER_CWD=%CD%"
if defined CLANG ( set "SEL_CLANG=%CLANG%" ) else set "SEL_CLANG=clang"

rem ── locate the PenguinLang tree (dir containing EmperorPenguin\) ─────
set "ROOT="
if defined EMPEROR_PENGUIN_ROOT if exist "!EMPEROR_PENGUIN_ROOT!\EmperorPenguin\std" set "ROOT=!EMPEROR_PENGUIN_ROOT!"
if not defined ROOT if exist "%SCRIPT_DIR%\EmperorPenguin\std" set "ROOT=%SCRIPT_DIR%"
if not defined ROOT if exist "%SCRIPT_DIR%\..\EmperorPenguin\std" set "ROOT=%SCRIPT_DIR%\.."
if not defined ROOT (
    echo [emperor] error: cannot locate EmperorPenguin\std relative to %SCRIPT_DIR%; set EMPEROR_PENGUIN_ROOT >&2
    exit /b 1
)
for %%i in ("%ROOT%") do set "ROOT=%%~fi"

rem ── Windows LLVM for the meta JIT: -llvm-win > LLVM_WIN_PREFIX > vendored
set "LLVMWIN="
if defined LLVM_WIN_PREFIX set "LLVMWIN=!LLVM_WIN_PREFIX!"
if not defined LLVMWIN if exist "%ROOT%\thirdparty\mingw-w64-x86_64-llvm-libs\lib\libLLVM-22.dll.a" set "LLVMWIN=%ROOT%\thirdparty\mingw-w64-x86_64-llvm-libs"

rem ── options ───────────────────────────────────────────────────────────
set "MODE=full"
set "OUT="
set "META=0"
set "TARGET="
set "OPT_EMITTER="
set "EM_ARGS="
set "LIBS="
set "POS_COUNT=0"
set "POS1="
set "POS2="

if /i "%~1"=="link" ( set "MODE=link" & shift & goto parse_opts )
if /i "%~1"=="link-lib" ( set "MODE=link-lib" & shift & goto parse_opts )
goto parse_full

:parse_opts
if "%~1"=="" goto opts_done
if /i "%~1"=="-o"            ( set "OUT=%~2" & shift & shift & goto parse_opts )
if /i "%~1"=="--output"      ( set "OUT=%~2" & shift & shift & goto parse_opts )
if /i "%~1"=="-enable-meta"  ( set "META=1" & shift & goto parse_opts )
if /i "%~1"=="--enable-meta" ( set "META=1" & shift & goto parse_opts )
if /i "%~1"=="-target"       ( set "TARGET=%~2" & shift & shift & goto parse_opts )
if /i "%~1"=="--target"      ( set "TARGET=%~2" & shift & shift & goto parse_opts )
if /i "%~1"=="-llvm-win"     ( call :set_llvmwin "%~2" & shift & shift & goto parse_opts )
if /i "%~1"=="--llvm-win"    ( call :set_llvmwin "%~2" & shift & shift & goto parse_opts )
set "A=%~1"
if "!A:~0,8!"=="-target="   ( set "TARGET=!A:~8!" & shift & goto parse_opts )
if "!A:~0,9!"=="--target="  ( set "TARGET=!A:~9!" & shift & goto parse_opts )
if "!A:~0,10!"=="-llvm-win="  ( call :set_llvmwin "!A:~10!" & shift & goto parse_opts )
if "!A:~0,11!"=="--llvm-win=" ( call :set_llvmwin "!A:~11!" & shift & goto parse_opts )
if /i "%~1"=="--consumer-lib" ( call :add_lib "%~2" & shift & shift & goto parse_opts )
if /i "%~1"=="--emitter"    ( set "OPT_EMITTER=%~2" & shift & shift & goto parse_opts )
set "A0=!A:~0,1!"
if "!A0!"=="-" (
    echo [emperor] error: unknown option: %~1 >&2
    exit /b 1
)
set /a POS_COUNT+=1
if !POS_COUNT!==1 set "POS1=%~1"
if !POS_COUNT!==2 set "POS2=%~1"
shift
goto parse_opts

:parse_full
if "%~1"=="" goto opts_done
if /i "%~1"=="-o"            ( set "OUT=%~2" & shift & shift & goto parse_full )
if /i "%~1"=="--output"      ( set "OUT=%~2" & shift & shift & goto parse_full )
if /i "%~1"=="-enable-meta"  ( set "META=1" & shift & goto parse_full )
if /i "%~1"=="--enable-meta" ( set "META=1" & shift & goto parse_full )
if /i "%~1"=="-target"       ( set "TARGET=%~2" & shift & shift & goto parse_full )
if /i "%~1"=="--target"      ( set "TARGET=%~2" & shift & shift & goto parse_full )
if /i "%~1"=="-llvm-win"     ( call :set_llvmwin "%~2" & shift & shift & goto parse_full )
if /i "%~1"=="--llvm-win"    ( call :set_llvmwin "%~2" & shift & shift & goto parse_full )
set "A=%~1"
if "!A:~0,8!"=="-target="   ( set "TARGET=!A:~8!" & shift & goto parse_full )
if "!A:~0,9!"=="--target="  ( set "TARGET=!A:~9!" & shift & goto parse_full )
if "!A:~0,10!"=="-llvm-win="  ( call :set_llvmwin "!A:~10!" & shift & goto parse_full )
if "!A:~0,11!"=="--llvm-win=" ( call :set_llvmwin "!A:~11!" & shift & goto parse_full )
if /i "%~1"=="--lib" ( call :add_lib "%~2" & set "EM_ARGS=!EM_ARGS! --lib !LP_Q!" & shift & shift & goto parse_full )
if /i "%~1"=="-lib"  ( shift & goto parse_full_relib )
if /i "%~1"=="--emitter"    ( set "OPT_EMITTER=%~2" & shift & shift & goto parse_full )
rem Everything else (-vv, --enable-coroutine, --disable-dl, sources, ...) is
rem forwarded to the emitter verbatim; source paths become absolute.
set "SA=%~1"
set "SA0=!SA:~0,1!"
if "!SA0!"=="-" ( set "EM_ARGS=!EM_ARGS! "!SA!"" ) else call :abs_sa
shift
goto parse_full
:parse_full_relib
rem -lib (single dash) takes a value like --lib
call :add_lib "%~1"
set "EM_ARGS=!EM_ARGS! --lib !LP_Q!"
shift
goto parse_full

:add_lib
rem Absolutize %1 against the caller cwd; !LP! = path, !LP_Q! = quoted
set "LP=%~1"
call :abs_path LP
set "LP_Q="!LP!""
set "LIBS=!LIBS! !LP_Q!"
goto :eof

:set_llvmwin
set "LLVMWIN=%~1"
goto :eof

:abs_sa
if not "!SA:~1,2!"==":\" if not "!SA:~1,2!"==":/" set "SA=!CALLER_CWD!\!SA!"
set "EM_ARGS=!EM_ARGS! "!SA!""
goto :eof

:abs_path
rem %1 = name of variable to absolutize against CALLER_CWD
call set "AP=%%%~1%%"
set "AP2=!AP:~1,2!"
if not "!AP2!"==":\" if not "!AP2!"==":/" set "AP=!CALLER_CWD!\!AP!"
set "%~1=!AP!"
goto :eof

:opts_done
set "CLANG=!SEL_CLANG!"

rem ── emitter resolution (full mode only) ──────────────────────────────
if "%MODE%"=="full" (
    if defined OPT_EMITTER ( set "EMITTER=!OPT_EMITTER!" ) else if defined EMPEROR_EMITTER ( set "EMITTER=!EMPEROR_EMITTER!" ) else if exist "%SCRIPT_DIR%\emperor_penguin_llvmir_emitter.exe" ( set "EMITTER=%SCRIPT_DIR%\emperor_penguin_llvmir_emitter.exe" ) else (
        echo [emperor] error: no emitter found: pass --emitter, set EMPEROR_EMITTER, or place emperor_penguin_llvmir_emitter.exe beside this script >&2
        exit /b 1
    )
)

rem A meta-linked emitter loads libLLVM-22.dll at process start; the vendored
rem DLLs live in <llvm-win>\bin.
if defined LLVMWIN set "PATH=!LLVMWIN!\bin;!PATH!"

rem Every link on windows is PE; an empty target means native win64.
if not defined TARGET set "TARGET=win64"

rem ── clang availability + linker flavor (GNU ld vs lld-link) ──────────
set "IS_MINGW=0"
echo !CLANG! | find /i "mingw" >nul && set "IS_MINGW=1"
where "!CLANG!" >nul 2>&1
if errorlevel 1 (
    echo [emperor] error: clang '!CLANG!' not found on PATH >&2
    exit /b 1
)

rem ─────────────────────────────────────────────────────────────────────
if "%MODE%"=="link" goto do_link
if "%MODE%"=="link-lib" goto do_link_lib

rem ── full mode: emit -> C runtime -> link ─────────────────────────────
if not defined OUT set "OUT=out"
set "OUT_ABS=%OUT%"
call :abs_path OUT_ABS
if "!EM_ARGS!"=="" (
    echo [emperor] error: no source files specified >&2
    exit /b 1
)
set "TMPD=%TEMP%\emperor_%RANDOM%%RANDOM%"
mkdir "!TMPD!" || ( echo [emperor] error: cannot create !TMPD! >&2 & exit /b 1 )

set "EMIT_OUT=!TMPD!\out"
echo !OUT_ABS! | find /i ".penguin-lib" >nul && set "EMIT_OUT=!TMPD!\out.penguin-lib"

echo [emperor] emitting: (cd !ROOT!) "!EMITTER!" !EM_ARGS! -o "!EMIT_OUT!" >&2
pushd "!ROOT!"
"!EMITTER!" !EM_ARGS! -o "!EMIT_OUT!"
set "RC=!errorlevel!"
popd
if not "!RC!"=="0" ( echo [emperor] error: emitter failed ^(kept !TMPD!^) >&2 & exit /b 1 )
if not exist "!EMIT_OUT!.ll" ( echo [emperor] error: emitter produced no !EMIT_OUT!.ll ^(kept !TMPD!^) >&2 & exit /b 1 )

echo !OUT_ABS! | find /i ".penguin-lib" >nul && goto full_link_lib
call :link_exe "!EMIT_OUT!.ll" "!OUT_ABS!"
if errorlevel 1 exit /b 1
rd /s /q "!TMPD!" >nul 2>&1
exit /b 0

:full_link_lib
call :link_lib "!EMIT_OUT!.ll" "!EMIT_OUT!.libmeta" "!OUT_ABS!"
if errorlevel 1 exit /b 1
rd /s /q "!TMPD!" >nul 2>&1
exit /b 0

:do_link
if not !POS_COUNT!==1 ( echo [emperor] error: link takes exactly one ^<file.ll^> >&2 & exit /b 1 )
if not exist "!POS1!" ( echo [emperor] error: no such file: !POS1! >&2 & exit /b 1 )
if not defined OUT ( echo [emperor] error: link needs -o ^<output^> >&2 & exit /b 1 )
set "LL=!POS1!"
call :abs_path LL
set "OUT_ABS=!OUT!"
call :abs_path OUT_ABS
call :link_exe "!LL!" "!OUT_ABS!"
exit /b !errorlevel!

:do_link_lib
if not !POS_COUNT!==2 ( echo [emperor] error: link-lib takes ^<file.ll^> ^<file.libmeta^> >&2 & exit /b 1 )
if not exist "!POS1!" ( echo [emperor] error: no such file: !POS1! >&2 & exit /b 1 )
if not exist "!POS2!" ( echo [emperor] error: no such file: !POS2! >&2 & exit /b 1 )
if not defined OUT ( echo [emperor] error: link-lib needs -o ^<output.penguin-lib^> >&2 & exit /b 1 )
set "LL=!POS1!"
call :abs_path LL
set "MF=!POS2!"
call :abs_path MF
set "OUT_ABS=!OUT!"
call :abs_path OUT_ABS
call :link_lib "!LL!" "!MF!" "!OUT_ABS!"
exit /b !errorlevel!

rem ── :link_exe <ll> <out> — uses META / LLVMWIN / LIBS / CLANG ────────
:link_exe
set "LLF=%~1"
set "OUTF=%~2"
where make >nul 2>&1
if errorlevel 1 ( echo [emperor] error: make not found on PATH ^(required to build the Penguin C runtime^) >&2 & exit /b 1 )
set "TMPD=%TEMP%\emperor_%RANDOM%%RANDOM%"
mkdir "!TMPD!" || ( echo [emperor] error: cannot create !TMPD! >&2 & exit /b 1 )
set "TMPF=!TMPD:\=/!"
rem Stable link basename: the linked output's STT_FILE symbol records the
rem input .ll's name (the old in-process pipeline always linked
rem <tmp>/combined.ll) — keep that name so identical IR links to identical
rem binaries regardless of the .ll's path.
set "LLC=!LLF!"
for %%F in ("!LLF!") do set "LLBASE=%%~nxF"
if not /i "!LLBASE!"=="combined.ll" (
    copy /y "!LLF!" "!TMPF!/combined.ll" >nul
    set "LLC=!TMPF!/combined.ll"
)
set "STDC=!ROOT!\EmperorPenguin\std\c"
set "STDC=!STDC:\=/!"
rem Explicit tool names keep the runtime makefile's mingw-triple default from
rem taking over, and CROSS=win64 steers it onto the vendored Windows-LLVM JIT
rem path (every link here is PE anyway).
set CC=clang& set CXX=clang++& set AR=llvm-ar& set "CLANG=!CLANG!"& set CROSS=win64
make --silent -C "!STDC!" OUTPUT_DIR="!TMPF!"
if errorlevel 1 ( echo [emperor] error: C runtime build failed >&2 & exit /b 1 )

set "LARGS="!LLC!" "!TMPF!/libcore_builtin.a" -Wno-override-module"
if "!META!"=="1" goto jit_meta
set "LARGS=!LARGS! "!TMPF!/meta_stubs.o""
goto jit_done
:jit_meta
if not defined LLVMWIN goto jit_stubs
if not exist "!TMPF!/libpenguin_jit.a" goto jit_stubs
if "!IS_MINGW!"=="1" (
    set "LARGS=!LARGS! "!TMPF!/libpenguin_jit.a" "!LLVMWIN!\lib\libLLVM-22.dll.a" -Wl,--export-all-symbols -lc++ -lwinpthread"
) else (
    rem Native Windows LLVM drives lld-link: bulk exports need a /DEF file
    rem (the emitter wrote the export table beside the .ll) and the C++
    rem runtime via /defaultlib.
    set "DEFF=!LLF:~0,-3!.def"
    if not exist "!DEFF!" ( echo [emperor] error: missing export table !DEFF! ^(the emitter writes it beside the .ll^) >&2 & exit /b 1 )
    set "LARGS=!LARGS! "!TMPF!/libpenguin_jit.a" "!LLVMWIN!\lib\libLLVM-22.dll.a" -Wl,/DEF:"!DEFF!" -Wl,/defaultlib:msvcprt.lib"
)
goto jit_done
:jit_stubs
if defined LLVMWIN ( echo [emperor] warning: --enable-meta requested but the C runtime make built no libpenguin_jit.a ^(Windows LLVM incomplete^); linking no-op JIT stubs >&2 ) else ( echo [emperor] warning: --enable-meta requested but no Windows LLVM ^(-llvm-win^) is available; linking no-op JIT stubs >&2 )
set "LARGS=!LARGS! "!TMPF!/meta_stubs.o""
:jit_done

rem Consumer dyn-lib closure: the emitter wrote every .so to link (incl.
rem libs resolved recursively through another lib's deps) beside the .ll as
rem <base>.libs, one path per line. Explicit --consumer-lib flags add to it.
set "LIBSFILE=!LLF:~0,-3!.libs"
if exist "!LIBSFILE!" (
    for /f "usebackq delims=" %%L in ("!LIBSFILE!") do set "LIBS=!LIBS! "%%L""
)

rem Dyn-lib consumer: link the .penguin-lib shared objects and export this
rem exe's runtime symbols so the lib's undefined refs bind.
if not "!LIBS!"=="" set "LARGS=!LARGS! !LIBS! -Wl,--export-all-symbols"

rem 32MB stack, linker-flavored (GNU ld vs lld-link /STACK:).
if "!IS_MINGW!"=="1" ( set "LARGS=!LARGS! -Wl,--stack,33554432" ) else set "LARGS=!LARGS! -Wl,/STACK:33554432"
set "LARGS=!LARGS! -o "!OUTF!""

for %%i in ("!OUTF!") do if not exist "%%~dpi" mkdir "%%~dpi"
echo [emperor] linking: !CLANG! !LARGS! >&2
"!CLANG!" !LARGS!
if errorlevel 1 ( echo [emperor] error: clang link failed >&2 & rd /s /q "!TMPD!" >nul 2>&1 & exit /b 1 )
echo [emperor] linked !OUTF! >&2
rd /s /q "!TMPD!" >nul 2>&1
exit /b 0

rem ── :link_lib <ll> <libmeta> <out> — links the .so and appends the ────
rem JSON metadata + PENGUINLIB footer (offsets measured BEFORE appending).
:link_lib
set "LLF=%~1"
set "MFF=%~2"
set "OUTF=%~3"
if not exist "!MFF!" ( echo [emperor] error: no such file: !MFF! >&2 & exit /b 1 )
for %%F in ("!MFF!") do set "MSIZE=%%~zF"
if "!MSIZE!"=="0" ( echo [emperor] error: no metadata produced ^(dynamic linking unavailable in this compiler build^): !MFF! >&2 & exit /b 1 )
rem Stable link basename (see :link_exe).
set "LIBTMP="
set "LLC=!LLF!"
for %%F in ("!LLF!") do set "LLBASE=%%~nxF"
if not /i "!LLBASE!"=="combined.ll" (
    set "LIBTMP=%TEMP%\emperor_%RANDOM%%RANDOM%"
    mkdir "!LIBTMP!" >nul 2>&1
    copy /y "!LLF!" "!LIBTMP!\combined.ll" >nul
    set "LLC=!LIBTMP:\=/!/combined.ll"
)
set "LARGS="!LLC!" -shared -Wno-override-module -Wl,--export-all-symbols -o "!OUTF!""
for %%i in ("!OUTF!") do if not exist "%%~dpi" mkdir "%%~dpi"
echo [emperor] linking lib: !CLANG! !LARGS! >&2
"!CLANG!" !LARGS!
if errorlevel 1 ( echo [emperor] error: clang -shared link failed >&2 & exit /b 1 )
for %%F in ("!OUTF!") do set "SOSIZE=%%~zF"
copy /b "!OUTF!"+"!MFF!" "!OUTF!" >nul
rem Footer without CRLF (the loader cuts the size at LF / end of file).
<nul set /p "DUMMY=PENGUINLIB:!SOSIZE!:!MSIZE!" >> "!OUTF!"
if defined LIBTMP rd /s /q "!LIBTMP!" >nul 2>&1
echo [emperor] linked !OUTF! ^(appended !MSIZE!b metadata^) >&2
exit /b 0
