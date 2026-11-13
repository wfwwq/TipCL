@echo off
REM C5a: one-shot verification -- build + VSTest + in-proc runner inside a SINGLE build-lock hold.
REM WHY: if the build and the two test runs sit in different lock holds, another agent can rebuild the
REM shared bin/ in between and every failure after that is a false alarm ("stale DLL").
REM USAGE (ASCII only, per D-08):
REM   powershell -NoProfile -ExecutionPolicy Bypass -File tools\with-build-lock.ps1 -What "tools\c5a-verify.cmd" -TimeoutSec 2400
dotnet build TipCL.Mac.sln -c Release -p:UseSharedCompilation=false -m:1 -v:q -clp:Summary
echo BUILD_EXIT=%errorlevel%
dotnet test TipCL.Mac.sln -c Release --no-build --nologo
echo VSTEST_EXIT=%errorlevel%
tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.exe
echo INPROC_EXIT=%errorlevel%
