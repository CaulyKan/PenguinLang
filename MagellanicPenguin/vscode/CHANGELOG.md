# Change Log

All notable changes to the "penguinlangvscode" extension will be documented in this file.

Check [Keep a Changelog](http://keepachangelog.com/) for recommendations on how to structure this file.

## [Unreleased]

- Initial release

## [0.0.7]

- Windows: bundle the PenguinLang-native LSP server (`server/windows/MagellanicPenguinLSP.exe`, built by `make lsp TARGET=win` via llvm-mingw cross-compilation — Win32 fiber coroutines + PeekNamedPipe stdio events in the C runtime) alongside the existing compiler; the client now shows a clear error when the server binary is missing instead of failing silently.