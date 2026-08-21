(for /r %%i in (*.cs) do @(echo ### File: %%~pnxi & echo ```csharp & type "%%i" & echo. & echo ``` & echo.)) > semua_kode_cs.md
